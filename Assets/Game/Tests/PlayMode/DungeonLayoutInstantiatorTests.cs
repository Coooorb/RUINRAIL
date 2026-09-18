using RuinRail.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// Runtime never paints geometry: instantiating a layout yields exactly the prefab's authored tiles, placed at the
    /// integer offsets the assembler computed.
    /// </summary>
    public class DungeonLayoutInstantiatorTests
    {
        private readonly List<Object> _created = new();
        private Tile _tile;

        [SetUp]
        public void SetUp()
        {
            _tile = ScriptableObject.CreateInstance<Tile>();
            _tile.colliderType = Tile.ColliderType.Grid;
            _created.Add(_tile);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private RoomDefinition AuthoredRoom(string id, RoomType type, int wallTiles, params DoorDirection[] doors)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            typeof(RoomDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(definition, id);
            typeof(RoomDefinition).GetField("_roomType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(definition, type);
            typeof(RoomDefinition).GetField("_dimensions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(definition, new Vector2Int(16, 12));

            var grid = RoomGridBuilder.CreateRoomGrid(id);
            grid.gameObject.SetActive(false); // acts as the prefab template
            _created.Add(grid.gameObject);
            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            for (var i = 0; i < wallTiles; i++) walls.SetTile(new Vector3Int(i, 0, 0), _tile);
            foreach (var door in doors)
            {
                var socket = new GameObject($"Door_{door}").AddComponent<DoorSocket>();
                socket.transform.SetParent(grid.transform, false);
                socket.Configure(door, door == DoorDirection.East ? new Vector2Int(15, 5) : new Vector2Int(0, 5));
            }

            typeof(RoomDefinition).GetField("_prefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(definition, grid.gameObject);
            return definition;
        }

        [UnityTest]
        public IEnumerator Instantiate_PlacesPrefabsAtOffsets_WithExactlyAuthoredTiles()
        {
            var start = AuthoredRoom("s", RoomType.Start, 5, DoorDirection.East);
            var boss = AuthoredRoom("b", RoomType.Boss, 7, DoorDirection.West);
            var graph = new DungeonGraph(1, 1);
            var s = graph.AddNode(RoomType.Start).Id;
            var b = graph.AddNode(RoomType.Boss).Id;
            graph.AddEdge(s, b);
            graph.SetMainPath(new[] { s, b });
            var layout = new DungeonLayout(graph, Biome.RuinedMetro);
            layout.Add(new RoomPlacement(s, start, Vector2Int.zero));
            layout.Add(new RoomPlacement(b, boss, new Vector2Int(16, 0)));
            layout.AddConnection(new RoomConnection(s, DoorDirection.East, b, DoorDirection.West));

            var parent = new GameObject("Dungeon");
            _created.Add(parent);
            var rooms = DungeonLayoutInstantiator.Instantiate(layout, parent.transform);
            foreach (var r in rooms.Values) r.gameObject.SetActive(true);
            yield return null;

            Assert.AreEqual(2, rooms.Count);
            Assert.AreEqual(Vector3.zero, rooms[s].transform.position);
            Assert.AreEqual(new Vector3(16f, 0f, 0f), rooms[b].transform.position);
            Assert.AreEqual(5, CountTiles(rooms[s]));
            Assert.AreEqual(7, CountTiles(rooms[b]), "Runtime adds no tiles of its own.");
            Assert.AreEqual(parent.transform, rooms[b].transform.parent);
            Assert.AreEqual(new Vector3(16.5f, 5.5f, 0f), RoomGridBuilder.FindLayer(rooms[b].Grid, RoomTilemapLayer.Walls).GetCellCenterWorld(new Vector3Int(0, 5, 0)),
                "Boss room cell (0,5) sits at layout cell (16,5): world centre (16.5, 5.5).");
        }

        private static int CountTiles(RoomRoot room)
        {
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            walls.CompressBounds();
            var count = 0;
            foreach (var p in walls.cellBounds.allPositionsWithin)
            {
                if (walls.HasTile(p)) count++;
            }

            return count;
        }
    }
}
