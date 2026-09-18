using RuinRail.Dungeon.Grid;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Authoring marker on an exact grid cell with an integer footprint. Roles are typed (<see cref="RoomMarkerRole"/>),
    /// never derived from object names. The generator chooses what to place at legal markers; it never invents positions.
    /// </summary>
    public sealed class RoomMarker : MonoBehaviour
    {
        [SerializeField] private RoomMarkerRole _role;
        [SerializeField] private Vector2Int _cell;
        [SerializeField] private Vector2Int _footprint = Vector2Int.one;

        public RoomMarkerRole Role => _role;
        public Vector2Int Cell => _cell;
        public Vector2Int Footprint => new(Mathf.Max(1, _footprint.x), Mathf.Max(1, _footprint.y));
        public RectInt Rect => new(_cell, Footprint);

        public void Configure(RoomMarkerRole role, Vector2Int cell, Vector2Int? footprint = null)
        {
            _role = role;
            _cell = cell;
            _footprint = footprint ?? Vector2Int.one;
            SnapToGrid();
        }

        public bool IsInsideRoom(Vector2Int roomSize)
        {
            var rect = Rect;
            return rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= roomSize.x && rect.yMax <= roomSize.y;
        }

        public bool Overlaps(RoomMarker other)
        {
            return other != null && other != this && Rect.Overlaps(other.Rect);
        }

        public Vector2 WorldCenter => GridCoordinates.CellToWorldMin(_cell) + (Vector2)Footprint * 0.5f;

        public void SnapToGrid()
        {
            transform.localPosition = WorldCenter;
        }
    }
}
