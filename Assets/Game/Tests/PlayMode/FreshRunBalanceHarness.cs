using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The measurement model for the fresh-run balance pass. It is a TEST-ONLY harness: it reads shipped data and
    /// computes through shipped math (<see cref="WeaponStatMath"/>, <see cref="PlayerStats"/>,
    /// <see cref="DepthScaling"/>), and it never writes a shipping value.
    ///
    /// Two kinds of number come out of here and the CSVs label which is which:
    ///
    ///   MEASURED — derived only from shipped data and shipped formulas (damage per hit, cadence, effective magazine,
    ///   reload time, ammo per shot, scaled enemy health, prices, loot rolls). These are exact; PlayMode cross-checks
    ///   in FreshRunBalanceRuntimeTests prove the same values come out of the real components at runtime.
    ///
    ///   MODELLED — quantities that depend on how a human plays: hit rate, pellet connection, how much of a fight the
    ///   player spends inside an enemy's attack range, and how much telegraph-dodging downtime a boss imposes. Each of
    ///   those is a single named constant on a <see cref="SkillProfile"/> or <see cref="ExposureModel"/> so the report
    ///   can state the assumption instead of hiding it inside a formula.
    /// </summary>
    public static class FreshRunBalance
    {
        /// <summary>How a given player actually shoots. No value here is read from shipping data — they are the pass's stated assumptions.</summary>
        public sealed class SkillProfile
        {
            public SkillProfile(string name, float accuracy, float pelletConnect, float reloadWasteFactor, float dodgeEfficiency)
            {
                Name = name;
                Accuracy = accuracy;
                PelletConnect = pelletConnect;
                ReloadWasteFactor = reloadWasteFactor;
                DodgeEfficiency = dodgeEfficiency;
            }

            public string Name { get; }

            /// <summary>Share of fired shots that connect at all.</summary>
            public float Accuracy { get; }

            /// <summary>Share of a shotgun's pellets that connect on a shot that hit — a cone never lands every pellet at real range.</summary>
            public float PelletConnect { get; }

            /// <summary>Extra reloads per magazine from reloading early. RUINRAIL draws only the shortfall, so this costs time, never rounds.</summary>
            public float ReloadWasteFactor { get; }

            /// <summary>Share of avoidable incoming damage the player actually avoids (1 = perfect dodging).</summary>
            public float DodgeEfficiency { get; }

            public static readonly SkillProfile High = new("HIGH_ACCURACY", 0.92f, 0.70f, 0.00f, 0.90f);
            public static readonly SkillProfile Normal = new("NORMAL_ACCURACY", 0.75f, 0.55f, 0.15f, 0.70f);
            public static readonly SkillProfile NewPlayer = new("NEW_PLAYER", 0.55f, 0.40f, 0.60f, 0.45f);
            public static readonly SkillProfile[] All = { High, Normal, NewPlayer };
        }

        /// <summary>How much of a fight the player is inside the enemy's reach. Stated, not measured — see the class summary.</summary>
        public static class ExposureModel
        {
            /// <summary>A ranged fight: the player kites, so only part of the fight is spent inside a melee enemy's reach.</summary>
            public const float RangedVsMelee = 0.35f;

            /// <summary>A ranged fight against a shooter/sniper: their reach covers the whole arena, so exposure is near total.</summary>
            public const float RangedVsRanged = 0.85f;

            /// <summary>A melee fight: the player is in contact range by definition.</summary>
            public const float MeleeContact = 1.00f;

            /// <summary>Share of a boss fight spent dodging telegraphs rather than dealing damage.</summary>
            public const float BossDowntime = 0.35f;

            /// <summary>Share of an elite fight spent repositioning rather than dealing damage.</summary>
            public const float EliteDowntime = 0.25f;
        }

        // ================= weapons =================

        /// <summary>The effective performance of one weapon for one wielder, computed through the shipped stat math.</summary>
        public sealed class WeaponProfile
        {
            public WeaponProfile(WeaponDefinition definition, IPlayerStatsProvider stats, SkillProfile skill)
            {
                Definition = definition ?? throw new ArgumentNullException(nameof(definition));
                Skill = skill;
                var damageMultiplier = stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f;

                switch (definition)
                {
                    case RangedWeaponDefinition ranged:
                        Kind = ranged.IsExplosive ? WeaponKind.Explosive : WeaponKind.Firearm;
                        Pellets = ranged.ProjectilesPerShot;
                        PerPelletDamage = (ranged.DamageMin + ranged.DamageMax) * 0.5f * damageMultiplier;
                        ShotsPerSecond = WeaponStatMath.FireRate(ranged.FireRate, stats);
                        Magazine = WeaponStatMath.MagazineSize(ranged.MagazineSize, stats);
                        ReloadSeconds = ranged.ReloadTime / (stats?.GetMultiplier(StatId.ReloadSpeed) ?? 1f);
                        Range = WeaponStatMath.ProjectileRange(ranged.Range, stats);
                        ProjectileSpeed = WeaponStatMath.ProjectileSpeed(ranged.ProjectileSpeed, stats);
                        SpreadDegrees = WeaponStatMath.SpreadDegrees(ranged.SpreadDegrees, stats);
                        AmmoPerShot = ranged.AmmoCostPerShot;
                        AmmoType = ranged.AmmoType;
                        ExplosionRadius = ranged.ExplosionRadiusTiles;
                        break;
                    case BlasterWeaponDefinition blaster:
                        Kind = WeaponKind.Blaster;
                        Pellets = 1;
                        PerPelletDamage = (blaster.DamageMin + blaster.DamageMax) * 0.5f * damageMultiplier;
                        ShotsPerSecond = WeaponStatMath.FireRate(blaster.FireRate, stats);
                        Range = WeaponStatMath.ProjectileRange(blaster.Range, stats);
                        ProjectileSpeed = WeaponStatMath.ProjectileSpeed(blaster.ProjectileSpeed, stats);
                        HeatPerShot = WeaponStatMath.BlasterHeatPerShot(blaster.HeatPerShot, stats);
                        CoolingRate = WeaponStatMath.BlasterCoolingRate(blaster.CoolingRatePerSecond, stats);
                        MaxHeat = blaster.MaxHeat;
                        CoolingDelay = blaster.CoolingDelaySeconds;
                        OverheatLockout = blaster.OverheatLockoutSeconds;
                        break;
                    case BowWeaponDefinition bow:
                        Kind = WeaponKind.Bow;
                        Pellets = 1;
                        PerPelletDamage = (bow.FullDrawDamageMin + bow.FullDrawDamageMax) * 0.5f * damageMultiplier;
                        QuickDamage = (bow.QuickDamageMin + bow.QuickDamageMax) * 0.5f * damageMultiplier;
                        FullChargeSeconds = WeaponStatMath.BowFullChargeSeconds(bow.FullChargeSeconds, stats);
                        ShotsPerSecond = 1f / Mathf.Max(0.0001f, FullChargeSeconds);
                        Range = WeaponStatMath.ProjectileRange(bow.FullRange, stats);
                        ProjectileSpeed = WeaponStatMath.ProjectileSpeed(bow.FullProjectileSpeed, stats);
                        break;
                    case MeleeWeaponDefinition melee:
                        Kind = WeaponKind.Melee;
                        Pellets = 1;
                        PerPelletDamage = (melee.DamageMin + melee.DamageMax) * 0.5f * damageMultiplier;
                        ShotsPerSecond = WeaponStatMath.MeleeAttackRate(melee.AttackRate, stats);
                        Range = melee.AttackRange;
                        ArcDegrees = melee.AttackArcDegrees;
                        WindUpSeconds = WeaponStatMath.MeleePhaseSeconds(melee.WindUpSeconds, stats);
                        RecoverySeconds = WeaponStatMath.MeleePhaseSeconds(melee.RecoverySeconds, stats);
                        break;
                }

                Knockback = WeaponStatMath.Knockback(definition.Knockback, stats);
                StaggerPower = WeaponStatMath.StaggerPower(definition.StaggerPower, stats);
            }

            public WeaponDefinition Definition { get; }
            public SkillProfile Skill { get; }
            public WeaponKind Kind { get; }
            public int Pellets { get; }
            public float PerPelletDamage { get; }
            public float QuickDamage { get; }
            public float ShotsPerSecond { get; }
            public int Magazine { get; }
            public float ReloadSeconds { get; }
            public float Range { get; }
            public float ProjectileSpeed { get; }
            public float SpreadDegrees { get; }
            public int AmmoPerShot { get; }
            public AmmoType AmmoType { get; }
            public float ExplosionRadius { get; }
            public float HeatPerShot { get; }
            public float CoolingRate { get; }
            public float MaxHeat { get; }
            public float CoolingDelay { get; }
            public float OverheatLockout { get; }
            public float FullChargeSeconds { get; }
            public float ArcDegrees { get; }
            public float WindUpSeconds { get; }
            public float RecoverySeconds { get; }
            public float Knockback { get; }
            public float StaggerPower { get; }

            public bool UsesAmmo => Kind == WeaponKind.Firearm || Kind == WeaponKind.Explosive;
            public bool IsAoe => ExplosionRadius > 0f;

            /// <summary>Expected damage one trigger pull lands, after hit rate and (for a cone) pellet connection.</summary>
            public float ExpectedDamagePerShot =>
                Pellets > 1
                    ? PerPelletDamage * Pellets * Skill.Accuracy * Skill.PelletConnect
                    : PerPelletDamage * Skill.Accuracy;

            /// <summary>Damage a perfectly connecting trigger pull lands — the theoretical ceiling, used only for burst DPS.</summary>
            public float MaxDamagePerShot => PerPelletDamage * Pellets;

            /// <summary>Burst DPS: uninterrupted trigger time, perfect connection, no reload or heat accounted for.</summary>
            public float BurstDps => MaxDamagePerShot * ShotsPerSecond;

            /// <summary>
            /// Practical sustained DPS at this skill profile: expected damage per shot over the real cycle time, where
            /// the cycle includes the reload (firearms), the heat cycle (blasters), the draw (bows) or nothing extra
            /// (melee, whose cadence already contains wind-up and recovery).
            /// </summary>
            public float SustainedDps
            {
                get
                {
                    var interval = 1f / Mathf.Max(0.0001f, ShotsPerSecond);
                    switch (Kind)
                    {
                        case WeaponKind.Firearm:
                        case WeaponKind.Explosive:
                        {
                            if (Magazine <= 0) return ExpectedDamagePerShot * ShotsPerSecond;
                            var cycle = Magazine * interval + ReloadSeconds * (1f + Skill.ReloadWasteFactor);
                            return Magazine * ExpectedDamagePerShot / cycle;
                        }
                        case WeaponKind.Blaster:
                        {
                            var shots = ShotsToOverheat;
                            var firingTime = shots * interval;
                            // Heat shed while firing offsets part of the build-up only after the cooling delay, which a
                            // continuous burst never clears; the honest cycle is fire-to-lockout plus the lockout.
                            var cycle = firingTime + OverheatLockout;
                            return shots * ExpectedDamagePerShot / Mathf.Max(0.0001f, cycle);
                        }
                        case WeaponKind.Bow:
                            return ExpectedDamagePerShot / Mathf.Max(0.0001f, FullChargeSeconds);
                        default:
                            return ExpectedDamagePerShot * ShotsPerSecond;
                    }
                }
            }

            /// <summary>Shots a blaster fires from cold before the lockout.</summary>
            public int ShotsToOverheat => HeatPerShot <= 0f ? 0 : Mathf.Max(1, Mathf.CeilToInt(MaxHeat / HeatPerShot));

            /// <summary>Seconds from a full overheat back to zero heat.</summary>
            public float FullCoolSeconds => CoolingRate <= 0f ? 0f : CoolingDelay + MaxHeat / CoolingRate;

            /// <summary>Share of a sustained firing cycle spent not dealing damage.</summary>
            public float DowntimeRatio
            {
                get
                {
                    var interval = 1f / Mathf.Max(0.0001f, ShotsPerSecond);
                    switch (Kind)
                    {
                        case WeaponKind.Firearm:
                        case WeaponKind.Explosive:
                        {
                            if (Magazine <= 0) return 0f;
                            var down = ReloadSeconds * (1f + Skill.ReloadWasteFactor);
                            return down / (Magazine * interval + down);
                        }
                        case WeaponKind.Blaster:
                        {
                            var firing = ShotsToOverheat * interval;
                            return OverheatLockout / Mathf.Max(0.0001f, firing + OverheatLockout);
                        }
                        default:
                            return 0f;
                    }
                }
            }

            /// <summary>Expected damage delivered per unit of reserve ammo spent (0 for weapons that use none).</summary>
            public float DamagePerAmmoUnit => UsesAmmo && AmmoPerShot > 0 ? ExpectedDamagePerShot / AmmoPerShot : 0f;

            public float AmmoPerSecond => UsesAmmo ? AmmoPerShot * ShotsPerSecond * (1f - DowntimeRatio) : 0f;

            /// <summary>Trigger pulls needed to remove <paramref name="health"/> at this skill profile.</summary>
            public int ShotsToKill(float health) =>
                Mathf.Max(1, Mathf.CeilToInt(health / Mathf.Max(0.01f, ExpectedDamagePerShot)));

            /// <summary>Reserve rounds consumed removing <paramref name="health"/> (0 for ammo-free weapons).</summary>
            public int AmmoToKill(float health) => UsesAmmo ? ShotsToKill(health) * AmmoPerShot : 0;

            /// <summary>Seconds to remove <paramref name="health"/>, including the downtime this weapon's cycle imposes.</summary>
            public float TimeToKill(float health)
            {
                var shots = ShotsToKill(health);
                var interval = 1f / Mathf.Max(0.0001f, ShotsPerSecond);
                var firing = shots * interval;
                switch (Kind)
                {
                    case WeaponKind.Firearm:
                    case WeaponKind.Explosive:
                        if (Magazine <= 0) return firing;
                        return firing + Mathf.Floor((shots - 1) / (float)Magazine) * ReloadSeconds * (1f + Skill.ReloadWasteFactor);
                    case WeaponKind.Blaster:
                    {
                        var perCycle = Mathf.Max(1, ShotsToOverheat);
                        return firing + Mathf.Floor((shots - 1) / (float)perCycle) * OverheatLockout;
                    }
                    default:
                        return firing;
                }
            }

            /// <summary>Travel time to a distance, or 0 for a melee swing. Capped at the weapon's own reach.</summary>
            public float TravelTimeTo(float tiles) =>
                ProjectileSpeed <= 0f ? 0f : Mathf.Min(tiles, Range) / ProjectileSpeed;

            /// <summary>Effective targets one shot damages, given how many enemies are grouped.</summary>
            public float TargetsPerShot(int grouped) => IsAoe ? Mathf.Min(grouped, 1f + ExplosionRadius) : 1f;
        }

        public enum WeaponKind { Firearm, Explosive, Blaster, Bow, Melee }

        // ================= enemies =================

        /// <summary>One enemy archetype at one depth, through the shipped depth curves.</summary>
        public sealed class EnemyProfile
        {
            public EnemyProfile(string id, string display, float health, int damageMin, int damageMax, float attackPeriod,
                float moveSpeed, float attackRange, int staggerResist, int knockbackResist, bool isRanged, int frontalShieldPercent = 0)
            {
                Id = id;
                Display = display;
                Health = health;
                DamageMin = damageMin;
                DamageMax = damageMax;
                AttackPeriod = Mathf.Max(0.1f, attackPeriod);
                MoveSpeed = moveSpeed;
                AttackRange = attackRange;
                StaggerResistPercent = staggerResist;
                KnockbackResistPercent = knockbackResist;
                IsRanged = isRanged;
                FrontalShieldPercent = frontalShieldPercent;
            }

            public string Id { get; }
            public string Display { get; }
            public float Health { get; }
            public int DamageMin { get; }
            public int DamageMax { get; }
            public float AttackPeriod { get; }
            public float MoveSpeed { get; }
            public float AttackRange { get; }
            public int StaggerResistPercent { get; }
            public int KnockbackResistPercent { get; }
            public bool IsRanged { get; }
            public int FrontalShieldPercent { get; }

            public float AverageDamage => (DamageMin + DamageMax) * 0.5f;
            public float DamagePerSecond => AverageDamage / AttackPeriod;

            /// <summary>Health a frontal attacker must chew through when the shield is not flanked.</summary>
            public float FrontalEffectiveHealth => FrontalShieldPercent <= 0 ? Health : Health / Mathf.Max(0.01f, 1f - FrontalShieldPercent / 100f);

            public static EnemyProfile From(EnemyDefinition definition, int depth, DepthScalingConfig config)
            {
                var (min, max) = DepthScaling.ScaledDamage(definition.DamageMin, definition.DamageMax, depth, config);
                var period = definition.AttackTelegraphSeconds + definition.AttackCooldownSeconds;
                period /= Mathf.Max(0.01f, DepthScaling.AttackSpeedMultiplier(depth, config));
                return new EnemyProfile(definition.Id, definition.DisplayName,
                    DepthScaling.ScaledHealth(definition.BaseHealth, depth, 1, false, config), min, max, period,
                    definition.MoveSpeed * DepthScaling.MovementSpeedMultiplier(depth, config), definition.AttackRange,
                    definition.StaggerResistancePercent, definition.KnockbackResistancePercent,
                    definition.AttackKind != EnemyAttackKind.MeleeContact, definition.FrontalShieldPercent);
            }

            public static EnemyProfile From(RuinRail.Gameplay.Enemies.Elites.EliteDefinition elite, int depth, DepthScalingConfig config)
            {
                var (min, max) = DepthScaling.ScaledDamage(elite.StrongestAttackDamageMin, elite.StrongestAttackDamageMax, depth, config);
                var period = MovesetPeriod(elite.Moveset);
                return new EnemyProfile(elite.Id, elite.DisplayName,
                    DepthScaling.ScaledHealth(elite.BaseHealth, depth, 1, false, config), min, max, period,
                    elite.MoveSpeed * DepthScaling.MovementSpeedMultiplier(depth, config), 2f,
                    elite.StaggerResistancePercent, elite.KnockbackResistancePercent, true);
            }

            public static EnemyProfile From(RuinRail.Gameplay.Enemies.Bosses.BossDefinition boss, int depth, DepthScalingConfig config)
            {
                var (min, max) = DepthScaling.ScaledDamage(boss.StrongestAttackDamageMin, boss.StrongestAttackDamageMax, depth, config);
                var period = MovesetPeriod(boss.Moveset);
                return new EnemyProfile(boss.Id, boss.DisplayName,
                    DepthScaling.ScaledHealth(boss.BaseHealth, depth, 1, true, config), min, max, period,
                    boss.MoveSpeed * DepthScaling.MovementSpeedMultiplier(depth, config), 2f,
                    boss.StaggerResistancePercent, 100, true);
            }

            /// <summary>Average seconds between attacks across a moveset (telegraph + recovery + cooldown, averaged).</summary>
            private static float MovesetPeriod(IReadOnlyList<RuinRail.Gameplay.Enemies.Attacks.EnemyAttackDefinition> moveset)
            {
                var attacks = (moveset ?? Array.Empty<RuinRail.Gameplay.Enemies.Attacks.EnemyAttackDefinition>()).Where(a => a != null).ToList();
                if (attacks.Count == 0) return 2f;
                return attacks.Average(a => a.TelegraphSeconds + a.RecoverySeconds + a.CooldownSeconds);
            }
        }

        /// <summary>Damage the player takes killing one enemy with one weapon, at one skill profile.</summary>
        public static float IncomingDamage(WeaponProfile weapon, EnemyProfile enemy, SkillProfile skill, float extraDowntime = 0f)
        {
            var seconds = weapon.TimeToKill(enemy.Health) * (1f + extraDowntime);
            var exposure = weapon.Kind == WeaponKind.Melee
                ? ExposureModel.MeleeContact
                : enemy.IsRanged ? ExposureModel.RangedVsRanged : ExposureModel.RangedVsMelee;
            return enemy.DamagePerSecond * seconds * exposure * (1f - skill.DodgeEfficiency);
        }

        /// <summary>CSV escaping used by every artefact this pass writes.</summary>
        public static string Csv(string value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        public static string F(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        public static string F1(float value) => value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
    }
}
