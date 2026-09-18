using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;

namespace RuinRail.Audio
{
    /// <summary>Mixer categories (art/105 + ui/90): weapons, enemies, player, loot, UI, ambience and music under one master.</summary>
    public enum AudioBus
    {
        Weapons,
        Enemies,
        Player,
        Loot,
        UI,
        Ambience,
        Music
    }

    /// <summary>
    /// The authored audio-event contract: every gameplay-significant action (art/105 "SFX Coverage Rule") has a stable
    /// id here; definitions/catalogs are matched by id, and the audit lists which ids still lack clips
    /// (BLOCKED_EXTERNAL_ASSET). Music tracks and stingers belong to the music routing task and are not listed here.
    /// </summary>
    public static class AudioEventIds
    {
        // Weapons — one identity per class (art/105 "Weapon Identity").
        public const string FirePistol = "weapon.fire.pistol";
        public const string FireSmg = "weapon.fire.smg";
        public const string FireAssaultRifle = "weapon.fire.assault_rifle";
        public const string FireBattleRifle = "weapon.fire.battle_rifle";
        public const string FireShotgun = "weapon.fire.shotgun";
        public const string FireSniper = "weapon.fire.sniper";
        public const string FireBlaster = "weapon.fire.blaster";
        public const string BowDraw = "weapon.bow.draw";
        public const string BowRelease = "weapon.bow.release";
        public const string KnifeSlash = "weapon.melee.knife_slash";
        public const string SpearThrust = "weapon.melee.spear_thrust";
        public const string RocketLaunch = "weapon.fire.rocket_launch";
        public const string RocketExplosion = "weapon.rocket.explosion";
        public const string Reload = "weapon.reload";
        public const string DryFire = "weapon.dry_fire";
        public const string BlasterHeatRising = "weapon.blaster.heat_rising";
        public const string BlasterOverheatWarning = "weapon.blaster.overheat_warning";
        public const string BlasterOverheat = "weapon.blaster.overheat";
        public const string BlasterVent = "weapon.blaster.vent";
        public const string LegendarySpecial = "weapon.legendary_special";

        // Enemies.
        public const string EnemyTelegraph = "enemy.telegraph";
        public const string EliteTelegraph = "enemy.telegraph.elite";
        public const string BossTelegraph = "enemy.telegraph.boss";
        public const string EnemyHit = "enemy.hit";
        public const string EnemyStagger = "enemy.stagger";
        public const string EnemyDeath = "enemy.death";
        public const string EliteSpawn = "enemy.elite.spawn";
        public const string BossPhase = "enemy.boss.phase";

        // Player.
        public const string PlayerHit = "player.hit";
        public const string PlayerDash = "player.dash";
        public const string PlayerDowned = "player.downed";
        public const string PlayerReviveStart = "player.revive.start";
        public const string PlayerReviveComplete = "player.revive.complete";
        public const string PlayerDeath = "player.death";
        public const string ConsumableUse = "player.consumable.use";
        public const string Heal = "player.heal";
        public const string HazardDamage = "player.hazard";

        // Loot / world.
        public const string PickupItem = "loot.pickup.item";
        public const string PickupCoins = "loot.pickup.coins";
        public const string DropCommon = "loot.drop.common";
        public const string DropUncommon = "loot.drop.uncommon";
        public const string DropRare = "loot.drop.rare";
        public const string DropEpic = "loot.drop.epic";
        /// <summary>art/105: distinctive reusable Legendary drop sound.</summary>
        public const string DropLegendary = "loot.drop.legendary";
        public const string ChestOpen = "loot.chest.open";
        public const string DoorOpen = "world.door.open";
        public const string TransitDepart = "world.transit.depart";
        public const string MerchantOpen = "world.merchant.open";

        // UI (subtle; confirm/cancel/failed clear but not noisy).
        public const string UiNavigate = "ui.navigate";
        public const string UiConfirm = "ui.confirm";
        public const string UiCancel = "ui.cancel";
        public const string UiFailure = "ui.failure";
        public const string UiPurchase = "ui.purchase";

        public static readonly IReadOnlyList<(string id, AudioBus bus, bool loop)> Required = new[]
        {
            (FirePistol, AudioBus.Weapons, false), (FireSmg, AudioBus.Weapons, false), (FireAssaultRifle, AudioBus.Weapons, false), (FireBattleRifle, AudioBus.Weapons, false),
            (FireShotgun, AudioBus.Weapons, false), (FireSniper, AudioBus.Weapons, false), (FireBlaster, AudioBus.Weapons, false), (BowDraw, AudioBus.Weapons, false),
            (BowRelease, AudioBus.Weapons, false), (KnifeSlash, AudioBus.Weapons, false), (SpearThrust, AudioBus.Weapons, false), (RocketLaunch, AudioBus.Weapons, false),
            (RocketExplosion, AudioBus.Weapons, false), (Reload, AudioBus.Weapons, false), (DryFire, AudioBus.Weapons, false), (BlasterHeatRising, AudioBus.Weapons, true),
            (BlasterOverheatWarning, AudioBus.Weapons, false), (BlasterOverheat, AudioBus.Weapons, false), (BlasterVent, AudioBus.Weapons, false), (LegendarySpecial, AudioBus.Weapons, false),
            (EnemyTelegraph, AudioBus.Enemies, false), (EliteTelegraph, AudioBus.Enemies, false), (BossTelegraph, AudioBus.Enemies, false), (EnemyHit, AudioBus.Enemies, false),
            (EnemyStagger, AudioBus.Enemies, false), (EnemyDeath, AudioBus.Enemies, false), (EliteSpawn, AudioBus.Enemies, false), (BossPhase, AudioBus.Enemies, false),
            (PlayerHit, AudioBus.Player, false), (PlayerDash, AudioBus.Player, false), (PlayerDowned, AudioBus.Player, false), (PlayerReviveStart, AudioBus.Player, true),
            (PlayerReviveComplete, AudioBus.Player, false), (PlayerDeath, AudioBus.Player, false), (ConsumableUse, AudioBus.Player, false), (Heal, AudioBus.Player, false), (HazardDamage, AudioBus.Player, false),
            (PickupItem, AudioBus.Loot, false), (PickupCoins, AudioBus.Loot, false), (DropCommon, AudioBus.Loot, false), (DropUncommon, AudioBus.Loot, false), (DropRare, AudioBus.Loot, false),
            (DropEpic, AudioBus.Loot, false), (DropLegendary, AudioBus.Loot, false), (ChestOpen, AudioBus.Loot, false), (DoorOpen, AudioBus.Loot, false), (TransitDepart, AudioBus.Loot, false), (MerchantOpen, AudioBus.Loot, false),
            (UiNavigate, AudioBus.UI, false), (UiConfirm, AudioBus.UI, false), (UiCancel, AudioBus.UI, false), (UiFailure, AudioBus.UI, false), (UiPurchase, AudioBus.UI, false)
        };

        public static IEnumerable<string> RequiredIds => Required.Select(r => r.id);

        public static string FireEventFor(WeaponClass weaponClass) => weaponClass switch
        {
            WeaponClass.Pistol => FirePistol,
            WeaponClass.Smg => FireSmg,
            WeaponClass.AssaultRifle => FireAssaultRifle,
            WeaponClass.BattleRifle => FireBattleRifle,
            WeaponClass.Shotgun => FireShotgun,
            WeaponClass.Sniper => FireSniper,
            WeaponClass.Blaster => FireBlaster,
            WeaponClass.Bow => BowRelease,
            WeaponClass.RocketLauncher => RocketLaunch,
            WeaponClass.Knife => KnifeSlash,
            WeaponClass.Spear => SpearThrust,
            _ => FirePistol
        };

        public static string DropEventFor(Rarity rarity) => rarity switch
        {
            Rarity.Legendary => DropLegendary,
            Rarity.Epic => DropEpic,
            Rarity.Rare => DropRare,
            Rarity.Uncommon => DropUncommon,
            _ => DropCommon
        };
    }
}
