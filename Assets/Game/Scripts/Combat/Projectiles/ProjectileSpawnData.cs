using RuinRail.Gameplay.Combat.Impact;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Projectiles
{
    public readonly struct ProjectileSpawnData
    {
        public int Damage { get; }
        public float Speed { get; }
        public float MaxRange { get; }
        public float Knockback { get; }
        public float StaggerPower { get; }
        public Vector2 Direction { get; }
        public GameObject Source { get; }

        /// <summary>Attacker-side impact hooks (player wearer effects); null for enemies/hazards.</summary>
        public IImpactAttackerFeedback Feedback { get; }

        /// <summary>Detonation radius on impact/expiry; 0 = direct-hit projectile.</summary>
        public float ExplosionRadius { get; }

        /// <summary>Team the shot belongs to; the explosion never touches that team (friendly fire OFF for players).</summary>
        public DamageTeam SourceTeam { get; }
        public bool IsExplosive => ExplosionRadius > 0f;

        /// <summary>Piercing shots damage every target along their path once each and stop only at walls or range.</summary>
        public bool Piercing { get; }

        /// <summary>
        /// A non-piercing shot that passes through its first N targets (full damage each) before an ordinary hit resolves
        /// it — a fully drawn Bow shot with Perfect Draw (items/34). 0 = ordinary.
        /// </summary>
        public int PierceCount { get; }

        /// <summary>The in-flight presentation profile (ProjectileVisualCatalog id); null/empty = the side's default. Presentation only.</summary>
        public string VisualId { get; }

        public ProjectileSpawnData(
            int damage,
            float speed,
            float maxRange,
            float knockback,
            float staggerPower,
            Vector2 direction,
            GameObject source = null,
            IImpactAttackerFeedback feedback = null,
            float explosionRadius = 0f,
            DamageTeam sourceTeam = DamageTeam.Enemy,
            bool piercing = false,
            string visualId = null,
            int pierceCount = 0)
        {
            VisualId = visualId;
            PierceCount = Mathf.Max(0, pierceCount);
            Piercing = piercing;
            Feedback = feedback;
            ExplosionRadius = Mathf.Max(0f, explosionRadius);
            SourceTeam = sourceTeam;
            Damage = damage;
            Speed = speed;
            MaxRange = maxRange;
            Knockback = knockback;
            StaggerPower = staggerPower;
            Direction = direction;
            Source = source;
        }

        /// <summary>The same shot with a named in-flight visual (nothing mechanical changes).</summary>
        public ProjectileSpawnData WithVisual(string visualId) =>
            new(Damage, Speed, MaxRange, Knockback, StaggerPower, Direction, Source, Feedback, ExplosionRadius, SourceTeam, Piercing, visualId, PierceCount);
    }
}
