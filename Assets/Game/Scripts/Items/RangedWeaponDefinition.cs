using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    [CreateAssetMenu(fileName = "RangedWeaponDefinition", menuName = "RuinRail/Items/Ranged Weapon Definition")]
    public sealed class RangedWeaponDefinition : WeaponDefinition
    {
        [SerializeField] private int _damageMin;
        [SerializeField] private int _damageMax;
        [SerializeField] private float _fireRate;
        [SerializeField] private int _magazineSize;
        [SerializeField] private float _reloadTime;
        [SerializeField] private float _range;
        [SerializeField] private float _projectileSpeed;
        [SerializeField] private AmmoType _ammoType;
        [SerializeField] private int _ammoCostPerShot = 1;
        [SerializeField, Min(1)] private int _projectilesPerShot = 1;
        [SerializeField, Min(0f)] private float _spreadDegrees;
        [SerializeField, Min(0f)] private float _explosionRadiusTiles;

        public int DamageMin => _damageMin;
        public int DamageMax => _damageMax;
        public float FireRate => _fireRate;
        public int MagazineSize => _magazineSize;
        public float ReloadTime => _reloadTime;
        public float Range => _range;
        public float ProjectileSpeed => _projectileSpeed;
        public AmmoType AmmoType => _ammoType;
        public int AmmoCostPerShot => Mathf.Max(1, _ammoCostPerShot);

        /// <summary>Pellets per shot (1 for normal guns). Ammo/magazine cost is per shot, never per pellet.</summary>
        public int ProjectilesPerShot => Mathf.Max(1, _projectilesPerShot);

        /// <summary>Total cone angle in degrees the pellets are distributed across (0 = perfectly straight).</summary>
        public float SpreadDegrees => Mathf.Max(0f, _spreadDegrees);

        /// <summary>Rockets: the projectile detonates on impact/expiry in this radius (tiles); 0 = ordinary direct-hit projectile.</summary>
        public float ExplosionRadiusTiles => Mathf.Max(0f, _explosionRadiusTiles);
        public bool IsExplosive => _explosionRadiusTiles > 0f;
    }
}
