using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The dungeon's surroundings (<see cref="DungeonSurroundings"/>) — everything the camera shows outside the rooms —
    /// in a live expedition per biome: composed by the shipping scene over the substrate, presentation only (no
    /// collider, Ground layer below every floor, nothing inside a room rect, no structure in a room's moat),
    /// deterministic for (seed, depth, rooms), darker than the rooms it surrounds; captured at the framings with the
    /// most non-room area and during a fight beside a room's outer wall. Captures: <c>TestResults/DungeonSurroundings</c>.
    /// </summary>
    public sealed class DungeonSurroundingsTests
    {
        private const string Folder = "TestResults/DungeonSurroundings";
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
            if (_saveDir != null) try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static int SeedFor(Biome biome, int skip = 0)
        {
            for (var seed = 1; seed < 500; seed++)
                if (BiomeSelector.SelectFirst(seed) == biome && skip-- <= 0) return seed;
            return 1;
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private IEnumerator Boot(int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_surroundings_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Surroundings");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;
        }

        private static float MeanLuminance(LiveDungeonCapture.Result shot)
        {
            var sum = 0f;
            foreach (var p in shot.Pixels) sum += (0.2126f * p.r + 0.7152f * p.g + 0.0722f * p.b) / 255f;
            return sum / Mathf.Max(1, shot.Pixels.Length);
        }

        private static List<RectInt> RoomRects(ExpeditionScene run) => run.Generation.Layout.Placements.Select(p => p.Bounds).ToList();

        private static float OutsideShare(List<RectInt> rooms, Vector2 centre)
        {
            int outside = 0, total = 0;
            for (var y = centre.y - 5.5f; y < centre.y + 5.5f; y += 0.5f)
            for (var x = centre.x - 10f; x < centre.x + 10f; x += 0.5f)
            {
                total++;
                var cell = new Vector2Int(Mathf.FloorToInt(x), Mathf.FloorToInt(y));
                if (!rooms.Any(r => r.Contains(cell))) outside++;
            }

            return outside / (float)total;
        }

        [UnityTest]
        public IEnumerator LiveRun_Surroundings_PerBiome([Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            Directory.CreateDirectory(Folder);
            yield return Boot(SeedFor(biome));
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var rooms = RoomRects(run);
            var bounds = rooms.Aggregate((a, b) => new RectInt(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax) - Mathf.Min(a.xMin, b.xMin), Mathf.Max(a.yMax, b.yMax) - Mathf.Min(a.yMin, b.yMin)));
            var roomCells = rooms.Sum(r => r.width * r.height);
            Debug.Log($"[SURROUND] {biome} layout {bounds} cells {bounds.width * bounds.height} rooms {rooms.Count} roomCells {roomCells}");

            // ---- composed by the shipping scene, presentation only ----
            var surroundings = run.Surroundings;
            Assert.IsNotNull(surroundings, "the scene composes the surroundings");
            Assert.AreEqual(biome, surroundings.Biome);
            Assert.Greater(surroundings.GroundCells, 0);
            Assert.Greater(surroundings.LinearCells, 0, "spines (tracks / pipes / ducts / coolant lines)");
            Assert.Greater(surroundings.Props, 10, "structures");
            Assert.Greater(surroundings.ShadowCells, 0, "every room throws its shadow");
            Assert.IsEmpty(surroundings.GetComponentsInChildren<Collider2D>(true), "no collider");
            foreach (var renderer in surroundings.GetComponentsInChildren<Renderer>(true))
            {
                Assert.AreEqual(RuinRail.Core.Rendering.SortingLayers.Ground, renderer.sortingLayerName, renderer.name);
                Assert.Less(renderer.sortingOrder, 0, renderer.name + ": below every floor tilemap");
                Assert.Greater(renderer.sortingOrder, WorldSubstrate.SortingOrder, renderer.name + ": above the underlay");
            }

            foreach (var room in rooms)
            {
                for (var y = room.yMin; y < room.yMax; y++)
                for (var x = room.xMin; x < room.xMax; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    Assert.IsFalse(surroundings.Ground.HasTile(cell) || surroundings.Linear.HasTile(cell) || surroundings.Shadow.HasTile(cell), $"nothing inside room {room} at {cell}");
                }

                var moat = new RectInt(room.xMin - 1, room.yMin - 1, room.width + 2, room.height + 2);
                foreach (var prop in surroundings.PropRects) Assert.IsFalse(prop.Overlaps(moat), $"structure {prop} stays out of room {room} and its moat");
            }

            // ---- deterministic for the same depth, different for another ----
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var twin = DungeonSurroundings.Create(null, biome, rooms, run.Expedition.State.RunSeed, run.Expedition.State.Depth);
            var ms = watch.Elapsed.TotalMilliseconds;
            var other = DungeonSurroundings.Create(null, biome, rooms, run.Expedition.State.RunSeed, run.Expedition.State.Depth + 1);
            Assert.AreEqual(surroundings.Fingerprint, twin.Fingerprint, "the same seed, depth and rooms build the same surroundings (every peer)");
            Assert.AreNotEqual(surroundings.Fingerprint, other.Fingerprint, "another depth builds other surroundings");
            Object.DestroyImmediate(twin.gameObject);
            Object.DestroyImmediate(other.gameObject);
            Debug.Log($"[SURROUND] {biome} built in {ms:0.0} ms: ground {surroundings.GroundCells} linear {surroundings.LinearCells} props {surroundings.Props} shadow {surroundings.ShadowCells}");

            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var half = new Vector2(10f, 5.625f);

            // The framings with the most non-room area (clamped like the game camera), two far apart.
            var candidates = new List<(Vector2 C, float S)>();
            for (var y = bounds.yMin + half.y; y <= bounds.yMax - half.y; y += 2f)
            for (var x = bounds.xMin + half.x; x <= bounds.xMax - half.x; x += 2f)
                candidates.Add((new Vector2(x, y), OutsideShare(rooms, new Vector2(x, y))));
            var picks = new List<Vector2>();
            foreach (var c in candidates.OrderByDescending(c => c.S))
            {
                if (picks.Any(p => Vector2.Distance(p, c.C) < 18f)) continue;
                picks.Add(c.C);
                if (picks.Count == 2) break;
            }

            var outsideLuminance = 0f;
            for (var i = 0; i < picks.Count; i++)
                outsideLuminance = Mathf.Max(outsideLuminance, MeanLuminance(LiveDungeonCapture.Capture(Folder, $"{biome}_outside_{i}", camera, picks[i], LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false)));

            // Subordinate: the surroundings read clearly darker than the rooms they hold.
            var start0 = run.Rooms[run.Generation.Graph.StartId].InteriorWorldBounds.center;
            var roomLuminance = MeanLuminance(LiveDungeonCapture.Capture(Folder, $"{biome}_room_reference", camera, start0, 3f, ppu, includeUi: false));
            Debug.Log($"[SURROUND] {biome} luminance outside {outsideLuminance:0.000} vs room {roomLuminance:0.000}");
            Assert.Less(outsideLuminance, roomLuminance * 0.75f, $"{biome}: the surroundings must stay well below the rooms in value");

            // A fight next to a room's outer wall: the combat room with the most open surroundings beside it.
            var combat = run.Rooms.Values.Where(r => r.State.RoomType == RoomType.Combat).ToList();
            var best = combat.OrderByDescending(r => OutsideShare(rooms, r.InteriorWorldBounds.center)).First();
            var b = best.InteriorWorldBounds;
            var player = run.Rig.Player;
            var reader = new ProofInputReader();
            player.GetComponent<PlayerInput>().UseReader(reader);
            foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var method = component.GetType().GetMethod("SetInputReader", new[] { typeof(RuinRail.Core.Input.IPlayerInputReader) });
                if (method != null) method.Invoke(component, new object[] { reader });
            }

            run.Rig.Loadout.SetInputReader(reader);
            // Stand one tile in from the wall that faces the most open surroundings.
            var sides = new[] { (new Vector2(b.xMin + 1.2f, b.center.y), Vector2.left), (new Vector2(b.xMax - 1.2f, b.center.y), Vector2.right),
                (new Vector2(b.center.x, b.yMin + 1.2f), Vector2.down), (new Vector2(b.center.x, b.yMax - 1.2f), Vector2.up) };
            var stand = sides.OrderByDescending(s => OutsideShare(rooms, s.Item1 + s.Item2 * 6f)).First().Item1;
            var body = player.GetComponent<Rigidbody2D>();
            player.transform.position = stand;
            body.position = stand;
            Physics2D.SyncTransforms();
            var health = player.GetComponent<HealthComponent>();
            var start = Time.time;
            var shots = 0;
            while (Time.time < start + 3.4f)
            {
                if (health.CurrentHealth < health.MaxHealth / 3) health.Heal(health.MaxHealth);
                var me = (Vector2)player.transform.position;
                var target = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive && b.Contains(e.transform.position))
                    .OrderBy(e => Vector2.Distance(e.transform.position, me)).FirstOrDefault();
                if (target != null) { reader.AimAt((Vector2)target.transform.position - me); reader.SetFire(true); }
                if (shots < 2 && Time.time > start + 1.3f + shots * 1.5f)
                {
                    LiveDungeonCapture.Capture(Folder, $"{biome}_combat_edge_{shots}", camera, ppu);
                    shots++;
                }

                yield return null;
            }

            reader.Release();
            Assert.AreEqual(2, shots);
        }
    }
}
