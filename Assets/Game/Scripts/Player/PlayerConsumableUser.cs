using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Binds the Consumable input to the use action for this player's inventory, health and stats, and ticks channels
    /// and timed buffs. Taking a hit does not interrupt a channel in V1 (41: use-time risk comes from not firing).
    /// </summary>
    public sealed class PlayerConsumableUser : MonoBehaviour
    {
        private IPlayerInputReader _inputReader;
        private ConsumableUseAction _useAction;
        private ConsumableEffectRunner _effects;
        private readonly ActionGateLookup _actionGate = new();

        public ConsumableUseAction UseAction => _useAction;
        public ConsumableEffectRunner Effects => _effects;

        public void Configure(PlayerInventory inventory, Func<string, ItemDefinition> resolveDefinition, PlayerStats stats, PlayerCombatEvents events, HealthComponent health, GrenadeLauncher launcher = null, Func<Vector2> aimDirection = null, Func<ReviveRequest, bool> requestRevive = null)
        {
            Func<GrenadeData, bool> throwGrenade = null;
            if (launcher != null)
            {
                throwGrenade = data => launcher.Throw(data, aimDirection?.Invoke() ?? Vector2.right) != null;
            }

            _effects = new ConsumableEffectRunner(new ConsumableTargets(stats, events, amount =>
            {
                var before = health.CurrentHealth;
                health.Heal(amount);
                return health.CurrentHealth - before;
            }, throwGrenade, requestRevive, () => health.CurrentHealth < health.MaxHealth));
            _useAction = new ConsumableUseAction(inventory, resolveDefinition, _effects);
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            if (_inputReader != null) { _inputReader.ConsumableUsed -= HandleConsumableUsed; _inputReader.QuickGrenadeUsed -= HandleQuickGrenade; }
            _inputReader = inputReader;
            if (_inputReader != null) { _inputReader.ConsumableUsed += HandleConsumableUsed; _inputReader.QuickGrenadeUsed += HandleQuickGrenade; }
        }

        private void Awake()
        {
            if (_inputReader == null) SetInputReader(GetComponent<PlayerInput>()?.Reader);
        }

        private void OnDestroy()
        {
            SetInputReader(null);
        }

        private void HandleConsumableUsed()
        {
            TryUse();
        }

        private void HandleQuickGrenade()
        {
            TryQuickGrenade();
        }

        /// <summary>Uses the selected consumable unless the life state forbids it (84: Downed/Dead cannot use items).</summary>
        public bool TryUse()
        {
            if (_useAction == null || !_actionGate.CanAct(this)) return false;
            return _useAction.TryUse() == ConsumableUseResult.Started;
        }

        /// <summary>
        /// Throws the first grenade the player is carrying without changing the Active Consumable slot.
        ///
        /// It is the same channel, the same effect runner and the same one-unit spend as any other consumable use —
        /// the key only picks the stack. Nothing happens, and nothing is spent, when there is no grenade, when a use
        /// is already running, or when the life state forbids acting; a window holding gameplay input never reaches
        /// here at all, because the reader does not raise the event while the gate is held.
        /// </summary>
        public bool TryQuickGrenade()
        {
            if (_useAction == null || !_actionGate.CanAct(this)) return false;
            var grenade = _useAction.FindQuickGrenade();
            if (grenade == null) return false;
            return _useAction.TryUse(grenade) == ConsumableUseResult.Started;
        }

        private void Update()
        {
            _useAction?.Tick(Time.deltaTime);
        }
    }
}
