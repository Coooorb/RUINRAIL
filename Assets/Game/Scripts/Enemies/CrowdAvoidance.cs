using System.Collections.Generic;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Local enemy-vs-enemy avoidance for the chase loops. Every pursuer heads for the same point (its target), and
    /// enemy bodies are solid to each other, so once the few places in attack range around a player were taken the
    /// rest drove straight into the backs in front of them and stood there pressing — a pile in the open, a queue in a
    /// doorway or corner (measured in live runs: pressing pins of 2-4 s, five to seven enemies at a time).
    ///
    /// This adjusts only the heading, between the route (<see cref="PursuitNavigator"/>) and the wall steering
    /// (<see cref="ObstacleSteering"/>): a gentle separation from bodies closer than a small gap, and — when another
    /// enemy's body is directly ahead — a sidestep around it (the side held for a moment so it does not flip), so a
    /// waiting enemy flows around the crowd into whatever opens. Boxed in by bodies on both sides, it stops pushing.
    /// The result is still a unit heading (or zero): speed, ranges, timing and states are untouched, enemy bodies stay
    /// solid, and walls and the room bounds still have the final word after it.
    /// </summary>
    public sealed class CrowdAvoidance
    {
        /// <summary>Bodies closer than this gap (tiles, surface to surface) push each other apart a little.</summary>
        public const float SeparationGap = 0.3f;
        /// <summary>How far ahead (surface to surface) a body counts as blocking the way.</summary>
        public const float BlockLookAhead = 0.35f;
        public const float SideHoldSeconds = 0.8f;
        /// <summary>How long a boxed-in actor backs out of the crowd before approaching again (from its chosen side).</summary>
        public const float BackOffSeconds = 0.4f;
        private const float SeparationWeight = 0.6f;
        private const float MaxNeighbourRadius = 1.2f;
        private static readonly Collider2D[] Overlaps = new Collider2D[24];
        private static readonly List<(Vector2 position, float radius)> Neighbours = new();

        private readonly Transform _self;
        private readonly int _defaultSide;
        private int _heldSide;
        private float _holdRemaining;
        private float _backOffRemaining;
        private Vector2 _backOff;

        public CrowdAvoidance(Transform self)
        {
            _self = self;
            _defaultSide = self != null && (self.GetInstanceID() & 1) == 0 ? 1 : -1;
        }

        public int Sidesteps { get; private set; }
        public int Holds { get; private set; }
        public int BackOffs { get; private set; }
        public bool LastWasBlocked { get; private set; }

        /// <summary>How long other enemies' bodies have blocked the way without a break (seconds).</summary>
        public float HeldSeconds { get; private set; }
        /// <summary>The bodies blocking the way on the last step (position, radius).</summary>
        public IReadOnlyList<(Vector2 position, float radius)> Blockers => _blockers;
        private readonly List<(Vector2 position, float radius)> _blockers = new();

        /// <summary>The holder asked for a way around (a route that avoids <see cref="Blockers"/>): start counting again.</summary>
        public void ClearHeld() => HeldSeconds = 0f;

        /// <summary>The heading adjusted for nearby enemy bodies; zero when boxed in by them.</summary>
        public Vector2 Adjust(Vector2 position, Vector2 heading, float radius, float deltaTime)
        {
            LastWasBlocked = false;
            if (heading.sqrMagnitude < 0.0001f) return heading;
            heading.Normalize();
            _holdRemaining -= deltaTime;
            _backOffRemaining -= deltaTime;
            CollectNeighbours(position, radius);
            // Backing out of a crowd it was boxed into: keep going for the moment, unless a body is in that way too.
            if (_backOffRemaining > 0f && !SideBlocked(position, _backOff, radius)) return _backOff;
            if (Neighbours.Count == 0)
            {
                if (_holdRemaining <= 0f) _heldSide = 0;
                return heading;
            }

            var separation = Vector2.zero;
            var blockerLateral = 0f;
            var blocked = false;
            var side = new Vector2(-heading.y, heading.x);
            _blockers.Clear();
            foreach (var (other, otherRadius) in Neighbours)
            {
                var offset = other - position;
                var distance = offset.magnitude;
                var reach = radius + otherRadius;
                var gap = distance - reach;
                if (gap < SeparationGap && distance > 0.0001f) separation -= offset / distance * (1f - Mathf.Max(0f, gap) / SeparationGap);
                var along = Vector2.Dot(offset, heading);
                var lateral = Vector2.Dot(offset, side);
                if (along > 0f && Mathf.Abs(lateral) < reach * 0.9f && along - reach < BlockLookAhead)
                {
                    blocked = true;
                    blockerLateral += lateral;
                    _blockers.Add((other, otherRadius));
                }
            }

            HeldSeconds = blocked ? HeldSeconds + deltaTime : 0f;
            if (!blocked)
            {
                if (_holdRemaining <= 0f) _heldSide = 0;
                var spread = heading + separation * SeparationWeight;
                if (spread.sqrMagnitude < 0.0001f) return heading;
                spread.Normalize();
                // Spreading never steers onto a floor that hurts enemies when plain pursuit would not have gone there.
                return HazardAhead(position, spread, radius) && !HazardAhead(position, heading, radius) ? heading : spread;
            }

            LastWasBlocked = true;
            // Around the body in front: the side already committed to, else away from where the blockers sit.
            var preferred = _heldSide != 0 && _holdRemaining > 0f ? _heldSide
                : Mathf.Abs(blockerLateral) > 0.05f ? (blockerLateral > 0f ? -1 : 1) : _defaultSide;
            foreach (var candidate in new[] { preferred, -preferred })
            {
                var tangent = side * candidate;
                if (SideBlocked(position, tangent, radius)) continue;
                if (_heldSide != candidate) { _heldSide = candidate; _holdRemaining = SideHoldSeconds; Sidesteps++; }
                var around = tangent + heading * 0.3f + separation * SeparationWeight;
                if (around.sqrMagnitude < 0.0001f) return tangent;
                around.Normalize();
                return HazardAhead(position, around, radius) ? tangent : around; // the tangent itself was checked above
            }

            // Bodies ahead and on both sides: never shove into them (that only jams the crowd). Back out of the pile for a
            // moment — diagonally toward the committed side, the other diagonal, or straight back (the only way out of a
            // one-wide passage, so a queue there unwinds from its tail) — then come around; with no room behind, wait.
            foreach (var escape in new[] { (-heading * 0.6f + side * preferred).normalized, (-heading * 0.6f - side * preferred).normalized, -heading })
            {
                if (SideBlocked(position, escape, radius)) continue;
                _backOff = escape;
                _backOffRemaining = BackOffSeconds;
                _heldSide = preferred;
                _holdRemaining = SideHoldSeconds + BackOffSeconds;
                BackOffs++;
                return escape;
            }

            Holds++;
            return Vector2.zero;
        }

        private void CollectNeighbours(Vector2 position, float radius)
        {
            Neighbours.Clear();
            var count = Physics2D.OverlapCircle(position, radius + SeparationGap + BlockLookAhead + MaxNeighbourRadius, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < count; i++)
            {
                var c = Overlaps[i];
                if (c == null || c.isTrigger || !c.enabled || IsSelf(c) || !(c is CircleCollider2D circle)) continue;
                var actor = (Component)c.GetComponentInParent<EnemyController>() ?? c.GetComponentInParent<MovesetActorController>();
                if (actor == null || c.transform != actor.transform) continue; // only the body circle on the actor itself
                Neighbours.Add(((Vector2)c.transform.position + circle.offset, circle.radius));
            }
        }

        private static readonly RaycastHit2D[] WallHits = new RaycastHit2D[12];

        private static readonly Collider2D[] HazardHits = new Collider2D[8];

        /// <summary>A hazard that hurts enemies a step ahead of the body's edge in that direction.</summary>
        private static bool HazardAhead(Vector2 position, Vector2 direction, float radius)
        {
            var count = Physics2D.OverlapCircle(position + direction * (radius + BlockLookAhead), radius * 0.5f, ContactFilter2D.noFilter, HazardHits);
            for (var i = 0; i < count; i++)
            {
                var hazard = HazardHits[i] != null ? HazardHits[i].GetComponentInParent<RuinRail.Gameplay.Combat.Hazards.HazardVolume>() : null;
                if (hazard != null && hazard.Definition != null && hazard.Definition.AffectsEnemies) return true;
            }

            return false;
        }

        /// <summary>
        /// Another enemy body, solid geometry, or a hazard that hurts enemies within a step that way. A sidestep into a
        /// wall would only have the wall steering ease the body to a standstill against it (seen in corners); a crowd
        /// manoeuvre onto an enemy-damaging floor would cost enemies they never walked into before (seen in live runs).
        /// Plain pursuit is unchanged: this only rules sides out for the avoidance's own sidesteps and back-outs.
        /// </summary>
        private bool SideBlocked(Vector2 position, Vector2 direction, float radius)
        {
            var count = Physics2D.CircleCast(position, radius * 0.9f, direction, Physics2DQueries.LegacyQueryFilter(), WallHits, BlockLookAhead + 0.2f);
            for (var i = 0; i < count; i++)
            {
                var c = WallHits[i].collider;
                if (c == null || c.isTrigger || IsSelf(c)) continue;
                if (c.GetComponentInParent<RuinRail.Gameplay.Combat.EnvironmentObstacle>() != null) return true;
            }

            if (HazardAhead(position, direction, radius)) return true;

            foreach (var (other, otherRadius) in Neighbours)
            {
                var offset = other - position;
                var along = Vector2.Dot(offset, direction);
                if (along <= 0f) continue;
                var lateral = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
                if (lateral < (radius + otherRadius) * 0.9f && along - (radius + otherRadius) < BlockLookAhead) return true;
            }

            return false;
        }

        /// <summary>True when <paramref name="c"/> is this actor's own body.</summary>
        private bool IsSelf(Collider2D c) => _self != null && (c.transform == _self || c.transform.IsChildOf(_self));
    }
}
