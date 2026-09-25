using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// The body-side producers of the player's <see cref="PlayerCombatEvents"/> hub (items/28 + 34): dash start and end,
    /// the movement state, health changes and weapon swaps. The hub existed and its Legendary passives subscribed, but
    /// none of these was ever raised in the shipped game (tests raised them by hand), so Runner's Watch / Scout Rig,
    /// Dash Capacitor, Field Scope, Second Wind, Last Stand and Quickdraw Holster were inert.
    ///
    /// One instance per composed rig body, bound by <see cref="PlayerRig"/>; <see cref="Unbind"/> runs on reattach and on
    /// destroy, so a reconnect or a remount never leaves a second subscription behind.
    /// </summary>
    public sealed class PlayerCombatEventProducers : MonoBehaviour
    {
        private PlayerCombatEvents _events;
        private IPlayerInputReader _reader;
        private PlayerDash _dash;
        private HealthComponent _health;
        private WeaponLoadout _loadout;
        private bool _moving;

        /// <summary>Bindings live right now (diagnostics / tests).</summary>
        public bool IsBound => _events != null;

        public void Bind(PlayerCombatEvents events, IPlayerInputReader reader, WeaponLoadout loadout)
        {
            Unbind();
            if (events == null) return;
            _events = events;
            _reader = reader;
            _dash = GetComponent<PlayerDash>();
            _health = GetComponent<HealthComponent>();
            _loadout = loadout;
            _moving = false;
            if (_dash != null) { _dash.DashStarted += OnDashStarted; _dash.DashEnded += OnDashEnded; }
            if (_health != null) { _health.Damaged += OnHealth; _health.Healed += OnHealth; }
            if (_loadout != null) _loadout.ActiveSlotChanged += OnSlotChanged;
        }

        public void Unbind()
        {
            if (_dash != null) { _dash.DashStarted -= OnDashStarted; _dash.DashEnded -= OnDashEnded; }
            if (_health != null) { _health.Damaged -= OnHealth; _health.Healed -= OnHealth; }
            if (_loadout != null) _loadout.ActiveSlotChanged -= OnSlotChanged;
            _dash = null;
            _health = null;
            _loadout = null;
            _reader = null;
            _events = null;
        }

        private void OnDashStarted(PlayerDash dash, Vector2 direction) => _events?.RaiseDashed();
        private void OnDashEnded(PlayerDash dash) => _events?.RaiseDashEnded();
        private void OnHealth(int amount) { if (_health != null) _events?.RaiseHealthChanged(_health.CurrentHealth, _health.MaxHealth); }
        private void OnSlotChanged(WeaponSlot slot) => _events?.RaiseWeaponSwapped();

        private void FixedUpdate()
        {
            if (_events == null) return;
            // Character movement for Field Scope: move intent or a dash in progress; a standing player is still.
            var moving = (_reader != null && _reader.Move.sqrMagnitude > 0.0001f) || (_dash != null && _dash.IsDashing);
            if (moving == _moving) return;
            _moving = moving;
            _events.RaiseMovementStateChanged(moving);
        }

        private void OnDestroy() => Unbind();
    }
}
