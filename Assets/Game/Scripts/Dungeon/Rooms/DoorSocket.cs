using RuinRail.Dungeon.Grid;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// One cardinal connection point on the room edge. <see cref="Cell"/> is the first door tile (grid coordinate inside
    /// room bounds); <see cref="Width"/> extends along the edge (+x for North/South, +y for East/West). Assembly connects
    /// sockets of opposite direction and equal width on exact grid coordinates; it never approximates alignment.
    /// </summary>
    public sealed class DoorSocket : MonoBehaviour
    {
        public const int DefaultWidth = 2;

        [SerializeField] private DoorDirection _direction;
        [SerializeField] private Vector2Int _cell;
        [SerializeField, Min(1)] private int _width = DefaultWidth;

        public DoorDirection Direction => _direction;
        public Vector2Int Cell => _cell;
        public int Width => Mathf.Max(1, _width);

        public void Configure(DoorDirection direction, Vector2Int cell, int width = DefaultWidth)
        {
            _direction = direction;
            _cell = cell;
            _width = Mathf.Max(1, width);
            SnapToGrid();
        }

        /// <summary>All door tiles covered by this socket, in edge order.</summary>
        public Vector2Int[] Cells()
        {
            var cells = new Vector2Int[Width];
            var along = DoorDirections.IsHorizontalEdge(_direction) ? Vector2Int.right : Vector2Int.up;
            for (var i = 0; i < Width; i++)
            {
                cells[i] = _cell + along * i;
            }

            return cells;
        }

        /// <summary>Sockets are compatible when they face each other with the same width.</summary>
        public bool IsCompatibleWith(DoorSocket other)
        {
            return other != null && other.Direction == DoorDirections.Opposite(_direction) && other.Width == Width;
        }

        /// <summary>True when the socket sits on the correct edge of a room of the given size.</summary>
        public bool LiesOnEdge(Vector2Int roomSize)
        {
            foreach (var cell in Cells())
            {
                if (!GridCoordinates.IsInsideBounds(cell, roomSize))
                {
                    return false;
                }

                var onEdge = _direction switch
                {
                    DoorDirection.North => cell.y == roomSize.y - 1,
                    DoorDirection.South => cell.y == 0,
                    DoorDirection.East => cell.x == roomSize.x - 1,
                    DoorDirection.West => cell.x == 0,
                    _ => false
                };
                if (!onEdge)
                {
                    return false;
                }
            }

            return true;
        }

        public void SnapToGrid()
        {
            var first = GridCoordinates.CellToWorldCenter(_cell);
            var last = GridCoordinates.CellToWorldCenter(Cells()[Width - 1]);
            transform.localPosition = (first + last) * 0.5f;
        }
    }
}
