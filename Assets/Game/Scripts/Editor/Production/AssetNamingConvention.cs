using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Presentation.Animation;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 151 — where a final asset for a given manifest role must live, and what already binds it at runtime.
    ///
    /// The point of fixing this is requirement 6: an approved asset replaces a placeholder by landing at its
    /// convention path and being wired into the binding point that already exists. No gameplay id, definition or
    /// serialized reference changes. Every binding point named here was built in TASK 131-148; this type invents none.
    ///
    /// Folder roots follow the layout already in the repository (Assets/Game/Art, Assets/Game/Audio), so no parallel
    /// asset root is created.
    /// </summary>
    public static class AssetNamingConvention
    {
        public const string ArtRoot = "Assets/Game/Art";
        public const string AudioRoot = "Assets/Game/Audio";

        /// <summary>Where a role's source file belongs, and which existing runtime object binds it.</summary>
        public sealed class Placement
        {
            public string ExpectedSourcePath = string.Empty;
            public string BindingPoint = string.Empty;
            /// <summary>How a sheet is cut, when the role is a sheet rather than a single sprite.</summary>
            public string Slicing = string.Empty;
            public bool HasConvention => ExpectedSourcePath.Length > 0;
        }

        /// <summary>
        /// Naming is lowercase, underscore-separated, and derived from the stable role id — never from a display name
        /// (coding rule 9). A role id that already contains dots keeps them as path separators where it reads better.
        /// </summary>
        public static string FileStem(string roleId) => roleId.Replace('.', '_').ToLowerInvariant();

        public static Placement For(string category, string roleId)
        {
            if (string.IsNullOrEmpty(roleId)) return new Placement();

            switch (category)
            {
                case "Character source sprites":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Characters/{FileStem(roleId)}/{FileStem(roleId)}_sheet.png",
                        BindingPoint = "CharacterAnimationSet clips (assigned by AnimationSetImporter)",
                        Slicing = $"Grid by cell: 8 rows, one per BodyFacing8 in enum order ({string.Join(", ", AnimationRules.AllFacings)}); columns are frames."
                    };

                case "Character animation clip roles":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Characters/{FileStem(roleId)}/{FileStem(roleId)}_animation_set.asset",
                        BindingPoint = "CharacterAnimationSet.ActorId == role id; SpriteAnimator resolves clips by Key/Facing",
                        Slicing = "One SpriteAnimationClip per clip key x facing, 8-12 fps (art/103)."
                    };

                case "Weapon sprites":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Weapons/{FileStem(roleId)}.png",
                        BindingPoint = "WeaponVisualDriver sprite on the WeaponPivot",
                        Slicing = "Single sprite, drawn pointing +X (east); the driver rotates it through 360 degrees."
                    };

                case "Item icons":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Icons/Items/{FileStem(roleId)}.png",
                        BindingPoint = "ItemDefinition icon field (TASK 158 must add this seam; it does not exist yet)",
                        Slicing = "Single sprite."
                    };

                case "Biome tiles":
                {
                    var (biome, rest) = Split(roleId);
                    var tile = rest.StartsWith("tile.", StringComparison.Ordinal) ? rest.Substring("tile.".Length) : rest;
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Tiles/{biome}/{FileStem(biome)}_{FileStem(tile)}.png",
                        BindingPoint = "Tile asset beside the png, referenced by the baked room prefabs' Tilemaps",
                        Slicing = "32x32 grid (art/101). Multiple only when the role ships a variant set."
                    };
                }

                case "Biome props and dressing":
                {
                    var (biome, package) = Split(roleId);
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Props/{biome}/{FileStem(package)}/",
                        BindingPoint = "Room prefab SpriteRenderers on the approved SortingRole layers",
                        Slicing = "Package folder; each sprite sits on the 32 px grid or a whole multiple of it."
                    };
                }

                case "Biome lighting":
                {
                    var (biome, _) = Split(roleId);
                    return new Placement
                    {
                        ExpectedSourcePath = $"Assets/Game/ScriptableObjects/Presentation/Lighting_{biome}.asset",
                        BindingPoint = "BiomeLightingProfile, already referenced by the expedition scene composer",
                        Slicing = "Existing asset; final art tunes its values in place, keeping the asset GUID."
                    };
                }

                case "World objects and base presentation":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/World/{FileStem(roleId)}.png",
                        BindingPoint = "SpriteRenderer on the existing runtime component named in the manifest",
                        Slicing = "Single sprite, or Multiple when the object has authored states."
                    };

                case "UI skin":
                    return roleId == "ui.font.pixel"
                        ? new Placement
                        {
                            ExpectedSourcePath = $"{ArtRoot}/Fonts/ruinrail_pixel.ttf",
                            BindingPoint = "UiKit.Font(), which currently returns the builtin LegacyRuntime.ttf",
                            Slicing = "Font asset. Its licence must be recorded in production/asset_provenance.json."
                        }
                        : new Placement
                        {
                            ExpectedSourcePath = $"{ArtRoot}/UI/{FileStem(roleId)}.png",
                            BindingPoint = "UiKit / the view that owns the role",
                            Slicing = "Single sprite; 9-sliced via sprite border for frames and bars."
                        };

                case "VFX":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Vfx/{FileStem(roleId)}.png",
                        BindingPoint = "EffectPool.Spawn kind, via CombatFeedback or TelegraphIndicator",
                        Slicing = "Multiple when the effect is a frame strip; frames read left to right."
                    };

                case "Projectile visuals":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{ArtRoot}/Vfx/{FileStem(roleId)}.png",
                        BindingPoint = "ProjectileVisualCatalog profile (frames/trail), resolved by ProjectileVisual from ProjectileSpawnData.VisualId",
                        Slicing = "Frame strip (1 or 2 cells), authored pointing +X, pivot at the head."
                    };

                case "SFX":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{AudioRoot}/Sfx/{FileStem(roleId)}.wav",
                        BindingPoint = "AudioEventDefinition.Clips for this event id, in AudioEventCatalog",
                        Slicing = "One or more clips; AudioService picks a variation per play."
                    };

                case "Music":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{AudioRoot}/Music/{FileStem(roleId)}.ogg",
                        BindingPoint = "MusicCatalog entry for this MusicRole",
                        Slicing = "Full track, loop-safe."
                    };

                case "Stingers":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{AudioRoot}/Stingers/{FileStem(roleId)}.ogg",
                        BindingPoint = "MusicCatalog entry for this StingerRole",
                        Slicing = "One-shot; never looped."
                    };

                case "Ambience":
                    return new Placement
                    {
                        ExpectedSourcePath = $"{AudioRoot}/Ambience/{FileStem(roleId)}.ogg",
                        BindingPoint = "MusicCatalog ambience entry for this biome",
                        Slicing = "Seamless loop."
                    };

                case "Network prefab":
                    return new Placement
                    {
                        ExpectedSourcePath = "Assets/Game/Prefabs/Network/PlayerNetworkEntity.prefab",
                        BindingPoint = "NgoPlayerEntityFactory at composition, plus a DefaultNetworkPrefabs.asset entry",
                        Slicing = "Prefab, not a sprite; authored by TASK 180."
                    };

                default:
                    return new Placement();
            }
        }

        /// <summary>Splits "RuinedMetro.tile.floor" into ("RuinedMetro", "tile.floor").</summary>
        private static (string Head, string Tail) Split(string roleId)
        {
            var dot = roleId.IndexOf('.');
            return dot < 0 ? (roleId, string.Empty) : (roleId.Substring(0, dot), roleId.Substring(dot + 1));
        }

        /// <summary>Every art/audio folder the convention can place a role into. Used to document and validate the layout.</summary>
        public static IReadOnlyList<string> ConventionFolders => new[]
        {
            $"{ArtRoot}/Characters", $"{ArtRoot}/Weapons", $"{ArtRoot}/Icons/Items", $"{ArtRoot}/Tiles",
            $"{ArtRoot}/Props", $"{ArtRoot}/World", $"{ArtRoot}/UI", $"{ArtRoot}/Fonts", $"{ArtRoot}/Vfx",
            $"{AudioRoot}/Sfx", $"{AudioRoot}/Music", $"{AudioRoot}/Stingers", $"{AudioRoot}/Ambience"
        };
    }
}
