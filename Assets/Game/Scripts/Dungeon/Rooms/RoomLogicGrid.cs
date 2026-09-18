using System.Collections.Generic;
using RuinRail.Dungeon.Grid;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Walkability model of one authored room derived from its tilemaps and markers: a cell is walkable when it has
    /// floor, no Walls/Obstacles tile and no Blocked/Reserved marker footprint. Hazards stay walkable (they hurt, not block).
    /// Used by validation and, later, by placement/reachability checks in assembly.
    /// </summary>
    public sealed class RoomLogicGrid
    {
        private readonly bool[,] _walkable;

        public RoomLogicGrid(Vector2Int size)
        {
            Size = size;
            _walkable = new bool[size.x, size.y];
        }

        public Vector2Int Size { get; }

        public bool IsInside(Vector2Int cell) => GridCoordinates.IsInsideBounds(cell, Size);

        public bool IsWalkable(Vector2Int cell) => IsInside(cell) && _walkable[cell.x, cell.y];

        public void SetWalkable(Vector2Int cell, bool walkable)
        {
            if (IsInside(cell))
            {
                _walkable[cell.x, cell.y] = walkable;
            }
        }

        public int WalkableCount
        {
            get
            {
                var count = 0;
                for (var x = 0; x < Size.x; x++)
                {
                    for (var y = 0; y < Size.y; y++)
                    {
                        if (_walkable[x, y]) count++;
                    }
                }

                return count;
            }
        }

        public static RoomLogicGrid FromRoom(RoomRoot room)
        {
            var grid = new RoomLogicGrid(room.Size);
            var floor = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Floor);
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            var obstacles = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Obstacles);

            for (var x = 0; x < room.Size.x; x++)
            {
                for (var y = 0; y < room.Size.y; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    var hasFloor = floor != null && floor.HasTile(cell);
                    var blocked = Has(walls, cell) || Has(obstacles, cell);
                    grid._walkable[x, y] = hasFloor && !blocked;
                }
            }

            foreach (var marker in room.GetMarkers())
            {
                if (marker.Role != RoomMarkerRole.Blocked && marker.Role != RoomMarkerRole.Reserved)
                {
                    continue;
                }

                var rect = marker.Rect;
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    for (var y = rect.yMin; y < rect.yMax; y++)
                    {
                        grid.SetWalkable(new Vector2Int(x, y), false);
                    }
                }
            }

            return grid;
        }

        /// <summary>4-connected flood fill from <paramref name="start"/>; returns every reachable walkable cell.</summary>
        public HashSet<Vector2Int> Reachable(Vector2Int start)
        {
            var visited = new HashSet<Vector2Int>();
            if (!IsWalkable(start))
            {
                return visited;
            }

            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            visited.Add(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var step in Steps)
                {
                    var next = current + step;
                    if (IsWalkable(next) && visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            return visited;
        }

        public bool AreConnected(Vector2Int a, Vector2Int b) => Reachable(a).Contains(b);

        private static bool Has(Tilemap map, Vector3Int cell) => map != null && map.HasTile(cell);

        private static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    }
}
