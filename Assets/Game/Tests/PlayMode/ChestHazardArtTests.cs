using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Hazards;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation;
using RuinRail.Presentation.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// Chest and hazard art per biome, on a shipped room dressed through the composer's own seam: every chest tier in
    /// every state (closed / opened / Boss Cache locked) and the biome's damaging floor, captured at the 640x360
    /// reference into <c>TestResults/ChestHazardArt</c>.
    /// </summary>
    public sealed class ChestHazardArtTests
    {
        private const string Folder = "TestResults/ChestHazardArt";
        private const int Ppu = 32;
        private readonly List<GameObject> _spawned = new();

        private GameApp _app;
        private string _saveDir;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            ShutDownRun();
            WorldObjectArt.Resolver = null;
        }

        private void ShutDownRun()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            _app = null;
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
            if (_saveDir != null) try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
            _saveDir = null;
        }

        private static int HazardCells(RoomRoot root)
        {
            var map = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Hazards);
            return map == null ? 0 : Cells(map).Count;
        }

        private static List<Vector3Int> Cells(UnityEngine.Tilemaps.Tilemap map)
        {
            var cells = new List<Vector3Int>();
            foreach (var c in map.cellBounds.allPositionsWithin) if (map.HasTile(c)) cells.Add(c);
            return cells;
        }

        private RoomRoot _room;

        /// <summary>The nearest cell centre to <paramref name="at"/> on open floor (no wall, obstacle or hazard), as a real chest cell would be.</summary>
        private Vector2 Free(Vector2 at)
        {
            bool Blocked(Vector3Int c) => new[] { RoomTilemapLayer.Walls, RoomTilemapLayer.Obstacles, RoomTilemapLayer.Hazards }
                .Any(l => RoomGridBuilder.FindLayer(_room.Grid, l)?.HasTile(c) == true) || RoomGridBuilder.FindLayer(_room.Grid, RoomTilemapLayer.Floor)?.HasTile(c) != true;
            var start = new Vector3Int(Mathf.FloorToInt(at.x), Mathf.FloorToInt(at.y), 0);
            for (var r = 0; r < 6; r++)
            for (var dy = -r; dy <= r; dy++)
            for (var dx = -r; dx <= r; dx++)
            {
                var c = start + new Vector3Int(dx, dy, 0);
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) == r && !Blocked(c)) return new Vector2(c.x + 0.5f, c.y + 0.5f);
            }

            return at;
        }

        private SupplyChest Chest(Vector2 at, LootSourceKind kind, Biome biome, bool elite, bool opened, bool locked)
        {
            at = Free(at);
            var go = new GameObject(elite ? "EliteRewardChest" : kind.ToString());
            _spawned.Add(go);
            go.transform.position = at;
            go.AddComponent<BoxCollider2D>().isTrigger = true;
            var chest = go.AddComponent<SupplyChest>();
            chest.SetKind(kind);
            chest.AttachVisual();
            ChestArt.Apply(chest, biome, ChestArt.TierOf(kind, elite));
            if (locked) chest.SetLocked(true);
            if (opened) chest.RestoreOpened();
            return chest;
        }

        [UnityTest]
        public IEnumerator Gallery_ChestTiersAndHazards_PerBiome([Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            Directory.CreateDirectory(Folder);
            var content = GameContentCatalog.Load();
            WorldObjectArt.Resolver = content.WorldSpriteFor;
            var lighting = new GameObject("Lighting");
            _spawned.Add(lighting);
            lighting.AddComponent<BiomeLightingApplier>().Apply(content.LightingFor(biome));

            // The biome's combat room with the most damaging floor.
            RoomRoot room = null;
            var best = -1;
            foreach (var definition in content.Rooms.Where(r => r != null && r.Biome == biome && r.RoomType == RoomType.Combat).OrderBy(r => r.Id, System.StringComparer.Ordinal))
            {
                var candidate = Object.Instantiate(definition.Prefab).GetComponent<RoomRoot>();
                var n = HazardCells(candidate);
                if (n > best) { if (room != null) Object.DestroyImmediate(room.gameObject); room = candidate; best = n; }
                else Object.DestroyImmediate(candidate.gameObject);
            }

            _spawned.Add(room.gameObject);
            _room = room;
            Assert.Greater(best, 0, $"{biome}: no shipped room with a damaging floor");
            var hazardMap = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Hazards);
            var cellsBefore = Cells(hazardMap);
            var boxesBefore = room.GetComponentsInChildren<RoomHazard>(true).Select(h => h.GetComponent<BoxCollider2D>()).Where(b => b != null)
                .Select(b => $"{b.transform.position}{b.size}{b.offset}{b.isTrigger}").ToList();
            var definitionsBefore = room.GetComponentsInChildren<RoomHazard>(true).Select(h => h.Definition).ToList();
            DungeonRoomRuntimeComposer.Dress(room, 11, 1, 3);

            // ---- the gameplay contract of the damaging floor is untouched ----
            var cellsAfter = Cells(hazardMap);
            CollectionAssert.AreEqual(cellsBefore, cellsAfter, $"{biome}: the same hazard cells are painted");
            foreach (var c in cellsAfter)
            {
                var tile = hazardMap.GetTile(c) as HazardAnimatedTile;
                Assert.IsNotNull(tile, $"{biome}: {c} is an animated hazard tile");
                StringAssert.StartsWith(biome.ToString().ToLowerInvariant(), tile.name, "the tile keeps the biome stem");
                Assert.AreEqual(HazardArt.Frames, tile.Frames.Length);
                Assert.AreEqual(Tile.ColliderType.None, hazardMap.GetColliderType(c), $"{biome}: {c} carries no collider shape");
            }

            CollectionAssert.AreEqual(boxesBefore, room.GetComponentsInChildren<RoomHazard>(true).Select(h => h.GetComponent<BoxCollider2D>()).Where(b => b != null)
                .Select(b => $"{b.transform.position}{b.size}{b.offset}{b.isTrigger}").ToList(), $"{biome}: every hazard trigger box is unchanged");
            CollectionAssert.AreEqual(definitionsBefore, room.GetComponentsInChildren<RoomHazard>(true).Select(h => h.Definition).ToList(), $"{biome}: same hazard definitions");
            var art = room.GetComponent<RoomHazardArt>();
            Assert.AreEqual(cellsAfter.Count, art.Cells);
            var twin = Object.Instantiate(room.Definition.Prefab, new Vector3(800f, 800f, 0f), Quaternion.identity).GetComponent<RoomRoot>();
            _spawned.Add(twin.gameObject);
            DungeonRoomRuntimeComposer.Dress(twin, 11, 1, 3);
            Assert.AreEqual(art.Fingerprint, twin.GetComponent<RoomHazardArt>().Fingerprint, $"{biome}: the same room paints the same hazard art (every peer)");
            twin.gameObject.SetActive(false);
            var size = (Vector2)room.Size * GridConstants.TileWorldSize;
            _spawned.Add(WorldSubstrate.Create(room.transform, biome, new Rect(Vector2.zero, size)).gameObject);
            var camera = new GameObject("Cam").AddComponent<Camera>();
            _spawned.Add(camera.gameObject);
            camera.backgroundColor = WorldSubstrate.ClearColorFor(biome);
            var native = LiveDungeonCapture.Height / (2f * Ppu);

            // ---- hazards: centred on the damaging floor ----
            var hazards = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Hazards);
            var cells = Cells(hazards);
            var centre = new Vector2((float)cells.Average(c => c.x) + 0.5f, (float)cells.Average(c => c.y) + 0.5f);
            yield return null;
            yield return new WaitForSeconds(0.3f);
            LiveDungeonCapture.Capture(Folder, $"{biome}_hazard_a", camera, centre, native, Ppu, includeUi: false);
            yield return new WaitForSeconds(0.45f);
            LiveDungeonCapture.Capture(Folder, $"{biome}_hazard_b", camera, centre, native, Ppu, includeUi: false);

            // ---- chests: every tier and state on the room's floor, beside the room centre ----
            var mid = size * 0.5f;
            var rowA = mid + new Vector2(-4.5f, 1.6f);
            Chest(rowA + new Vector2(0f, 0f), LootSourceKind.SupplyChest, biome, false, false, false);
            Chest(rowA + new Vector2(2f, 0f), LootSourceKind.SupplyChest, biome, false, true, false);
            Chest(rowA + new Vector2(4f, 0f), LootSourceKind.SupplyChest, biome, true, false, false);
            Chest(rowA + new Vector2(6f, 0f), LootSourceKind.EquipmentChest, biome, false, false, false);
            Chest(rowA + new Vector2(8f, 0f), LootSourceKind.TreasureChest, biome, false, false, false);
            Chest(rowA + new Vector2(10f, 0f), LootSourceKind.TreasureChest, biome, false, true, false);
            var rowB = mid + new Vector2(-3.5f, -2f);
            Chest(rowB, LootSourceKind.BossCache, biome, false, false, true);
            Chest(rowB + new Vector2(3.5f, 0f), LootSourceKind.BossCache, biome, false, false, false);
            Chest(rowB + new Vector2(7f, 0f), LootSourceKind.BossCache, biome, false, true, false);
            yield return null;
            LiveDungeonCapture.Capture(Folder, $"{biome}_chests", camera, mid + new Vector2(0.2f, 0f), native, Ppu, includeUi: false);
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
        public IEnumerator LiveRun_HazardsAndChestsReadDuringCombat([Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            Directory.CreateDirectory(Folder);
            ExpeditionScene run = null;
            RoomRuntime hazardRoom = null;
            var tried = 0;
            for (var seed = 1; seed < 400 && hazardRoom == null && tried < 5; seed++)
            {
                if (BiomeSelector.SelectFirst(seed) != biome) continue;
                tried++;
                ShutDownRun();
                RuinRail.Core.Input.GameplayInputGate.Reset();
                _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_chesthazard_" + System.Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_saveDir);
                _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
                _app.SetRunSeedOverride(seed);
                SceneManager.LoadScene(SceneNames.MainMenu);
                yield return WaitComposed(SceneNames.MainMenu);
                _app.Menu.Play();
                yield return WaitComposed(SceneNames.Base);
                var hub = Object.FindFirstObjectByType<BaseHubScreen>();
                hub.Onboarding.SubmitDisplayName("Art Proof");
                hub.Onboarding.AcknowledgeStarterKit();
                _app.Settings.Current.Tutorial.ShowPrompts = false;
                Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
                hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
                Assert.IsTrue(hub.Hub.Transit.StartExpedition());
                yield return WaitComposed(SceneNames.Dungeon);
                for (var i = 0; i < 6; i++) yield return null;
                run = Object.FindFirstObjectByType<ExpeditionScene>();
                hazardRoom = run.Rooms.Values.FirstOrDefault(r => r.State.RoomType == RoomType.Combat && r.Root.GetComponent<RoomHazardArt>() != null);
            }

            Assert.IsNotNull(hazardRoom, $"{biome}: no depth among {tried} seeds composed a combat room with a damaging floor");
            // Every composed room with a damaging floor carries the hazard art, and the chests carry the chest art.
            foreach (var room in run.Rooms.Values)
            {
                var map = RoomGridBuilder.FindLayer(room.Root.Grid, RoomTilemapLayer.Hazards);
                var cells = map != null ? Cells(map) : new List<Vector3Int>();
                if (cells.Count > 0) Assert.AreEqual(cells.Count, room.Root.GetComponent<RoomHazardArt>()?.Cells ?? 0, $"{room.name}: hazard art on every painted cell");
            }

            // Fight inside the hazard room, next to its damaging floor, and capture.
            var player = run.Rig.Player;
            var reader = new ProofInputReader();
            player.GetComponent<PlayerInput>().UseReader(reader);
            foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            {
                var method = component.GetType().GetMethod("SetInputReader", new[] { typeof(RuinRail.Core.Input.IPlayerInputReader) });
                if (method != null) method.Invoke(component, new object[] { reader });
            }

            run.Rig.Loadout.SetInputReader(reader);
            var health = player.GetComponent<HealthComponent>();
            var hazards = RoomGridBuilder.FindLayer(hazardRoom.Root.Grid, RoomTilemapLayer.Hazards);
            var hazardCells = Cells(hazards);
            var nearest = hazardCells.Select(c => (Vector2)hazards.GetCellCenterWorld(c)).OrderBy(p => Vector2.Distance(p, hazardRoom.InteriorWorldBounds.center)).First();
            var bounds = hazardRoom.InteriorWorldBounds;
            var stand = nearest + (bounds.center - nearest).normalized * 2.2f;
            yield return Put(player, stand);
            var start = Time.time;
            var shots = 0;
            while (Time.time < start + 3.4f)
            {
                if (health.CurrentHealth < health.MaxHealth / 3) health.Heal(health.MaxHealth);
                var me = (Vector2)player.transform.position;
                var target = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e != null && e.IsAlive && bounds.Contains(e.transform.position))
                    .OrderBy(e => Vector2.Distance(e.transform.position, me)).FirstOrDefault();
                if (target != null) { reader.AimAt((Vector2)target.transform.position - me); reader.SetFire(true); }
                if (shots < 2 && Time.time > start + 1.2f + shots * 1.5f)
                {
                    LiveDungeonCapture.Capture(Folder, $"live_{biome}_combat_hazard_{shots}", run.Camera.Camera, run.Camera.Config.PixelsPerUnit);
                    shots++;
                }

                yield return null;
            }

            reader.Release();
            Assert.AreEqual(2, shots);
            // Standing in it still hurts exactly as before (the trigger and the definition are the room's own).
            var hpBefore = health.CurrentHealth;
            yield return Put(player, nearest);
            var until = Time.time + 2.5f;
            while (Time.time < until && health.CurrentHealth >= hpBefore) yield return null;
            Assert.Less(health.CurrentHealth, hpBefore, $"{biome}: the damaging floor still damages");
        }
    }
}