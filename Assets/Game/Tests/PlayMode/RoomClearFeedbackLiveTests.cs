using System.Collections;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Room-clear feedback in real runs of each biome: winning a Combat room and an Elite room shows one ROOM CLEARED
    /// line (confirmation green, gone on its own), plays the Room Cleared stinger once, and every door that opens glows
    /// as it releases. A client-style clear (the host's state replicated into a non-authoritative room) presents the
    /// same way, and repeated/resynced clear reports never present again. Captures: TestResults/RegressionProof/room_clear_*.png.
    /// </summary>
    public sealed class RoomClearFeedbackLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_room_clear_" + System.Guid.NewGuid().ToString("N"));
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

        /// <summary>The first seed of the biome whose depth-1 graph carries an Elite room (shipped generator).</summary>
        private static int SeedWithElite(Biome biome, System.Collections.Generic.IReadOnlyList<RoomDefinition> rooms)
        {
            var pools = BiomeRoomPools.Build(rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            try
            {
                for (var seed = 1; seed <= 1500; seed++)
                {
                    if (BiomeSelector.SelectFirst(seed) != biome) continue;
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(biome), seed, 1);
                    if (generation.Success && generation.Graph.Nodes.Any(n => n.IsElite)) return seed;
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }

            Assert.Fail($"no {biome} seed with an Elite room on depth 1");
            return 0;
        }

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }

        private static bool IsHostile(HealthComponent h) =>
            h != null && h.IsAlive && (h.GetComponent<EnemyController>() != null || h.GetComponent<MovesetActorController>() != null);

        [UnityTest]
        public IEnumerator LiveRun_CombatAndEliteClears_PresentOnce_WithNoticeStingerAndDoorRelease(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            var content = GameContentCatalog.Load();
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(SeedWithElite(biome, content.Rooms));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Clear Check");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            var notice = run.HudView.Notice;
            var music = _app.Music;
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }

            IEnumerator Win(RoomRuntime room, string label)
            {
                Put(RoomCentre(room));
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                var deadline = Time.realtimeSinceStartup + 20f;
                while (room.Lifecycle != RoomLifecycleState.Active && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle, label + " activated");
                Assert.IsTrue(room.DoorsLocked, label + " locked while the encounter runs");
                var presented = run.RoomClearsPresented;
                var stingers = music.StingersPlayed;
                while (room.Lifecycle == RoomLifecycleState.Active)
                {
                    Assert.Less(Time.realtimeSinceStartup, deadline, label + " was not won in time");
                    Assert.AreEqual(presented, run.RoomClearsPresented, label + ": nothing presents before the room is won");
                    foreach (var h in Object.FindObjectsByType<HealthComponent>(FindObjectsSortMode.None).Where(IsHostile).Where(h => room.InteriorWorldBounds.Contains(h.transform.position)).ToList())
                        h.TryApplyDamage(new DamageRequest(9999999));
                    yield return null;
                }

                // The frame the room is won: one line, one stinger, the doors released and glowing.
                Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
                Assert.AreEqual(presented + 1, run.RoomClearsPresented, label + " presented once");
                Assert.AreEqual("ROOM CLEARED", notice.Text);
                Assert.IsTrue(notice.IsShowing && !notice.IsHeld, "a transient line");
                Assert.AreEqual(stingers + 1, music.StingersPlayed, label + ": exactly one stinger");
                Assert.AreEqual(StingerRole.RoomCleared, music.LastStinger);
                var doors = room.Doors.Where(d => d != null && !d.IsSealedSocket).ToList();
                Assert.IsTrue(doors.Count > 0 && doors.All(d => !d.IsLocked && !d.IsBlocking), label + ": every door open");
                Assert.IsTrue(doors.All(d => d.IsFlashingOpen), label + ": every door shows its release");
                yield return null;
                var cam = camera.transform;
                var doorAt = (Vector2)doors[0].transform.position;
                LiveDungeonCapture.Capture(Folder, $"room_clear_{biome}_{label}", camera, ((Vector2)player.transform.position + doorAt) * 0.5f, 7f, ppu, includeUi: true);

                // A repeated report of the same win (a client's resync of the Cleared state) presents nothing more.
                room.RestoreState(room.State.Clone());
                Assert.AreEqual(presented + 1, run.RoomClearsPresented);
                Assert.AreEqual(stingers + 1, music.StingersPlayed);

                // The line leaves on its own and never blocks play.
                var until = Time.realtimeSinceStartup + HudNoticeView.ConfirmSeconds + 0.5f;
                while (Time.realtimeSinceStartup < until) yield return null;
                Assert.IsFalse(notice.IsShowing, label + ": the notice cleared itself");
                Assert.IsTrue(doors.All(d => !d.IsFlashingOpen && d.DoorRenderer.color == Color.white), label + ": doors back to their plain open skin");
                Debug.Log($"[PROOF] {biome} {label} ({room.State.RoomId}): cleared -> 'ROOM CLEARED', stinger {music.LastStinger}, {doors.Count} doors released");
            }

            var combat = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter && r.Lifecycle == RoomLifecycleState.Unentered);
            yield return Win(combat, "combat");
            var elite = run.Rooms.Values.First(r => r.State.IsElite && r.Lifecycle == RoomLifecycleState.Unentered);
            yield return Win(elite, "elite");

            // A co-op client's rooms mirror the host: the replicated Active -> Cleared state presents the same way, once.
            var mirrored = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && r.Lifecycle == RoomLifecycleState.Unentered && r != combat && r != elite);
            mirrored.SetAuthoritative(false);
            var presentedBefore = run.RoomClearsPresented;
            var stingersBefore = music.StingersPlayed;
            var active = mirrored.State.Clone();
            active.State = RoomLifecycleState.Active;
            mirrored.RestoreState(active);
            Assert.IsTrue(mirrored.DoorsLocked);
            var cleared = active.Clone();
            cleared.State = RoomLifecycleState.Cleared;
            mirrored.RestoreState(cleared);
            Assert.AreEqual(presentedBefore + 1, run.RoomClearsPresented, "the client presents the host's clear");
            Assert.AreEqual(stingersBefore + 1, music.StingersPlayed);
            Assert.IsTrue(mirrored.Doors.Where(d => d != null && !d.IsSealedSocket).All(d => d.IsFlashingOpen));
            mirrored.RestoreState(cleared.Clone()); // resync / reconnect snapshot of the same clear
            Assert.AreEqual(presentedBefore + 1, run.RoomClearsPresented, "a resync never replays the clear");
            Assert.AreEqual(stingersBefore + 1, music.StingersPlayed);
        }
    }
}
