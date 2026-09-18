using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Grid
{
    /// <summary>
    /// Validates that a room Grid follows the fixed layer contract: every required layer exists by exact name with a
    /// Tilemap, blocking layers carry solid collision + EnvironmentObstacle, Hazards is a trigger, cell size is one tile.
    /// Returns human-readable problems instead of throwing so authoring tools and tests can list every issue at once.
    /// </summary>
    public static class RoomTilemapValidator
    {
        public static List<string> Validate(UnityEngine.Grid grid)
        {
            var problems = new List<string>();
            if (grid == null)
            {
                problems.Add("Room has no Grid component.");
                return problems;
            }

            var expectedCell = new Vector3(GridConstants.TileWorldSize, GridConstants.TileWorldSize, 0f);
            if ((grid.cellSize - expectedCell).sqrMagnitude > 1e-6f)
            {
                problems.Add($"Grid cell size must be {expectedCell} (one 32px tile per unit) but is {grid.cellSize}.");
            }

            if (grid.transform.localScale != Vector3.one)
            {
                problems.Add($"Grid transform scale must be (1,1,1) but is {grid.transform.localScale}.");
            }

            foreach (var layer in RoomTilemapLayers.All)
            {
                var name = RoomTilemapLayers.NameOf(layer);
                var child = grid.transform.Find(name);
                if (child == null)
                {
                    problems.Add($"Missing Tilemap layer '{name}'.");
                    continue;
                }

                if (child.GetComponent<Tilemap>() == null)
                {
                    problems.Add($"Layer '{name}' has no Tilemap component.");
                }

                if (child.localScale != Vector3.one || child.localPosition != Vector3.zero)
                {
                    problems.Add($"Layer '{name}' must sit at local origin with scale (1,1,1).");
                }

                var collider = child.GetComponent<TilemapCollider2D>();
                if (RoomTilemapLayers.BlocksMovement(layer))
                {
                    if (collider == null)
                    {
                        problems.Add($"Blocking layer '{name}' needs a TilemapCollider2D.");
                    }
                    else if (collider.isTrigger)
                    {
                        problems.Add($"Blocking layer '{name}' collider must not be a trigger.");
                    }

                    if (child.GetComponent<EnvironmentObstacle>() == null)
                    {
                        problems.Add($"Blocking layer '{name}' needs EnvironmentObstacle so projectiles terminate on it.");
                    }
                }
                else if (RoomTilemapLayers.IsTrigger(layer))
                {
                    if (collider == null || !collider.isTrigger)
                    {
                        problems.Add($"Hazard layer '{name}' needs a trigger TilemapCollider2D.");
                    }
                }
                else if (collider != null && !collider.isTrigger)
                {
                    problems.Add($"Layer '{name}' must not carry solid collision.");
                }
            }

            return problems;
        }
    }
}
