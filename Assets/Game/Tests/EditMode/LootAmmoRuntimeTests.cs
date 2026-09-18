using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Loot-source presence and ammo economy on the real generator, real room prefabs and real loot tables:
    /// the ordinary-room Supply Chest plan (deterministic, ~25 %, floored), its placement inside walkable room space
    /// away from doors/markers, the 100-seeds-per-biome sweep (no depth without meaningful loot), the usefulness
    /// weighting of ammo rolls, the caps, and the 300-run ammo economy before/after. Evidence is written to
    /// <c>TestResults/LootAmmoAudioProof</c>.
    /// </summary>
    public sealed class LootAmmoRuntimeTests
    {
        public const string ProofFolder = "TestResults/LootAmmoAudioProof";
        private const int SeedsPerBiome = 100;

        private static readonly Biome[] Biomes = { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs };
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp() => Directory.CreateDirectory(ProofFolder);

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static DungeonGenerationResult Generate(GameContentCatalog catalog, BiomeRoomPools pools, Biome biome, int seed, int depth)
        {
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var result = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(rules), pools.PoolFor(biome), seed, depth);
                Assert.IsTrue(result.Success, $"{biome} seed {seed} depth {depth}: {result.Error}");
                return result;
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }
        }

        // ---- Planner ----

        [Test]
        public void SupplyChestPlan_IsDeterministic_AboutAQuarterOfOrdinaryRooms_NeverStartBossOrSpecial_AndAlwaysMeetsTheFloor()
        {
            var catalog = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(catalog.Rooms);
            var eligibleTotal = 0;
            var selectedTotal = 0;
            var depthsAtFloorOnly = 0;
            foreach (var biome in Biomes)
            {
                for (var seed = 1; seed <= SeedsPerBiome; seed++)
                {
                    var depth = 1 + seed % 3;
                    var graph = Generate(catalog, pools, biome, seed, depth).Graph;
                    var plan = SupplyChestPlanner.Plan(graph, seed, depth);
                    var again = SupplyChestPlanner.Plan(graph, seed, depth);
                    CollectionAssert.AreEquivalent(plan, again, "same seed → same rooms");
                    var eligible = graph.Nodes.Where(n => SupplyChestPlanner.IsEligible(n, graph)).Select(n => n.Id).ToList();
                    Assert.IsTrue(plan.All(id => eligible.Contains(id)), "only ordinary Combat rooms carry a Supply Chest");
                    Assert.IsFalse(plan.Contains(graph.StartId) || plan.Contains(graph.BossId));
                    Assert.IsTrue(plan.All(id => graph.GetNode(id).Type == RoomType.Combat));
                    Assert.GreaterOrEqual(plan.Count, System.Math.Min(SupplyChestPlanner.MinimumPerDepth, eligible.Count), $"{biome} seed {seed}: the floor holds");
                    var byRoll = eligible.Count(id => SupplyChestPlanner.RollFor(seed, depth, id) < SupplyChestPlanner.OrdinaryRoomPercent);
                    if (byRoll < SupplyChestPlanner.MinimumPerDepth) depthsAtFloorOnly++;
                    Assert.AreEqual(System.Math.Max(byRoll, System.Math.Min(SupplyChestPlanner.MinimumPerDepth, eligible.Count)), plan.Count, "rolls below the threshold plus the floor promotion, nothing else");
                    eligibleTotal += eligible.Count;
                    selectedTotal += plan.Count;
                }
            }

            var share = selectedTotal / (float)eligibleTotal;
            TestContext.WriteLine($"ordinary rooms with a Supply Chest: {selectedTotal}/{eligibleTotal} = {share:P1}; depths lifted by the floor: {depthsAtFloorOnly}/{3 * SeedsPerBiome}");
            Assert.That(share, Is.InRange(0.22f, 0.50f), "≈25 % by roll plus the deterministic floor, never a chest in every room");
        }

        // ---- Placement ----

        [Test]
        public void SupplyChestPlacement_ForEveryShippedCombatRoom_IsWalkable_ReachableFromEveryDoor_ClearOfDoorsAndMarkers_AndDeterministic()
        {
            var catalog = GameContentCatalog.Load();
            var combatRooms = catalog.Rooms.Where(r => r.RoomType == RoomType.Combat).ToList();
            Assert.AreEqual(33, combatRooms.Count, "11 combat rooms per biome");
            foreach (var definition in combatRooms)
            {
                var instance = Object.Instantiate(definition.Prefab);
                _created.Add(instance);
                var root = instance.GetComponent<RoomRoot>();
                var grid = RoomLogicGrid.FromRoom(root);
                var sockets = root.GetSockets();
                var reachable = SupplyChestPlacement.ReachableFromDoors(grid, sockets);
                var candidates = SupplyChestPlacement.Candidates(root, grid);
                Assert.Greater(candidates.Count, 0, $"{definition.Id}: at least one valid cell");
                for (var seed = 1; seed <= 12; seed++)
                {
                    var cell = SupplyChestPlacement.Choose(root, seed, 1, 7, grid);
                    Assert.IsTrue(cell.HasValue, definition.Id);
                    Assert.AreEqual(cell, SupplyChestPlacement.Choose(root, seed, 1, 7, grid), $"{definition.Id}: deterministic per seed");
                    var c = cell.Value;
                    Assert.IsTrue(grid.IsWalkable(c), $"{definition.Id} {c}: walkable floor, never inside a wall or obstacle");
                    Assert.IsTrue(reachable.Contains(c), $"{definition.Id} {c}: reachable from the doorways");
                    Assert.IsTrue(c.x >= SupplyChestPlacement.EdgeClearanceTiles && c.y >= SupplyChestPlacement.EdgeClearanceTiles && c.x < root.Size.x - SupplyChestPlacement.EdgeClearanceTiles && c.y < root.Size.y - SupplyChestPlacement.EdgeClearanceTiles, $"{definition.Id} {c}: clear of the wall ring");
                    foreach (var socket in sockets)
                    foreach (var doorCell in socket.Cells())
                        Assert.GreaterOrEqual(Mathf.Max(Mathf.Abs(doorCell.x - c.x), Mathf.Abs(doorCell.y - c.y)), SupplyChestPlacement.DoorClearanceTiles, $"{definition.Id} {c}: never blocks the {socket.Direction} door");
                    foreach (var marker in root.GetMarkers().Where(m => m.Role != RoomMarkerRole.Walkable))
                        Assert.IsFalse(marker.Rect.Contains(c) || Neighbours(c).Any(n => marker.Rect.Contains(n)), $"{definition.Id} {c}: clear of the {marker.Role} marker at {marker.Cell} and its neighbours");
                    // The activation volume is a trigger the chest never blocks, and the chest sits strictly inside the room proper.
                    var volume = RoomEntryTrigger.InteriorVolume(root.Size);
                    Assert.IsTrue(volume.Contains(new Vector2(c.x + 0.5f, c.y + 0.5f)), $"{definition.Id} {c}: inside the room interior");
                }
            }
        }

        private static IEnumerable<Vector2Int> Neighbours(Vector2Int c)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
                yield return new Vector2Int(c.x + dx, c.y + dy);
        }

        // ---- Seed sweep ----

        [Test]
        public void SeedSweep_100SeedsPerBiome_NoValidDepthWithoutMeaningfulLoot_AndSpecialRoomsAlwaysCarryTheirSource()
        {
            var catalog = GameContentCatalog.Load();
            var harness = new AmmoEconomyHarness(catalog);
            var csv = new StringBuilder("biome,depth,seed,room_count,chest_container_count,special_loot_source_count,ammo_source_opportunity_count,relevant_ammo_quantity_budget\n");
            var minChests = int.MaxValue;
            var minAmmoOpportunities = int.MaxValue;
            var zeroBudget = 0;
            foreach (var biome in Biomes)
            {
                for (var seed = 1; seed <= SeedsPerBiome; seed++)
                {
                    var depth = 1 + seed % 3;
                    var run = harness.Simulate(biome, seed, depth);
                    csv.AppendLine($"{biome},{depth},{seed},{run.RoomCount},{run.ChestContainerCount},{run.SpecialLootSourceCount},{run.AmmoOpportunityCount},{run.RelevantAmmoFound}");
                    minChests = System.Math.Min(minChests, run.ChestContainerCount);
                    minAmmoOpportunities = System.Math.Min(minAmmoOpportunities, run.AmmoOpportunityCount);
                    if (run.RelevantAmmoFound == 0) zeroBudget++;
                    Assert.GreaterOrEqual(run.SupplyChests, SupplyChestPlanner.MinimumPerDepth, $"{biome} seed {seed} depth {depth}: ammo opportunities before the boss");
                    Assert.Greater(run.ChestContainerCount - 1, 0, $"{biome} seed {seed} depth {depth}: meaningful loot before the boss (not only the Boss Cache)");
                }
            }

            File.WriteAllText(Path.Combine(ProofFolder, "loot_seed_evidence.csv"), csv.ToString());
            TestContext.WriteLine($"min chest/container count per depth {minChests}, min ammo opportunities {minAmmoOpportunities}, depths with a zero relevant-ammo budget {zeroBudget}/{3 * SeedsPerBiome}");
            Assert.GreaterOrEqual(minChests, SupplyChestPlanner.MinimumPerDepth + 1);
            Assert.GreaterOrEqual(minAmmoOpportunities, SupplyChestPlanner.MinimumPerDepth);
            // 26/58: the 70/30 rule keeps ammo rolls non-deterministic, so a depth can (rarely) roll only other types from its opportunities.
            Assert.LessOrEqual(zeroBudget, 3 * SeedsPerBiome / 20, "with two opportunities at ~81 % relevance a zero relevant-ammo depth is rare (≤ 5 %)");

            // Special rooms promise their source in the prefab: every Loot room 2 chest markers, every Treasure room 1, every Boss room a cache marker.
            foreach (var room in catalog.Rooms)
            {
                var markers = room.Prefab.GetComponentsInChildren<RoomMarker>(true).Count(m => m.Role == RoomMarkerRole.ChestSpawn);
                switch (room.RoomType)
                {
                    case RoomType.Loot: Assert.AreEqual(2, markers, room.Id); break;
                    case RoomType.Treasure: Assert.AreEqual(1, markers, room.Id); break;
                    case RoomType.Boss: Assert.AreEqual(1, markers, room.Id); break;
                    case RoomType.Event: Assert.AreEqual(1, room.Prefab.GetComponentsInChildren<RoomMarker>(true).Count(m => m.Role == RoomMarkerRole.EventAnchor), room.Id); break;
                }
            }
        }

        /// <summary>Seed index for proof runs: what the first depth of a fresh run contains per seed (BiomeSelector picks the biome from the seed).</summary>
        [Test]
        public void SeedIndex_ForProofRuns_ListsSpecialRoomsOfTheFirstDepth()
        {
            var catalog = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(catalog.Rooms);
            var csv = new StringBuilder("seed,biome,rooms,combat,loot_rooms,treasure_rooms,merchant,events,supply_chests\n");
            var withLootRoom = 0;
            for (var seed = 1; seed <= 60; seed++)
            {
                var biome = RuinRail.Gameplay.Expedition.BiomeSelector.SelectFirst(seed);
                var graph = Generate(catalog, pools, biome, seed, 1).Graph;
                var loot = graph.Nodes.Count(n => n.Type == RoomType.Loot);
                var treasure = graph.Nodes.Count(n => n.Type == RoomType.Treasure);
                if (loot + treasure > 0) withLootRoom++;
                csv.AppendLine($"{seed},{biome},{graph.Nodes.Count},{graph.Nodes.Count(n => n.Type == RoomType.Combat)},{loot},{treasure},{graph.Nodes.Count(n => n.Type == RoomType.Merchant)},{graph.Nodes.Count(n => n.Type == RoomType.Event)},{SupplyChestPlanner.Plan(graph, seed, 1).Count}");
            }

            File.WriteAllText(Path.Combine(ProofFolder, "seed_room_index.csv"), csv.ToString());
            Assert.Greater(withLootRoom, 0, "some first depths carry a Loot/Treasure room (55: 0–2 / 0–1 per dungeon)");
        }

        // ---- Ammo rolls ----

        [Test]
        public void SupplyChestRolls_AlwaysProduceValidNonZeroAmmo_AndPreferTheCarriedType_WithoutBecomingDeterministic()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsTrue(catalog.Loot.TryGet(LootSourceKind.SupplyChest, out var source));
            var roller = catalog.Loot.CreateRoller();
            var registry = catalog.BuildRegistry();
            int light = 0, total = 0, unrestrictedLight = 0;
            var quantities = new List<int>();
            for (var i = 0; i < 1000; i++)
            {
                var useful = roller.Roll(source.Table, LootContext.ForSource(i, 1, 15, LootQuality.Standard, 1, new[] { AmmoType.Light }));
                var ammo = useful.Items.Select(it => registry.TryGet(it.DefinitionId, out var d) ? (d as AmmoItemDefinition, it.Quantity) : (null, 0)).Where(t => t.Item1 != null).ToList();
                Assert.AreEqual(1, ammo.Count, "every Supply Chest carries exactly one ammo stack");
                Assert.Greater(ammo[0].Item2, 0);
                quantities.Add(ammo[0].Item2);
                total++;
                if (ammo[0].Item1.AmmoType == AmmoType.Light) light++;
                var any = roller.Roll(source.Table, LootContext.ForSource(i, 1, 15, LootQuality.Standard, 1, null));
                if (any.Items.Any(it => it.DefinitionId == "ammo_light")) unrestrictedLight++;
            }

            var share = light / (float)total;
            var baseline = unrestrictedLight / (float)total;
            TestContext.WriteLine($"Light share with a Light weapon carried: {share:P1}; unrestricted: {baseline:P1}; quantity {quantities.Min()}..{quantities.Max()} avg {quantities.Average():0.0}");
            Assert.That(share, Is.InRange(0.72f, 0.92f), "70 % carried-type weight + 30 % random (which still contains Light): preferred, not deterministic");
            Assert.That(baseline, Is.InRange(0.28f, 0.46f), "without an ammo weapon the pick is the unrestricted weighted pool");
            Assert.Greater(share, baseline + 0.25f);
        }

        [Test]
        public void AmmoCaps_AreEnforcedOnPickup_AndNothingDuplicatesOrGoesInfinite()
        {
            var catalog = GameContentCatalog.Load();
            var inventory = PlayerInventory.FromRegistry(catalog.BuildRegistry(), catalog.AmmoBalance);
            // 26: the caps are per backpack slot (Light 180, Medium 120, Heavy 60, Shells 40). Fill all but one slot so exactly one stack can exist per type.
            for (var i = 0; i < PlayerInventory.BackpackCapacity - 1; i++) Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger", 1)), "weapons never stack: one slot each");
            Assert.AreEqual(60, inventory.Add(AmmoType.Light, 60));
            Assert.AreEqual(120, inventory.Add(AmmoType.Light, 500), "only the room up to the stack cap is taken");
            Assert.AreEqual(180, inventory.Get(AmmoType.Light));
            Assert.AreEqual(0, inventory.Add(AmmoType.Light, 10), "a full stack with no free slot takes nothing");
            Assert.AreEqual(180, inventory.Consume(AmmoType.Light, 1000), "a reserve never goes negative or refills itself");
            Assert.AreEqual(0, inventory.Get(AmmoType.Light));
            Assert.AreEqual(120, inventory.Add(AmmoType.Medium, 999));
            Assert.AreEqual(0, inventory.Add(AmmoType.Medium, 1));
            Assert.AreEqual(120, inventory.Consume(AmmoType.Medium, 999));
            Assert.AreEqual(60, inventory.Add(AmmoType.Heavy, 999));
            Assert.AreEqual(60, inventory.Consume(AmmoType.Heavy, 999));
            Assert.AreEqual(40, inventory.Add(AmmoType.Shells, 999));
            Assert.AreEqual(5, inventory.Consume(AmmoType.Shells, 5));
            Assert.AreEqual(35, inventory.Get(AmmoType.Shells));
        }

        // ---- 300-run economy ----

        [Test]
        public void AmmoEconomy_300GeneratedDepths_AfterTheFix_AtLeast85PercentAreNotMajorityDry_AndTheMedianEndReserveStaysBelowTheCap()
        {
            var catalog = GameContentCatalog.Load();
            var harness = new AmmoEconomyHarness(catalog);
            var before = new List<AmmoEconomyHarness.RunResult>();
            var after = new List<AmmoEconomyHarness.RunResult>();
            foreach (var biome in Biomes)
            {
                for (var seed = 1; seed <= SeedsPerBiome; seed++)
                {
                    var depth = 1 + seed % 3;
                    before.Add(harness.Simulate(biome, seed, depth, ordinaryChests: false));
                    after.Add(harness.Simulate(biome, seed, depth, ordinaryChests: true));
                }
            }

            var report = new StringBuilder();
            report.AppendLine("# Ammo economy simulation — starter-firearm path (P9 Ranger), 300 generated depths");
            report.AppendLine();
            report.AppendLine($"Model: real generator/room pool/encounter director/depth scaling/Elite+Boss rosters/loot tables/LootRoller; accuracy {AmmoEconomyHarness.DefaultAccuracy:P0} (misses spend rounds); starter reserve {RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount}; the ammo-free secondary is never counted; the merchant Light bundle is bought when coins found in the depth cover it. 100 seeds per biome, depths 1–3 (seed % 3 + 1).");
            report.AppendLine();
            report.AppendLine("Metrics: **room-weighted** (an engagement is \"dry\" when it starts with nothing to fire or more than half of its required rounds cannot be fired; a run is majority-dry when more than half of its engagements are dry — this is the acceptance metric) and **shot-weighted** (unfired/required rounds; the boss alone is ~40 % of a depth's rounds at the starter pistol's damage, so this share is dominated by the boss fight).");
            report.AppendLine();
            Summarise(report, "BEFORE (no Supply Chest in ordinary rooms, no usefulness weighting — the pre-fix runtime)", before, catalog.AmmoBalance.GetStackLimit(AmmoType.Light));
            Summarise(report, $"AFTER ({SupplyChestPlanner.OrdinaryRoomPercent} % ordinary rooms + floor of {SupplyChestPlanner.MinimumPerDepth}, 70/30 usefulness weighting — the shipped rule)", after, catalog.AmmoBalance.GetStackLimit(AmmoType.Light));
            report.AppendLine("## Sensitivity (same 300 depths)");
            report.AppendLine();
            report.AppendLine("| accuracy | floor | not majority-dry (rooms) | shot-weighted median dry share | median Light found | median end reserve |");
            report.AppendLine("|---|---|---|---|---|---|");
            foreach (var accuracy in new[] { 0.55f, 0.65f, 0.75f })
            foreach (var floor in new[] { 2, 3 })
            {
                var probe = new AmmoEconomyHarness(catalog) { Accuracy = accuracy, MinimumChests = floor };
                var runs = new List<AmmoEconomyHarness.RunResult>();
                foreach (var biome in Biomes) for (var seed = 1; seed <= SeedsPerBiome; seed++) runs.Add(probe.Simulate(biome, seed, 1 + seed % 3));
                report.AppendLine($"| {accuracy:P0} | {floor} | {runs.Count(r => !r.MajorityAtZero)}/{runs.Count} | {Median(runs.Select(r => r.DryShare).ToList()):P1} | {Median(runs.Select(r => r.RelevantAmmoFound).ToList())} | {Median(runs.Select(r => r.ReserveAtEnd).ToList())} |");
            }

            report.AppendLine();
            var csv = new StringBuilder("phase,biome,depth,seed,rooms,combat_rooms,elite_rooms,supply_chests,relevant_ammo_found,other_ammo_found,coins_found,merchant_rounds,rounds_needed,rounds_fired,rounds_unfired,dry_share,reserve_end,rooms_ended_dry,engagements\n");
            foreach (var r in before) csv.AppendLine(Row("before", r));
            foreach (var r in after) csv.AppendLine(Row("after", r));
            File.WriteAllText(Path.Combine(ProofFolder, "ammo_economy_simulation.md"), report.ToString());
            File.WriteAllText(Path.Combine(ProofFolder, "ammo_economy_simulation.csv"), csv.ToString());
            TestContext.WriteLine(report.ToString());

            var okShare = after.Count(r => !r.MajorityAtZero) / (float)after.Count;
            var medianEnd = Median(after.Select(r => r.ReserveAtEnd).ToList());
            Assert.AreEqual(300, after.Count);
            Assert.GreaterOrEqual(okShare, 0.85f, "at least 85 % of representative runs do not spend the majority of the depth at zero primary reserve");
            Assert.Less(medianEnd, catalog.AmmoBalance.GetStackLimit(AmmoType.Light) * 0.75f, "the median end-of-depth reserve stays meaningfully below the hard cap: ammo remains scarce");
            Assert.Greater(after.Count(r => r.RoomsEndedDry > 0), 0, "poor accuracy / overuse can still run a room dry — scarcity is preserved");
            Assert.Less(after.Max(r => r.RelevantAmmoFound), catalog.AmmoBalance.GetStackLimit(AmmoType.Light) * 2, "no depth showers the player in ammo");
        }

        private static string Row(string phase, AmmoEconomyHarness.RunResult r) =>
            $"{phase},{r.Biome},{r.Depth},{r.Seed},{r.RoomCount},{r.CombatRooms},{r.EliteRooms},{r.SupplyChests},{r.RelevantAmmoFound},{r.OtherAmmoFound},{r.CoinsFound},{r.MerchantRoundsBought},{r.RoundsNeeded},{r.RoundsFired},{r.RoundsUnfired},{r.DryShare:0.000},{r.ReserveAtEnd},{r.RoomsEndedDry},{r.EngagementRooms},{r.DryRooms},{(r.MajorityAtZero ? 1 : 0)}";

        private static void Summarise(StringBuilder report, string title, List<AmmoEconomyHarness.RunResult> runs, int cap)
        {
            var ok = runs.Count(r => !r.MajorityAtZero);
            report.AppendLine($"## {title}");
            report.AppendLine();
            report.AppendLine($"- runs not majority-dry (room-weighted, acceptance metric): **{ok}/{runs.Count} = {ok / (float)runs.Count:P1}**; runs with fewer than half of all rounds unfired (shot-weighted): {runs.Count(r => !r.MajorityOfShotsUnfired)}/{runs.Count}");
            report.AppendLine($"- dry engagements per depth: median {Median(runs.Select(r => r.DryRooms).ToList())} of median {Median(runs.Select(r => r.EngagementRooms).ToList())} engagements");
            report.AppendLine($"- rounds needed per depth: min {runs.Min(r => r.RoundsNeeded)}, median {Median(runs.Select(r => r.RoundsNeeded).ToList())}, max {runs.Max(r => r.RoundsNeeded)}");
            report.AppendLine($"- relevant (Light) ammo found per depth: min {runs.Min(r => r.RelevantAmmoFound)}, median {Median(runs.Select(r => r.RelevantAmmoFound).ToList())}, max {runs.Max(r => r.RelevantAmmoFound)}; other ammo median {Median(runs.Select(r => r.OtherAmmoFound).ToList())}");
            report.AppendLine($"- supply chests per depth: min {runs.Min(r => r.SupplyChests)}, median {Median(runs.Select(r => r.SupplyChests).ToList())}, max {runs.Max(r => r.SupplyChests)}; merchant bundles bought in {runs.Count(r => r.MerchantRoundsBought > 0)} runs");
            report.AppendLine($"- dry share (unfired/needed): median {Median(runs.Select(r => r.DryShare).ToList()):P1}, runs with any dry room {runs.Count(r => r.RoomsEndedDry > 0)}");
            report.AppendLine($"- end-of-depth reserve (mag+reserve): min {runs.Min(r => r.ReserveAtEnd)}, median {Median(runs.Select(r => r.ReserveAtEnd).ToList())}, max {runs.Max(r => r.ReserveAtEnd)} (cap {cap})");
            foreach (var biome in Biomes)
            {
                var b = runs.Where(r => r.Biome == biome).ToList();
                report.AppendLine($"- {biome}: not majority-dry {b.Count(r => !r.MajorityAtZero)}/{b.Count}, median needed {Median(b.Select(r => r.RoundsNeeded).ToList())}, median found {Median(b.Select(r => r.RelevantAmmoFound).ToList())}");
            }

            report.AppendLine();
        }

        private static float Median(List<int> values) => Median(values.Select(v => (float)v).ToList());

        private static float Median(List<float> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            if (sorted.Count == 0) return 0f;
            return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5f;
        }
    }
}
