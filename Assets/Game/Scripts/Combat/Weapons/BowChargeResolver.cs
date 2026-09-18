using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Pure charge → shot-parameter mapping for bows. Charge fraction is clamped to [0, 1]; the damage window is
    /// interpolated between the quick and full-draw integer ranges (linear, tunable curve) and stays integer.
    /// </summary>
    public static class BowChargeResolver
    {
        public readonly struct ShotParameters
        {
            public ShotParameters(int damageMin, int damageMax, float projectileSpeed, float range)
            {
                DamageMin = damageMin;
                DamageMax = damageMax;
                ProjectileSpeed = projectileSpeed;
                Range = range;
            }

            public int DamageMin { get; }
            public int DamageMax { get; }
            public float ProjectileSpeed { get; }
            public float Range { get; }
        }

        public static float ChargeFraction(float heldSeconds, float fullChargeSeconds)
        {
            return Mathf.Clamp01(heldSeconds / Mathf.Max(0.0001f, fullChargeSeconds));
        }

        public static ShotParameters Resolve(BowWeaponDefinition definition, float chargeFraction)
        {
            var t = Mathf.Clamp01(chargeFraction);
            var min = Mathf.RoundToInt(Mathf.Lerp(definition.QuickDamageMin, definition.FullDrawDamageMin, t));
            var max = Mathf.RoundToInt(Mathf.Lerp(definition.QuickDamageMax, definition.FullDrawDamageMax, t));
            return new ShotParameters(
                min,
                Mathf.Max(min, max),
                Mathf.Lerp(definition.QuickProjectileSpeed, definition.FullProjectileSpeed, t),
                Mathf.Lerp(definition.QuickRange, definition.FullRange, t));
        }
    }
}
