using RuinRail.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Grid
{
    /// <summary>
    /// Builds the standard room Grid with all fixed Tilemap layers configured (cell size 1x1, sorting, collision
    /// semantics, EnvironmentObstacle on blocking layers). Used by authoring tools, fixtures and tests so every room
    /// shares one layer contract instead of hand-configured hierarchies.
    /// </summary>
    public static class RoomGridBuilder
    {
        public static UnityEngine.Grid CreateRoomGrid(string name, Transform parent = null)
        {
            var gridObject = new GameObject(name);
            if (parent != null)
            {
                gridObject.transform.SetParent(parent, false);
            }

            var grid = gridObject.AddComponent<UnityEngine.Grid>();
            grid.cellSize = new Vector3(GridConstants.TileWorldSize, GridConstants.TileWorldSize, 0f);
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;

            foreach (var layer in RoomTilemapLayers.All)
            {
                CreateLayer(grid, layer);
            }

            return grid;
        }

        public static Tilemap CreateLayer(UnityEngine.Grid grid, RoomTilemapLayer layer)
        {
            var layerObject = new GameObject(RoomTilemapLayers.NameOf(layer));
            layerObject.transform.SetParent(grid.transform, false);

            var tilemap = layerObject.AddComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(0.5f, 0.5f, 0f);

            var renderer = layerObject.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = RoomTilemapLayers.SortingLayerNameOf(layer);
            renderer.sortingOrder = RoomTilemapLayers.SortingOrderOf(layer);
            renderer.enabled = RoomTilemapLayers.IsRendered(layer);

            if (RoomTilemapLayers.BlocksMovement(layer))
            {
                var body = layerObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;
                var collider = layerObject.AddComponent<TilemapCollider2D>();
                collider.compositeOperation = Collider2D.CompositeOperation.Merge;
                var composite = layerObject.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                layerObject.AddComponent<EnvironmentObstacle>();
            }
            else if (RoomTilemapLayers.IsTrigger(layer))
            {
                var collider = layerObject.AddComponent<TilemapCollider2D>();
                collider.isTrigger = true;
            }

            return tilemap;
        }

        public static Tilemap FindLayer(UnityEngine.Grid grid, RoomTilemapLayer layer)
        {
            var child = grid.transform.Find(RoomTilemapLayers.NameOf(layer));
            return child == null ? null : child.GetComponent<Tilemap>();
        }
    }
}
