using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Hazards;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Where a new enemy body may appear: never overlapping solid level geometry (walls, props, sealed sockets, door
    /// blockers — every <see cref="EnvironmentObstacle"/>) and, for the placements that choose a spot themselves, never
    /// on a floor that hurts enemies. A body created inside a collider is not reliably pushed back out — a prop's merged
    /// concave outline can hold a centre that ended up inside it — so the check happens before the body exists.
    /// </summary>
    public static class SpawnClearance
    {
        private static readonly Collider2D[] Overlaps = new Collider2D[16];

        /// <summary>True when a body of <paramref name="radius"/> centred at <paramref name="world"/> touches no solid geometry.</summary>
        public static bool IsClearOfGeometry(Vector2 world, float radius = DefaultEnemySpawner.BodyRadius)
        {
            var count = Physics2D.OverlapCircle(world, radius, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < count; i++)
            {
                var c = Overlaps[i];
                if (c != null && !c.isTrigger && c.GetComponentInParent<EnvironmentObstacle>() != null) return false;
            }

            return true;
        }

        /// <summary>True when a hazard that damages enemies covers the spot.</summary>
        public static bool IsOnEnemyHazard(Vector2 world, float radius = DefaultEnemySpawner.BodyRadius)
        {
            var count = Physics2D.OverlapCircle(world, radius, ContactFilter2D.noFilter, Overlaps);
            for (var i = 0; i < count; i++)
            {
                var hazard = Overlaps[i] != null ? Overlaps[i].GetComponentInParent<HazardVolume>() : null;
                if (hazard != null && hazard.Definition != null && hazard.Definition.AffectsEnemies) return true;
            }

            return false;
        }

        private static readonly RaycastHit2D[] Sweep = new RaycastHit2D[16];

        /// <summary>
        /// True when a body can move in a straight line from <paramref name="from"/> to <paramref name="to"/> without
        /// touching solid geometry — so a spot picked beside a marker (or beside a summoner) is on the same open floor,
        /// never inside a pocket that props or walls close off. The sweep is a little slimmer than the body so a body
        /// already resting against a wall (a summoner hugging it) does not count as blocked from the start; it still
        /// cannot slip through a wall or a prop, which are a whole tile thick, and the destination is checked at full
        /// body size by <see cref="IsFreeSpot"/>.
        /// </summary>
        public static bool HasClearPath(Vector2 from, Vector2 to, float radius = DefaultEnemySpawner.BodyRadius * 0.8f)
        {
            var delta = to - from;
            var distance = delta.magnitude;
            if (distance < 0.0001f) return true;
            var count = Physics2D.CircleCast(from, radius, delta / distance, ContactFilter2D.noFilter, Sweep, distance);
            for (var i = 0; i < count; i++)
            {
                var c = Sweep[i].collider;
                if (c != null && !c.isTrigger && c.GetComponentInParent<EnvironmentObstacle>() != null) return false;
            }

            return true;
        }

        /// <summary>
        /// A spot a placement may pick on its own: clear of geometry, off enemy-damaging floors and — when the actor is
        /// bound to a room — with the whole body inside that room's encounter interior.
        /// </summary>
        public static bool IsFreeSpot(Vector2 world, Rect? legalCentres, float radius = DefaultEnemySpawner.BodyRadius) =>
            (legalCentres == null || legalCentres.Value.Contains(world)) && IsClearOfGeometry(world, radius) && !IsOnEnemyHazard(world, radius);
    }
}
