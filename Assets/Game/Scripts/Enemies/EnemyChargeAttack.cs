using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Charger core attack (44): the direction is locked when the telegraph starts (the "direction telegraph"), then the
    /// shared AttackResolver runs the authored Dash attack in a straight line — damaging what it touches once, stopping
    /// early at walls — and the controller's recovery only begins once the charge has ended. Missing or hitting a wall
    /// therefore leaves the documented recovery window.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class EnemyChargeAttack : MonoBehaviour, IEnemyAttackBehaviour, IEnemyContinuousAttack, IEnemyTelegraphAware
    {
        private EnemyDefinition _definition;
        private IDamageRoller _damageRoller;
        private AttackResolver _resolver;
        private Vector2 _lockedDirection = Vector2.right;

        public int ChargesStarted { get; private set; }
        public Vector2 LockedDirection => _lockedDirection;
        public AttackResolver Resolver => _resolver;
        public EnemyAttackDefinition Charge => _definition != null ? _definition.ChargeAttack : null;
        public bool IsResolving => _resolver != null && _resolver.IsRunning;
        public bool LastChargeStoppedByWall => _resolver != null && _resolver.LastDashStoppedByWall;

        public void Configure(EnemyDefinition definition, IDamageRoller damageRoller)
        {
            _definition = definition;
            _damageRoller = damageRoller;
            _resolver = new AttackResolver(transform, GetComponent<Rigidbody2D>(), damageRoller ?? new UnityRandomDamageRoller());
        }

        public bool IsTargetInAttackRange(Transform target)
        {
            if (target == null || Charge == null) return false;
            var distance = ((Vector2)target.position - (Vector2)transform.position).magnitude;
            if (distance > Charge.MaxTriggerRange || distance < Charge.MinTriggerRange) return false;
            return !SmokeZone.IsLineOfSightBlocked(transform.position, target.position, ignoresSmoke: false);
        }

        public void OnTelegraphStarted(Transform target)
        {
            if (target == null) return;
            var toTarget = (Vector2)target.position - (Vector2)transform.position;
            _lockedDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
        }

        /// <summary>Launches the charge along the direction locked at telegraph start; the target's later movement is irrelevant.</summary>
        public bool TryResolveAttack(Transform target)
        {
            if (Charge == null || _resolver == null) return false;
            _resolver.Begin(Charge, _lockedDirection);
            ChargesStarted++;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (_resolver != null && _resolver.IsRunning) _resolver.Tick(deltaTime);
        }

        public void Cancel()
        {
            _resolver?.Cancel();
        }
    }
}
