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
    /// <summary>Outcome of a same-container slot move (<see cref="ItemSlotContainer.TryMove"/>).</summary>
    public enum SlotMoveResult
    {
        /// <summary>Out of range or nothing in the source slot; the container is untouched.</summary>
        Invalid,
        /// <summary>Source and target are the same slot; nothing changed.</summary>
        Unchanged,
        Moved,
        Swapped,
        /// <summary>The source stack (partly or fully) merged into the same-definition target stack.</summary>
        Merged
    }

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

        /// <summary>
        /// Manual reorder inside this container: the item in <paramref name="from"/> goes to exactly <paramref name="to"/>.
        /// An empty target is a move, a same-definition stackable target merges up to its limit (the remainder stays in
        /// the source slot), anything else swaps the two slots. Slot indices are the player's order — nothing here
        /// compacts or re-sorts, and no instance is created, duplicated or lost.
        /// </summary>
        public SlotMoveResult TryMove(int from, int to)
        {
            if (from < 0 || from >= Capacity || to < 0 || to >= Capacity) return SlotMoveResult.Invalid;
            if (from == to) return SlotMoveResult.Unchanged;
            var source = _slots[from];
            if (source == null) return SlotMoveResult.Invalid;
            var target = _slots[to];
            if (target == null)
            {
                _slots[to] = source;
                _slots[from] = null;
                Changed?.Invoke();
                return SlotMoveResult.Moved;
            }

            var definition = _resolveDefinition(source.DefinitionId);
            if (definition != null && definition.IsStackable && target.DefinitionId == source.DefinitionId)
            {
                var max = MaxStackFor(definition);
                var room = max - target.Quantity;
                if (room > 0)
                {
                    var moved = Mathf.Min(room, source.Quantity);
                    target.SetQuantity(target.Quantity + moved);
                    source.SetQuantity(source.Quantity - moved);
                    if (source.Quantity == 0) _slots[from] = null;
                    Changed?.Invoke();
                    return SlotMoveResult.Merged;
                }
            }

            _slots[to] = source;
            _slots[from] = target;
            Changed?.Invoke();
            return SlotMoveResult.Swapped;
        }

        /// <summary>
        /// Puts <paramref name="replacement"/> into the occupied slot <paramref name="slotIndex"/> and returns the item
        /// that was there, without raising Changed: the caller (an atomic exchange with another container) raises it
        /// once both sides hold their final item. Refuses an empty slot or an instance this container already holds.
        /// </summary>
        internal ItemInstance ExchangeAt(int slotIndex, ItemInstance replacement)
        {
            if (slotIndex < 0 || slotIndex >= Capacity || _slots[slotIndex] == null || replacement == null || Contains(replacement.InstanceId)) return null;
            var previous = _slots[slotIndex];
            _slots[slotIndex] = replacement;
            return previous;
        }

        internal void RaiseChanged() => Changed?.Invoke();

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
