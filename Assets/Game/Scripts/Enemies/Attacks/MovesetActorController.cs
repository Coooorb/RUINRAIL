using System;
using System.Collections.Generic;
using RuinRail.Core.Rng;
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

        /// <summary>Diagnostics/proof seam: while true the actor pursues but never selects an attack (pursuit-only containment proofs).</summary>
        public bool SuppressAttacks { get; set; }

        /// <summary>
        /// Seeds this actor's attack-selection stream. The composer derives it from RunSeed + Depth + room, so the same
        /// run replays the same sequence of choices and a client rebuilding the encounter sees the same one. Without it
        /// the actor still chooses deterministically, from a seed derived from its own id alone.
        /// </summary>
        public void SetSelectionSeed(int runSeed, int depth, int roomIndex)
        {
            _selectionSeed = SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Encounter, SelectionSalt, roomIndex);
            _selectionRandom = null;
            _selectionDraws = 0;
        }

        private const int SelectionSalt = 0x4153; // "AS" — attack selection
        private ulong _selectionSeed;
        private SeededRandom _selectionRandom;
        private int _selectionDraws;
        private EnemyAttackDefinition _lastSelected;

        /// <summary>The attack chosen immediately before the current one (no-repeat diagnostics).</summary>
        public EnemyAttackDefinition LastSelectedAttack => _lastSelected;

        /// <summary>
        /// Diagnostics seam: clears every cooldown so a distribution proof can sample selection across the whole band
        /// range without cooldowns, rather than list order, deciding what was reachable.
        /// </summary>
        public void ClearCooldownsForDiagnostics() => _cooldowns.Clear();

        /// <summary>The attacks selection currently considers, including any phase-specific entries a subclass prepends.</summary>
        public IEnumerable<EnemyAttackDefinition> ActiveMovesetForDiagnostics => ActiveMoveset;

        /// <summary>How many selection draws this actor has made (determinism proofs).</summary>
        public int SelectionDraws => _selectionDraws;

        /// <summary>
        /// The deterministic stream this actor draws attack choices from. Created on first use so an actor that never
        /// reaches a choice allocates nothing, and never reseeded mid-encounter so the sequence stays reproducible.
        /// </summary>
        private SeededRandom SelectionRandom => _selectionRandom ??= new SeededRandom(
            _selectionSeed != 0 ? _selectionSeed : SeededRandom.MixSeed(ActorDefinition?.Id?.GetHashCode() ?? 0, SelectionSalt));

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
            // RepositionToEngagementRange: while the target sits outside every authored attack band the actor closes
            // faster, because nothing it owns can reach and plain pursuit at 1.6-3.2 tiles/s never catches a player at
            // 5. It deals no damage, uses the same obstacle steering and the same EncounterBounds constraint as normal
            // pursuit, and stops the moment any band contains the target — from there normal selection takes over.
            var outOfRange = IsOutOfEngagementRange;
            IsReengaging = outOfRange;
            if (outOfRange) TimeOutOfEngagementRange += Time.fixedDeltaTime;
            else TimeOutOfEngagementRange = 0f;
            var speed = ActorDefinition.MoveSpeed * (outOfRange ? ReengageSpeedMultiplier : 1f);
            var velocity = heading * speed;
            // The arena's legal edge is not traversable: a boss/elite pursuing a player beyond it holds at the edge.
            var bounds = Bounds;
            if (bounds != null && bounds.IsBound) velocity = bounds.ConstrainVelocity(_rigidbody2D.position, velocity, Time.fixedDeltaTime);
            _rigidbody2D.linearVelocity = velocity;
        }

        private ObstacleSteering _steering;
        private EncounterBounds _bounds;

        /// <summary>The obstacle steering in use (diagnostics/tests).</summary>
        public ObstacleSteering Steering => _steering;

        /// <summary>The encounter-room (arena) bounds this actor is confined to; null before the owning room binds them.</summary>
        public EncounterBounds Bounds => _bounds != null ? _bounds : _bounds = GetComponent<EncounterBounds>();

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

        /// <summary>
        /// One attack drawn from EVERY ready active-moveset attack whose trigger band contains the target.
        ///
        /// List order used to decide this, which made an attack that sits late in a moveset effectively unreachable
        /// whenever an earlier one was ready and in band — the Conductor's 14-tile burst cannon could not be seen while
        /// its 10-tile sweep was off cooldown. Candidates are now drawn from this actor's deterministic selection
        /// stream, so the same run replays the same sequence while no authored attack is unreachable.
        ///
        /// The attack chosen immediately before is skipped when any alternative is available, which removes the
        /// degenerate same-attack-twice case without inventing adaptive behaviour. Cooldowns, phases, bands, damage and
        /// telegraph timings are untouched.
        /// </summary>
        public EnemyAttackDefinition SelectAttack()
        {
            if (_target == null || ActorDefinition == null || SuppressAttacks) return null;
            var distance = Vector2.Distance(_target.position, transform.position);
            _candidates.Clear();
            foreach (var attack in ActiveMoveset)
            {
                if (attack == null) continue;
                if (_cooldowns.TryGetValue(attack, out var remaining) && remaining > 0f) continue;
                if (attack.IsInTriggerRange(distance)) _candidates.Add(attack);
            }

            if (_candidates.Count == 0) return null;
            if (_candidates.Count == 1)
            {
                _lastSelected = _candidates[0];
                return _lastSelected;
            }

            // Avoid an immediate repeat while an alternative exists; with only the previous attack ready it still fires.
            var pool = _candidates.Count > 1 && _lastSelected != null && _candidates.Contains(_lastSelected) ? _candidates.Count - 1 : _candidates.Count;
            var pick = SelectionRandom.NextInt(pool);
            _selectionDraws++;
            foreach (var attack in _candidates)
            {
                if (pool < _candidates.Count && attack == _lastSelected) continue;
                if (pick-- == 0)
                {
                    _lastSelected = attack;
                    return attack;
                }
            }

            _lastSelected = _candidates[_candidates.Count - 1];
            return _lastSelected;
        }

        /// <summary>Reused across selections: the AI loop must not allocate a list per choice.</summary>
        private readonly List<EnemyAttackDefinition> _candidates = new();

        // ---- Phase 7: re-engagement when nothing is in band ----

        /// <summary>
        /// How much faster the actor closes while NO authored attack can reach the target. Bounded and non-damaging: it
        /// is a gap-close, not a speed buff — inside any attack band the actor moves at its authored speed again.
        /// </summary>
        public const float ReengageSpeedMultiplier = 1.6f;

        /// <summary>The longest trigger range any attack in the active moveset has; 0 when the actor has no moveset.</summary>
        public float MaxAuthoredAttackRange
        {
            get
            {
                var max = 0f;
                foreach (var attack in ActiveMoveset)
                {
                    if (attack != null && attack.MaxTriggerRange > max) max = attack.MaxTriggerRange;
                }

                return max;
            }
        }

        /// <summary>
        /// The target is beyond every authored attack band, so no attack can ever become valid where the actor stands.
        /// This is the state that used to leave a boss walking at 1.6-3.2 tiles/s after a player moving at 5.
        /// </summary>
        public bool IsOutOfEngagementRange =>
            _target != null && ActorDefinition != null && MaxAuthoredAttackRange > 0f
            && Vector2.Distance(_target.position, transform.position) > MaxAuthoredAttackRange;

        /// <summary>Seconds spent so far unable to reach the target with any attack (diagnostics / anti-kite proof).</summary>
        public float TimeOutOfEngagementRange { get; private set; }

        /// <summary>True while the re-engagement gap-close is driving the body.</summary>
        public bool IsReengaging { get; private set; }

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
