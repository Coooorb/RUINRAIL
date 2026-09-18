using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>What one shot resolves to before the emitter fires it.</summary>
    public readonly struct ShotSolution
    {
        public ShotSolution(Vector2 spawnPosition, Vector2 direction, bool spawnPulledBack, IDamageable assistedTarget, Vector2 assistedAimPoint)
        {
            SpawnPosition = spawnPosition;
            Direction = direction;
            SpawnPulledBack = spawnPulledBack;
            AssistedTarget = assistedTarget;
            AssistedAimPoint = assistedAimPoint;
        }

        public Vector2 SpawnPosition { get; }
        public Vector2 Direction { get; }
        /// <summary>The muzzle would have been past a target or a wall, so the shot leaves from the hand instead.</summary>
        public bool SpawnPulledBack { get; }
        public IDamageable AssistedTarget { get; }
        public Vector2 AssistedAimPoint { get; }
        public bool Assisted => AssistedTarget != null;
    }

    /// <summary>
    /// The shared "where does this shot start and where does it go" step every player projectile weapon runs:
    ///
    ///  1. <b>Direct aim is correct without help.</b> The raw direction already comes from the weapon pivot toward the
    ///     crosshair (<see cref="PlayerAiming"/>), so the bullet line passes through the crosshair.
    ///  2. <b>Close range.</b> The muzzle sits ahead of the hand; if a hostile hurtbox or a wall already lies between
    ///     the hand and the muzzle, the projectile is spawned at the hand so it cannot start *behind* the thing the
    ///     player is shooting at.
    ///  3. <b>Soft aim assist.</b> Among alive hostiles inside weapon range, in front of the raw aim, inside the
    ///     class-scaled cone and in clear line of sight from the spawn point, the best candidate by crosshair
    ///     proximity, then angle, then distance gets the shot pointed at its hurtbox centre. A crosshair already inside
    ///     a hurtbox makes that target win outright. No candidate → the raw direction is used unchanged.
    ///
    /// Spread weapons receive the assisted centre and apply their own pellet spread around it as before.
    /// </summary>
    public static class ShotSolver
    {
        private const float ProbeRadius = 0.08f;
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[24];
        private static readonly Collider2D[] Overlaps = new Collider2D[96];

        public sealed class Candidate
        {
            public IDamageable Target;
            public Vector2 AimPoint;
            public float AngleDegrees;
            public float Distance;
            public float CrosshairPixels;
            public bool CrosshairInside;
            public float Score;
        }

        public static ShotSolution Solve(Vector2 origin, Vector2 muzzle, Vector2 rawDirection, float range, WeaponClass weaponClass,
            GameObject shooter, DamageTeam shooterTeam, AimAssistConfig assist, PlayerAiming aiming, List<Candidate> diagnostics = null)
        {
            var direction = rawDirection.sqrMagnitude > 0.0001f ? rawDirection.normalized : Vector2.right;
            var spawn = muzzle;
            var pulledBack = false;
            if (BlockedBetween(origin, muzzle, shooter, shooterTeam))
            {
                spawn = origin;
                pulledBack = true;
            }

            if (assist == null) return new ShotSolution(spawn, direction, pulledBack, null, Vector2.zero);

            var pointer = aiming != null && aiming.HasPointerAim;
            var halfAngle = assist.HalfAngleFor(weaponClass, pointer);
            if (halfAngle <= 0f) return new ShotSolution(spawn, direction, pulledBack, null, Vector2.zero);

            var crosshair = pointer ? aiming.AimWorldPoint : spawn + direction * range;
            var best = SelectTarget(spawn, direction, range, halfAngle, crosshair, pointer, shooter, shooterTeam, assist, diagnostics);
            if (best == null) return new ShotSolution(spawn, direction, pulledBack, null, Vector2.zero);

            var assisted = best.AimPoint - spawn;
            if (assisted.sqrMagnitude < 0.0001f) return new ShotSolution(spawn, direction, pulledBack, null, Vector2.zero);
            return new ShotSolution(spawn, assisted.normalized, pulledBack, best.Target, best.AimPoint);
        }

        /// <summary>A hostile hurtbox/body or a wall on the segment hand→muzzle (the muzzle would be past it).</summary>
        public static bool BlockedBetween(Vector2 origin, Vector2 muzzle, GameObject shooter, DamageTeam shooterTeam)
        {
            var delta = muzzle - origin;
            var length = delta.magnitude;
            if (length < 0.001f) return false;
            var count = Physics2D.CircleCast(origin, ProbeRadius, delta / length, Physics2DQueries.LegacyQueryFilter(), Hits, length);
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider == null || IsShooter(collider, shooter)) continue;
                if (collider.GetComponentInParent<EnvironmentObstacle>() != null) return true;
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable != null && !TeamMember.IsTagged(collider, shooterTeam)) return true;
            }

            return false;
        }

        /// <summary>The best assisted target, or null. Pure over the physics scene; exposed for the tests.</summary>
        public static Candidate SelectTarget(Vector2 spawn, Vector2 direction, float range, float halfAngleDegrees, Vector2 crosshair, bool pointerAim,
            GameObject shooter, DamageTeam shooterTeam, AimAssistConfig assist, List<Candidate> diagnostics = null)
        {
            diagnostics?.Clear();
            var count = Physics2D.OverlapCircle(spawn, range, Physics2DQueries.LegacyQueryFilter(), Overlaps);
            var seen = new HashSet<IDamageable>();
            Candidate best = null;
            for (var i = 0; i < count; i++)
            {
                var collider = Overlaps[i];
                if (collider == null || IsShooter(collider, shooter)) continue;
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable == null || TeamMember.IsTagged(collider, shooterTeam)) continue;
                var health = collider.GetComponentInParent<HealthComponent>();
                if (health != null && !health.IsAlive) continue;
                if (!seen.Add(damageable)) continue;

                var hurtbox = CombatHurtbox.Of(collider);
                var aimPoint = hurtbox != null ? hurtbox.AimPoint : (Vector2)collider.bounds.center;
                var to = aimPoint - spawn;
                var distance = to.magnitude;
                if (distance < 0.001f || distance > range) continue;
                var dirTo = to / distance;
                if (Vector2.Dot(direction, dirTo) <= 0f) continue; // behind the aim, however close
                var angle = Vector2.Angle(direction, dirTo);
                var crosshairInside = pointerAim && (hurtbox != null ? hurtbox.Contains(crosshair) : collider.OverlapPoint(crosshair));
                if (angle > halfAngleDegrees && !crosshairInside) continue;
                if (!HasLineOfSight(spawn, aimPoint, damageable, shooter, shooterTeam)) continue;

                var candidate = new Candidate
                {
                    Target = damageable,
                    AimPoint = aimPoint,
                    AngleDegrees = angle,
                    Distance = distance,
                    CrosshairPixels = pointerAim ? Vector2.Distance(crosshair, aimPoint) * assist.PixelsPerUnit : float.PositiveInfinity,
                    CrosshairInside = crosshairInside
                };
                candidate.Score = ScoreOf(candidate, halfAngleDegrees, range, assist, pointerAim);
                diagnostics?.Add(candidate);
                if (best == null || candidate.Score < best.Score) best = candidate;
            }

            return best;
        }

        /// <summary>Lower is better: crosshair proximity first, angle second, distance as the weak tie-breaker; a direct hover wins outright.</summary>
        public static float ScoreOf(Candidate c, float halfAngleDegrees, float range, AimAssistConfig assist, bool pointerAim)
        {
            if (c.CrosshairInside) return -1000f + c.CrosshairPixels * 0.001f;
            var proximity = pointerAim ? Mathf.Min(4f, c.CrosshairPixels / Mathf.Max(1f, assist.CrosshairProximityPixels)) : 0f;
            var angle = halfAngleDegrees > 0f ? c.AngleDegrees / halfAngleDegrees : 0f;
            var distance = range > 0f ? c.Distance / range : 0f;
            return proximity * 3f + angle * 2f + distance * 0.5f;
        }

        private static bool HasLineOfSight(Vector2 from, Vector2 to, IDamageable target, GameObject shooter, DamageTeam shooterTeam)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length < 0.001f) return true;
            var count = Physics2D.Raycast(from, delta / length, Physics2DQueries.LegacyQueryFilter(), Hits, length);
            var nearestBlock = float.PositiveInfinity;
            var nearestTarget = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var collider = Hits[i].collider;
                if (collider == null || IsShooter(collider, shooter)) continue;
                if (collider.GetComponentInParent<EnvironmentObstacle>() != null) { nearestBlock = Mathf.Min(nearestBlock, Hits[i].distance); continue; }
                var damageable = collider.GetComponentInParent<IDamageable>();
                if (damageable == null || TeamMember.IsTagged(collider, shooterTeam)) continue;
                if (ReferenceEquals(damageable, target)) nearestTarget = Mathf.Min(nearestTarget, Hits[i].distance);
                else nearestBlock = Mathf.Min(nearestBlock, Hits[i].distance); // another hostile in the way takes the shot instead
            }

            return nearestBlock >= nearestTarget || float.IsPositiveInfinity(nearestBlock);
        }

        private static bool IsShooter(Collider2D collider, GameObject shooter) =>
            shooter != null && (collider.transform == shooter.transform || collider.transform.IsChildOf(shooter.transform));
    }
}
