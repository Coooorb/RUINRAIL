using RuinRail.Core;
using System.IO;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// Authors the small grid fixture room (Small class 16x12): floor everywhere, wall ring with North/East door gaps,
    /// one obstacle block, one hazard strip, plus RoomRoot + definition + sockets + markers following the room contract.
    /// It is a fixture for grid/schema/collision tests and tooling — not a biome room.
    /// </summary>
    public static class GridTestRoomFixtureAuthoring
    {
        public const string PrefabFolder = "Assets/Game/Prefabs/Rooms/_Test";
        public const string PrefabPath = PrefabFolder + "/GridTestRoom.prefab";
        public const string DefinitionFolder = "Assets/Game/ScriptableObjects/Rooms/_Test";
        public const string DefinitionPath = DefinitionFolder + "/Room_GridTestRoom.asset";
        public const string DefinitionId = "test_grid_small_01";
        public const string HazardDefinitionPath = "Assets/Game/ScriptableObjects/Combat/Hazard_ElectrifiedRail.asset";
        public static readonly Vector2Int RoomSize = new(16, 12);
        public static readonly RectInt ObstacleBlock = new(6, 5, 2, 2);
        public static readonly RectInt HazardStrip = new(10, 3, 3, 1);
        public static readonly Vector2Int NorthDoorCell = new(7, 11);
        public static readonly Vector2Int EastDoorCell = new(15, 5);

        [MenuItem("RuinRail/Rooms/Rebuild Grid Test Room Fixture")]
        public static void Rebuild()
        {
            var floor = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Floor", new Color(0.35f, 0.35f, 0.38f), Tile.ColliderType.None);
            var wall = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Wall", new Color(0.55f, 0.45f, 0.35f), Tile.ColliderType.Grid);
            var obstacle = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Obstacle", new Color(0.4f, 0.5f, 0.6f), Tile.ColliderType.Grid);
            var hazard = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Hazard", new Color(0.8f, 0.3f, 0.2f), Tile.ColliderType.Grid);

            var definition = GetOrCreateDefinition();

            var grid = RoomGridBuilder.CreateRoomGrid("GridTestRoom");
            var floorMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            var wallMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var obstacleMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
            var hazardMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);

            var northDoor = new RectInt(NorthDoorCell.x, NorthDoorCell.y, DoorSocket.DefaultWidth, 1);
            var eastDoor = new RectInt(EastDoorCell.x, EastDoorCell.y, 1, DoorSocket.DefaultWidth);

            for (var x = 0; x < RoomSize.x; x++)
            {
                for (var y = 0; y < RoomSize.y; y++)
                {
                    var cell = GridCoordinates.ToTilemapCell(new Vector2Int(x, y));
                    floorMap.SetTile(cell, floor);
                    var position = new Vector2Int(x, y);
                    var isBorder = x == 0 || y == 0 || x == RoomSize.x - 1 || y == RoomSize.y - 1;
                    var isDoor = northDoor.Contains(position) || eastDoor.Contains(position);
                    if (isBorder && !isDoor)
                    {
                        wallMap.SetTile(cell, wall);
                    }
                    else if (ObstacleBlock.Contains(position))
                    {
                        obstacleMap.SetTile(cell, obstacle);
                    }
                    else if (HazardStrip.Contains(position))
                    {
                        hazardMap.SetTile(cell, hazard);
                    }
                }
            }

            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, RoomSize);

            var sockets = new GameObject("DoorSockets").transform;
            sockets.SetParent(grid.transform, false);
            AddSocket(sockets, DoorDirection.North, NorthDoorCell);
            AddSocket(sockets, DoorDirection.East, EastDoorCell);

            var playerSpawns = new GameObject("PlayerSpawnPoints").transform;
            playerSpawns.SetParent(grid.transform, false);
            AddMarker(playerSpawns, RoomMarkerRole.PlayerSpawn, new Vector2Int(2, 2));

            var enemySpawns = new GameObject("EnemySpawnPoints").transform;
            enemySpawns.SetParent(grid.transform, false);
            AddMarker(enemySpawns, RoomMarkerRole.EnemySpawn, new Vector2Int(11, 8));
            AddMarker(enemySpawns, RoomMarkerRole.EnemySpawn, new Vector2Int(4, 8));

            var lootSpawns = new GameObject("LootSpawnPoints").transform;
            lootSpawns.SetParent(grid.transform, false);
            AddMarker(lootSpawns, RoomMarkerRole.ChestSpawn, new Vector2Int(12, 9), new Vector2Int(2, 1));

            var logic = new GameObject("LogicMarkers").transform;
            logic.SetParent(grid.transform, false);
            AddMarker(logic, RoomMarkerRole.Blocked, ObstacleBlock.position, ObstacleBlock.size);
            var hazardMarker = AddMarker(logic, RoomMarkerRole.Hazard, HazardStrip.position, HazardStrip.size);
            // A Hazard marker is gameplay: it carries the authored definition (TASK 051) so validation accepts the room.
            var hazardDefinition = AssetDatabase.LoadAssetAtPath<HazardDefinition>(HazardDefinitionPath);
            if (hazardDefinition == null) throw new FileNotFoundException("Hazard definition asset missing.", HazardDefinitionPath);
            hazardMarker.gameObject.AddComponent<RoomHazard>().SetDefinition(hazardDefinition);

            Directory.CreateDirectory(PrefabFolder);
            var prefab = PrefabUtility.SaveAsPrefabAsset(grid.gameObject, PrefabPath);
            Object.DestroyImmediate(grid.gameObject);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Grid test room fixture written to {PrefabPath} with definition {DefinitionPath}.");
        }

        private static RoomDefinition GetOrCreateDefinition()
        {
            Directory.CreateDirectory(DefinitionFolder);
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<RoomDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = DefinitionId;
            serialized.FindProperty("_biome").enumValueIndex = (int)Biome.RuinedMetro;
            serialized.FindProperty("_roomType").enumValueIndex = (int)RoomType.Combat;
            serialized.FindProperty("_sizeClass").enumValueIndex = (int)RoomSizeClass.Small;
            serialized.FindProperty("_dimensions").vector2IntValue = RoomSize;
            serialized.FindProperty("_allowsAuthoredSizeVariation").boolValue = false;
            serialized.FindProperty("_difficulty").intValue = 1;
            serialized.FindProperty("_minDepth").intValue = 1;
            serialized.FindProperty("_maxDepth").intValue = 0;
            serialized.FindProperty("_supportsElite").boolValue = false;
            serialized.FindProperty("_selectionWeight").floatValue = 0f;
            var doors = serialized.FindProperty("_supportedDoors");
            doors.arraySize = 2;
            doors.GetArrayElementAtIndex(0).enumValueIndex = (int)DoorDirection.North;
            doors.GetArrayElementAtIndex(1).enumValueIndex = (int)DoorDirection.East;
            var tags = serialized.FindProperty("_tags");
            tags.arraySize = 1;
            tags.GetArrayElementAtIndex(0).stringValue = "test_fixture";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static void AddSocket(Transform parent, DoorDirection direction, Vector2Int cell)
        {
            var socket = new GameObject($"Door_{direction}").AddComponent<DoorSocket>();
            socket.transform.SetParent(parent, false);
            socket.Configure(direction, cell);
        }

        private static RoomMarker AddMarker(Transform parent, RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            var marker = new GameObject($"{role}_{cell.x}_{cell.y}").AddComponent<RoomMarker>();
            marker.transform.SetParent(parent, false);
            marker.Configure(role, cell, footprint);
            return marker;
        }
    }
}
