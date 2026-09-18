using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Rooms;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    public class RoomValidatorTests
    {
        private const string FixtureDefinitionPath = "Assets/Game/ScriptableObjects/Rooms/_Test/Room_GridTestRoom.asset";
        private readonly List<Object> _created = new();
        private Tile _floorTile;
        private Tile _wallTile;

        [SetUp]
        public void SetUp()
        {
            _floorTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile = ScriptableObject.CreateInstance<Tile>();
            _wallTile.colliderType = Tile.ColliderType.Grid;
            _created.Add(_floorTile);
            _created.Add(_wallTile);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private RoomDefinition Definition(string id, RoomType type, params DoorDirection[] doors)
        {
            var d = ScriptableObject.CreateInstance<RoomDefinition>();
            d.name = id;
            Set(d, "_id", id);
            Set(d, "_roomType", type);
            Set(d, "_sizeClass", RoomSizeClass.Small);
            Set(d, "_dimensions", new Vector2Int(16, 12));
            Set(d, "_supportedDoors", doors);
            _created.Add(d);
            return d;
        }

        /// <summary>16x12 room: floor everywhere, wall ring with door gaps for the given sockets.</summary>
        private RoomRoot BuildRoom(RoomDefinition definition, params (DoorDirection dir, Vector2Int cell)[] doors)
        {
            var grid = RoomGridBuilder.CreateRoomGrid(definition.Id);
            _created.Add(grid.gameObject);
            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            Set(definition, "_prefab", grid.gameObject);

            var floor = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var doorCells = new HashSet<Vector2Int>();
            foreach (var (dir, cell) in doors)
            {
                var socket = new GameObject($"Door_{dir}").AddComponent<DoorSocket>();
                socket.transform.SetParent(grid.transform, false);
                socket.Configure(dir, cell);
                foreach (var c in socket.Cells()) doorCells.Add(c);
            }

            for (var x = 0; x < 16; x++)
            {
                for (var y = 0; y < 12; y++)
                {
                    floor.SetTile(new Vector3Int(x, y, 0), _floorTile);
                    var border = x == 0 || y == 0 || x == 15 || y == 11;
                    if (border && !doorCells.Contains(new Vector2Int(x, y)))
                    {
                        walls.SetTile(new Vector3Int(x, y, 0), _wallTile);
                    }
                }
            }

            return root;
        }

        private static RoomMarker Marker(RoomRoot room, RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            var marker = new GameObject($"{role}").AddComponent<RoomMarker>();
            marker.transform.SetParent(room.transform, false);
            marker.Configure(role, cell, footprint);
            return marker;
        }

        private static void PaintWalls(RoomRoot room, Tile wall, params Vector2Int[] cells)
        {
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            foreach (var c in cells) walls.SetTile(GridCoordinates.ToTilemapCell(c), wall);
        }

        // ---- Acceptance 1: known-good passes, malformed fail for the expected reason ----

        [Test]
        public void KnownGoodFixture_IsGeneratorReady()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(FixtureDefinitionPath);
            var report = RoomValidator.Validate(definition);
            Assert.IsTrue(report.IsGeneratorReady, string.Join("\n", report.Problems));
            Assert.AreEqual("test_grid_small_01", report.RoomId);
        }

        [Test]
        public void GoodRuntimeCombatRoom_Passes()
        {
            var def = Definition("metro_combat_small_10", RoomType.Combat, DoorDirection.North, DoorDirection.South);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)), (DoorDirection.South, new Vector2Int(7, 0)));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(3, 6));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(12, 6));

            var report = RoomValidator.Validate(room);

            Assert.IsTrue(report.IsGeneratorReady, string.Join("\n", report.Problems));
        }

        [Test]
        public void MalformedLayer_FailsWithLayerReason()
        {
            var def = Definition("metro_combat_small_11", RoomType.Combat, DoorDirection.North);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(8, 5));
            Object.DestroyImmediate(room.transform.Find("Obstacles").gameObject);

            var report = RoomValidator.Validate(room);

            Assert.IsFalse(report.IsGeneratorReady);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("metro_combat_small_11") && p.Contains("Missing Tilemap layer 'Obstacles'")), string.Join("\n", report.Problems));
        }

        [Test]
        public void SocketBlockedByWall_FailsWithDoorReason()
        {
            var def = Definition("metro_combat_small_12", RoomType.Combat, DoorDirection.North);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(8, 5));
            PaintWalls(room, _wallTile, new Vector2Int(7, 11));

            var report = RoomValidator.Validate(room);

            Assert.IsFalse(report.IsGeneratorReady);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("North door tile (7, 11) is not walkable")), string.Join("\n", report.Problems));
        }

        [Test]
        public void DoorOpeningIntoBlockedCell_Fails()
        {
            var def = Definition("metro_combat_small_13", RoomType.Combat, DoorDirection.North);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(8, 5));
            PaintWalls(room, _wallTile, new Vector2Int(7, 10), new Vector2Int(8, 10));

            var report = RoomValidator.Validate(room);

            Assert.IsTrue(report.Problems.Any(p => p.Contains("opens into a blocked cell (7, 10)")), string.Join("\n", report.Problems));
        }

        [Test]
        public void UnreachableAnchor_FailsWithReachabilityReason_NamingBothCells()
        {
            var def = Definition("metro_combat_small_14", RoomType.Combat, DoorDirection.North, DoorDirection.South);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)), (DoorDirection.South, new Vector2Int(7, 0)));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(12, 6));
            // Full-height wall splits the room at x = 4.
            PaintWalls(room, _wallTile, Enumerable.Range(1, 10).Select(y => new Vector2Int(4, y)).ToArray());
            Marker(room, RoomMarkerRole.ChestSpawn, new Vector2Int(2, 6));

            var report = RoomValidator.Validate(room);

            Assert.IsFalse(report.IsGeneratorReady);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("ChestSpawn marker at (2, 6) is not reachable from North door at (7, 11)")), string.Join("\n", report.Problems));
        }

        [Test]
        public void EnemySpawnInsideWall_AndTooCloseToPlayerSpawn_Fail()
        {
            var def = Definition("metro_start_small_01", RoomType.Combat, DoorDirection.North);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(room, RoomMarkerRole.PlayerSpawn, new Vector2Int(3, 3));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(6, 3));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(0, 5));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(12, 8));

            var report = RoomValidator.Validate(room);

            Assert.IsTrue(report.Problems.Any(p => p.Contains("EnemySpawn marker at (6, 3) is 3.0 tiles from PlayerSpawn at (3, 3)")), string.Join("\n", report.Problems));
            Assert.IsTrue(report.Problems.Any(p => p.Contains("EnemySpawn marker cell (0, 5) is inside a wall")), string.Join("\n", report.Problems));
            Assert.IsFalse(report.Problems.Any(p => p.Contains("(12, 8)")), "Far spawn is fine.");
        }

        [Test]
        public void BlockedMarkerFootprint_MakesCellsUnwalkable_ForSpawnAndReachability()
        {
            var def = Definition("metro_combat_small_15", RoomType.Combat, DoorDirection.North);
            var room = BuildRoom(def, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(room, RoomMarkerRole.Blocked, new Vector2Int(5, 5), new Vector2Int(2, 2));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(6, 6));

            var logic = RoomLogicGrid.FromRoom(room);
            Assert.IsFalse(logic.IsWalkable(new Vector2Int(6, 6)));
            Assert.IsTrue(logic.IsWalkable(new Vector2Int(7, 7)));

            var report = RoomValidator.Validate(room);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("EnemySpawn marker cell (6, 6) is inside")), string.Join("\n", report.Problems));
        }

        [Test]
        public void RoomTypeRequirements_BossMerchantStartCombat()
        {
            var boss = Definition("metro_boss_01", RoomType.Boss, DoorDirection.South);
            Set(boss, "_sizeClass", RoomSizeClass.Boss);
            Set(boss, "_dimensions", new Vector2Int(36, 24));
            Set(boss, "_allowsAuthoredSizeVariation", true);
            Set(boss, "_dimensions", new Vector2Int(16, 12));
            var bossRoom = BuildRoom(boss, (DoorDirection.South, new Vector2Int(7, 0)));
            Assert.IsTrue(RoomValidator.Validate(bossRoom).Problems.Any(p => p.Contains("Boss room requires exactly one BossAnchor")));
            Marker(bossRoom, RoomMarkerRole.BossAnchor, new Vector2Int(8, 8));
            Assert.IsTrue(RoomValidator.Validate(bossRoom).IsGeneratorReady, string.Join("\n", RoomValidator.Validate(bossRoom).Problems));

            var merchant = Definition("metro_merchant_01", RoomType.Merchant, DoorDirection.South);
            var merchantRoom = BuildRoom(merchant, (DoorDirection.South, new Vector2Int(7, 0)));
            Assert.IsTrue(RoomValidator.Validate(merchantRoom).Problems.Any(p => p.Contains("Merchant room requires exactly one MerchantAnchor")));

            var start = Definition("metro_start_01", RoomType.Start, DoorDirection.North);
            var startRoom = BuildRoom(start, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(startRoom, RoomMarkerRole.EnemySpawn, new Vector2Int(8, 5));
            var startProblems = RoomValidator.Validate(startRoom).Problems;
            Assert.IsTrue(startProblems.Any(p => p.Contains("Start room requires at least one PlayerSpawn")), string.Join("\n", startProblems));
            Assert.IsTrue(startProblems.Any(p => p.Contains("Start room must not contain EnemySpawn")), string.Join("\n", startProblems));

            var combat = Definition("metro_combat_small_16", RoomType.Combat, DoorDirection.North);
            var combatRoom = BuildRoom(combat, (DoorDirection.North, new Vector2Int(7, 11)));
            Marker(combatRoom, RoomMarkerRole.BossAnchor, new Vector2Int(8, 5));
            var combatProblems = RoomValidator.Validate(combatRoom).Problems;
            Assert.IsTrue(combatProblems.Any(p => p.Contains("Combat room requires at least one EnemySpawn")), string.Join("\n", combatProblems));
            Assert.IsTrue(combatProblems.Any(p => p.Contains("only Boss rooms may contain a BossAnchor")), string.Join("\n", combatProblems));
        }

        // ---- Acceptance 2: duplicate ids ----

        [Test]
        public void DuplicateStableIds_AreDetectedAcrossTheSet()
        {
            var a = Definition("rust_combat_small_01", RoomType.Combat, DoorDirection.North);
            var b = Definition("rust_combat_small_01", RoomType.Combat, DoorDirection.North);
            var c = Definition("rust_combat_small_02", RoomType.Combat, DoorDirection.North);
            foreach (var d in new[] { a, b, c })
            {
                var room = BuildRoom(d, (DoorDirection.North, new Vector2Int(7, 11)));
                Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(8, 5));
            }

            var reports = RoomValidator.ValidateAll(new[] { a, b, c });

            Assert.AreEqual(3, reports.Count);
            Assert.IsTrue(reports[0].Problems.Any(p => p.Contains("duplicate stable room id")), string.Join("\n", reports[0].Problems));
            Assert.IsTrue(reports[1].Problems.Any(p => p.Contains("duplicate stable room id")));
            Assert.IsTrue(reports[2].IsGeneratorReady, string.Join("\n", reports[2].Problems));
            Assert.IsFalse(reports[0].IsGeneratorReady);
        }

        // ---- Acceptance 3/4: project audit without scenes; invalid never generator-ready ----

        [Test]
        public void ProjectAudit_ValidatesEveryRoomDefinitionAsset_WithoutOpeningScenes()
        {
            var definitions = RoomValidationTools.LoadAllRoomDefinitions();
            Assert.GreaterOrEqual(definitions.Count, 1);

            var reports = RoomValidationTools.ValidateProject();

            Assert.AreEqual(definitions.Count, reports.Count);
            foreach (var report in reports)
            {
                Assert.IsTrue(report.IsGeneratorReady, $"{report.RoomId}:\n" + string.Join("\n", report.Problems));
            }
        }

        [Test]
        public void Report_IsGeneratorReady_OnlyWhenNoProblems()
        {
            Assert.IsTrue(new RoomValidationReport("x", new List<string>()).IsGeneratorReady);
            Assert.IsFalse(new RoomValidationReport("x", new List<string> { "x: broken" }).IsGeneratorReady);

            var broken = Definition("labs_combat_small_99", RoomType.Combat, DoorDirection.North);
            var report = RoomValidator.Validate(broken);
            Assert.IsFalse(report.IsGeneratorReady, "Definition without prefab can never be generator-ready.");
            Assert.IsTrue(report.Problems.Any(p => p.Contains("no room prefab")));
        }
    }
}
