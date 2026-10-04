using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    /// <summary>
    /// Which damageable a collider found by a hit query is a damage surface of. A projectile in flight is never the body of
    /// whoever fired it (shots are pooled under their shooter, so a parent lookup alone made a slam that overlapped a
    /// player's bullet hurt the player across the room). An actor with an exclusive hurtbox (players) is hit only through
    /// that hurtbox, which lies inside its drawn body, never through its larger feet-level movement collider.
    /// </summary>
    public static class DamageTargets
    {
        public static IDamageable Resolve(Collider2D collider)
        {
            if (collider == null) return null;
            if (collider.GetComponent<Projectile>() != null) return null;
            var damageable = collider.GetComponentInParent<IDamageable>();
            if (damageable is Component owner)
            {
                var hurtbox = owner.GetComponent<CombatHurtbox>();
                if (hurtbox != null && hurtbox.Exclusive && !hurtbox.Owns(collider)) return null;
            }

            return damageable;
        }
    }
}
