using UnityEngine;

namespace RuinRail.Gameplay.Combat.Impact
{
    /// <summary>
    /// Attacker-side hooks a receiver reports back to, without knowing what listens (armor/accessory passives on the
    /// player side). Null when the attacker has no reactive equipment (enemies, hazards).
    /// </summary>
    public interface IImpactAttackerFeedback
    {
        /// <summary>The attacker's hit pushed a target over its stagger threshold.</summary>
        void OnTargetStaggered(string targetId);

        /// <summary>The attacker's knockback drove a target into a wall; the return value may add damage/stagger. Null = nothing extra.</summary>
        WallImpactOutcome OnTargetKnockedIntoWall(string targetId, bool isBoss);

        /// <summary>
        /// A direct (non-explosive) projectile of the attacker is about to damage a target after travelling
        /// <paramref name="travelDistance"/> tiles; returns the damage to apply (wearer passives may raise it).
        /// </summary>
        int OnProjectileHitRolling(int damage, float travelDistance) => damage;

        /// <summary>The attacker's hit (projectile, blast or melee) took the target's last health.</summary>
        void OnTargetKilled(bool melee) { }
    }

    /// <summary>Plain result of the wall-impact hook (mirrors Stats.WallImpactRequest without a Stats dependency).</summary>
    public readonly struct WallImpactOutcome
    {
        public WallImpactOutcome(int bonusDamageMin, int bonusDamageMax, bool applyHighStagger)
        {
            BonusDamageMin = bonusDamageMin;
            BonusDamageMax = bonusDamageMax;
            ApplyHighStagger = applyHighStagger;
        }

        public int BonusDamageMin { get; }
        public int BonusDamageMax { get; }
        public bool ApplyHighStagger { get; }
        public bool HasBonus => BonusDamageMax > 0 || ApplyHighStagger;
    }

    /// <summary>Stagger pressure and knockback carried by one hit, resolved separately from damage (42_STAGGER_KNOCKBACK).</summary>
    public readonly struct ImpactRequest
    {
        public ImpactRequest(Vector2 direction, float knockback, float staggerPower, DamageKind kind = DamageKind.Normal, GameObject source = null, IImpactAttackerFeedback feedback = null)
        {
            Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
            Knockback = Mathf.Max(0f, knockback);
            StaggerPower = Mathf.Max(0f, staggerPower);
            Kind = kind;
            Source = source;
            Feedback = feedback;
        }

        public Vector2 Direction { get; }
        public float Knockback { get; }
        public float StaggerPower { get; }
        public DamageKind Kind { get; }
        public GameObject Source { get; }
        public IImpactAttackerFeedback Feedback { get; }
        public bool IsExplosion => Kind == DamageKind.Explosion;
        public bool HasKnockback => Knockback > 0f && Direction != Vector2.zero;
        public bool HasStagger => StaggerPower > 0f;
    }

    public readonly struct StaggerResult
    {
        public StaggerResult(float applied, bool triggered, bool negated)
        {
            Applied = applied;
            Triggered = triggered;
            Negated = negated;
        }

        /// <summary>Pressure added after resistance (0 when negated/immune).</summary>
        public float Applied { get; }
        public bool Triggered { get; }
        public bool Negated { get; }
        public static StaggerResult None => new(0f, false, false);
    }

    public readonly struct KnockbackResult
    {
        public KnockbackResult(float distance, bool negated)
        {
            Distance = distance;
            Negated = negated;
        }

        /// <summary>World-unit displacement that will be travelled (0 = none).</summary>
        public float Distance { get; }
        public bool Negated { get; }
        public bool Moved => Distance > 0f;
        public static KnockbackResult None => new(0f, false);
    }

    /// <summary>Separate receiver boundaries; HealthComponent stays health-only.</summary>
    public interface IStaggerReceiver
    {
        StaggerResult ApplyStagger(ImpactRequest request);
    }

    public interface IKnockbackReceiver
    {
        KnockbackResult ApplyKnockback(ImpactRequest request);
    }

    /// <summary>Delivers one hit's impact to whatever receivers the struck object exposes (any subset, none is fine).</summary>
    public static class ImpactDispatcher
    {
        /// <summary>
        /// 82 on a client process: knockback and stagger of a hit are the host's to apply, so the impact of a hit the
        /// local player landed is forwarded here (together with the damage request) instead of moving a replica.
        /// Returns true when forwarded. Null outside a co-op client run.
        /// </summary>
        public static System.Func<Component, ImpactRequest, bool> RemoteImpactRelay { get; set; }

        public static void Apply(Component struck, ImpactRequest request)
        {
            if (struck == null) return;
            if (!DamageAuthority.LocalIsAuthoritative)
            {
                RemoteImpactRelay?.Invoke(struck, request);
                return;
            }

            if (request.HasStagger) struck.GetComponentInParent<IStaggerReceiver>()?.ApplyStagger(request);
            if (request.HasKnockback) struck.GetComponentInParent<IKnockbackReceiver>()?.ApplyKnockback(request);
        }
    }
}
