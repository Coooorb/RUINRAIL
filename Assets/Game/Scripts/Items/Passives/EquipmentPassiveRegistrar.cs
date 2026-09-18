using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items.Accessories;
using RuinRail.Gameplay.Items.Armor;

namespace RuinRail.Gameplay.Items.Passives
{
    /// <summary>Resolves a Legendary mechanic id to a passive across the armor and accessory catalogs.</summary>
    public static class EquipmentPassiveFactory
    {
        public static EquipmentPassive Create(string mechanicId)
        {
            return (EquipmentPassive)ArmorPassiveFactory.Create(mechanicId) ?? AccessoryPassiveFactory.Create(mechanicId);
        }
    }

    /// <summary>
    /// Attaches the fixed Legendary passive of the armor and accessory slots while Legendary gear is equipped and
    /// detaches it on unequip. Legendary armor/accessories never get an RMB special — only this passive.
    /// Drive Tick from the player.
    /// </summary>
    public sealed class EquipmentPassiveRegistrar : IDisposable
    {
        private static readonly EquippedSlot[] PassiveSlots = { EquippedSlot.Armor, EquippedSlot.Accessory };

        private readonly PlayerInventory _inventory;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly PassiveContext _context;
        private readonly Dictionary<EquippedSlot, EquipmentPassive> _active = new();

        public EquipmentPassiveRegistrar(PlayerInventory inventory, Func<string, ItemDefinition> resolveDefinition, PassiveContext context)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _inventory.EquippedChanged += HandleEquippedChanged;
            foreach (var slot in PassiveSlots)
            {
                HandleEquippedChanged(slot, _inventory.GetEquipped(slot));
            }
        }

        public EquipmentPassive ArmorPassive => GetActive(EquippedSlot.Armor);
        public EquipmentPassive AccessoryPassive => GetActive(EquippedSlot.Accessory);
        public IReadOnlyDictionary<EquippedSlot, EquipmentPassive> Active => _active;

        public EquipmentPassive GetActive(EquippedSlot slot) => _active.TryGetValue(slot, out var p) ? p : null;

        public void Tick(float deltaTime)
        {
            foreach (var passive in _active.Values) passive.Tick(deltaTime);
        }

        private void HandleEquippedChanged(EquippedSlot slot, ItemInstance item)
        {
            if (Array.IndexOf(PassiveSlots, slot) < 0) return;

            if (_active.TryGetValue(slot, out var previous))
            {
                previous.Detach();
                _active.Remove(slot);
            }

            if (item == null || !RarityRules.HasLegendaryMechanic(item.Rarity)) return;
            var mechanicId = AffixRollService.GetLegendaryMechanicId(item, _resolveDefinition(item.DefinitionId));
            var passive = EquipmentPassiveFactory.Create(mechanicId);
            if (passive == null) return;

            passive.Attach(_context);
            _active[slot] = passive;
        }

        public void Dispose()
        {
            _inventory.EquippedChanged -= HandleEquippedChanged;
            foreach (var passive in _active.Values) passive.Detach();
            _active.Clear();
        }
    }
}
