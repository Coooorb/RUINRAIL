using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Hold-to-draw charge weapon (Bow class). No ammo, no magazine, no reload. Charge improves damage, projectile
    /// speed and range between the quick-shot and full-draw values (24_WEAPON_CLASSES: exact curve tunable).
    /// </summary>
    [CreateAssetMenu(fileName = "BowWeaponDefinition", menuName = "RuinRail/Items/Bow Weapon Definition")]
    public sealed class BowWeaponDefinition : WeaponDefinition
    {
        [SerializeField] private int _quickDamageMin;
        [SerializeField] private int _quickDamageMax;
        [SerializeField] private int _fullDrawDamageMin;
        [SerializeField] private int _fullDrawDamageMax;
        [SerializeField] private float _fullChargeSeconds;
        [SerializeField] private float _quickProjectileSpeed;
        [SerializeField] private float _fullProjectileSpeed;
        [SerializeField] private float _quickRange;
        [SerializeField] private float _fullRange;

        public int QuickDamageMin => _quickDamageMin;
        public int QuickDamageMax => _quickDamageMax;
        public int FullDrawDamageMin => _fullDrawDamageMin;
        public int FullDrawDamageMax => _fullDrawDamageMax;
        public float FullChargeSeconds => Mathf.Max(0.0001f, _fullChargeSeconds);
        public float QuickProjectileSpeed => _quickProjectileSpeed;
        public float FullProjectileSpeed => _fullProjectileSpeed;
        public float QuickRange => _quickRange;
        public float FullRange => _fullRange;
    }
}
