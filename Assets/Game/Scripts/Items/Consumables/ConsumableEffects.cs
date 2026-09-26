using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Items.Consumables
{
    /// <summary>Player-side sinks a consumable effect writes to.</summary>
    public sealed class ConsumableTargets
    {
        public ConsumableTargets(PlayerStats stats, PlayerCombatEvents events, Func<int, int> heal, Func<GrenadeData, bool> throwGrenade = null, Func<ReviveRequest, bool> requestRevive = null, Func<bool> canReceiveHealing = null)
        {
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            Events = events;
            Heal = heal ?? throw new ArgumentNullException(nameof(heal));
            ThrowGrenade = throwGrenade;
            RequestRevive = requestRevive;
            CanReceiveHealing = canReceiveHealing;
        }

        /// <summary>True while the player is below maximum effective HP (a heal would restore something); null = unknown, treated as yes.</summary>
        public Func<bool> CanReceiveHealing { get; }

        /// <summary>Launches a grenade with the authored data toward the player's aim; null = throwing unsupported here.</summary>
        public Func<GrenadeData, bool> ThrowGrenade { get; }

        /// <summary>
        /// Asks the co-op revive system to revive a fully Dead teammate; returns true only when the revive actually
        /// happened (validated target, host-authoritative). Null in Solo and until the downed/revive task exists.
        /// </summary>
        public Func<ReviveRequest, bool> RequestRevive { get; }

        public PlayerStats Stats { get; }
        public PlayerCombatEvents Events { get; }

        /// <summary>Applies healing to the player; returns the amount actually restored (clamped at max HP).</summary>
        public Func<int, int> Heal { get; }
    }

    /// <summary>Validated revive-use request (84_DOWNED_REVIVE_DEATH): the responder revives one fully Dead teammate at the given HP fraction.</summary>
    public sealed class ReviveRequest
    {
        public ReviveRequest(string consumableId, int healthPercent)
        {
            ConsumableId = consumableId;
            HealthPercent = healthPercent;
        }

        public string ConsumableId { get; }
        public int HealthPercent { get; }
    }

    /// <summary>
    /// Applies consumable effects and owns their timers. Healing: base amount → reactive hub bonuses (Emergency Care
    /// etc.) → capped Healing Received multiplier → health clamp. Timed buffs: one source per definition, refreshed
    /// (never stacked) on re-use — the deterministic policy chosen because 31_CONSUMABLES is silent on stacking —
    /// and removed exactly once on expiry.
    /// </summary>
    public sealed class ConsumableEffectRunner
    {
        private sealed class ActiveBuff
        {
            public string DefinitionId;
            public string SourceId;
            public float Remaining;
            public bool ArmorInjector;
        }

        private readonly ConsumableTargets _targets;
        private readonly Dictionary<string, ActiveBuff> _buffs = new();

        public ConsumableEffectRunner(ConsumableTargets targets)
        {
            _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        }

        public IReadOnlyCollection<string> ActiveBuffDefinitionIds => _buffs.Keys;
        public bool CanThrowGrenades => _targets.ThrowGrenade != null;
        public bool CanRequestRevives => _targets.RequestRevive != null;

        public float RemainingSeconds(string definitionId) => _buffs.TryGetValue(definitionId, out var b) ? b.Remaining : 0f;

        public event Action<ConsumableDefinition, int> Healed;
        public event Action<ConsumableDefinition> BuffStarted;
        public event Action<string> BuffExpired;

        /// <summary>
        /// Use-start eligibility: would the complete effect do anything right now? Only a heal can be a total no-op
        /// (the player is already at maximum effective HP — its reactive bonuses scale that same heal). Timed buffs
        /// refresh their duration, grenades are thrown, and a revive with no valid target already spends nothing.
        /// </summary>
        public bool WouldHaveEffect(ConsumableDefinition definition)
        {
            if (definition == null) return false;
            return definition.EffectKind switch
            {
                ConsumableEffectKind.Heal => _targets.CanReceiveHealing == null || _targets.CanReceiveHealing(),
                _ => true
            };
        }

        /// <summary>Applies the definition's effect. Returns false only for effect kinds this runner does not handle (grenades).</summary>
        public bool Apply(ConsumableDefinition definition)
        {
            if (definition == null) return false;
            switch (definition.EffectKind)
            {
                case ConsumableEffectKind.Heal:
                    ApplyHeal(definition);
                    return true;
                case ConsumableEffectKind.TimedBuff:
                    ApplyBuff(definition);
                    return true;
                case ConsumableEffectKind.Grenade:
                    return _targets.ThrowGrenade != null && _targets.ThrowGrenade(definition.Grenade);
                case ConsumableEffectKind.Revive:
                    return _targets.RequestRevive != null && _targets.RequestRevive(new ReviveRequest(definition.Id, definition.ReviveHealthPercent));
                default:
                    return false;
            }
        }

        private void ApplyHeal(ConsumableDefinition definition)
        {
            var amount = definition.HealAmount;
            if (_targets.Events != null)
            {
                amount = _targets.Events.RaiseHealingConsumableUsed(amount).FinalAmount;
            }

            amount = Mathf.Max(0, Mathf.RoundToInt(amount * _targets.Stats.GetMultiplier(StatId.HealingReceived)));
            var restored = _targets.Heal(amount);
            Healed?.Invoke(definition, restored);
        }

        private void ApplyBuff(ConsumableDefinition definition)
        {
            var sourceId = $"consumable:{definition.Id}";
            if (!_buffs.TryGetValue(definition.Id, out var buff))
            {
                buff = new ActiveBuff { DefinitionId = definition.Id, SourceId = sourceId, ArmorInjector = definition.ActivatesArmorInjectorCap };
                _buffs[definition.Id] = buff;
            }

            buff.Remaining = definition.BuffDurationSeconds;
            _targets.Stats.SetSource(new StatModifierSource(sourceId, StatModifier.Percent(definition.BuffStat, definition.BuffPercent)));
            if (buff.ArmorInjector) _targets.Stats.ArmorInjectorActive = true;
            BuffStarted?.Invoke(definition);
        }

        private readonly List<string> _expiredScratch = new();

        public void Tick(float deltaTime)
        {
            if (_buffs.Count == 0 || deltaTime <= 0f) return;
            _expiredScratch.Clear();
            foreach (var buff in _buffs.Values)
            {
                buff.Remaining -= deltaTime;
                if (buff.Remaining <= 0f) _expiredScratch.Add(buff.DefinitionId);
            }

            foreach (var id in _expiredScratch) Expire(id);
        }

        private void Expire(string definitionId)
        {
            if (!_buffs.TryGetValue(definitionId, out var buff)) return;
            _buffs.Remove(definitionId);
            _targets.Stats.RemoveSource(buff.SourceId);
            if (buff.ArmorInjector && !AnyArmorInjectorActive()) _targets.Stats.ArmorInjectorActive = false;
            BuffExpired?.Invoke(definitionId);
        }

        private bool AnyArmorInjectorActive()
        {
            foreach (var buff in _buffs.Values) if (buff.ArmorInjector) return true;
            return false;
        }

        public void ClearAll()
        {
            foreach (var id in new List<string>(_buffs.Keys)) Expire(id);
        }
    }
}
