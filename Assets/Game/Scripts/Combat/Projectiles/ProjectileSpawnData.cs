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
            bool piercing = false)
        {
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
    }
}
