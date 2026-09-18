using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Heat-based energy weapon (Blaster class). No ammo type, no magazine, no reload — heat replaces all of them.
    /// Class-wide constants (Max Heat 100, Cooling Delay 0.6s, Overheat Lockout 2.2s) are serialized so they stay data-driven.
    /// </summary>
    [CreateAssetMenu(fileName = "BlasterWeaponDefinition", menuName = "RuinRail/Items/Blaster Weapon Definition")]
    public sealed class BlasterWeaponDefinition : WeaponDefinition
    {
        [SerializeField] private int _damageMin;
        [SerializeField] private int _damageMax;
        [SerializeField] private float _fireRate;
        [SerializeField] private float _range;
        [SerializeField] private float _projectileSpeed;
        [SerializeField] private float _heatPerShot;
        [SerializeField] private float _coolingRatePerSecond;
        [SerializeField] private float _maxHeat = 100f;
        [SerializeField] private float _coolingDelaySeconds = 0.6f;
        [SerializeField] private float _overheatLockoutSeconds = 2.2f;

        public int DamageMin => _damageMin;
        public int DamageMax => _damageMax;
        public float FireRate => _fireRate;
        public float Range => _range;
        public float ProjectileSpeed => _projectileSpeed;
        public float HeatPerShot => _heatPerShot;
        public float CoolingRatePerSecond => _coolingRatePerSecond;
        public float MaxHeat => _maxHeat;
        public float CoolingDelaySeconds => _coolingDelaySeconds;
        public float OverheatLockoutSeconds => _overheatLockoutSeconds;
    }
}
