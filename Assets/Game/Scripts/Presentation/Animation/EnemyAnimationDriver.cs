using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    public enum EnemyAnimState
    {
        Idle,
        Move,
        Telegraph,
        Attack,
        Recover,
        Death
    }

    /// <summary>
    /// Enemy / Elite / Boss body animation mapped from the authoritative state machines (EnemyController for the nine
    /// archetypes, MovesetActorController for Elites and Bosses): telegraph shows preparation for exactly as long as the
    /// gameplay telegraph lasts, the strike frame plays at the moment gameplay resolves the attack, recovery follows.
    /// Damage timing stays in gameplay — the animation only observes. Facing is 8-way from movement or target direction.
    /// </summary>
    public sealed class EnemyAnimationDriver : MonoBehaviour
    {
        /// <summary>V1 FINAL (TASK 179): how long the strike frame is held after the gameplay attack resolves (visual only).</summary>
        public const float StrikeSeconds = 0.15f;

        [SerializeField] private SpriteAnimator _animator;

        private EnemyController _enemy;
        private MovesetActorController _actor;
        private Rigidbody2D _body;
        private float _strikeRemaining;

        public EnemyAnimState State { get; private set; } = EnemyAnimState.Idle;
        public BodyFacing8 Facing { get; private set; } = BodyFacing8.S;
        public int StrikesShown { get; private set; }
        public SpriteAnimator Animator => _animator;

        private IReplicatedActorView _replica;

        /// <summary>Co-op client: drives the same animation from a host-replicated actor view (no controller exists here).</summary>
        public void ConfigureReplica(SpriteAnimator animator, IReplicatedActorView replica)
        {
            Configure(animator, null, null, null);
            if (_replica != null) _replica.Struck -= OnReplicaStruck;
            _replica = replica;
            if (_replica != null) _replica.Struck += OnReplicaStruck;
        }

        private void OnReplicaStruck(IReplicatedActorView _) => Strike();

        public void Configure(SpriteAnimator animator, EnemyController enemy, MovesetActorController actor, Rigidbody2D body)
        {
            Unsubscribe();
            _animator = animator;
            if (_animator != null) _animator.SelfTicking = false;
            _enemy = enemy;
            _actor = actor;
            _body = body;
            Subscribe();
        }

        private void Awake()
        {
            if (_enemy == null) _enemy = GetComponent<EnemyController>();
            if (_actor == null) _actor = GetComponent<MovesetActorController>();
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_animator == null) _animator = GetComponentInChildren<SpriteAnimator>();
            if (_animator != null) _animator.SelfTicking = false;
            Subscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_replica != null) _replica.Struck -= OnReplicaStruck;
        }

        private void Subscribe()
        {
            if (_enemy != null) _enemy.AttackResolved += OnEnemyAttackResolved;
            if (_actor != null) _actor.AttackResolved += OnActorAttackResolved;
        }

        private void Unsubscribe()
        {
            if (_enemy != null) _enemy.AttackResolved -= OnEnemyAttackResolved;
            if (_actor != null) _actor.AttackResolved -= OnActorAttackResolved;
        }

        private void OnEnemyAttackResolved(EnemyController _) => Strike();
        private void OnActorAttackResolved(MovesetActorController _, EnemyAttackDefinition __) => Strike();

        /// <summary>Scene wiring may report a resolved attack directly (e.g. summons without a controller reference).</summary>
        public void ReportStrike() => Strike();

        private void Strike()
        {
            _strikeRemaining = StrikeSeconds;
            StrikesShown++;
        }

        /// <summary>Pure mapping from the gameplay state (tests).</summary>
        public static EnemyAnimState Resolve(EnemyState state, bool moving, bool striking)
        {
            if (state == EnemyState.Dead) return EnemyAnimState.Death;
            if (striking) return EnemyAnimState.Attack;
            return state switch
            {
                EnemyState.Telegraph => EnemyAnimState.Telegraph,
                EnemyState.Recovery => EnemyAnimState.Recover,
                EnemyState.Staggered => EnemyAnimState.Recover,
                EnemyState.Chase => moving ? EnemyAnimState.Move : EnemyAnimState.Idle,
                _ => moving ? EnemyAnimState.Move : EnemyAnimState.Idle
            };
        }

        public static EnemyAnimState Resolve(MovesetActorState state, bool moving, bool striking)
        {
            if (state == MovesetActorState.Dead) return EnemyAnimState.Death;
            if (striking) return EnemyAnimState.Attack;
            return state switch
            {
                MovesetActorState.Telegraph => EnemyAnimState.Telegraph,
                MovesetActorState.Attacking => EnemyAnimState.Attack,
                MovesetActorState.Recovery => EnemyAnimState.Recover,
                MovesetActorState.Chase => moving ? EnemyAnimState.Move : EnemyAnimState.Idle,
                _ => moving ? EnemyAnimState.Move : EnemyAnimState.Idle
            };
        }

        public void Tick(float deltaTime)
        {
            if (_strikeRemaining > 0f) _strikeRemaining -= deltaTime;
            var striking = _strikeRemaining > 0f;
            var velocity = _replica != null ? _replica.Velocity : _body != null ? _body.linearVelocity : Vector2.zero;
            var moving = velocity.sqrMagnitude > 0.01f;

            if (_replica != null)
            {
                State = _replica.IsMoveset ? Resolve(_replica.MovesetState, moving, striking) : Resolve(_replica.EnemyState, moving, striking);
                var replicaFacing = moving ? velocity : _replica.Facing;
                if (State != EnemyAnimState.Death && replicaFacing.sqrMagnitude > 0.0001f) Facing = BodyFacingResolver.Resolve(replicaFacing.normalized);
                _animator?.Play(State.ToString(), Facing);
                _animator?.Tick(deltaTime);
                return;
            }

            if (_actor != null) State = Resolve(_actor.State, moving, striking);
            else if (_enemy != null) State = Resolve(_enemy.State, moving, striking);
            else State = moving ? EnemyAnimState.Move : EnemyAnimState.Idle;

            var target = _actor != null ? _actor.Target : _enemy != null ? _enemy.Target : null;
            var facingDirection = moving ? velocity : target != null ? (Vector2)(target.position - transform.position) : Vector2.zero;
            // A corpse keeps the facing it died with: the death animation plays in place, never turned toward the target.
            if (State != EnemyAnimState.Death && facingDirection.sqrMagnitude > 0.0001f) Facing = BodyFacingResolver.Resolve(facingDirection.normalized);

            _animator?.Play(State.ToString(), Facing);
            _animator?.Tick(deltaTime);
        }

        private void Update() => Tick(Time.deltaTime);
    }
}
