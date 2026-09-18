using UnityEngine;

namespace RuinRail.Dungeon.Grid
{
    /// <summary>
    /// Deterministic grid/world conversion for validators, generators and placement tooling.
    /// Cell (x, y) covers world [x, x+1) x [y, y+1); its center is (x + 0.5, y + 0.5). No transform scale involved.
    /// </summary>
    public static class GridCoordinates
    {
        public static Vector2 CellToWorldCenter(Vector2Int cell)
        {
            return new Vector2((cell.x + 0.5f) * GridConstants.TileWorldSize, (cell.y + 0.5f) * GridConstants.TileWorldSize);
        }

        public static Vector2 CellToWorldMin(Vector2Int cell)
        {
            return new Vector2(cell.x * GridConstants.TileWorldSize, cell.y * GridConstants.TileWorldSize);
        }

        public static Vector2Int WorldToCell(Vector2 world)
        {
            return new Vector2Int(
                Mathf.FloorToInt(world.x / GridConstants.TileWorldSize),
                Mathf.FloorToInt(world.y / GridConstants.TileWorldSize));
        }

        public static Vector3Int ToTilemapCell(Vector2Int cell) => new(cell.x, cell.y, 0);

        public static Vector2Int FromTilemapCell(Vector3Int cell) => new(cell.x, cell.y);

        public static Vector2 PixelsToWorld(Vector2Int pixels)
        {
            return new Vector2(pixels.x / (float)GridConstants.PixelsPerUnit, pixels.y / (float)GridConstants.PixelsPerUnit);
        }

        public static bool IsInsideBounds(Vector2Int cell, Vector2Int size)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < size.x && cell.y < size.y;
        }
    }
}
