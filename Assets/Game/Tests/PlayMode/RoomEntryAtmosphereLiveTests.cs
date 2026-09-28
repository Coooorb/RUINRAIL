using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Room-entry atmosphere in real runs of each biome: the local player's first entry into a room plays the biome's
    /// brief flourish (Metro signal lamps / sparks / cold flicker, Rustworks steam / sparks, Labs spores / containment
    /// lamps) — pixel pieces only, no collider or light, overlays under 8 % — it removes itself, and walking back into a
    /// room never replays it. Captures: TestResults/RegressionProof/room_atmosphere_*.png.
    /// </summary>
    public sealed class RoomEntryAtmosphereLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_atmosphere_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory(Folder);
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
            RoomDoorLock.SkinResolver = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private sealed class Guard : IInvulnerabilityState { public bool IsInvulnerable => true; }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }

        private ExpeditionScene _run;

        private IEnumerator Boot(Biome biome)
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Atmosphere");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;
            _run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, _run.Expedition.State.Biome);
        }

        [UnityTest]
        public IEnumerator LiveRun_FirstRoomEntry_PlaysTheBiomeFlourishOnce_AndChangesNothing(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            yield return Boot(biome);
            var run = _run;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); yield return null; }
            var start = run.Rooms[run.Generation.Graph.StartId];
            Assert.AreEqual(0, run.AtmospherePlays, "arriving on a depth is not entering a room");

            // A quiet room first (the flourish reads best without a fight), then a combat room.
            var quiet = run.Rooms.Values.FirstOrDefault(r => r != start && r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Boss)
                        ?? run.Rooms.Values.First(r => r != start && r.State.RoomType != RoomType.Boss);
            var combat = run.Rooms.Values.First(r => r != quiet && r != start && r.State.RoomType == RoomType.Combat);
            Put(RoomCentre(quiet));
            yield return Settle();
            var lifecycle = quiet.Lifecycle; // as entry decided it
            Assert.AreEqual(1, run.AtmospherePlays, "the first entry plays the flourish");
            var flourish = run.LastAtmosphere;
            Assert.IsNotNull(flourish);
            Assert.AreEqual(biome, flourish.Biome);
            Assert.AreEqual(run.AtmosphereVariantOf(quiet.State.NodeId), flourish.Variant, "the room's variant from the depth's assignment");
            Assert.Greater(flourish.PieceCount, 5);
            var pieces = flourish.PieceCount;
            Assert.AreEqual(0, flourish.GetComponentsInChildren<Collider2D>(true).Length, "presentation only: nothing to collide with");
            Assert.AreEqual(0, flourish.GetComponentsInChildren<Light>(true).Length + flourish.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true).Length, "no light source: visibility is untouched");
            Assert.LessOrEqual(flourish.PeakOverlayAlpha, RoomEntryAtmosphere.MaxOverlayAlpha);
            var room = quiet.InteriorWorldBounds;
            var until = Time.time + 0.45f;
            while (Time.time < until) yield return null;
            Assert.Greater(flourish.VisibleCount, 0, "the flourish is showing");
            var margin = new Rect(room.xMin - 0.5f, room.yMin - 0.5f, room.width + 1f, room.height + 1f);
            Assert.IsTrue(flourish.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled).All(r => margin.Contains(r.transform.position)), "pieces stay in the room");
            LiveDungeonCapture.Capture(Folder, $"room_atmosphere_{biome}_{quiet.State.RoomType}", camera, room.center, room.height * 0.5f + 1f, ppu, includeUi: true);
            until = Time.time + 0.4f;
            while (Time.time < until) yield return null;
            LiveDungeonCapture.Capture(Folder, $"room_atmosphere_{biome}_{quiet.State.RoomType}_later", camera, room.center, room.height * 0.5f + 1f, ppu, includeUi: true);

            // It removes itself and leaves the room exactly as entry made it.
            until = Time.time + RoomEntryAtmosphere.MaxSeconds;
            while (Time.time < until && flourish != null) yield return null;
            Assert.IsTrue(flourish == null, "the flourish cleaned itself up");
            Assert.AreEqual(lifecycle, quiet.Lifecycle, "the flourish never touches room state");

            // A combat room plays its own once; walking back and forth never replays either.
            Put(RoomCentre(combat));
            yield return Settle();
            Assert.AreEqual(2, run.AtmospherePlays);
            Assert.AreEqual(biome, run.LastAtmosphere.Biome);
            Assert.AreEqual(run.AtmosphereVariantOf(combat.State.NodeId), run.LastAtmosphere.Variant);
            for (var i = 0; i < 3; i++)
            {
                Put(RoomCentre(quiet));
                yield return Settle();
                Put(RoomCentre(combat));
                yield return Settle();
            }

            Assert.AreEqual(2, run.AtmospherePlays, "re-entering rooms never replays the flourish");
            Debug.Log($"[PROOF] {biome}: {quiet.State.RoomType} {quiet.State.RoomId} -> {pieces} pieces, then {combat.State.RoomId}; re-entries 0 extra");
        }

        /// <summary>Every variant of the biome, played in a real room of a real run: captured early and late, presentation only, gone on its own.</summary>
        [UnityTest]
        public IEnumerator LiveRun_EveryVariant_IsShort_PresentationOnly_AndCaptured(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            yield return Boot(biome);
            var run = _run;
            var player = run.Rig.Player;
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            var start = run.Rooms[run.Generation.Graph.StartId];
            var quiet = run.Rooms.Values.FirstOrDefault(r => r != start && r.State.RoomType != RoomType.Combat && r.State.RoomType != RoomType.Boss) ?? start;
            var room = quiet.InteriorWorldBounds;
            var body = player.GetComponent<Rigidbody2D>();
            var centre = RoomCentre(quiet);
            player.transform.position = centre; body.position = centre; Physics2D.SyncTransforms();
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var margin = new Rect(room.xMin - 0.5f, room.yMin - 0.5f, room.width + 1f, room.height + 1f);
            // Let the room's own entry flourish finish first so each capture shows one variant.
            var settle = Time.time + RoomEntryAtmosphere.MaxSeconds + 0.2f;
            while (Time.time < settle) yield return null;

            for (var variant = 0; variant < RoomEntryAtmosphere.VariantCount; variant++)
            {
                var flourish = RoomEntryAtmosphere.Play(biome, variant, room, 1234 + variant);
                Assert.AreEqual(variant, flourish.Variant);
                Assert.Greater(flourish.PieceCount, 5);
                Assert.AreEqual(0, flourish.GetComponentsInChildren<Collider2D>(true).Length, "presentation only");
                Assert.AreEqual(0, flourish.GetComponentsInChildren<Light>(true).Length + flourish.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true).Length, "no light source");
                Assert.LessOrEqual(flourish.PeakOverlayAlpha, RoomEntryAtmosphere.MaxOverlayAlpha);
                var peak = 0;
                foreach (var at in new[] { 0.45f, 1.0f })
                {
                    var until = Time.time + (at == 0.45f ? 0.45f : 0.55f);
                    while (Time.time < until) { peak = Mathf.Max(peak, flourish.VisibleCount); yield return null; }
                    Assert.IsTrue(flourish.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled).All(r => margin.Contains(r.transform.position)), $"{biome} {variant}: pieces stay in the room");
                    LiveDungeonCapture.Capture(Folder, $"room_atmosphere_{biome}_v{variant}_{(at < 0.5f ? "early" : "late")}", camera, room.center, room.height * 0.5f + 1f, ppu, includeUi: true);
                }

                Assert.Greater(peak, 3, $"{biome} {variant} showed itself");
                var gone = Time.time + RoomEntryAtmosphere.MaxSeconds;
                while (Time.time < gone && flourish != null) yield return null;
                Assert.IsTrue(flourish == null, $"{biome} {variant} cleaned itself up");
                Debug.Log($"[PROOF] {biome} variant {variant}: peak {peak} pieces visible, gone within {RoomEntryAtmosphere.MaxSeconds}s");
            }
        }

        /// <summary>
        /// Variant choice over real generated depths: deterministic, all variants in use, and a neighbour shares a room's
        /// variant only where the greedy rule has no choice (the room already had every variant among earlier neighbours).
        /// </summary>
        [Test]
        public void VariantAssignment_IsDeterministic_AndNeighboursDifferWheneverPossible()
        {
            var pools = BiomeRoomPools.Build(GameContentCatalog.Load().Rooms);
            var rules = RuinRail.Dungeon.Generation.DungeonGraphRules.CreateDefault();
            var generator = new RuinRail.Dungeon.Generation.DungeonGraphGenerator(rules);
            var edges = 0;
            var same = 0;
            var used = new HashSet<int>();
            try
            {
                for (var seed = 1; seed <= 60; seed++)
                for (var depth = 1; depth <= 3; depth++)
                {
                    var generation = RuinRail.Dungeon.Generation.DungeonGenerationPipeline.Generate(generator, pools.PoolFor(BiomeSelector.SelectFirst(seed)), seed, depth);
                    if (!generation.Success) continue;
                    var nodes = generation.Graph.Nodes.Select(n => (n.Id, (IReadOnlyList<int>)n.Neighbors)).ToList();
                    var depthSeed = unchecked(seed * 31 + depth * 977);
                    var a = RoomEntryAtmosphere.AssignVariants(nodes, depthSeed);
                    var b = RoomEntryAtmosphere.AssignVariants(nodes.AsEnumerable().Reverse(), depthSeed);
                    CollectionAssert.AreEquivalent(a, b, "same graph + seed -> same variants, whatever order the rooms come in");
                    foreach (var v in a.Values) used.Add(v);
                    foreach (var (x, y) in generation.Graph.Edges)
                    {
                        edges++;
                        if (a[x] != a[y]) continue;
                        same++;
                        var later = Mathf.Max(x, y);
                        var earlier = generation.Graph.Nodes.First(n => n.Id == later).Neighbors.Where(n => n < later).Select(n => a[n]).Distinct().Count();
                        Assert.AreEqual(RoomEntryAtmosphere.VariantCount, earlier, $"seed {seed} depth {depth}: rooms {x}/{y} share a variant although one was free");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }

            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, used);
            Debug.Log($"[PROOF] variant assignment: {same}/{edges} adjacent room pairs share a variant");
        }

        /// <summary>
        /// Persistent room details in real runs: the room the player stands in has <see cref="RoomAmbientDetails.EmittersPerRoom"/>
        /// small biome details that act now and then; rooms differ, a room re-entered gets the same set, only the current
        /// room's details exist, they are localized ground-layer pixels (no collider, light or overlay), and a Descend
        /// clears them. Captures (the real 640×360 game view): TestResults/RegressionProof/room_ambient_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator LiveRun_PersistentRoomDetails_AreLocalized_Varied_Deterministic_AndCleanedUp(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            yield return Boot(biome);
            var run = _run;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); yield return null; }
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var rooms = run.Rooms.Values.Where(r => r.State.RoomType != RoomType.Boss && r.State.RoomType != RoomType.Combat).Take(3)
                .Concat(run.Rooms.Values.Where(r => r.State.RoomType == RoomType.Combat).Take(3)).ToList();
            Assert.GreaterOrEqual(rooms.Count, 4);
            var kindsByRoom = new Dictionary<int, string>();

            foreach (var room in rooms)
            {
                Put(RoomCentre(room));
                yield return Settle();
                var details = run.AmbientDetails;
                Assert.IsNotNull(details, room.State.RoomId + " has details");
                Assert.AreEqual(biome, details.Biome);
                Assert.AreEqual(RoomAmbientDetails.EmittersPerRoom, details.Kinds.Distinct().Count());
                Assert.AreEqual(1, Object.FindObjectsByType<RoomAmbientDetails>(FindObjectsSortMode.None).Length, "only the current room's details exist");
                kindsByRoom[room.State.NodeId] = string.Join(",", details.Kinds.OrderBy(k => k));
            }

            Assert.Greater(kindsByRoom.Values.Distinct().Count(), 1, "rooms do not all show the same combination");

            // Back and forth: the same set per room every time, nothing accumulates.
            for (var pass = 0; pass < 3; pass++)
            foreach (var room in rooms.Take(3))
            {
                Put(RoomCentre(room));
                yield return Settle();
                Assert.AreEqual(kindsByRoom[room.State.NodeId], string.Join(",", run.AmbientDetails.Kinds.OrderBy(k => k)), "deterministic per room");
                Assert.AreEqual(1, Object.FindObjectsByType<RoomAmbientDetails>(FindObjectsSortMode.None).Length);
            }

            // Stand in two rooms for a while: the details act, stay small and local, on the ground layer, bounded.
            foreach (var room in new[] { rooms[0], rooms[rooms.Count - 1] })
            {
                Put(RoomCentre(room));
                yield return Settle();
                var details = run.AmbientDetails;
                var area = room.InteriorWorldBounds;
                var until = Time.time + 6f;
                var captured = false;
                while (Time.time < until)
                {
                    var shown = details.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled).ToList();
                    Assert.IsTrue(shown.All(r => area.Contains(r.transform.position)), "details stay inside the room");
                    Assert.IsTrue(shown.All(r => r.transform.localScale.x <= 8f && r.transform.localScale.y <= 12f), "small, local pieces only: no overlay");
                    Assert.LessOrEqual(details.LivePieces, RoomAmbientDetails.MaxLivePieces);
                    if (!captured && shown.Count >= 3 && Time.time > until - 3.5f)
                    {
                        LiveDungeonCapture.Capture(Folder, $"room_ambient_{biome}_{room.State.RoomType}", camera, ppu, includeUi: true);
                        captured = true;
                    }

                    yield return null;
                }

                if (!captured) LiveDungeonCapture.Capture(Folder, $"room_ambient_{biome}_{room.State.RoomType}", camera, ppu, includeUi: true);
                // A room whose fight is running slows its details to a third, so they never compete with the enemies.
                var fighting = room.Lifecycle == RoomLifecycleState.Active;
                Assert.Greater(details.PiecesSpawned, fighting ? 0 : 3, $"{room.State.RoomId}: the details acted ({string.Join(",", details.Kinds)}, fighting {fighting})");
                var renderers = details.GetComponentsInChildren<SpriteRenderer>(true);
                Assert.IsTrue(renderers.All(r => r.sortingLayerName == RuinRail.Core.Rendering.SortingLayers.GroundDetails && r.sortingOrder == RoomAmbientDetails.GroundOrder), "under hazards, characters, loot and telegraphs");
                Assert.AreEqual(0, details.GetComponentsInChildren<Collider2D>(true).Length + details.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true).Length, "presentation only");
                Debug.Log($"[PROOF] {biome} {room.State.RoomId} (fighting {fighting}): {string.Join(",", details.Kinds)}; spawned {details.PiecesSpawned}, peak live {details.PeakLivePieces}");
            }

            // A Descend tears the depth down: the details go with it and never pile up.
            var last = run.AmbientDetails;
            run.Expedition.RecordBossDefeated(0);
            run.Expedition.Descend();
            for (var i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(last == null, "the old room's details are gone");
            Assert.LessOrEqual(Object.FindObjectsByType<RoomAmbientDetails>(FindObjectsSortMode.None).Length, 1);
            Debug.Log($"[PROOF] {biome}: rooms {string.Join(" | ", kindsByRoom.Select(k => k.Key + ":" + k.Value))}");
        }
    }
}
