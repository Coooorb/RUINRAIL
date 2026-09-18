using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Composable firing pattern: turns one aim direction into the projectile directions a single shot emits.
    /// Every returned direction becomes a normal pooled projectile; the pattern never touches damage or hits.
    /// </summary>
    public interface IFiringPattern
    {
        int ProjectilesPerShot { get; }
        void ResolveDirections(Vector2 aimDirection, List<Vector2> directions);
    }
}
