using RuinRail.Core.Input;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovement : MonoBehaviour
    {
        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private Rigidbody2D _rigidbody2D;
        private IPlayerInputReader _inputReader;
        private IMovementOverride _movementOverride;
        private IMovementOverride[] _discoveredOverrides = System.Array.Empty<IMovementOverride>();
        private IPlayerStatsProvider _stats;

        /// <summary>Final speed = config speed x capped Movement Speed multiplier (1 when no stats are wired).</summary>
        public float CurrentMoveSpeed => _balanceConfig == null ? 0f : _balanceConfig.MoveSpeed * (_stats?.GetMultiplier(StatId.MovementSpeed) ?? 1f);

        public void SetStats(IPlayerStatsProvider stats)
        {
            _stats = stats;
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            _inputReader = inputReader;
        }

        public void SetBalanceConfig(PlayerBalanceConfig balanceConfig)
        {
            _balanceConfig = balanceConfig;
        }

        public void SetMovementOverride(IMovementOverride movementOverride)
        {
            _movementOverride = movementOverride;
        }

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _rigidbody2D.gravityScale = 0f;
            _rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (_inputReader == null)
            {
                _inputReader = GetComponent<PlayerInput>()?.Reader;
            }

            RefreshMovementOverrides();
        }

        /// <summary>Re-scans the object for IMovementOverride components (call after composing dash/impact/special components at runtime).</summary>
        public void RefreshMovementOverrides()
        {
            // Every override on the object (dash, stagger/knockback, legendary dash specials) can take the body; an explicit one is honoured too.
            _discoveredOverrides = GetComponents<IMovementOverride>();
        }

        /// <summary>True while any movement override (dash, stagger, knockback) owns the body.</summary>
        public bool IsOverridden
        {
            get
            {
                if (_movementOverride != null && _movementOverride.IsActive) return true;
                foreach (var o in _discoveredOverrides)
                {
                    if (o != null && o.IsActive) return true;
                }

                return false;
            }
        }

        private void FixedUpdate()
        {
            if (_inputReader == null || _balanceConfig == null)
            {
                return;
            }

            if (IsOverridden)
            {
                return;
            }

            var moveInput = Vector2.ClampMagnitude(_inputReader.Move, 1f);
            _rigidbody2D.linearVelocity = moveInput * CurrentMoveSpeed;
        }
    }
}
