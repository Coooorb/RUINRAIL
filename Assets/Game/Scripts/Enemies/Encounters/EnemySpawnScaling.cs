using System;
using RuinRail.Gameplay.Combat;

namespace RuinRail.Gameplay.Enemies.Encounters
{
    /// <summary>Wraps a damage roller so every enemy damage band is scaled by the depth curve (59) before rolling.</summary>
    public sealed class DepthScaledDamageRoller : IDamageRoller
    {
        private readonly IDamageRoller _inner;
        private readonly int _depth;
        private readonly DepthScalingConfig _config;

        public DepthScaledDamageRoller(IDamageRoller inner, int depth, DepthScalingConfig config = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _depth = Math.Max(1, depth);
            _config = config;
        }

        public int Roll(int minInclusive, int maxInclusive)
        {
            var (min, max) = DepthScaling.ScaledDamage(minInclusive, maxInclusive, _depth, _config);
            return _inner.Roll(min, max);
        }
    }

    /// <summary>
    /// Applies depth + party scaling to a freshly spawned enemy (72/59/83): health (depth, then party, rounded once),
    /// damage (depth only), attack speed and movement speed (both capped). Called once per spawn by the room runtime.
    /// </summary>
    public static class EnemySpawnScaling
    {
        public static void Apply(EnemyController enemy, int depth, int partySize, DepthScalingConfig config = null, IDamageRoller baseRoller = null, bool isBoss = false)
        {
            if (enemy == null || enemy.Definition == null) return;
            // The roller re-applies the definition (base health), so health is scaled last.
            enemy.SetDamageRoller(new DepthScaledDamageRoller(baseRoller ?? new UnityRandomDamageRoller(), depth, config));
            enemy.SetAttackSpeedMultiplier(DepthScaling.AttackSpeedMultiplier(depth, config));
            enemy.SetMovementSpeedMultiplier(DepthScaling.MovementSpeedMultiplier(depth, config));
            var health = enemy.GetComponent<HealthComponent>();
            if (health != null)
            {
                health.SetMaxHealth(DepthScaling.ScaledHealth(enemy.Definition.BaseHealth, depth, partySize, isBoss, config));
            }
        }
    }
}
