using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>
    /// Shared ranged attack for normal enemies (Shooter, later Sniper): after the controller's telegraph it fires one or
    /// more visible pooled projectiles at the target's position (locked when the burst starts). Damage is the
    /// definition's integer band rolled per projectile through the shared projectile/IDamageable path; never hitscan,
    /// no crits. Bursts are ticked by the controller through <see cref="Tick"/>.
    /// </summary>
    public sealed class EnemyProjectileAttack : MonoBehaviour, IEnemyAttackBehaviour, IEnemyContinuousAttack, IEnemyTelegraphAware
    {
        private EnemyDefinition _definition;
        private IDamageRoller _damageRoller;
        private ProjectilePool _pool;
        private readonly List<Projectile> _spawned = new();
        private Vector2 _lockedDirection = Vector2.right;

        /// <summary>Direction committed when the telegraph started (presentation reads it for the danger shape).</summary>
        public Vector2 LockedDirection => _lockedDirection;
        private int _burstRemaining;
        private float _untilNextShot;
        private Transform _aimTarget;
        private float _trackingRemaining;
        private bool _aimLocked;

        public int ShotsFired { get; private set; }

        /// <summary>Current aim: tracks the target during the telegraph, then locks (44 Sniper: visible aim line tracks, locks, fires).</summary>
        public Vector2 AimDirection => _lockedDirection;
        public bool IsAimLocked => _aimLocked;
        public int LastDamageDealt { get; private set; }
        public bool IsBursting => _burstRemaining > 0;
        public bool IsResolving => IsBursting;
        public IReadOnlyList<Projectile> SpawnedProjectiles => _spawned;

        public void Configure(EnemyDefinition definition, IDamageRoller damageRoller, ProjectilePool pool)
        {
            _definition = definition;
            _damageRoller = damageRoller;
            _pool = pool;
        }

        public void SetProjectilePool(ProjectilePool pool)
        {
            _pool = pool;
        }

        private void Awake()
        {
            _pool ??= GetComponent<ProjectilePool>();
        }

        /// <summary>In range and with line of sight (normal enemies cannot shoot through smoke).</summary>
        public bool IsTargetInAttackRange(Transform target)
        {
            if (target == null || _definition == null) return false;
            var toTarget = (Vector2)target.position - (Vector2)transform.position;
            if (toTarget.magnitude > _definition.AttackRange) return false;
            return !SmokeZone.IsLineOfSightBlocked(transform.position, target.position, ignoresSmoke: false);
        }

        /// <summary>Telegraph start: aim tracks the target until AimLockSeconds before the (unscaled) telegraph ends, then locks.</summary>
        public void OnTelegraphStarted(Transform target)
        {
            _aimTarget = target;
            _aimLocked = false;
            _trackingRemaining = _definition != null ? Mathf.Max(0f, _definition.AttackTelegraphSeconds - _definition.AimLockSeconds) : 0f;
            TrackAim();
            if (_definition == null || _definition.AimLockSeconds <= 0f) _aimTarget = null; // no lock window: aim at fire time
        }

        private void TrackAim()
        {
            if (_aimTarget == null) return;
            var toTarget = (Vector2)_aimTarget.position - (Vector2)transform.position;
            if (toTarget.sqrMagnitude > 0.0001f) _lockedDirection = toTarget.normalized;
        }

        /// <summary>Fires the first projectile of the burst along the locked aim (or at the target's current position); the rest follow through Tick.</summary>
        public bool TryResolveAttack(Transform target)
        {
            if (_pool == null || _damageRoller == null || target == null) return false;
            if (!_aimLocked)
            {
                if (!IsTargetInAttackRange(target)) return false;
                var toTarget = (Vector2)target.position - (Vector2)transform.position;
                _lockedDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : Vector2.right;
            }

            _aimTarget = null;
            _aimLocked = false;
            _burstRemaining = _definition.BurstCount;
            _untilNextShot = 0f;
            _spawned.Clear();
            Tick(0f);
            return true;
        }

        /// <summary>Advances a running burst; safe to call every frame.</summary>
        public void Tick(float deltaTime)
        {
            if (_aimTarget != null && !_aimLocked)
            {
                _trackingRemaining -= deltaTime;
                if (_trackingRemaining <= 0f) _aimLocked = true; // locked: the shot goes where the line points now
                else TrackAim();
            }

            if (_burstRemaining <= 0 || _definition == null) return;
            _untilNextShot -= deltaTime;
            while (_burstRemaining > 0 && _untilNextShot <= 0f)
            {
                Fire();
                _burstRemaining--;
                _untilNextShot += _definition.BurstIntervalSeconds;
                if (_definition.BurstIntervalSeconds <= 0f) _untilNextShot = 0f;
            }
        }

        /// <summary>Death or stagger: whatever is left of the burst is dropped.</summary>
        public void Cancel()
        {
            _burstRemaining = 0;
            _aimTarget = null;
            _aimLocked = false;
        }

        private void Fire()
        {
            LastDamageDealt = _damageRoller.Roll(_definition.DamageMin, _definition.DamageMax);
            var data = new ProjectileSpawnData(LastDamageDealt, _definition.ProjectileSpeed, _definition.ProjectileRange, 0f, 0f, _lockedDirection, gameObject, null, 0f, DamageTeam.Enemy, false, _definition.ProjectileVisualId);
            _spawned.Add(_pool.Spawn(transform.position, data));
            ShotsFired++;
        }
    }
}
