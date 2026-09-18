using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Attacks
{
    public enum MovesetActorState
    {
        Idle,
        Chase,
        Telegraph,
        Attacking,
        Recovery,
        Dead
    }

    /// <summary>Data an actor needs from its definition; implemented by Elite and Boss definitions.</summary>
    public interface IMovesetActorDefinition
    {
        string Id { get; }
        int BaseHealth { get; }
        float MoveSpeed { get; }
        int BaseXp { get; }
        int StaggerResistancePercent { get; }
        int KnockbackResistancePercent { get; }

        /// <summary>False for bosses: knockback never moves them (46: no wall-knocking a boss).</summary>
        bool IsDisplaceable { get; }
        IReadOnlyList<EnemyAttackDefinition> Moveset { get; }
    }

    /// <summary>
    /// Shared fixed-moveset actor FSM (43_ENEMY_FRAMEWORK): acquire → chase → telegraph → attack → recovery, cycling a
    /// data-driven moveset. Attack selection is deterministic (first ready entry whose trigger band contains the
    /// target), the attack direction is locked when the telegraph starts, and all damage flows through
    /// AttackResolver → IDamageable. Subclasses add Elite/Boss specifics (phases, encounter semantics).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(HealthComponent))]
    public abstract class MovesetActorController : MonoBehaviour
    {
        [SerializeField] private StaggerConfig _staggerConfig;

        private Rigidbody2D _rigidbody2D;
        private HealthComponent _health;
        private ImpactReceiver _impact;
        private IDamageRoller _damageRoller;
        private AttackResolver _resolver;
        private ProjectilePool _projectilePool;
        private Transform _target;
        private float _phaseTimeRemaining;
        private EnemyAttackDefinition _currentAttack;
        private Vector2 _lockedDirection;
        private bool _encounterStarted;
        private bool _diedRaised;
        private readonly Dictionary<EnemyAttackDefinition, float> _cooldowns = new();

        public MovesetActorState State { get; private set; } = MovesetActorState.Idle;
        public Transform Target => _target;
        public EnemyAttackDefinition CurrentAttack => _currentAttack;
        /// <summary>Direction locked at telegraph start (read-only seam for telegraph indicators).</summary>
        public Vector2 LockedDirection => _lockedDirection;
        public bool IsAlive => _health != null && _health.IsAlive;
        public HealthComponent Health => _health;
        public ImpactReceiver Impact => _impact;
        public bool IsStaggered => _impact != null && _impact.IsStaggered;
        public int StaggerInterruptions { get; private set; }
        public bool EncounterStarted => _encounterStarted;
        public int XpValue => ActorDefinition != null ? ActorDefinition.BaseXp : 0;

        /// <summary>Multiplier applied to telegraph, recovery and cooldown durations (1 = authored values).</summary>
        public float TimingMultiplier { get; protected set; } = 1f;

        protected abstract IMovesetActorDefinition ActorDefinition { get; }

        /// <summary>Attacks considered for selection, in priority order (subclasses may prepend phase-specific entries).</summary>
        protected virtual IEnumerable<EnemyAttackDefinition> ActiveMoveset => ActorDefinition?.Moveset ?? Array.Empty<EnemyAttackDefinition>();

        public event Action<MovesetActorController> EncounterStartedEvent;
        public event Action<MovesetActorController, EnemyAttackDefinition> AttackTelegraphStarted;
        public event Action<MovesetActorController, EnemyAttackDefinition> AttackResolved;
        public event Action<MovesetActorController> Died;

        /// <summary>Shared stagger/knockback tuning; without it the actor neither staggers nor gets knocked back.</summary>
        public void SetStaggerConfig(StaggerConfig config)
        {
            _staggerConfig = config;
            EnsureImpactReceiver();
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        public void SetDamageRoller(IDamageRoller damageRoller)
        {
            _damageRoller = damageRoller;
            _resolver = null;
        }

        public void SetProjectilePool(ProjectilePool pool)
        {
            _projectilePool = pool;
            _resolver = null;
        }

        protected virtual void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _rigidbody2D.gravityScale = 0f;
            _rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            _rigidbody2D.freezeRotation = true;

            _health = GetComponent<HealthComponent>();
            _health.Died += HandleDied;
            _health.Damaged += HandleDamaged;
            _damageRoller ??= new UnityRandomDamageRoller();
            _projectilePool ??= GetComponent<ProjectilePool>();
            EnsureImpactReceiver();

            if (_target == null)
            {
                var player = FindFirstObjectByType<PlayerInput>();
                if (player != null) _target = player.transform;
            }

            ApplyDefinition();
        }

        protected virtual void OnDestroy()
        {
            if (_health != null)
            {
                _health.Died -= HandleDied;
                _health.Damaged -= HandleDamaged;
            }
        }

        public AttackResolver Resolver => _resolver ??= new AttackResolver(transform, _rigidbody2D, _damageRoller ?? new UnityRandomDamageRoller(), _projectilePool ??= GetComponent<ProjectilePool>());

        protected void ApplyDefinition()
        {
            if (ActorDefinition == null || _health == null) return;
            _health.SetMaxHealth(ActorDefinition.BaseHealth);
            EnsureImpactReceiver();
        }

        private void EnsureImpactReceiver()
        {
            if (_impact == null)
            {
                _impact = GetComponent<ImpactReceiver>();
                if (_impact == null && _staggerConfig != null) _impact = gameObject.AddComponent<ImpactReceiver>();
                if (_impact != null)
                {
                    _impact.Staggered -= HandleStaggered;
                    _impact.Staggered += HandleStaggered;
                }
            }

            if (_impact == null) return;
            if (_staggerConfig != null) _impact.SetConfig(_staggerConfig);
            if (_damageRoller != null) _impact.SetDamageRoller(_damageRoller);
            var d = ActorDefinition;
            if (d != null) _impact.SetProfile(new ImpactProfile(d.Id, d.StaggerResistancePercent, d.KnockbackResistancePercent, d.IsDisplaceable, !d.IsDisplaceable));
        }

        private void HandleStaggered(ImpactReceiver receiver)
        {
            if (State == MovesetActorState.Dead) return;
            StaggerInterruptions++;
            // Interrupt the current action: a telegraph or running attack is cancelled and the actor sits in recovery
            // for the stagger duration, then re-evaluates from chase (cooldown of the lost attack is not charged).
            if (State == MovesetActorState.Telegraph || State == MovesetActorState.Attacking)
            {
                Resolver.Cancel();
                _currentAttack = null;
            }

            State = MovesetActorState.Recovery;
            _phaseTimeRemaining = receiver.Meter.StaggerTimeRemaining;
            if (_rigidbody2D != null) _rigidbody2D.linearVelocity = Vector2.zero;
        }

        protected virtual void OnDamaged(int amount)
        {
        }

        protected virtual void OnDied()
        {
        }

        private void HandleDamaged(int amount)
        {
            if (State == MovesetActorState.Dead) return;
            OnDamaged(amount);
        }

        private void HandleDied()
        {
            if (_diedRaised) return;
            _diedRaised = true;
            State = MovesetActorState.Dead;
            Resolver.Cancel();
            _currentAttack = null;
            if (_rigidbody2D != null) _rigidbody2D.linearVelocity = Vector2.zero;
            OnDied();
            Died?.Invoke(this);
        }

        private void Update()
        {
            if (State == MovesetActorState.Dead || ActorDefinition == null) return;
            var deltaTime = Time.deltaTime;
            TickCooldowns(deltaTime);

            switch (State)
            {
                case MovesetActorState.Idle:
                    if (_target != null)
                    {
                        StartEncounter();
                        State = MovesetActorState.Chase;
                    }
                    break;

                case MovesetActorState.Chase:
                    if (_target == null)
                    {
                        State = MovesetActorState.Idle;
                        break;
                    }

                    var attack = SelectAttack();
                    if (attack != null)
                    {
                        BeginTelegraph(attack);
                    }
                    break;

                case MovesetActorState.Telegraph:
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        // Committed: direction was locked at telegraph start so the attack stays readable/dodgeable.
                        Resolver.Begin(_currentAttack, _lockedDirection);
                        State = MovesetActorState.Attacking;
                    }
                    break;

                case MovesetActorState.Attacking:
                    if (Resolver.Tick(deltaTime))
                    {
                        AttackResolved?.Invoke(this, _currentAttack);
                        _cooldowns[_currentAttack] = _currentAttack.CooldownSeconds * TimingMultiplier;
                        _phaseTimeRemaining = _currentAttack.RecoverySeconds * TimingMultiplier;
                        State = MovesetActorState.Recovery;
                    }
                    break;

                case MovesetActorState.Recovery:
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        _currentAttack = null;
                        State = _target != null ? MovesetActorState.Chase : MovesetActorState.Idle;
                    }
                    break;
            }
        }

        private void FixedUpdate()
        {
            if (_impact != null && _impact.IsKnockbackActive)
            {
                return; // the knockback owns the body this step
            }

            if (State != MovesetActorState.Chase || _target == null || ActorDefinition == null)
            {
                if (_rigidbody2D != null && State != MovesetActorState.Dead && State != MovesetActorState.Attacking)
                {
                    _rigidbody2D.linearVelocity = Vector2.zero;
                }

                return;
            }

            var toTarget = (Vector2)_target.position - _rigidbody2D.position;
            if (toTarget.sqrMagnitude < 0.0001f) { _rigidbody2D.linearVelocity = Vector2.zero; return; }
            _steering ??= new ObstacleSteering(transform, BodyRadius());
            var heading = _steering.Steer(_rigidbody2D.position, toTarget.normalized, Time.fixedDeltaTime);
            _rigidbody2D.linearVelocity = heading * ActorDefinition.MoveSpeed;
        }

        private ObstacleSteering _steering;

        /// <summary>The obstacle steering in use (diagnostics/tests).</summary>
        public ObstacleSteering Steering => _steering;

        private float BodyRadius()
        {
            var circle = GetComponent<CircleCollider2D>();
            return circle != null ? circle.radius : 0.6f;
        }

        private void StartEncounter()
        {
            if (_encounterStarted) return;
            _encounterStarted = true;
            EncounterStartedEvent?.Invoke(this);
        }

        /// <summary>First ready active-moveset attack whose trigger range contains the target (fixed priority order).</summary>
        public EnemyAttackDefinition SelectAttack()
        {
            if (_target == null || ActorDefinition == null) return null;
            var distance = Vector2.Distance(_target.position, transform.position);
            foreach (var attack in ActiveMoveset)
            {
                if (attack == null) continue;
                if (_cooldowns.TryGetValue(attack, out var remaining) && remaining > 0f) continue;
                if (attack.IsInTriggerRange(distance)) return attack;
            }

            return null;
        }

        private void BeginTelegraph(EnemyAttackDefinition attack)
        {
            _currentAttack = attack;
            _lockedDirection = ((Vector2)_target.position - (Vector2)transform.position).normalized;
            _phaseTimeRemaining = attack.TelegraphSeconds * TimingMultiplier;
            _rigidbody2D.linearVelocity = Vector2.zero;
            State = MovesetActorState.Telegraph;
            AttackTelegraphStarted?.Invoke(this, attack);
        }

        private void TickCooldowns(float deltaTime)
        {
            if (_cooldowns.Count == 0) return;
            var keys = new List<EnemyAttackDefinition>(_cooldowns.Keys);
            foreach (var key in keys)
            {
                _cooldowns[key] = Mathf.Max(0f, _cooldowns[key] - deltaTime);
            }
        }
    }
}
