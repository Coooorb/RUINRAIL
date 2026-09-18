using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Validation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Items
{
    /// <summary>Editor entry point for the weapon catalog audit (menu, EditMode tests and production audits share it).</summary>
    public static class WeaponCatalogValidationTools
    {
        public const string EconomyConfigPath = "Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset";

        public static List<WeaponDefinition> LoadAllWeaponDefinitions()
        {
            return AssetDatabase.FindAssets("t:WeaponDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(d => d != null)
                .OrderBy(d => d.Id)
                .ToList();
        }

        public static LegendarySpecialRegistry LoadSpecialRegistry()
        {
            return new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(guid))));
        }

        public static WeaponCatalogReport ValidateProject()
        {
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>(EconomyConfigPath);
            return WeaponCatalogValidator.Validate(LoadAllWeaponDefinitions(), economy, LoadSpecialRegistry());
        }

        [MenuItem("RuinRail/Items/Validate Weapon Catalog")]
        public static void ValidateWeaponCatalogMenu()
        {
            var report = ValidateProject();
            foreach (var problem in report.Problems) Debug.LogError($"Weapon catalog: {problem}");
            var distribution = string.Join(", ", report.PerClass.Select(kv => $"{kv.Key} {kv.Value.normal}+{kv.Value.legendary}"));
            if (report.IsValid) Debug.Log($"{report.Summary} All classes 2+1: {distribution}");
            else Debug.LogError($"{report.Summary} Distribution: {distribution}");
        }
    }
}
