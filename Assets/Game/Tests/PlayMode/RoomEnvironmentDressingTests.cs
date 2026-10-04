using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation;
using RuinRail.Presentation.Vfx;
using RuinRail.Presentation.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The room environment layer (<see cref="RoomEnvironmentDressing"/>) on every shipped room, dressed through the
    /// composer's own seam (<see cref="DungeonRoomRuntimeComposer.Dress"/>) over the biome substrate and lighting:
    /// presentation only (no collider, no gameplay tile change, approved layers, nothing on hazard cells, the wall
    /// texture only on walls), deterministic per (seed, depth, node), clutter only on wall/obstacle ground away from
    /// doors and markers, and the central combat space kept low-contrast. Then a live expedition per biome: the
    /// shipping composer attaches the layer to every room, and a fight and a boss telegraph still read cleanly.
    /// Captures: <c>TestResults/RoomEnvironmentDressing</c> (gallery at 640x360, live_* during combat).
    /// </summary>
    public sealed class RoomEnvironmentDressingTests
    {
        private const string Folder = "TestResults/RoomEnvironmentDressing";
        private const int Ppu = 32;
        private static readonly string Scratch = Path.Combine(Path.GetTempPath(), "ruinrail_envdressing");
        private readonly List<GameObject> _spawned = new();
        private GameApp _app;
        private string _saveDir;

        private static readonly RoomTilemapLayer[] GameplayLayers =
            { RoomTilemapLayer.Floor, RoomTilemapLayer.Walls, RoomTilemapLayer.Obstacles, RoomTilemapLayer.Hazards, RoomTilemapLayer.AbovePlayer, RoomTilemapLayer.Logic };

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
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

        private static IEnumerable<RoomDefinition> Rooms(Biome biome) =>
            GameContentCatalog.Load().Rooms.Where(r => r != null && r.Biome == biome && r.Prefab != null).OrderBy(r => r.Id, System.StringComparer.Ordinal);

        private RoomRoot Spawn(RoomDefinition definition)
        {
            var go = Object.Instantiate(definition.Prefab, Vector3.zero, Quaternion.identity);
            _spawned.Add(go);
            return go.GetComponent<RoomRoot>();
        }

        // ---------------------------------------------------------------- independent room facts (not the layer's own map)

        private sealed class Facts
        {
            public int W, H;
            public bool[,] Floor, Wall, Obstacle, Hazard, Above, Detail, Door, Marker;
            public int[,] Dist;
            public bool Solid(int x, int y) => x < 0 || y < 0 || x >= W || y >= H || !Floor[x, y] || Wall[x, y];
            public bool Open(int x, int y) => !Solid(x, y) && !Obstacle[x, y];
            public int ObstacleCount
            {
                get
                {
                    var n = 0;
                    foreach (var o in Obstacle) if (o) n++;
                    return n;
                }
            }
        }

        private static Facts Read(RoomRoot root)
        {
            var f = new Facts { W = root.Size.x, H = root.Size.y };
            f.Floor = new bool[f.W, f.H]; f.Wall = new bool[f.W, f.H]; f.Obstacle = new bool[f.W, f.H]; f.Hazard = new bool[f.W, f.H];
            f.Above = new bool[f.W, f.H]; f.Detail = new bool[f.W, f.H]; f.Door = new bool[f.W, f.H]; f.Marker = new bool[f.W, f.H]; f.Dist = new int[f.W, f.H];
            Tilemap Layer(RoomTilemapLayer l) => RoomGridBuilder.FindLayer(root.Grid, l);
            for (var x = 0; x < f.W; x++)
            for (var y = 0; y < f.H; y++)
            {
                var c = new Vector3Int(x, y, 0);
                f.Floor[x, y] = Layer(RoomTilemapLayer.Floor)?.GetTile(c) != null;
                f.Wall[x, y] = Layer(RoomTilemapLayer.Walls)?.GetTile(c) != null;
                f.Obstacle[x, y] = Layer(RoomTilemapLayer.Obstacles)?.GetTile(c) != null;
                f.Hazard[x, y] = Layer(RoomTilemapLayer.Hazards)?.GetTile(c) != null;
                f.Above[x, y] = Layer(RoomTilemapLayer.AbovePlayer)?.GetTile(c) != null;
                f.Detail[x, y] = Layer(RoomTilemapLayer.FloorDetail)?.GetTile(c) != null;
            }

            foreach (var socket in root.GetSockets())
            for (var w = -2; w <= socket.Width + 2; w++)
            for (var d = -2; d <= 2; d++)
            {
                var c = DoorDirections.IsHorizontalEdge(socket.Direction) ? new Vector2Int(socket.Cell.x + w, socket.Cell.y + d) : new Vector2Int(socket.Cell.x + d, socket.Cell.y + w);
                if (c.x >= 0 && c.y >= 0 && c.x < f.W && c.y < f.H) f.Door[c.x, c.y] = true;
            }

            foreach (var marker in root.GetMarkers())
            for (var x = marker.Rect.xMin - 1; x <= marker.Rect.xMax; x++)
            for (var y = marker.Rect.yMin - 1; y <= marker.Rect.yMax; y++)
                if (x >= 0 && y >= 0 && x < f.W && y < f.H) f.Marker[x, y] = true;

            // Steps from the nearest wall through open floor.
            var queue = new Queue<Vector2Int>();
            for (var x = 0; x < f.W; x++)
            for (var y = 0; y < f.H; y++)
            {
                f.Dist[x, y] = int.MaxValue;
                if (!f.Open(x, y)) { f.Dist[x, y] = 0; continue; }
                if (f.Solid(x + 1, y) || f.Solid(x - 1, y) || f.Solid(x, y + 1) || f.Solid(x, y - 1)) { f.Dist[x, y] = 1; queue.Enqueue(new Vector2Int(x, y)); }
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var n in new[] { c + Vector2Int.right, c + Vector2Int.left, c + Vector2Int.up, c + Vector2Int.down })
                {
                    if (n.x < 0 || n.y < 0 || n.x >= f.W || n.y >= f.H || !f.Open(n.x, n.y) || f.Dist[n.x, n.y] <= f.Dist[c.x, c.y] + 1) continue;
                    f.Dist[n.x, n.y] = f.Dist[c.x, c.y] + 1;
                    queue.Enqueue(n);
                }
            }

            return f;
        }

        private static string Snapshot(RoomRoot root)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var layer in GameplayLayers)
            {
                var map = RoomGridBuilder.FindLayer(root.Grid, layer);
                if (map == null) continue;
                foreach (var p in map.cellBounds.allPositionsWithin)
                {
                    var tile = map.GetTile(p);
                    if (tile == null) continue;
                    // Hazard cells are re-skinned in place by the hazard art: what must not change is where they are and
                    // that they carry no collider shape; every other gameplay layer must keep its exact tiles.
                    if (layer == RoomTilemapLayer.Hazards) sb.Append(layer).Append(p).Append(map.GetColliderType(p)).Append(';');
                    // Every room's cover is re-skinned in place as whole objects: same cells, same collider type.
                    else if (layer == RoomTilemapLayer.Obstacles)
                        sb.Append(layer).Append(p).Append(map.GetColliderType(p)).Append(';');
                    else sb.Append(layer).Append(p).Append(tile.name).Append(map.GetTransformMatrix(p).GetHashCode()).Append(';');
                }
            }

            return sb.ToString();
        }

        private static float Luminance(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        private static LiveDungeonCapture.Result Render(Camera camera, Vector2 centre, float orthographic, string name) =>
            LiveDungeonCapture.Capture(Scratch, name, camera, centre, orthographic, Ppu, includeUi: false);

        // ---------------------------------------------------------------- every shipped room

        [UnityTest]
        public IEnumerator EveryShippedRoom_IsDressedPresentationOnly_Deterministically_AndKeepsCombatSpaceReadable(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Scratch);
            var content = GameContentCatalog.Load();
            var lightingGo = new GameObject("GalleryLighting");
            _spawned.Add(lightingGo);
            lightingGo.AddComponent<BiomeLightingApplier>().Apply(content.LightingFor(biome));
            var cameraGo = new GameObject("GalleryCam");
            _spawned.Add(cameraGo);
            var camera = cameraGo.AddComponent<Camera>();
            camera.backgroundColor = WorldSubstrate.ClearColorFor(biome);
            var report = new List<string>();

            var node = 0;
            foreach (var definition in Rooms(biome))
            {
                node++;
                var root = Spawn(definition);
                var twin = Spawn(definition);
                var other = Spawn(definition);
                twin.transform.position = other.transform.position = new Vector3(500f, 500f, 0f);
                var colliders = root.GetComponentsInChildren<Collider2D>(true).Length;
                var tilesBefore = Snapshot(root);
                var facts = Read(root);

                var watch = System.Diagnostics.Stopwatch.StartNew();
                DungeonRoomRuntimeComposer.Dress(root, 11, 1, node);
                var ms = watch.Elapsed.TotalMilliseconds;
                DungeonRoomRuntimeComposer.Dress(twin, 11, 1, node);
                DungeonRoomRuntimeComposer.Dress(other, 11, 1, node + 100);

                // ---- presentation only ----
                var layer = root.GetComponentInChildren<RoomEnvironmentLayer>();
                Assert.IsNotNull(layer, $"{definition.Id}: the composer's dressing seam attaches the environment layer");
                Assert.AreEqual(colliders, root.GetComponentsInChildren<Collider2D>(true).Length, $"{definition.Id}: dressing adds no collider");
                Assert.AreEqual(tilesBefore, Snapshot(root), $"{definition.Id}: dressing changes no gameplay tile");
                Assert.IsEmpty(layer.GetComponentsInChildren<Component>(true).Where(c => c is Collider2D || c is Light || c.GetType().Name == "Light2D"), $"{definition.Id}: no collider or light on the layer");
                Assert.AreEqual(SortingLayers.Ground, layer.Floor.sortingLayerName);
                Assert.AreEqual(RoomEnvironmentDressing.FloorSortingOrder, layer.Floor.sortingOrder);
                Assert.AreEqual(SortingLayers.LowProps, layer.Walls.sortingLayerName);
                Assert.AreEqual(RoomEnvironmentDressing.WallSortingOrder, layer.Walls.sortingOrder);

                // ---- deterministic, and two rooms of one id differ ----
                var twinLayer = twin.GetComponentInChildren<RoomEnvironmentLayer>();
                var otherLayer = other.GetComponentInChildren<RoomEnvironmentLayer>();
                Assert.AreEqual(layer.Result.FloorFingerprint, twinLayer.Result.FloorFingerprint, $"{definition.Id}: same seed/depth/node paints the same floor");
                Assert.AreEqual(layer.Result.WallFingerprint, twinLayer.Result.WallFingerprint, $"{definition.Id}: same seed/depth/node paints the same walls");
                Assert.AreNotEqual(layer.Result.FloorFingerprint, otherLayer.Result.FloorFingerprint, $"{definition.Id}: another node of the same room looks different");
                Assert.Greater(layer.Result.MergedCells, 0, $"{definition.Id}: the floor grid is broken into slabs");

                // ---- cover in every room category: whole biome objects, deterministic, varied, the same collision ----
                var obstacleArt = root.GetComponent<RoomObstacleArt>();
                if (facts.ObstacleCount > 0)
                {
                    Assert.IsNotNull(obstacleArt, $"{definition.Id}: the cover is re-skinned");
                    Assert.AreEqual(facts.ObstacleCount, obstacleArt.Cells, $"{definition.Id}: every obstacle cell, no more");
                    Assert.AreEqual(facts.ObstacleCount, obstacleArt.Pieces.Sum(p => p.W * p.H), $"{definition.Id}: the pieces tile the cover exactly");
                    Assert.AreEqual(obstacleArt.Fingerprint, twin.GetComponent<RoomObstacleArt>().Fingerprint, $"{definition.Id}: same seed/depth/node, same objects");
                    Assert.IsTrue(obstacleArt.Pieces.All(p => ObstacleArt.FamiliesOf(biome).Contains(p.Family)), $"{definition.Id}: biome families only");
                    for (var i = 1; i < obstacleArt.Pieces.Count; i++)
                        if (ObstacleArt.FamiliesOf(biome).Count(f => ObstacleArt.Fits(f, obstacleArt.Pieces[i].W, obstacleArt.Pieces[i].H)) > 1)
                            Assert.AreNotEqual(obstacleArt.Pieces[i - 1].Family, obstacleArt.Pieces[i].Family, $"{definition.Id}: neighbouring pieces never repeat an object");
                    if (obstacleArt.Pieces.Count >= 3) Assert.GreaterOrEqual(obstacleArt.Pieces.Select(p => p.Family).Distinct().Count(), 2, $"{definition.Id}: more than one object family");
                    report.Add($"{definition.Id} [{definition.RoomType}] cover: {obstacleArt.Pieces.Count} pieces, {obstacleArt.Pieces.Select(p => p.Family).Distinct().Count()} families ({string.Join(",", obstacleArt.Pieces.Select(p => $"{p.Family}{p.W}x{p.H}"))})");
                }
                else Assert.IsNull(obstacleArt, $"{definition.Id}: no cover, nothing re-skinned");

                // ---- clutter only on wall / obstacle ground, never on doors, markers, hazards, props ----
                foreach (var c in layer.ClutterCells)
                {
                    Assert.IsTrue(facts.Open(c.x, c.y), $"{definition.Id}: clutter at {c} is on open floor");
                    Assert.IsFalse(facts.Hazard[c.x, c.y], $"{definition.Id}: clutter at {c} is off hazards");
                    Assert.IsFalse(facts.Door[c.x, c.y], $"{definition.Id}: clutter at {c} keeps door clearance");
                    Assert.IsFalse(facts.Marker[c.x, c.y], $"{definition.Id}: clutter at {c} keeps markers clear");
                    Assert.IsFalse(facts.Detail[c.x, c.y] || facts.Above[c.x, c.y], $"{definition.Id}: clutter at {c} stays off props and foreground");
                    Assert.LessOrEqual(facts.Dist[c.x, c.y], RoomEnvironmentDressing.ClutterMaxWallDistance,
                        $"{definition.Id}: clutter at {c} stays on the wall band (never the open combat space)");
                }

                // ---- rendered: hazards untouched, walls-only wall texture, low-contrast combat space ----
                var size = (Vector2)root.Size * GridConstants.TileWorldSize;
                var substrate = WorldSubstrate.Create(root.transform, biome, new Rect(Vector2.zero, size));
                twin.gameObject.SetActive(false);
                other.gameObject.SetActive(false);
                yield return null;
                var whole = Mathf.Max(size.y * 0.5f, size.x * 0.5f * LiveDungeonCapture.Height / LiveDungeonCapture.Width) + 0.5f;
                var native = LiveDungeonCapture.Height / (2f * Ppu);
                var shot = LiveDungeonCapture.Capture(Folder, $"{biome}_{definition.Id}", camera, size * 0.5f, native, Ppu, includeUi: false);
                var allOn = Render(camera, size * 0.5f, whole, "all");
                layer.Floor.enabled = false;
                var wallsOnly = Render(camera, size * 0.5f, whole, "walls");
                layer.Walls.enabled = false;
                var none = Render(camera, size * 0.5f, whole, "none");
                var noneNative = Render(camera, size * 0.5f, native, "none_native");
                layer.Floor.enabled = layer.Walls.enabled = true;

                int hazardDiff = 0, offWallDiff = 0, corePixels = 0, coreStrong = 0;
                var coreDelta = 0f;
                for (var py = 0; py < LiveDungeonCapture.Height; py++)
                for (var px = 0; px < LiveDungeonCapture.Width; px++)
                {
                    var world = allOn.CameraPosition + new Vector2(px + 0.5f - LiveDungeonCapture.Width * 0.5f, py + 0.5f - LiveDungeonCapture.Height * 0.5f) * (2f * whole / LiveDungeonCapture.Height);
                    var cx = Mathf.FloorToInt(world.x);
                    var cy = Mathf.FloorToInt(world.y);
                    // A screen pixel that straddles a cell edge cannot be attributed to one cell; judge the rest.
                    var margin = 1.5f * (2f * whole / LiveDungeonCapture.Height);
                    var fx = world.x - cx;
                    var fy = world.y - cy;
                    if (fx < margin || fy < margin || fx > 1f - margin || fy > 1f - margin) continue;
                    var inside = cx >= 0 && cy >= 0 && cx < facts.W && cy < facts.H;
                    var i = py * LiveDungeonCapture.Width + px;
                    if (inside && facts.Hazard[cx, cy] && !allOn.Pixels[i].Equals(wallsOnly.Pixels[i])) hazardDiff++;
                    if ((!inside || !facts.Wall[cx, cy]) && !wallsOnly.Pixels[i].Equals(none.Pixels[i])) offWallDiff++;
                }

                for (var py = 0; py < LiveDungeonCapture.Height; py++)
                for (var px = 0; px < LiveDungeonCapture.Width; px++)
                {
                    var world = shot.CameraPosition + new Vector2(px + 0.5f - LiveDungeonCapture.Width * 0.5f, py + 0.5f - LiveDungeonCapture.Height * 0.5f) / Ppu;
                    var cx = Mathf.FloorToInt(world.x);
                    var cy = Mathf.FloorToInt(world.y);
                    if (cx < 0 || cy < 0 || cx >= facts.W || cy >= facts.H || facts.Dist[cx, cy] < RoomEnvironmentDressing.CoreDistance || facts.Dist[cx, cy] == int.MaxValue) continue;
                    // Tile borders are the seams the slab pass removes on purpose; judge decals on the tile interiors.
                    var lx = Mathf.FloorToInt((world.x - cx) * Ppu);
                    var ly = Mathf.FloorToInt((world.y - cy) * Ppu);
                    if (lx < 3 || ly < 3 || lx > 28 || ly > 28) continue;
                    var i = py * LiveDungeonCapture.Width + px;
                    var delta = Mathf.Abs(Luminance(shot.Pixels[i]) - Luminance(noneNative.Pixels[i]));
                    corePixels++;
                    coreDelta += delta;
                    if (delta > 0.12f) coreStrong++;
                }

                Object.DestroyImmediate(substrate.gameObject);
                var mean = corePixels == 0 ? 0f : coreDelta / corePixels;
                var strong = corePixels == 0 ? 0f : coreStrong / (float)corePixels;
                report.Add($"{definition.Id} {root.Size} {ms:0.0}ms merged={layer.Result.MergedCells} clutter={layer.Result.Clutter} glows={layer.Result.Glows} story={layer.Result.Vignette} coreMeanDL={mean:0.000} coreStrong={strong:P2}");
                Assert.AreEqual(0, hazardDiff, $"{definition.Id}: the floor layer must leave hazard cells untouched");
                Assert.AreEqual(0, offWallDiff, $"{definition.Id}: the wall layer must paint wall cells only");
                Assert.Less(mean, 0.04f, $"{definition.Id}: the central combat space stays low-contrast (mean luminance change)");
                Assert.Less(strong, 0.02f, $"{definition.Id}: no strong decal contrast in the central combat space");

                foreach (var go in new[] { root.gameObject, twin.gameObject, other.gameObject }) Object.DestroyImmediate(go);
            }

            File.WriteAllLines(Path.Combine(Folder, $"{biome}_report.txt"), report);
            Assert.Greater(node, 0, $"no rooms shipped for {biome}");
        }

        // ---------------------------------------------------------------- the shipping runtime

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
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

        [UnityTest]
        public IEnumerator LiveRun_ShippingComposerDressesEveryRoom_AndAFightAndATelegraphStillRead(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_envlive_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Env Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            foreach (var room in run.Rooms.Values)
            {
                var layer = room.GetComponentInChildren<RoomEnvironmentLayer>();
                Assert.IsNotNull(layer, $"room {room.name}: the shipping composer attaches the environment layer");
                Assert.IsTrue(layer.Floor.enabled && layer.Floor.sprite != null && layer.Walls.sprite != null, $"room {room.name}: both textures are live");
            }

            // Drive the real player through the proof reader (every input consumer, including the weapons).
            var player = run.Rig.Player;
            var reader = new ProofInputReader();
            player.GetComponent<PlayerInput>().UseReader(reader);
            foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var method = component.GetType().GetMethod("SetInputReader", new[] { typeof(RuinRail.Core.Input.IPlayerInputReader) });
                if (method != null) method.Invoke(component, new object[] { reader });
            }

            run.Rig.Loadout.SetInputReader(reader);
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var health = player.GetComponent<HealthComponent>();
            var tag = biome.ToString();

            // ---- a fight in a combat room ----
            var combat = run.Generation.Graph.Nodes.First(n => n.Type == RoomType.Combat && !n.IsElite);
            var bounds = run.Rooms[combat.Id].InteriorWorldBounds;
            yield return Put(player, new Vector2(bounds.center.x, bounds.center.y - bounds.height * 0.2f));
            var start = Time.time;
            var shots = 0;
            while (Time.time < start + 3.2f)
            {
                if (health.CurrentHealth < health.MaxHealth / 3) health.Heal(health.MaxHealth);
                var me = (Vector2)player.transform.position;
                var target = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive && bounds.Contains(e.transform.position))
                    .OrderBy(e => Vector2.Distance(e.transform.position, me)).FirstOrDefault();
                if (target != null) { reader.AimAt((Vector2)target.transform.position - me); reader.SetFire(true); }
                if (shots < 2 && Time.time > start + 1.2f + shots * 1.4f) { LiveDungeonCapture.Capture(Folder, $"live_{tag}_combat_{shots}", camera, ppu); shots++; }
                yield return null;
            }

            reader.Release();
            Assert.AreEqual(2, shots);

            // ---- a boss telegraph over the dressed arena ----
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var boss = bossRoom.GetComponent<RoomContentBinding>().Boss.Boss;
            var arena = bossRoom.InteriorWorldBounds;
            yield return Put(player, new Vector2(arena.center.x, arena.yMin + 1.4f));
            var intro = BossIntroSequence.Current;
            var introEnd = Time.time + 5f;
            while (intro != null && intro.IsPlaying && Time.time < introEnd) yield return null;
            var telegraph = boss.GetComponent<TelegraphIndicator>();
            var captured = false;
            var until = Time.time + 14f;
            var hop = 0f;
            var angles = new[] { 180f, 0f, 200f, -20f };
            var k = 0;
            while (Time.time < until && !captured && boss.IsAlive)
            {
                if (health.CurrentHealth < health.MaxHealth / 3) health.Heal(health.MaxHealth);
                if (Time.time > hop)
                {
                    var r = angles[k++ % angles.Length] * Mathf.Deg2Rad;
                    var spot = (Vector2)boss.transform.position + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * 4.5f;
                    spot = new Vector2(Mathf.Clamp(spot.x, arena.xMin + 1.5f, arena.xMax - 1.5f), Mathf.Clamp(spot.y, arena.yMin + 1.5f, arena.yMax - 1.5f));
                    yield return Put(player, spot);
                    hop = Time.time + 2.5f;
                }

                var me = (Vector2)player.transform.position;
                reader.AimAt((Vector2)boss.transform.position - me);
                reader.SetFire(true);
                var d = (Vector2)boss.transform.position - (Vector2)camera.transform.position;
                if (telegraph != null && telegraph.IsShowing && telegraph.Fill01 > 0.5f && Mathf.Abs(d.x) < 8.5f && Mathf.Abs(d.y) < 4f)
                {
                    LiveDungeonCapture.Capture(Folder, $"live_{tag}_boss_telegraph", camera, ppu);
                    captured = true;
                }

                yield return null;
            }

            reader.Release();
            Assert.IsTrue(captured, $"{tag}: a boss telegraph was captured over the dressed arena");
        }
    }
}
