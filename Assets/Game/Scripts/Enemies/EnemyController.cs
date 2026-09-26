using System;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(HealthComponent))]
    public sealed class EnemyController : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition _definition;
        [SerializeField] private StaggerConfig _staggerConfig;

        private Rigidbody2D _rigidbody2D;
        private HealthComponent _health;
        private ImpactReceiver _impact;
        private EnemyState _stateBeforeStagger;
        private ProjectilePool _projectilePool;
        private FrontalShield _shield;
        private EnemySummoner _summoner;
        private float _attackSpeedMultiplier = 1f;
        private float _movementSpeedMultiplier = 1f;
        private bool _attackAutoComposed;
        private float _pendingRecoverySeconds;
        private IEnemyAttackBehaviour _attack;
        private IDamageRoller _damageRoller;
        private Transform _target;
        private float _phaseTimeRemaining;

        public EnemyDefinition Definition => _definition;
        public EnemyState State { get; private set; } = EnemyState.Idle;
        public Transform Target => _target;
        public int XpValue => _definition != null ? _definition.BaseXp : 0;
        public bool IsAlive => _health != null && _health.IsAlive;
        public ImpactReceiver Impact => _impact;
        public bool IsStaggered => State == EnemyState.Staggered;
        public IEnemyAttackBehaviour Attack => _attack;
        public FrontalShield Shield => _shield;
        public EnemySummoner Summoner => _summoner;

        /// <summary>59: attack frequency scales only slightly with depth and caps (+10%). Telegraph/cooldown durations divide by this.</summary>
        public float AttackSpeedMultiplier => _attackSpeedMultiplier;
        public const float MaxAttackSpeedMultiplier = 1.1f;
        public const float MaxMovementSpeedMultiplier = 1.1f;

        /// <summary>Normal enemies are blinded by smoke; only Elites/Bosses ignore it (they use MovesetActorController).</summary>
        public bool HasLineOfSightToTarget => _target != null && !SmokeZone.IsLineOfSightBlocked(transform.position, _target.position, ignoresSmoke: false);

        public event Action<EnemyController> Died;
        public event Action<EnemyController> AttackResolved;

        public void SetDefinition(EnemyDefinition definition)
        {
            _definition = definition;
            ApplyDefinition();
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }

        public void SetDamageRoller(IDamageRoller damageRoller)
        {
            _damageRoller = damageRoller;
            ApplyDefinition();
        }

        public void SetAttackBehaviour(IEnemyAttackBehaviour attack)
        {
            _attack = attack;
            _attackAutoComposed = attack == null;
            ApplyDefinition();
        }

        /// <summary>Pool used by projectile archetypes; auto-discovered on the object when not injected.</summary>
        public void SetProjectilePool(ProjectilePool pool)
        {
            _projectilePool = pool;
            ApplyDefinition();
        }

        /// <summary>Depth scaling seam: clamped to the approved cap so telegraphs stay readable.</summary>
        public void SetAttackSpeedMultiplier(float multiplier)
        {
            _attackSpeedMultiplier = Mathf.Clamp(multiplier, 0.5f, MaxAttackSpeedMultiplier);
        }

        /// <summary>Depth scaling seam for movement (59 cap 1.1).</summary>
        public void SetMovementSpeedMultiplier(float multiplier)
        {
            _movementSpeedMultiplier = Mathf.Clamp(multiplier, 0.5f, MaxMovementSpeedMultiplier);
        }

        public float MovementSpeedMultiplier => _movementSpeedMultiplier;

        public float CurrentTelegraphSeconds => _definition == null ? 0f : _definition.AttackTelegraphSeconds / _attackSpeedMultiplier;
        public float CurrentCooldownSeconds => _definition == null ? 0f : _definition.AttackCooldownSeconds / _attackSpeedMultiplier;

        /// <summary>Shared stagger/knockback tuning; without it the enemy neither staggers nor gets knocked back.</summary>
        public void SetStaggerConfig(StaggerConfig config)
        {
            _staggerConfig = config;
            EnsureImpactReceiver();
        }

        private ObstacleSteering _steering;
        private EncounterBounds _bounds;

        /// <summary>The obstacle steering in use (diagnostics/tests).</summary>
        public ObstacleSteering Steering => _steering;

        /// <summary>The encounter-room bounds this enemy is confined to (bound by the owning room when it spawns; null before/without).</summary>
        public EncounterBounds Bounds => _bounds != null ? _bounds : _bounds = GetComponent<EncounterBounds>();

        private float BodyRadius()
        {
            var circle = GetComponent<CircleCollider2D>();
            return circle != null ? circle.radius : 0.35f;
        }

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _rigidbody2D.gravityScale = 0f;
            _rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // A chaser never needs to rotate; a locked rotation keeps the body from spinning off a wall contact.
            _rigidbody2D.freezeRotation = true;

            _health = GetComponent<HealthComponent>();
            _health.Died += HandleDied;

            _damageRoller ??= new UnityRandomDamageRoller();
            _projectilePool ??= GetComponent<ProjectilePool>();
            EnsureImpactReceiver();
            EnsureAttackBehaviour();

            if (_target == null)
            {
                var player = FindFirstObjectByType<PlayerInput>();
                if (player != null)
                {
                    _target = player.transform;
                }
            }

            ApplyDefinition();
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.Died -= HandleDied;
            }
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
                    _impact.StaggerEnded -= HandleStaggerEnded;
                    _impact.StaggerEnded += HandleStaggerEnded;
                }
            }

            if (_impact != null)
            {
                if (_staggerConfig != null) _impact.SetConfig(_staggerConfig);
                if (_damageRoller != null) _impact.SetDamageRoller(_damageRoller);
                if (_definition != null)
                {
                    _impact.SetProfile(new ImpactProfile(_definition.Id, _definition.StaggerResistancePercent, _definition.KnockbackResistancePercent, true, false));
                }
            }
        }

        private void HandleStaggered(ImpactReceiver _)
        {
            if (State == EnemyState.Dead) return;
            // The current action (telegraph, swing recovery, chase) is interrupted for the stagger duration.
            _stateBeforeStagger = State;
            State = EnemyState.Staggered;
            (_attack as IEnemyContinuousAttack)?.Cancel();
            _phaseTimeRemaining = 0f;
            if (_rigidbody2D != null) _rigidbody2D.linearVelocity = Vector2.zero;
        }

        private void HandleStaggerEnded(ImpactReceiver _)
        {
            if (State != EnemyState.Staggered) return;
            // A staggered attack is lost: the enemy resumes from a short recovery, never mid-swing.
            State = EnemyState.Recovery;
            _phaseTimeRemaining = _stateBeforeStagger == EnemyState.Telegraph ? _definition.AttackCooldownSeconds : 0f;
        }

        /// <summary>Composes the attack behaviour the definition asks for (43: modular behaviours). Injected behaviours are kept.</summary>
        private void EnsureAttackBehaviour()
        {
            var kind = _definition != null ? _definition.AttackKind : EnemyAttackKind.MeleeContact;
            if (_attack != null)
            {
                // An injected behaviour is authoritative; an auto-composed one follows the definition's kind.
                var matches = kind switch
                {
                    EnemyAttackKind.Projectile => _attack is EnemyProjectileAttack,
                    EnemyAttackKind.Charge => _attack is EnemyChargeAttack,
                    EnemyAttackKind.Moveset => _attack is EnemyMovesetAttack,
                    EnemyAttackKind.Lob => _attack is EnemyLobAttack,
                    _ => _attack is EnemyMeleeContactAttack
                };
                if (!_attackAutoComposed || matches) return;
            }

            _attackAutoComposed = true;
            if (kind == EnemyAttackKind.Projectile)
            {
                var ranged = GetComponent<EnemyProjectileAttack>();
                if (ranged == null) ranged = gameObject.AddComponent<EnemyProjectileAttack>();
                _attack = ranged;
            }
            else if (kind == EnemyAttackKind.Charge)
            {
                var charge = GetComponent<EnemyChargeAttack>();
                if (charge == null) charge = gameObject.AddComponent<EnemyChargeAttack>();
                _attack = charge;
            }
            else if (kind == EnemyAttackKind.Moveset)
            {
                var moveset = GetComponent<EnemyMovesetAttack>();
                if (moveset == null) moveset = gameObject.AddComponent<EnemyMovesetAttack>();
                _attack = moveset;
            }
            else if (kind == EnemyAttackKind.Lob)
            {
                var lob = GetComponent<EnemyLobAttack>();
                if (lob == null) lob = gameObject.AddComponent<EnemyLobAttack>();
                _attack = lob;
            }
            else
            {
                var contact = GetComponent<EnemyMeleeContactAttack>();
                if (contact == null) contact = gameObject.AddComponent<EnemyMeleeContactAttack>();
                _attack = contact;
            }
        }

        private void ApplyDefinition()
        {
            if (_definition == null)
            {
                return;
            }

            if (_health != null)
            {
                _health.SetMaxHealth(_definition.BaseHealth);
            }

            EnsureImpactReceiver();
            if (_rigidbody2D != null) EnsureAttackBehaviour();

            if (_definition.IsSummoner)
            {
                _summoner = GetComponent<EnemySummoner>();
                if (_summoner == null) _summoner = gameObject.AddComponent<EnemySummoner>();
                _summoner.Configure(_definition, this);
            }

            if (_definition.HasFrontalShield && _health != null)
            {
                _shield = GetComponent<FrontalShield>();
                if (_shield == null) _shield = gameObject.AddComponent<FrontalShield>();
                _shield.Configure(_definition.FrontalShieldPercent, _definition.FrontalShieldArcDegrees);
            }

            if (_attack is EnemyProjectileAttack ranged && _damageRoller != null)
            {
                ranged.Configure(_definition, _damageRoller, _projectilePool ??= GetComponent<ProjectilePool>());
            }

            if (_attack is EnemyChargeAttack charger && _damageRoller != null)
            {
                charger.Configure(_definition, _damageRoller);
            }

            if (_attack is EnemyMovesetAttack moves && _damageRoller != null)
            {
                moves.Configure(_definition, _damageRoller);
            }

            if (_attack is EnemyLobAttack lobber && _damageRoller != null)
            {
                lobber.Configure(_definition, _damageRoller);
            }

            if (_attack is EnemyMeleeContactAttack contact && _damageRoller != null)
            {
                contact.Configure(_definition, _damageRoller);
            }
        }

        private void HandleDied()
        {
            State = EnemyState.Dead;
            (_attack as IEnemyContinuousAttack)?.Cancel();
            if (_rigidbody2D != null)
            {
                // The corpse is stationary: leaving the simulation means no body, knockback, blast or hazard moves or
                // turns it again (the renderer stays; lifetime rules are unchanged).
                _rigidbody2D.linearVelocity = Vector2.zero;
                _rigidbody2D.angularVelocity = 0f;
                _rigidbody2D.simulated = false;
            }

            if (_impact != null) _impact.MakeInert();
            Died?.Invoke(this);
        }

        private void Update()
        {
            if (State == EnemyState.Dead || _definition == null)
            {
                return;
            }

            var deltaTime = Time.deltaTime;
            if (_shield != null && _target != null) _shield.SetFacing((Vector2)_target.position - (Vector2)transform.position);
            var continuous = _attack as IEnemyContinuousAttack;
            continuous?.Tick(deltaTime);

            switch (State)
            {
                case EnemyState.Idle:
                    if (_target != null && HasLineOfSightToTarget)
                    {
                        State = EnemyState.Chase;
                    }

                    break;

                case EnemyState.Chase:
                    if (_target == null || !HasLineOfSightToTarget)
                    {
                        // Normal enemies cannot maintain targeting through smoke (31_CONSUMABLES): stop chasing until it clears.
                        State = EnemyState.Idle;
                    }
                    else if (_attack.IsTargetInAttackRange(_target) && !IsTooClose())
                    {
                        State = EnemyState.Telegraph;

                        // Stop the instant the attack is committed to, rather than waiting for the next movement
                        // tick to notice the state left Chase. Whether that tick runs before or after this transition
                        // depends on frame ordering, so leaving it implicit let an enemy slide through the first
                        // telegraph frame at full chase speed — which reads as the telegraph not being honoured.
                        if (_rigidbody2D != null) _rigidbody2D.linearVelocity = Vector2.zero;

                        _phaseTimeRemaining = CurrentTelegraphSeconds;
                        _pendingRecoverySeconds = CurrentCooldownSeconds;
                        if (_attack is IEnemyAttackTiming timing && timing.TryGetTiming(out var telegraph, out var recovery))
                        {
                            // Movesets: the chosen move's own timings, scaled and capped like the definition's.
                            _phaseTimeRemaining = telegraph / _attackSpeedMultiplier;
                            _pendingRecoverySeconds = recovery / _attackSpeedMultiplier;
                        }

                        (_attack as IEnemyTelegraphAware)?.OnTelegraphStarted(_target);
                    }

                    break;

                case EnemyState.Telegraph:
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        _attack.TryResolveAttack(_target);
                        AttackResolved?.Invoke(this);
                        State = EnemyState.Recovery;
                        _phaseTimeRemaining = _pendingRecoverySeconds;
                    }

                    break;

                case EnemyState.Recovery:
                    if (continuous != null && continuous.IsResolving) break; // the attack is still running (burst/charge): recovery starts after it
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        State = _target != null ? EnemyState.Chase : EnemyState.Idle;
                    }

                    break;

                case EnemyState.Staggered:
                    // Timed by the ImpactReceiver's meter; HandleStaggerEnded moves on.
                    break;
            }
        }

        /// <summary>Distance-keepers (44 Shooter) reposition before attacking when the target is inside their preferred distance.</summary>
        private bool IsTooClose()
        {
            if (_definition == null || !_definition.KeepsDistance || _target == null) return false;
            return ((Vector2)_target.position - (Vector2)transform.position).magnitude < _definition.PreferredDistance;
        }

        private void FixedUpdate()
        {
            if (_impact != null && _impact.IsKnockbackActive)
            {
                return; // the knockback owns the body this step
            }

            if (_attack is IEnemyContinuousAttack running && running.IsResolving)
            {
                return; // a charge drives the body itself
            }

            if (State != EnemyState.Chase || _target == null || _definition == null)
            {
                if (_rigidbody2D != null && State != EnemyState.Dead)
                {
                    _rigidbody2D.linearVelocity = Vector2.zero;
                }

                return;
            }

            var toTarget = (Vector2)_target.position - _rigidbody2D.position;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                _rigidbody2D.linearVelocity = Vector2.zero;
                return;
            }

            // Spacing (44 Shooter: keeps medium distance): approach beyond attack range, back off inside the preferred
            // distance, hold between. Pure pursuers (preferred distance 0) always close in.
            var distance = toTarget.magnitude;
            var direction = toTarget / distance;
            if (_definition.KeepsDistance)
            {
                if (distance < _definition.PreferredDistance) direction = -direction;
                else if (distance <= _definition.AttackRange) { _rigidbody2D.linearVelocity = Vector2.zero; return; }
            }

            // Solid geometry deflects the heading (wall slide / corner rounding) instead of being pushed into.
            _steering ??= new ObstacleSteering(transform, BodyRadius());
            direction = _steering.Steer(_rigidbody2D.position, direction, Time.fixedDeltaTime);
            var velocity = direction * _definition.MoveSpeed * _movementSpeedMultiplier;
            // The encounter room's legal edge is not traversable: pursuit stops (slides) there, whatever the doors do.
            var bounds = Bounds;
            if (bounds != null && bounds.IsBound) velocity = bounds.ConstrainVelocity(_rigidbody2D.position, velocity, Time.fixedDeltaTime);
            _rigidbody2D.linearVelocity = velocity;
        }
    }
}
