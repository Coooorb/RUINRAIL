using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Binds the generated projectile sprites into the release catalog: one <see cref="ProjectileVisualCatalog"/>
    /// asset with every profile's frames and trail, the weapon-class family defaults, the per-weapon overrides for the
    /// Legendary weapons, and the per-archetype / per-attack hostile profiles written into the enemy definitions and
    /// attack definitions. Every binding is by stable id; nothing is loaded by path at runtime.
    /// </summary>
    public static class ProjectileArtIntegration
    {
        public const string CatalogPath = "Assets/Game/ScriptableObjects/Presentation/ProjectileVisualCatalog.asset";
        public const string ArtFolder = ArtIntegration.ArtRoot + "/Vfx"; // the VFX convention folder: role vfx.<id> ↔ vfx_<id>.png

        public static string SheetPath(string profileId) => $"{ArtFolder}/vfx_{profileId}.png";

        /// <summary>The sliced frames of a profile's sheet, in frame order.</summary>
        public static Sprite[] FramesOf(string profileId)
        {
            var path = SheetPath(profileId);
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>()
                .OrderBy(s => s.name.Length).ThenBy(s => s.name, System.StringComparer.Ordinal).ToArray();
        }

        public static ProjectileVisualCatalog BindCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ProjectileVisualCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath) ?? "Assets");
                catalog = ScriptableObject.CreateInstance<ProjectileVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var profiles = new List<ProjectileVisualCatalog.Profile>();
            foreach (var spec in ProjectileFactory.Specs())
            {
                var frames = FramesOf(spec.Id);
                if (frames.Length == 0) { Debug.LogError($"No frames for projectile profile {spec.Id} at {SheetPath(spec.Id)}"); continue; }
                var trail = string.IsNullOrEmpty(spec.TrailId) ? null : FramesOf(spec.TrailId).FirstOrDefault();
                profiles.Add(new ProjectileVisualCatalog.Profile { Id = spec.Id, Frames = frames, FrameSeconds = spec.FrameSeconds, Trail = trail, TrailBack = spec.TrailBack });
            }

            var families = ProjectileFactory.FamilyDefaults.Select(kv => new ProjectileVisualCatalog.FamilyDefault { Class = kv.Key, ProfileId = kv.Value }).ToList();
            catalog.EditorSet(profiles, families, ProjectileFactory.PlayerDefault, ProjectileFactory.EnemyDefault);
            BindDefinitions(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        /// <summary>Legendary weapons carry their family's Legendary variant; every hostile projectile attack names its profile.</summary>
        public static void BindDefinitions(ProjectileVisualCatalog catalog)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition"))
            {
                var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (weapon == null || !ProjectileFactory.FamilyDefaults.TryGetValue(weapon.WeaponClass, out var family)) continue;
                var wanted = !string.IsNullOrEmpty(weapon.LegendaryMechanicId) ? family + ProjectileFactory.LegendarySuffix : string.Empty;
                if (!string.IsNullOrEmpty(wanted) && catalog.Find(wanted) == null) wanted = string.Empty;
                if (weapon.ProjectileVisualId != wanted) weapon.EditorSetProjectileVisualId(wanted);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:EnemyDefinition"))
            {
                var enemy = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (enemy == null || enemy.AttackKind != EnemyAttackKind.Projectile) continue;
                var wanted = ProjectileFactory.EnemyProfiles.TryGetValue(enemy.Id, out var id) ? id : ProjectileFactory.EnemyDefault;
                if (enemy.ProjectileVisualId != wanted) enemy.EditorSetProjectileVisualId(wanted);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:EnemyAttackDefinition"))
            {
                var attack = AssetDatabase.LoadAssetAtPath<EnemyAttackDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                if (attack == null || attack.Motion != AttackMotion.Projectile) continue;
                var wanted = ProjectileFactory.AttackProfiles.TryGetValue(attack.Id, out var id) ? id : ProjectileFactory.EnemyDefault;
                if (attack.ProjectileVisualId != wanted) attack.EditorSetProjectileVisualId(wanted);
            }
        }
    }
}
