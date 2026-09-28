using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Enemy-vs-enemy crowding in the running game: a real Combat room in each biome with its own encounter plus an
    /// extra swarm pack and grunts, the player in the open, just inside a doorway, in a corner and behind an obstacle.
    /// For every pursuer blocked by another enemy's body it records how long, what the blocker was doing, and whether a
    /// free approach slot on the attack ring was available at the time ([CROWD]).
    /// </summary>
    public sealed class EnemyCrowdingLiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_crowd_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time"); yield return null; }
        }

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];

        /// <summary>The first solid thing between the enemy and the player: another enemy's body, geometry, or nothing.</summary>
        private static EnemyController BlockingEnemy(EnemyController e, Vector2 stand, out bool geometry)
        {
            geometry = false;
            var from = (Vector2)e.transform.position;
            var d = stand - from;
            var count = Physics2D.CircleCast(from, 0.33f, d.normalized, ContactFilter2D.noFilter, Hits, d.magnitude);
            var best = float.MaxValue;
            EnemyController blocker = null;
            for (var i = 0; i < count; i++)
            {
                var c = Hits[i].collider;
                if (c == null || c.isTrigger || c.transform.IsChildOf(e.transform) || Hits[i].distance >= best) continue;
                var other = c.GetComponentInParent<EnemyController>();
                if (other != null && other != e) { best = Hits[i].distance; blocker = other; geometry = false; }
                else if (c.GetComponentInParent<EnvironmentObstacle>() != null) { best = Hits[i].distance; blocker = null; geometry = true; }
            }

            return blocker;
        }

        /// <summary>Free approach slots on the enemy's attack ring around the player (no geometry, no other body, in the room, in straight view).</summary>
        private static int FreeSlots(EnemyController e, Vector2 stand, Rect legal, IReadOnlyList<EnemyController> all)
        {
            var radius = Mathf.Max(0.5f, e.Definition.AttackRange * 0.8f);
            var free = 0;
            for (var k = 0; k < 16; k++)
            {
                var angle = k * Mathf.PI * 2f / 16f;
                var slot = stand + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                if (!legal.Contains(slot) || !EnemyPursuitNavigationTests.Free(slot, 0.36f)) continue;
                if (all.Any(o => o != null && o != e && o.IsAlive && Vector2.Distance(o.transform.position, slot) < 0.7f)) continue;
                free++;
            }

            return free;
        }

        [UnityTest]
        public IEnumerator LiveRun_Crowds_NeverPinEnemiesBehindEachOther_WhenApproachSlotsAreFree(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            var content = GameContentCatalog.Load();
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Crowd Check");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var body = player.GetComponent<Rigidbody2D>();
            var candidates = run.Rooms.Values.Where(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered)
                .Select(r => (room: r, cases: EnemyPursuitNavigationTests.Cases(r.InteriorWorldBounds, DefaultEnemySpawner.BodyRadius + 0.05f, 1)))
                .Where(c => c.cases.Count > 0).ToList();
            Assert.Greater(candidates.Count, 0, biome + ": a Combat room with interior geometry");
            var (room, roomCases) = candidates.OrderByDescending(c => c.cases[0].route).First();
            var interior = room.InteriorWorldBounds;
            var legal = new Rect(interior.xMin + 0.36f, interior.yMin + 0.36f, interior.width - 0.72f, interior.height - 0.72f);
            var enemies = new List<EnemyController>();
            room.EnemySpawned += (_, e) => enemies.Add(e);

            IEnumerator Put(Vector2 p)
            {
                player.transform.position = p;
                body.position = p;
                body.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            }

            // The room's own encounter (the player enters) plus a swarm pack and grunts from its spawn markers.
            yield return Put(roomCases[0].to);
            Assert.Greater(enemies.Count, 0, "the room spawned its encounter");
            var spawns = room.SpawnPointsFor(roomCases[0].to);
            var extra = new[] { "swarm", "swarm", "swarm", "swarm", "swarm", "swarm", "grunt", "grunt" };
            for (var i = 0; i < extra.Length; i++)
            {
                var definition = content.Enemies.First(d => d.Id == extra[i]);
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, spawns[i % spawns.Count] + new Vector2((i % 3) * 0.4f, (i / 3) * 0.4f), player.transform);
                if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                room.BindEncounterBounds(enemy.gameObject);
                run.BindEnemyPresentation(enemy);
                enemies.Add(enemy);
            }

            var log = new StringBuilder();
            var failures = new List<string>();
            var results = new List<(string name, float longest, float coverage)>();
            var deaths = new Dictionary<EnemyController, string>();
            var lastPosition = new Dictionary<EnemyController, Vector2>();

            // Where each enemy's damage came from: everything it took, and the share from hazard floors.
            var damageTaken = new Dictionary<EnemyController, int>();
            var hazardDamage = new Dictionary<EnemyController, int>();
            var tracked = new HashSet<EnemyController>();
            void Track(EnemyController e)
            {
                if (e == null || !tracked.Add(e)) return;
                var h = e.GetComponent<HealthComponent>();
                if (h != null) h.Damaged += amount => damageTaken[e] = (damageTaken.TryGetValue(e, out var d) ? d : 0) + amount;
            }

            foreach (var e in enemies) Track(e);
            room.EnemySpawned += (_, e) => Track(e);
            foreach (var hazard in room.GetComponentsInChildren<RuinRail.Gameplay.Combat.Hazards.HazardVolume>(true))
                hazard.Ticked += (target, amount) =>
                {
                    var victim = (target as Component) != null ? ((Component)target).GetComponentInParent<EnemyController>() : null;
                    if (victim != null) hazardDamage[victim] = (hazardDamage.TryGetValue(victim, out var d) ? d : 0) + amount;
                };
            IEnumerator Scenario(string name, Vector2 stand, float seconds)
            {
                yield return Put(stand);
                var pressing = new Dictionary<EnemyController, float>();
                var worst = new Dictionary<EnemyController, (float t, string blocker)>();
                var inRange = new HashSet<EnemyController>();
                var coverageSum = 0f;
                var coverageSamples = 0;
                var start = Time.time;
                while (Time.time - start < seconds)
                {
                    health.Heal(100000);
                    body.position = stand; body.linearVelocity = Vector2.zero;
                    yield return new WaitForFixedUpdate();
                    foreach (var e in enemies.Where(e => e != null && !e.IsAlive && !deaths.ContainsKey(e)))
                    {
                        var at = lastPosition.TryGetValue(e, out var lp) ? lp : (Vector2)e.transform.position;
                        var hazard = Physics2D.OverlapPointAll(at).Select(c => c.GetComponentInParent<RuinRail.Gameplay.Combat.Hazards.HazardVolume>()).FirstOrDefault(h => h != null);
                        var took = damageTaken.TryGetValue(e, out var total) ? total : 0;
                        var fromHazards = hazardDamage.TryGetValue(e, out var hz) ? hz : 0;
                        deaths[e] = $"{e.Definition.Id} died at {at} during '{name}'{(hazard != null ? " inside hazard " + hazard.name : " (no hazard there)")}: took {took}, {fromHazards} from hazard ticks, {took - fromHazards} otherwise";
                    }

                    var alive = enemies.Where(e => e != null && e.IsAlive).ToList();
                    foreach (var e in alive) lastPosition[e] = e.transform.position;
                    foreach (var e in alive)
                    {
                        Assert.IsTrue(interior.Contains(e.transform.position), $"{name}: {e.Definition.Id} stayed in the room");
                        var position = (Vector2)e.transform.position;
                        var toPlayer = stand - position;
                        var distance = toPlayer.magnitude;
                        if (distance <= e.Definition.AttackRange + 0.4f) inRange.Add(e);
                        if (distance <= e.Definition.AttackRange + 0.4f || e.State != EnemyState.Chase) { pressing[e] = 0f; continue; }
                        // Pressing: not moving, touching another enemy's body on the side facing the player.
                        var still = e.GetComponent<Rigidbody2D>().linearVelocity.magnitude < 0.1f;
                        var front = alive.FirstOrDefault(o => o != e && Vector2.Distance(o.transform.position, position) < 0.8f && Vector2.Dot((Vector2)o.transform.position - position, toPlayer) > 0f);
                        pressing[e] = still && front != null ? (pressing.TryGetValue(e, out var p) ? p : 0f) + Time.fixedDeltaTime : 0f;
                        if (pressing[e] > (worst.TryGetValue(e, out var w) ? w.t : 0f)) worst[e] = (pressing[e], $"{front.Definition.Id} ({front.State})");
                    }

                    // Surround: of the eight directions around the player that geometry leaves open, how many hold an enemy.
                    if (Time.time - start > seconds * 0.5f)
                    {
                        var open = 0; var held = 0;
                        for (var k = 0; k < 8; k++)
                        {
                            var dir = new Vector2(Mathf.Cos(k * Mathf.PI / 4f), Mathf.Sin(k * Mathf.PI / 4f));
                            var probe = stand + dir * 1.2f;
                            if (!legal.Contains(probe) || !EnemyPursuitNavigationTests.Free(probe, 0.36f)) continue;
                            open++;
                            if (alive.Any(o => { var d = (Vector2)o.transform.position - stand; return d.magnitude < 2.5f && d.magnitude > 0.05f && Vector2.Angle(d, dir) <= 22.5f; })) held++;
                        }

                        if (open > 0) { coverageSum += held / (float)open; coverageSamples++; }
                    }
                }

                var longest = worst.Count > 0 ? worst.Max(x => x.Value.t) : 0f;
                var coverage = coverageSamples > 0 ? coverageSum / coverageSamples : 0f;
                log.AppendLine($"  {name} at {stand}: {enemies.Count(e => e != null && e.IsAlive)} enemies, {inRange.Count} reached range, surround {coverage:P0}, longest pressing pin {longest:0.00}s, pins > 1.5s: {worst.Count(x => x.Value.t > 1.5f)}");
                results.Add((name, longest, coverage));
                foreach (var (e, (t, blocker)) in worst.Where(x => x.Value.t > 1.5f))
                {
                    var line = $"{biome} {room.State.RoomId} {name}: {e.Definition.Id} pressed {t:0.00}s into {blocker} [crowd sidesteps {e.Crowd?.Sidesteps} backoffs {e.Crowd?.BackOffs} holds {e.Crowd?.Holds} blocked {e.Crowd?.LastWasBlocked}; wall stops {e.Steering?.Stops} blocked {e.Steering?.LastWasBlocked}; detour {e.Navigator?.IsDetouring}, crowd reroutes {e.Navigator?.CrowdReroutes}, alternative found {e.Navigator?.LastCrowdRerouteFound}]";
                    // A wait is legitimate only when the planner proved that the bodies hold the only way (a one-wide
                    // passage): it asked for a way around them and none reaches the player. Anything else is a jam.
                    var onlyWay = e.Navigator != null && e.Navigator.CrowdReroutes > 0 && !e.Navigator.LastCrowdRerouteFound;
                    log.AppendLine("    " + line + (onlyWay ? " — the only way (no route around the bodies)" : " — JAM"));
                    if (!onlyWay) failures.Add(line);
                }
            }

            yield return Scenario("in the open", interior.center, 6f);
            var socket = room.Root.GetSockets().First();
            var cells = socket.Cells();
            var door = cells.Aggregate(Vector2.zero, (a, c) => a + (Vector2)room.Root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(c))) / cells.Length;
            var inward = -(Vector2)DoorDirections.Step(socket.Direction);
            var inside = door;
            for (var k = 0; k < 12 && !interior.Contains(inside + inward * 0.6f); k++) inside += inward * 0.5f;
            yield return Scenario("just inside a doorway", inside + inward * 0.8f, 6f);
            var corner = new Vector2(interior.xMin + 0.7f, interior.yMin + 0.7f);
            for (var k = 0; k < 8 && !EnemyPursuitNavigationTests.Free(corner, 0.45f); k++) corner += new Vector2(0.5f, 0.5f);
            yield return Scenario("in a corner", corner, 6f);
            yield return Scenario("behind an obstacle", roomCases[0].to, 6f);

            log.AppendLine($"  deaths: {deaths.Count} ({deaths.Values.Count(d => d.Contains("inside hazard"))} inside an enemy-affecting hazard or its area)");
            foreach (var d in deaths.Values) log.AppendLine("  death: " + d);
            Debug.Log($"[CROWD] {biome} room {room.State.RoomId}\n{log}");
            CollectionAssert.IsEmpty(failures, "no enemy stays pressed into another enemy's back for long");
        }
    }
}
