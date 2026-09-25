using RuinRail.Core.Input;
using RuinRail.Gameplay.Stats;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerDash : MonoBehaviour, IMovementOverride, IInvulnerabilityState
    {
        private const float MinMeaningfulInputSqrMagnitude = 0.0001f;

        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private Rigidbody2D _rigidbody2D;
        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();

        private float _dashTimeRemaining;
        private float _iFrameTimeRemaining;
        private float _cooldownTimeRemaining;
        private Vector2 _dashDirection;

        public bool IsDashing { get; private set; }
        public bool IsInvulnerable { get; private set; }
        public bool IsActive => IsDashing;
        public Vector2 DashDirection => _dashDirection;

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            AttachInputReader(inputReader);
        }

        public void SetBalanceConfig(PlayerBalanceConfig balanceConfig)
        {
            _balanceConfig = balanceConfig;
        }

        private IPlayerStatsProvider _stats;

        public void SetStats(IPlayerStatsProvider stats)
        {
            _stats = stats;
        }

        /// <summary>Approved 1.4706 s cooldown (design update 2026-09-17) × (1 − capped Dash Cooldown Reduction); the duration (0.18s) is never modified.</summary>
        public float CurrentDashCooldown => _balanceConfig == null ? 0f : _balanceConfig.DashCooldown * (_stats?.GetReductionFactor(StatId.DashCooldownReduction) ?? 1f);

        public float CooldownRemaining => _cooldownTimeRemaining;

        /// <summary>Clears the current cooldown (Second Wind passive); never interrupts an active dash.</summary>
        public void ResetCooldown()
        {
            _cooldownTimeRemaining = 0f;
        }

        /// <summary>Dash Distance bonus scales speed over the fixed 0.18s duration, so distance grows without changing the timing.</summary>
        public float CurrentDashSpeed => _balanceConfig == null ? 0f : _balanceConfig.DashSpeed * (_stats?.GetMultiplier(StatId.DashDistance) ?? 1f);

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();

            if (_inputReader == null)
            {
                AttachInputReader(GetComponent<PlayerInput>()?.Reader);
            }
        }

        private void OnDestroy()
        {
            AttachInputReader(null);
        }

        private void AttachInputReader(IPlayerInputReader inputReader)
        {
            if (_inputReader != null)
            {
                _inputReader.Dash -= HandleDashPressed;
            }

            _inputReader = inputReader;

            if (_inputReader != null)
            {
                _inputReader.Dash += HandleDashPressed;
            }
        }

        /// <summary>Number of dashes started (local press or authoritative start); one accepted request = one increment.</summary>
        public int DashesStarted { get; private set; }

        public event System.Action<PlayerDash, Vector2> DashStarted;

        /// <summary>The dash reached its endpoint (Dash Capacitor's shockwave point).</summary>
        public event System.Action<PlayerDash> DashEnded;

        /// <summary>True when a dash could start now (not dashing, cooldown elapsed, configured).</summary>
        public bool CanDash => _balanceConfig != null && !IsDashing && _cooldownTimeRemaining <= 0f;

        private void HandleDashPressed()
        {
            if (_inputReader == null) return;
            TryStartDash(_inputReader.Move);
        }

        /// <summary>
        /// Starts a dash along a direction. The local press path and the host-validated network path both end here,
        /// so the 0.18 s movement / 0.10 s iFrame / 1.4706 s cooldown invariants have exactly one implementation.
        /// </summary>
        public bool TryStartDash(Vector2 direction)
        {
            if (_balanceConfig == null)
            {
                return false;
            }

            if (IsDashing || _cooldownTimeRemaining > 0f)
            {
                return false;
            }

            // 84: Downed/Dead players never dash (local press and host-validated network path alike).
            if (!_actionGate.CanAct(this))
            {
                return false;
            }

            if (direction.sqrMagnitude < MinMeaningfulInputSqrMagnitude)
            {
                return false;
            }

            _dashDirection = direction.normalized;
            _dashTimeRemaining = _balanceConfig.DashDuration;
            _cooldownTimeRemaining = CurrentDashCooldown;
            _iFrameTimeRemaining = _balanceConfig.DashIFrameDuration;

            IsDashing = true;
            IsInvulnerable = _iFrameTimeRemaining > 0f;
            DashesStarted++;
            DashStarted?.Invoke(this, _dashDirection);

            return true;
        }

        private void FixedUpdate()
        {
            var deltaTime = Time.fixedDeltaTime;

            if (IsDashing)
            {
                _rigidbody2D.linearVelocity = _dashDirection * CurrentDashSpeed;

                _dashTimeRemaining -= deltaTime;
                if (_dashTimeRemaining <= 0f)
                {
                    _dashTimeRemaining = 0f;
                    IsDashing = false;
                    DashEnded?.Invoke(this);
                }
            }

            if (_iFrameTimeRemaining > 0f)
            {
                _iFrameTimeRemaining -= deltaTime;
                if (_iFrameTimeRemaining <= 0f)
                {
                    _iFrameTimeRemaining = 0f;
                    IsInvulnerable = false;
                }
            }

            if (_cooldownTimeRemaining > 0f)
            {
                _cooldownTimeRemaining -= deltaTime;
                if (_cooldownTimeRemaining < 0f)
                {
                    _cooldownTimeRemaining = 0f;
                }
            }
        }
    }
}
