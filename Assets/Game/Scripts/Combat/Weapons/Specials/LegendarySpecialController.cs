using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons.Specials
{
    /// <summary>
    /// Routes the Special input (RMB / LT) to the fixed special of one Legendary weapon while that weapon is the active
    /// one. Normal weapons simply have no controller, so they ignore the input. The cooldown belongs to this weapon
    /// instance and keeps counting while holstered; a special input while unequipped, cooling, or already running does
    /// nothing. Running executions (bursts, dashes) are ticked here; a dash execution owns movement via IMovementOverride.
    /// </summary>
    public sealed class LegendarySpecialController : MonoBehaviour, IMovementOverride
    {
        private ILegendarySpecial _special;
        private LegendarySpecialState _state;
        private IEquippableWeapon _weapon;
        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private Func<SpecialContext> _contextFactory;
        private ISpecialExecution _running;
        private bool _specialHeldLastFrame;

        public ILegendarySpecial Special => _special;
        /// <summary>Cooldown state of the configured special (HUD overlay); null until configured.</summary>
        public LegendarySpecialState State => _state;
        public bool IsRunning => _running != null && !_running.IsComplete;
        public bool IsActive => IsRunning && _running.LocksMovement;
        public bool IsWeaponEquipped => _weapon != null && _weapon.IsEquipped;
        public int Activations { get; private set; }

        public event Action<ILegendarySpecial> SpecialFired;

        public void Configure(ILegendarySpecial special, IEquippableWeapon weapon, Func<SpecialContext> contextFactory, LegendarySpecialState state = null)
        {
            _special = special ?? throw new ArgumentNullException(nameof(special));
            _weapon = weapon ?? throw new ArgumentNullException(nameof(weapon));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _state = state ?? new LegendarySpecialState(special.CooldownSeconds);
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            _inputReader = inputReader;
            _specialHeldLastFrame = inputReader != null && inputReader.SpecialHeld;
        }

        private void Awake()
        {
            _inputReader ??= GetComponent<PlayerInput>()?.Reader;
        }

        private void Update()
        {
            var dt = Time.deltaTime;
            _state?.Tick(dt);
            if (_running != null)
            {
                _running.Tick(dt);
                if (_running.IsComplete) _running = null;
            }

            if (_inputReader == null) return;
            var held = _inputReader.SpecialHeld;
            if (held && !_specialHeldLastFrame) TryActivate();
            _specialHeldLastFrame = held;
        }

        /// <summary>Attempts the special now (input edge or scripted). Only the active Legendary weapon may fire it, only when ready.</summary>
        public bool TryActivate()
        {
            if (_special == null || _state == null || !IsWeaponEquipped || IsRunning || !_actionGate.CanAct(this)) return false;
            if (!_state.TryUse()) return false;

            var execution = _special.Begin(_contextFactory());
            if (execution != null && !execution.IsComplete) _running = execution;
            Activations++;
            SpecialFired?.Invoke(_special);
            return true;
        }
    }
}
