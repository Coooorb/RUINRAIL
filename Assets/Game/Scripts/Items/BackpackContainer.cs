using System;
using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Gameplay.Items
{
    public sealed class BackpackContainer : IItemContainer
    {
        private readonly PlayerInventory _inventory;

        public BackpackContainer(PlayerInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public string ContainerId => "backpack";

        public IEnumerable<ItemInstance> Items => _inventory.BackpackSlots.Where(i => i != null);

        public ItemInstance Find(string instanceId)
        {
            return _inventory.BackpackSlots.FirstOrDefault(i => i != null && i.InstanceId == instanceId);
        }

        public bool CanAccept(ItemInstance item)
        {
            return item != null && _inventory.CanAddToBackpack(item, ignoreCurrentOwnership: true);
        }

        public bool TryAdd(ItemInstance item)
        {
            return _inventory.TryAddToBackpack(item);
        }

        public ItemInstance TryRemove(string instanceId)
        {
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
            {
                var slot = _inventory.BackpackSlots[i];
                if (slot != null && slot.InstanceId == instanceId)
                {
                    return _inventory.RemoveFromBackpack(i);
                }
            }

            return null;
        }
    }
}
