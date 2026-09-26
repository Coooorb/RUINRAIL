using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Hazards;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Networking;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// Damaging floor hazards read as live danger in the shipped game: on a real expedition (seed 11, Overgrown Labs)
    /// and on each other biome's shipped hazard room placed into the same live scene, the Hazards tilemap cell under a
    /// RoomHazard is the biome's animated tile, the renderer advances its frames, the drawn pixels change between two
    /// captures, and standing in it still costs HP. Captures go to <c>TestResults/HazardFloorAnimationProof</c>.
    /// </summary>
    public sealed class HazardFloorAnimationProofTests
    {
        private const string Folder = "TestResults/HazardFloorAnimationProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_hazardproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder)) File.Delete(stale);
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "hazard_evidence.txt"), _evidence.ToString());
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
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private void Note(string line)
        {
            _evidence.AppendLine(line);
            Debug.Log("[HAZARD-PROOF] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private IEnumerator EnterDungeon()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Hazard Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
        }

        private static Vector3Int CellOf(Tilemap map, RoomHazard hazard) => map.WorldToCell(hazard.transform.position);

        /// <summary>A RoomHazard whose centre cell is painted on the room's Hazards tilemap (what the player sees).</summary>
        private static RoomHazard HazardIn(GameObject room)
        {
            var root = room.GetComponent<RoomRoot>();
            var map = root != null ? RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Hazards) : null;
            return map == null ? null : room.GetComponentsInChildren<RoomHazard>(true).FirstOrDefault(h => h.Definition != null && map.GetTile(CellOf(map, h)) != null);
        }

        /// <summary>Pixels that differ between two captures inside a world rect.</summary>
        private static int Changed(LiveDungeonCapture.Result a, LiveDungeonCapture.Result b, Vector2 min, Vector2 max)
        {
            var p0 = a.WorldToPixel(min);
            var p1 = a.WorldToPixel(max);
            var changed = 0;
            for (var y = Mathf.Max(0, p0.y); y < Mathf.Min(LiveDungeonCapture.Height, p1.y); y++)
            for (var x = Mathf.Max(0, p0.x); x < Mathf.Min(LiveDungeonCapture.Width, p1.x); x++)
            {
                var c0 = a.At(x, y);
                var c1 = b.At(x, y);
                if (Mathf.Abs(c0.r - c1.r) + Mathf.Abs(c0.g - c1.g) + Mathf.Abs(c0.b - c1.b) > 24) changed++;
            }

            return changed;
        }

        /// <summary>
        /// One room's hazard: the tile, the renderer's frame advance, the visible change, the captures, the damage.
        /// </summary>
        private IEnumerator ProveHazard(ExpeditionScene run, GameObject roomObject, Biome biome, string label)
        {
            var root = roomObject.GetComponent<RoomRoot>();
            var map = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Hazards);
            Assert.IsNotNull(map, $"{label}: no Hazards tilemap");
            var hazard = HazardIn(roomObject);
            Assert.IsNotNull(hazard, $"{label}: no RoomHazard over a painted hazard cell");
            var cell = CellOf(map, hazard);

            // The tile the player sees is the biome's animated hazard, never a static fallback.
            var tile = map.GetTile(cell) as HazardAnimatedTile;
            Assert.IsNotNull(tile, $"{label}: hazard cell {cell} is {map.GetTile(cell)?.GetType().Name}, not the animated hazard tile");
            StringAssert.StartsWith(BiomeStem(biome), tile.name, $"{label}: the {biome} room paints another biome's hazard");
            Assert.IsTrue(tile.IsAnimated);
            Assert.AreEqual(8, map.GetAnimationFrameCount(cell), $"{label}: the renderer does not see an 8-frame loop");
            var renderer = map.GetComponent<TilemapRenderer>();
            Assert.IsTrue(renderer != null && renderer.enabled, $"{label}: Hazards tilemap not rendered");

            // Visible motion: the real render of the hazard cell, captured across most of a loop, changes.
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var centre = (Vector2)map.GetCellCenterWorld(cell);
            var closeOrtho = LiveDungeonCapture.Height / (2f * ppu * 3f);
            var frameA = map.GetAnimationFrame(cell);
            var a = LiveDungeonCapture.Capture(Folder, $"{label}_close_a", camera, centre, closeOrtho, ppu, includeUi: false);
            // Sample most of a loop: the largest change on the hazard cell against the first capture.
            var half = new Vector2(0.5f, 0.5f);
            var changed = 0;
            var frames = new System.Collections.Generic.List<int>();
            var deltas = new System.Collections.Generic.List<int>();
            for (var shot = 0; shot < 5; shot++)
            {
                var until = Time.time + 0.2f;
                while (Time.time < until) yield return null;
                var frame = map.GetAnimationFrame(cell);
                frames.Add(frame);
                var b = LiveDungeonCapture.Capture(Folder, $"{label}_close_{(char)('b' + shot)}", camera, centre, closeOrtho, ppu, includeUi: false);
                var delta = Changed(a, b, centre - half, centre + half);
                changed = Mathf.Max(changed, delta);
                deltas.Add(delta);
            }

            Assert.IsTrue(frames.Any(f => f >= 0) && frames.Distinct().Count() > 1, $"{label}: the tile animation did not advance ({frameA} -> {string.Join(",", frames)})");

            // Liquids/heat run out of step cell to cell; current pulses together.
            var neighbour = new[] { Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right }.Select(d => cell + d).FirstOrDefault(n => map.GetTile(n) == tile);
            int Shown(Vector3Int c) => (map.GetAnimationFrame(c) + (tile.DesyncCells ? HazardAnimatedTile.StartFrame(c, tile.Frames.Length) : 0)) % tile.Frames.Length;
            var neighbourFrame = map.GetTile(neighbour) == tile ? Shown(neighbour) : -2;
            if (neighbourFrame != -2)
            {
                if (tile.DesyncCells) Assert.AreNotEqual(Shown(cell), neighbourFrame, $"{label}: neighbouring {biome} hazard cells show the same frame");
                else Assert.AreEqual(Shown(cell), neighbourFrame, $"{label}: {biome} rail cells should pulse together");
            }

            Assert.Greater(changed, 150, $"{label}: at most {changed} hazard pixels changed across the loop; the motion is not visible");
            LiveDungeonCapture.Capture(Folder, $"{label}_gameplay_distance", camera, centre, LiveDungeonCapture.Height / (2f * ppu), ppu, includeUi: false);

            // Shots across it: the player stands outside and fires the live rig's projectiles through the hazard.
            var player = run.Rig.Player;
            var ticksOnPlayer = 0;
            void OnTick(IDamageable target, int damage) { if (target is Component c && c.transform.root == player.transform.root) ticksOnPlayer++; }
            hazard.Volume.Ticked += OnTick;
            var body = player.GetComponent<Rigidbody2D>();
            var hazardBox = hazard.GetComponent<BoxCollider2D>();
            var outside = (Vector2)hazard.transform.position + Vector2.left * (hazardBox.size.x * 0.5f + 2.5f);
            player.transform.position = outside;
            body.position = outside;
            body.linearVelocity = Vector2.zero;
            yield return new WaitForFixedUpdate();
            var playerHealth = player.GetComponent<HealthComponent>();
            var hpBeforeShots = playerHealth.CurrentHealth;
            var crossed = false;
            for (var volley = 0; volley < 4; volley++)
            {
                run.Rig.Projectiles.Spawn(outside + Vector2.right * 0.6f, new ProjectileSpawnData(1, 16f, hazardBox.size.x + 5f, 0f, 0f, Vector2.right, player, null, 0f, DamageTeam.Player));
                for (var i = 0; i < 8; i++)
                {
                    body.position = outside;
                    yield return new WaitForFixedUpdate();
                    crossed |= run.Rig.Projectiles.GetComponentsInChildren<Projectile>(false).Any(p => hazardBox.OverlapPoint(p.transform.position));
                }
            }

            var settle = Time.time + 0.6f;
            while (Time.time < settle) { body.position = outside; yield return new WaitForFixedUpdate(); }
            Assert.IsTrue(crossed, $"{label}: a projectile really flew through the hazard");
            Assert.AreEqual(0, ticksOnPlayer, $"{label}: bullets crossing {hazard.Definition.Id} ticked the player standing outside it");
            Assert.AreEqual(hpBeforeShots, playerHealth.CurrentHealth, $"{label}: no HP lost while outside the hazard");

            // Still damaging: the player standing in it takes the hazard's ticks.
            player.transform.position = hazard.transform.position;
            body.position = hazard.transform.position;
            body.linearVelocity = Vector2.zero;
            var deadline = Time.time + hazard.Definition.InitialDelaySeconds + hazard.Definition.TickIntervalSeconds * 2f + 0.5f;
            while (ticksOnPlayer == 0 && Time.time < deadline)
            {
                body.position = hazard.transform.position;
                yield return new WaitForFixedUpdate();
            }

            hazard.Volume.Ticked -= OnTick;
            Assert.Greater(ticksOnPlayer, 0, $"{label}: standing in {hazard.Definition.Id} no longer damages the player");
            Note($"{label}: {biome} hazard {hazard.Definition.Id} cell {cell} tile {tile.name} fps {tile.FramesPerSecond} desync {tile.DesyncCells} frames {frameA}->{string.Join(",", frames)} neighbour {neighbourFrame} pixelsChanged {string.Join(",", deltas)} ticksOnPlayer {ticksOnPlayer}");
        }

        private static string BiomeStem(Biome biome) => biome.ToString().ToLowerInvariant();

        [UnityTest]
        public IEnumerator EveryBiomeDamagingFloor_IsAnimatedVisibly_InTheLiveDungeon_AndStillDamages()
        {
            yield return EnterDungeon();
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var liveBiome = run.Expedition.State.Biome;

            // The generated expedition itself: a hazard room of the live run.
            var live = run.Rooms.Values.FirstOrDefault(r => HazardIn(r.Root.gameObject) != null);
            Assert.IsNotNull(live, "the seed-11 depth composed no room with a damaging floor hazard");
            yield return ProveHazard(run, live.Root.gameObject, liveBiome, $"live_{BiomeStem(liveBiome)}");

            // Each other biome's shipped hazard room, placed into the same live scene with the same camera and lighting.
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }.Where(b => b != liveBiome))
            {
                var definition = content.Rooms.First(r => r.Biome == biome && r.RoomType == RoomType.Combat && r.Prefab.GetComponentsInChildren<RoomHazard>(true).Any());
                var origin = new Vector3(4000f + (int)biome * 100f, 4000f, 0f);
                var instance = Object.Instantiate(definition.Prefab, origin, Quaternion.identity);
                yield return null;
                yield return ProveHazard(run, instance, biome, $"{BiomeStem(biome)}_{definition.Id}");
                Object.DestroyImmediate(instance);
            }
        }
    }
}
