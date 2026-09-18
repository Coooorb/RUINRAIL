using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Shield Enemy (44): incoming frontal projectile damage is reduced by a fixed percentage; side, rear, melee, area
    /// and explosion damage pass in full. No shield-HP subsystem — this is a pure orientation-aware damage modifier on
    /// the shared HealthComponent pipeline. Facing follows the enemy's target (the controller keeps it updated).
    /// </summary>
    public sealed class FrontalShield : MonoBehaviour, IIncomingDamageModifier
    {
        private int _reductionPercent;
        private float _arcDegrees = 120f;
        private Vector2 _facing = Vector2.right;

        public int ReductionPercent => _reductionPercent;
        public float ArcDegrees => _arcDegrees;
        public Vector2 Facing => _facing;
        public int BlockedHits { get; private set; }
        public int PassedHits { get; private set; }

        public void Configure(int reductionPercent, float arcDegrees)
        {
            _reductionPercent = Mathf.Clamp(reductionPercent, 0, 100);
            _arcDegrees = Mathf.Clamp(arcDegrees, 0f, 360f);
            var health = GetComponent<HealthComponent>();
            if (health != null) health.SetIncomingDamageModifier(this);
        }

        public void SetFacing(Vector2 facing)
        {
            if (facing.sqrMagnitude > 0.0001f) _facing = facing.normalized;
        }

        /// <summary>A projectile travelling opposite to the facing (into the face) is frontal; within +/- ArcDegrees/2.</summary>
        public bool IsFrontal(Vector2 hitDirection)
        {
            if (hitDirection == Vector2.zero) return false;
            var fromHitTowardAttacker = -hitDirection;
            return Vector2.Angle(_facing, fromHitTowardAttacker) <= _arcDegrees * 0.5f;
        }

        public int ModifyIncomingDamage(DamageRequest request)
        {
            if (request.Kind != DamageKind.Normal || !request.IsProjectileHit || !IsFrontal(request.HitDirection))
            {
                PassedHits++;
                return request.Amount;
            }

            BlockedHits++;
            return Mathf.Max(0, Mathf.RoundToInt(request.Amount * (100 - _reductionPercent) / 100f));
        }
    }
}
