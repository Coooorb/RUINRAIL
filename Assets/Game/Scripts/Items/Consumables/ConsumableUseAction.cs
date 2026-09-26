using System;

namespace RuinRail.Gameplay.Items.Consumables
{
    public enum ConsumableUseResult
    {
        Started,
        NothingEquipped,
        EmptyStack,
        AlreadyUsing,
        UnsupportedEffect,
        /// <summary>The effect would do nothing right now (a heal at full HP): no channel, no spend.</summary>
        NoEffect
    }

    /// <summary>
    /// The channelled use action for the Active Consumable slot. Exactly one stack unit is removed per successful use,
    /// at the definition's consumption point; a cancelled OnCompletion use removes nothing; a stack can never go below 0
    /// and an emptied stack leaves the slot. Plain C#, driven by Tick from the player.
    /// </summary>
    public sealed class ConsumableUseAction
    {
        private readonly PlayerInventory _inventory;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly ConsumableEffectRunner _effects;
        private ItemInstance _stack;
        private ConsumableDefinition _definition;
        private float _remaining;
        private bool _consumedOnActivation;

        public ConsumableUseAction(PlayerInventory inventory, Func<string, ItemDefinition> resolveDefinition, ConsumableEffectRunner effects)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }

        public bool IsUsing => _definition != null;
        public ConsumableDefinition Current => _definition;
        public float RemainingSeconds => _remaining;
        public float Progress => _definition == null || _definition.UseTimeSeconds <= 0f ? 0f : 1f - _remaining / _definition.UseTimeSeconds;

        public event Action<ConsumableDefinition> UseStarted;
        public event Action<ConsumableDefinition> UseCompleted;
        public event Action<ConsumableDefinition> UseCancelled;

        public ConsumableUseResult TryUse() => TryUse(_inventory.GetEquipped(EquippedSlot.ActiveConsumable));

        /// <summary>
        /// Uses one explicit stack through this same channel.
        ///
        /// The quick-grenade key needs to throw a grenade the player is carrying without first making it the Active
        /// Consumable, and there must be exactly one place that channels, applies and spends a consumable — so it is
        /// an entry point on this action rather than a second use path. Every rule below is the same one the Active
        /// Consumable obeys: one use at a time, one unit per successful use, spent at the definition's consumption
        /// point, a cancelled OnCompletion use costs nothing, and an emptied stack leaves the inventory.
        /// </summary>
        public ConsumableUseResult TryUse(ItemInstance stack)
        {
            if (IsUsing) return ConsumableUseResult.AlreadyUsing;

            if (stack == null) return ConsumableUseResult.NothingEquipped;
            if (stack.Quantity <= 0)
            {
                Discard(stack);
                return ConsumableUseResult.EmptyStack;
            }

            if (_resolveDefinition(stack.DefinitionId) is not ConsumableDefinition definition) return ConsumableUseResult.UnsupportedEffect;
            if (definition.EffectKind == ConsumableEffectKind.Grenade && !_effects.CanThrowGrenades) return ConsumableUseResult.UnsupportedEffect;
            if (definition.EffectKind == ConsumableEffectKind.Revive && !_effects.CanRequestRevives) return ConsumableUseResult.UnsupportedEffect;
            if (!_effects.WouldHaveEffect(definition)) return ConsumableUseResult.NoEffect;

            _stack = stack;
            _definition = definition;
            _remaining = definition.UseTimeSeconds;
            _consumedOnActivation = false;
            if (definition.ConsumptionPoint == ConsumptionPoint.OnActivation)
            {
                ConsumeOne();
                _consumedOnActivation = true;
            }

            UseStarted?.Invoke(definition);
            if (_remaining <= 0f) Complete();
            return ConsumableUseResult.Started;
        }

        public void Tick(float deltaTime)
        {
            _effects.Tick(deltaTime);
            if (!IsUsing) return;
            _remaining -= deltaTime;
            if (_remaining <= 0f) Complete();
        }

        /// <summary>Interrupts the channel. OnCompletion items cost nothing; OnActivation items were already spent.</summary>
        public bool Cancel()
        {
            if (!IsUsing) return false;
            var definition = _definition;
            Reset();
            UseCancelled?.Invoke(definition);
            return true;
        }

        private void Complete()
        {
            var definition = _definition;
            var applied = _effects.Apply(definition);
            // A unit is only spent for an effect that actually happened (a grenade that could not be thrown costs nothing).
            if (applied && !_consumedOnActivation) ConsumeOne();
            Reset();
            UseCompleted?.Invoke(definition);
        }

        private void ConsumeOne()
        {
            if (_stack == null || _stack.Quantity <= 0) return;
            _stack.SetQuantity(_stack.Quantity - 1);
            if (_stack.Quantity == 0) Discard(_stack);
        }

        /// <summary>Removes an emptied stack from wherever it is held — the Active Consumable slot or a backpack slot.</summary>
        private void Discard(ItemInstance stack)
        {
            if (stack == null) return;
            if (ReferenceEquals(_inventory.GetEquipped(EquippedSlot.ActiveConsumable), stack))
            {
                _inventory.Unequip(EquippedSlot.ActiveConsumable);
                return;
            }

            var slots = _inventory.BackpackSlots;
            for (var i = 0; i < slots.Count; i++)
            {
                if (ReferenceEquals(slots[i], stack)) { _inventory.RemoveFromBackpack(i); return; }
            }
        }

        /// <summary>
        /// The first grenade this inventory can actually throw, in the order a player would reach for one: the Active
        /// Consumable if it already holds grenades, then the backpack left to right. Returns null when there is none,
        /// which is what makes an empty quick-grenade press a silent no-op rather than an error.
        /// </summary>
        public ItemInstance FindQuickGrenade()
        {
            bool IsUsableGrenade(ItemInstance item) =>
                item != null && item.Quantity > 0 &&
                _resolveDefinition(item.DefinitionId) is ConsumableDefinition d && d.EffectKind == ConsumableEffectKind.Grenade;

            var active = _inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            if (IsUsableGrenade(active)) return active;
            var slots = _inventory.BackpackSlots;
            for (var i = 0; i < slots.Count; i++) if (IsUsableGrenade(slots[i])) return slots[i];
            return null;
        }

        private void Reset()
        {
            _stack = null;
            _definition = null;
            _remaining = 0f;
            _consumedOnActivation = false;
        }
    }
}
