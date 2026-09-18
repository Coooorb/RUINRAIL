using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    public sealed class EnemyMeleeContactAttack : MonoBehaviour, IEnemyAttackBehaviour
    {
        private EnemyDefinition _definition;
        private IDamageRoller _damageRoller;

        public int LastDamageDealt { get; private set; }

        public void Configure(EnemyDefinition definition, IDamageRoller damageRoller)
        {
            _definition = definition;
            _damageRoller = damageRoller;
        }

        public bool IsTargetInAttackRange(Transform target)
        {
            if (target == null || _definition == null)
            {
                return false;
            }

            var toTarget = (Vector2)target.position - (Vector2)transform.position;
            return toTarget.magnitude <= _definition.AttackRange;
        }

        public bool TryResolveAttack(Transform target)
        {
            if (!IsTargetInAttackRange(target))
            {
                return false;
            }

            var damageable = target.GetComponentInParent<IDamageable>();
            if (damageable == null)
            {
                return false;
            }

            LastDamageDealt = _damageRoller.Roll(_definition.DamageMin, _definition.DamageMax);
            return damageable.TryApplyDamage(new DamageRequest(LastDamageDealt));
        }
    }
}
