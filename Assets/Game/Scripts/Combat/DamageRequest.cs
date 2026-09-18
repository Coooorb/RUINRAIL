using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Damage category tag (41: no elemental resistance matrix). Normal = direct hits; Explosion = Blast Suit / Shock
    /// Absorber rules apply; Burn = damage-over-time zones; Hazard = authored environmental damage (never subject to
    /// the player friendly-fire rule).
    /// </summary>
    public enum DamageKind
    {
        Normal,
        Explosion,
        Burn,
        Hazard
    }

    public readonly struct DamageRequest
    {
        public int Amount { get; }
        public DamageKind Kind { get; }

        /// <summary>Stagger pressure transported with the hit (consumed by the stagger foundation; 0 = none).</summary>
        public float StaggerPower { get; }

        /// <summary>Travel direction of a projectile hit (zero for melee, area and hazard damage). Lets orientation-aware receivers (frontal shields) tell front from flank.</summary>
        public Vector2 HitDirection { get; }
        public bool IsProjectileHit => HitDirection != Vector2.zero;

        public DamageRequest(int amount) : this(amount, DamageKind.Normal, 0f)
        {
        }

        public DamageRequest(int amount, DamageKind kind, float staggerPower = 0f) : this(amount, kind, staggerPower, Vector2.zero)
        {
        }

        public DamageRequest(int amount, DamageKind kind, float staggerPower, Vector2 hitDirection)
        {
            Amount = amount;
            Kind = kind;
            StaggerPower = staggerPower;
            HitDirection = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : Vector2.zero;
        }

        public bool IsExplosion => Kind == DamageKind.Explosion;
        public bool IsEnvironmental => Kind == DamageKind.Hazard;
    }
}
