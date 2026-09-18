using System;
using UnityEngine;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// Narrow event hub for player-side reactive effects (armor and accessory passives). Producers (room lifecycle,
    /// movement, dash, weapons, kills, health, consumables, blaster heat, projectiles, stagger/knockback) raise these
    /// explicitly; consumers never poll scene state. Hooks that can alter an outcome pass request objects.
    /// </summary>
    public sealed class PlayerCombatEvents
    {
        // ---- rooms / movement / dash ----
        public event Action CombatRoomEntered;
        public event Action CombatRoomCleared;
        public event Action<bool> MovementStateChanged;
        public event Action Dashed;
        public event Action DashEnded;

        // ---- kills / health ----
        public event Action EnemyKilled;
        public event Action MeleeKill;
        public event Action<int> DamageTaken;
        public event Action<int, int> HealthChanged;

        // ---- weapons ----
        public event Action WeaponSwapped;
        public event Action ReloadCompleted;
        public event Action<bool> FiringStateChanged;
        public event Action<AttackDamageRequest> AttackDamageRolling;
        public event Action<ProjectileHitRequest> ProjectileHitRolling;
        public event Action<BowShotRequest> BowShotFired;
        public event Action<HeatRequest> BlasterShotHeatRolling;
        public event Action<float> BlasterCooledToZero;
        public event Action BlasterOverheated;

        // ---- consumables / pickups ----
        public event Action<HealingRequest> HealingConsumableUsed;
        public event Action<AmmoPickupRequest> AmmoPickupRolling;

        // ---- stagger / knockback ----
        public event Action<NegatableRequest> StaggerIncoming;
        public event Action<NegatableRequest> ExplosionKnockbackIncoming;
        public event Action<WallImpactRequest> EnemyKnockedIntoWall;
        public event Action<string> EnemyStaggeredByWearer;

        public void RaiseCombatRoomEntered() => CombatRoomEntered?.Invoke();
        public void RaiseCombatRoomCleared() => CombatRoomCleared?.Invoke();
        public void RaiseMovementStateChanged(bool isMoving) => MovementStateChanged?.Invoke(isMoving);
        public void RaiseDashed() => Dashed?.Invoke();
        public void RaiseDashEnded() => DashEnded?.Invoke();
        public void RaiseEnemyKilled() => EnemyKilled?.Invoke();
        public void RaiseMeleeKill() { MeleeKill?.Invoke(); EnemyKilled?.Invoke(); }
        public void RaiseDamageTaken(int finalDamage) => DamageTaken?.Invoke(finalDamage);
        public void RaiseHealthChanged(int current, int max) => HealthChanged?.Invoke(current, max);
        public void RaiseWeaponSwapped() => WeaponSwapped?.Invoke();
        public void RaiseReloadCompleted() => ReloadCompleted?.Invoke();
        public void RaiseFiringStateChanged(bool isFiring) => FiringStateChanged?.Invoke(isFiring);
        public void RaiseBlasterCooledToZero(float peakHeat) => BlasterCooledToZero?.Invoke(peakHeat);
        public void RaiseBlasterOverheated() => BlasterOverheated?.Invoke();
        public void RaiseEnemyStaggeredByWearer(string targetId) => EnemyStaggeredByWearer?.Invoke(targetId);

        public AttackDamageRequest RaiseAttackDamageRolling(int baseDamage, bool isProjectileWeapon)
        {
            var request = new AttackDamageRequest(baseDamage, isProjectileWeapon);
            AttackDamageRolling?.Invoke(request);
            return request;
        }

        public ProjectileHitRequest RaiseProjectileHitRolling(int baseDamage, float travelDistance)
        {
            var request = new ProjectileHitRequest(baseDamage, travelDistance);
            ProjectileHitRolling?.Invoke(request);
            return request;
        }

        public BowShotRequest RaiseBowShotFired(bool isFullDraw)
        {
            var request = new BowShotRequest(isFullDraw);
            BowShotFired?.Invoke(request);
            return request;
        }

        public HeatRequest RaiseBlasterShotHeatRolling(float baseHeat)
        {
            var request = new HeatRequest(baseHeat);
            BlasterShotHeatRolling?.Invoke(request);
            return request;
        }

        public HealingRequest RaiseHealingConsumableUsed(int baseAmount)
        {
            var request = new HealingRequest(baseAmount);
            HealingConsumableUsed?.Invoke(request);
            return request;
        }

        public AmmoPickupRequest RaiseAmmoPickupRolling(int baseAmount)
        {
            var request = new AmmoPickupRequest(baseAmount);
            AmmoPickupRolling?.Invoke(request);
            return request;
        }

        public NegatableRequest RaiseStaggerIncoming()
        {
            var request = new NegatableRequest();
            StaggerIncoming?.Invoke(request);
            return request;
        }

        public NegatableRequest RaiseExplosionKnockbackIncoming()
        {
            var request = new NegatableRequest();
            ExplosionKnockbackIncoming?.Invoke(request);
            return request;
        }

        public WallImpactRequest RaiseEnemyKnockedIntoWall(string targetId, bool isBoss)
        {
            var request = new WallImpactRequest(targetId, isBoss);
            EnemyKnockedIntoWall?.Invoke(request);
            return request;
        }
    }

    /// <summary>Percent bonus request shared by the damage-style hooks; final value rounded to an integer.</summary>
    public abstract class PercentBonusRequest
    {
        protected PercentBonusRequest(int baseAmount)
        {
            BaseAmount = baseAmount;
        }

        public int BaseAmount { get; }
        public int BonusPercent { get; set; }
        public int FinalAmount => Math.Max(0, Mathf.RoundToInt(BaseAmount * (100 + BonusPercent) / 100f));
    }

    public sealed class HealingRequest : PercentBonusRequest
    {
        public HealingRequest(int baseAmount) : base(baseAmount) { }
    }

    public sealed class AttackDamageRequest : PercentBonusRequest
    {
        public AttackDamageRequest(int baseDamage, bool isProjectileWeapon) : base(baseDamage) { IsProjectileWeapon = isProjectileWeapon; }
        public bool IsProjectileWeapon { get; }
    }

    public sealed class ProjectileHitRequest : PercentBonusRequest
    {
        public ProjectileHitRequest(int baseDamage, float travelDistance) : base(baseDamage) { TravelDistance = travelDistance; }
        public float TravelDistance { get; }
    }

    public sealed class AmmoPickupRequest : PercentBonusRequest
    {
        public AmmoPickupRequest(int baseAmount) : base(baseAmount) { }
    }

    public sealed class BowShotRequest
    {
        public BowShotRequest(bool isFullDraw) { IsFullDraw = isFullDraw; }
        public bool IsFullDraw { get; }

        /// <summary>Number of enemies the arrow passes through without losing damage.</summary>
        public int Penetrations { get; set; }
    }

    public sealed class HeatRequest
    {
        public HeatRequest(float baseHeat) { BaseHeat = baseHeat; Multiplier = 1f; }
        public float BaseHeat { get; }
        public float Multiplier { get; set; }
        public float FinalHeat => Math.Max(0f, BaseHeat * Multiplier);
    }

    public sealed class WallImpactRequest
    {
        public WallImpactRequest(string targetId, bool isBoss) { TargetId = targetId; IsBoss = isBoss; }
        public string TargetId { get; }
        public bool IsBoss { get; }
        public int BonusDamageMin { get; set; }
        public int BonusDamageMax { get; set; }
        public bool ApplyHighStagger { get; set; }
        public bool HasBonus => BonusDamageMax > 0;
    }

    public sealed class NegatableRequest
    {
        public bool IsNegated { get; private set; }
        public string NegatedBy { get; private set; }

        public void Negate(string source)
        {
            if (IsNegated) return;
            IsNegated = true;
            NegatedBy = source;
        }
    }
}
