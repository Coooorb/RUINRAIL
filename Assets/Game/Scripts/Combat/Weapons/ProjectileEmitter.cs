using System.Collections.Generic;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Shared "emit one shot" behaviour used by every projectile weapon (gun, shotgun, blaster):
    /// resolves the firing pattern, rolls integer damage per projectile and spawns pooled projectiles.
    /// Resource rules (magazine, ammo, heat) stay with the owning weapon.
    /// </summary>
    public sealed class ProjectileEmitter
    {
        private readonly List<Vector2> _directions = new();

        public void Emit(
            ProjectilePool pool,
            IFiringPattern pattern,
            IDamageRoller damageRoller,
            Vector2 aimDirection,
            Vector2 spawnPosition,
            int damageMin,
            int damageMax,
            float projectileSpeed,
            float range,
            GameObject source,
            List<Projectile> spawned,
            float damageMultiplier = 1f,
            float knockback = 0f,
            float staggerPower = 0f,
            IImpactAttackerFeedback feedback = null,
            float explosionRadius = 0f,
            DamageTeam sourceTeam = DamageTeam.Player)
        {
            pattern.ResolveDirections(aimDirection, _directions);
            spawned.Clear();

            foreach (var direction in _directions)
            {
                // Damage is rolled independently per projectile; each one is an ordinary pooled projectile.
                // Integer roll first, then the (already capped) weapon-damage multiplier, rounded back to an integer.
                var damage = Mathf.Max(0, Mathf.RoundToInt(damageRoller.Roll(damageMin, damageMax) * damageMultiplier));
                var data = new ProjectileSpawnData(damage, projectileSpeed, range, knockback, staggerPower, direction, source, feedback, explosionRadius, sourceTeam);
                spawned.Add(pool.Spawn(spawnPosition, data));
            }
        }
    }
}
