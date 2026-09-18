using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    [CreateAssetMenu(fileName = "MeleeWeaponDefinition", menuName = "RuinRail/Items/Melee Weapon Definition")]
    public sealed class MeleeWeaponDefinition : WeaponDefinition
    {
        [SerializeField] private int _damageMin;
        [SerializeField] private int _damageMax;
        [SerializeField] private float _attackRate;
        [SerializeField] private float _attackRange;
        [SerializeField] private float _attackArcDegrees;
        [SerializeField] private float _windUpSeconds;
        [SerializeField] private float _recoverySeconds;

        public int DamageMin => _damageMin;
        public int DamageMax => _damageMax;
        public float AttackRate => _attackRate;
        public float AttackRange => _attackRange;
        public float AttackArcDegrees => _attackArcDegrees;
        public float WindUpSeconds => _windUpSeconds;
        public float RecoverySeconds => _recoverySeconds;
    }
}
