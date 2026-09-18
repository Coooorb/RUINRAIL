using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    public sealed class SingleProjectilePattern : IFiringPattern
    {
        public static readonly SingleProjectilePattern Instance = new();

        public int ProjectilesPerShot => 1;

        public void ResolveDirections(Vector2 aimDirection, List<Vector2> directions)
        {
            directions.Clear();
            directions.Add(aimDirection);
        }
    }
}
