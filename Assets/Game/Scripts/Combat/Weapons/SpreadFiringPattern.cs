using System;
using System.Collections.Generic;
using RuinRail.Core.Rng;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Multi-pellet cone: each pellet gets an independent angular offset in [-spread/2, +spread/2] around the aim
    /// direction, drawn from the injected seeded random source so a shot is reproducible.
    /// </summary>
    public sealed class SpreadFiringPattern : IFiringPattern
    {
        private readonly int _pelletCount;
        private readonly float _spreadDegrees;
        private readonly IRandomSource _random;

        public SpreadFiringPattern(int pelletCount, float spreadDegrees, IRandomSource random)
        {
            _pelletCount = Mathf.Max(1, pelletCount);
            _spreadDegrees = Mathf.Max(0f, spreadDegrees);
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int ProjectilesPerShot => _pelletCount;

        public float SpreadDegrees => _spreadDegrees;

        public void ResolveDirections(Vector2 aimDirection, List<Vector2> directions)
        {
            directions.Clear();
            var aim = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector2.right;
            var half = _spreadDegrees * 0.5f;

            for (var i = 0; i < _pelletCount; i++)
            {
                var offset = half <= 0f ? 0f : Mathf.Lerp(-half, half, _random.NextFloat());
                directions.Add(Quaternion.Euler(0f, 0f, offset) * aim);
            }
        }
    }
}
