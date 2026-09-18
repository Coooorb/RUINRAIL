using System;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Impact
{
    /// <summary>
    /// Hidden stagger pressure for one target: pressure accumulates per hit (after resistance), decays while the target
    /// is not staggered, triggers exactly when the threshold is crossed, and is followed by a fixed immunity window so
    /// nothing can be stun-locked. Pure and deterministic; the same inputs always produce the same trigger frame.
    /// </summary>
    public sealed class StaggerMeter
    {
        private readonly StaggerConfig _config;

        public StaggerMeter(StaggerConfig config)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        public float Pressure { get; private set; }
        public float Threshold => _config.Threshold;
        public bool IsStaggered => StaggerTimeRemaining > 0f;
        public float StaggerTimeRemaining { get; private set; }
        public float ImmunityRemaining { get; private set; }
        public bool IsImmune => ImmunityRemaining > 0f;
        public int TriggerCount { get; private set; }

        public event Action Staggered;
        public event Action StaggerEnded;

        /// <summary>Adds <paramref name="staggerPower"/> reduced by <paramref name="resistancePercent"/> (0–100). Returns what happened.</summary>
        public StaggerResult Apply(float staggerPower, int resistancePercent)
        {
            if (staggerPower <= 0f) return StaggerResult.None;
            var resist = Mathf.Clamp(resistancePercent, 0, 100);
            if (resist >= 100 || IsStaggered || IsImmune) return StaggerResult.None;

            var applied = staggerPower * (100 - resist) / 100f;
            Pressure += applied;
            if (Pressure + 0.0001f < Threshold) return new StaggerResult(applied, false, false);

            Pressure = 0f;
            StaggerTimeRemaining = _config.StaggerDurationSeconds;
            TriggerCount++;
            Staggered?.Invoke();
            if (StaggerTimeRemaining <= 0f) EndStagger();
            return new StaggerResult(applied, true, false);
        }

        /// <summary>Advances timers: stagger duration, then immunity, and pressure decay while idle.</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            if (IsStaggered)
            {
                StaggerTimeRemaining = Mathf.Max(0f, StaggerTimeRemaining - deltaTime);
                if (!IsStaggered) EndStagger();
                return;
            }

            if (IsImmune) ImmunityRemaining = Mathf.Max(0f, ImmunityRemaining - deltaTime);
            if (Pressure > 0f) Pressure = Mathf.Max(0f, Pressure - _config.RecoveryPerSecond * deltaTime);
        }

        public void Reset()
        {
            Pressure = 0f;
            StaggerTimeRemaining = 0f;
            ImmunityRemaining = 0f;
        }

        private void EndStagger()
        {
            StaggerTimeRemaining = 0f;
            ImmunityRemaining = _config.PostStaggerImmunitySeconds;
            StaggerEnded?.Invoke();
        }
    }

    /// <summary>Knockback arithmetic shared by every receiver: displacement from knockback points and resistance.</summary>
    public static class KnockbackMath
    {
        /// <summary>World-unit displacement for <paramref name="knockback"/> points against <paramref name="resistancePercent"/> (0–100).</summary>
        public static float Distance(float knockback, int resistancePercent, StaggerConfig config)
        {
            if (config == null || knockback <= 0f) return 0f;
            var resist = Mathf.Clamp(resistancePercent, 0, 100);
            if (resist >= 100) return 0f;
            var distance = knockback * config.UnitsPerKnockbackPoint * (100 - resist) / 100f;
            distance = Mathf.Min(distance, config.MaxKnockbackDistance);
            return distance < config.MinKnockbackDistance ? 0f : distance;
        }
    }
}
