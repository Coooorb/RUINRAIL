using System.Collections.Generic;

namespace RuinRail.Core.Rendering
{
    /// <summary>
    /// art/102: the approved sorting layers, bottom to top. Names are the contract every renderer, validator and
    /// baked room prefab uses; the order here is the order the project's TagManager must define them in.
    /// </summary>
    public static class SortingLayers
    {
        public const string Ground = "Ground";
        public const string GroundDetails = "GroundDetails";
        public const string LowProps = "LowProps";
        public const string Characters = "Characters";
        public const string Weapons = "Weapons";
        public const string WorldProps = "WorldProps";
        public const string AboveCharacters = "AboveCharacters";
        public const string Projectiles = "Projectiles";
        public const string WorldVFX = "WorldVFX";
        public const string Loot = "Loot";
        public const string UIWorld = "UIWorld";
        public const string ScreenUI = "ScreenUI";

        public static readonly IReadOnlyList<string> Ordered = new[]
        {
            Ground, GroundDetails, LowProps, Characters, Weapons, WorldProps, AboveCharacters, Projectiles, WorldVFX, Loot, UIWorld, ScreenUI
        };
    }

    /// <summary>What a renderer is, mapped to a layer and an order rule (art/102, art/101: PPU 32).</summary>
    public enum SortingRole
    {
        Floor,
        FloorDetail,
        Hazard,
        LowWall,
        Obstacle,
        Character,
        Weapon,
        WorldProp,
        AboveCharacters,
        Projectile,
        WorldVfx,
        Loot,
        WorldUi,
        ScreenUi
    }

    public static class SortingConvention
    {
        /// <summary>art/101 pixels per unit; y-sorting quantises to whole pixels so equal-row sprites never flicker.</summary>
        public const int PixelsPerUnit = 32;

        public static string LayerOf(SortingRole role) => role switch
        {
            SortingRole.Floor => SortingLayers.Ground,
            SortingRole.FloorDetail => SortingLayers.GroundDetails,
            SortingRole.Hazard => SortingLayers.GroundDetails,
            SortingRole.LowWall => SortingLayers.LowProps,
            SortingRole.Obstacle => SortingLayers.LowProps,
            SortingRole.Character => SortingLayers.Characters,
            SortingRole.Weapon => SortingLayers.Weapons,
            SortingRole.WorldProp => SortingLayers.WorldProps,
            SortingRole.AboveCharacters => SortingLayers.AboveCharacters,
            SortingRole.Projectile => SortingLayers.Projectiles,
            SortingRole.WorldVfx => SortingLayers.WorldVFX,
            SortingRole.Loot => SortingLayers.Loot,
            SortingRole.WorldUi => SortingLayers.UIWorld,
            SortingRole.ScreenUi => SortingLayers.ScreenUI,
            _ => SortingLayers.Ground
        };

        /// <summary>Fixed order inside the layer for roles that share one (hazards above floor details, obstacles above low walls).</summary>
        public static int BaseOrderOf(SortingRole role) => role switch
        {
            SortingRole.Hazard => 10,
            SortingRole.Obstacle => 10,
            _ => 0
        };

        /// <summary>Roles whose order follows the sprite's feet position (art/102 "Y-sorting where needed").</summary>
        public static bool IsYSorted(SortingRole role) => role == SortingRole.Character || role == SortingRole.Weapon || role == SortingRole.WorldProp || role == SortingRole.Loot;

        /// <summary>Lower on screen renders on top: order = −(feet y in pixels). Whole pixels only, so the order is stable while a sprite moves within a pixel row.</summary>
        public static int YSortOrder(float feetY) => -(int)System.Math.Round(feetY * PixelsPerUnit, System.MidpointRounding.AwayFromZero);

        public static int OrderOf(SortingRole role, float feetY) => BaseOrderOf(role) + (IsYSorted(role) ? YSortOrder(feetY) : 0);
    }
}
