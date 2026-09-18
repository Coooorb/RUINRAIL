using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Stats
{
    /// <summary>
    /// The stat contribution of one equipped item instance: the definition's base modifiers (armor bases, accessory
    /// intrinsics later) plus the instance's persisted affix rolls. One source per instance, keyed by instance id, so
    /// equip/unequip register and remove it exactly once regardless of how often it is repeated.
    /// </summary>
    public sealed class EquippedItemStatSource : IStatModifierSource
    {
        private readonly ItemInstance _item;
        private readonly ItemDefinition _definition;
        private readonly EquipmentAffixSource _affixes;

        public EquippedItemStatSource(ItemInstance item, ItemDefinition definition, Func<string, AffixDefinition> resolveAffix)
        {
            _item = item ?? throw new ArgumentNullException(nameof(item));
            _definition = definition;
            _affixes = resolveAffix != null ? new EquipmentAffixSource(item, resolveAffix) : null;
        }

        public static string SourceIdFor(ItemInstance item) => $"equipped:{item.InstanceId}";

        public string SourceId => SourceIdFor(_item);

        public IEnumerable<StatModifier> GetModifiers()
        {
            var modifiers = _definition is EquipmentItemDefinition equipment ? equipment.BaseModifiers() : Enumerable.Empty<StatModifier>();
            return _affixes != null ? modifiers.Concat(_affixes.GetModifiers()) : modifiers;
        }
    }

    /// <summary>
    /// Keeps PlayerStats in sync with the inventory's equipped slots: one source per equipped instance, added when the
    /// slot fills and removed when it empties. Health invariant: max HP changes never grant or take current HP
    /// (HealthComponent.ResizeMaxHealth clamps only), so re-equipping can never heal.
    /// </summary>
    public sealed class LoadoutStatRegistrar : IDisposable
    {
        private readonly PlayerInventory _inventory;
        private readonly PlayerStats _stats;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly Func<string, AffixDefinition> _resolveAffix;
        private readonly Dictionary<EquippedSlot, string> _sourceBySlot = new();

        public LoadoutStatRegistrar(PlayerInventory inventory, PlayerStats stats, Func<string, ItemDefinition> resolveDefinition, Func<string, AffixDefinition> resolveAffix)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _resolveAffix = resolveAffix;
            _inventory.EquippedChanged += HandleEquippedChanged;
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                HandleEquippedChanged(slot, _inventory.GetEquipped(slot));
            }
        }

        public IReadOnlyDictionary<EquippedSlot, string> RegisteredSources => _sourceBySlot;

        private void HandleEquippedChanged(EquippedSlot slot, ItemInstance item)
        {
            if (_sourceBySlot.TryGetValue(slot, out var previous))
            {
                _stats.RemoveSource(previous);
                _sourceBySlot.Remove(slot);
            }

            if (item == null)
            {
                return;
            }

            var source = new EquippedItemStatSource(item, _resolveDefinition(item.DefinitionId), _resolveAffix);
            _stats.SetSource(source);
            _sourceBySlot[slot] = source.SourceId;
        }

        public void Dispose()
        {
            _inventory.EquippedChanged -= HandleEquippedChanged;
            foreach (var id in _sourceBySlot.Values) _stats.RemoveSource(id);
            _sourceBySlot.Clear();
        }
    }
}
