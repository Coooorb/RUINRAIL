using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class RarityAffixTests
    {
        private readonly List<UnityEngine.Object> _created = new();
        private AffixDefinition _damage, _fireRate, _reload, _magazine, _heat, _cooling, _charge;
        private AffixPool _rangedPool, _blasterPool, _bowPool;
        private EquipmentItemDefinition _rifle, _blaster, _bow;
        private AffixRollService _service;

        [SetUp]
        public void SetUp()
        {
            _damage = Affix("affix_damage", AffixStat.Damage, 6, 10);
            _fireRate = Affix("affix_fire_rate", AffixStat.FireRate, 5, 9);
            _reload = Affix("affix_reload_speed", AffixStat.ReloadSpeed, 8, 14);
            _magazine = Affix("affix_magazine_size", AffixStat.MagazineSize, 10, 20);
            _heat = Affix("affix_heat_per_shot", AffixStat.BlasterHeatPerShot, 5, 10);
            _cooling = Affix("affix_cooling_rate", AffixStat.BlasterCoolingRate, 5, 10);
            _charge = Affix("affix_charge_speed", AffixStat.BowChargeSpeed, 5, 10);

            _rangedPool = Pool("pool_ranged", _damage, _fireRate, _reload, _magazine);
            _blasterPool = Pool("pool_blaster", _damage, _heat, _cooling);
            _bowPool = Pool("pool_bow", _damage, _charge, _reload);

            _rifle = Equipment("weapon_test_rifle", ItemCategory.Weapon, _rangedPool, "legendary_rifle_special");
            _blaster = Equipment("weapon_test_blaster", ItemCategory.Weapon, _blasterPool, "legendary_blaster_special");
            _bow = Equipment("weapon_test_bow", ItemCategory.Weapon, _bowPool, null);
            _service = new AffixRollService();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private AffixDefinition Affix(string id, AffixStat stat, int min, int max)
        {
            var a = ScriptableObject.CreateInstance<AffixDefinition>();
            a.name = id;
            Set(typeof(AffixDefinition), a, "_id", id);
            Set(typeof(AffixDefinition), a, "_stat", stat);
            Set(typeof(AffixDefinition), a, "_minValue", min);
            Set(typeof(AffixDefinition), a, "_maxValue", max);
            _created.Add(a);
            return a;
        }

        private AffixPool Pool(string id, params AffixDefinition[] affixes)
        {
            var p = ScriptableObject.CreateInstance<AffixPool>();
            p.name = id;
            Set(typeof(AffixPool), p, "_id", id);
            Set(typeof(AffixPool), p, "_affixes", affixes);
            _created.Add(p);
            return p;
        }

        private EquipmentItemDefinition Equipment(string id, ItemCategory category, AffixPool pool, string legendaryMechanic)
        {
            var d = ScriptableObject.CreateInstance<EquipmentItemDefinition>();
            d.name = id;
            Set(typeof(ItemDefinition), d, "_id", id);
            Set(typeof(ItemDefinition), d, "_category", category);
            Set(typeof(EquipmentItemDefinition), d, "_affixPool", pool);
            Set(typeof(EquipmentItemDefinition), d, "_legendaryMechanicId", legendaryMechanic);
            _created.Add(d);
            return d;
        }

        private AffixDefinition Resolve(string id) =>
            new[] { _damage, _fireRate, _reload, _magazine, _heat, _cooling, _charge }.FirstOrDefault(a => a.Id == id);

        // ---- Acceptance 1: counts match rarity rules ----

        [TestCase(Rarity.Common, 0)]
        [TestCase(Rarity.Uncommon, 1)]
        [TestCase(Rarity.Rare, 2)]
        [TestCase(Rarity.Epic, 3)]
        [TestCase(Rarity.Legendary, 3)]
        public void Roll_ProducesExactAffixCountForRarity(Rarity rarity, int expectedCount)
        {
            Assert.AreEqual(expectedCount, RarityRules.RandomAffixCount(rarity));

            var instance = new ItemInstance(_rifle.Id);
            var result = _service.Roll(instance, _rifle, rarity, new SeededRandom(1));

            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreEqual(expectedCount, result.AffixCount);
            Assert.AreEqual(expectedCount, instance.AffixRolls.Count);
            Assert.AreEqual(rarity, instance.Rarity);
            Assert.AreEqual(expectedCount, instance.AffixRolls.Select(r => r.AffixId).Distinct().Count(), "Affixes must be distinct.");
        }

        [Test]
        public void Legendary_HasThreeRandomAffixes_PlusFixedMechanic_EpicHasNoMechanic()
        {
            var legendary = new ItemInstance(_rifle.Id);
            var epic = new ItemInstance(_rifle.Id);
            _service.Roll(legendary, _rifle, Rarity.Legendary, new SeededRandom(3));
            _service.Roll(epic, _rifle, Rarity.Epic, new SeededRandom(3));

            Assert.AreEqual(3, legendary.AffixRolls.Count);
            Assert.AreEqual("legendary_rifle_special", AffixRollService.GetLegendaryMechanicId(legendary, _rifle));
            Assert.IsNull(AffixRollService.GetLegendaryMechanicId(epic, _rifle));
            Assert.IsNull(AffixRollService.GetLegendaryMechanicId(new ItemInstance(_bow.Id) { Rarity = Rarity.Legendary }, _bow),
                "Definition without a configured mechanic grants none.");
        }

        // ---- Acceptance 2: integer values inside inclusive ranges ----

        [Test]
        public void Roll_ValuesAreWholeIntegersWithinInclusiveRange_AndReachBothEnds()
        {
            var seen = new Dictionary<string, HashSet<int>>();
            for (var seed = 0; seed < 400; seed++)
            {
                var instance = new ItemInstance(_rifle.Id);
                Assert.IsTrue(_service.Roll(instance, _rifle, Rarity.Epic, new SeededRandom(seed)).Success);
                foreach (var roll in instance.AffixRolls)
                {
                    var def = Resolve(roll.AffixId);
                    Assert.IsNotNull(def);
                    Assert.GreaterOrEqual(roll.Value, def.MinValue);
                    Assert.LessOrEqual(roll.Value, def.MaxValue);
                    if (!seen.TryGetValue(roll.AffixId, out var set)) seen[roll.AffixId] = set = new HashSet<int>();
                    set.Add(roll.Value);
                }
            }

            Assert.IsTrue(seen["affix_damage"].Contains(6) && seen["affix_damage"].Contains(10), "Damage must be able to roll both 6 and 10.");
            CollectionAssert.IsSubsetOf(seen["affix_damage"], new[] { 6, 7, 8, 9, 10 });
            Assert.AreEqual(typeof(int), typeof(AffixRoll).GetField("Value").FieldType, "Affix values are integer-only.");
        }

        [Test]
        public void AffixDefinition_ClampsToPositiveIntegerRanges()
        {
            var bad = Affix("affix_bad", AffixStat.Damage, -5, -2);
            Assert.AreEqual(1, bad.MinValue);
            Assert.AreEqual(1, bad.MaxValue);

            var inverted = Affix("affix_inverted", AffixStat.Damage, 10, 4);
            Assert.AreEqual(10, inverted.MaxValue, "Max is never below Min.");
        }

        // ---- Acceptance 3: invalid class/slot affixes are never rolled ----

        [Test]
        public void Roll_OnlyDrawsFromTheItemsOwnPool()
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var rifle = new ItemInstance(_rifle.Id);
                var blaster = new ItemInstance(_blaster.Id);
                var bow = new ItemInstance(_bow.Id);
                _service.Roll(rifle, _rifle, Rarity.Epic, new SeededRandom(seed));
                _service.Roll(blaster, _blaster, Rarity.Epic, new SeededRandom(seed));
                _service.Roll(bow, _bow, Rarity.Epic, new SeededRandom(seed));

                Assert.IsTrue(rifle.AffixRolls.All(r => _rangedPool.Contains(r.AffixId)));
                Assert.IsFalse(rifle.AffixRolls.Any(r => r.AffixId is "affix_heat_per_shot" or "affix_cooling_rate" or "affix_charge_speed"));
                Assert.IsTrue(blaster.AffixRolls.All(r => _blasterPool.Contains(r.AffixId)));
                Assert.IsFalse(blaster.AffixRolls.Any(r => r.AffixId == "affix_charge_speed"));
                Assert.IsTrue(bow.AffixRolls.All(r => _bowPool.Contains(r.AffixId)));
                Assert.IsFalse(bow.AffixRolls.Any(r => r.AffixId is "affix_heat_per_shot" or "affix_cooling_rate"));
            }
        }

        [Test]
        public void Roll_RejectsConsumablesAndAmmo_WithoutAffixes()
        {
            var consumable = ScriptableObject.CreateInstance<EquipmentItemDefinition>();
            _created.Add(consumable);
            Set(typeof(ItemDefinition), consumable, "_id", "consumable_test");
            Set(typeof(ItemDefinition), consumable, "_category", ItemCategory.Consumable);
            Set(typeof(EquipmentItemDefinition), consumable, "_affixPool", _rangedPool);

            var instance = new ItemInstance(consumable.Id);
            var result = _service.Roll(instance, consumable, Rarity.Epic, new SeededRandom(1));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(AffixRollError.NotEquipment, result.Error);
            Assert.AreEqual(0, instance.AffixRolls.Count);
            Assert.AreEqual(Rarity.Common, instance.Rarity);
        }

        [Test]
        public void Roll_FailsWithDiagnostic_WhenPoolMissingOrTooSmall_LeavingInstanceUntouched()
        {
            var noPool = Equipment("armor_no_pool", ItemCategory.Armor, null, null);
            var tinyPool = Equipment("armor_tiny_pool", ItemCategory.Armor, Pool("pool_tiny", _damage), null);

            var a = new ItemInstance(noPool.Id);
            var b = new ItemInstance(tinyPool.Id);

            Assert.AreEqual(AffixRollError.MissingPool, _service.Roll(a, noPool, Rarity.Rare, new SeededRandom(1)).Error);
            Assert.AreEqual(AffixRollError.PoolTooSmall, _service.Roll(b, tinyPool, Rarity.Rare, new SeededRandom(1)).Error);
            Assert.AreEqual(0, a.AffixRolls.Count);
            Assert.AreEqual(0, b.AffixRolls.Count);
            Assert.AreEqual(Rarity.Common, a.Rarity);
            Assert.AreEqual(AffixRollError.InvalidRequest, _service.Roll(a, _rifle, Rarity.Rare, new SeededRandom(1)).Error, "Definition/instance mismatch is rejected.");
        }

        // ---- Acceptance 4: rolls persist through DTO round-trip and transfer ----

        [Test]
        public void RolledAffixes_SurviveSnapshotRoundTrip_AndTransfer_Unchanged()
        {
            var instance = new ItemInstance(_rifle.Id);
            _service.Roll(instance, _rifle, Rarity.Legendary, new SeededRandom(77));
            var original = instance.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray();

            var json = JsonUtility.ToJson(instance.ToSnapshot());
            var restored = ItemInstance.FromSnapshot(JsonUtility.FromJson<ItemInstanceSnapshot>(json));
            CollectionAssert.AreEqual(original, restored.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray());
            Assert.AreEqual(Rarity.Legendary, restored.Rarity);
            Assert.AreEqual(instance.InstanceId, restored.InstanceId);

            var ground = new ListItemContainer("ground");
            var other = new ListItemContainer("stash");
            ground.TryAdd(restored);
            Assert.IsTrue(new ItemTransferService().Transfer(ground, restored.InstanceId, other).Success);
            CollectionAssert.AreEqual(original, other.Find(restored.InstanceId).AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray());

            Assert.AreEqual(AffixRollError.AlreadyRolled, _service.Roll(restored, _rifle, Rarity.Legendary, new SeededRandom(1)).Error,
                "A loaded instance is never re-rolled.");
        }

        [Test]
        public void Roll_IsDeterministicForSameSeed_AndDiffersAcrossSeeds()
        {
            var a = new ItemInstance(_rifle.Id);
            var b = new ItemInstance(_rifle.Id);
            var c = new ItemInstance(_rifle.Id);
            _service.Roll(a, _rifle, Rarity.Epic, RngStreams.Derive(1000, 2, RngStream.Loot));
            _service.Roll(b, _rifle, Rarity.Epic, RngStreams.Derive(1000, 2, RngStream.Loot));
            _service.Roll(c, _rifle, Rarity.Epic, RngStreams.Derive(1001, 2, RngStream.Loot));

            CollectionAssert.AreEqual(a.AffixRolls.Select(r => (r.AffixId, r.Value)), b.AffixRolls.Select(r => (r.AffixId, r.Value)));
            var differs = Enumerable.Range(0, 3).Any(i => a.AffixRolls[i].AffixId != c.AffixRolls[i].AffixId || a.AffixRolls[i].Value != c.AffixRolls[i].Value);
            Assert.IsTrue(differs);
        }

        // ---- Acceptance 5: no negative random affix, crit or weak-spot stat ----

        [Test]
        public void AffixStat_ContainsNoCritOrWeakSpot_AndRolledValuesAreAlwaysPositive()
        {
            foreach (var name in Enum.GetNames(typeof(AffixStat)))
            {
                StringAssert.DoesNotContain("crit", name.ToLowerInvariant());
                StringAssert.DoesNotContain("weak", name.ToLowerInvariant());
            }

            for (var seed = 0; seed < 100; seed++)
            {
                var instance = new ItemInstance(_blaster.Id);
                _service.Roll(instance, _blaster, Rarity.Legendary, new SeededRandom(seed));
                Assert.IsTrue(instance.AffixRolls.All(r => r.Value > 0));
            }
        }

        // ---- Application boundary: summed per stat, capped once ----

        [Test]
        public void Aggregator_SumsPerStat_AndCapsCombinedTotalOnce()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var rolls = new[]
            {
                new AffixRoll("affix_damage", 10),
                new AffixRoll("affix_damage", 30),
                new AffixRoll("affix_damage", 25),
                new AffixRoll("affix_fire_rate", 9)
            };

            Assert.AreEqual(65, AffixStatAggregator.TotalPercent(rolls, Resolve, AffixStat.Damage));
            Assert.AreEqual(50, AffixStatAggregator.CappedTotalPercent(rolls, Resolve, AffixStat.Damage, caps), "Weapon Damage cap is +50%.");
            Assert.AreEqual(9, AffixStatAggregator.CappedTotalPercent(rolls, Resolve, AffixStat.FireRate, caps));
            Assert.AreEqual(0, AffixStatAggregator.CappedTotalPercent(rolls, Resolve, AffixStat.ReloadSpeed, caps));
            Assert.AreEqual(40, caps.GetCapPercent(AffixStat.ReloadSpeed));
            Assert.AreEqual(35, caps.GetCapPercent(AffixStat.BowChargeSpeed));
            Assert.AreEqual(30, caps.GetCapPercent(AffixStat.BlasterHeatPerShot));
            Assert.AreEqual(50, caps.GeneralDamageReductionWithArmorInjector);
        }

        // ---- Authored assets ----

        [Test]
        public void AuthoredAffixAssets_MatchApprovedExampleRanges_AndRangedPoolLinksThem()
        {
            var expected = new Dictionary<string, (AffixStat stat, int min, int max)>
            {
                ["affix_damage"] = (AffixStat.Damage, 6, 10),
                ["affix_fire_rate"] = (AffixStat.FireRate, 5, 9),
                ["affix_reload_speed"] = (AffixStat.ReloadSpeed, 8, 14),
                ["affix_magazine_size"] = (AffixStat.MagazineSize, 10, 20),
                ["affix_projectile_speed"] = (AffixStat.ProjectileSpeed, 8, 15),
                ["affix_range"] = (AffixStat.Range, 8, 15),
                ["affix_knockback"] = (AffixStat.Knockback, 10, 20),
                ["affix_stagger_power"] = (AffixStat.StaggerPower, 8, 16)
            };

            var assets = AssetDatabase.FindAssets("t:AffixDefinition", new[] { "Assets/Game/ScriptableObjects/Affixes" })
                .Select(g => AssetDatabase.LoadAssetAtPath<AffixDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .ToDictionary(a => a.Id);
            Assert.GreaterOrEqual(assets.Count, expected.Count, "The ranged affixes are a subset of all authored affixes (armor pool added in TASK 032).");
            Assert.AreEqual(assets.Count, assets.Keys.Distinct().Count(), "Affix ids are unique project-wide.");
            foreach (var kv in expected)
            {
                Assert.IsTrue(assets.ContainsKey(kv.Key), kv.Key);
                Assert.AreEqual(kv.Value.stat, assets[kv.Key].Stat, kv.Key);
                Assert.AreEqual(kv.Value.min, assets[kv.Key].MinValue, kv.Key);
                Assert.AreEqual(kv.Value.max, assets[kv.Key].MaxValue, kv.Key);
            }

            var pool = AssetDatabase.LoadAssetAtPath<AffixPool>("Assets/Game/ScriptableObjects/Affixes/AffixPool_Ranged.asset");
            Assert.IsNotNull(pool);
            Assert.AreEqual("pool_ranged", pool.Id);
            CollectionAssert.AreEquivalent(expected.Keys, pool.Affixes.Select(a => a.Id));

            var caps = AssetDatabase.LoadAssetAtPath<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            Assert.IsNotNull(caps);
            Assert.AreEqual(50, caps.GetCapPercent(AffixStat.Damage));
            Assert.AreEqual(30, caps.GetCapPercent(AffixStat.MovementSpeed));
        }
    }
}
