namespace RuinRail.Dungeon.Grid
{
    /// <summary>Approved grid/pixel conventions (dungeon/50_GRID_TILE_SYSTEM.md, art/101_PIXEL_GRID_AND_SCALE.md).</summary>
    public static class GridConstants
    {
        public const int TileSizePixels = 32;
        public const int PixelsPerUnit = 32;

        /// <summary>One tile is exactly one world unit: TileSizePixels / PixelsPerUnit.</summary>
        public const float TileWorldSize = (float)TileSizePixels / PixelsPerUnit;

        public const int ReferenceResolutionWidth = 640;
        public const int ReferenceResolutionHeight = 360;
    }
}
