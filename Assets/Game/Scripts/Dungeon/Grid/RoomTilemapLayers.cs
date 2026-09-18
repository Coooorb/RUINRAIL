using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;

namespace RuinRail.Dungeon.Grid
{
    /// <summary>Names, sorting and collision semantics for each room layer. Names are the contract validators check.</summary>
    public static class RoomTilemapLayers
    {
        /// <summary>art/102 sorting role of each rendered layer (walls are the lower blocking geometry: below characters; AbovePlayer is the upper/foreground portion).</summary>
        public static SortingRole SortingRoleOf(RoomTilemapLayer layer)
        {
            return layer switch
            {
                RoomTilemapLayer.Floor => SortingRole.Floor,
                RoomTilemapLayer.FloorDetail => SortingRole.FloorDetail,
                RoomTilemapLayer.Walls => SortingRole.LowWall,
                RoomTilemapLayer.Obstacles => SortingRole.Obstacle,
                RoomTilemapLayer.Hazards => SortingRole.Hazard,
                RoomTilemapLayer.AbovePlayer => SortingRole.AboveCharacters,
                RoomTilemapLayer.Logic => SortingRole.Floor,
                _ => throw new ArgumentOutOfRangeException(nameof(layer))
            };
        }

        public static string SortingLayerNameOf(RoomTilemapLayer layer) => SortingConvention.LayerOf(SortingRoleOf(layer));

        /// <summary>Layer index x 1000 + order: a single comparable rank across sorting layers (validators, tests).</summary>
        public static int GlobalRank(RoomTilemapLayer layer)
        {
            var index = 0;
            for (var i = 0; i < SortingLayers.Ordered.Count; i++) if (SortingLayers.Ordered[i] == SortingLayerNameOf(layer)) index = i;
            return index * 1000 + SortingOrderOf(layer);
        }

        public static readonly IReadOnlyList<RoomTilemapLayer> All = (RoomTilemapLayer[])Enum.GetValues(typeof(RoomTilemapLayer));

        public static string NameOf(RoomTilemapLayer layer)
        {
            return layer switch
            {
                RoomTilemapLayer.Floor => "Floor",
                RoomTilemapLayer.FloorDetail => "FloorDetail",
                RoomTilemapLayer.Walls => "Walls",
                RoomTilemapLayer.Obstacles => "Obstacles",
                RoomTilemapLayer.Hazards => "Hazards",
                RoomTilemapLayer.AbovePlayer => "AbovePlayer",
                RoomTilemapLayer.Logic => "Logic",
                _ => throw new ArgumentOutOfRangeException(nameof(layer))
            };
        }

        public static bool TryParse(string name, out RoomTilemapLayer layer)
        {
            foreach (var candidate in All)
            {
                if (NameOf(candidate) == name)
                {
                    layer = candidate;
                    return true;
                }
            }

            layer = default;
            return false;
        }

        /// <summary>Order inside the layer's sorting layer (art/102); characters live on their own layer between LowProps and AboveCharacters.</summary>
        public static int SortingOrderOf(RoomTilemapLayer layer) => SortingConvention.BaseOrderOf(SortingRoleOf(layer));

        /// <summary>Walls and Obstacles block movement and projectiles.</summary>
        public static bool BlocksMovement(RoomTilemapLayer layer)
        {
            return layer == RoomTilemapLayer.Walls || layer == RoomTilemapLayer.Obstacles;
        }

        /// <summary>Hazards are trigger volumes for damage logic; they never block.</summary>
        public static bool IsTrigger(RoomTilemapLayer layer)
        {
            return layer == RoomTilemapLayer.Hazards;
        }

        /// <summary>Logic is authoring-only: never rendered at runtime.</summary>
        public static bool IsRendered(RoomTilemapLayer layer)
        {
            return layer != RoomTilemapLayer.Logic;
        }
    }
}
