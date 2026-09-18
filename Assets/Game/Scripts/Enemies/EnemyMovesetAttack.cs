using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// A normal enemy with a small fixed moveset (Brute: Heavy Swing + Ground Slam). Attack selection is the same
    /// deterministic rule as the Elite/Boss actors (first ready entry whose trigger band contains the target), the
    /// chosen attack's own telegraph/recovery drive the controller's phases, its direction is locked when the telegraph
    /// starts and the shared AttackResolver executes it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class EnemyMovesetAttack : MonoBehaviour, IEnemyAttackBehaviour, IEnemyContinuousAttack, IEnemyTelegraphAware, IEnemyAttackTiming
    {
        private EnemyDefinition _definition;
        private AttackResolver _resolver;
        private readonly Dictionary<EnemyAttackDefinition, float> _cooldowns = new();
        private readonly List<EnemyAttackDefinition> _cooldownScratch = new();
        private EnemyAttackDefinition _pending;
        private Vector2 _lockedDirection = Vector2.right;

        /// <summary>Direction committed when the telegraph started (presentation reads it for the danger shape).</summary>
        public Vector2 LockedDirection => _lockedDirection;

        public EnemyAttackDefinition PendingAttack => _pending;
        public EnemyAttackDefinition LastStartedAttack { get; private set; }
        public int AttacksStarted { get; private set; }
        public AttackResolver Resolver => _resolver;
        public bool IsResolving => _resolver != null && _resolver.IsRunning;
        public IReadOnlyList<EnemyAttackDefinition> Moveset => _definition != null ? _definition.Moveset : System.Array.Empty<EnemyAttackDefinition>();

        public void Configure(EnemyDefinition definition, IDamageRoller damageRoller)
        {
            _definition = definition;
            _resolver = new AttackResolver(transform, GetComponent<Rigidbody2D>(), damageRoller ?? new UnityRandomDamageRoller());
            _cooldowns.Clear();
        }

        /// <summary>Selects the attack to telegraph (first ready one whose band contains the target); true when one exists.</summary>
        public bool IsTargetInAttackRange(Transform target)
        {
            _pending = null;
            if (target == null || _definition == null) return false;
            if (SmokeZone.IsLineOfSightBlocked(transform.position, target.position, ignoresSmoke: false)) return false;
            var distance = Vector2.Distance(target.position, transform.position);
            foreach (var attack in Moveset)
            {
                if (attack == null) continue;
                if (_cooldowns.TryGetValue(attack, out var remaining) && remaining > 0f) continue;
                if (attack.IsInTriggerRange(distance))
                {
                    _pending = attack;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetTiming(out float telegraphSeconds, out float recoverySeconds)
        {
            if (_pending == null)
            {
                telegraphSeconds = recoverySeconds = 0f;
                return false;
            }

            telegraphSeconds = _pending.TelegraphSeconds;
            recoverySeconds = _pending.RecoverySeconds;
            return true;
        }

        public void OnTelegraphStarted(Transform target)
        {
            if (target == null) return;
            var toTarget = (Vector2)target.position - (Vector2)transform.position;
            _lockedDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
        }

        public bool TryResolveAttack(Transform target)
        {
            if (_pending == null || _resolver == null) return false;
            var attack = _pending;
            _pending = null;
            _resolver.Begin(attack, _lockedDirection);
            _cooldowns[attack] = attack.CooldownSeconds;
            LastStartedAttack = attack;
            AttacksStarted++;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f && _cooldowns.Count > 0)
            {
                // Reused scratch list: no per-frame allocation for every active enemy (TASK 143).
                _cooldownScratch.Clear();
                _cooldownScratch.AddRange(_cooldowns.Keys);
                foreach (var key in _cooldownScratch) _cooldowns[key] = Mathf.Max(0f, _cooldowns[key] - deltaTime);
            }

            if (_resolver != null && _resolver.IsRunning) _resolver.Tick(deltaTime);
        }

        public void Cancel()
        {
            _pending = null;
            _resolver?.Cancel();
        }

        public float CooldownRemaining(EnemyAttackDefinition attack) => attack != null && _cooldowns.TryGetValue(attack, out var r) ? r : 0f;
    }
}
