using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Armor
{
    /// <summary>Scrap Vest — after clearing a Combat Room, restore 6% of Max HP.</summary>
    public sealed class PatchworkPassive : ArmorPassive
    {
        public const int HealPercentOfMax = 6;
        public override string Id => "patchwork";
        public override string Description => $"After clearing a Combat Room, restore {HealPercentOfMax}% of Max HP.";
        protected override void OnAttach() => Context.Events.CombatRoomCleared += OnCleared;
        protected override void OnDetach() => Context.Events.CombatRoomCleared -= OnCleared;
        private void OnCleared() => Context.Heal(Mathf.RoundToInt(Context.MaxHealth() * HealPercentOfMax / 100f));
    }

    /// <summary>Scout Rig — after Dash, +12% Movement Speed for 1 second.</summary>
    public sealed class MomentumPassive : ArmorPassive
    {
        public const int MovePercent = 12;
        public const float DurationSeconds = 1f;
        private TimedBuff _buff;
        public override string Id => "momentum";
        public override string Description => $"After a Dash, +{MovePercent}% Movement Speed for {DurationSeconds:0.#} s.";
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:momentum", StatModifier.Percent(StatId.MovementSpeed, MovePercent));
            Context.Events.Dashed += OnDashed;
        }
        protected override void OnDetach()
        {
            Context.Events.Dashed -= OnDashed;
            _buff.Clear();
        }
        private void OnDashed() => _buff.Trigger(DurationSeconds);
        public override void Tick(float deltaTime) => _buff?.Tick(deltaTime);
    }

    /// <summary>Riot Armor — fully negate one incoming stagger every 8 seconds; damage still applies.</summary>
    public sealed class AnchoredPassive : ArmorPassive
    {
        public const float CooldownSeconds = 8f;
        private float _cooldown;
        public override string Id => "anchored";
        public override string Description => $"Fully negate one incoming stagger every {CooldownSeconds:0.#} s (damage still applies).";
        public bool IsReady => _cooldown <= 0f;
        protected override void OnAttach() => Context.Events.StaggerIncoming += OnStagger;
        protected override void OnDetach() => Context.Events.StaggerIncoming -= OnStagger;
        private void OnStagger(NegatableRequest request)
        {
            if (!IsReady || request.IsNegated) return;
            request.Negate(Id);
            _cooldown = CooldownSeconds;
        }
        public override void Tick(float deltaTime) => _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
    }

    /// <summary>Heavy Plate — while below 25% HP, +15% General DR (still subject to the DR caps).</summary>
    public sealed class LastStandPassive : ArmorPassive
    {
        public const int ThresholdPercent = 25;
        public const int DrPercent = 15;
        private const string SourceId = "passive:last_stand";
        private bool _active;
        public override string Id => "last_stand";
        public override string Description => $"While below {ThresholdPercent}% HP: +{DrPercent}% Damage Reduction (DR caps still apply).";
        public bool IsActive => _active;
        protected override void OnAttach()
        {
            Context.Events.HealthChanged += OnHealth;
            OnHealth(Context.CurrentHealth(), Context.MaxHealth());
        }
        protected override void OnDetach()
        {
            Context.Events.HealthChanged -= OnHealth;
            SetActive(false);
        }
        private void OnHealth(int current, int max) => SetActive(max > 0 && current * 100 < max * ThresholdPercent);
        private void SetActive(bool active)
        {
            if (active == _active) return;
            _active = active;
            if (active) Context.Stats.SetSource(new StatModifierSource(SourceId, StatModifier.Percent(StatId.GeneralDamageReduction, DrPercent)));
            else Context.Stats.RemoveSource(SourceId);
        }
    }

    /// <summary>Blast Suit — ignore knockback caused by explosions; explosion damage still applies.</summary>
    public sealed class ShockAbsorberPassive : ArmorPassive
    {
        public override string Id => "shock_absorber";
        public override string Description => "Ignore knockback from explosions (explosion damage still applies).";
        protected override void OnAttach() => Context.Events.ExplosionKnockbackIncoming += OnKnockback;
        protected override void OnDetach() => Context.Events.ExplosionKnockbackIncoming -= OnKnockback;
        private void OnKnockback(NegatableRequest request) => request.Negate(Id);
    }

    /// <summary>Medic Harness — the first healing consumable used in each Combat Room heals 25% more.</summary>
    public sealed class EmergencyCarePassive : ArmorPassive
    {
        public const int BonusPercent = 25;
        private bool _usedThisRoom;
        private bool _inCombatRoom;
        public override string Id => "emergency_care";
        public override string Description => $"The first healing consumable used in each Combat Room heals {BonusPercent}% more.";
        protected override void OnAttach()
        {
            Context.Events.CombatRoomEntered += OnRoomEntered;
            Context.Events.CombatRoomCleared += OnRoomCleared;
            Context.Events.HealingConsumableUsed += OnHealing;
        }
        protected override void OnDetach()
        {
            Context.Events.CombatRoomEntered -= OnRoomEntered;
            Context.Events.CombatRoomCleared -= OnRoomCleared;
            Context.Events.HealingConsumableUsed -= OnHealing;
        }
        private void OnRoomEntered() { _inCombatRoom = true; _usedThisRoom = false; }
        private void OnRoomCleared() { _inCombatRoom = false; }
        private void OnHealing(HealingRequest request)
        {
            if (!_inCombatRoom || _usedThisRoom) return;
            _usedThisRoom = true;
            request.BonusPercent += BonusPercent;
        }
    }

    /// <summary>Combat Harness — kill an enemy: +10% Movement Speed for 3 seconds; internal cooldown 5 seconds.</summary>
    public sealed class AdrenalinePassive : ArmorPassive
    {
        public const int MovePercent = 10;
        public const float DurationSeconds = 3f;
        public const float CooldownSeconds = 5f;
        private TimedBuff _buff;
        private float _cooldown;
        public override string Id => "adrenaline";
        public override string Description => $"Kill an enemy: +{MovePercent}% Movement Speed for {DurationSeconds:0.#} s ({CooldownSeconds:0.#} s cooldown).";
        public bool IsBuffActive => _buff != null && _buff.IsActive;
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:adrenaline", StatModifier.Percent(StatId.MovementSpeed, MovePercent));
            Context.Events.EnemyKilled += OnKill;
        }
        protected override void OnDetach()
        {
            Context.Events.EnemyKilled -= OnKill;
            _buff.Clear();
        }
        private void OnKill()
        {
            if (_cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            _buff.Trigger(DurationSeconds);
        }
        public override void Tick(float deltaTime)
        {
            _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
            _buff?.Tick(deltaTime);
        }
    }

    /// <summary>Reinforced Exo-Rig — a single hit of 20+ final damage grants +30% Stagger and Knockback Resistance for 5 s; cooldown 8 s.</summary>
    public sealed class ExoLockPassive : ArmorPassive
    {
        public const int DamageThreshold = 20;
        public const int ResistPercent = 30;
        public const float DurationSeconds = 5f;
        public const float CooldownSeconds = 8f;
        private TimedBuff _buff;
        private float _cooldown;
        public override string Id => "exo_lock";
        public override string Description => $"A hit of {DamageThreshold}+ damage grants +{ResistPercent}% Stagger and Knockback Resistance for {DurationSeconds:0.#} s ({CooldownSeconds:0.#} s cooldown).";
        public bool IsBuffActive => _buff != null && _buff.IsActive;
        protected override void OnAttach()
        {
            _buff = new TimedBuff(Context.Stats, "passive:exo_lock",
                StatModifier.Percent(StatId.StaggerResistance, ResistPercent),
                StatModifier.Percent(StatId.KnockbackResistance, ResistPercent));
            Context.Events.DamageTaken += OnDamage;
        }
        protected override void OnDetach()
        {
            Context.Events.DamageTaken -= OnDamage;
            _buff.Clear();
        }
        private void OnDamage(int finalDamage)
        {
            if (finalDamage < DamageThreshold || _cooldown > 0f) return;
            _cooldown = CooldownSeconds;
            _buff.Trigger(DurationSeconds);
        }
        public override void Tick(float deltaTime)
        {
            _cooldown = Mathf.Max(0f, _cooldown - deltaTime);
            _buff?.Tick(deltaTime);
        }
    }

    /// <summary>Runner Suit — the first time HP falls below 30% in a Combat Room, reset Dash cooldown; once per Combat Room.</summary>
    public sealed class SecondWindPassive : ArmorPassive
    {
        public const int ThresholdPercent = 30;
        private bool _inCombatRoom;
        private bool _usedThisRoom;
        private bool _wasBelow;
        public override string Id => "second_wind";
        public override string Description => $"The first time HP falls below {ThresholdPercent}% in a Combat Room, the Dash cooldown resets (once per room).";
        protected override void OnAttach()
        {
            Context.Events.CombatRoomEntered += OnRoomEntered;
            Context.Events.CombatRoomCleared += OnRoomCleared;
            Context.Events.HealthChanged += OnHealth;
            _wasBelow = IsBelow(Context.CurrentHealth(), Context.MaxHealth());
        }
        protected override void OnDetach()
        {
            Context.Events.CombatRoomEntered -= OnRoomEntered;
            Context.Events.CombatRoomCleared -= OnRoomCleared;
            Context.Events.HealthChanged -= OnHealth;
        }
        private static bool IsBelow(int current, int max) => max > 0 && current * 100 < max * ThresholdPercent;
        private void OnRoomEntered() { _inCombatRoom = true; _usedThisRoom = false; }
        private void OnRoomCleared() { _inCombatRoom = false; }
        private void OnHealth(int current, int max)
        {
            var below = IsBelow(current, max);
            if (below && !_wasBelow && _inCombatRoom && !_usedThisRoom)
            {
                _usedThisRoom = true;
                Context.ResetDashCooldown();
            }

            _wasBelow = below;
        }
    }

    /// <summary>Creates the fixed passive for a Legendary mechanic id (28_ARMOR_CATALOG). Unknown ids yield null.</summary>
    public static class ArmorPassiveFactory
    {
        private static readonly Dictionary<string, Func<ArmorPassive>> Factories = new()
        {
            ["patchwork"] = () => new PatchworkPassive(),
            ["momentum"] = () => new MomentumPassive(),
            ["anchored"] = () => new AnchoredPassive(),
            ["last_stand"] = () => new LastStandPassive(),
            ["shock_absorber"] = () => new ShockAbsorberPassive(),
            ["emergency_care"] = () => new EmergencyCarePassive(),
            ["adrenaline"] = () => new AdrenalinePassive(),
            ["exo_lock"] = () => new ExoLockPassive(),
            ["second_wind"] = () => new SecondWindPassive()
        };

        public static IReadOnlyCollection<string> KnownIds => Factories.Keys;

        public static ArmorPassive Create(string mechanicId)
        {
            return !string.IsNullOrEmpty(mechanicId) && Factories.TryGetValue(mechanicId, out var factory) ? factory() : null;
        }
    }
}
