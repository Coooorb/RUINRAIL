using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// The smallest collision-aware steering the chase loops need (no navigation graph in V1): before an actor
    /// commits a velocity toward its target it probes ahead with its own body radius. A solid obstacle inside the
    /// look-ahead deflects the heading along the wall's tangent — toward the side that is closer to the goal — and the
    /// chosen side is held for a moment so a pursuer slides along a wall and around a corner instead of jittering
    /// against it. A wall that is head-on to the goal is not slid along: the actor eases up to it and holds there,
    /// facing its target. When every direction is blocked the actor stops rather than pushing into geometry.
    ///
    /// Solid = anything carrying <see cref="EnvironmentObstacle"/>: room walls, obstacles, sealed sockets and the
    /// combat door blockers. The physics body still does the final word on contact; this only chooses a heading the
    /// body can follow.
    /// </summary>
    public sealed class ObstacleSteering
    {
        public const float LookAheadTiles = 0.75f;
        public const float SideHoldSeconds = 0.6f;
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[12];

        private readonly Transform _self;
        private float _radius;
        private int _heldSide;
        private float _holdRemaining;

        public ObstacleSteering(Transform self, float radius)
        {
            _self = self;
            _radius = Mathf.Max(0.05f, radius);
        }

        public int Deflections { get; private set; }
        public int Stops { get; private set; }
        public bool LastWasBlocked { get; private set; }

        public void SetRadius(float radius) => _radius = Mathf.Max(0.05f, radius);

        /// <summary>Heading the body can follow toward <paramref name="desired"/> (unit vector), or zero when boxed in.</summary>
        public Vector2 Steer(Vector2 position, Vector2 desired, float deltaTime)
        {
            if (desired.sqrMagnitude < 0.0001f) return Vector2.zero;
            desired.Normalize();
            _holdRemaining -= deltaTime;

            if (!Blocked(position, desired, out var normal, out var distance))
            {
                if (_holdRemaining <= 0f) _heldSide = 0;
                LastWasBlocked = false;
                return desired;
            }

            LastWasBlocked = true;
            var tangent = new Vector2(-normal.y, normal.x);
            var along = Vector2.Dot(tangent, desired);

            // The goal is straight through the wall (no useful progress along it): press up to the wall and hold there,
            // easing in so the body rests against it instead of drifting sideways or pushing into it. This is what
            // "blocked by a wall" looks like — the enemy stands at the wall facing its target.
            if (Mathf.Abs(along) < HeadOnAlong)
            {
                if (_holdRemaining <= 0f) _heldSide = 0;
                var ease = Mathf.Clamp01(distance / LookAheadTiles);
                if (ease < 0.05f) Stops++;
                return desired * ease;
            }

            // Otherwise slide along the wall: prefer the side already committed to (so a corner is rounded, not
            // oscillated), else the side nearer the goal.
            var side = _heldSide != 0 && _holdRemaining > 0f ? _heldSide : (along >= 0f ? 1 : -1);
            foreach (var candidateSide in new[] { side, -side })
            {
                var heading = tangent * candidateSide;
                if (Blocked(position, heading, out _, out _)) continue;
                if (_heldSide != candidateSide) { _heldSide = candidateSide; _holdRemaining = SideHoldSeconds; Deflections++; }
                return heading;
            }

            Stops++;
            return Vector2.zero;
        }

        /// <summary>Below this share of the desired heading along the wall, the wall is head-on: press, don't slide.</summary>
        public const float HeadOnAlong = 0.25f;

        /// <summary>A solid obstacle inside the look-ahead along <paramref name="direction"/>; the surface normal when so.</summary>
        public bool Blocked(Vector2 position, Vector2 direction, out Vector2 normal) => Blocked(position, direction, out normal, out _);

        /// <summary>Same, with the free distance to the nearest solid surface along the probe.</summary>
        public bool Blocked(Vector2 position, Vector2 direction, out Vector2 normal, out float distance)
        {
            normal = Vector2.zero;
            var count = Physics2D.CircleCast(position, _radius * 0.9f, direction, Physics2DQueries.LegacyQueryFilter(), Hits, LookAheadTiles);
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || hit.collider.isTrigger) continue;
                if (hit.collider.transform == _self || hit.collider.transform.IsChildOf(_self)) continue;
                if (hit.collider.GetComponentInParent<EnvironmentObstacle>() == null) continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    normal = hit.normal;
                }
            }

            distance = float.IsPositiveInfinity(nearest) ? LookAheadTiles : nearest;
            return !float.IsPositiveInfinity(nearest);
        }
    }
}
