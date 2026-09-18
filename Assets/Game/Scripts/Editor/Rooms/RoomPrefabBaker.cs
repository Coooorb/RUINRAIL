using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// Bakes a RoomLayoutSpec into a static room prefab (all seven tilemap layers, sockets, markers, RoomRoot) and its
    /// RoomDefinition asset, using placeholder tiles until final biome art arrives. Re-running overwrites in place so
    /// asset GUIDs (and every reference to them) stay stable.
    /// </summary>
    public static class RoomPrefabBaker
    {
        public const string HazardDefinitionPath = "Assets/Game/ScriptableObjects/Combat/Hazard_ElectrifiedRail.asset";

        public sealed class BakeResult
        {
            public RoomLayoutSpec Spec;
            public RoomDefinition Definition;
            public GameObject Prefab;
            public string PrefabPath;
            public string DefinitionPath;
        }

        public static List<BakeResult> BakeAll(IEnumerable<RoomLayoutSpec> specs, string prefabFolder, string definitionFolder)
        {
            var results = new List<BakeResult>();
            foreach (var spec in specs) results.Add(Bake(spec, prefabFolder, definitionFolder));
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return results;
        }

        public static BakeResult Bake(RoomLayoutSpec spec, string prefabFolder, string definitionFolder)
        {
            Directory.CreateDirectory(prefabFolder);
            Directory.CreateDirectory(definitionFolder);
            var assetName = ToAssetName(spec.Id);
            var prefabPath = $"{prefabFolder}/{assetName}.prefab";
            var definitionPath = $"{definitionFolder}/Room_{assetName}.asset";

            var floor = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Floor", new Color(0.35f, 0.35f, 0.38f), Tile.ColliderType.None);
            var floorDetail = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_FloorDetail", new Color(0.30f, 0.32f, 0.36f), Tile.ColliderType.None);
            var wall = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Wall", new Color(0.55f, 0.45f, 0.35f), Tile.ColliderType.Grid);
            var obstacle = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Obstacle", new Color(0.4f, 0.5f, 0.6f), Tile.ColliderType.Grid);
            var hazard = PlaceholderTileAuthoring.GetOrCreateTile("Placeholder_Hazard", new Color(0.8f, 0.3f, 0.2f), Tile.ColliderType.Grid);
            var hazardPath = string.IsNullOrEmpty(spec.HazardDefinitionPath) ? HazardDefinitionPath : spec.HazardDefinitionPath;
            var hazardDefinition = AssetDatabase.LoadAssetAtPath<HazardDefinition>(hazardPath);
            if (hazardDefinition == null) throw new FileNotFoundException("Hazard definition asset missing.", hazardPath);

            var definition = GetOrCreateDefinition(spec, definitionPath);

            var grid = RoomGridBuilder.CreateRoomGrid(assetName);
            var floorMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            var detailMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.FloorDetail);
            var wallMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var obstacleMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
            var hazardMap = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);

            for (var x = 0; x < spec.Size.x; x++)
            for (var y = 0; y < spec.Size.y; y++)
            {
                var cell = GridCoordinates.ToTilemapCell(new Vector2Int(x, y));
                if (spec.HasFloor(x, y)) floorMap.SetTile(cell, floor);
                if (spec.IsFloorDetail(x, y)) detailMap.SetTile(cell, floorDetail);
                if (spec.IsWall(x, y)) wallMap.SetTile(cell, wall);
                else if (spec.IsObstacle(x, y)) obstacleMap.SetTile(cell, obstacle);
                else if (spec.IsHazard(x, y)) hazardMap.SetTile(cell, hazard);
            }

            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, spec.Size);

            var sockets = new GameObject("DoorSockets").transform;
            sockets.SetParent(grid.transform, false);
            foreach (var (direction, cell, width) in spec.Sockets())
            {
                var socket = new GameObject($"Door_{direction}").AddComponent<DoorSocket>();
                socket.transform.SetParent(sockets, false);
                socket.Configure(direction, cell, width);
                socket.SnapToGrid();
            }

            var groups = new Dictionary<RoomMarkerRole, Transform>();
            foreach (var (role, cell) in spec.Markers())
            {
                if (!groups.TryGetValue(role, out var parent))
                {
                    parent = new GameObject($"{role}Points").transform;
                    parent.SetParent(grid.transform, false);
                    groups[role] = parent;
                }

                AddMarker(parent, role, cell);
            }

            var strips = spec.HazardStrips().ToList();
            if (strips.Count > 0)
            {
                var logic = new GameObject("LogicMarkers").transform;
                logic.SetParent(grid.transform, false);
                foreach (var strip in strips)
                {
                    var marker = AddMarker(logic, RoomMarkerRole.Hazard, strip.position, strip.size);
                    marker.gameObject.AddComponent<RoomHazard>().SetDefinition(hazardDefinition);
                }
            }

            var prefab = PrefabUtility.SaveAsPrefabAsset(grid.gameObject, prefabPath);
            Object.DestroyImmediate(grid.gameObject);

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);

            return new BakeResult { Spec = spec, Definition = definition, Prefab = prefab, PrefabPath = prefabPath, DefinitionPath = definitionPath };
        }

        public static string ToAssetName(string id)
        {
            return string.Join("_", id.Split('_').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1)));
        }

        private static RoomDefinition GetOrCreateDefinition(RoomLayoutSpec spec, string path)
        {
            var definition = AssetDatabase.LoadAssetAtPath<RoomDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<RoomDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_id").stringValue = spec.Id;
            serialized.FindProperty("_biome").enumValueIndex = (int)spec.Biome;
            serialized.FindProperty("_roomType").enumValueIndex = (int)spec.RoomType;
            serialized.FindProperty("_sizeClass").enumValueIndex = (int)spec.SizeClass;
            serialized.FindProperty("_dimensions").vector2IntValue = spec.Size;
            serialized.FindProperty("_allowsAuthoredSizeVariation").boolValue = false;
            serialized.FindProperty("_difficulty").intValue = spec.Difficulty;
            serialized.FindProperty("_minDepth").intValue = spec.MinDepth;
            serialized.FindProperty("_maxDepth").intValue = spec.MaxDepth;
            serialized.FindProperty("_supportsElite").boolValue = spec.SupportsElite;
            serialized.FindProperty("_selectionWeight").floatValue = spec.Weight;
            var doors = spec.SupportedDoors().OrderBy(d => d).ToList();
            var doorsProperty = serialized.FindProperty("_supportedDoors");
            doorsProperty.arraySize = doors.Count;
            for (var i = 0; i < doors.Count; i++) doorsProperty.GetArrayElementAtIndex(i).enumValueIndex = (int)doors[i];
            var tags = serialized.FindProperty("_tags");
            tags.arraySize = spec.Tags.Count;
            for (var i = 0; i < spec.Tags.Count; i++) tags.GetArrayElementAtIndex(i).stringValue = spec.Tags[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static RoomMarker AddMarker(Transform parent, RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            var marker = new GameObject($"{role}_{cell.x}_{cell.y}").AddComponent<RoomMarker>();
            marker.transform.SetParent(parent, false);
            marker.Configure(role, cell, footprint);
            marker.SnapToGrid();
            return marker;
        }
    }
}
