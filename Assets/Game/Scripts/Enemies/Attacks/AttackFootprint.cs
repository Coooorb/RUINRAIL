using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Attacks
{
    /// <summary>The geometric form of a damaging footprint.</summary>
    public enum FootprintForm
    {
        /// <summary>A disc of <see cref="AttackFootprint.Shape.Radius"/> around <see cref="AttackFootprint.Shape.Centre"/>.</summary>
        Circle,

        /// <summary>A rectangle <see cref="AttackFootprint.Shape.Size"/> (x along the direction) centred on the centre.</summary>
        Box,

        /// <summary>The set of points within Radius of the segment Start→End (a dash body, a projectile path).</summary>
        Capsule
    }

    /// <summary>
    /// The one source of truth for where an <see cref="EnemyAttackDefinition"/> can hurt, given the attacker's position and
    /// the direction locked at telegraph start. <see cref="AttackResolver"/> runs its physics queries and fires its volleys
    /// from these shapes, and the danger telegraph draws exactly these shapes, so the warning and the hit can never drift
    /// apart. Pure geometry (plus the wall/edge clip a dash or shot really obeys); no state, no presentation.
    /// </summary>
    public static class AttackFootprint
    {
        /// <summary>Volley projectiles leave the attacker this far along their lane (clear of its own body).</summary>
        public const float VolleyMuzzleOffset = 0.6f;

        /// <summary>The collision radius of every enemy-fired projectile (the hostile pool builds them at this size).</summary>
        public static float EnemyProjectileRadius => ProjectilePool.DefaultColliderRadius;

        public readonly struct Shape
        {
            public Shape(FootprintForm form, Vector2 centre, Vector2 size, Vector2 direction, Vector2 start, Vector2 end, float radius)
            {
                Form = form;
                Centre = centre;
                Size = size;
                Direction = direction;
                Start = start;
                End = end;
                Radius = radius;
            }

            public FootprintForm Form { get; }
            public Vector2 Centre { get; }

            /// <summary>Extent in world tiles: x along <see cref="Direction"/>, y across (a circle: its diameter both ways).</summary>
            public Vector2 Size { get; }

            public Vector2 Direction { get; }
            public Vector2 Start { get; }
            public Vector2 End { get; }
            public float Radius { get; }
            public float AngleDegrees => Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;

            public static Shape Circle(Vector2 centre, float radius, Vector2 direction) =>
                new(FootprintForm.Circle, centre, Vector2.one * (radius * 2f), direction, centre, centre, radius);

            public static Shape Box(Vector2 centre, Vector2 size, Vector2 direction) =>
                new(FootprintForm.Box, centre, size, direction, centre - direction * (size.x * 0.5f), centre + direction * (size.x * 0.5f), Mathf.Min(size.x, size.y) * 0.5f);

            public static Shape Capsule(Vector2 start, Vector2 end, float radius, Vector2 direction)
            {
                var length = Vector2.Distance(start, end);
                return new(FootprintForm.Capsule, (start + end) * 0.5f, new Vector2(length + radius * 2f, radius * 2f), direction, start, end, radius);
            }

            /// <summary>Signed distance from <paramref name="point"/> to the footprint's boundary (negative inside).</summary>
            public float SignedDistance(Vector2 point)
            {
                switch (Form)
                {
                    case FootprintForm.Circle:
                        return Vector2.Distance(point, Centre) - Radius;
                    case FootprintForm.Box:
                    {
                        var d = point - Centre;
                        var along = Mathf.Abs(Vector2.Dot(d, Direction)) - Size.x * 0.5f;
                        var across = Mathf.Abs(Direction.x * d.y - Direction.y * d.x) - Size.y * 0.5f;
                        var outside = new Vector2(Mathf.Max(along, 0f), Mathf.Max(across, 0f)).magnitude;
                        return outside + Mathf.Min(Mathf.Max(along, across), 0f);
                    }
                    default:
                    {
                        var segment = End - Start;
                        var lengthSq = segment.sqrMagnitude;
                        var t = lengthSq < 0.000001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - Start, segment) / lengthSq);
                        return Vector2.Distance(point, Start + segment * t) - Radius;
                    }
                }
            }

            public bool Contains(Vector2 point, float margin = 0f) => SignedDistance(point) <= margin;
        }

        private static Vector2 Normalised(Vector2 direction) => direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;

        /// <summary>
        /// The area one hit window of a Stationary / Slam / Zone attack strikes: Stationary a HitRadius disc half a radius
        /// in front, Slam a HitRadius disc around the attacker, Zone a ZoneLength × ZoneWidth box ahead along the locked direction.
        /// </summary>
        public static Shape Strike(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction)
        {
            var dir = Normalised(direction);
            switch (attack.Motion)
            {
                case AttackMotion.Slam:
                    return Shape.Circle(origin, attack.HitRadius, dir);
                case AttackMotion.Zone:
                    return Shape.Box(origin + dir * (attack.ZoneLength * 0.5f), new Vector2(attack.ZoneLength, attack.ZoneWidth), dir);
                default:
                    return Shape.Circle(origin + dir * (attack.HitRadius * 0.5f), attack.HitRadius, dir);
            }
        }

        /// <summary>
        /// A dash: the HitRadius disc the resolver strikes around the body, swept along the run. The run is the authored
        /// DashDistance unless <paramref name="clip"/> finds the wall or encounter edge that ends it first.
        /// </summary>
        public static Shape Dash(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction, bool clip = false, EncounterBounds bounds = null, Transform self = null)
        {
            var dir = Normalised(direction);
            var run = attack.DashDistance;
            if (clip)
            {
                run = Mathf.Min(run, WallDistance(origin, dir, run, self));
                if (bounds != null && bounds.IsBound) run = Mathf.Min(run, bounds.FreeDistance(origin, dir, run));
            }

            return Shape.Capsule(origin, origin + dir * run, attack.HitRadius, dir);
        }

        public static int LaneCount(EnemyAttackDefinition attack) => attack.Motion == AttackMotion.Projectile ? attack.ProjectileCount : 0;

        /// <summary>The direction of volley lane <paramref name="index"/>: an even fan over SpreadDegrees around the locked direction.</summary>
        public static Vector2 LaneDirection(EnemyAttackDefinition attack, Vector2 direction, int index)
        {
            var dir = Normalised(direction);
            var count = attack.ProjectileCount;
            var spread = attack.SpreadDegrees;
            var offset = count == 1 || spread <= 0f ? 0f : Mathf.Lerp(-spread * 0.5f, spread * 0.5f, index / (float)(count - 1));
            return Quaternion.Euler(0f, 0f, offset) * dir;
        }

        /// <summary>
        /// Distinct volley lanes (a full ring repeats its first lane at ±180°, so that duplicate is skipped): the path
        /// each projectile sweeps, from the muzzle out to its range, ended early at the first wall it would stop on.
        /// </summary>
        public static void Lanes(EnemyAttackDefinition attack, Vector2 origin, Vector2 direction, List<Shape> lanes, bool clip = false, Transform self = null)
        {
            if (attack == null || attack.Motion != AttackMotion.Projectile) return;
            var count = attack.ProjectileCount;
            var distinct = count > 1 && attack.SpreadDegrees >= 359.5f ? count - 1 : count;
            for (var i = 0; i < distinct; i++)
                lanes.Add(ProjectileLane(origin, LaneDirection(attack, direction, i), VolleyMuzzleOffset, attack.ProjectileRange, clip, self));
        }

        /// <summary>
        /// One projectile's path: it is spawned at origin + direction × <paramref name="muzzle"/>, sweeps a circle of the
        /// hostile projectile radius for <paramref name="range"/> tiles, and stops at the first wall. The lane is drawn from
        /// the attacker, so it reads source-to-target.
        /// </summary>
        public static Shape ProjectileLane(Vector2 origin, Vector2 direction, float muzzle, float range, bool clip = false, Transform self = null)
        {
            var dir = Normalised(direction);
            var reach = muzzle + range;
            if (clip) reach = Mathf.Min(reach, WallDistance(origin, dir, reach, self));
            return Shape.Capsule(origin, origin + dir * Mathf.Max(muzzle, reach), EnemyProjectileRadius, dir);
        }

        // Walls are solid colliders: triggers (room volumes, hurtboxes, pickups, hazards) are left out of the query, and the
        // buffer is roomy, so a crowded room can never push the wall out of the result set and lengthen the lane.
        private static readonly RaycastHit2D[] WallHits = new RaycastHit2D[32];

        /// <summary>Distance from <paramref name="origin"/> to the first solid environment obstacle along the direction, capped.</summary>
        public static float WallDistance(Vector2 origin, Vector2 direction, float maxDistance, Transform self = null)
        {
            var nearest = maxDistance;
            var filter = Physics2DQueries.LegacyQueryFilter();
            filter.useTriggers = false;
            var count = Physics2D.Raycast(origin, direction, filter, WallHits, maxDistance);
            for (var i = 0; i < count; i++)
            {
                var hit = WallHits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (self != null && (hit.collider.transform == self || hit.collider.transform.IsChildOf(self))) continue;
                if (hit.collider.GetComponentInParent<EnvironmentObstacle>() == null) continue;
                nearest = Mathf.Min(nearest, hit.distance);
            }

            return nearest;
        }
    }
}
