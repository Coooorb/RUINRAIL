using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ArmorCatalogTests
    {
        private static readonly Dictionary<string, (string name, int hp, int dr, string passive, StatModifier[] extra)> Catalog = new()
        {
            ["armor_scrap_vest"] = ("Scrap Vest", 20, 4, "patchwork", new StatModifier[0]),
            ["armor_scout_rig"] = ("Scout Rig", 12, 2, "momentum", new[] { StatModifier.Percent(StatId.MovementSpeed, 5) }),
            ["armor_riot_armor"] = ("Riot Armor", 18, 7, "anchored", new StatModifier[0]),
            ["armor_heavy_plate"] = ("Heavy Plate", 35, 9, "last_stand", new[] { StatModifier.Percent(StatId.MovementSpeed, -4) }),
            ["armor_blast_suit"] = ("Blast Suit", 22, 5, "shock_absorber", new[] { StatModifier.Percent(StatId.ExplosionDamageReduction, 30) }),
            ["armor_medic_harness"] = ("Medic Harness", 18, 3, "emergency_care", new[] { StatModifier.Percent(StatId.HealingReceived, 20) }),
            ["armor_combat_harness"] = ("Combat Harness", 16, 3, "adrenaline", new[] { StatModifier.Percent(StatId.WeaponDamage, 4) }),
            ["armor_reinforced_exo_rig"] = ("Reinforced Exo-Rig", 30, 6, "exo_lock", new[] { StatModifier.Percent(StatId.StaggerResistance, 25) }),
            ["armor_runner_suit"] = ("Runner Suit", 10, 2, "second_wind", new[] { StatModifier.Percent(StatId.MovementSpeed, 8) })
        };

        private static List<ArmorDefinition> LoadAll()
        {
            return AssetDatabase.FindAssets("t:ArmorDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ArmorDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
        }

        [Test]
        public void Catalog_ContainsExactlyNineFamilies_AndNoHazmatSuit()
        {
            var armors = LoadAll();
            CollectionAssert.AreEquivalent(Catalog.Keys, armors.Select(a => a.Id));
            Assert.AreEqual(9, armors.Count);
            Assert.IsFalse(armors.Any(a => a.Id.Contains("hazmat") || a.DisplayName.ToLowerInvariant().Contains("hazmat")));
            Assert.AreEqual(9, ArmorPassiveFactory.KnownIds.Count);
            CollectionAssert.AreEquivalent(Catalog.Values.Select(v => v.passive), ArmorPassiveFactory.KnownIds);
        }

        [Test]
        public void EveryFamily_MatchesCatalogBaseValues_PassiveId_AndArmorPool()
        {
            foreach (var armor in LoadAll())
            {
                var expected = Catalog[armor.Id];
                Assert.AreEqual(expected.name, armor.DisplayName, armor.Id);
                Assert.AreEqual(ItemCategory.Armor, armor.Category, armor.Id);
                Assert.AreEqual(expected.hp, armor.BaseMaxHealth, armor.Id);
                Assert.AreEqual(expected.dr, armor.BaseDamageReductionPercent, armor.Id);
                Assert.AreEqual(expected.passive, armor.LegendaryMechanicId, armor.Id);
                Assert.IsNotNull(armor.AffixPool, armor.Id);
                Assert.AreEqual("pool_armor", armor.AffixPool.Id, armor.Id);
                CollectionAssert.AreEquivalent(expected.extra, armor.AdditionalBaseProperties.Select(e => e.ToModifier()), armor.Id);
                var expectedBase = new List<StatModifier> { StatModifier.Flat(StatId.MaxHealth, expected.hp), StatModifier.Percent(StatId.GeneralDamageReduction, expected.dr) };
                expectedBase.AddRange(expected.extra);
                CollectionAssert.AreEquivalent(expectedBase, armor.BaseModifiers(), armor.Id);
                Assert.IsNotNull(ArmorPassiveFactory.Create(armor.LegendaryMechanicId), armor.Id);
                Assert.AreEqual(armor.LegendaryMechanicId, ArmorPassiveFactory.Create(armor.LegendaryMechanicId).Id);
            }
        }

        [Test]
        public void ArmorNeverCarriesAnRmbSpecial_AndLegendaryRollsThreeAffixes()
        {
            Assert.IsNull(typeof(ArmorDefinition).GetProperty("Special"));
            Assert.IsNull(typeof(ArmorDefinition).GetProperty("RmbSpecial"));
            Assert.IsFalse(typeof(ArmorDefinition).GetProperties().Any(p => p.Name.ToLowerInvariant().Contains("special")));
            Assert.AreEqual(3, RarityRules.RandomAffixCount(Rarity.Legendary));

            var heavy = LoadAll().Single(a => a.Id == "armor_heavy_plate");
            var instance = new ItemInstance(heavy.Id);
            var result = new AffixRollService().Roll(instance, heavy, Rarity.Legendary, new RuinRail.Core.Rng.SeededRandom(5));
            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreEqual(3, instance.AffixRolls.Count);
            Assert.IsTrue(instance.AffixRolls.All(r => heavy.AffixPool.Contains(r.AffixId)));
            Assert.AreEqual("last_stand", AffixRollService.GetLegendaryMechanicId(instance, heavy));
            Assert.IsNull(AffixRollService.GetLegendaryMechanicId(new ItemInstance(heavy.Id, 1, Rarity.Epic), heavy), "Epic armor has no passive.");
        }

        [Test]
        public void FamilyModifiers_RespectGlobalCaps_AndExplosionStaysSeparate()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            var stats = new PlayerStats(caps, 100);
            var blast = LoadAll().Single(a => a.Id == "armor_blast_suit");
            var instance = new ItemInstance(blast.Id);
            stats.SetSource(new EquippedItemStatSource(instance, blast, null));
            stats.SetSource(new StatModifierSource("skills", StatModifier.Percent(StatId.GeneralDamageReduction, 60)));

            Assert.AreEqual(122, stats.MaxHealth);
            Assert.AreEqual(40, stats.GetPercent(StatId.GeneralDamageReduction), "5 + 60 clamps at 40.");
            Assert.AreEqual(60, stats.ApplyDamageReduction(100, isExplosion: false));
            Assert.AreEqual(42, stats.ApplyDamageReduction(100, isExplosion: true), "Blast Suit 30% applies after general DR: 60 → 42.");

            var runner = LoadAll().Single(a => a.Id == "armor_runner_suit");
            stats.ClearSources();
            stats.SetSource(new EquippedItemStatSource(new ItemInstance(runner.Id), runner, null));
            stats.SetSource(new StatModifierSource("skills", StatModifier.Percent(StatId.MovementSpeed, 25)));
            Assert.AreEqual(30, stats.GetPercent(StatId.MovementSpeed), "8 + 25 clamps at the +30% cap.");

            var heavy = LoadAll().Single(a => a.Id == "armor_heavy_plate");
            stats.ClearSources();
            stats.SetSource(new EquippedItemStatSource(new ItemInstance(heavy.Id), heavy, null));
            Assert.AreEqual(-4, stats.GetPercent(StatId.MovementSpeed), "Authored trade-off penalty passes through.");
            Assert.AreEqual(0.96f, stats.GetMultiplier(StatId.MovementSpeed), 0.0001f);
            Object.DestroyImmediate(caps);
        }
    }
}
