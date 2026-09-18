using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Accessories
{
    /// <summary>Runner's Watch — after Dash, +10% Movement Speed for 3 s.</summary>
    public sealed class WatchMomentumPassive : AccessoryPassive
    {
        public const int MovePercent = 10;
        public const float DurationSeconds = 3f;
        private TimedBuff _buff;
        public override string Id => "accessory_momentum";
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:accessory_momentum", StatModifier.Percent(StatId.MovementSpeed, MovePercent));
            Context.Events.Dashed += OnDashed;
        }
        protected override void OnDetach() { Context.Events.Dashed -= OnDashed; _buff.Clear(); }
        private void OnDashed() => _buff.Trigger(DurationSeconds);
        public override void Tick(float deltaTime) => _buff?.Tick(deltaTime);
    }

    /// <summary>Field Scope — after 1 s without character movement, projectile weapons deal +12% damage; ends the moment movement resumes.</summary>
    public sealed class SteadyAimPassive : AccessoryPassive
    {
        public const float StillSeconds = 1f;
        public const int DamagePercent = 12;
        private bool _moving;
        private float _stillFor;
        public override string Id => "steady_aim";
        public bool IsActive => !_moving && _stillFor >= StillSeconds;
        protected override void OnAttach()
        {
            Context.Events.MovementStateChanged += OnMovement;
            Context.Events.AttackDamageRolling += OnAttack;
        }
        protected override void OnDetach()
        {
            Context.Events.MovementStateChanged -= OnMovement;
            Context.Events.AttackDamageRolling -= OnAttack;
        }
        private void OnMovement(bool moving) { _moving = moving; if (moving) _stillFor = 0f; }
        private void OnAttack(AttackDamageRequest request) { if (IsActive && request.IsProjectileWeapon) request.BonusPercent += DamagePercent; }
        public override void Tick(float deltaTime) { if (!_moving) _stillFor += deltaTime; }
    }

    /// <summary>Combat Bracelet — melee kill grants +15% Melee Attack Speed for 4 s; further melee kills refresh.</summary>
    public sealed class FlowStatePassive : AccessoryPassive
    {
        public const int AttackSpeedPercent = 15;
        public const float DurationSeconds = 4f;
        private TimedBuff _buff;
        public override string Id => "flow_state";
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:flow_state", StatModifier.Percent(StatId.MeleeAttackSpeed, AttackSpeedPercent));
            Context.Events.MeleeKill += OnMeleeKill;
        }
        protected override void OnDetach() { Context.Events.MeleeKill -= OnMeleeKill; _buff.Clear(); }
        private void OnMeleeKill() => _buff.Trigger(DurationSeconds);
        public override void Tick(float deltaTime) => _buff?.Tick(deltaTime);
    }

    /// <summary>Quickdraw Holster — weapon swap grants the new weapon +15% Fire/Attack Speed for 3 s; internal cooldown 6 s.</summary>
    public sealed class HotSwapPassive : AccessoryPassive
    {
        public const int SpeedPercent = 15;
        public const float DurationSeconds = 3f;
        public const float CooldownSeconds = 6f;
        private TimedBuff _buff;
        private float _cooldown;
        public override string Id => "hot_swap";
        public bool IsBuffActive => _buff != null && _buff.IsActive;
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:hot_swap",
                StatModifier.Percent(StatId.FireRate, SpeedPercent),
                StatModifier.Percent(StatId.MeleeAttackSpeed, SpeedPercent));
            Context.Events.WeaponSwapped += OnSwap;
        }
        protected override void OnDetach() { Context.Events.WeaponSwapped -= OnSwap; _buff.Clear(); }
        private void OnSwap()
        {
            if (_cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            _buff.Trigger(DurationSeconds);
        }
        public override void Tick(float deltaTime) { _cooldown = Mathf.Max(0f, _cooldown - deltaTime); _buff?.Tick(deltaTime); }
    }

    /// <summary>Loader's Glove — after completing a reload, the next 3 weapon attacks (shots, not pellets) deal +15% damage.</summary>
    public sealed class FreshMagPassive : AccessoryPassive
    {
        public const int Attacks = 3;
        public const int DamagePercent = 15;
        private int _remaining;
        public override string Id => "fresh_mag";
        public int RemainingAttacks => _remaining;
        protected override void OnAttach()
        {
            Context.Events.ReloadCompleted += OnReload;
            Context.Events.AttackDamageRolling += OnAttack;
        }
        protected override void OnDetach()
        {
            Context.Events.ReloadCompleted -= OnReload;
            Context.Events.AttackDamageRolling -= OnAttack;
            _remaining = 0;
        }
        private void OnReload() => _remaining = Attacks;
        private void OnAttack(AttackDamageRequest request)
        {
            if (_remaining <= 0) return;
            _remaining--;
            request.BonusPercent += DamagePercent;
        }
    }

    /// <summary>Cooling Module — a Blaster that cools from 50+ Heat all the way to 0 generates 50% less Heat on its next 5 shots.</summary>
    public sealed class ColdStartPassive : AccessoryPassive
    {
        public const float PeakHeatThreshold = 50f;
        public const int Shots = 5;
        public const float HeatMultiplier = 0.5f;
        private int _remaining;
        public override string Id => "cold_start";
        public int RemainingShots => _remaining;
        protected override void OnAttach()
        {
            Context.Events.BlasterCooledToZero += OnCooled;
            Context.Events.BlasterShotHeatRolling += OnHeat;
        }
        protected override void OnDetach()
        {
            Context.Events.BlasterCooledToZero -= OnCooled;
            Context.Events.BlasterShotHeatRolling -= OnHeat;
            _remaining = 0;
        }
        private void OnCooled(float peakHeat) { if (peakHeat >= PeakHeatThreshold) _remaining = Shots; }
        private void OnHeat(HeatRequest request)
        {
            if (_remaining <= 0) return;
            _remaining--;
            request.Multiplier *= HeatMultiplier;
        }
    }

    /// <summary>Heat Sink — when a Blaster overheats, emit a 2.5-tile Energy Pulse dealing 30–40 damage; internal cooldown 8 s.</summary>
    public sealed class EmergencyVentPassive : AccessoryPassive
    {
        public const float RadiusTiles = 2.5f;
        public const int DamageMin = 30;
        public const int DamageMax = 40;
        public const float CooldownSeconds = 8f;
        private float _cooldown;
        public override string Id => "emergency_vent";
        protected override void OnAttach() => Context.Events.BlasterOverheated += OnOverheat;
        protected override void OnDetach() => Context.Events.BlasterOverheated -= OnOverheat;
        private void OnOverheat()
        {
            if (_cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            Context.World.AreaDamage(RadiusTiles, DamageMin, DamageMax);
        }
        public override void Tick(float deltaTime) => _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
    }

    /// <summary>Archer's Ring — a fully drawn Bow shot penetrates the first enemy hit without losing damage.</summary>
    public sealed class PerfectDrawPassive : AccessoryPassive
    {
        public override string Id => "perfect_draw";
        protected override void OnAttach() => Context.Events.BowShotFired += OnShot;
        protected override void OnDetach() => Context.Events.BowShotFired -= OnShot;
        private void OnShot(BowShotRequest request) { if (request.IsFullDraw) request.Penetrations = Math.Max(request.Penetrations, 1); }
    }

    /// <summary>Dash Capacitor — the dash endpoint emits a small shockwave with high knockback and stagger, no damage; internal cooldown 6 s.</summary>
    public sealed class DischargePassive : AccessoryPassive
    {
        public const float RadiusTiles = 1.5f;
        public const float Knockback = 8f;
        public const float StaggerPower = 8f;
        public const float CooldownSeconds = 6f;
        private float _cooldown;
        public override string Id => "discharge";
        protected override void OnAttach() => Context.Events.DashEnded += OnDashEnded;
        protected override void OnDetach() => Context.Events.DashEnded -= OnDashEnded;
        private void OnDashEnded()
        {
            if (_cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            Context.World.Shockwave(RadiusTiles, Knockback, StaggerPower);
        }
        public override void Tick(float deltaTime) => _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
    }

    /// <summary>Rangefinder — a projectile that travelled at least 7 tiles before hitting deals +15% damage.</summary>
    public sealed class LongShotPassive : AccessoryPassive
    {
        public const float MinTravelTiles = 7f;
        public const int DamagePercent = 15;
        public override string Id => "long_shot";
        protected override void OnAttach() => Context.Events.ProjectileHitRolling += OnHit;
        protected override void OnDetach() => Context.Events.ProjectileHitRolling -= OnHit;
        private void OnHit(ProjectileHitRequest request) { if (request.TravelDistance >= MinTravelTiles) request.BonusPercent += DamagePercent; }
    }

    /// <summary>Trauma Pendant — healing consumables additionally restore 15% of their normal healing over 5 s.</summary>
    public sealed class SecondPulsePassive : AccessoryPassive
    {
        public const int ExtraPercent = 15;
        public const float DurationSeconds = 5f;
        private int _pending;
        private float _remainingSeconds;
        private float _carry;
        public override string Id => "second_pulse";
        public int PendingHeal => _pending;
        protected override void OnAttach() => Context.Events.HealingConsumableUsed += OnHealing;
        protected override void OnDetach() { Context.Events.HealingConsumableUsed -= OnHealing; _pending = 0; _remainingSeconds = 0f; }
        private void OnHealing(HealingRequest request)
        {
            // "Normal healing amount" = the consumable's own amount before other reactive bonuses.
            _pending += Mathf.RoundToInt(request.BaseAmount * ExtraPercent / 100f);
            _remainingSeconds = DurationSeconds;
            _carry = 0f;
        }
        public override void Tick(float deltaTime)
        {
            if (_pending <= 0 || _remainingSeconds <= 0f) return;
            var step = Mathf.Min(deltaTime, _remainingSeconds);
            _carry += _pending * (step / _remainingSeconds);
            var whole = Mathf.FloorToInt(_carry);
            _remainingSeconds -= step;
            if (_remainingSeconds <= 0.0001f) whole = _pending;
            if (whole > 0)
            {
                Context.Heal(whole);
                _pending -= whole;
                _carry -= whole;
            }
        }
    }

    /// <summary>Ammo Pouch — ammo pickups grant 25% more ammo, rounded to the nearest whole unit.</summary>
    public sealed class ScavengersReservePassive : AccessoryPassive
    {
        public const int BonusPercent = 25;
        public override string Id => "scavengers_reserve";
        protected override void OnAttach() => Context.Events.AmmoPickupRolling += OnPickup;
        protected override void OnDetach() => Context.Events.AmmoPickupRolling -= OnPickup;
        private void OnPickup(AmmoPickupRequest request) => request.BonusPercent += BonusPercent;
    }

    /// <summary>Magnetic Coil — on Combat Room clear, remaining Coins and Ammo pickups are pulled to the wearer.</summary>
    public sealed class RoomSweepPassive : AccessoryPassive
    {
        public override string Id => "room_sweep";
        protected override void OnAttach() => Context.Events.CombatRoomCleared += OnCleared;
        protected override void OnDetach() => Context.Events.CombatRoomCleared -= OnCleared;
        private void OnCleared() => Context.World.PullCoinAndAmmoPickups();
    }

    /// <summary>Stabilizer — after 1 s of continuous firing, Spread is reduced by an additional 25% until firing stops.</summary>
    public sealed class LockInPassive : AccessoryPassive
    {
        public const float ContinuousSeconds = 1f;
        public const int SpreadReductionPercent = 25;
        private const string SourceId = "passive:lock_in";
        private bool _firing;
        private float _firingFor;
        private bool _applied;
        public override string Id => "lock_in";
        public bool IsApplied => _applied;
        protected override void OnAttach() => Context.Events.FiringStateChanged += OnFiring;
        protected override void OnDetach() { Context.Events.FiringStateChanged -= OnFiring; SetApplied(false); }
        private void OnFiring(bool firing)
        {
            _firing = firing;
            _firingFor = 0f;
            if (!firing) SetApplied(false);
        }
        public override void Tick(float deltaTime)
        {
            if (!_firing) return;
            _firingFor += deltaTime;
            if (_firingFor >= ContinuousSeconds) SetApplied(true);
        }
        private void SetApplied(bool applied)
        {
            if (applied == _applied) return;
            _applied = applied;
            if (applied) Context.Stats.SetSource(new StatModifierSource(SourceId, StatModifier.Percent(StatId.WeaponSpreadReduction, SpreadReductionPercent)));
            else Context.Stats.RemoveSource(SourceId);
        }
    }

    /// <summary>Impact Module — a normal enemy or Elite knocked into a wall takes 15–20 bonus damage and high stagger; per-target cooldown 2 s; never bosses.</summary>
    public sealed class WallbreakerPassive : AccessoryPassive
    {
        public const int BonusMin = 15;
        public const int BonusMax = 20;
        public const float PerTargetCooldownSeconds = 2f;
        private readonly Dictionary<string, float> _targetCooldowns = new();
        private readonly List<string> _cooldownScratch = new();
        public override string Id => "wallbreaker";
        protected override void OnAttach() => Context.Events.EnemyKnockedIntoWall += OnWall;
        protected override void OnDetach() { Context.Events.EnemyKnockedIntoWall -= OnWall; _targetCooldowns.Clear(); }
        private void OnWall(WallImpactRequest request)
        {
            if (request.IsBoss || string.IsNullOrEmpty(request.TargetId)) return;
            if (_targetCooldowns.TryGetValue(request.TargetId, out var remaining) && remaining > 0f) return;
            _targetCooldowns[request.TargetId] = PerTargetCooldownSeconds;
            request.BonusDamageMin = BonusMin;
            request.BonusDamageMax = BonusMax;
            request.ApplyHighStagger = true;
        }
        public override void Tick(float deltaTime)
        {
            if (_targetCooldowns.Count == 0) return;
            _cooldownScratch.Clear();
            _cooldownScratch.AddRange(_targetCooldowns.Keys);
            foreach (var key in _cooldownScratch)
            {
                _targetCooldowns[key] = Mathf.Max(0f, _targetCooldowns[key] - deltaTime);
            }
        }
    }

    /// <summary>Shock Charm — when the wearer staggers an enemy, emit a 2.5-tile shockwave applying high stagger nearby; internal cooldown 6 s.</summary>
    public sealed class ArcStaggerPassive : AccessoryPassive
    {
        public const float RadiusTiles = 2.5f;
        public const float StaggerPower = 8f;
        public const float CooldownSeconds = 6f;
        private float _cooldown;
        public override string Id => "arc_stagger";
        protected override void OnAttach() => Context.Events.EnemyStaggeredByWearer += OnStagger;
        protected override void OnDetach() => Context.Events.EnemyStaggeredByWearer -= OnStagger;
        private void OnStagger(string targetId)
        {
            if (_cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            Context.World.Shockwave(RadiusTiles, 0f, StaggerPower);
        }
        public override void Tick(float deltaTime) => _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
    }

    /// <summary>Creates the fixed passive for a Legendary accessory mechanic id (34_LEGENDARY_ACCESSORY_PASSIVES).</summary>
    public static class AccessoryPassiveFactory
    {
        private static readonly Dictionary<string, Func<AccessoryPassive>> Factories = new()
        {
            ["accessory_momentum"] = () => new WatchMomentumPassive(),
            ["steady_aim"] = () => new SteadyAimPassive(),
            ["flow_state"] = () => new FlowStatePassive(),
            ["hot_swap"] = () => new HotSwapPassive(),
            ["fresh_mag"] = () => new FreshMagPassive(),
            ["cold_start"] = () => new ColdStartPassive(),
            ["emergency_vent"] = () => new EmergencyVentPassive(),
            ["perfect_draw"] = () => new PerfectDrawPassive(),
            ["discharge"] = () => new DischargePassive(),
            ["long_shot"] = () => new LongShotPassive(),
            ["second_pulse"] = () => new SecondPulsePassive(),
            ["scavengers_reserve"] = () => new ScavengersReservePassive(),
            ["room_sweep"] = () => new RoomSweepPassive(),
            ["lock_in"] = () => new LockInPassive(),
            ["wallbreaker"] = () => new WallbreakerPassive(),
            ["arc_stagger"] = () => new ArcStaggerPassive()
        };

        public static IReadOnlyCollection<string> KnownIds => Factories.Keys;

        public static AccessoryPassive Create(string mechanicId)
        {
            return !string.IsNullOrEmpty(mechanicId) && Factories.TryGetValue(mechanicId, out var factory) ? factory() : null;
        }
    }
}
