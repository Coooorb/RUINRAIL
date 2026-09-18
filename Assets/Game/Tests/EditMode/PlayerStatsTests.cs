using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class PlayerStatsTests
    {
        private GlobalStatCapsConfig _caps;
        private PlayerStats _stats;

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _stats = new PlayerStats(_caps, 100);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_caps);
        }

        private static readonly (StatId stat, int cap)[] ApprovedCaps =
        {
            (StatId.MovementSpeed, 30),
            (StatId.GeneralDamageReduction, 40),
            (StatId.WeaponDamage, 50),
            (StatId.ReloadSpeed, 40),
            (StatId.WeaponSwitchSpeed, 50),
            (StatId.DashCooldownReduction, 35),
            (StatId.DashDistance, 30),
            (StatId.MeleeAttackSpeed, 30),
            (StatId.ProjectileRange, 40),
            (StatId.ProjectileSpeed, 40),
            (StatId.HealingReceived, 50),
            (StatId.BlasterCoolingRate, 50),
            (StatId.BlasterHeatPerShotReduction, 30),
            (StatId.BowChargeSpeed, 35),
            (StatId.KnockbackResistance, 50),
            (StatId.StaggerResistance, 50)
        };

        // ---- Acceptance 1: exact caps under stacked modifiers ----

        [Test]
        public void EveryCappedStat_ClampsAtItsExactApprovedCap_UnderStackedSources()
        {
            foreach (var (stat, cap) in ApprovedCaps)
            {
                _stats.ClearSources();
                _stats.SetSource(new StatModifierSource("skill", StatModifier.Percent(stat, cap - 5)));
                Assert.AreEqual(cap - 5, _stats.GetPercent(stat), $"{stat} below cap is untouched");
                _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(stat, 20)));
                _stats.SetSource(new StatModifierSource("affix", StatModifier.Percent(stat, 25)));
                _stats.SetSource(new StatModifierSource("buff", StatModifier.Percent(stat, 99)));
                Assert.AreEqual(cap, _stats.GetPercent(stat), $"{stat} must clamp at {cap}");
                Assert.AreEqual(cap, _stats.CapFor(stat));
            }
        }

        [Test]
        public void GeneralDamageReduction_Caps40Normally_And50WhileArmorInjectorActive()
        {
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.GeneralDamageReduction, 35)));
            _stats.SetSource(new StatModifierSource("skills", StatModifier.Percent(StatId.GeneralDamageReduction, 30)));
            Assert.AreEqual(40, _stats.GetPercent(StatId.GeneralDamageReduction));
            Assert.AreEqual(60, _stats.ApplyDamageReduction(100));

            _stats.ArmorInjectorActive = true;
            Assert.AreEqual(50, _stats.GetPercent(StatId.GeneralDamageReduction));
            Assert.AreEqual(50, _stats.ApplyDamageReduction(100));

            _stats.ArmorInjectorActive = false;
            Assert.AreEqual(40, _stats.GetPercent(StatId.GeneralDamageReduction));
        }

        // ---- Requirement 4: specialized explosion reduction separate from general DR ----

        [Test]
        public void ExplosionReduction_AppliesMultiplicativelyAfterGeneralDr_AndIsNotCappedWithIt()
        {
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.GeneralDamageReduction, 40), StatModifier.Percent(StatId.ExplosionDamageReduction, 30)));
            _stats.SetSource(new StatModifierSource("extra", StatModifier.Percent(StatId.GeneralDamageReduction, 20)));

            Assert.AreEqual(40, _stats.GetPercent(StatId.GeneralDamageReduction), "General DR still capped at 40.");
            Assert.AreEqual(60, _stats.ApplyDamageReduction(100, isExplosion: false));
            Assert.AreEqual(42, _stats.ApplyDamageReduction(100, isExplosion: true), "16_GLOBAL_STAT_CAPS worked example: 100 → 60 → 42.");
            Assert.AreEqual(0, _stats.GetPercent(StatId.ExplosionDamageReduction) - 30, "Explosion reduction is its own stat, uncapped by general DR.");
            Assert.AreEqual(0, _stats.ApplyDamageReduction(0));
        }

        // ---- Acceptance 2: removal recomputes ----

        [Test]
        public void RemovingOneSource_RecomputesDeterministically()
        {
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.MovementSpeed, 10), StatModifier.Flat(StatId.MaxHealth, 20)));
            _stats.SetSource(new StatModifierSource("accessory", StatModifier.Percent(StatId.MovementSpeed, 15)));
            _stats.SetSource(new StatModifierSource("affix", StatModifier.Percent(StatId.MovementSpeed, 12)));
            Assert.AreEqual(30, _stats.GetPercent(StatId.MovementSpeed), "37 clamps to 30.");
            Assert.AreEqual(120, _stats.MaxHealth);

            Assert.IsTrue(_stats.RemoveSource("accessory"));
            Assert.AreEqual(22, _stats.GetPercent(StatId.MovementSpeed));
            Assert.IsTrue(_stats.RemoveSource("armor"));
            Assert.AreEqual(12, _stats.GetPercent(StatId.MovementSpeed));
            Assert.AreEqual(100, _stats.MaxHealth);
            Assert.IsFalse(_stats.RemoveSource("armor"), "Removing twice is a no-op.");
            Assert.AreEqual(12, _stats.GetPercent(StatId.MovementSpeed));

            _stats.SetSource(new StatModifierSource("affix", StatModifier.Percent(StatId.MovementSpeed, 5)));
            Assert.AreEqual(5, _stats.GetPercent(StatId.MovementSpeed), "Same id replaces, never stacks.");
        }

        // ---- Acceptance 3: order independence ----

        [Test]
        public void InsertionOrder_DoesNotChangeAnyResult()
        {
            var sources = new IStatModifierSource[]
            {
                new StatModifierSource("a", StatModifier.Percent(StatId.WeaponDamage, 20), StatModifier.Flat(StatId.MaxHealth, 15)),
                new StatModifierSource("b", StatModifier.Percent(StatId.WeaponDamage, 25), StatModifier.Percent(StatId.MaxHealth, 10)),
                new StatModifierSource("c", StatModifier.Percent(StatId.WeaponDamage, 30), StatModifier.Percent(StatId.ReloadSpeed, 14)),
                new StatModifierSource("d", StatModifier.Percent(StatId.ReloadSpeed, 40), StatModifier.Flat(StatId.MaxHealth, 5))
            };

            string Signature(IEnumerable<IStatModifierSource> order)
            {
                var stats = new PlayerStats(_caps, 100);
                foreach (var s in order) stats.SetSource(s);
                return string.Join("|", Enum.GetValues(typeof(StatId)).Cast<StatId>().Select(id => $"{id}={stats.GetPercent(id)}/{stats.GetFlat(id)}")) + $"|hp={stats.MaxHealth}";
            }

            var reference = Signature(sources);
            foreach (var permutation in Permutations(sources.ToList()))
            {
                Assert.AreEqual(reference, Signature(permutation));
            }

            var probe = new PlayerStats(_caps, 100);
            foreach (var s in sources) probe.SetSource(s);
            Assert.AreEqual(50, probe.GetPercent(StatId.WeaponDamage));
            Assert.AreEqual(40, probe.GetPercent(StatId.ReloadSpeed));
            Assert.AreEqual(132, probe.MaxHealth, "(100 + 20 flat) × 1.10");
        }

        private static IEnumerable<List<T>> Permutations<T>(List<T> items)
        {
            if (items.Count <= 1)
            {
                yield return items;
                yield break;
            }

            for (var i = 0; i < items.Count; i++)
            {
                var rest = items.Where((_, index) => index != i).ToList();
                foreach (var p in Permutations(rest))
                {
                    var result = new List<T> { items[i] };
                    result.AddRange(p);
                    yield return result;
                }
            }
        }

        // ---- Acceptance 4: baseline ----

        [Test]
        public void NoSources_YieldsBaselineValues()
        {
            foreach (StatId stat in Enum.GetValues(typeof(StatId)))
            {
                Assert.AreEqual(0, _stats.GetPercent(stat));
                Assert.AreEqual(0, _stats.GetFlat(stat));
                Assert.AreEqual(1f, _stats.GetMultiplier(stat), 0.0001f);
                Assert.AreEqual(1f, _stats.GetReductionFactor(stat), 0.0001f);
            }

            Assert.AreEqual(100, _stats.MaxHealth);
            Assert.AreEqual(37, _stats.ApplyDamageReduction(37));
            Assert.AreEqual(100, new PlayerStats(null, 100).MaxHealth, "No caps config → uncapped but still baseline-correct.");
        }

        [Test]
        public void Multipliers_DeriveFromCappedPercents()
        {
            _stats.SetSource(new StatModifierSource("x", StatModifier.Percent(StatId.DashCooldownReduction, 50), StatModifier.Percent(StatId.MovementSpeed, 30)));
            Assert.AreEqual(0.65f, _stats.GetReductionFactor(StatId.DashCooldownReduction), 0.0001f, "35% cap → ×0.65");
            Assert.AreEqual(1.3f, _stats.GetMultiplier(StatId.MovementSpeed), 0.0001f);
        }

        // ---- Affix bridge + authored caps asset ----

        [Test]
        public void EquipmentAffixSource_FeedsPersistedRollsThroughStableStatIds()
        {
            var damage = ScriptableObject.CreateInstance<AffixDefinition>();
            typeof(AffixDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(damage, "affix_damage");
            typeof(AffixDefinition).GetField("_stat", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(damage, AffixStat.Damage);
            var reload = ScriptableObject.CreateInstance<AffixDefinition>();
            typeof(AffixDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(reload, "affix_reload_speed");
            typeof(AffixDefinition).GetField("_stat", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(reload, AffixStat.ReloadSpeed);
            var lookup = new Dictionary<string, AffixDefinition> { ["affix_damage"] = damage, ["affix_reload_speed"] = reload };

            var pistol = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare);
            pistol.AddAffixRoll(new AffixRoll("affix_damage", 9));
            pistol.AddAffixRoll(new AffixRoll("affix_reload_speed", 12));
            var second = new ItemInstance("weapon_ar_17", 1, Rarity.Epic);
            second.AddAffixRoll(new AffixRoll("affix_damage", 45));

            _stats.SetSource(new EquipmentAffixSource(pistol, id => lookup.TryGetValue(id, out var d) ? d : null));
            _stats.SetSource(new EquipmentAffixSource(second, id => lookup.TryGetValue(id, out var d) ? d : null));

            Assert.AreEqual(50, _stats.GetPercent(StatId.WeaponDamage), "9 + 45 = 54 clamps to 50.");
            Assert.AreEqual(12, _stats.GetPercent(StatId.ReloadSpeed));
            Assert.IsTrue(_stats.RemoveSource($"affixes:{second.InstanceId}"));
            Assert.AreEqual(9, _stats.GetPercent(StatId.WeaponDamage));

            foreach (AffixStat affixStat in Enum.GetValues(typeof(AffixStat)))
            {
                Assert.DoesNotThrow(() => StatIds.FromAffix(affixStat));
            }

            UnityEngine.Object.DestroyImmediate(damage);
            UnityEngine.Object.DestroyImmediate(reload);
        }

        [Test]
        public void AuthoredCapsAsset_MatchesApprovedTable_ForStatIds()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            Assert.IsNotNull(asset);
            foreach (var (stat, cap) in ApprovedCaps)
            {
                Assert.AreEqual(cap, asset.GetCapPercent(stat), stat.ToString());
            }

            Assert.AreEqual(50, asset.GetCapPercent(StatId.GeneralDamageReduction, armorInjectorActive: true));
            Assert.AreEqual(0, asset.GetCapPercent(StatId.FireRate), "Uncapped stats report 0.");
        }
    }
}
