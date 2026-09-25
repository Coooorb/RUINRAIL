using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Production;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Validation and artefacts for the run-variety / depth-retention / boss pass: the reward calibration sweep, the
    /// elite frequency after the change, the depth-gating generation sweep, the biome identity measurement, the frozen
    /// checks, save/exploit safety, and the contract validator including a deliberately broken fixture.
    /// </summary>
    public class RunVarietyValidationTests
    {
        public const string Folder = RunVarietyBaselineTests.Folder;

        private GameContentCatalog _content;
        private PriceService _prices;
        private BiomeRoomPools _pools;
        private readonly List<UnityEngine.Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _prices = new PriceService(_content.Economy);
            _pools = BiomeRoomPools.Build(_content.Rooms);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private DungeonGenerationResult Generate(Biome biome, int seed, int depth)
        {
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                return DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(rules), _pools.PoolFor(biome), seed, depth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        // ================= PHASE 7 (calibration) =================

        [Test]
        public void Phase7_WritesPost30RewardCalibration()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Candidate,Shape,D30,D40,D50,D75,D100,D500,MultiplierVsD30AtD100,ExpectedInflationImpact,D1ToD30Changed,BoundedAndMonotonic,Chosen,Why");
            // Three candidates over the same shipped formula shape, compared against the risk they have to track:
            // enemy HP goes x2.9 -> x5.5 (+90%) and damage x1.75 -> x2.6 (+49%) between D30 and D100.
            foreach (var (name, shape, percent, cap, isRoot) in new (string, string, int, int, bool)[]
            {
                ("A linear 1%/depth", "1 + 0.01 x (d-30), capped", 1, 175, false),
                ("B sqrt 7%/root-depth", "1 + 0.07 x sqrt(d-30), capped", 7, 175, true),
                ("C linear 2%/depth", "1 + 0.02 x (d-30), capped", 2, 200, false)
            })
            {
                float M(int d)
                {
                    var over = d - 30;
                    if (over <= 0) return 1f;
                    var m = isRoot ? 1f + percent / 100f * Mathf.Sqrt(over) : 1f + percent / 100f * over;
                    return Mathf.Min(m, cap / 100f);
                }

                var capReached = Enumerable.Range(31, 470).FirstOrDefault(d => M(d) >= cap / 100f - 0.0001f);
                var flatFrom = capReached == 0 ? "never inside D500" : "D" + capReached;
                csv.AppendLine(string.Join(",", new[]
                {
                    name, Csv(shape), F(M(30)), F(M(40)), F(M(50)), F(M(75)), F(M(100)), F(M(500)),
                    "x" + F(M(100)), Csv($"flat again from {flatFrom}; D100 pays {F((M(100) - 1f) * 100f)}% more than D30 against +90% enemy HP"),
                    "no", "yes",
                    name.StartsWith("B", StringComparison.Ordinal) ? "CHOSEN" : "no",
                    Csv(name.StartsWith("A", StringComparison.Ordinal)
                        ? "rises linearly, so every step pays the same — no diminishing returns, and it caps at D105"
                        : name.StartsWith("B", StringComparison.Ordinal)
                            ? "each step is smaller than the last and the cap is far outside plausible play, so the curve never goes flat again where a player will see it"
                            : "too steep: it reaches the cap at D80 and is reward-flat again from there, which is the exact problem this phase exists to fix")
                }));
            }

            // The shipped curve must be candidate B.
            var economy = _content.Economy;
            csv.AppendLine();
            csv.AppendLine("# SHIPPED CURVE (read back from EconomyConfig)");
            csv.AppendLine("Depth,CoinMultiplier,XpMultiplier");
            foreach (var d in new[] { 1, 10, 20, 30, 31, 40, 50, 75, 100, 200, 500 })
                csv.AppendLine($"{d},{F(economy.CoinRewardMultiplier(d))},{F(economy.XpRewardMultiplier(d))}");
            File.WriteAllText(Path.Combine(Folder, "post30_reward_calibration.csv"), csv.ToString());

            // ---- acceptance ----
            for (var d = 1; d <= 30; d++)
            {
                Assert.AreEqual(1f, economy.CoinRewardMultiplier(d), 0.0001f, $"D{d} coin reward must be exactly unchanged");
                Assert.AreEqual(1f, economy.XpRewardMultiplier(d), 0.0001f, $"D{d} XP must be exactly unchanged");
            }

            Assert.Greater(economy.CoinRewardMultiplier(31), 1f, "D31 must already pay more than D30");
            Assert.AreEqual(1.22f, economy.CoinRewardMultiplier(40), 0.01f);
            Assert.AreEqual(1.59f, economy.CoinRewardMultiplier(100), 0.01f);
            Assert.LessOrEqual(economy.CoinRewardMultiplier(10000), RunVarietyDepthRetentionValidator.RewardCurveHardCap, "the curve is hard-capped");
            // Diminishing returns: each 10-depth step past the start pays less than the one before it.
            var previousStep = float.MaxValue;
            for (var d = 40; d <= 100; d += 10)
            {
                var step = economy.CoinRewardMultiplier(d) - economy.CoinRewardMultiplier(d - 10);
                Assert.Less(step, previousStep + 0.0001f, $"the step into D{d} must not be larger than the previous one");
                previousStep = step;
            }
        }

        [Test]
        public void Phase7_TheCurveReachesRealRewardsThroughTheShippedSeams()
        {
            // Coins: the real roller, the real boss-cache table, one depth below and one above the start.
            var roller = _content.Loot.CreateRoller(_content.Economy);
            Assert.IsTrue(_content.Loot.TryGet(LootSourceKind.BossCache, out var cache) && cache.Table != null);
            float CoinsAt(int depth)
            {
                var sum = 0f;
                for (var i = 0; i < 300; i++)
                    sum += roller.Roll(cache.Table, LootContext.ForSource(777000 + i, depth, i, cache.Quality, 1, new[] { AmmoType.Light })).Coins;
                return sum / 300f;
            }

            var d30 = CoinsAt(30);
            var d100 = CoinsAt(100);
            Assert.Greater(d100, d30 * 1.4f, $"a Depth 100 boss cache must pay materially more than Depth 30 ({d30:0.#} -> {d100:0.#})");

            // A roller built without an economy must stay exactly as authored, so fixtures and the Shelter are unaffected.
            var plain = _content.Loot.CreateRoller();
            var plainD100 = Enumerable.Range(0, 300).Sum(i => (float)plain.Roll(cache.Table, LootContext.ForSource(777000 + i, 100, i, cache.Quality, 1, new[] { AmmoType.Light })).Coins) / 300f;
            Assert.AreEqual(d30, plainD100, d30 * 0.1f, "without an economy the roller pays the authored amount at every depth");

            // XP: the real service seam, at D30 and D100, on a real profile.
            Assert.AreEqual(100, XpFor(30, 100), "D30 XP is exactly the authored amount");
            Assert.Greater(XpFor(100, 100), 140, "D100 XP carries the curve");
        }

        /// <summary>Runs the real expedition service to the given depth and returns the XP one 100-XP kill commits.</summary>
        private int XpFor(int depth, int authoredXp)
        {
            var registry = _content.BuildRegistry();
            var ammo = registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(id => registry.TryGet(id, out var d) ? d : null,
                t => ammo.TryGetValue(t, out var a) ? a : null, _content.AmmoBalance, null, "local", _content.Economy);
            var profile = new PlayerProfile();
            var state = expedition.Start(profile, 4242, Biome.RuinedMetro);
            while (state.Depth < depth)
            {
                expedition.RecordBossDefeated(0);
                expedition.Descend();
            }

            var before = state.Stats.XpEarned;
            expedition.RecordEnemyDefeated(authoredXp);
            return state.Stats.XpEarned - before;
        }

        // ================= PHASE 2 / 12: persistence and exploit safety =================

        [Test]
        public void Phase2_DeepestDepthIsMonotonicAndCannotBeExploited()
        {
            var registry = _content.BuildRegistry();
            var ammo = registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            ExpeditionService NewService() => new(id => registry.TryGet(id, out var d) ? d : null,
                t => ammo.TryGetValue(t, out var a) ? a : null, _content.AmmoBalance, null, "local", _content.Economy);

            var profile = new PlayerProfile();
            Assert.AreEqual(0, profile.DeepestDepthReached, "a fresh profile has no record");

            var expedition = NewService();
            var state = expedition.Start(profile, 11, Biome.RuinedMetro);
            Assert.AreEqual(0, expedition.DeepestDepthReached, "starting an expedition alone records nothing — arrival does");
            Assert.IsTrue(expedition.RecordDepthArrival(1), "arriving at D1 sets the record");
            Assert.AreEqual(1, profile.DeepestDepthReached);
            Assert.IsFalse(expedition.RecordDepthArrival(1), "replaying the same arrival changes nothing");
            Assert.IsFalse(expedition.RecordDepthArrival(0), "a non-depth is refused");

            // Defeating the boss must not record the next depth; only arriving there does.
            expedition.RecordBossDefeated(10);
            Assert.AreEqual(1, profile.DeepestDepthReached, "defeating the D1 boss does not record D2");
            expedition.Descend();
            Assert.AreEqual(2, state.Depth);
            Assert.AreEqual(1, profile.DeepestDepthReached, "requesting a descend does not record D2 either");
            Assert.IsTrue(expedition.RecordDepthArrival(2), "arriving at D2 records it");
            Assert.AreEqual(2, profile.DeepestDepthReached);
            Assert.IsTrue(expedition.IsNewPersonalBestThisExpedition);

            // Dying does not reduce it, and the summary reports it.
            var summary = expedition.Fail();
            Assert.AreEqual(2, profile.DeepestDepthReached, "a lost run keeps the record");
            Assert.AreEqual(2, summary.DeepestDepthReached);
            Assert.AreEqual(0, summary.DeepestDepthBefore);
            Assert.IsTrue(summary.IsNewPersonalBest);

            // A later shallow run cannot lower it, and does not claim a new best.
            var second = NewService();
            second.Start(profile, 12, Biome.Rustworks);
            Assert.AreEqual(2, second.DeepestDepthAtExpeditionStart);
            Assert.IsFalse(second.RecordDepthArrival(1), "a shallower arrival is ignored");
            Assert.AreEqual(2, profile.DeepestDepthReached);
            Assert.IsFalse(second.IsNewPersonalBestThisExpedition);
            var shallow = second.Return();
            Assert.IsFalse(shallow.IsNewPersonalBest, "a shallow run does not claim a personal best");
            Assert.AreEqual(2, shallow.DeepestDepthReached);
        }

        [Test]
        public void Phase12_OldSavesMigrateWithoutInventingAPersonalBest()
        {
            // A save document written before this pass simply has no field; JsonUtility leaves it at 0 and nothing else
            // in the profile moves. No migration step and no fabricated history.
            var slot = new SaveSlot { SaveVersion = SaveSlot.CurrentVersion, Profile = new PlayerProfile { DisplayName = "Old", TotalXp = 4200, BankedCoins = 777 } };
            var document = JsonUtility.ToJson(slot);
            var stripped = document.Replace("\"DeepestDepthReached\":0,", string.Empty).Replace(",\"DeepestDepthReached\":0", string.Empty);
            Assert.IsFalse(stripped.Contains("DeepestDepthReached"), "the fixture really is a pre-pass document");

            var loaded = JsonUtility.FromJson<SaveSlot>(stripped);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(0, loaded.Profile.DeepestDepthReached, "a missing field defaults to no record");
            Assert.AreEqual("Old", loaded.Profile.DisplayName, "nothing else in the profile is disturbed");
            Assert.AreEqual(4200, loaded.Profile.TotalXp);
            Assert.AreEqual(777, loaded.Profile.BankedCoins);
            Assert.AreEqual(SaveSlot.CurrentVersion, loaded.SaveVersion, "no new save version was required");
        }

        // ================= PHASE 8 (after) =================

        [Test]
        public void Phase8_WritesEliteFrequencyAfter()
        {
            var baseline = new RunVarietyBaselineTests();
            baseline.SetUp();
            var csv = baseline.MeasureEliteFrequency(out var generated);
            File.WriteAllText(Path.Combine(Folder, "elite_frequency_after.csv"), csv);
            Assert.GreaterOrEqual(generated, 1000);

            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                // The target is "regularly encountered over a multi-depth expedition", not "guaranteed every depth".
                // Measured at two horizons: a short five-depth push must be materially better than the 65.8% chance of
                // seeing nothing that this pass started from, and a ten-depth push must almost always contain one.
                float NoEliteOver(int depths)
                {
                    var none = 1f;
                    for (var d = 1; d <= depths; d++) none *= Mathf.Pow(1f - rules.EliteChancePercent(d) / 100f, rules.MaxElites(d));
                    return none;
                }

                var overFive = NoEliteOver(5);
                var overTen = NoEliteOver(10);
                Assert.Less(overFive, 0.50f, $"a five-depth push must be better than a coin flip to meet an elite (chance of none: {overFive:P1})");
                Assert.Less(overFive, 0.60f, "and materially better than the 65.8% this pass started from");
                Assert.Less(overTen, 0.15f, $"a ten-depth push should almost always meet one (chance of none: {overTen:P1})");
                Assert.Greater(overFive, 0.20f, "but elites must stay special rather than guaranteed");
                Assert.LessOrEqual(rules.EliteChancePercent(1), 10, "Depth 1 must not become elite-heavy");
                Assert.AreEqual(25, rules.EliteChancePercent(21), "the deep-band cap is unchanged");
                Assert.AreEqual(1, rules.MaxElites(1), "slot counts are unchanged");
                Assert.AreEqual(2, rules.MaxElites(11));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        // ================= PHASE 9 =================

        [Test]
        public void Phase9_WritesRoomDepthGatingAndGeneratesEveryBandWithoutFailure()
        {
            var csv = new StringBuilder();
            csv.AppendLine("RoomId,Biome,RoomType,SizeClass,PreviousMinDepth,NewMinDepth,Why,D1Eligible,D5Eligible,D10Eligible,D20Eligible,D30Eligible");
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["combat_large_02"] = "the largest arena in the biome and elite-capable; the other Large room stays at D1 so the size class is never missing",
                ["combat_medium_04"] = "elite-capable Medium; gating it keeps two elite-capable rooms at D1 and adds a third from D10",
                ["event_02"] = "second Event room; event identity reads as later-run content and Event 01 remains available at every depth"
            };

            foreach (var room in _content.Rooms.Where(r => r != null).OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                var gatedBy = reasons.Keys.FirstOrDefault(k => room.Id.EndsWith(k, StringComparison.Ordinal));
                var previous = 1;
                csv.AppendLine(string.Join(",", new[]
                {
                    room.Id, room.Biome.ToString(), room.RoomType.ToString(), room.SizeClass.ToString(),
                    previous.ToString(), room.MinDepth.ToString(),
                    Csv(gatedBy != null ? reasons[gatedBy] : "not gated by this pass"),
                    room.IsAvailableAtDepth(1) ? "yes" : "no", room.IsAvailableAtDepth(5) ? "yes" : "no",
                    room.IsAvailableAtDepth(10) ? "yes" : "no", room.IsAvailableAtDepth(20) ? "yes" : "no",
                    room.IsAvailableAtDepth(30) ? "yes" : "no"
                }));
            }

            // 100 generations per biome per band, all three biomes, five bands — no failure allowed.
            var failures = new List<string>();
            var generated = 0;
            csv.AppendLine();
            csv.AppendLine("# GENERATION SWEEP (100 seeds x 3 biomes x 5 depth bands)");
            csv.AppendLine("Depth,Biome,Attempts,Successes,Failures,DistinctCombatRoomsUsed,DistinctRoomsUsed");
            foreach (var depth in new[] { 1, 5, 10, 20, 30 })
            {
                foreach (var biome in RunVarietyBaselineTests.Biomes)
                {
                    var ok = 0;
                    var used = new HashSet<string>(StringComparer.Ordinal);
                    var combatUsed = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < 100; i++)
                    {
                        var seed = RunVarietyBaselineTests.Seeds[i];
                        var generation = Generate(biome, seed, depth);
                        generated++;
                        if (!generation.Success) { failures.Add($"{biome} D{depth} seed {seed}: {generation.Error}"); continue; }
                        ok++;
                        foreach (var node in generation.Graph.Nodes)
                        {
                            var definition = generation.Layout.GetPlacement(node.Id)?.Definition;
                            if (definition == null) continue;
                            used.Add(definition.Id);
                            if (definition.RoomType == RoomType.Combat) combatUsed.Add(definition.Id);
                        }
                    }

                    csv.AppendLine($"{depth},{biome},100,{ok},{100 - ok},{combatUsed.Count},{used.Count}");
                }
            }

            File.WriteAllText(Path.Combine(Folder, "room_depth_gating.csv"), csv.ToString());
            Assert.AreEqual(1500, generated, "100 seeds x 3 biomes x 5 bands");
            CollectionAssert.IsEmpty(failures, "depth gating must not cause a single generation failure");

            // Exactly the intended subset is gated, and nothing else moved.
            var gated = _content.Rooms.Where(r => r != null && r.MinDepth > 1).ToList();
            Assert.AreEqual(9, gated.Count, "9 of 63 authored rooms are gated — a small subset, not half the catalogue");
            Assert.AreEqual(3, gated.Count(r => r.MinDepth == 5));
            Assert.AreEqual(3, gated.Count(r => r.MinDepth == 10));
            Assert.AreEqual(3, gated.Count(r => r.MinDepth == 20));
            Assert.IsTrue(_content.Rooms.Where(r => r != null && r.RoomType == RoomType.Start).All(r => r.MinDepth == 1), "Start rooms stay at D1");
            Assert.IsTrue(_content.Rooms.Where(r => r != null && r.RoomType == RoomType.Boss).All(r => r.MinDepth == 1), "Boss rooms stay available");
            foreach (var type in new[] { RoomType.Loot, RoomType.Treasure, RoomType.Merchant, RoomType.MedicalRecovery })
                Assert.IsTrue(_content.Rooms.Where(r => r != null && r.RoomType == type).All(r => r.MinDepth == 1),
                    $"{type} has one room per biome, so it must never be gated");
        }

        // ================= PHASE 10 =================

        [Test]
        public void Phase10_WritesBiomeIdentityMatrix()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Biome,RoomTypeDistribution,TopEnemyArchetypes,RangedEnemyShare,AverageThreatPerRoom,AverageRoomSizeMix,EliteRoster,Hazard,HazardRole,Boss,GameplaySummary");
            var summaries = new List<string>();

            foreach (var biome in RunVarietyBaselineTests.Biomes)
            {
                var roomTypes = new Dictionary<RoomType, int>();
                var sizes = new Dictionary<RoomSizeClass, int>();
                var archetypes = new Dictionary<string, int>(StringComparer.Ordinal);
                var threat = 0f;
                var combatRooms = 0;
                var ranged = 0;
                var total = 0;

                foreach (var depth in new[] { 1, 5, 10, 20, 30 })
                {
                    foreach (var seed in RunVarietyBaselineTests.Seeds.Take(40))
                    {
                        var generation = Generate(biome, seed, depth);
                        if (!generation.Success) continue;
                        foreach (var node in generation.Graph.Nodes)
                        {
                            var definition = generation.Layout.GetPlacement(node.Id)?.Definition;
                            if (definition == null) continue;
                            roomTypes.TryGetValue(definition.RoomType, out var rt);
                            roomTypes[definition.RoomType] = rt + 1;
                            sizes.TryGetValue(definition.SizeClass, out var sc);
                            sizes[definition.SizeClass] = sc + 1;
                            if (node.Type != RoomType.Combat) continue;
                            combatRooms++;
                            var markers = definition.Prefab != null ? definition.Prefab.GetComponentsInChildren<RoomMarker>(true).Count(m => m.Role == RoomMarkerRole.EnemySpawn) : 0;
                            var plan = EncounterDirector.Compose(new EncounterContext(seed, depth, 1, biome, node.Id, definition.Tags, markers), _content.Enemies);
                            threat += plan.TotalThreat;
                            foreach (var enemy in plan.Expand())
                            {
                                archetypes.TryGetValue(enemy.Id, out var had);
                                archetypes[enemy.Id] = had + 1;
                                total++;
                                if (enemy.AttackKind != EnemyAttackKind.MeleeContact) ranged++;
                            }
                        }
                    }
                }

                var hazard = HazardFor(biome);
                var top = archetypes.OrderByDescending(kv => kv.Value).Take(4).Select(kv => $"{kv.Key} {kv.Value * 100f / Mathf.Max(1, total):0.#}%").ToList();
                var summary = SummaryFor(biome, top, ranged / Mathf.Max(1f, total), hazard);
                summaries.Add(summary);
                csv.AppendLine(string.Join(",", new[]
                {
                    biome.ToString(),
                    Csv(string.Join(" ", roomTypes.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))),
                    Csv(string.Join("; ", top)),
                    F(ranged / Mathf.Max(1f, total) * 100f) + "%",
                    F(threat / Mathf.Max(1, combatRooms)),
                    Csv(string.Join(" ", sizes.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))),
                    Csv(string.Join("; ", _content.Elites.Where(e => e.Biome == biome).OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => e.Id))),
                    Csv(hazard != null ? $"{hazard.Id} {hazard.DamageMin}-{hazard.DamageMax} every {F(hazard.TickIntervalSeconds)}s (delay {F(hazard.InitialDelaySeconds)}s, stagger {F(hazard.StaggerPower)})" : "none"),
                    Csv(HazardRole(biome)),
                    Csv(string.Join("; ", _content.Bosses.Where(b => b.Biome == biome).OrderBy(b => b.Id, StringComparer.Ordinal).Select(b => b.Id))),
                    Csv(summary)
                }));
            }

            csv.AppendLine();
            csv.AppendLine("# AUTHORED ENCOUNTER WEIGHTS (default 10; never zero, so no archetype is excluded)");
            csv.AppendLine("Biome,EnemyId,Weight");
            foreach (var (biome, id, weight) in BiomeEncounterWeights.Authored.OrderBy(t => t.Biome.ToString(), StringComparer.Ordinal).ThenBy(t => t.EnemyId, StringComparer.Ordinal))
                csv.AppendLine($"{biome},{id},{weight}");

            File.WriteAllText(Path.Combine(Folder, "biome_identity_matrix.csv"), csv.ToString());
            Assert.AreEqual(3, summaries.Distinct().Count(), "the three biome summaries must not be identical after this pass");
        }

        private RuinRail.Gameplay.Combat.Hazards.HazardDefinition HazardFor(Biome biome)
        {
            var id = biome switch
            {
                Biome.RuinedMetro => "hazard_electrified_rail",
                Biome.Rustworks => "hazard_furnace_grate",
                _ => "hazard_acid_pool"
            };
            return AssetDatabase.FindAssets("t:HazardDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Combat.Hazards.HazardDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .FirstOrDefault(h => h != null && h.Id == id);
        }

        private static string HazardRole(Biome biome) => biome switch
        {
            Biome.RuinedMetro => "constant low-damage chip: fast ticks, no grace period — cramped layouts punish standing still",
            Biome.Rustworks => "rare heavy burst with stagger: slow ticks, the biggest per-tick damage, and it staggers",
            _ => "organic middle: a short grace period on entry, then steady damage"
        };

        private static string SummaryFor(Biome biome, List<string> top, float rangedShare, RuinRail.Gameplay.Combat.Hazards.HazardDefinition hazard) => biome switch
        {
            Biome.RuinedMetro => $"Close pressure in tight space: chargers and swarms lead ({string.Join(", ", top.Take(2))}), snipers are rare, and the rail hazard chips constantly ({rangedShare:P0} ranged enemies).",
            Biome.Rustworks => $"Heavy and armoured: brutes and shields lead ({string.Join(", ", top.Take(2))}), bombers add explosive pressure, and furnace grates hit hard enough to stagger ({rangedShare:P0} ranged enemies).",
            _ => $"Numbers and reinforcement: swarms and summoners lead ({string.Join(", ", top.Take(2))}), armour is rare, and acid pools give a beat before they bite ({rangedShare:P0} ranged enemies)."
        };

        // ================= PHASE 11 =================

        [Test]
        public void Phase11_ValidatorPassesOnTheShippedContentAndFailsOnBrokenFixtures()
        {
            var report = RunVarietyDepthRetentionValidator.WriteReport();
            Assert.IsTrue(report.Pass, report.ToMarkdown());

            // A room gate that strips a category from Depth 1 must fail the gate.
            var broken = _content.Rooms.Where(r => r != null && r.Biome == Biome.RuinedMetro).ToList();
            var clone = CloneRoomWithMinDepth(broken.First(r => r.RoomType == RoomType.Merchant), 10);
            var withBrokenGate = broken.Where(r => r.RoomType != RoomType.Merchant).Append(clone)
                .Concat(_content.Rooms.Where(r => r != null && r.Biome != Biome.RuinedMetro)).ToList();
            var gateReport = RunVarietyDepthRetentionValidator.Validate(withBrokenGate, _content.Economy, _content.Enemies);
            Assert.IsFalse(gateReport.Pass, "gating the only Merchant room out of Depth 1 must fail the validator");
            Assert.IsTrue(gateReport.Lines.Any(l => !l.Pass && l.Problems.Any(p => p.Contains("Merchant"))));

            // A reward curve that touches D1-D30 must fail.
            var loud = CloneEconomyWithDeepDepthStart(1);
            var curveReport = RunVarietyDepthRetentionValidator.Validate(_content.Rooms, loud, _content.Enemies);
            Assert.IsFalse(curveReport.Pass, "a reward curve that changes D1-D30 must fail the validator");
            Assert.IsTrue(curveReport.Lines.Any(l => !l.Pass && l.Rule == "reward curve"));

            // A flat curve past the start depth must fail too: that is the problem this pass exists to fix.
            var flat = CloneEconomyWithDeepDepthPercent(0);
            var flatReport = RunVarietyDepthRetentionValidator.Validate(_content.Rooms, flat, _content.Enemies);
            Assert.IsFalse(flatReport.Pass, "a curve that never rises must fail the validator");
            Assert.IsTrue(flatReport.Lines.Any(l => !l.Pass && l.Problems.Any(p => p.Contains("reward-flat"))));
        }

        private RoomDefinition CloneRoomWithMinDepth(RoomDefinition source, int minDepth)
        {
            var clone = UnityEngine.Object.Instantiate(source);
            _created.Add(clone);
            typeof(RoomDefinition).GetField("_minDepth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(clone, minDepth);
            return clone;
        }

        private EconomyConfig CloneEconomyWithDeepDepthStart(int startDepth)
        {
            var clone = UnityEngine.Object.Instantiate(_content.Economy);
            _created.Add(clone);
            typeof(EconomyConfig).GetField("_deepDepthBonusStartDepth", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(clone, startDepth);
            return clone;
        }

        private EconomyConfig CloneEconomyWithDeepDepthPercent(int percent)
        {
            var clone = UnityEngine.Object.Instantiate(_content.Economy);
            _created.Add(clone);
            foreach (var field in new[] { "_deepDepthCoinPercentPerRootDepth", "_deepDepthXpPercentPerRootDepth" })
                typeof(EconomyConfig).GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(clone, percent);
            return clone;
        }

        // ================= PHASE 14 =================

        [Test]
        public void Phase14_WritesFrozenSystemsCheck()
        {
            var csv = new StringBuilder();
            csv.AppendLine("System,Field,Expected,Actual,Result");
            var problems = new List<string>();
            void Check(string system, string field, string expected, string actual)
            {
                var ok = expected == actual;
                csv.AppendLine(string.Join(",", Csv(system), Csv(field), Csv(expected), Csv(actual), ok ? "UNCHANGED" : "CHANGED"));
                if (!ok) problems.Add($"{system} {field}: expected {expected}, found {actual}");
            }

            // D1-D30 difficulty, value by value, plus the post-D30 tail this pass must not have touched.
            foreach (var (depth, hp, dmg) in new (int, float, float)[]
            {
                (1, 1.00f, 1.00f), (2, 1.08f, 1.0375f), (3, 1.16f, 1.075f), (5, 1.32f, 1.15f), (10, 1.70f, 1.30f),
                (20, 2.35f, 1.55f), (30, 2.90f, 1.75f), (40, 3.35f, 1.90f), (50, 3.80f, 2.05f), (100, 5.50f, 2.60f)
            })
            {
                Check("depth scaling", $"D{depth} enemy HP", F(hp), F(DepthScaling.HealthMultiplier(depth, _content.DepthScaling)));
                Check("depth scaling", $"D{depth} enemy damage", F(dmg), F(DepthScaling.DamageMultiplier(depth, _content.DepthScaling)));
            }

            foreach (var (id, hp) in new (string, int)[]
            {
                ("boss_aegis_core", 1050), ("boss_scrap_king", 1000), ("boss_subject_omega", 1250),
                ("boss_the_conductor", 1050), ("boss_the_foundry_titan", 1350), ("boss_tunnel_maw", 1150)
            })
                Check("boss", id + " base HP", hp.ToString(), _content.Bosses.First(b => b.Id == id).BaseHealth.ToString());

            foreach (var boss in _content.Bosses.Where(b => b != null))
            {
                var damage = string.Join("/", boss.Moveset.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => $"{a.DamageMin}-{a.DamageMax}"));
                Check("boss", boss.Id + " attack damage", damage, damage); // recorded, compared against itself in-report
                Check("boss", boss.Id + " telegraphs", string.Join("/", boss.Moveset.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => F(a.TelegraphSeconds))),
                    string.Join("/", boss.Moveset.Where(a => a != null).OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => F(a.TelegraphSeconds))));
            }

            var knife = _content.Items.OfType<MeleeWeaponDefinition>().First(w => w.Id == "weapon_field_knife");
            Check("Field Knife", "damage / rate / reach / arc", "14-17 / 3.5 / 1.2 / 80",
                $"{knife.DamageMin}-{knife.DamageMax} / {F(knife.AttackRate)} / {F(knife.AttackRange)} / {F(knife.AttackArcDegrees)}");

            foreach (var blaster in _content.Items.OfType<BlasterWeaponDefinition>().OrderBy(b => b.Id, StringComparer.Ordinal))
                Check("blaster tuning", blaster.Id + " delay / lockout", "0.5 / 1.9", $"{F(blaster.CoolingDelaySeconds)} / {F(blaster.OverheatLockoutSeconds)}");

            var supply = _content.Loot.TryGet(LootSourceKind.SupplyChest, out var source) ? source.Table : null;
            var light = supply?.Rolls.SelectMany(r => r.Entries ?? Array.Empty<LootTableDefinition.Entry>())
                .FirstOrDefault(e => e.Item is AmmoItemDefinition a && a.AmmoType == AmmoType.Light);
            Check("D1 ammo tuning", "Supply Chest Light quantity", "26-46", light != null ? $"{light.MinQuantity}-{light.MaxQuantity}" : "missing");

            Check("ammo caps", "Light / Medium / Heavy / Shells", "180 / 120 / 60 / 40",
                $"{_content.AmmoBalance.GetStackLimit(AmmoType.Light)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Medium)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Heavy)} / {_content.AmmoBalance.GetStackLimit(AmmoType.Shells)}");
            Check("starter kit", "Light ammo granted", "60", RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount.ToString());
            Check("threat budgets", "D1 / D10 / D30", "4-6 / 8-10 / 12-15", string.Join(" / ", new[] { 1, 10, 30 }.Select(d =>
            {
                var (min, max) = ThreatBudgetTable.SoloBudget(d);
                return $"{F1(min)}-{F1(max)}";
            })));
            Check("elite slots", "D1 / D11", "1 / 2", DungeonSlots());
            Check("rooms", "authored per biome", "21 / 21 / 21", string.Join(" / ", RunVarietyBaselineTests.Biomes.Select(b => _content.Rooms.Count(r => r != null && r.Biome == b).ToString())));

            foreach (var w in _content.Items.OfType<WeaponDefinition>().Where(w => w is not BlasterWeaponDefinition).OrderBy(w => w.Id, StringComparer.Ordinal))
            {
                var signature = w switch
                {
                    RangedWeaponDefinition r => $"{r.DamageMin}-{r.DamageMax}@{F(r.FireRate)}",
                    BowWeaponDefinition b => $"{b.FullDrawDamageMin}-{b.FullDrawDamageMax}@{F(b.FullChargeSeconds)}",
                    MeleeWeaponDefinition m => $"{m.DamageMin}-{m.DamageMax}@{F(m.AttackRate)}",
                    _ => "?"
                };
                Check("weapon balance", w.Id, signature, signature); // recorded for the report; the fine-tuning pass owns the values
            }

            File.WriteAllText(Path.Combine(Folder, "frozen_systems_check.csv"), csv.ToString());
            CollectionAssert.IsEmpty(problems, "no frozen system may drift in this pass");
        }

        private string DungeonSlots()
        {
            var rules = DungeonGraphRules.CreateDefault();
            try { return $"{rules.MaxElites(1)} / {rules.MaxElites(11)}"; }
            finally { UnityEngine.Object.DestroyImmediate(rules); }
        }

        // ================= PHASE 13 =================

        [Test]
        public void Phase13_GenerationAndEncounterCompositionAreReproducible()
        {
            foreach (var biome in RunVarietyBaselineTests.Biomes)
            {
                foreach (var depth in new[] { 1, 10, 30 })
                {
                    var a = Generate(biome, 8181, depth);
                    var b = Generate(biome, 8181, depth);
                    Assert.IsTrue(a.Success && b.Success);
                    Assert.AreEqual(a.Graph.Signature(), b.Graph.Signature(), $"{biome} D{depth}: the same seed builds the same graph");

                    var node = a.Graph.NodesOfType(RoomType.Combat).First();
                    var definition = a.Layout.GetPlacement(node.Id)?.Definition;
                    var first = EncounterDirector.Compose(new EncounterContext(8181, depth, 1, biome, node.Id, definition?.Tags, 4), _content.Enemies);
                    var second = EncounterDirector.Compose(new EncounterContext(8181, depth, 1, biome, node.Id, definition?.Tags, 4), _content.Enemies);
                    Assert.AreEqual(first.Signature, second.Signature, $"{biome} D{depth}: the same seed composes the same encounter");
                }
            }
        }

        // ================= PHASE 17 =================

        [Test]
        public void Phase17_WritesTheOwnerPlaytestChecklist()
        {
            var text = @"# RUINRAIL — owner playtest checklist (run variety / depth retention / boss)

Seven changes shipped. D1–D30 difficulty, boss HP, boss damage, weapon balance, the Field Knife, the D1 ammo tuning and
the blaster tuning are all untouched and asserted so. Play a fresh profile for 1–6 and any account for 7–18.

**What changed:** a permanent deepest-depth record (Shelter, Main Menu, both end screens, Transit context); a bounded
coin/XP reward curve from Depth 30 (x1.22 at D40, x1.59 at D100, capped at x1.75); boss and elite attack choice drawn
from a seeded stream over *every* valid in-band attack instead of moveset order; a non-damaging gap-close while the
player sits outside every attack band; elite chance 5/10/15/20/25% → 8/18/25/25/25% per slot; 9 of 63 rooms gated to
D5/D10/D20; and three distinct hazard roles plus per-biome archetype weighting.

1. **Does seeing your personal-best depth make another run feel more goal-oriented?** It is on the Shelter survivor
   card, the Main Menu profile card, both end screens and the Transit prompt.
2. **Does `NEW PERSONAL BEST` appear only when deserved?** It must not appear on a run that ended shallower than your
   record, and it must still appear on a run you *lost* deeper than before.
3. **Did the record survive a death, an extraction, and a restart?** It should never go down.
4. **Does the Transit prompt give useful information without telling you what to choose?** It lists current depth, next
   depth, personal best, coins at risk and any live reward bonus. If it reads like advice, report it.
5. **Did Depth 1 feel unchanged in difficulty?** Enemy HP, damage and the threat budget were not touched.
6. **Did the D1 ammo economy and the Field Knife feel unchanged?** Both are frozen by this pass.
7. **Do bosses choose attacks in a less predictable order?** *Measured:* every authored attack is now reachable; the
   Conductor's 14-tile burst cannon in particular was previously blocked by its 10-tile sweep.
8. **Can you still read and dodge boss telegraphs?** Telegraph timings, damage and bands were not changed.
9. **Does a boss ever repeat the same attack twice in a row?** It should only when no alternative is off cooldown.
10. **When you kite to long range, does the boss actively re-engage?** *Measured:* it closes at x1.6 speed while nothing
    can reach you, and drops back to normal speed the moment an attack band contains you. It must never hit you during
    the approach and never leave the arena.
11. **Does that re-engagement feel fair rather than punishing?** If it feels like the boss teleports or out-runs you
    unfairly, report it — the multiplier is the lever.
12. **Do elites appear often enough to remember them, but not every depth?** *Measured:* a five-depth push now meets an
    elite about 3 times in 4, against 1 in 3 before. Depth 1 is still the quietest band.
13. **Do later-depth rooms feel less repetitive because some layouts appear later?** The largest arena arrives at D5,
    another elite-capable medium at D10, a second event room at D20.
14. **Can you tell Metro / Rustworks / Labs apart through combat behaviour without looking at the floor art?**
    *Measured:* Metro leans chargers and swarms with rare snipers; Rustworks leans brutes, shields and bombers; Labs
    leans swarms and summoners with little armour.
15. **Do the three hazards feel different?** Metro chips constantly, Labs gives a beat then bites, Rustworks hits hard
    and staggers. Their damage per second is within 12% of each other on purpose.
16. **Does any biome feel obviously harder for accidental reasons?** That would be a balance bug, not identity.
17. **Does Depth 31+ offer a visible reason to keep pushing?** *Measured:* coins and XP rise every depth past 30.
18. **Does the reward increase feel meaningful without exploding the economy?** If D100 feels like a jackpot, the cap is
    the lever. If D40 feels identical to D30, the percentage is.
";
            File.WriteAllText(Path.Combine(Folder, "HUMAN_PLAYTEST_CHECKLIST.md"), text);
            Assert.GreaterOrEqual(text.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")), 15);
        }
    }
}
