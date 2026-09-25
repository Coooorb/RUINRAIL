using System.Collections.Generic;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Deterministic variation of a room's decorative dressing.
    ///
    /// Every prop in the game is baked into its room prefab's FloorDetail tilemap, so the same room id looks
    /// pixel-identical on every visit — and the authored density is very low (1 to 8 detail tiles against a 146-tile
    /// small-room floor). This varies what is already there rather than authoring new art: the existing detail tiles
    /// are flipped and rotated per room instance, and a few extra copies of the tiles that room already uses are
    /// placed on plain floor.
    ///
    /// It cannot affect gameplay, and the rules below are what makes that true rather than a hope:
    /// <list type="bullet">
    /// <item>it writes only to FloorDetail, which has no collider and no tile occupancy — it is neither
    /// <see cref="RoomTilemapLayers.BlocksMovement"/> nor <see cref="RoomTilemapLayers.IsTrigger"/>;</item>
    /// <item>a cell is eligible only when Floor has a tile there and Walls, Obstacles, Hazards and AbovePlayer do not,
    /// so nothing is placed into geometry, a hazard or a foreground piece;</item>
    /// <item>every cell within <see cref="DoorClearanceTiles"/> of a door socket is excluded, and so is every room
    /// marker cell (player spawn, enemy spawns, chest, boss anchor, interactables) plus its immediate neighbours —
    /// so doorways, spawns, pickups and choice objects are never dressed over;</item>
    /// <item>the count added is capped by <see cref="MaxAddedFraction"/> of the room's floor, so a room can never
    /// become visually busy enough to hide a telegraph.</item>
    /// </list>
    ///
    /// Determinism: the seed is (run seed, depth, node id) through the Biome stream, so the same depth of the same run
    /// dresses identically every time it is built, and two different rooms of the same id in one layout differ.
    /// </summary>
    public static class RoomPropDressing
    {
        /// <summary>Tiles of clearance kept around every door socket cell.</summary>
        public const int DoorClearanceTiles = 2;

        /// <summary>Extra detail tiles are capped at this fraction of the room's floor cells.</summary>
        public const float MaxAddedFraction = 0.04f;

        /// <summary>Lower bound on added tiles when the fraction rounds to nothing (a small room still gets some variety).</summary>
        public const int MinAdded = 2;

        /// <summary>What one dressing pass did, for the diagnostics and the validator.</summary>
        public readonly struct Result
        {
            public Result(int transformed, int added, int eligibleCells, int patterns)
            {
                Transformed = transformed;
                Added = added;
                EligibleCells = eligibleCells;
                Patterns = patterns;
            }

            /// <summary>Authored detail tiles given a flip/rotation.</summary>
            public int Transformed { get; }
            /// <summary>Detail tiles added on plain floor.</summary>
            public int Added { get; }
            public int EligibleCells { get; }
            /// <summary>Distinct tile assets the room's dressing draws from.</summary>
            public int Patterns { get; }
        }

        private static readonly Matrix4x4[] Variants =
        {
            Matrix4x4.identity,
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1f, 1f, 1f)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, -1f, 1f)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1f, -1f, 1f)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 90f), Vector3.one),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0f, 0f, 270f), Vector3.one)
        };

        /// <summary>Dresses one room. Safe to call on a room with no FloorDetail layer: it does nothing.</summary>
        public static Result Apply(RoomRoot root, int runSeed, int depth, int nodeId)
        {
            if (root == null) return default;
            var grid = root.Grid;
            if (grid == null) return default;
            var detail = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.FloorDetail);
            var floor = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Floor);
            if (detail == null || floor == null) return default;

            var rng = new SeededRandom(SeededRandom.MixSeed(runSeed, depth, nodeId, (int)RngStream.Biome));
            var walls = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Walls);
            var obstacles = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Obstacles);
            var hazards = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.Hazards);
            var above = RoomGridBuilder.FindLayer(grid, RoomTilemapLayer.AbovePlayer);

            // 1. Vary the tiles the room already has. A flipped crate is the same crate in the same cell.
            var authored = new List<Vector3Int>();
            var palette = new List<TileBase>();
            foreach (var position in detail.cellBounds.allPositionsWithin)
            {
                var tile = detail.GetTile(position);
                if (tile == null) continue;
                authored.Add(position);
                if (!palette.Contains(tile)) palette.Add(tile);
            }

            foreach (var position in authored)
            {
                detail.SetTileFlags(position, TileFlags.None);
                detail.SetTransformMatrix(position, Variants[rng.NextInt(Variants.Length)]);
            }

            // 2. Add a few more of the room's own detail tiles on plain, unused floor.
            if (palette.Count == 0) return new Result(authored.Count, 0, 0, 0);

            var blocked = new HashSet<Vector3Int>();
            foreach (var socket in root.GetSockets())
            {
                if (socket == null) continue;
                for (var w = -DoorClearanceTiles; w <= socket.Width + DoorClearanceTiles; w++)
                for (var d = -DoorClearanceTiles; d <= DoorClearanceTiles; d++)
                {
                    var cell = DoorDirections.IsHorizontalEdge(socket.Direction)
                        ? new Vector2Int(socket.Cell.x + w, socket.Cell.y + d)
                        : new Vector2Int(socket.Cell.x + d, socket.Cell.y + w);
                    blocked.Add(new Vector3Int(cell.x, cell.y, 0));
                }
            }

            foreach (var marker in root.GetMarkers())
            {
                if (marker == null) continue;
                for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++)
                    blocked.Add(new Vector3Int(marker.Cell.x + dx, marker.Cell.y + dy, 0));
            }

            var eligible = new List<Vector3Int>();
            var floorCells = 0;
            foreach (var position in floor.cellBounds.allPositionsWithin)
            {
                if (floor.GetTile(position) == null) continue;
                floorCells++;
                if (blocked.Contains(position)) continue;
                if (detail.GetTile(position) != null) continue;
                if (walls != null && walls.GetTile(position) != null) continue;
                if (obstacles != null && obstacles.GetTile(position) != null) continue;
                if (hazards != null && hazards.GetTile(position) != null) continue;
                if (above != null && above.GetTile(position) != null) continue;
                eligible.Add(position);
            }

            var budget = Mathf.Min(eligible.Count, Mathf.Max(MinAdded, Mathf.FloorToInt(floorCells * MaxAddedFraction)));
            var added = 0;
            for (var i = 0; i < budget && eligible.Count > 0; i++)
            {
                var at = rng.NextInt(eligible.Count);
                var cell = eligible[at];
                eligible.RemoveAt(at);
                // Never two adjacent additions: a cluster of identical props is the repetition this is fixing.
                if (HasNeighbouringDetail(detail, cell)) continue;
                detail.SetTile(cell, palette[rng.NextInt(palette.Count)]);
                detail.SetTileFlags(cell, TileFlags.None);
                detail.SetTransformMatrix(cell, Variants[rng.NextInt(Variants.Length)]);
                added++;
            }

            return new Result(authored.Count, added, eligible.Count + added, palette.Count);
        }

        private static bool HasNeighbouringDetail(Tilemap detail, Vector3Int cell)
        {
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                if (detail.GetTile(new Vector3Int(cell.x + dx, cell.y + dy, 0)) != null) return true;
            }

            return false;
        }
    }
}
