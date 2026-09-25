using System.Collections.Generic;
using RuinRail.Gameplay.Combat.Impact;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Area
{
    /// <summary>
    /// Reusable AoE application: every IDamageable within a circle is hit at most once per resolution (multi-collider
    /// targets dedupe on the damageable), filtered by team so player-sourced effects never touch Players and enemy
    /// effects never touch Enemies. Each target rolls its own integer damage.
    /// </summary>
    public static class AreaDamageResolver
    {
        private static readonly Collider2D[] Overlaps = new Collider2D[64];

        public readonly struct Result
        {
            public Result(int targetsHit, int totalDamage)
            {
                TargetsHit = targetsHit;
                TotalDamage = totalDamage;
            }

            public int TargetsHit { get; }
            public int TotalDamage { get; }
        }

        /// <summary>Collects unique damageables in the circle whose team differs from <paramref name="sourceTeam"/>.</summary>
        public static List<IDamageable> CollectTargets(Vector2 center, float radius, DamageTeam sourceTeam)
        {
            var count = Physics2D.OverlapCircle(center, radius, Physics2DQueries.LegacyQueryFilter(), Overlaps);
            var targets = new List<IDamageable>();
            var seen = new HashSet<IDamageable>();
            for (var i = 0; i < count; i++)
            {
                var damageable = Overlaps[i].GetComponentInParent<IDamageable>();
                if (damageable == null || !seen.Add(damageable)) continue;
                if (TeamMember.TeamOf(Overlaps[i]) == sourceTeam) continue;
                targets.Add(damageable);
            }

            return targets;
        }

        public static Result Apply(Vector2 center, float radius, int damageMin, int damageMax, DamageKind kind, float staggerPower, DamageTeam sourceTeam, IDamageRoller roller,
            float knockback = 0f, GameObject source = null, IImpactAttackerFeedback feedback = null)
        {
            var hit = 0;
            var total = 0;
            foreach (var target in CollectTargets(center, radius, sourceTeam))
            {
                var damage = roller.Roll(damageMin, damageMax);
                if (target.TryApplyDamage(new DamageRequest(damage, kind, staggerPower)))
                {
                    hit++;
                    total += damage;
                    if (target is Component component && (staggerPower > 0f || knockback > 0f))
                    {
                        var direction = (Vector2)component.transform.position - center;
                        ImpactDispatcher.Apply(component, new ImpactRequest(direction, knockback, staggerPower, kind, source, feedback));
                    }

                    // The blast's owner took this target's last health (Adrenaline); a co-op replica never dies locally.
                    if (feedback != null && target is Component struck && struck.GetComponentInParent<HealthComponent>() is { IsAlive: false }) feedback.OnTargetKilled(false);
                }
            }

            return new Result(hit, total);
        }
    }
}
