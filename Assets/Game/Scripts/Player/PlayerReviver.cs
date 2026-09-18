using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The reviver side (84 Standard Revive): a living teammate holds Interact near a Downed player for the channel
    /// duration. Moving out of range, attacking (fire/special input), dashing or taking damage cancels; a reviver who
    /// can no longer act (Downed/Dead) cannot continue. No consumable, skill or class is involved. Runs on the
    /// authority only; the host reads the remote owner's held Interact from its intents.
    /// </summary>
    public sealed class PlayerReviver : MonoBehaviour
    {
        [SerializeField] private PlayerBalanceConfig _balanceConfig;

        private IPlayerInputReader _inputReader;
        private PlayerLifeStateComponent _life;
        private HealthComponent _health;
        private PlayerDash _dash;
        private ReviveArbiter _arbiter;
        private ReviveChannel _channel;
        private ReviveCancelReason _pendingCancel;

        public PlayerBalanceConfig Balance => _balanceConfig;
        public ReviveChannel Channel => _channel;
        public bool IsReviving => _channel != null;
        public float ChannelSeconds => _balanceConfig != null ? _balanceConfig.ReviveChannelSeconds : 4f;
        public float RangeTiles => _balanceConfig != null ? _balanceConfig.ReviveRangeTiles : 1.5f;
        /// <summary>Alive players only: Downed/Dead teammates cannot revive anyone (84).</summary>
        public bool CanRevive => _life == null || _life.CanAct;
        public ReviveCancelReason LastCancelReason { get; private set; }

        public void SetInputReader(IPlayerInputReader reader) => _inputReader = reader;
        public void SetBalanceConfig(PlayerBalanceConfig config) => _balanceConfig = config;
        public void SetArbiter(ReviveArbiter arbiter) => _arbiter = arbiter;

        private void Awake()
        {
            _life = GetComponent<PlayerLifeStateComponent>();
            _health = GetComponent<HealthComponent>();
            _dash = GetComponent<PlayerDash>();
            if (_inputReader == null) _inputReader = GetComponent<PlayerInput>()?.Reader;
            if (_health != null) _health.Damaged += OnDamaged;
            if (_dash != null) _dash.DashStarted += OnDashStarted;
        }

        private void OnDestroy()
        {
            if (_health != null) _health.Damaged -= OnDamaged;
            if (_dash != null) _dash.DashStarted -= OnDashStarted;
            CancelChannel(ReviveCancelReason.ReviverCannotAct);
        }

        private ReviveArbiter Arbiter => _arbiter ?? (_life != null && _life.Roster != null ? _life.Roster.Revives : null);

        /// <summary>The closest Downed teammate in range, or null.</summary>
        public PlayerLifeStateComponent FindDownedTeammateInRange()
        {
            var roster = _life != null ? _life.Roster : null;
            if (roster == null) return null;
            PlayerLifeStateComponent best = null;
            var bestDistance = float.MaxValue;
            foreach (var member in roster.Members)
            {
                if (member == null || member == _life || !member.IsDowned) continue;
                var distance = Vector2.Distance(member.transform.position, transform.position);
                if (distance <= RangeTiles && distance < bestDistance)
                {
                    best = member;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private void OnDamaged(int amount) => _pendingCancel = ReviveCancelReason.TookDamage;
        private void OnDashStarted(PlayerDash dash, Vector2 direction) => _pendingCancel = ReviveCancelReason.Dashed;

        /// <summary>Authoritative step: begins, advances or cancels this reviver's channel from the held Interact.</summary>
        public void Step(float deltaTime)
        {
            if (!DamageAuthority.LocalIsAuthoritative) return;
            var arbiter = Arbiter;
            if (arbiter == null) return;
            var held = _inputReader != null && _inputReader.InteractHeld;

            if (_channel == null)
            {
                _pendingCancel = ReviveCancelReason.None;
                if (!held || !CanRevive) return;
                if (_inputReader.FireHeld || _inputReader.SpecialHeld) return;
                var target = FindDownedTeammateInRange();
                if (target == null) return;
                _channel = arbiter.TryBegin(this, target, ChannelSeconds);
                if (_channel == null) return;
                // The hold started this step: this step already counts toward the channel.
                arbiter.Advance(_channel, deltaTime);
                if (!arbiter.IsActive(_channel)) _channel = null;
                return;
            }

            if (!arbiter.IsActive(_channel))
            {
                // Completed or cancelled by the arbiter.
                _channel = null;
                return;
            }

            var reason = ReviveCancelReason.None;
            if (_pendingCancel != ReviveCancelReason.None) reason = _pendingCancel;
            else if (!held) reason = ReviveCancelReason.Released;
            else if (!CanRevive) reason = ReviveCancelReason.ReviverCannotAct;
            else if (_inputReader.FireHeld || _inputReader.SpecialHeld) reason = ReviveCancelReason.Attacked;
            else if (Vector2.Distance(_channel.Target.transform.position, transform.position) > RangeTiles) reason = ReviveCancelReason.MovedAway;

            if (reason != ReviveCancelReason.None)
            {
                CancelChannel(reason);
                return;
            }

            arbiter.Advance(_channel, deltaTime);
            if (!arbiter.IsActive(_channel)) _channel = null;
        }

        private void CancelChannel(ReviveCancelReason reason)
        {
            if (_channel == null) return;
            LastCancelReason = reason;
            Arbiter?.Cancel(_channel, reason);
            _channel = null;
            _pendingCancel = ReviveCancelReason.None;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }
    }
}
