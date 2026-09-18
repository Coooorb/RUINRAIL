using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 073 — the four chest source types, quality tables, substreams and the ammo usefulness hook.</summary>
    public class LootSourceTests
    {
        private LootSourceCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            Assert.IsNotNull(_catalog);
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private SupplyChest Chest(LootSourceKind kind, int runSeed, int depth, int index, int party = 1, IReadOnlyCollection<AmmoType> ammo = null)
        {
            var go = new GameObject($"Chest_{kind}");
            _created.Add(go);
            var chest = go.AddComponent<SupplyChest>();
            Assert.IsTrue(_catalog.Configure(chest, kind, runSeed, depth, index, party, ammo));
            return chest;
        }

        private ItemDefinition Def(string id) => _registry.TryGet(id, out var d) ? d : null;
        private bool IsEquipment(ItemInstance i) => Def(i.DefinitionId) is EquipmentItemDefinition;

        // ---- Acceptance 1: four sources, deterministic valid loot ----

        [Test]
        public void ExactlyFourSourceKinds_EachProducesDeterministicValidLoot()
        {
            Assert.AreEqual(4, System.Enum.GetValues(typeof(LootSourceKind)).Length, "58: no coloured chest tiers.");
            Assert.AreEqual(4, _catalog.Sources.Count);
            Assert.AreEqual(LootQuality.Standard, _catalog.Sources.Single(s => s.Kind == LootSourceKind.SupplyChest).Quality);
            Assert.AreEqual(LootQuality.Standard, _catalog.Sources.Single(s => s.Kind == LootSourceKind.EquipmentChest).Quality);
            Assert.AreEqual(LootQuality.Improved, _catalog.Sources.Single(s => s.Kind == LootSourceKind.TreasureChest).Quality);
            Assert.AreEqual(LootQuality.StronglyImproved, _catalog.Sources.Single(s => s.Kind == LootSourceKind.BossCache).Quality);

            foreach (LootSourceKind kind in System.Enum.GetValues(typeof(LootSourceKind)))
            {
                var a = Chest(kind, 4242, 7, 3);
                var b = Chest(kind, 4242, 7, 3);
                Assert.IsTrue(a.TryOpen(out var ra));
                Assert.IsTrue(b.TryOpen(out var rb));
                Assert.AreEqual(kind, a.Kind);
                Assert.IsFalse(ra.IsEmpty, kind.ToString());
                CollectionAssert.AreEqual(ra.Items.Select(i => (i.DefinitionId, (int)i.Rarity, i.Quantity, string.Join("|", i.AffixRolls.Select(r => r.AffixId + r.Value)))).ToList(),
                    rb.Items.Select(i => (i.DefinitionId, (int)i.Rarity, i.Quantity, string.Join("|", i.AffixRolls.Select(r => r.AffixId + r.Value)))).ToList(), $"{kind}: same seed, same loot.");
                Assert.AreEqual(ra.Coins, rb.Coins);
                Assert.IsTrue(ra.Items.All(i => Def(i.DefinitionId) != null), "Every rolled item resolves to an authored definition.");
                Assert.IsEmpty(ra.Warnings, string.Join("\n", ra.Warnings));
            }
        }

        [Test]
        public void EquipmentTreasureAndBossCache_GuaranteeEquipment_SupplyOnlySometimes()
        {
            var supplyWithGear = 0;
            for (var i = 0; i < 40; i++)
            {
                Assert.IsTrue(Chest(LootSourceKind.EquipmentChest, 100 + i, 5, i).TryOpen(out var eq));
                Assert.IsTrue(eq.Items.Any(IsEquipment), $"Equipment chest {i} guarantees equipment.");
                Assert.IsTrue(Chest(LootSourceKind.TreasureChest, 100 + i, 5, i).TryOpen(out var tr));
                Assert.IsTrue(tr.Items.Any(IsEquipment), "Treasure guarantees equipment.");
                Assert.Greater(tr.Coins, 0);
                Assert.IsTrue(Chest(LootSourceKind.BossCache, 100 + i, 5, i).TryOpen(out var bc));
                Assert.IsTrue(bc.Items.Any(IsEquipment), "Boss Cache guarantees equipment.");
                Assert.Greater(bc.Coins, 0, "Boss Cache guarantees coins.");
                Assert.IsTrue(Chest(LootSourceKind.SupplyChest, 100 + i, 5, i).TryOpen(out var su));
                if (su.Items.Any(IsEquipment)) supplyWithGear++;
                Assert.Greater(su.Coins, 0);
            }

            Assert.IsTrue(supplyWithGear > 0 && supplyWithGear < 30, "Supply: small equipment chance.");
        }

        // ---- Acceptance 2: quality tables improve rarity without making Legendary common ----

        [Test]
        public void HigherQualitySources_RollBetterRarity_AndLegendaryStaysCapped()
        {
            var standard = _catalog.RarityTableFor(LootQuality.Standard);
            var improved = _catalog.RarityTableFor(LootQuality.Improved);
            var strong = _catalog.RarityTableFor(LootQuality.StronglyImproved);
            Assert.AreNotSame(standard, improved);
            Assert.AreNotSame(improved, strong);
            foreach (var depth in new[] { 1, 5, 10, 20, 30, 60 })
            {
                var s = standard.WeightsAt(depth);
                var i = improved.WeightsAt(depth);
                var st = strong.WeightsAt(depth);
                Assert.Less(i[0], s[0], $"Improved has fewer Commons at depth {depth}.");
                Assert.Less(st[0], i[0], "Strongly improved fewer still.");
                Assert.Greater(i[3] + i[4], s[3] + s[4], "More Epic+Legendary.");
                Assert.Greater(st[3] + st[4], i[3] + i[4]);
                Assert.LessOrEqual(st[4], 50, "Legendary never above 5 percent even for the boss cache.");
                Assert.LessOrEqual(i[4], 20);
                Assert.AreEqual(1000, s.Sum());
                Assert.AreEqual(1000, i.Sum());
                Assert.AreEqual(1000, st.Sum());
            }

            var roller = _catalog.CreateRoller();
            int legendaries = 0, epicsOrBetter = 0;
            for (var seed = 0; seed < 600; seed++)
            {
                var rarity = roller.RollRarity(new LootContext(30, LootQuality.StronglyImproved, new SeededRandom(seed)));
                if (rarity == Rarity.Legendary) legendaries++;
                if (rarity >= Rarity.Epic) epicsOrBetter++;
            }

            Assert.Less(legendaries, 80, "Legendary remains rare even deep with the strongly improved table.");
            Assert.Greater(epicsOrBetter, 120, "But Epic+ is common there.");

            // A Legendary roll on a regular weapon yields the class Legendary; on armor/accessories, the family at Legendary rarity.
            var found = new HashSet<string>();
            for (var i = 0; i < 300; i++)
            {
                Chest(LootSourceKind.BossCache, 9000 + i, 30, i).TryOpen(out var result);
                foreach (var item in result.Items.Where(x => x.Rarity == Rarity.Legendary))
                {
                    var def = Def(item.DefinitionId) as EquipmentItemDefinition;
                    Assert.IsNotNull(def);
                    Assert.IsFalse(string.IsNullOrEmpty(def.LegendaryMechanicId), $"{item.DefinitionId} rolled Legendary without a fixed mechanic.");
                    Assert.AreEqual(3, item.AffixRolls.Count);
                    found.Add(def.Category.ToString());
                }
            }

            Assert.IsTrue(found.Count >= 2, "Legendary weapons and armor/accessories both occur across 300 boss caches.");
        }

        // ---- Acceptance 3: ammo usefulness 70/30 under controlled RNG ----

        [Test]
        public void AmmoChooser_Prefers70PercentUsefulTypes_UnrestrictedWithoutAmmoWeapons()
        {
            var table = AssetDatabase.LoadAssetAtPath<LootTableDefinition>("Assets/Game/ScriptableObjects/Loot/LootTable_SupplyChest.asset");
            var ammoRoll = table.Rolls.Single(r => r.Label == "Ammo");
            Assert.IsTrue(LootRoller.IsAmmoRoll(ammoRoll.Entries));
            var useful = new[] { AmmoType.Shells };
            var shells = 0;
            const int trials = 4000;
            for (var i = 0; i < trials; i++)
            {
                var ctx = new LootContext(1, LootQuality.Standard, new SeededRandom(i), 1, unchecked((ulong)i + 1), useful);
                var pick = LootRoller.PickAmmo(ammoRoll.Entries, ctx, new SeededRandom(i * 31 + 7));
                if (((AmmoItemDefinition)pick.Item).AmmoType == AmmoType.Shells) shells++;
            }

            // 70% forced useful + 30% x (shell weight 2 of 11) = ~75.5%.
            Assert.AreEqual(0.755f, shells / (float)trials, 0.03f, "70/30 selection path.");

            var unrestricted = 0;
            for (var i = 0; i < trials; i++)
            {
                var ctx = new LootContext(1, LootQuality.Standard, new SeededRandom(i), 1, unchecked((ulong)i + 1), null);
                var pick = LootRoller.PickAmmo(ammoRoll.Entries, ctx, new SeededRandom(i * 31 + 7));
                if (((AmmoItemDefinition)pick.Item).AmmoType == AmmoType.Shells) unrestricted++;
            }

            Assert.AreEqual(2f / 11f, unrestricted / (float)trials, 0.03f, "No ammo weapon: plain weighted pick.");

            // Deterministic: the same context and stream give the same pick.
            var c1 = new LootContext(1, LootQuality.Standard, new SeededRandom(5), 1, 77, useful);
            var c2 = new LootContext(1, LootQuality.Standard, new SeededRandom(5), 1, 77, useful);
            Assert.AreSame(LootRoller.PickAmmo(ammoRoll.Entries, c1, new SeededRandom(9)).Item, LootRoller.PickAmmo(ammoRoll.Entries, c2, new SeededRandom(9)).Item);
        }

        // ---- Requirement 4: substreams keep decisions independent ----

        [Test]
        public void Substreams_KeepRarityAndAffixesStable_WhenTableOrderChanges()
        {
            var roller = _catalog.CreateRoller();
            var context = LootContext.ForSource(31337, 12, 2, LootQuality.Improved);
            var rarityA = roller.RollRarity(context, context.Substream(202));
            var rarityB = roller.RollRarity(LootContext.ForSource(31337, 12, 2, LootQuality.Improved), LootContext.ForSource(31337, 12, 2, LootQuality.Improved).Substream(202));
            Assert.AreEqual(rarityA, rarityB, "Rarity comes from its own substream.");
            var sel1 = context.Substream(101);
            var sel2 = context.Substream(101);
            Assert.AreEqual(sel1.NextInt(1000), sel2.NextInt(1000), "Substreams are reproducible.");
            Assert.AreNotEqual(context.Substream(101).NextInt(100000), context.Substream(202).NextInt(100000), "Distinct salts are distinct streams.");

            // The loot stream never touches the dungeon/encounter streams of the same run and depth.
            var dungeon = RngStreams.Derive(31337, 12, RngStream.Dungeon).NextInt(int.MaxValue);
            Chest(LootSourceKind.BossCache, 31337, 12, 0).TryOpen(out _);
            Assert.AreEqual(dungeon, RngStreams.Derive(31337, 12, RngStream.Dungeon).NextInt(int.MaxValue));
        }

        // ---- Acceptance 4: each source pays out once; unique instances; boss cache locked until the boss falls ----

        [Test]
        public void Sources_PayOutOnce_WithUniqueInstances_AndBossCacheWaitsForTheBoss()
        {
            var chest = Chest(LootSourceKind.TreasureChest, 1, 9, 0);
            Assert.IsTrue(chest.TryOpen(out var first));
            Assert.IsFalse(chest.TryOpen(out var second), "Second open pays nothing.");
            Assert.IsNull(second);
            Assert.IsFalse(chest.CanInteract(null));

            var ids = new HashSet<string>();
            for (var i = 0; i < 50; i++)
            {
                Chest(LootSourceKind.BossCache, 2, 15, i).TryOpen(out var result);
                foreach (var item in result.Items) Assert.IsTrue(ids.Add(item.InstanceId), "Instance ids are globally unique.");
            }

            var cache = Chest(LootSourceKind.BossCache, 3, 20, 0);
            var gate = cache.gameObject.AddComponent<BossCacheGate>();
            var bossObject = new GameObject("Boss");
            _created.Add(bossObject);
            var encounter = bossObject.AddComponent<RuinRail.Gameplay.Enemies.Bosses.BossEncounter>();
            gate.Bind(encounter);
            Assert.IsTrue(cache.IsLocked);
            Assert.IsFalse(cache.CanInteract(null), "Locked until the boss is defeated.");
            Assert.IsFalse(cache.TryOpen(out _));
            typeof(RuinRail.Gameplay.Enemies.Bosses.BossEncounter).GetField("_defeated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(encounter, true);
            typeof(RuinRail.Gameplay.Enemies.Bosses.BossEncounter).GetEvent("BossDefeated").GetRaiseMethod(true)?.Invoke(encounter, new object[] { encounter, 650 });
            gate.Bind(encounter); // re-bind observes the defeated state
            Assert.IsFalse(cache.IsLocked);
            Assert.IsTrue(cache.TryOpen(out var loot));
            Assert.IsTrue(loot.Items.Any(IsEquipment));
            Assert.IsFalse(cache.TryOpen(out _), "Boss Cache opens once.");
        }
    }
}
