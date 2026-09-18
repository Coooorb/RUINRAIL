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

        /// <summary>Changes the maximum without refilling; current health is clamped and never dropped below 1 while alive.</summary>
        public void ResizeMaxHealth(int maxHealth)
        {
            _maxHealth = Mathf.Max(1, maxHealth);
            if (IsAlive)
            {
                CurrentHealth = Mathf.Clamp(CurrentHealth, 1, _maxHealth);
            }
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

            // 82: only the host (or a solo game) applies damage; clients render replicated health.
            if (!DamageAuthority.LocalIsAuthoritative)
            {
                return false;
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

            // 82: healing is decided by the host as well; a client only receives replicated values.
            if (!DamageAuthority.LocalIsAuthoritative)
            {
                return false;
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
