using System;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Area
{
    /// <summary>
    /// Burning ground (Incendiary): every whole second for the configured duration, each opposing IDamageable inside the
    /// radius takes the tick damage as Burn. Ticks are counted deterministically (duration × 1 tick/s) regardless of
    /// frame rate, dead targets are refused by their health, and the zone destroys itself after the last tick.
    /// </summary>
    public sealed class BurnZone : MonoBehaviour
    {
        private float _radius;
        private int _damagePerSecond;
        private int _totalTicks;
        private int _ticksDone;
        private float _elapsed;
        private DamageTeam _sourceTeam;
        private IDamageRoller _roller;

        public float Radius => _radius;
        public int TicksDone => _ticksDone;
        public int TotalTicks => _totalTicks;
        public bool IsFinished => _ticksDone >= _totalTicks;

        public event Action<BurnZone, int> Ticked;

        public void Configure(float radius, int damagePerSecond, float durationSeconds, DamageTeam sourceTeam)
        {
            _radius = radius;
            _damagePerSecond = Mathf.Max(0, damagePerSecond);
            _totalTicks = Mathf.Max(0, Mathf.RoundToInt(durationSeconds));
            _sourceTeam = sourceTeam;
            _roller = ExactRoller.Instance; // burn ticks are a fixed amount, never a roll
            _ticksDone = 0;
            _elapsed = 0f;
        }

        private sealed class ExactRoller : IDamageRoller
        {
            public static readonly ExactRoller Instance = new();
            public int Roll(int minInclusive, int maxInclusive) => minInclusive;
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>Deterministic time stepping (also used by tests): tick k fires once elapsed ≥ k seconds.</summary>
        public void Advance(float deltaTime)
        {
            if (IsFinished) return;
            _elapsed += deltaTime;
            while (!IsFinished && _elapsed >= _ticksDone + 1)
            {
                _ticksDone++;
                var result = AreaDamageResolver.Apply(transform.position, _radius, _damagePerSecond, _damagePerSecond, DamageKind.Burn, 0f, _sourceTeam, _roller);
                Ticked?.Invoke(this, result.TargetsHit);
            }

            if (IsFinished)
            {
                Destroy(gameObject);
            }
        }
    }
}
