using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Player-specific life-state logic (15: the generic HealthComponent never owns Downed). At 0 HP the authority
    /// decides: solo, or no Alive teammate → Dead; co-op with an Alive teammate → Downed with the 20 s bleedout (84).
    /// Downed players crawl slowly (this component owns the body as a movement override) and are gated out of attack,
    /// dash, items, inventory and interactions through IPlayerActionGate. The bleedout timer only runs on the
    /// authority; clients receive replicated state. Revive is a separate task and enters through ReturnToAlive.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class PlayerLifeStateComponent : MonoBehaviour, IMovementOverride, IPlayerActionGate
    {
        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private HealthComponent _health;
        private Rigidbody2D _body;
        private PlayerMovement _movement;
        private IPlayerInputReader _inputReader;
        private PartyLifeRoster _roster;

        public PlayerLifeState State { get; private set; } = PlayerLifeState.Alive;
        public float BleedoutRemaining { get; private set; }
        public float BleedoutSeconds => _balanceConfig != null ? _balanceConfig.DownedBleedoutSeconds : 20f;
        public float CrawlSpeed => _movement != null && _balanceConfig != null ? _movement.CurrentMoveSpeed * _balanceConfig.DownedCrawlSpeedMultiplier : 0f;
        public bool IsAlive => State == PlayerLifeState.Alive;
        public bool IsDowned => State == PlayerLifeState.Downed;
        public bool IsDead => State == PlayerLifeState.Dead;
        public PartyLifeRoster Roster => _roster;
        /// <summary>Party participant id (session roster / loot participant id); defaults to the object name.</summary>
        public string ParticipantId { get; private set; }

        public void SetParticipantId(string participantId) => ParticipantId = participantId;

        /// <summary>IPlayerActionGate: only Alive players attack, dash, use items, open the inventory or interact.</summary>
        public bool CanAct => State == PlayerLifeState.Alive;
        /// <summary>IMovementOverride: the life state owns the body while Downed (crawl) or Dead (still).</summary>
        public bool IsActive => State != PlayerLifeState.Alive;

        public event Action<PlayerLifeStateComponent, PlayerLifeState, PlayerLifeState> StateChanged;
        public event Action<PlayerLifeStateComponent> Downed;
        public event Action<PlayerLifeStateComponent> Died;

        public void SetBalanceConfig(PlayerBalanceConfig config) => _balanceConfig = config;
        public void SetInputReader(IPlayerInputReader reader) => _inputReader = reader;

        public void SetRoster(PartyLifeRoster roster)
        {
            _roster?.Unregister(this);
            _roster = roster;
            _roster?.Register(this);
        }

        private void Awake()
        {
            _health = GetComponent<HealthComponent>();
            _body = GetComponent<Rigidbody2D>();
            ParticipantId ??= gameObject.name;
            _movement = GetComponent<PlayerMovement>();
            if (_health != null) _health.Died += OnZeroHealth;
            if (_inputReader == null) _inputReader = GetComponent<PlayerInput>()?.Reader;
            _movement?.RefreshMovementOverrides();
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Died -= OnZeroHealth;
            _roster?.Unregister(this);
        }

        private void OnZeroHealth()
        {
            // 82: the host (or solo) decides; a client sees the replicated state instead.
            if (!DamageAuthority.LocalIsAuthoritative || State != PlayerLifeState.Alive) return;
            if (_roster != null && _roster.IsCoop && _roster.AnyTeammateCanAct(this))
            {
                EnterDowned(BleedoutSeconds);
            }
            else
            {
                EnterDead();
            }
        }

        private void EnterDowned(float bleedout)
        {
            BleedoutRemaining = Mathf.Max(0f, bleedout);
            if (_body != null) _body.linearVelocity = Vector2.zero;
            Transition(PlayerLifeState.Downed);
            Downed?.Invoke(this);
        }

        private void EnterDead()
        {
            BleedoutRemaining = 0f;
            if (_body != null) _body.linearVelocity = Vector2.zero;
            Transition(PlayerLifeState.Dead);
            Died?.Invoke(this);
        }

        /// <summary>
        /// Authority-declared death from any state (85: reconnect grace expired). Nothing drops; a Downed bleedout that
        /// was still running simply ends. Idempotent once Dead.
        /// </summary>
        public bool MarkDeadByAuthority(string reason = null)
        {
            if (!DamageAuthority.LocalIsAuthoritative || State == PlayerLifeState.Dead) return false;
            LastDeathReason = reason;
            EnterDead();
            return true;
        }

        public string LastDeathReason { get; private set; }

        /// <summary>Revive/return seam (84 Standard Revive, Defibrillator, Medical Station): back to Alive at the given health.</summary>
        public bool ReturnToAlive(int health)
        {
            if (State == PlayerLifeState.Alive || !DamageAuthority.LocalIsAuthoritative) return false;
            _health?.Revive(Mathf.Max(1, health));
            BleedoutRemaining = 0f;
            Transition(PlayerLifeState.Alive);
            return true;
        }

        /// <summary>Client-side application of the authority's state (no local decisions).</summary>
        public void ApplyReplicatedState(PlayerLifeState state, float bleedoutRemaining)
        {
            BleedoutRemaining = Mathf.Max(0f, bleedoutRemaining);
            if (state == State) return;
            Transition(state);
            if (state == PlayerLifeState.Downed) Downed?.Invoke(this);
            else if (state == PlayerLifeState.Dead) Died?.Invoke(this);
        }

        /// <summary>Authoritative bleedout: reaching zero while still Downed means Dead (84).</summary>
        public void Tick(float deltaTime)
        {
            if (State != PlayerLifeState.Downed || !DamageAuthority.LocalIsAuthoritative) return;
            BleedoutRemaining = Mathf.Max(0f, BleedoutRemaining - Mathf.Max(0f, deltaTime));
            if (BleedoutRemaining <= 0f) EnterDead();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (_body == null) return;
            if (State == PlayerLifeState.Downed)
            {
                var move = _inputReader != null ? Vector2.ClampMagnitude(_inputReader.Move, 1f) : Vector2.zero;
                _body.linearVelocity = move * CrawlSpeed;
            }
            else if (State == PlayerLifeState.Dead)
            {
                _body.linearVelocity = Vector2.zero;
            }
        }

        private void Transition(PlayerLifeState next)
        {
            var previous = State;
            State = next;
            StateChanged?.Invoke(this, previous, next);
        }
    }
}
