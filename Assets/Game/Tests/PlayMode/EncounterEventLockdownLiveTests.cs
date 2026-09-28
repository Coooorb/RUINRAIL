using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof that the Supply Signal holds the party in its room (57: "survive a ~30-second wave encounter"):
    /// through the real boot flow into the first seed whose depth 1 carries a Supply Signal, the player presses E on the
    /// signal and every door locks solid at once, stays locked across the later waves and a repeated press, and opens
    /// only when the survival resolves — the same encounter-event lockdown the Cursed Chest uses. Captures go to
    /// <c>TestResults/RegressionProof/EncounterEventLockdown</c>.
    /// </summary>
    public sealed class EncounterEventLockdownLiveTests
    {
        private const string Folder = "TestResults/RegressionProof/EncounterEventLockdown";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_lockdown_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            CursorService.SetApplier(_ => true);
            GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            WorldObjectArt.Resolver = null;
            ProjectileVisualCatalog.Active = null;
            ItemDescriptions.WeaponCatalog = null;
            CursorService.Reset();
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private sealed class Guard : IInvulnerabilityState { public bool IsInvulnerable => true; }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        /// <summary>The first run seed whose depth-1 layout places a Supply Signal (shipped generation + event-kind pick).</summary>
        private static int SeedWithSupplySignalOnDepthOne(IReadOnlyList<RoomDefinition> rooms)
        {
            var pools = BiomeRoomPools.Build(rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            try
            {
                for (var seed = 1; seed <= 400; seed++)
                {
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(BiomeSelector.SelectFirst(seed)), seed, 1);
                    if (!generation.Success) continue;
                    if (generation.Layout.Placements.Any(p => p.Definition.RoomType == RoomType.Event
                                                              && RoomCategoryComposer.ResolveEventKind(p.Definition.Tags, seed, 1, p.NodeId) == DungeonEventKind.SupplySignal)) return seed;
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }

            Assert.Fail("no seed in 1..400 places a Supply Signal on depth 1");
            return 0;
        }

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            return marker != null ? (Vector2)room.Root.transform.TransformPoint(marker.WorldCenter) : room.InteriorWorldBounds.center;
        }

        /// <summary>A player-sized body cast from inside the doorway outward meets the door's solid blocker.</summary>
        private static bool DoorwayBlocked(RoomRuntime room, RoomDoorLock door)
        {
            var outward = (Vector2)DoorDirections.Step(door.Socket.Direction);
            var doorway = (Vector2)door.transform.position;
            var hits = Physics2D.CircleCastAll(doorway - outward * 2.5f, 0.3f, outward, 4f);
            return hits.Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<RoomDoorLock>() == door);
        }

        [UnityTest]
        public IEnumerator LiveRun_SupplySignal_LocksTheRoomUntilTheSurvivalResolves()
        {
            var seed = SeedWithSupplySignalOnDepthOne(GameContentCatalog.Load().Rooms);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Lockdown Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var interactor = player.GetComponent<PlayerInteractor>();
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle() { for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate(); for (var i = 0; i < 3; i++) yield return null; }

            var room = run.Rooms.Values.First(r => r.GetComponent<RoomContentBinding>()?.EventInstance is SupplySignalEvent);
            var binding = room.GetComponent<RoomContentBinding>();
            var signal = (SupplySignalEvent)binding.EventInstance;
            Assert.Greater(room.Doors.Count, 0);

            // Walking in: an event room is resolved on entry and stays open while the signal is idle (passive until pressed).
            Put(RoomCentre(room));
            yield return Settle();
            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.IsFalse(room.DoorsLocked, "an idle Supply Signal room is escapable");
            Assert.AreEqual(DungeonEventPhase.Available, signal.Phase);

            // Pressing E on the signal: the first wave spawns and every door locks solid at once.
            Put((Vector2)binding.Event.transform.position + Vector2.down * 1.0f);
            yield return Settle(); yield return null;
            StringAssert.Contains("SUPPLY SIGNAL", run.CurrentInteractionPrompt);
            Assert.IsTrue(interactor.TryInteract(), "E reaches the signal");
            yield return null;
            Assert.IsTrue(signal.IsRunning);
            Assert.IsTrue(room.DoorsLocked, "activating the signal locks the doors immediately");
            yield return Settle();
            Assert.IsTrue(room.Doors.All(d => d.IsLocked && d.IsBlocking), "every door is solid (nobody stands in a doorway)");
            Assert.IsTrue(room.Doors.All(d => DoorwayBlocked(room, d)), "a body walking out through any doorway meets the door");
            var enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e != null && e.IsAlive && room.InteriorWorldBounds.Contains(e.transform.position));
            Assert.Greater(enemies, 0, "the first wave spawned in the room");
            LiveDungeonCapture.Capture(Folder, "supply_signal_locked", camera, ppu, includeUi: true);

            // The shipped ticker runs the timer (composed, not driven by the test).
            var elapsed = signal.Elapsed;
            var until = Time.time + 0.5f;
            while (Time.time < until) yield return null;
            Assert.Greater(signal.Elapsed, elapsed, "the room's SupplySignalTicker advances the survival timer");

            // A repeated press never restarts the event or releases the room.
            var waves = signal.Waves.Count;
            Assert.IsFalse(binding.Event.Interact(player), "a second press starts nothing");
            Assert.AreEqual(waves, signal.Waves.Count);
            Assert.IsTrue(room.DoorsLocked && signal.IsRunning);

            // The later waves keep the room locked.
            signal.Tick(signal.WaveInterval * waves - signal.Elapsed + 0.01f);
            yield return null;
            Assert.Greater(signal.Waves.Count, waves, "the next wave was raised");
            Assert.IsTrue(room.DoorsLocked && signal.IsRunning, "still locked mid-survival");

            // The survival resolves: the doors open, the drop lands, the event is resolved for the room.
            var pickups = Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length + Object.FindObjectsByType<CoinPickup>(FindObjectsSortMode.None).Length;
            signal.Tick(signal.DurationSeconds);
            yield return Settle();
            Assert.AreEqual(DungeonEventPhase.Completed, signal.Phase);
            Assert.AreEqual(DungeonEventOutcome.Success, signal.Result.Outcome);
            Assert.IsFalse(room.DoorsLocked, "the doors open when the survival resolves");
            Assert.IsTrue(room.Doors.All(d => !d.IsLocked && !d.IsBlocking), "every door passable again");
            Assert.IsTrue(room.State.IsResolved("event:SupplySignal"));
            Assert.Greater(Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Length + Object.FindObjectsByType<CoinPickup>(FindObjectsSortMode.None).Length, pickups, "the supply drop landed");
            LiveDungeonCapture.Capture(Folder, "supply_signal_resolved_open", camera, ppu, includeUi: true);

            // Used: no second outcome, the room stays open.
            Assert.IsFalse(binding.Event.Interact(player));
            yield return null;
            Assert.IsFalse(room.DoorsLocked);
        }
    }
}
