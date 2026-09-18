using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 071 — deterministic encounter composition from depth, party size, unlocks, tags and caps.</summary>
    public class EncounterDirectorTests
    {
        private List<EnemyDefinition> _archetypes;

        [SetUp]
        public void SetUp()
        {
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .ToList();
            Assert.AreEqual(9, _archetypes.Count, "44: exactly nine normal archetypes.");
        }

        [Test]
        public void ThreatCosts_MatchCombat47_AndBudgetsMatchDepthScaling59()
        {
            var costs = _archetypes.ToDictionary(a => a.Id, a => a.ThreatCost);
            Assert.AreEqual(0.5f, costs["swarm"]);
            Assert.AreEqual(1f, costs["grunt"]);
            Assert.AreEqual(1f, costs["shooter"]);
            Assert.AreEqual(2f, costs["charger"]);
            Assert.AreEqual(2f, costs["bomber"]);
            Assert.AreEqual(2f, costs["shield_enemy"]);
            Assert.AreEqual(2.5f, costs["sniper_enemy"]);
            Assert.AreEqual(3f, costs["brute"]);
            Assert.AreEqual(3f, costs["summoner"]);

            Assert.AreEqual((4f, 6f), ThreatBudgetTable.SoloBudget(1));
            Assert.AreEqual((5f, 7f), ThreatBudgetTable.SoloBudget(3));
            Assert.AreEqual((6f, 8f), ThreatBudgetTable.SoloBudget(5));
            Assert.AreEqual((8f, 10f), ThreatBudgetTable.SoloBudget(10));
            Assert.AreEqual((10f, 13f), ThreatBudgetTable.SoloBudget(20));
            Assert.AreEqual((12f, 15f), ThreatBudgetTable.SoloBudget(30));
            Assert.AreEqual((14f, 18f), ThreatBudgetTable.SoloBudget(50));
            Assert.AreEqual((14f, 18f), ThreatBudgetTable.SoloBudget(100), "Capped, never growing forever.");
            var (min2, max2) = ThreatBudgetTable.SoloBudget(2);
            Assert.AreEqual(4.5f, min2, 0.001f, "Interpolated between anchors.");
            Assert.AreEqual(6.5f, max2, 0.001f);

            Assert.AreEqual((1f, 1.4f, 1.75f), (PartyScaling.ThreatMultiplier(1), PartyScaling.ThreatMultiplier(2), PartyScaling.ThreatMultiplier(3)));
            Assert.AreEqual((10, 14, 18), (PartyScaling.ActiveNormalCap(1), PartyScaling.ActiveNormalCap(2), PartyScaling.ActiveNormalCap(3)));
            Assert.AreEqual(1, PartyScaling.ActiveEliteCap);
            Assert.AreEqual(1f, PartyScaling.EnemyDamageMultiplier(3), "83: damage per hit never scales with party size.");
        }

        // ---- Acceptance 1 + 2: budgets, unlocks and caps hold over many seeds/depths ----

        [Test]
        public void Budgets_Unlocks_AndCaps_HoldOverManySeedsAndDepths()
        {
            var checkedPlans = 0;
            foreach (var depth in new[] { 1, 2, 3, 4, 5, 7, 9, 11, 14, 20, 30, 50, 80 })
            {
                foreach (var party in new[] { 1, 2, 3 })
                {
                    for (var seed = 1; seed <= 25; seed++)
                    {
                        var context = new EncounterContext(seed * 7919, depth, party, Biome.RuinedMetro, roomIndex: seed % 5);
                        var plan = EncounterDirector.Compose(context, _archetypes);
                        checkedPlans++;
                        var (min, max) = ThreatBudgetTable.SoloBudget(depth);
                        var multiplier = PartyScaling.ThreatMultiplier(party);
                        Assert.GreaterOrEqual(plan.TargetThreat, min * multiplier - 0.001f, $"d{depth} p{party} s{seed}");
                        Assert.LessOrEqual(plan.TargetThreat, max * multiplier + 0.001f, $"d{depth} p{party} s{seed}");
                        Assert.LessOrEqual(plan.TotalThreat, plan.TargetThreat + 0.001f, "Never over budget.");
                        Assert.Greater(plan.TotalThreat, 0f, "Never an empty combat room.");
                        Assert.LessOrEqual(plan.TotalCount, PartyScaling.ActiveNormalCap(party), "Active cap.");
                        Assert.IsTrue(plan.Entries.All(e => e.Definition.UnlockDepth <= depth), $"Locked archetype at depth {depth}: {plan.Signature}");
                        Assert.IsTrue(plan.RoleCount >= 1 && plan.RoleCount <= EncounterDirector.MaxRoles);
                        // The fill stops only when nothing cheaper fits: budget is used well.
                        var cheapest = plan.Entries.Min(e => e.Definition.ThreatCost);
                        Assert.IsTrue(plan.TotalThreat + cheapest > plan.TargetThreat || plan.TotalCount == plan.ActiveCap, "Budget filled or cap reached.");
                    }
                }
            }

            Assert.AreEqual(13 * 3 * 25, checkedPlans);
        }

        [Test]
        public void LockedArchetypes_NeverAppearEarly_AndAppearOnceUnlocked()
        {
            var seenAt = new Dictionary<int, HashSet<string>>();
            foreach (var depth in new[] { 1, 2, 3, 5, 7, 9, 11, 14, 25 })
            {
                seenAt[depth] = new HashSet<string>();
                for (var seed = 0; seed < 300; seed++)
                {
                    var plan = EncounterDirector.Compose(new EncounterContext(seed, depth, 1, Biome.Rustworks, seed % 7), _archetypes);
                    foreach (var e in plan.Entries) seenAt[depth].Add(e.Definition.Id);
                }
            }

            CollectionAssert.IsSubsetOf(seenAt[1], new[] { "grunt", "shooter", "swarm" });
            CollectionAssert.IsSubsetOf(seenAt[2], new[] { "grunt", "shooter", "swarm" });
            Assert.IsFalse(seenAt[2].Contains("charger"));
            Assert.IsTrue(seenAt[3].Contains("charger"), "Charger from Depth 3.");
            Assert.IsFalse(seenAt[3].Contains("brute"));
            Assert.IsTrue(seenAt[5].Contains("brute"), "Brute from Depth 5.");
            Assert.IsTrue(seenAt[7].Contains("bomber") && !seenAt[5].Contains("bomber"), "Bomber from Depth 7.");
            Assert.IsTrue(seenAt[9].Contains("shield_enemy") && !seenAt[7].Contains("shield_enemy"), "Shield Enemy from Depth 9.");
            Assert.IsTrue(seenAt[11].Contains("sniper_enemy") && !seenAt[9].Contains("sniper_enemy"), "Sniper from Depth 11.");
            Assert.IsTrue(seenAt[14].Contains("summoner") && !seenAt[11].Contains("summoner"), "Summoner from Depth 14.");
            Assert.AreEqual(9, seenAt[25].Count, "Deep rooms draw from the full roster.");
        }

        // ---- Acceptance 3: party multiplier changes pressure, not per-hit damage ----

        [Test]
        public void PartySize_RaisesThreat_WithoutTouchingDamageBands()
        {
            var soloThreat = 0f;
            var trioThreat = 0f;
            for (var seed = 0; seed < 100; seed++)
            {
                soloThreat += EncounterDirector.Compose(new EncounterContext(seed, 10, 1, Biome.OvergrownLabs, 0), _archetypes).TargetThreat;
                trioThreat += EncounterDirector.Compose(new EncounterContext(seed, 10, 3, Biome.OvergrownLabs, 0), _archetypes).TargetThreat;
            }

            Assert.AreEqual(1.75f, trioThreat / soloThreat, 0.03f, "Trio budget = 175% of solo on average.");
            var duo = EncounterDirector.Compose(new EncounterContext(5, 10, 2, Biome.OvergrownLabs, 0), _archetypes);
            Assert.AreEqual(14, duo.ActiveCap);
            Assert.AreEqual(1.2f, duo.NormalEnemyHealthMultiplier, 0.001f);
            foreach (var entry in duo.Entries)
            {
                var authored = _archetypes.Single(a => a.Id == entry.Definition.Id);
                Assert.AreSame(authored, entry.Definition, "The plan references the authored definition: damage bands are untouched.");
            }
        }

        // ---- Acceptance 4: determinism ----

        [Test]
        public void SameSeedAndContext_ProduceTheSamePlan_DifferentRoomsOrSeedsDiffer()
        {
            var a = EncounterDirector.Compose(new EncounterContext(12345, 12, 2, Biome.RuinedMetro, 3, new[] { "combat" }), _archetypes);
            var b = EncounterDirector.Compose(new EncounterContext(12345, 12, 2, Biome.RuinedMetro, 3, new[] { "combat" }), _archetypes);
            Assert.AreEqual(a.Signature, b.Signature);
            Assert.AreEqual(a.TargetThreat, b.TargetThreat);

            var signatures = Enumerable.Range(0, 40).Select(room => EncounterDirector.Compose(new EncounterContext(12345, 12, 2, Biome.RuinedMetro, room), _archetypes).Signature).Distinct().Count();
            Assert.Greater(signatures, 10, "Rooms of one depth differ.");
            var seeds = Enumerable.Range(0, 40).Select(seed => EncounterDirector.Compose(new EncounterContext(seed, 12, 2, Biome.RuinedMetro, 3), _archetypes).Signature).Distinct().Count();
            Assert.Greater(seeds, 10, "Seeds differ.");

            // The encounter stream is separate: consuming the dungeon stream never changes the encounter plan.
            var before = EncounterDirector.Compose(new EncounterContext(777, 8, 1, Biome.Rustworks, 1), _archetypes).Signature;
            var dungeon = RuinRail.Core.Rng.RngStreams.Derive(777, 8, RuinRail.Core.Rng.RngStream.Dungeon);
            for (var i = 0; i < 50; i++) dungeon.NextInt(100);
            Assert.AreEqual(before, EncounterDirector.Compose(new EncounterContext(777, 8, 1, Biome.Rustworks, 1), _archetypes).Signature);
        }

        [Test]
        public void RoomTags_ExcludeArchetypes_AndRolesMixWhenTheBudgetAllows()
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var plan = EncounterDirector.Compose(new EncounterContext(seed, 20, 1, Biome.RuinedMetro, 0, new[] { "no_ranged", "no_summoner" }), _archetypes);
                Assert.IsTrue(plan.Entries.All(e => !e.Definition.SpawnTags.Contains("ranged") && e.Definition.Id != "summoner"), plan.Signature);
            }

            var mixed = 0;
            for (var seed = 0; seed < 100; seed++)
            {
                if (EncounterDirector.Compose(new EncounterContext(seed, 20, 1, Biome.RuinedMetro, 0), _archetypes).RoleCount >= 2) mixed++;
            }

            Assert.Greater(mixed, 80, "47: combat rooms usually mix 2-4 roles.");
        }
    }
}
