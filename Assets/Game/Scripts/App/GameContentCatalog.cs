using System.Collections.Generic;
using System.Linq;
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
using RuinRail.UI.Base;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The one asset a built player loads (Resources): every approved definition and balance config the boot flow
    /// composes from. Filled by the editor builder from the project's ScriptableObjects; validated against the
    /// content-count contract, never hand-edited to satisfy counts.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/App/Game Content Catalog", fileName = "GameContentCatalog")]
    public sealed class GameContentCatalog : ScriptableObject
    {
        public const string ResourcePath = "GameContentCatalog";

        [Header("Balance / configs")]
        public PlayerBalanceConfig PlayerBalance;
        public GlobalStatCapsConfig StatCaps;
        public AmmoBalanceConfig AmmoBalance;
        public EconomyConfig Economy;
        public TraderConfig Trader;
        public WorkshopConfig Workshop;
        public DepthScalingConfig DepthScaling;
        public StaggerConfig Stagger;
        public DungeonEventConfig Events;
        public DungeonMerchantConfig Merchant;
        public LootSourceCatalog Loot;
        public DisplayNamePolicy DisplayNamePolicy;
        public CameraRigConfig CameraRig;
        public FeedbackConfig Feedback;
        public AimAssistConfig AimAssist;
        public AudioEventCatalog AudioEvents;
        public MusicCatalog Music;
        /// <summary>
        /// The release network player prefab (82): the object a host spawns per session member. It is carried here
        /// because a built player has no AssetDatabase — without a runtime reference the prefab exists, validates and
        /// is registered with NGO, but no running process can ever instantiate it.
        /// </summary>
        public GameObject NetworkPlayerEntity;
        /// <summary>
        /// The session's co-op expedition channel (one network object per session; enemies, loot and rooms replicate
        /// through it by definition id). Registered with NGO next to the player prefab on every peer.
        /// </summary>
        public GameObject CoopRunLink;
        public List<BiomeLightingProfile> Lighting = new();

        [Header("Definitions")]
        public List<ItemDefinition> Items = new();
        public List<LegendarySpecialDefinition> Specials = new();
        public List<EnemyDefinition> Enemies = new();
        public List<EliteDefinition> Elites = new();
        public List<BossDefinition> Bosses = new();
        public List<RoomDefinition> Rooms = new();

        /// <summary>Every actor's CharacterAnimationSet (player, enemies, Elites, Bosses), so a built player can bind bodies at runtime.</summary>
        [Header("Presentation")]
        public List<CharacterAnimationSet> AnimationSets = new();
        /// <summary>Every weapon's held world sprite (authored +X, grip pivot), named by the weapon's stable id; the held-weapon visual resolves by that name.</summary>
        public List<Sprite> WeaponSprites = new();
        /// <summary>Every effect frame under Art/Vfx (named vfx_&lt;kind&gt;_&lt;row&gt;_&lt;frame&gt;), so the effect pool draws the accepted VFX instead of its placeholder quad.</summary>
        public List<Sprite> VfxSprites = new();
        /// <summary>Combat door skins per biome (open frame / locked shutter), drawn by the room door locks.</summary>
        public List<DoorSkin> DoorSkins = new();
        /// <summary>World-object art (chests, pickups, coins, merchant, event objects, transit car) keyed by the Art/World file stem; WorldObjectArt resolves through it.</summary>
        public List<WorldSprite> WorldSprites = new();
        /// <summary>Every in-flight projectile presentation profile plus the weapon-class family defaults; ProjectileVisual resolves through it.</summary>
        public RuinRail.Gameplay.Combat.Projectiles.ProjectileVisualCatalog ProjectileVisuals;

        [System.Serializable]
        public sealed class WorldSprite
        {
            public string Key;
            public Sprite Sprite;
        }

        /// <summary>The world-object sprite for an art key (WorldObjectArt.*); null when nothing is bound.</summary>
        public Sprite WorldSpriteFor(string key) => string.IsNullOrEmpty(key) ? null : WorldSprites.FirstOrDefault(w => w != null && w.Key == key)?.Sprite;

        [System.Serializable]
        public sealed class DoorSkin
        {
            public Biome Biome;
            public Sprite Open;
            public Sprite Locked;
        }

        public DoorSkin DoorSkinFor(Biome biome) => DoorSkins.FirstOrDefault(d => d != null && d.Biome == biome);

        public static GameContentCatalog Load() => Resources.Load<GameContentCatalog>(ResourcePath);

        public ItemDefinitionRegistry BuildRegistry() => ItemDefinitionRegistry.Build(Items.Where(i => i != null));
        public LegendarySpecialRegistry BuildSpecials() => new(Specials.Where(s => s != null));

        public BaseConfigs BuildBaseConfigs() => new()
        {
            Registry = BuildRegistry(),
            AmmoBalance = AmmoBalance,
            Economy = Economy,
            Trader = Trader,
            Workshop = Workshop,
            StatCaps = StatCaps
        };

        public BiomeLightingProfile LightingFor(Biome biome) => Lighting.FirstOrDefault(l => l != null && l.Biome == biome);

        /// <summary>The animation set an actor id (definition id) resolves to; null when no art is bound for it.</summary>
        public CharacterAnimationSet AnimationSetFor(string actorId) =>
            string.IsNullOrEmpty(actorId) ? null : AnimationSets.FirstOrDefault(s => s != null && s.ActorId == actorId);

        /// <summary>The ordered frames of an effect kind ('muzzle', 'telegraph_dash', …); empty when no art is bound.</summary>
        public IReadOnlyList<Sprite> VfxFramesFor(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return System.Array.Empty<Sprite>();
            var prefix = "vfx_" + kind + "_";
            return VfxSprites.Where(s => s != null && s.name.StartsWith(prefix, System.StringComparison.Ordinal) && char.IsDigit(s.name[prefix.Length]))
                .OrderBy(s => s.name.Length).ThenBy(s => s.name, System.StringComparer.Ordinal).ToList();
        }

        /// <summary>The held world sprite of a weapon definition id; null when no art is bound for it.</summary>
        public Sprite WeaponSpriteFor(string weaponId) =>
            string.IsNullOrEmpty(weaponId) ? null : WeaponSprites.FirstOrDefault(s => s != null && s.name == weaponId);

        /// <summary>Everything the boot flow needs is referenced; counts match the approved content contract.</summary>
        public IReadOnlyList<string> Problems()
        {
            var problems = new List<string>();
            void Need(Object o, string name) { if (o == null) problems.Add($"missing {name}"); }
            Need(PlayerBalance, nameof(PlayerBalance)); Need(AmmoBalance, nameof(AmmoBalance)); Need(Economy, nameof(Economy)); Need(Trader, nameof(Trader)); Need(Workshop, nameof(Workshop));
            Need(DepthScaling, nameof(DepthScaling)); Need(Stagger, nameof(Stagger)); Need(Events, nameof(Events)); Need(Merchant, nameof(Merchant)); Need(Loot, nameof(Loot));
            Need(DisplayNamePolicy, nameof(DisplayNamePolicy)); Need(CameraRig, nameof(CameraRig)); Need(Feedback, nameof(Feedback)); Need(AimAssist, nameof(AimAssist)); Need(AudioEvents, nameof(AudioEvents)); Need(Music, nameof(Music));
            if (Lighting.Count(l => l != null) != 3) problems.Add($"lighting profiles {Lighting.Count(l => l != null)}/3");
            if (Items.Count(i => i != null) != 72) problems.Add($"items {Items.Count(i => i != null)}/72");
            if (Specials.Count(s => s != null) != 11) problems.Add($"legendary specials {Specials.Count(s => s != null)}/11");
            if (Enemies.Count(e => e != null) != 9) problems.Add($"enemies {Enemies.Count(e => e != null)}/9");
            if (Elites.Count(e => e != null) != 6) problems.Add($"elites {Elites.Count(e => e != null)}/6");
            if (Bosses.Count(b => b != null) != 6) problems.Add($"bosses {Bosses.Count(b => b != null)}/6");
            if (Rooms.Count(r => r != null) != 63) problems.Add($"rooms {Rooms.Count(r => r != null)}/63");
            if (Rooms.Any(r => r != null && r.Prefab == null)) problems.Add("a room has no prefab");
            if (AnimationSetFor(CharacterVisual.PlayerActorId) == null) problems.Add("missing player animation set");
            var weaponsWithoutSprite = Items.OfType<WeaponDefinition>().Count(w => WeaponSpriteFor(w.Id) == null);
            if (weaponsWithoutSprite > 0) problems.Add($"{weaponsWithoutSprite} weapons have no held sprite");
            foreach (Biome biome in System.Enum.GetValues(typeof(Biome)))
            {
                var skin = DoorSkinFor(biome);
                if (skin == null || skin.Open == null || skin.Locked == null) problems.Add($"missing door skin for {biome}");
            }
            if (ProjectileVisuals == null) problems.Add("missing projectile visual catalog");
            else
            {
                foreach (var problem in ProjectileVisuals.Problems()) problems.Add("projectile visuals: " + problem);
                foreach (var weapon in Items.OfType<WeaponDefinition>().Where(w => w.WeaponClass != WeaponClass.Knife && w.WeaponClass != WeaponClass.Spear))
                {
                    var id = !string.IsNullOrEmpty(weapon.ProjectileVisualId) ? weapon.ProjectileVisualId : ProjectileVisuals.FamilyDefaultFor(weapon.WeaponClass);
                    if (string.IsNullOrEmpty(id) || ProjectileVisuals.Find(id) == null) problems.Add($"weapon {weapon.Id} resolves no projectile visual profile");
                }
            }

            foreach (var key in WorldObjectArt.RequiredKeys)
            {
                if (WorldSpriteFor(key) == null) problems.Add($"missing world sprite {key}");
            }
            foreach (var kind in new[] { "muzzle", "impact", "explosion", "melee", "stagger", "heal", "status", "loot_glow", "telegraph_stationary", "telegraph_dash", "telegraph_projectile", "telegraph_zone", "telegraph_slam" })
            {
                if (VfxFramesFor(kind).Count == 0) problems.Add($"missing vfx frames for {kind}");
            }
            return problems;
        }
    }
}
