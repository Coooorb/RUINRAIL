using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons.Specials
{
    /// <summary>
    /// Routes the Special input (RMB / LT) to the fixed special of the Legendary weapon the player is <b>holding</b>.
    /// Every mounted Legendary weapon registers its special here; only the one whose weapon is the loadout's active,
    /// live weapon is the <see cref="Special"/> — what the input fires and what the HUD shows. A Legendary in the
    /// backpack is not mounted at all, a holstered one is registered but inactive, and a weapon destroyed by a remount
    /// never counts (its entries are cleared with it). Normal weapons register nothing, so they ignore the input.
    /// Each special's cooldown belongs to its weapon instance and keeps counting while holstered; a special input while
    /// nothing Legendary is held, cooling, or already running does nothing. Running executions (bursts, dashes) are
    /// ticked here; a dash execution owns movement via IMovementOverride.
    /// </summary>
    public sealed class LegendarySpecialController : MonoBehaviour, IMovementOverride
    {
        private sealed class Entry
        {
            public ILegendarySpecial Special;
            public LegendarySpecialState State;
            public IEquippableWeapon Weapon;
            public Func<SpecialContext> ContextFactory;

            /// <summary>The weapon is still mounted (not destroyed by a remount) and is the active, held one.</summary>
            public bool IsHeld => Weapon != null && !(Weapon is UnityEngine.Object o && o == null) && Weapon.IsEquipped;
        }

        private readonly System.Collections.Generic.List<Entry> _entries = new();
        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private ISpecialExecution _running;
        private bool _specialHeldLastFrame;

        private Entry Held
        {
            get
            {
                foreach (var entry in _entries) if (entry.IsHeld) return entry;
                return null;
            }
        }

        /// <summary>The held Legendary weapon's special; null while the held weapon has none (or nothing is held).</summary>
        public ILegendarySpecial Special => Held?.Special;
        /// <summary>Cooldown state of the held weapon's special (HUD overlay); null while the held weapon has none.</summary>
        public LegendarySpecialState State => Held?.State;
        public bool IsRunning => _running != null && !_running.IsComplete;
        public bool IsActive => IsRunning && _running.LocksMovement;
        /// <summary>True while a Legendary weapon with a special is the held weapon.</summary>
        public bool IsWeaponEquipped => Held != null;
        /// <summary>One mounted weapon's own cooldown, held or holstered (diagnostics/tests); null when it has no special.</summary>
        public LegendarySpecialState StateOf(IEquippableWeapon weapon)
        {
            foreach (var entry in _entries) if (ReferenceEquals(entry.Weapon, weapon)) return entry.State;
            return null;
        }

        /// <summary>Specials of the mounted Legendary weapons (held or holstered).</summary>
        public int Registered => _entries.Count;
        public int Activations { get; private set; }

        public event Action<ILegendarySpecial> SpecialFired;

        /// <summary>Registers the special of one mounted Legendary weapon (each weapon keeps its own cooldown).</summary>
        public void Configure(ILegendarySpecial special, IEquippableWeapon weapon, Func<SpecialContext> contextFactory, LegendarySpecialState state = null)
        {
            if (special == null) throw new ArgumentNullException(nameof(special));
            if (weapon == null) throw new ArgumentNullException(nameof(weapon));
            if (contextFactory == null) throw new ArgumentNullException(nameof(contextFactory));
            _entries.RemoveAll(e => ReferenceEquals(e.Weapon, weapon));
            _entries.Add(new Entry { Special = special, Weapon = weapon, ContextFactory = contextFactory, State = state ?? new LegendarySpecialState(special.CooldownSeconds) });
        }

        /// <summary>The weapons were remounted: every registered special goes with the components it belonged to.</summary>
        public void Clear() => _entries.Clear();

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
            foreach (var entry in _entries) entry.State?.Tick(dt);
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

        /// <summary>Attempts the special now (input edge or scripted). Only the held Legendary weapon may fire its own, only when ready.</summary>
        public bool TryActivate()
        {
            var entry = Held;
            if (entry == null || IsRunning || !_actionGate.CanAct(this)) return false;
            if (!entry.State.TryUse()) return false;

            var execution = entry.Special.Begin(entry.ContextFactory());
            if (execution != null && !execution.IsComplete) _running = execution;
            Activations++;
            SpecialFired?.Invoke(entry.Special);
            return true;
        }
    }
}
