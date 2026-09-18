using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The depth's ordinary-room loot budget (58: the Supply Chest is the common container; 55: Loot/Treasure rooms
    /// carry their own guaranteed chests). Roughly a quarter of the ordinary Combat rooms receive one Supply Chest,
    /// chosen per room by a seeded roll on the Loot stream so a seed always yields the same rooms; a small
    /// deterministic floor guarantees that no depth realises with fewer than <see cref="MinimumPerDepth"/> containers
    /// before the boss (the rooms with the lowest rolls are promoted). Start, Boss and special rooms never take part,
    /// and no room ever receives more than one.
    /// </summary>
    public static class SupplyChestPlanner
    {
        /// <summary>V1 target share of eligible ordinary rooms that carry a Supply Chest (tunable).</summary>
        public const int OrdinaryRoomPercent = 25;

        /// <summary>V1 guaranteed floor of independent ammo/loot opportunities before the boss (tunable).</summary>
        public const int MinimumPerDepth = 2;

        private const int Salt = 0x5343; // "SC"

        public static bool IsEligible(RoomNode node, DungeonGraph graph) =>
            node != null && node.Type == RoomType.Combat && node.Id != graph.StartId && node.Id != graph.BossId;

        /// <summary>The seeded 0..99 roll of one room; rooms below the percent threshold carry a chest.</summary>
        public static int RollFor(int runSeed, int depth, int nodeId) =>
            new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Loot, Salt, nodeId)).NextInt(100);

        public static HashSet<int> Plan(DungeonGraph graph, int runSeed, int depth, int percent = OrdinaryRoomPercent, int minimum = MinimumPerDepth)
        {
            var selected = new HashSet<int>();
            if (graph == null) return selected;
            var eligible = graph.Nodes.Where(n => IsEligible(n, graph)).OrderBy(n => n.Id).Select(n => (id: n.Id, roll: RollFor(runSeed, depth, n.Id))).ToList();
            foreach (var (id, roll) in eligible)
            {
                if (roll < percent) selected.Add(id);
            }

            // Floor: promote the lowest remaining rolls (deterministic, ties by id) until the minimum is met or no room is left.
            foreach (var (id, _) in eligible.Where(e => !selected.Contains(e.id)).OrderBy(e => e.roll).ThenBy(e => e.id))
            {
                if (selected.Count >= minimum) break;
                selected.Add(id);
            }

            return selected;
        }
    }

    /// <summary>
    /// Where a Supply Chest stands inside an ordinary room that has no ChestSpawn marker: a walkable floor cell that
    /// is reachable from every doorway, at least <see cref="DoorClearanceTiles"/> cells from any door socket and from
    /// the wall ring, clear of every authored marker footprint (spawn points, hazards, anchors) and their neighbours,
    /// preferring cells that touch a wall or obstacle so the container reads as placed against something. The pick
    /// among the candidates is seeded, so the same seed always puts the chest on the same cell.
    /// </summary>
    public static class SupplyChestPlacement
    {
        public const int DoorClearanceTiles = 3;
        public const int MarkerClearanceTiles = 1;
        public const int EdgeClearanceTiles = 2;
        private const int Salt = 0x5350; // "SP"

        public static IReadOnlyList<Vector2Int> Candidates(RoomRoot root, RoomLogicGrid grid = null)
        {
            var result = new List<Vector2Int>();
            if (root == null) return result;
            grid ??= RoomLogicGrid.FromRoom(root);
            var size = root.Size;
            var sockets = root.GetSockets();
            var markers = root.GetMarkers();
            var reachable = ReachableFromDoors(grid, sockets);

            for (var y = EdgeClearanceTiles; y < size.y - EdgeClearanceTiles; y++)
            {
                for (var x = EdgeClearanceTiles; x < size.x - EdgeClearanceTiles; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!grid.IsWalkable(cell) || !reachable.Contains(cell)) continue;
                    if (sockets.Any(s => s.Cells().Any(c => Chebyshev(c, cell) < DoorClearanceTiles))) continue;
                    if (markers.Any(m => m.Role != RoomMarkerRole.Walkable && Distance(m.Rect, cell) <= MarkerClearanceTiles)) continue;
                    result.Add(cell);
                }
            }

            return result;
        }

        /// <summary>The seeded cell for this room, or null when the room offers no valid cell (the planner then skips it).</summary>
        public static Vector2Int? Choose(RoomRoot root, int runSeed, int depth, int nodeId, RoomLogicGrid grid = null)
        {
            grid ??= RoomLogicGrid.FromRoom(root);
            var candidates = Candidates(root, grid);
            if (candidates.Count == 0) return null;
            var againstWall = candidates.Where(c => TouchesSolid(grid, c)).ToList();
            var pool = againstWall.Count > 0 ? againstWall : candidates.ToList();
            var random = new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Loot, Salt, nodeId));
            return pool[random.NextInt(pool.Count)];
        }

        public static bool TouchesSolid(RoomLogicGrid grid, Vector2Int cell)
        {
            foreach (var step in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                var next = cell + step;
                if (grid.IsInside(next) && !grid.IsWalkable(next)) return true;
            }

            return false;
        }

        /// <summary>Cells reachable (4-connected, walkable) from the walkable cell just inside every door socket.</summary>
        public static HashSet<Vector2Int> ReachableFromDoors(RoomLogicGrid grid, IReadOnlyList<DoorSocket> sockets)
        {
            var reachable = new HashSet<Vector2Int>();
            foreach (var socket in sockets)
            {
                var inward = -DoorDirections.Step(socket.Direction);
                foreach (var cell in socket.Cells())
                {
                    var start = cell;
                    for (var i = 0; i < 3 && !grid.IsWalkable(start); i++) start += inward;
                    if (!grid.IsWalkable(start)) continue;
                    reachable.UnionWith(grid.Reachable(start));
                }
            }

            return reachable;
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b) => Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));

        private static int Distance(RectInt rect, Vector2Int cell)
        {
            var dx = cell.x < rect.xMin ? rect.xMin - cell.x : cell.x >= rect.xMax ? cell.x - (rect.xMax - 1) : 0;
            var dy = cell.y < rect.yMin ? rect.yMin - cell.y : cell.y >= rect.yMax ? cell.y - (rect.yMax - 1) : 0;
            return Math.Max(dx, dy);
        }

        public static Vector2 WorldCenter(RoomRoot root, Vector2Int cell) => root.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cell));
    }
}
