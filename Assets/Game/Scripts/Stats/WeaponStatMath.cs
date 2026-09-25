using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// The one place every weapon-facing stat modifier is applied.
    ///
    /// Each weapon component exposes a <c>Current…</c> property (the existing <see cref="RuinRail.Gameplay.Combat.Weapons.RangedWeapon.CurrentReloadTime"/>
    /// convention) that calls straight into this helper, so a stat's arithmetic exists exactly once and can never be
    /// applied twice on one code path. The values handed in are already capped: <see cref="PlayerStats.Recompute"/>
    /// clamps each percent stat once at the player/16 global cap, so nothing here re-does cap math.
    ///
    /// A null provider always yields the authored value, which is what an unbound weapon (editor scene, test fixture,
    /// remote replica before Bind) must keep doing.
    ///
    /// Each read names its StatId literally rather than going through a shared helper, so
    /// <c>StatConsumerIntegrityValidator</c>'s reader scan sees the consumer it is looking for. A helper would hide
    /// every weapon stat behind one indirection and make "this stat has a consumer" unprovable by inspection.
    /// </summary>
    public static class WeaponStatMath
    {
        /// <summary>Shots per second: authored rate x (1 + Fire Rate %). Higher is faster.</summary>
        public static float FireRate(float authored, IPlayerStatsProvider stats) =>
            Mathf.Max(0.0001f, authored * (stats?.GetMultiplier(StatId.FireRate) ?? 1f));

        /// <summary>Seconds between shots; the reciprocal of <see cref="FireRate"/> and the only place that division lives.</summary>
        public static float FireInterval(float authoredFireRate, IPlayerStatsProvider stats) =>
            1f / FireRate(authoredFireRate, stats);

        /// <summary>
        /// Effective magazine capacity: authored size x (1 + Magazine Size %), rounded half-away-from-zero to a whole
        /// round and never below 1. Rounding is deterministic so the HUD, the reload and the network clamp agree.
        /// This changes only how many rounds the magazine holds — reserve ammo is neither created nor consumed here.
        /// </summary>
        public static int MagazineSize(int authored, IPlayerStatsProvider stats) =>
            authored <= 0 ? authored : Mathf.Max(1, Mathf.RoundToInt(authored * (stats?.GetMultiplier(StatId.MagazineSize) ?? 1f)));

        /// <summary>Projectile travel speed in tiles/second: authored x (1 + Projectile Speed %).</summary>
        public static float ProjectileSpeed(float authored, IPlayerStatsProvider stats) =>
            authored * (stats?.GetMultiplier(StatId.ProjectileSpeed) ?? 1f);

        /// <summary>Projectile maximum reach in tiles: authored x (1 + Projectile Range %). Drives the real travel limit, not a label.</summary>
        public static float ProjectileRange(float authored, IPlayerStatsProvider stats) =>
            authored * (stats?.GetMultiplier(StatId.ProjectileRange) ?? 1f);

        /// <summary>Melee swings per second: authored x (1 + Melee Attack Speed %).</summary>
        public static float MeleeAttackRate(float authored, IPlayerStatsProvider stats) =>
            Mathf.Max(0.0001f, authored * (stats?.GetMultiplier(StatId.MeleeAttackSpeed) ?? 1f));

        /// <summary>A melee wind-up/recovery window: authored / (1 + Melee Attack Speed %). Faster attacks shorten the whole cadence, not only the cooldown.</summary>
        public static float MeleePhaseSeconds(float authored, IPlayerStatsProvider stats) =>
            authored / (stats?.GetMultiplier(StatId.MeleeAttackSpeed) ?? 1f);

        /// <summary>Blaster heat shed per second: authored x (1 + Blaster Cooling Rate %).</summary>
        public static float BlasterCoolingRate(float authored, IPlayerStatsProvider stats) =>
            authored * (stats?.GetMultiplier(StatId.BlasterCoolingRate) ?? 1f);

        /// <summary>Heat added by one blaster shot: authored x (1 - Blaster Heat per Shot reduction %), never below 0.</summary>
        public static float BlasterHeatPerShot(float authored, IPlayerStatsProvider stats) =>
            Mathf.Max(0f, authored * (stats?.GetReductionFactor(StatId.BlasterHeatPerShotReduction) ?? 1f));

        /// <summary>Seconds to a full bow draw: authored / (1 + Bow Charge Speed %). Lower is faster.</summary>
        public static float BowFullChargeSeconds(float authored, IPlayerStatsProvider stats) =>
            Mathf.Max(0.0001f, authored / (stats?.GetMultiplier(StatId.BowChargeSpeed) ?? 1f));

        /// <summary>
        /// Effective pellet cone: authored spread x (1 - Weapon Spread Reduction %), never below 0. Only weapons that
        /// author a spread have one to tighten; a 0-spread weapon stays a single perfectly accurate shot.
        /// </summary>
        public static float SpreadDegrees(float authored, IPlayerStatsProvider stats) =>
            authored <= 0f ? 0f : Mathf.Max(0f, authored * (stats?.GetReductionFactor(StatId.WeaponSpreadReduction) ?? 1f));

        /// <summary>Knockback points one hit carries: authored x (1 + Knockback %). 0 stays 0 — no stat can create impact a weapon does not have.</summary>
        public static float Knockback(float authored, IPlayerStatsProvider stats) =>
            authored <= 0f ? 0f : authored * (stats?.GetMultiplier(StatId.Knockback) ?? 1f);

        /// <summary>Stagger pressure one hit carries: authored x (1 + Stagger Power %). 0 stays 0.</summary>
        public static float StaggerPower(float authored, IPlayerStatsProvider stats) =>
            authored <= 0f ? 0f : authored * (stats?.GetMultiplier(StatId.StaggerPower) ?? 1f);
    }
}
