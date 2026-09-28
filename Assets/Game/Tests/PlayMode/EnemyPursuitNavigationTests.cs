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
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Pursuit around static room geometry in the shipped rooms of every biome: for each Combat room, target positions
    /// whose straight line from the enemy is blocked by walls or obstacles but that are reachable by another route
    /// (found independently by flood-filling the room's physics geometry). A room-bound pursuer must reach attack range
    /// by going around; the case table is logged ([PURSUIT]).
    /// </summary>
    public sealed class EnemyPursuitNavigationTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            CombatLayers.Apply();
            _catalog = GameContentCatalog.Load();
            RoomDoorLock.SkinResolver = biome => { var skin = _catalog.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            foreach (var e in Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            _created.Clear();
            RoomDoorLock.SkinResolver = null;
        }

        private static readonly Collider2D[] Overlaps = new Collider2D[16];
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[16];

        private static bool Solid(Collider2D c) => c != null && !c.isTrigger && c.enabled && c.GetComponentInParent<EnvironmentObstacle>() != null;

        internal static bool Free(Vector2 p, float radius)
        {
            var n = Physics2D.OverlapCircle(p, radius, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < n; i++) if (Solid(Overlaps[i])) return false;
            return true;
        }

        private static bool LineBlocked(Vector2 a, Vector2 b, float radius)
        {
            var d = b - a;
            var n = Physics2D.CircleCast(a, radius, d.normalized, ContactFilter2D.noFilter, Hits, d.magnitude);
            for (var i = 0; i < n; i++) if (Solid(Hits[i].collider)) return true;
            return false;
        }

        /// <summary>Blocked-but-reachable pairs in one room: an independent flood fill over the physics geometry.</summary>
        internal static List<(Vector2 from, Vector2 to, float route)> Cases(Rect interior, float clearance, int max)
        {
            const float step = 0.5f;
            var cols = Mathf.FloorToInt(interior.width / step);
            var rows = Mathf.FloorToInt(interior.height / step);
            Vector2 At(int x, int y) => interior.min + new Vector2((x + 0.5f) * step, (y + 0.5f) * step);
            var free = new bool[cols, rows];
            for (var x = 0; x < cols; x++) for (var y = 0; y < rows; y++) free[x, y] = Free(At(x, y), clearance);

            int[,] Distances(int sx, int sy)
            {
                var dist = new int[cols, rows];
                for (var x = 0; x < cols; x++) for (var y = 0; y < rows; y++) dist[x, y] = -1;
                var q = new Queue<(int, int)>();
                dist[sx, sy] = 0; q.Enqueue((sx, sy));
                while (q.Count > 0)
                {
                    var (cx, cy) = q.Dequeue();
                    foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        int nx = cx + dx, ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || !free[nx, ny] || dist[nx, ny] >= 0) continue;
                        dist[nx, ny] = dist[cx, cy] + 1; q.Enqueue((nx, ny));
                    }
                }

                return dist;
            }

            var cases = new List<(Vector2, Vector2, float, float)>();
            for (var sx = 1; sx < cols - 1; sx += 3)
            for (var sy = 1; sy < rows - 1; sy += 3)
            {
                if (!free[sx, sy]) continue;
                var dist = Distances(sx, sy);
                for (var tx = 1; tx < cols - 1; tx += 3)
                for (var ty = 1; ty < rows - 1; ty += 3)
                {
                    if (dist[tx, ty] < 0) continue;
                    var a = At(sx, sy); var b = At(tx, ty);
                    var straight = Vector2.Distance(a, b);
                    if (straight < 3f || !LineBlocked(a, b, clearance * 0.9f)) continue;
                    var route = dist[tx, ty] * step;
                    cases.Add((a, b, route, route / straight));
                }
            }

            // The detours that matter most first (largest route over straight distance), spread over the room.
            var picked = new List<(Vector2 from, Vector2 to, float route)>();
            foreach (var c in cases.OrderByDescending(c => c.Item4).ThenBy(c => c.Item1.x).ThenBy(c => c.Item1.y))
            {
                if (picked.Any(p => Vector2.Distance(p.from, c.Item1) < 2.5f && Vector2.Distance(p.to, c.Item2) < 2.5f)) continue;
                picked.Add((c.Item1, c.Item2, c.Item3));
                if (picked.Count >= max) break;
            }

            return picked;
        }

        private GameObject Target(Vector2 at)
        {
            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = at;
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            target.AddComponent<TestDamageableTarget>();
            return target;
        }

        [UnityTest]
        public IEnumerator ShippedRooms_BlockedLineTargets_AreReachedByGoingAround_AcrossAllBiomes()
        {
            // One pursuer per room at a time (two in the same room would block each other's body, which is a
            // different question than routing around static geometry): wave k runs every room's k-th case.
            var definition = _catalog.Enemies.First(e => e.Id == "grunt");
            var rooms = new List<(string id, RoomRuntime runtime, List<(Vector2 from, Vector2 to, float route)> cases)>();
            var index = 0;
            foreach (var room in _catalog.Rooms.Where(r => r != null && r.Prefab != null && r.RoomType == RoomType.Combat).OrderBy(r => r.Id))
            {
                var instance = Object.Instantiate(room.Prefab, new Vector3(3000f + (index % 8) * 90f, 3000f + (index / 8) * 90f, 0f), Quaternion.identity);
                index++;
                _created.Add(instance);
                var root = instance.GetComponent<RoomRoot>();
                var runtime = instance.AddComponent<RoomRuntime>();
                runtime.Configure(root, index, 1, 1);
                Physics2D.SyncTransforms();
                rooms.Add((room.Id, runtime, Cases(runtime.InteriorWorldBounds, DefaultEnemySpawner.BodyRadius + 0.05f, 2)));
            }

            var runs = new List<(string room, EnemyController enemy, Transform target, float route, float limit)>();
            var reached = new Dictionary<EnemyController, float>();
            for (var wave = 0; wave < 2; wave++)
            {
                var batch = new List<(string room, EnemyController enemy, Transform target, float route, float limit)>();
                foreach (var (id, runtime, cases) in rooms)
                {
                    if (wave >= cases.Count) continue;
                    var (from, to, route) = cases[wave];
                    var target = Target(to);
                    var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, from, target.transform);
                    _created.Add(enemy.gameObject);
                    if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                    runtime.BindEncounterBounds(enemy.gameObject);
                    batch.Add((id, enemy, target.transform, route, route / definition.MoveSpeed * 2.5f + 3f));
                }

                Time.timeScale = 4f; // the same fixed physics step, just more of them per frame
                var start = Time.time;
                var limit = batch.Count > 0 ? batch.Max(r => r.limit) : 0f;
                while (Time.time - start < limit && batch.Any(r => !reached.ContainsKey(r.enemy)))
                {
                    yield return new WaitForFixedUpdate();
                    foreach (var r in batch)
                    {
                        if (reached.ContainsKey(r.enemy) || r.enemy == null) continue;
                        if (Vector2.Distance(r.enemy.transform.position, r.target.position) <= definition.AttackRange + 0.3f) reached[r.enemy] = Time.time - start;
                    }
                }

                Time.timeScale = 1f;
                runs.AddRange(batch);
                foreach (var r in batch) { r.enemy.enabled = false; r.enemy.GetComponent<Rigidbody2D>().simulated = false; }
            }

            Assert.Greater(runs.Count, 20, "enough blocked-but-reachable cases in the shipped rooms");
            var report = new StringBuilder();
            var failures = new List<string>();
            foreach (var r in runs)
            {
                var ok = reached.TryGetValue(r.enemy, out var t) && t <= r.limit;
                var nav = r.enemy.Navigator;
                var detail = nav == null ? "no navigator" : $"detour {nav.IsDetouring}, path {nav.Path.Count} pts (end {(nav.Path.Count > 0 ? nav.Path[nav.Path.Count - 1].ToString() : "-")}), repaths {nav.Repaths}, steer stops {r.enemy.Steering?.Stops}, state {r.enemy.State}, target {(Vector2)r.target.position}";
                var line = $"{r.room}: route {r.route:0.0} tiles -> {(ok ? $"reached in {t:0.0}s" : $"STUCK at {(Vector2)r.enemy.transform.position} ({Vector2.Distance(r.enemy.transform.position, r.target.position):0.0} from target) [{detail}]")}";
                report.AppendLine(line);
                if (!ok) failures.Add(line);
            }

            Debug.Log($"[PURSUIT] {runs.Count - failures.Count}/{runs.Count} reached\n{report}");
            CollectionAssert.IsEmpty(failures, $"{failures.Count}/{runs.Count} pursuers never reached a reachable target");
        }

        /// <summary>
        /// Every shipped Elite (the moveset pursuit path, a larger body) in Combat rooms of its own biome: attacks are
        /// suppressed so the question is pursuit alone, and each must reach a blocked-but-reachable target by going
        /// around the room's geometry — never through it and never out of the room.
        /// </summary>
        [UnityTest]
        public IEnumerator EveryElite_ReachesBlockedLineTargets_InItsBiomesRooms_WithoutLeavingTheRoom()
        {
            var report = new StringBuilder();
            var failures = new List<string>();
            var index = 0;
            foreach (var elite in _catalog.Elites.OrderBy(e => e.Id))
            {
                var rooms = _catalog.Rooms.Where(r => r != null && r.Prefab != null && r.RoomType == RoomType.Combat && r.Biome == elite.Biome && r.SupportsElite).OrderBy(r => r.Id).Take(3).ToList();
                if (rooms.Count == 0) rooms = _catalog.Rooms.Where(r => r != null && r.Prefab != null && r.RoomType == RoomType.Combat && r.Biome == elite.Biome).OrderBy(r => r.Id).Take(3).ToList();
                var runs = new List<(string room, MovesetActorController actor, Transform target, Rect interior, float limit)>();
                foreach (var room in rooms)
                {
                    var instance = Object.Instantiate(room.Prefab, new Vector3(6000f + (index % 8) * 90f, 6000f + (index / 8) * 90f, 0f), Quaternion.identity);
                    index++;
                    _created.Add(instance);
                    var runtime = instance.AddComponent<RoomRuntime>();
                    runtime.Configure(instance.GetComponent<RoomRoot>(), index, 1, 1);
                    Physics2D.SyncTransforms();
                    var cases = Cases(runtime.InteriorWorldBounds, 0.65f, 1);
                    if (cases.Count == 0) continue;
                    var (from, to, route) = cases[0];
                    var target = Target(to);
                    var encounter = new DefaultEliteSpawner(_catalog.Stagger).Spawn(elite, from, null, target.transform);
                    _created.Add(encounter.gameObject);
                    encounter.Elite.SuppressAttacks = true; // pursuit only
                    runtime.BindEncounterBounds(encounter.Elite.gameObject);
                    runs.Add((room.Id, encounter.Elite, target.transform, runtime.InteriorWorldBounds, route / Mathf.Max(0.5f, elite.MoveSpeed) * 2.5f + 3f));
                }

                Assert.Greater(runs.Count, 0, elite.Id + ": a blocked-but-reachable case in its biome");
                Time.timeScale = 4f;
                var start = Time.time;
                var reached = new Dictionary<MovesetActorController, float>();
                var limit = runs.Max(r => r.limit);
                while (Time.time - start < limit && runs.Any(r => !reached.ContainsKey(r.actor)))
                {
                    yield return new WaitForFixedUpdate();
                    foreach (var r in runs)
                    {
                        Assert.IsTrue(r.interior.Contains(r.actor.transform.position), $"{elite.Id} in {r.room}: stayed in the room");
                        if (!reached.ContainsKey(r.actor) && Vector2.Distance(r.actor.transform.position, r.target.position) <= 1.6f) reached[r.actor] = Time.time - start;
                    }
                }

                Time.timeScale = 1f;
                foreach (var r in runs)
                {
                    var ok = reached.TryGetValue(r.actor, out var t);
                    var line = $"{elite.Id} in {r.room}: {(ok ? $"reached in {t:0.0}s" : $"STUCK at {(Vector2)r.actor.transform.position}, {Vector2.Distance(r.actor.transform.position, r.target.position):0.0} from target")}";
                    report.AppendLine(line);
                    if (!ok) failures.Add(line);
                    r.actor.enabled = false;
                    r.actor.GetComponent<Rigidbody2D>().simulated = false;
                }
            }

            Debug.Log("[PURSUIT-ELITE]\n" + report);
            CollectionAssert.IsEmpty(failures, "every Elite routes around its rooms' geometry");
        }
    }

    /// <summary>
    /// Pursuit in the running game (boot → Shelter → dungeon) in each biome: a real Combat room with interior geometry
    /// spawns its own encounter, and the player stands behind the room's most-blocking obstacle, then just inside a
    /// doorway, then in a corner. No chasing enemy may stay pinned (in Chase, out of attack range, not moving) for more
    /// than a second, and none may leave the room.
    /// </summary>
    public sealed class EnemyPursuitLiveTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_pursuit_" + System.Guid.NewGuid().ToString("N"));
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

        private static readonly RaycastHit2D[] BlockHits = new RaycastHit2D[16];

        /// <summary>What the pinned body runs into toward the player: static geometry or another enemy's body.</summary>
        private static string WhatBlocks(EnemyController e, Vector2 stand)
        {
            var from = (Vector2)e.transform.position;
            var d = stand - from;
            var count = Physics2D.CircleCast(from, 0.33f, d.normalized, ContactFilter2D.noFilter, BlockHits, d.magnitude);
            var best = float.MaxValue;
            var what = "nothing";
            for (var i = 0; i < count; i++)
            {
                var c = BlockHits[i].collider;
                if (c == null || c.isTrigger || c.transform.IsChildOf(e.transform) || BlockHits[i].distance >= best) continue;
                var other = c.GetComponentInParent<EnemyController>();
                if (other != null && other != e) { best = BlockHits[i].distance; what = "enemy " + other.Definition.Id; }
                else if (c.GetComponentInParent<EnvironmentObstacle>() != null) { best = BlockHits[i].distance; what = "geometry " + c.name; }
            }

            return what;
        }

        [UnityTest]
        public IEnumerator LiveRun_EnemiesRouteAroundObstacles_DoorwaysAndCorners_NeverPinned_InEveryBiome(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Pursuit Check");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var body = player.GetComponent<Rigidbody2D>();

            // The Combat room with the strongest detour inside it (a room with real interior geometry).
            var candidates = run.Rooms.Values.Where(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered)
                .Select(r => (room: r, cases: EnemyPursuitNavigationTests.Cases(r.InteriorWorldBounds, DefaultEnemySpawner.BodyRadius + 0.05f, 1)))
                .Where(c => c.cases.Count > 0).ToList();
            Assert.Greater(candidates.Count, 0, biome + ": a Combat room with interior geometry");
            var (room, roomCases) = candidates.OrderByDescending(c => c.cases[0].route).First();
            var interior = room.InteriorWorldBounds;
            var spawned = new List<EnemyController>();
            room.EnemySpawned += (_, e) => spawned.Add(e);

            IEnumerator Put(Vector2 p)
            {
                player.transform.position = p;
                body.position = p;
                body.linearVelocity = Vector2.zero;
                Physics2D.SyncTransforms();
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            }

            var log = new StringBuilder();
            var failures = new List<string>();
            IEnumerator Scenario(string name, Vector2 stand, float seconds)
            {
                yield return Put(stand);
                var pinned = new Dictionary<EnemyController, float>();
                var blocker = new Dictionary<EnemyController, string>();
                var worstPinned = new Dictionary<EnemyController, float>();
                var reached = new HashSet<EnemyController>();
                var start = Time.time;
                while (Time.time - start < seconds)
                {
                    health.Heal(100000);
                    body.position = stand; body.linearVelocity = Vector2.zero; // the player holds the spot
                    yield return new WaitForFixedUpdate();
                    foreach (var e in spawned.Where(e => e != null && e.IsAlive))
                    {
                        Assert.IsTrue(interior.Contains(e.transform.position), $"{name}: {e.Definition.Id} stayed in the room");
                        var distance = Vector2.Distance(e.transform.position, stand);
                        if (distance <= e.Definition.AttackRange + 0.4f || e.State != EnemyState.Chase) { reached.Add(e); pinned[e] = 0f; continue; }
                        var still = e.GetComponent<Rigidbody2D>().linearVelocity.magnitude < 0.1f;
                        pinned[e] = still ? (pinned.TryGetValue(e, out var p) ? p : 0f) + Time.fixedDeltaTime : 0f;
                        if (pinned[e] > (worstPinned.TryGetValue(e, out var w) ? w : 0f))
                        {
                            worstPinned[e] = pinned[e];
                            blocker[e] = WhatBlocks(e, stand);
                        }
                    }
                }

                var alive = spawned.Where(e => e != null && e.IsAlive).ToList();
                var worst = worstPinned.Count > 0 ? worstPinned.Max(p => p.Value) : 0f;
                log.AppendLine($"  {name} at {stand}: {alive.Count} enemies, {reached.Count(e => e != null)} reached range or attacked, worst pinned {worst:0.00}s");
                foreach (var (e, t) in worstPinned.Where(p => p.Value > 1f))
                {
                    var line = $"{biome} {room.State.RoomId} {name}: {e.Definition.Id} pinned {t:0.00}s at {(Vector2)e.transform.position} by {blocker[e]} (detour {e.Navigator?.IsDetouring}, path {e.Navigator?.Path.Count})";
                    log.AppendLine("    " + line);
                    // Other enemies' bodies filling the space around the player is a crowd, not a navigation failure.
                    if (!blocker[e].StartsWith("enemy")) failures.Add(line);
                }
            }

            // 1. Behind the room's most-blocking obstacle (the player enters there and the encounter spawns).
            yield return Scenario("behind an obstacle", roomCases[0].to, 8f);
            Assert.Greater(spawned.Count, 0, "the room spawned its encounter");
            // 2. Just inside a doorway.
            var socket = room.Root.GetSockets().First();
            var cells = socket.Cells();
            var door = cells.Aggregate(Vector2.zero, (a, c) => a + (Vector2)room.Root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(c))) / cells.Length;
            var inward = -(Vector2)DoorDirections.Step(socket.Direction);
            var inside = door;
            for (var k = 0; k < 12 && !interior.Contains(inside + inward * 0.6f); k++) inside += inward * 0.5f;
            yield return Scenario("just inside a doorway", inside + inward * 0.8f, 6f);
            // 3. A corner of the room.
            var corner = new Vector2(interior.xMin + 0.7f, interior.yMin + 0.7f);
            for (var k = 0; k < 8 && !EnemyPursuitNavigationTests.Free(corner, 0.45f); k++) corner += new Vector2(0.5f, 0.5f);
            yield return Scenario("in a corner", corner, 6f);

            Debug.Log($"[PURSUIT-LIVE] {biome} room {room.State.RoomId}\n{log}");
            CollectionAssert.IsEmpty(failures, "no enemy stays pinned against geometry while its target is reachable");
        }
    }
}
