using System;
using System.Collections.Generic;

namespace RuinRail.Gameplay.Items
{
    public sealed class EquippedSlotContainer : IItemContainer
    {
        private readonly PlayerInventory _inventory;
        private readonly EquippedSlot _slot;

        public EquippedSlotContainer(PlayerInventory inventory, EquippedSlot slot)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _slot = slot;
        }

        public string ContainerId => $"equipped:{_slot}";

        public IEnumerable<ItemInstance> Items
        {
            get
            {
                var item = _inventory.GetEquipped(_slot);
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        public ItemInstance Find(string instanceId)
        {
            var item = _inventory.GetEquipped(_slot);
            return item != null && item.InstanceId == instanceId ? item : null;
        }

        public bool CanAccept(ItemInstance item)
        {
            return item != null && _inventory.CanEquip(item, _slot, ignoreCurrentOwnership: true);
        }

        public bool TryAdd(ItemInstance item)
        {
            return _inventory.TryEquip(item, _slot);
        }

        public ItemInstance TryRemove(string instanceId)
        {
            return Find(instanceId) == null ? null : _inventory.Unequip(_slot);
        }
    }
}
