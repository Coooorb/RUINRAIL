using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 051 — hazard markers are validated on the room grid and materialise footprint-sized volumes.</summary>
    public class RoomHazardTests
    {
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

        private RoomDefinition Definition(string id)
        {
            var d = ScriptableObject.CreateInstance<RoomDefinition>();
            d.name = id;
            Set(d, "_id", id);
            Set(d, "_roomType", RoomType.Combat);
            Set(d, "_sizeClass", RoomSizeClass.Small);
            Set(d, "_dimensions", new Vector2Int(16, 12));
            Set(d, "_supportedDoors", new[] { DoorDirection.North, DoorDirection.South });
            _created.Add(d);
            return d;
        }

        /// <summary>16x12 combat room (same recipe as RoomValidatorTests): floor everywhere, wall ring with N/S door gaps, two enemy spawns.</summary>
        private RoomRoot BuildRoom(RoomDefinition definition)
        {
            var grid = RoomGridBuilder.CreateRoomGrid(definition.Id);
            _created.Add(grid.gameObject);
            var room = grid.gameObject.AddComponent<RoomRoot>();
            room.Configure(definition, new Vector2Int(16, 12));
            Set(definition, "_prefab", grid.gameObject);
            var floorTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            var wallTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
            _created.Add(floorTile);
            _created.Add(wallTile);

            var floor = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var doorCells = new HashSet<Vector2Int>();
            foreach (var (dir, cell) in new[] { (DoorDirection.North, new Vector2Int(7, 11)), (DoorDirection.South, new Vector2Int(7, 0)) })
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
                    floor.SetTile(new Vector3Int(x, y, 0), floorTile);
                    var border = x == 0 || y == 0 || x == 15 || y == 11;
                    if (border && !doorCells.Contains(new Vector2Int(x, y))) walls.SetTile(new Vector3Int(x, y, 0), wallTile);
                }
            }

            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(3, 6));
            Marker(room, RoomMarkerRole.EnemySpawn, new Vector2Int(12, 6));
            return room;
        }

        private static RoomMarker Marker(RoomRoot room, RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            var marker = new GameObject($"{role}").AddComponent<RoomMarker>();
            marker.transform.SetParent(room.transform, false);
            marker.Configure(role, cell, footprint);
            return marker;
        }

        [Test]
        public void HazardMarker_RequiresADefinition_ToBeGeneratorReady()
        {
            var room = BuildRoom(Definition("metro_combat_hazard_01"));
            var marker = Marker(room, RoomMarkerRole.Hazard, new Vector2Int(6, 5), new Vector2Int(2, 2));

            var missing = RoomValidator.Validate(room);
            Assert.IsFalse(missing.IsGeneratorReady);
            Assert.IsTrue(missing.Problems.Any(p => p.Contains("Hazard marker at (6, 5)") && p.Contains("RoomHazard")), string.Join("\n", missing.Problems));

            var hazard = marker.gameObject.AddComponent<RoomHazard>();
            var noDefinition = RoomValidator.Validate(room);
            Assert.IsTrue(noDefinition.Problems.Any(p => p.Contains("no HazardDefinition")), string.Join("\n", noDefinition.Problems));

            var definition = HazardDefinition.Create("electric_rail", DamageKind.Hazard, 5, 8, 1f);
            _created.Add(definition);
            hazard.SetDefinition(definition);
            var ready = RoomValidator.Validate(room);
            Assert.IsTrue(ready.IsGeneratorReady, string.Join("\n", ready.Problems));

            // Hazards do not block walkability: the enemy spawn on the far side is still reachable.
            var logic = RoomLogicGrid.FromRoom(room);
            Assert.IsTrue(logic.IsWalkable(new Vector2Int(6, 5)));
        }

        [Test]
        public void HazardMarker_OutsideTheRoom_IsRejectedLikeAnyMarker()
        {
            var room = BuildRoom(Definition("metro_combat_hazard_02"));
            var marker = Marker(room, RoomMarkerRole.Hazard, new Vector2Int(15, 5), new Vector2Int(2, 2));
            var definition = HazardDefinition.Create("acid", DamageKind.Hazard, 4, 6, 1f);
            _created.Add(definition);
            marker.gameObject.AddComponent<RoomHazard>().SetDefinition(definition);
            var report = RoomValidator.Validate(room);
            Assert.IsFalse(report.IsGeneratorReady);
            Assert.IsTrue(report.Problems.Any(p => p.Contains("(15, 5)")), string.Join("\n", report.Problems));
        }

        [Test]
        public void RoomHazard_BuildsATriggerVolume_OfExactlyTheMarkerFootprint()
        {
            var room = BuildRoom(Definition("metro_combat_hazard_03"));
            var marker = Marker(room, RoomMarkerRole.Hazard, new Vector2Int(4, 3), new Vector2Int(3, 2));
            var definition = HazardDefinition.Create("steam", DamageKind.Hazard, 5, 8, 0.5f);
            _created.Add(definition);
            var hazard = marker.gameObject.AddComponent<RoomHazard>();
            hazard.SetDefinition(definition);

            var volume = hazard.Build();
            var box = marker.GetComponent<BoxCollider2D>();
            Assert.IsNotNull(volume);
            Assert.AreSame(definition, volume.Definition);
            Assert.IsTrue(box.isTrigger);
            Assert.AreEqual(new Vector2(3f, 2f), box.size, "One tile = one world unit.");
            Assert.AreEqual(Vector2.zero, box.offset);
            Assert.AreEqual(new Vector2(5.5f, 4f), (Vector2)marker.transform.localPosition, "Centred on the footprint.");
            Assert.AreSame(volume, hazard.Build(), "Rebuilding reuses the same components.");
            Assert.AreEqual(1, marker.GetComponents<HazardVolume>().Length);
        }
    }
}
