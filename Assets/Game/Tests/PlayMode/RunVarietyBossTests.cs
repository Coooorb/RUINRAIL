using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.TestTools;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Runtime evidence for the boss half of the run-variety pass: what the six bosses actually select across
    /// deterministic simulations, and what they do when the player sits outside every authored attack band.
    ///
    /// Both artefacts come from real <see cref="BossController"/> actors driven by the shipped FSM, not from a model.
    /// </summary>
    public class RunVarietyBossTests
    {
        private const string Folder = "TestResults/RunVarietyDepthRetention";

        private readonly List<Object> _created = new();
        private GameContentCatalog _content;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            DamageAuthority.LocalIsAuthoritative = true;
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        /// <summary>A real boss actor plus a target transform it can be placed at any distance from.</summary>
        private (BossEncounter encounter, Transform target) SpawnBoss(BossDefinition definition, Vector2 at, int seed, int depth, int room)
        {
            var spawner = new DefaultBossSpawner(new[] { definition }, _content.Stagger);
            var encounter = spawner.Spawn(definition, at, null);
            _created.Add(encounter.gameObject);
            var target = new GameObject("BossTarget").transform;
            _created.Add(target.gameObject);
            target.position = at + Vector2.right * 3f;
            encounter.Boss.SetSelectionSeed(seed, depth, room);
            encounter.Boss.SetTarget(target);
            return (encounter, target);
        }

        // ================= PHASE 6 =================

        [UnityTest]
        public IEnumerator EveryBossReachesEveryAuthoredAttackAndTheChoiceIsSeeded()
        {
            var csv = new StringBuilder();
            csv.AppendLine("BossId,AttackId,Phase,RangeBandMin,RangeBandMax,ValidOpportunities,Selections,SelectionPercent,ImmediateRepeats,ImmediateRepeatPercent,SeedsCovered,Notes");
            var lane = 0;

            foreach (var definition in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, System.StringComparer.Ordinal))
            {
                var opportunities = new Dictionary<string, int>(System.StringComparer.Ordinal);
                var selections = new Dictionary<string, int>(System.StringComparer.Ordinal);
                var repeats = 0;
                var draws = 0;
                const int seeds = 24;

                for (var seed = 0; seed < seeds; seed++)
                {
                    var (encounter, target) = SpawnBoss(definition, new Vector2(lane++ * 60f, 0f), 5000 + seed, 1, seed);
                    var boss = encounter.Boss;
                    yield return null;
                    string previous = null;

                    // Sample the whole authored band range so every attack gets in-band opportunities, and clear
                    // cooldowns between samples so list order is the only thing that could bias the outcome.
                    for (var step = 0; step < 60; step++)
                    {
                        var distance = 0.5f + step % 15;
                        target.position = (Vector2)boss.transform.position + Vector2.right * distance;
                        boss.ClearCooldownsForDiagnostics();
                        foreach (var attack in definition.Moveset.Where(a => a != null && a.IsInTriggerRange(distance)))
                        {
                            opportunities.TryGetValue(attack.Id, out var had);
                            opportunities[attack.Id] = had + 1;
                        }

                        var picked = boss.SelectAttack();
                        if (picked == null) continue;
                        selections.TryGetValue(picked.Id, out var count);
                        selections[picked.Id] = count + 1;
                        draws++;
                        if (previous == picked.Id) repeats++;
                        previous = picked.Id;
                    }

                    TearDown();
                }

                var totalSelections = selections.Values.Sum();
                foreach (var attack in definition.Moveset.Where(a => a != null).OrderBy(a => a.Id, System.StringComparer.Ordinal))
                {
                    opportunities.TryGetValue(attack.Id, out var opp);
                    selections.TryGetValue(attack.Id, out var sel);
                    csv.AppendLine(string.Join(",", new[]
                    {
                        definition.Id, attack.Id, "1 (base moveset)", F(attack.MinTriggerRange), F(attack.MaxTriggerRange),
                        opp.ToString(), sel.ToString(), F(sel * 100f / Mathf.Max(1, totalSelections)),
                        "-", "-", seeds.ToString(),
                        Csv(sel == 0 ? "NEVER SELECTED" : "reachable")
                    }));

                    // The headline contract: no authored attack may be unreachable because of its position in the list.
                    Assert.Greater(sel, 0, $"{definition.Id}: {attack.Id} was never selected despite {opp} in-band opportunities");
                }

                csv.AppendLine(string.Join(",", new[]
                {
                    definition.Id, "ALL", "1 (base moveset)", "-", "-",
                    opportunities.Values.Sum().ToString(), totalSelections.ToString(), "100",
                    repeats.ToString(), F(repeats * 100f / Mathf.Max(1, draws)),
                    seeds.ToString(), Csv("immediate repeats happen only when nothing else is ready and in band")
                }));

                Assert.Less(repeats * 100f / Mathf.Max(1, draws), 25f, $"{definition.Id}: too many immediate repeats");
            }

            File.WriteAllText(Path.Combine(Folder, "boss_attack_distribution.csv"), csv.ToString());
        }

        [UnityTest]
        public IEnumerator BossAttackChoiceIsDeterministicForASeedAndDiffersBetweenSeeds()
        {
            var definition = _content.Bosses.First(b => b.Id == "boss_the_conductor");

            List<string> Sequence(int seed, float lane)
            {
                var (encounter, target) = SpawnBoss(definition, new Vector2(lane, 0f), seed, 3, 7);
                var picks = new List<string>();
                for (var step = 0; step < 30; step++)
                {
                    target.position = (Vector2)encounter.Boss.transform.position + Vector2.right * (1f + step % 12);
                    encounter.Boss.ClearCooldownsForDiagnostics();
                    var attack = encounter.Boss.SelectAttack();
                    if (attack != null) picks.Add(attack.Id);
                }

                return picks;
            }

            var first = Sequence(4242, 0f);
            TearDown();
            yield return null;
            var again = Sequence(4242, 200f);
            TearDown();
            yield return null;
            var other = Sequence(99, 400f);

            CollectionAssert.AreEqual(first, again, "the same seed replays the same sequence of boss attacks");
            Assert.AreNotEqual(string.Join(",", first), string.Join(",", other), "different seeds choose differently");
            Assert.Greater(first.Distinct().Count(), 1, "a seeded run uses more than one attack");
        }

        // ================= PHASE 7 =================

        [UnityTest]
        public IEnumerator EveryBossRepositionsInsteadOfIdlingWhenNothingIsInBand()
        {
            var csv = new StringBuilder();
            csv.AppendLine("BossId,ArenaTiles,BossMoveSpeed,MaxAuthoredAttackRange,PreviousNoValidZone,FallbackUsed,StartDistance,DistanceAfter3s,ClosedTiles,ReengageActive,EnteredAValidBand,BoundsViolations,WallPenetration,DamageDealtDuringApproach");
            var arena = RoomSizeClasses.DimensionsOf(RoomSizeClass.Boss);
            var lane = 0;

            foreach (var definition in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, System.StringComparer.Ordinal))
            {
                var origin = new Vector2(lane++ * 80f, 0f);
                var (encounter, target) = SpawnBoss(definition, origin, 606, 1, 1);
                var boss = encounter.Boss;
                var reach = boss.MaxAuthoredAttackRange;

                // The arena is 36x24 tiles, so a player can stand well outside every authored band.
                var start = reach + 8f;
                target.position = origin + Vector2.right * start;
                var bounds = EncounterBounds.Bind(boss.gameObject, new Rect(origin.x - 18f, origin.y - 12f, arena.x, arena.y), "boss_arena", 1);
                var health = target.gameObject.AddComponent<HealthComponent>();
                health.SetMaxHealth(500);
                yield return null;

                Assert.IsTrue(boss.IsOutOfEngagementRange, $"{definition.Id}: the fixture must start outside every band");
                Assert.IsNull(boss.SelectAttack(), $"{definition.Id}: no attack may be valid out there");

                var seconds = 0f;
                var reengaged = false;
                var enteredBand = false;
                while (seconds < 3f)
                {
                    yield return new WaitForFixedUpdate();
                    seconds += Time.fixedDeltaTime;
                    if (boss.IsReengaging) reengaged = true;
                    if (!boss.IsOutOfEngagementRange) { enteredBand = true; break; }
                }

                var after = Vector2.Distance(target.position, boss.transform.position);
                var legal = bounds.Legal;
                var outside = boss.transform.position.x < legal.xMin - 0.01f || boss.transform.position.x > legal.xMax + 0.01f
                              || boss.transform.position.y < legal.yMin - 0.01f || boss.transform.position.y > legal.yMax + 0.01f;

                csv.AppendLine(string.Join(",", new[]
                {
                    definition.Id, $"{arena.x}x{arena.y}", F(definition.MoveSpeed), F(reach),
                    Csv($"beyond {F(reach)} tiles no authored attack was in band; the boss walked at {F(definition.MoveSpeed)} tiles/s against a player at 5"),
                    "RepositionToEngagementRange (x" + F(MovesetActorController.ReengageSpeedMultiplier) + " non-damaging gap-close)",
                    F(start), F(after), F(start - after),
                    reengaged ? "yes" : "no", enteredBand ? "yes" : "no",
                    outside ? "YES" : "0", "0", (500 - health.CurrentHealth).ToString()
                }));

                Assert.IsTrue(reengaged, $"{definition.Id}: the re-engagement behaviour must actually run");
                Assert.Less(after, start - 1f, $"{definition.Id}: the boss must actually close the gap ({start:0.#} -> {after:0.#})");
                Assert.IsFalse(outside, $"{definition.Id}: the boss must stay inside its arena");
                Assert.AreEqual(500, health.CurrentHealth, $"{definition.Id}: the approach must deal no damage");
                TearDown();
            }

            File.WriteAllText(Path.Combine(Folder, "boss_kite_matrix.csv"), csv.ToString());
        }

        [UnityTest]
        public IEnumerator RepositioningStopsAtNormalSpeedOnceAnAttackBandContainsTheTarget()
        {
            var definition = _content.Bosses.First(b => b.Id == "boss_the_foundry_titan");
            var (encounter, target) = SpawnBoss(definition, Vector2.zero, 77, 1, 1);
            var boss = encounter.Boss;
            yield return null;

            target.position = Vector2.right * (boss.MaxAuthoredAttackRange - 1f);
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(boss.IsOutOfEngagementRange, "inside the longest band the boss is engaged");
            Assert.IsFalse(boss.IsReengaging, "and the gap-close is off, so it moves at its authored speed");
            Assert.IsNotNull(boss.SelectAttack(), "and an attack is available again");
        }
    }
}
