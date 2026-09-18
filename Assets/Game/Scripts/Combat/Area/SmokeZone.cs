using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Area
{
    /// <summary>
    /// Smoke cloud: for its duration, normal enemies cannot maintain line-of-sight targeting through it. Elites and
    /// Bosses ignore the blindness (they query with ignoresSmoke = true). Pure geometry — nothing else about the actors changes.
    /// </summary>
    public sealed class SmokeZone : MonoBehaviour
    {
        private static readonly List<SmokeZone> Active = new();

        private float _radius;
        private float _remaining;

        public float Radius => _radius;
        public float RemainingSeconds => _remaining;
        public static IReadOnlyList<SmokeZone> ActiveZones => Active;

        public void Configure(float radius, float durationSeconds)
        {
            _radius = radius;
            _remaining = durationSeconds;
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        public void Advance(float deltaTime)
        {
            _remaining -= deltaTime;
            if (_remaining <= 0f) Destroy(gameObject);
        }

        /// <summary>True when the segment from → to passes through this cloud (or either end is inside it).</summary>
        public bool BlocksSegment(Vector2 from, Vector2 to)
        {
            var center = (Vector2)transform.position;
            var ab = to - from;
            var lengthSq = ab.sqrMagnitude;
            var t = lengthSq < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(center - from, ab) / lengthSq);
            var closest = from + ab * t;
            return (closest - center).sqrMagnitude <= _radius * _radius;
        }

        /// <summary>Line of sight for targeting: blocked only for observers that do not ignore smoke.</summary>
        public static bool IsLineOfSightBlocked(Vector2 from, Vector2 to, bool ignoresSmoke)
        {
            if (ignoresSmoke) return false;
            foreach (var zone in Active)
            {
                if (zone != null && zone.BlocksSegment(from, to)) return true;
            }

            return false;
        }
    }
}
