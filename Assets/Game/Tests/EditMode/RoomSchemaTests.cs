using RuinRail.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class RoomSchemaTests
    {
        private const string FixturePrefabPath = "Assets/Game/Prefabs/Rooms/_Test/GridTestRoom.prefab";
        private const string FixtureDefinitionPath = "Assets/Game/ScriptableObjects/Rooms/_Test/Room_GridTestRoom.asset";
        private readonly List<Object> _created = new();

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

        private RoomDefinition Definition(string id, RoomType type, RoomSizeClass sizeClass, Vector2Int dims, params DoorDirection[] doors)
        {
            var d = ScriptableObject.CreateInstance<RoomDefinition>();
            d.name = id;
            Set(d, "_id", id);
            Set(d, "_roomType", type);
            Set(d, "_sizeClass", sizeClass);
            Set(d, "_dimensions", dims);
            Set(d, "_supportedDoors", doors);
            _created.Add(d);
            return d;
        }

        private RoomRoot Room(RoomDefinition definition, Vector2Int size)
        {
            var grid = RoomGridBuilder.CreateRoomGrid("Room");
            _created.Add(grid.gameObject);
            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, size);
            return root;
        }

        private static DoorSocket Socket(RoomRoot room, DoorDirection direction, Vector2Int cell, int width = DoorSocket.DefaultWidth)
        {
            var socket = new GameObject($"Door_{direction}").AddComponent<DoorSocket>();
            socket.transform.SetParent(room.transform, false);
            socket.Configure(direction, cell, width);
            return socket;
        }

        private static RoomMarker Marker(RoomRoot room, RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            var marker = new GameObject($"{role}").AddComponent<RoomMarker>();
            marker.transform.SetParent(room.transform, false);
            marker.Configure(role, cell, footprint);
            return marker;
        }

        // ---- Acceptance 1: metadata serializes ----

        [Test]
        public void FixtureDefinition_SerializesStableIdCategoryBiomeDimensionsAndSockets()
        {
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(FixtureDefinitionPath);
            Assert.IsNotNull(definition);
            Assert.AreEqual("test_grid_small_01", definition.Id);
            Assert.AreEqual(Biome.RuinedMetro, definition.Biome);
            Assert.AreEqual(RoomType.Combat, definition.RoomType);
            Assert.AreEqual(RoomSizeClass.Small, definition.SizeClass);
            Assert.AreEqual(new Vector2Int(16, 12), definition.Dimensions);
            Assert.AreEqual(1, definition.MinDepth);
            Assert.IsTrue(definition.IsUnlimitedDepth);
            Assert.IsFalse(definition.SupportsElite);
            Assert.AreEqual(0f, definition.SelectionWeight, "Fixture must never be picked by a generator.");
            CollectionAssert.AreEquivalent(new[] { DoorDirection.North, DoorDirection.East }, definition.SupportedDoors);
            Assert.IsTrue(definition.HasTag("test_fixture"));
            Assert.IsNotNull(definition.Prefab);
            Assert.AreSame(definition, definition.Prefab.GetComponent<RoomRoot>().Definition, "Prefab and definition reference each other.");
            Assert.IsEmpty(RoomSchemaValidator.ValidateDefinition(definition));

            var json = JsonUtility.ToJson(definition);
            StringAssert.Contains("test_grid_small_01", json);
        }

        [Test]
        public void SizeClasses_MatchApprovedDimensions()
        {
            Assert.AreEqual(new Vector2Int(16, 12), RoomSizeClasses.DimensionsOf(RoomSizeClass.Small));
            Assert.AreEqual(new Vector2Int(24, 16), RoomSizeClasses.DimensionsOf(RoomSizeClass.Medium));
            Assert.AreEqual(new Vector2Int(32, 20), RoomSizeClasses.DimensionsOf(RoomSizeClass.Large));
            Assert.AreEqual(new Vector2Int(36, 24), RoomSizeClasses.DimensionsOf(RoomSizeClass.Boss));
            CollectionAssert.AreEquivalent(new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }, System.Enum.GetValues(typeof(Biome)));
            CollectionAssert.AreEquivalent(
                new[] { RoomType.Start, RoomType.Combat, RoomType.Loot, RoomType.Treasure, RoomType.Merchant, RoomType.Event, RoomType.MedicalRecovery, RoomType.Boss },
                System.Enum.GetValues(typeof(RoomType)));
        }

        [Test]
        public void Definition_DimensionsMustMatchClass_UnlessVariationExplicitlyAllowed()
        {
            var strict = Definition("metro_combat_medium_01", RoomType.Combat, RoomSizeClass.Medium, new Vector2Int(20, 16), DoorDirection.North);
            Assert.IsTrue(RoomSchemaValidator.ValidateDefinition(strict).Any(p => p.Contains("do not match Medium class")));

            Set(strict, "_allowsAuthoredSizeVariation", true);
            Assert.IsFalse(RoomSchemaValidator.ValidateDefinition(strict).Any(p => p.Contains("do not match")));

            var invalidId = Definition("Metro Combat", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North);
            Assert.IsTrue(RoomSchemaValidator.ValidateDefinition(invalidId).Any(p => p.Contains("must match")));

            var eliteLoot = Definition("labs_loot_01", RoomType.Loot, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.South);
            Set(eliteLoot, "_supportsElite", true);
            Assert.IsTrue(RoomSchemaValidator.ValidateDefinition(eliteLoot).Any(p => p.Contains("only Combat rooms")));

            var noDoors = Definition("labs_start_01", RoomType.Start, RoomSizeClass.Small, new Vector2Int(16, 12));
            Assert.IsTrue(RoomSchemaValidator.ValidateDefinition(noDoors).Any(p => p.Contains("no supported door")));
        }

        [Test]
        public void Definition_DepthAvailability_UsesMinAndUnlimitedMax()
        {
            var d = Definition("rust_combat_01", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North);
            Set(d, "_minDepth", 3);
            Assert.IsFalse(d.IsAvailableAtDepth(2));
            Assert.IsTrue(d.IsAvailableAtDepth(3));
            Assert.IsTrue(d.IsAvailableAtDepth(999), "MaxDepth 0 = unlimited.");
            Set(d, "_maxDepth", 5);
            Assert.IsTrue(d.IsAvailableAtDepth(5));
            Assert.IsFalse(d.IsAvailableAtDepth(6));
        }

        // ---- Acceptance 2: sockets cardinal, grid-aligned, deterministic ----

        [Test]
        public void DoorDirections_AreExactlyFourCardinals_WithOpposites()
        {
            CollectionAssert.AreEquivalent(new[] { DoorDirection.North, DoorDirection.South, DoorDirection.East, DoorDirection.West }, System.Enum.GetValues(typeof(DoorDirection)));
            Assert.AreEqual(DoorDirection.South, DoorDirections.Opposite(DoorDirection.North));
            Assert.AreEqual(DoorDirection.West, DoorDirections.Opposite(DoorDirection.East));
            Assert.AreEqual(Vector2Int.up, DoorDirections.Step(DoorDirection.North));
        }

        [Test]
        public void Sockets_CoverEdgeCells_SnapToGrid_AndMatchOppositeSameWidthOnly()
        {
            var definition = Definition("metro_combat_small_01", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North, DoorDirection.East);
            var room = Room(definition, new Vector2Int(16, 12));
            var north = Socket(room, DoorDirection.North, new Vector2Int(7, 11));
            var east = Socket(room, DoorDirection.East, new Vector2Int(15, 5));

            CollectionAssert.AreEqual(new[] { new Vector2Int(7, 11), new Vector2Int(8, 11) }, north.Cells());
            CollectionAssert.AreEqual(new[] { new Vector2Int(15, 5), new Vector2Int(15, 6) }, east.Cells());
            Assert.IsTrue(north.LiesOnEdge(room.Size));
            Assert.IsTrue(east.LiesOnEdge(room.Size));
            Assert.AreEqual(new Vector3(8f, 11.5f, 0f), north.transform.localPosition, "Centered on the two door tiles.");
            Assert.AreEqual(new Vector3(15.5f, 6f, 0f), east.transform.localPosition);

            var otherRoom = Room(definition, new Vector2Int(16, 12));
            var south = Socket(otherRoom, DoorDirection.South, new Vector2Int(3, 0));
            var southWide = Socket(otherRoom, DoorDirection.South, new Vector2Int(6, 0), 3);
            Assert.IsTrue(north.IsCompatibleWith(south));
            Assert.IsFalse(north.IsCompatibleWith(southWide), "Width must match.");
            Assert.IsFalse(north.IsCompatibleWith(east), "Only opposite directions connect.");
            Assert.IsFalse(north.IsCompatibleWith(north));

            var ordered = room.GetSockets();
            Assert.AreEqual(2, ordered.Count);
            Assert.AreEqual(DoorDirection.North, ordered[0].Direction);
            Assert.AreSame(east, room.GetSocket(DoorDirection.East));
            Assert.IsNull(room.GetSocket(DoorDirection.West));
            Assert.IsEmpty(RoomSchemaValidator.ValidateRoom(room));
        }

        [Test]
        public void Validator_RejectsSocketsOffTheirEdge_UndeclaredDirections_AndDuplicates()
        {
            var definition = Definition("metro_combat_small_02", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North);
            var room = Room(definition, new Vector2Int(16, 12));
            Socket(room, DoorDirection.North, new Vector2Int(7, 10));
            Socket(room, DoorDirection.North, new Vector2Int(2, 11));
            Socket(room, DoorDirection.West, new Vector2Int(0, 4));
            Socket(room, DoorDirection.South, new Vector2Int(15, 0));

            var problems = RoomSchemaValidator.ValidateRoom(room);

            Assert.IsTrue(problems.Any(p => p.Contains("North socket at (7, 10)") && p.Contains("not on the North edge")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("more than one North socket")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("West socket") && p.Contains("not declared")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("South socket at (15, 0)") && p.Contains("not on the South edge")), "2-wide socket at y=0 spans (15,0),(15,1) — second tile leaves the edge.");
        }

        // ---- Acceptance 3: markers by role ----

        [Test]
        public void Markers_AreQueriedByTypedRole_NotByName_AndReserveIntegerFootprints()
        {
            var definition = Definition("labs_combat_small_01", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North);
            var room = Room(definition, new Vector2Int(16, 12));
            Socket(room, DoorDirection.North, new Vector2Int(7, 11));
            var spawn = Marker(room, RoomMarkerRole.PlayerSpawn, new Vector2Int(2, 2));
            spawn.name = "totally misleading name";
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(10, 8));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(4, 8));
            var chest = Marker(room, RoomMarkerRole.ChestSpawn, new Vector2Int(12, 9), new Vector2Int(2, 1));

            Assert.AreEqual(1, room.GetMarkers(RoomMarkerRole.PlayerSpawn).Count);
            Assert.AreSame(spawn, room.GetMarkers(RoomMarkerRole.PlayerSpawn)[0]);
            var enemies = room.GetMarkers(RoomMarkerRole.EnemySpawn);
            Assert.AreEqual(2, enemies.Count);
            Assert.AreEqual(new Vector2Int(4, 8), enemies[0].Cell, "Deterministic cell ordering.");
            Assert.AreEqual(new RectInt(12, 9, 2, 1), chest.Rect);
            Assert.AreEqual(new Vector3(13f, 9.5f, 0f), chest.transform.localPosition, "Footprint center snapped to grid.");
            Assert.AreEqual(new Vector3(2.5f, 2.5f, 0f), spawn.transform.localPosition);
            Assert.IsEmpty(RoomSchemaValidator.ValidateRoom(room));
        }

        [Test]
        public void Validator_RejectsMarkersOutsideBounds_AndOverlappingReservedFootprints()
        {
            var definition = Definition("labs_combat_small_02", RoomType.Combat, RoomSizeClass.Small, new Vector2Int(16, 12), DoorDirection.North);
            var room = Room(definition, new Vector2Int(16, 12));
            Socket(room, DoorDirection.North, new Vector2Int(7, 11));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(15, 11), new Vector2Int(2, 2));
            Marker(room, RoomMarkerRole.ChestSpawn, new Vector2Int(5, 5), new Vector2Int(2, 2));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(6, 6));
            Marker(room, RoomMarkerRole.Walkable, new Vector2Int(6, 6));

            var problems = RoomSchemaValidator.ValidateRoom(room);

            Assert.IsTrue(problems.Any(p => p.Contains("EnemySpawn marker at (15, 11)") && p.Contains("leaves")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("overlaps") && p.Contains("ChestSpawn marker at (5, 5)") && p.Contains("EnemySpawn marker at (6, 6)")), string.Join("\n", problems));
            Assert.IsFalse(problems.Any(p => p.Contains("Walkable")), "Walkable annotations may share cells.");
        }

        // ---- Acceptance 4: no generator ----

        [Test]
        public void FixturePrefab_FollowsContract_AndNoGeneratorTypeExists()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePrefabPath);
            var room = prefab.GetComponent<RoomRoot>();
            Assert.IsNotNull(room);
            Assert.AreEqual(new Vector2Int(16, 12), room.Size);
            Assert.IsEmpty(RoomSchemaValidator.ValidateRoom(room));
            Assert.AreEqual(2, room.GetSockets().Count);
            Assert.AreEqual(1, room.GetMarkers(RoomMarkerRole.PlayerSpawn).Count);
            Assert.AreEqual(2, room.GetMarkers(RoomMarkerRole.EnemySpawn).Count);
            Assert.AreEqual(1, room.GetMarkers(RoomMarkerRole.ChestSpawn).Count);
            Assert.AreEqual(1, room.GetMarkers(RoomMarkerRole.Blocked).Count);
            Assert.AreEqual(1, room.GetMarkers(RoomMarkerRole.Hazard).Count);

            // The room contract layer never generates geometry: nothing in RuinRail.Dungeon.Rooms paints tiles or instantiates prefabs.
            var roomsNamespaceTypes = typeof(RoomRoot).Assembly.GetTypes().Where(t => t.Namespace == "RuinRail.Dungeon.Rooms");
            Assert.IsFalse(roomsNamespaceTypes.Any(t => t.Name.Contains("Generator") || t.Name.Contains("Assembler")),
                "Room schema/validation stays free of generation; graph generation lives in RuinRail.Dungeon.Generation (TASK 024).");
        }
    }
}
