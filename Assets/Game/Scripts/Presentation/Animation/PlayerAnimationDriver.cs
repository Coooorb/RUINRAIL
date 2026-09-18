using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Presentation.Animation
{
    public enum PlayerAnimState
    {
        Idle,
        Walk,
        Dash,
        Downed,
        GetUp,
        Death
    }

    /// <summary>Everything the player body animation is derived from; filled from gameplay components and never fed back.</summary>
    public readonly struct PlayerAnimInputs
    {
        public PlayerAnimInputs(PlayerLifeState life, bool isDashing, bool isMoving, BodyFacing8 facing, float sinceRevive)
        {
            Life = life;
            IsDashing = isDashing;
            IsMoving = isMoving;
            Facing = facing;
            SecondsSinceRevive = sinceRevive;
        }

        public PlayerLifeState Life { get; }
        public bool IsDashing { get; }
        public bool IsMoving { get; }
        public BodyFacing8 Facing { get; }
        public float SecondsSinceRevive { get; }
    }

    /// <summary>
    /// art/103 player baseline (Idle, Walk, Dash, Downed, Revive/Get Up, Death) mapped from authoritative gameplay state:
    /// PlayerLifeStateComponent (Downed/Dead/revived), PlayerDash.IsDashing, movement intent, and PlayerAiming.BodyFacing
    /// for the 8-way body. The weapon pivot keeps its mathematical 360° rotation from PlayerAiming; this driver never
    /// touches it and never quantises the aim. Read-only over gameplay; output is a clip key + facing for the animator.
    /// </summary>
    public sealed class PlayerAnimationDriver : MonoBehaviour
    {
        /// <summary>V1 FINAL (TASK 179) visual-only length of the Get Up clip after a revive (revive protection itself is gameplay data).</summary>
        public const float GetUpSeconds = 0.4f;

        [SerializeField] private SpriteAnimator _animator;

        private PlayerLifeStateComponent _life;
        private PlayerDash _dash;
        private PlayerAiming _aiming;
        private IPlayerInputReader _reader;
        private Rigidbody2D _body;
        private PlayerLifeState _lastLife = PlayerLifeState.Alive;
        private float _sinceRevive = float.MaxValue;

        public PlayerAnimState State { get; private set; } = PlayerAnimState.Idle;
        public BodyFacing8 Facing { get; private set; } = BodyFacing8.S;
        public SpriteAnimator Animator => _animator;

        public void Configure(SpriteAnimator animator, PlayerLifeStateComponent life, PlayerDash dash, PlayerAiming aiming, IPlayerInputReader reader, Rigidbody2D body)
        {
            _animator = animator;
            if (_animator != null) _animator.SelfTicking = false;
            _life = life;
            _dash = dash;
            _aiming = aiming;
            _reader = reader;
            _body = body;
            _lastLife = life != null ? life.State : PlayerLifeState.Alive;
        }

        private void Awake()
        {
            if (_life == null) _life = GetComponent<PlayerLifeStateComponent>();
            if (_dash == null) _dash = GetComponent<PlayerDash>();
            if (_aiming == null) _aiming = GetComponent<PlayerAiming>();
            if (_body == null) _body = GetComponent<Rigidbody2D>();
            if (_reader == null) _reader = GetComponent<PlayerInput>()?.Reader;
            if (_animator == null) _animator = GetComponentInChildren<SpriteAnimator>();
            if (_animator != null) _animator.SelfTicking = false;
        }

        /// <summary>Pure mapping (tests): priority Death > Downed > Get Up > Dash > Walk > Idle.</summary>
        public static PlayerAnimState Resolve(in PlayerAnimInputs inputs)
        {
            if (inputs.Life == PlayerLifeState.Dead) return PlayerAnimState.Death;
            if (inputs.Life == PlayerLifeState.Downed) return PlayerAnimState.Downed;
            if (inputs.SecondsSinceRevive < GetUpSeconds) return PlayerAnimState.GetUp;
            if (inputs.IsDashing) return PlayerAnimState.Dash;
            return inputs.IsMoving ? PlayerAnimState.Walk : PlayerAnimState.Idle;
        }

        public PlayerAnimInputs Sample(float deltaTime)
        {
            var life = _life != null ? _life.State : PlayerLifeState.Alive;
            if (_lastLife == PlayerLifeState.Downed && life == PlayerLifeState.Alive) _sinceRevive = 0f;
            else if (_sinceRevive < float.MaxValue) _sinceRevive += deltaTime;
            _lastLife = life;
            var moving = (_reader != null && _reader.Move.sqrMagnitude > 0.01f) || (_body != null && _body.linearVelocity.sqrMagnitude > 0.01f);
            var facing = _aiming != null ? _aiming.BodyFacing : Facing;
            return new PlayerAnimInputs(life, _dash != null && _dash.IsDashing, moving, facing, _sinceRevive);
        }

        public void Tick(float deltaTime)
        {
            var inputs = Sample(deltaTime);
            State = Resolve(inputs);
            Facing = inputs.Facing;
            _animator?.Play(State.ToString(), Facing);
            _animator?.Tick(deltaTime);
        }

        private void Update() => Tick(Time.deltaTime);
    }
}
