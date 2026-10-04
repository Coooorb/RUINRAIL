using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The minimap in a live run: rooms are discovered as the player walks in, the current room is the amber block
    /// with its halo, the player arrow sits at the player's place and turns with their aim, special rooms show their
    /// symbol once entered, the depth chip reads the depth. Captures (whole frame and a 4x crop of the map):
    /// <c>TestResults/MinimapPresentation</c>.
    /// </summary>
    public sealed class MinimapPresentationTests
    {
        private const string Folder = "TestResults/MinimapPresentation";
        private GameApp _app;
        private string _saveDir;

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Put(GameObject player, Vector2 p)
        {
            var body = player.GetComponent<Rigidbody2D>();
            player.transform.position = p;
            body.position = p;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            var until = Time.time + 0.4f;
            while (Time.time < until) yield return null;
        }

        private static void Shot(ExpeditionScene run, string name) =>
            LiveDungeonCapture.Capture(Folder, name, run.Camera.Camera, run.Camera.Config.PixelsPerUnit);

        [UnityTest]
        public IEnumerator LiveRun_MapDiscoversRooms_TracksThePlayerAndAim_AndMarksSpecialRooms([Values(2, 1)] int seed)
        {
            Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_minimap_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Cartographer");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var map = run.HudView.Minimap;
            var player = run.Rig.Player;
            var biome = run.Expedition.State.Biome;
            Assert.AreEqual("D1", map.DepthText);
            yield return null;
            Assert.IsTrue(map.PlayerMarkerVisible, "the player arrow shows in the start room");
            var startDrawn = map.DrawnNodeIds.Count;
            Shot(run, $"{biome}_{seed}_a_start");

            // Walk into rooms along the graph: each entry is discovered, the arrow follows, symbols appear once entered.
            var visitedSpecial = false;
            var steps = 0;
            foreach (var node in run.Generation.Graph.Nodes.OrderBy(n => n.Type == RoomType.Combat ? 1 : 0).ThenBy(n => n.Id))
            {
                if (node.Id == run.Generation.Graph.StartId || node.Type == RoomType.Boss || steps >= 4) continue;
                var room = run.Rooms[node.Id];
                var b = room.InteriorWorldBounds;
                yield return Put(player, new Vector2(b.xMin + b.width * 0.25f, b.center.y));
                // Kill whatever wakes up so the walk stays a walk.
                foreach (var e in Object.FindObjectsByType<RuinRail.Gameplay.Enemies.EnemyController>(FindObjectsSortMode.None))
                    e.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>()?.TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(100000));
                yield return null;
                Assert.AreEqual(node.Id, run.Minimap.CurrentNodeId, "entering a room makes it the current block");
                Assert.IsTrue(map.DrawnNodeIds.Contains(node.Id));
                Assert.IsTrue(map.PlayerMarkerVisible);
                Assert.Less(run.Minimap.PlayerInRoom.x, 0.5f, "the arrow sits at the player's place in the room (left quarter)");
                if (node.Type != RoomType.Combat) { visitedSpecial = true; Assert.IsTrue(map.DrawnMarkerNodeIds.Contains(node.Id), $"the {node.Type} room shows its symbol once entered"); }
                steps++;
                Shot(run, $"{biome}_{seed}_b_room{steps}_{node.Type}");
            }

            Assert.Greater(map.DrawnNodeIds.Count, startDrawn, "the map grew as rooms were discovered");
            Debug.Log($"[MINIMAP] {biome} seed {seed}: drawn {map.DrawnNodeIds.Count} markers {map.DrawnMarkerNodeIds.Count} links {map.DrawnLinkParts} special visited {visitedSpecial}");

            // The arrow turns with the aim (eight ways).
            var aiming = player.GetComponent<RuinRail.Gameplay.Player.PlayerAiming>();
            var reader = new ProofInputReader();
            player.GetComponent<RuinRail.Gameplay.Player.PlayerInput>().UseReader(reader);
            aiming.SetInputReader(reader);
            foreach (var (dir, octant) in new[] { (Vector2.right, 0), (Vector2.up, 2), (Vector2.left, 4), (Vector2.down, 6), (new Vector2(1, 1), 1) })
            {
                reader.AimAt(dir);
                for (var i = 0; i < 4; i++) yield return null;
                Assert.AreEqual(octant, map.PlayerOctant, $"aim {dir} turns the arrow to octant {octant}");
            }

            reader.AimAt(Vector2.up);
            for (var i = 0; i < 4; i++) yield return null;
            Shot(run, $"{biome}_{seed}_c_aim_up");
        }
    }
}
