using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>Fills the Resources content catalog from the project's ScriptableObjects (TASK 147 boot flow); idempotent, never invents content.</summary>
    public static class GameContentCatalogBuilder
    {
        public const string CatalogPath = "Assets/Game/Resources/GameContentCatalog.asset";
        public const string AimAssistConfigPath = "Assets/Game/ScriptableObjects/Combat/AimAssistConfig.asset";

        [MenuItem("RuinRail/Production/Build Game Content Catalog")]
        public static void BuildMenu() => Debug.Log(string.Join("\n", Build().Problems().DefaultIfEmpty("GameContentCatalog complete.")));

        public static GameContentCatalog Build()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath) ?? "Assets/Game/Resources");
            var catalog = AssetDatabase.LoadAssetAtPath<GameContentCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<GameContentCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.PlayerBalance = Load<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            catalog.StatCaps = Load<GlobalStatCapsConfig>("Assets/Game/ScriptableObjects/Balance/GlobalStatCapsConfig.asset");
            catalog.AmmoBalance = Load<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            catalog.Economy = Load<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            catalog.Trader = Load<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            catalog.Workshop = Load<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset");
            catalog.DepthScaling = Load<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            catalog.Stagger = Load<StaggerConfig>("Assets/Game/ScriptableObjects/Balance/StaggerConfig.asset");
            catalog.Events = Load<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            catalog.Merchant = Load<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset");
            catalog.Loot = Load<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            catalog.DisplayNamePolicy = Load<DisplayNamePolicy>("Assets/Game/ScriptableObjects/Player/DisplayNamePolicy.asset");
            catalog.CameraRig = Load<CameraRigConfig>(PresentationValidator.CameraConfigPath);
            catalog.Feedback = Load<FeedbackConfig>(PresentationValidator.FeedbackConfigPath);
            catalog.AimAssist = LoadOrCreate<AimAssistConfig>(AimAssistConfigPath);
            catalog.AudioEvents = Load<AudioEventCatalog>(AudioAssetAudit.CatalogPath);
            catalog.Music = Load<MusicCatalog>(MusicAssetAudit.CatalogPath);
            catalog.Lighting = All<BiomeLightingProfile>();
            catalog.Items = All<ItemDefinition>();
            catalog.Specials = All<LegendarySpecialDefinition>();
            catalog.Enemies = All<EnemyDefinition>().Where(e => AssetDatabase.GetAssetPath(e).StartsWith("Assets/Game/ScriptableObjects/Enemies")).ToList();
            catalog.Elites = All<EliteDefinition>();
            catalog.Bosses = All<BossDefinition>();
            catalog.Rooms = All<RoomDefinition>().Where(r => !AssetDatabase.GetAssetPath(r).Contains("/_Test/") && !r.Id.StartsWith("test_")).ToList();
            catalog.AnimationSets = All<CharacterAnimationSet>();
            // Held weapon art lives at its convention path keyed by the weapon's stable id (the same rule AnimationAssetAudit counts).
            catalog.VfxSprites = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Game/Art/Vfx" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal)
                .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>()).OrderBy(s => s.name, StringComparer.Ordinal).ToList();
            catalog.DoorSkins = ((Biome[])Enum.GetValues(typeof(Biome))).Select(b => new GameContentCatalog.DoorSkin
            {
                Biome = b,
                Open = AssetDatabase.LoadAssetAtPath<Sprite>(RuinRail.EditorTools.ArtGen.DoorFactory.PathFor(b, false)),
                Locked = AssetDatabase.LoadAssetAtPath<Sprite>(RuinRail.EditorTools.ArtGen.DoorFactory.PathFor(b, true))
            }).ToList();
            // World-object art at its convention path, keyed by file stem (the key WorldObjectArt uses at runtime).
            catalog.WorldSprites = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Game/Art/World" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new GameContentCatalog.WorldSprite { Key = Path.GetFileNameWithoutExtension(p), Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(p) })
                .Where(w => w.Sprite != null).ToList();
            catalog.WeaponSprites = catalog.Items.OfType<WeaponDefinition>().Select(w => AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Game/Art/Weapons/{w.Id}.png")).Where(s => s != null).ToList();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return catalog;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

        /// <summary>A tuning asset that ships with its authored defaults when nobody has created it yet.</summary>
        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Assets");
            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        private static List<T> All<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}").Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).OrderBy(a => AssetDatabase.GetAssetPath(a), StringComparer.Ordinal).ToList();
    }
}
