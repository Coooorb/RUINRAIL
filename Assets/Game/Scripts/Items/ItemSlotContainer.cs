using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Fixed-capacity slot container with the shared stacking rules (20_INVENTORY_SYSTEM): equipment = one slot,
    /// stackables merge into same-definition stacks up to their limit (ammo limits from AmmoBalanceConfig), coins never
    /// occupy slots. Adds are atomic: an item that does not fully fit is rejected without partial insertion.
    /// Backpack and Storage are both instances of this container.
    /// </summary>
    public sealed class ItemSlotContainer : IItemContainer
    {
        private ItemInstance[] _slots;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly AmmoBalanceConfig _ammoBalance;

        public ItemSlotContainer(string containerId, int capacity, Func<string, ItemDefinition> resolveDefinition, AmmoBalanceConfig ammoBalance)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            ContainerId = containerId;
            _slots = new ItemInstance[capacity];
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _ammoBalance = ammoBalance;
        }

        public string ContainerId { get; }
        public int Capacity => _slots.Length;
        public IReadOnlyList<ItemInstance> Slots => _slots;
        public IEnumerable<ItemInstance> Items => _slots.Where(s => s != null);
        public int OccupiedSlots => _slots.Count(s => s != null);
        public int FreeSlots => Capacity - OccupiedSlots;

        public event Action Changed;

        private Func<int> _ammoCapacityBonusPercent;

        /// <summary>Narrow hook for the Ammo Pouch intrinsic (30_ACCESSORY_CATALOG): percent added to every ammo stack limit.</summary>
        public void SetAmmoCapacityBonusProvider(Func<int> bonusPercent)
        {
            _ammoCapacityBonusPercent = bonusPercent;
        }

        public bool Contains(string instanceId) => _slots.Any(s => s != null && s.InstanceId == instanceId);

        public ItemInstance Find(string instanceId) => _slots.FirstOrDefault(s => s != null && s.InstanceId == instanceId);

        public int MaxStackFor(ItemDefinition definition)
        {
            if (definition == null || !definition.IsStackable)
            {
                return 1;
            }

            if (definition is AmmoItemDefinition ammo && _ammoBalance != null)
            {
                var bonus = _ammoCapacityBonusPercent?.Invoke() ?? 0;
                return Mathf.Max(1, Mathf.RoundToInt(_ammoBalance.GetStackLimit(ammo.AmmoType) * (100 + Mathf.Max(0, bonus)) / 100f));
            }

            return definition.MaxStack;
        }

        public bool CanAccept(ItemInstance item) => CanAdd(item);

        public bool CanAdd(ItemInstance item)
        {
            if (item == null || Contains(item.InstanceId))
            {
                return false;
            }

            var definition = _resolveDefinition(item.DefinitionId);
            if (definition == null)
            {
                return false;
            }

            if (!definition.IsStackable)
            {
                return FindEmptySlot() >= 0;
            }

            return item.Quantity > 0 && item.Quantity <= CapacityForStack(definition.Id, MaxStackFor(definition));
        }

        public bool TryAdd(ItemInstance item)
        {
            if (!CanAdd(item))
            {
                return false;
            }

            var definition = _resolveDefinition(item.DefinitionId);
            if (!definition.IsStackable)
            {
                _slots[FindEmptySlot()] = item;
                Changed?.Invoke();
                return true;
            }

            var maxStack = MaxStackFor(definition);
            var remaining = item.Quantity;
            foreach (var stack in _slots.Where(s => s != null && s.DefinitionId == definition.Id && s.Quantity < maxStack))
            {
                var moved = Mathf.Min(maxStack - stack.Quantity, remaining);
                stack.SetQuantity(stack.Quantity + moved);
                remaining -= moved;
                if (remaining == 0) break;
            }

            while (remaining > 0)
            {
                var amount = Mathf.Min(maxStack, remaining);
                _slots[FindEmptySlot()] = new ItemInstance(definition.Id, amount, item.Rarity) { IsAtRisk = item.IsAtRisk };
                remaining -= amount;
            }

            // The source stack has been fully absorbed into this container's stacks.
            item.SetQuantity(0);
            Changed?.Invoke();
            return true;
        }

        public ItemInstance RemoveAt(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= Capacity || _slots[slotIndex] == null)
            {
                return null;
            }

            var item = _slots[slotIndex];
            _slots[slotIndex] = null;
            Changed?.Invoke();
            return item;
        }

        public ItemInstance TryRemove(string instanceId)
        {
            for (var i = 0; i < Capacity; i++)
            {
                if (_slots[i] != null && _slots[i].InstanceId == instanceId)
                {
                    return RemoveAt(i);
                }
            }

            return null;
        }

        public int CountOf(string definitionId) => _slots.Where(s => s != null && s.DefinitionId == definitionId).Sum(s => s.Quantity);

        /// <summary>Removes up to <paramref name="amount"/> units across stacks of the definition; returns the amount removed.</summary>
        public int ConsumeQuantity(string definitionId, int amount)
        {
            if (amount <= 0) return 0;
            var remaining = Mathf.Min(amount, CountOf(definitionId));
            var consumed = 0;
            for (var i = 0; i < Capacity && remaining > 0; i++)
            {
                var stack = _slots[i];
                if (stack == null || stack.DefinitionId != definitionId) continue;
                var take = Mathf.Min(stack.Quantity, remaining);
                stack.SetQuantity(stack.Quantity - take);
                remaining -= take;
                consumed += take;
                if (stack.Quantity == 0) _slots[i] = null;
            }

            if (consumed > 0) Changed?.Invoke();
            return consumed;
        }

        public int CapacityForStack(string definitionId, int maxStack)
        {
            var roomInExisting = _slots.Where(s => s != null && s.DefinitionId == definitionId).Sum(s => maxStack - s.Quantity);
            return roomInExisting + _slots.Count(s => s == null) * maxStack;
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Changed?.Invoke();
        }

        /// <summary>Grows the container (upgrades only ever add slots): every existing item keeps its slot index. Shrinking is refused.</summary>
        public bool Expand(int newCapacity)
        {
            if (newCapacity <= Capacity) return false;
            Array.Resize(ref _slots, newCapacity);
            Changed?.Invoke();
            return true;
        }

        public InventorySnapshot.Entry[] ToEntries()
        {
            return _slots.Select((item, index) => (item, index))
                .Where(t => t.item != null)
                .Select(t => new InventorySnapshot.Entry { Slot = t.index, Item = t.item.ToSnapshot() })
                .ToArray();
        }

        public void RestoreFromEntries(InventorySnapshot.Entry[] entries)
        {
            Array.Clear(_slots, 0, _slots.Length);
            foreach (var entry in entries ?? Array.Empty<InventorySnapshot.Entry>())
            {
                if (entry.Slot >= 0 && entry.Slot < Capacity && entry.Item != null)
                {
                    _slots[entry.Slot] = ItemInstance.FromSnapshot(entry.Item);
                }
            }

            Changed?.Invoke();
        }

        private int FindEmptySlot()
        {
            for (var i = 0; i < Capacity; i++)
            {
                if (_slots[i] == null) return i;
            }

            return -1;
        }
    }
}
