using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Paints every room prefab's tilemaps with its biome's final tile set.
    ///
    /// It started as a straight placeholder-for-final substitution and now also distributes the floor family across
    /// each room, because a single floor tile repeated a few hundred times is what made the environments read as
    /// wallpaper. The distribution comes from <see cref="BiomeFloorPlan"/>: quota-based, zone-clustered and derived
    /// only from the cell coordinate and a per-room seed, so a room bakes identically every time.
    ///
    /// What it still refuses to touch: cell positions, tilemap layers, colliders, and every RoomRoot/marker
    /// component. FINAL_ART_PRODUCTION_SPEC D4 requires room logic and layout to survive an art pass untouched, and
    /// every member of a floor family carries the same collider type, so swapping one for another cannot change what
    /// the room does.
    ///
    /// The pass is re-runnable: it recognises the tiles it wrote last time as well as the original placeholders, so a
    /// revised tile set lands on rooms that have already been repainted once.
    /// </summary>
    public static class RoomTileRepainter
    {
        /// <summary>Maps a placeholder tile asset name onto the logical role it stood in for.</summary>
        private static readonly Dictionary<string, TileRole> PlaceholderRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Placeholder_Floor", TileRole.Floor },
            { "Placeholder_FloorDetail", TileRole.FloorDetail },
            { "Placeholder_Wall", TileRole.Wall },
            { "Placeholder_Obstacle", TileRole.Obstacle },
            { "Placeholder_Hazard", TileRole.Hazard }
        };

        public sealed class Result
        {
            public int PrefabsTouched;
            public int CellsRepainted;
            public int PrefabsSkippedNoBiome;
            public int FloorCells;
            public int DetailCells;
            public readonly Dictionary<TileFactory.FloorFamily, int> FloorFamilyCounts =
                TileFactory.FloorFamilies.ToDictionary(f => f, _ => 0);
            public readonly List<string> Problems = new();

            /// <summary>Share of every repainted floor cell that landed on each family, across the whole room set.</summary>
            public Dictionary<TileFactory.FloorFamily, float> FloorBalance =>
                FloorFamilyCounts.ToDictionary(kv => kv.Key, kv => FloorCells == 0 ? 0f : kv.Value / (float)FloorCells);
        }

        public static Result RepaintAll(Dictionary<(TileFactory.Biome, TileRole, int), TileBase> finalTiles)
        {
            var result = new Result();

            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs/Rooms" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var biome = BiomeFromPath(path);
                if (biome == null)
                {
                    // The grid test fixture lives outside the biome folders and never ships.
                    result.PrefabsSkippedNoBiome++;
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var repainted = RepaintPrefab(root, biome.Value, SeedFor(path), finalTiles, result);
                    if (repainted > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        result.PrefabsTouched++;
                        result.CellsRepainted += repainted;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            var balance = result.FloorBalance;
            Debug.Log($"Repainted {result.CellsRepainted} cells across {result.PrefabsTouched} room prefabs " +
                      $"({result.PrefabsSkippedNoBiome} non-biome prefabs skipped). Floor balance: " +
                      string.Join(", ", balance.Select(kv => $"{kv.Key} {kv.Value:P0}")));
            return result;
        }

        /// <summary>
        /// A stable per-room seed.
        ///
        /// Taken from the asset path rather than from a counter or a GUID lookup order, so the same room always gets
        /// the same floor plan no matter what else is in the project or which order the prefabs come back in.
        /// </summary>
        public static int SeedFor(string assetPath)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var ch in assetPath ?? string.Empty)
                {
                    hash ^= ch;
                    hash *= 16777619u;
                }

                return (int)(hash & 0x7FFFFFFF);
            }
        }

        private static int RepaintPrefab(GameObject root, TileFactory.Biome biome, int seed,
            Dictionary<(TileFactory.Biome, TileRole, int), TileBase> finalTiles, Result result)
        {
            var repainted = 0;

            foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
            {
                // Collect first, then write: mutating a tilemap while enumerating its bounds is unreliable.
                var cellsByRole = new Dictionary<TileRole, List<Vector3Int>>();

                foreach (var pos in map.cellBounds.allPositionsWithin)
                {
                    var existing = map.GetTile(pos);
                    if (existing == null) continue;
                    var role = RoleOf(existing.name, biome);
                    if (role == null) continue;

                    if (!cellsByRole.TryGetValue(role.Value, out var list))
                        cellsByRole[role.Value] = list = new List<Vector3Int>();
                    list.Add(pos);
                }

                if (cellsByRole.Count == 0) continue;

                var swaps = new List<(Vector3Int pos, TileBase tile)>();

                foreach (var (role, cells) in cellsByRole)
                {
                    switch (role)
                    {
                        case TileRole.Floor:
                        {
                            // The floor is the one layer with a family, and the one that decides how the room reads.
                            var plan = BiomeFloorPlan.PlanFloor(cells, seed);
                            foreach (var (pos, family) in plan)
                            {
                                if (!TryResolve(finalTiles, biome, TileRole.Floor, (int)family, result, out var tile)) continue;
                                swaps.Add((pos, tile));
                                result.FloorCells++;
                                result.FloorFamilyCounts[family]++;
                            }

                            break;
                        }

                        case TileRole.FloorDetail:
                        {
                            var plan = BiomeFloorPlan.PlanDetail(cells, seed ^ 0x13579BDF);
                            foreach (var (pos, kind) in plan)
                            {
                                if (!TryResolve(finalTiles, biome, TileRole.FloorDetail, (int)kind, result, out var tile)) continue;
                                swaps.Add((pos, tile));
                                result.DetailCells++;
                            }

                            break;
                        }

                        default:
                        {
                            if (!TryResolve(finalTiles, biome, role, 0, result, out var tile)) break;
                            foreach (var pos in cells) swaps.Add((pos, tile));
                            break;
                        }
                    }
                }

                foreach (var (pos, tile) in swaps)
                {
                    map.SetTile(pos, tile);
                    repainted++;
                }

                if (swaps.Count > 0) EditorUtility.SetDirty(map);
            }

            return repainted;
        }

        private static bool TryResolve(Dictionary<(TileFactory.Biome, TileRole, int), TileBase> finalTiles,
            TileFactory.Biome biome, TileRole role, int variant, Result result, out TileBase tile)
        {
            if (finalTiles.TryGetValue((biome, role, variant), out tile) && tile != null) return true;

            // Fall back to the family's base member rather than leaving a cell on the tile it had: a mixed room is a
            // worse outcome than a slightly plainer one, and the problem is recorded either way.
            if (variant != 0 && finalTiles.TryGetValue((biome, role, 0), out tile) && tile != null)
            {
                result.Problems.Add($"{biome}/{role} variant {variant}: not generated, fell back to the base tile.");
                return true;
            }

            result.Problems.Add($"{biome}/{role}: no final tile generated, cell left unchanged.");
            tile = null;
            return false;
        }

        /// <summary>
        /// The logical role of a tile already sitting in a room.
        ///
        /// Recognises both the original placeholders and any tile this repainter has written before, which is what
        /// makes the pass repeatable — the first run replaced placeholders, and every run after that re-plans the
        /// floors that the previous run laid down.
        /// </summary>
        public static TileRole? RoleOf(string tileName, TileFactory.Biome biome)
        {
            if (string.IsNullOrEmpty(tileName)) return null;
            if (PlaceholderRoles.TryGetValue(tileName, out var placeholder)) return placeholder;

            var prefix = biome.ToString().ToLowerInvariant() + "_";
            if (!tileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            var rest = tileName.Substring(prefix.Length);
            // Detail first: "floor_detail" also starts with "floor".
            if (rest.StartsWith("floor_detail", StringComparison.OrdinalIgnoreCase)) return TileRole.FloorDetail;
            if (rest.StartsWith("floor", StringComparison.OrdinalIgnoreCase)) return TileRole.Floor;
            if (rest.StartsWith("wall", StringComparison.OrdinalIgnoreCase)) return TileRole.Wall;
            if (rest.StartsWith("obstacle", StringComparison.OrdinalIgnoreCase)) return TileRole.Obstacle;
            if (rest.StartsWith("hazard", StringComparison.OrdinalIgnoreCase)) return TileRole.Hazard;
            return null;
        }

        /// <summary>Room prefabs live under a folder named for their biome, which is the authoritative signal.</summary>
        private static TileFactory.Biome? BiomeFromPath(string path)
        {
            if (path.Contains("/RuinedMetro/")) return TileFactory.Biome.RuinedMetro;
            if (path.Contains("/Rustworks/")) return TileFactory.Biome.Rustworks;
            if (path.Contains("/OvergrownLabs/")) return TileFactory.Biome.OvergrownLabs;
            return null;
        }

        /// <summary>Maps the runtime Biome enum onto the art generator's, so callers can cross between them.</summary>
        public static TileFactory.Biome FromRuntime(Biome biome) => biome switch
        {
            Biome.RuinedMetro => TileFactory.Biome.RuinedMetro,
            Biome.Rustworks => TileFactory.Biome.Rustworks,
            _ => TileFactory.Biome.OvergrownLabs
        };
    }
}
