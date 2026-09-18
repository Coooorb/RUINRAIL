using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Decides which member of a biome's floor family each cell of a room gets.
    ///
    /// Two things have to be true at once, and picking a variant per cell at random satisfies neither. The room has to
    /// hit the spec's screen balance — roughly 60-75% calm navigable floor, structural variation next, detail last
    /// (section B7) — and the detail has to arrive in zones, so a room has quiet ground and busy ground rather than an
    /// even sprinkle of damage (section B1.5).
    ///
    /// So the plan is quota-based rather than probabilistic. Every floor cell is scored against two independent
    /// low-frequency noise fields, the cells are ranked by those scores, and the quotas are handed out from the top
    /// down. Ranking by a smooth field means the cells that win a quota are neighbours, which is what produces zones;
    /// filling a fixed quota means the proportions are exact in every room rather than approximately right on average.
    ///
    /// Everything here is a pure function of the cell coordinate and the room seed, so the same room always bakes to
    /// the same floor. Nothing is decided at play time.
    /// </summary>
    public static class BiomeFloorPlan
    {
        /// <summary>
        /// Share of an ordinary room each floor family covers.
        ///
        /// Base is the calm navigable ground the polish pass asks to dominate. Accent is the loudest member and is
        /// deliberately the smallest slice: it is a focal note, not a texture.
        /// </summary>
        public static readonly IReadOnlyDictionary<TileFactory.FloorFamily, float> FloorQuota =
            new Dictionary<TileFactory.FloorFamily, float>
            {
                { TileFactory.FloorFamily.Base, 0.66f },
                { TileFactory.FloorFamily.Worn, 0.15f },
                { TileFactory.FloorFamily.Cracked, 0.09f },
                { TileFactory.FloorFamily.Utility, 0.06f },
                { TileFactory.FloorFamily.Accent, 0.04f }
            };

        /// <summary>Share of the detail layer each detail kind covers. Grime dominates because it is the quietest.</summary>
        public static readonly IReadOnlyDictionary<TileFactory.DetailKind, float> DetailQuota =
            new Dictionary<TileFactory.DetailKind, float>
            {
                { TileFactory.DetailKind.Grime, 0.52f },
                { TileFactory.DetailKind.Marking, 0.20f },
                { TileFactory.DetailKind.Service, 0.12f },
                { TileFactory.DetailKind.Residue, 0.16f }
            };

        /// <summary>Feature size of the zone fields, in tiles. Large enough that a zone spans several tiles.</summary>
        private const float WearZoneTiles = 5.5f;
        private const float UtilityZoneTiles = 7.5f;

        /// <summary>
        /// Assigns a floor family to every cell of one room.
        ///
        /// Utility and Accent are ranked on their own field first, because a service panel or a platform edge is a
        /// built feature and has nothing to do with where the floor happens to be worn. The wear families then rank
        /// what is left, so damage clusters independently of the constructed elements.
        /// </summary>
        public static Dictionary<Vector3Int, TileFactory.FloorFamily> PlanFloor(IReadOnlyList<Vector3Int> cells, int seed)
        {
            var plan = new Dictionary<Vector3Int, TileFactory.FloorFamily>();
            if (cells == null || cells.Count == 0) return plan;

            var total = cells.Count;
            var remaining = new List<Vector3Int>(cells);

            // Constructed elements first, on the utility field.
            remaining = TakeTop(remaining, plan, TileFactory.FloorFamily.Accent,
                Quota(total, TileFactory.FloorFamily.Accent), c => Zone(c, seed ^ 0x51ED2701, UtilityZoneTiles), seed);
            remaining = TakeTop(remaining, plan, TileFactory.FloorFamily.Utility,
                Quota(total, TileFactory.FloorFamily.Utility), c => Zone(c, seed ^ 0x51ED2701, UtilityZoneTiles), seed);

            // Wear and damage next, on their own field, over whatever is left.
            remaining = TakeTop(remaining, plan, TileFactory.FloorFamily.Cracked,
                Quota(total, TileFactory.FloorFamily.Cracked), c => Zone(c, seed ^ 0x2F1B3C57, WearZoneTiles), seed);
            remaining = TakeTop(remaining, plan, TileFactory.FloorFamily.Worn,
                Quota(total, TileFactory.FloorFamily.Worn), c => Zone(c, seed ^ 0x2F1B3C57, WearZoneTiles), seed);

            foreach (var cell in remaining) plan[cell] = TileFactory.FloorFamily.Base;
            return plan;
        }

        /// <summary>Assigns a detail kind to every cell the room baker put on the detail layer.</summary>
        public static Dictionary<Vector3Int, TileFactory.DetailKind> PlanDetail(IReadOnlyList<Vector3Int> cells, int seed)
        {
            var plan = new Dictionary<Vector3Int, TileFactory.DetailKind>();
            if (cells == null || cells.Count == 0) return plan;

            var total = cells.Count;
            var remaining = new List<Vector3Int>(cells);

            foreach (var kind in new[] { TileFactory.DetailKind.Service, TileFactory.DetailKind.Marking, TileFactory.DetailKind.Residue })
            {
                var quota = Mathf.RoundToInt(total * DetailQuota[kind]);
                remaining = TakeTop(remaining, plan, kind, quota, c => Zone(c, seed ^ (int)kind * 7919, WearZoneTiles), seed);
            }

            foreach (var cell in remaining) plan[cell] = TileFactory.DetailKind.Grime;
            return plan;
        }

        /// <summary>
        /// The whole-number count of cells a family gets in a room of this size.
        ///
        /// Rounded down, and never more than one cell for a family whose share would otherwise vanish in a small room:
        /// a five-tile corridor should not receive a service panel just because the percentage rounds up.
        /// </summary>
        public static int Quota(int totalCells, TileFactory.FloorFamily family) =>
            family == TileFactory.FloorFamily.Base
                ? totalCells
                : Mathf.FloorToInt(totalCells * FloorQuota[family]);

        private static List<Vector3Int> TakeTop<TKind>(List<Vector3Int> pool, Dictionary<Vector3Int, TKind> plan,
            TKind kind, int count, Func<Vector3Int, float> score, int seed)
        {
            if (count <= 0 || pool.Count == 0) return pool;
            count = Mathf.Min(count, pool.Count);

            // Rank by the zone field; the hash only breaks ties, so neighbouring cells stay neighbours in the ranking.
            var ordered = pool
                .OrderByDescending(score)
                .ThenBy(c => Hash01(c.x, c.y, seed))
                .ToList();

            for (var i = 0; i < count; i++) plan[ordered[i]] = kind;
            return ordered.Skip(count).ToList();
        }

        /// <summary>
        /// Smooth value noise over the room grid, used to rank cells into zones.
        ///
        /// Bilinear interpolation over a lattice of <paramref name="featureTiles"/>, so neighbouring cells score
        /// similarly and a quota handed out by rank lands on a connected patch rather than on scattered cells.
        /// </summary>
        public static float Zone(Vector3Int cell, int seed, float featureTiles)
        {
            var fx = cell.x / featureTiles;
            var fy = cell.y / featureTiles;
            var x0 = Mathf.FloorToInt(fx);
            var y0 = Mathf.FloorToInt(fy);
            var tx = Smooth(fx - x0);
            var ty = Smooth(fy - y0);

            var v00 = Hash01(x0, y0, seed);
            var v10 = Hash01(x0 + 1, y0, seed);
            var v01 = Hash01(x0, y0 + 1, seed);
            var v11 = Hash01(x0 + 1, y0 + 1, seed);

            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>A stable 0..1 hash of a grid coordinate. Same input, same value, every bake.</summary>
        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                var h = x * 374761393 + y * 668265263 + seed * 2246822519;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
            }
        }

        /// <summary>
        /// Measures a planned room, so the screen-balance target can be asserted rather than assumed.
        /// </summary>
        public static Dictionary<TileFactory.FloorFamily, float> Measure(IReadOnlyDictionary<Vector3Int, TileFactory.FloorFamily> plan)
        {
            var result = TileFactory.FloorFamilies.ToDictionary(f => f, _ => 0f);
            if (plan == null || plan.Count == 0) return result;
            foreach (var family in plan.Values) result[family] += 1f;
            foreach (var family in TileFactory.FloorFamilies) result[family] /= plan.Count;
            return result;
        }

        /// <summary>
        /// How clustered a planned family is: the share of its cells that touch another cell of the same family.
        ///
        /// This is the number that separates "detail arrives in zones" from "detail is sprinkled evenly", and it is
        /// what the repetition test actually checks. A uniform random assignment scores low here by construction.
        /// </summary>
        public static float Clustering(IReadOnlyDictionary<Vector3Int, TileFactory.FloorFamily> plan, TileFactory.FloorFamily family)
        {
            var cells = plan.Where(kv => kv.Value == family).Select(kv => kv.Key).ToHashSet();
            if (cells.Count == 0) return 1f;

            var touching = cells.Count(c =>
                cells.Contains(new Vector3Int(c.x + 1, c.y, c.z)) || cells.Contains(new Vector3Int(c.x - 1, c.y, c.z)) ||
                cells.Contains(new Vector3Int(c.x, c.y + 1, c.z)) || cells.Contains(new Vector3Int(c.x, c.y - 1, c.z)));

            return touching / (float)cells.Count;
        }
    }
}
