using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class LootRollerTests
    {
        private const string RarityTablePath = "Assets/Game/ScriptableObjects/Loot/RarityTable_Standard.asset";
        private const string SupplyTablePath = "Assets/Game/ScriptableObjects/Loot/LootTable_SupplyChest.asset";
        private readonly List<Object> _created = new();
        private RarityTableDefinition _rarity;
        private LootRoller _roller;
        private AffixPool _pool;
        private EquipmentItemDefinition _rifle;
        private EquipmentItemDefinition _legendaryRifle;
        private AmmoItemDefinition _ammo;

        [SetUp]
        public void SetUp()
        {
            _rarity = AssetDatabase.LoadAssetAtPath<RarityTableDefinition>(RarityTablePath);
            _roller = new LootRoller(_ => _rarity);

            var affixes = new[] { Affix("a1", 6, 10), Affix("a2", 5, 9), Affix("a3", 8, 14), Affix("a4", 10, 20) };
            _pool = ScriptableObject.CreateInstance<AffixPool>();
            _created.Add(_pool);
            Set(_pool, "_id", "pool_test");
            Set(_pool, "_affixes", affixes);
            _rifle = Equipment("weapon_test_rifle", "");
            _legendaryRifle = Equipment("weapon_test_rifle_legendary", "legendary_special");
            _ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            _created.Add(_ammo);
            Set(_ammo, "_id", "ammo_light");
            Set(_ammo, "_category", ItemCategory.Ammo);
            Set(_ammo, "_isStackable", true);
            Set(_ammo, "_maxStack", 180);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            info.SetValue(target, value);
        }

        private AffixDefinition Affix(string id, int min, int max)
        {
            var a = ScriptableObject.CreateInstance<AffixDefinition>();
            _created.Add(a);
            Set(a, "_id", id);
            Set(a, "_minValue", min);
            Set(a, "_maxValue", max);
            return a;
        }

        private EquipmentItemDefinition Equipment(string id, string legendary)
        {
            var d = ScriptableObject.CreateInstance<EquipmentItemDefinition>();
            _created.Add(d);
            Set(d, "_id", id);
            Set(d, "_category", ItemCategory.Weapon);
            Set(d, "_affixPool", _pool);
            Set(d, "_legendaryMechanicId", legendary);
            return d;
        }

        private LootTableDefinition Table(params LootTableDefinition.Roll[] rolls)
        {
            var t = ScriptableObject.CreateInstance<LootTableDefinition>();
            _created.Add(t);
            Set(t, "_id", "loot_test");
            Set(t, "_rolls", rolls);
            return t;
        }

        private static LootTableDefinition.Roll Roll(string label, int chance, params LootTableDefinition.Entry[] entries)
        {
            return new LootTableDefinition.Roll { Label = label, ChancePercent = chance, Entries = entries };
        }

        private static LootTableDefinition.Entry Entry(ItemDefinition item, int weight = 1, int min = 1, int max = 1, EquipmentItemDefinition legendary = null)
        {
            return new LootTableDefinition.Entry { Item = item, Weight = weight, MinQuantity = min, MaxQuantity = max, LegendaryVariant = legendary };
        }

        [Test]
        public void RarityTable_MatchesApprovedDepthBands_AndInterpolatesBetween()
        {
            Assert.IsNotNull(_rarity);
            Assert.AreEqual(LootQuality.Standard, _rarity.Quality);
            CollectionAssert.AreEqual(new[] { 600, 310, 80, 9, 1 }, _rarity.WeightsAt(1));
            CollectionAssert.AreEqual(new[] { 450, 360, 160, 27, 3 }, _rarity.WeightsAt(5));
            CollectionAssert.AreEqual(new[] { 300, 380, 250, 64, 6 }, _rarity.WeightsAt(10));
            CollectionAssert.AreEqual(new[] { 180, 320, 340, 150, 10 }, _rarity.WeightsAt(20));
            CollectionAssert.AreEqual(new[] { 120, 260, 390, 215, 15 }, _rarity.WeightsAt(30));
            CollectionAssert.AreEqual(_rarity.WeightsAt(30), _rarity.WeightsAt(100), "30+ holds.");
            var d3 = _rarity.WeightsAt(3);
            Assert.Less(d3[0], 600);
            Assert.Greater(d3[0], 450);
            Assert.AreEqual(1000, _rarity.WeightsAt(1).Sum());
            Assert.AreEqual(1000, _rarity.WeightsAt(20).Sum());
        }

        [Test]
        public void RollRarity_FollowsTheDepthCurve_Statistically()
        {
            int CountRare(int depth)
            {
                var ctx = new LootContext(depth, LootQuality.Standard, new SeededRandom(depth * 101));
                var rare = 0;
                for (var i = 0; i < 5000; i++) if (_roller.RollRarity(ctx) >= Rarity.Rare) rare++;
                return rare;
            }

            var shallow = CountRare(1);
            var deep = CountRare(30);
            Assert.That(shallow, Is.InRange(300, 600), "~9% Rare+ at depth 1");
            Assert.That(deep, Is.InRange(2800, 3400), "~62% Rare+ at depth 30");
        }

        [Test]
        public void Roll_IsDeterministicForSameContext_AndDiffersPerSourceIndex()
        {
            var table = Table(
                Roll("coins", 100, Entry(null, 1, 10, 25)),
                Roll("ammo", 100, Entry(_ammo, 1, 20, 40)),
                Roll("gear", 50, Entry(_rifle)));

            var a = _roller.Roll(table, LootContext.ForSource(77, 3, 0, LootQuality.Standard));
            var b = _roller.Roll(table, LootContext.ForSource(77, 3, 0, LootQuality.Standard));
            var other = _roller.Roll(table, LootContext.ForSource(77, 3, 1, LootQuality.Standard));

            Assert.AreEqual(a.Coins, b.Coins);
            CollectionAssert.AreEqual(a.Items.Select(Describe), b.Items.Select(Describe));
            var anyDifferent = a.Coins != other.Coins || !a.Items.Select(Describe).SequenceEqual(other.Items.Select(Describe));
            Assert.IsTrue(anyDifferent, "Different chests in the same depth roll independently.");
            Assert.AreNotEqual(a.Items[0].InstanceId, b.Items[0].InstanceId, "Instances are unique objects even when rolls match.");
        }

        private static string Describe(ItemInstance i) => $"{i.DefinitionId}x{i.Quantity}:{i.Rarity}:{string.Join(",", i.AffixRolls.Select(r => r.AffixId + "=" + r.Value))}";

        [Test]
        public void Roll_ProducesCoinsQuantitiesAndStableEquipmentRarityAffixes()
        {
            var table = Table(
                Roll("coins", 100, Entry(null, 1, 10, 25)),
                Roll("ammo", 100, Entry(_ammo, 1, 20, 40)),
                Roll("gear", 100, Entry(_rifle)));

            var raritiesSeen = new HashSet<Rarity>();
            for (var seed = 0; seed < 300; seed++)
            {
                var result = _roller.Roll(table, new LootContext(10, LootQuality.Standard, new SeededRandom(seed)));
                Assert.That(result.Coins, Is.InRange(10, 25));
                var ammo = result.Items.Single(i => i.DefinitionId == "ammo_light");
                Assert.That(ammo.Quantity, Is.InRange(20, 40));
                var gear = result.Items.Single(i => i.DefinitionId == "weapon_test_rifle");
                Assert.AreEqual(1, gear.Quantity);
                Assert.AreEqual(RarityRules.RandomAffixCount(gear.Rarity), gear.AffixRolls.Count, "Affix count matches rolled rarity.");
                Assert.AreNotEqual(Rarity.Legendary, gear.Rarity, "Regular definitions never become Legendary.");
                raritiesSeen.Add(gear.Rarity);
                Assert.IsEmpty(result.Warnings);
            }

            CollectionAssert.IsSupersetOf(raritiesSeen, new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic });
        }

        [Test]
        public void LegendaryRoll_YieldsTheLegendaryVariantDefinition_WithThreeAffixes()
        {
            var table = Table(Roll("gear", 100, Entry(_rifle, legendary: _legendaryRifle)));
            var legendaries = 0;
            for (var seed = 0; seed < 4000; seed++)
            {
                var result = _roller.Roll(table, new LootContext(30, LootQuality.Standard, new SeededRandom(seed)));
                var gear = result.Items[0];
                if (gear.Rarity == Rarity.Legendary)
                {
                    legendaries++;
                    Assert.AreEqual("weapon_test_rifle_legendary", gear.DefinitionId);
                    Assert.AreEqual(3, gear.AffixRolls.Count);
                    Assert.AreEqual("legendary_special", AffixRollService.GetLegendaryMechanicId(gear, _legendaryRifle));
                }
                else
                {
                    Assert.AreEqual("weapon_test_rifle", gear.DefinitionId);
                }
            }

            Assert.That(legendaries, Is.InRange(20, 130), "~1.5% at depth 30");
        }

        [Test]
        public void RollChance_SkipsRolls_AndWeightsBiasEntries()
        {
            var heavy = Equipment("weapon_heavy", "");
            var light = Equipment("weapon_light", "");
            var table = Table(Roll("gear", 25, Entry(heavy, 9), Entry(light, 1)));
            var produced = 0;
            var heavyCount = 0;
            for (var seed = 0; seed < 2000; seed++)
            {
                var result = _roller.Roll(table, new LootContext(1, LootQuality.Standard, new SeededRandom(seed)));
                if (result.IsEmpty) continue;
                produced++;
                if (result.Items[0].DefinitionId == "weapon_heavy") heavyCount++;
            }

            Assert.That(produced, Is.InRange(400, 600), "25% chance");
            Assert.Greater(heavyCount, produced * 0.8f, "9:1 weighting");
        }

        [Test]
        public void SupplyChestTable_IsAuthored_WithCoinsAmmoAndSmallEquipmentChance()
        {
            var table = AssetDatabase.LoadAssetAtPath<LootTableDefinition>(SupplyTablePath);
            Assert.IsNotNull(table);
            Assert.AreEqual("loot_supply_chest", table.Id);
            Assert.AreEqual(3, table.Rolls.Count);
            Assert.IsTrue(table.Rolls[0].Entries.All(e => e.IsCoins));
            Assert.AreEqual(100, table.Rolls[0].ChancePercent);
            Assert.IsTrue(table.Rolls[1].Entries.All(e => e.Item is AmmoItemDefinition));
            Assert.AreEqual(4, table.Rolls[1].Entries.Length, "One entry per approved ammo type.");
            Assert.IsTrue(table.Rolls[2].Entries.All(e => e.Item is WeaponDefinition));
            Assert.Less(table.Rolls[2].ChancePercent, 50, "Small equipment chance.");
            foreach (var entry in table.Rolls.SelectMany(r => r.Entries))
            {
                Assert.GreaterOrEqual(entry.Weight, 1);
                Assert.LessOrEqual(entry.MinQuantity, entry.MaxQuantity);
            }

            var ammoAssets = AssetDatabase.FindAssets("t:AmmoItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<AmmoItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CollectionAssert.AreEquivalent(new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells }, ammoAssets.Select(a => a.AmmoType));
            CollectionAssert.AreEquivalent(new[] { 180, 120, 60, 40 }, ammoAssets.Select(a => a.MaxStack));

            var result = _roller.Roll(table, LootContext.ForSource(1, 1, 0, LootQuality.Standard));
            Assert.Greater(result.Coins, 0);
            Assert.IsTrue(result.Items.Any(i => i.DefinitionId.StartsWith("ammo_")));
        }
    }
}
