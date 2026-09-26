using System;
using UnityEngine;

namespace RuinRail.Gameplay.Combat
{
    public sealed class HealthComponent : MonoBehaviour, IDamageable
    {
        [SerializeField] private int _maxHealth = 100;

        private IInvulnerabilityState _invulnerabilityState;
        private IInvulnerabilityState[] _composedInvulnerability = System.Array.Empty<IInvulnerabilityState>();
        private IIncomingDamageModifier _incomingDamageModifier;
        // The health state a run of maximum changes started from (the last damage, heal or refill). One equipment change
        // can move the maximum several times (the old item's modifiers leave before the new item's arrive), so every
        // resize is measured against this anchor, never against an intermediate clamp.
        private int _anchorHealth;
        private int _anchorMax;
        private int _resizedHealth = -1;
        private int _resizedMax = -1;

        public int MaxHealth => _maxHealth;
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public event Action<int> Damaged;
        public event Action<int> Healed;
        public event Action Died;

        public void SetMaxHealth(int maxHealth)
        {
            _maxHealth = maxHealth;
            CurrentHealth = _maxHealth;
        }

        public void SetInvulnerabilityState(IInvulnerabilityState invulnerabilityState)
        {
            _invulnerabilityState = invulnerabilityState;
        }

        /// <summary>Damage reduction seam (player stats); null = damage applies unmodified.</summary>
        public void SetIncomingDamageModifier(IIncomingDamageModifier modifier)
        {
            _incomingDamageModifier = modifier;
        }

        /// <summary>
        /// Changes the maximum (equipment, attributes, affixes, passives) without ever healing damage: a player at full
        /// health stays at full health against the new maximum, a damaged player keeps their current HP, and a lower
        /// maximum clamps it (never below 1 while alive). Consecutive resizes with no damage or healing in between are
        /// one change, so an intermediate lower maximum (armor A off before armor B on) neither loses nor grants HP.
        /// </summary>
        public void ResizeMaxHealth(int maxHealth)
        {
            if (CurrentHealth != _resizedHealth || _maxHealth != _resizedMax)
            {
                _anchorHealth = CurrentHealth;
                _anchorMax = _maxHealth;
            }

            _maxHealth = Mathf.Max(1, maxHealth);
            if (IsAlive)
            {
                CurrentHealth = _anchorHealth >= _anchorMax ? _maxHealth : Mathf.Clamp(_anchorHealth, 1, _maxHealth);
            }

            _resizedHealth = CurrentHealth;
            _resizedMax = _maxHealth;
        }

        private void Awake()
        {
            CurrentHealth = _maxHealth;

            RefreshInvulnerabilityStates();
        }

        /// <summary>Re-scans the object for IInvulnerabilityState components (dash iFrames, revive protection, ...).</summary>
        public void RefreshInvulnerabilityStates()
        {
            _composedInvulnerability = GetComponents<IInvulnerabilityState>();
        }

        /// <summary>True while the explicit state or any composed state on the object grants invulnerability.</summary>
        public bool IsInvulnerable
        {
            get
            {
                if (_invulnerabilityState != null && _invulnerabilityState.IsInvulnerable) return true;
                foreach (var state in _composedInvulnerability)
                {
                    if (state != null && state.IsInvulnerable) return true;
                }

                return false;
            }
        }

        public bool TryApplyDamage(DamageRequest request)
        {
            if (!IsAlive || request.Amount <= 0)
            {
                return false;
            }

            // 82: only the host (or a solo game) applies damage; clients render replicated health. A client's own hit
            // on a replicated enemy becomes a request to the host instead of a local change.
            if (!DamageAuthority.LocalIsAuthoritative)
            {
                var relay = DamageAuthority.RemoteDamageRelay;
                return relay != null && relay(this, request);
            }

            if (IsInvulnerable)
            {
                return false;
            }

            var incoming = _incomingDamageModifier != null ? _incomingDamageModifier.ModifyIncomingDamage(request) : request.Amount;
            if (incoming <= 0)
            {
                return false;
            }

            var previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Clamp(CurrentHealth - incoming, 0, _maxHealth);
            var appliedAmount = previousHealth - CurrentHealth;

            if (appliedAmount <= 0)
            {
                return false;
            }

            Damaged?.Invoke(appliedAmount);

            if (CurrentHealth == 0)
            {
                Died?.Invoke();
            }

            return true;
        }

        /// <summary>Applies a replicated authoritative health value on a client (no events beyond HealthChanged-style Healed/Damaged).</summary>
        public void ApplyReplicatedHealth(int current, int max)
        {
            _maxHealth = Mathf.Max(1, max);
            var previous = CurrentHealth;
            CurrentHealth = Mathf.Clamp(current, 0, _maxHealth);
            if (CurrentHealth < previous) Damaged?.Invoke(previous - CurrentHealth);
            else if (CurrentHealth > previous) Healed?.Invoke(CurrentHealth - previous);
            if (previous > 0 && CurrentHealth == 0) Died?.Invoke();
        }

        /// <summary>Authoritative return from 0 HP (84 revive/defibrillator/medical station): sets health without a Died/Healed replay.</summary>
        public bool Revive(int health)
        {
            if (IsAlive || health <= 0 || !DamageAuthority.LocalIsAuthoritative)
            {
                return false;
            }

            CurrentHealth = Mathf.Clamp(health, 1, _maxHealth);
            Healed?.Invoke(CurrentHealth);
            return true;
        }

        public bool Heal(int amount)
        {
            if (!IsAlive || amount <= 0)
            {
                return false;
            }

            // 82: healing is decided by the host as well; a client only receives replicated values (its own heal is
            // forwarded as a request and comes back through the replicated health).
            if (!DamageAuthority.LocalIsAuthoritative)
            {
                var relay = DamageAuthority.RemoteHealRelay;
                return relay != null && relay(this, amount);
            }

            var previousHealth = CurrentHealth;
            CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, _maxHealth);
            var appliedAmount = CurrentHealth - previousHealth;

            if (appliedAmount <= 0)
            {
                return false;
            }

            Healed?.Invoke(appliedAmount);
            return true;
        }
    }
}
