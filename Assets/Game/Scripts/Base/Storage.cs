using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Base
{
    /// <summary>Persistence DTO for the Shelter storage.</summary>
    [Serializable]
    public sealed class StorageSnapshot
    {
        public int Capacity;
        public InventorySnapshot.Entry[] Slots;
    }

    /// <summary>
    /// Permanently safe Shelter storage (base/71_STORAGE): 60 item slots at base level, same stacking rules as the
    /// backpack, moved only through ItemTransferService. Storage never accepts an item that is still at risk — only the
    /// extraction transaction clears that flag, so referencing Storage mid-expedition can never launder loot.
    /// </summary>
    public sealed class Storage : IItemContainer
    {
        public const int BaseCapacity = 60;

        private readonly ItemSlotContainer _slots;

        public Storage(Func<string, ItemDefinition> resolveDefinition, AmmoBalanceConfig ammoBalance)
            : this(BaseCapacity, resolveDefinition, ammoBalance)
        {
        }

        private Storage(int capacity, Func<string, ItemDefinition> resolveDefinition, AmmoBalanceConfig ammoBalance)
        {
            _slots = new ItemSlotContainer("storage", capacity, resolveDefinition, ammoBalance);
            _slots.Changed += () => Changed?.Invoke();
        }

        public string ContainerId => _slots.ContainerId;
        public int Capacity => _slots.Capacity;
        public IReadOnlyList<ItemInstance> Slots => _slots.Slots;
        public IEnumerable<ItemInstance> Items => _slots.Items;
        public int OccupiedSlots => _slots.OccupiedSlots;
        public int FreeSlots => _slots.FreeSlots;

        public event Action Changed;

        public ItemInstance Find(string instanceId) => _slots.Find(instanceId);

        public bool Contains(string instanceId) => _slots.Contains(instanceId);

        public bool CanAccept(ItemInstance item) => item != null && !item.IsAtRisk && _slots.CanAdd(item);

        public bool TryAdd(ItemInstance item) => CanAccept(item) && _slots.TryAdd(item);

        public ItemInstance TryRemove(string instanceId) => _slots.TryRemove(instanceId);

        public int CountOf(string definitionId) => _slots.CountOf(definitionId);

        /// <summary>Applies an approved capacity upgrade; never shrinks and never touches items (71: items are never lost).</summary>
        public bool ExpandTo(int capacity) => _slots.Expand(capacity);

        public StorageSnapshot ToSnapshot()
        {
            return new StorageSnapshot { Capacity = Capacity, Slots = _slots.ToEntries() };
        }

        public void RestoreFromSnapshot(StorageSnapshot snapshot)
        {
            _slots.RestoreFromEntries(snapshot?.Slots);
        }

        public static Storage FromSnapshot(StorageSnapshot snapshot, Func<string, ItemDefinition> resolveDefinition, AmmoBalanceConfig ammoBalance)
        {
            var capacity = snapshot != null && snapshot.Capacity > 0 ? snapshot.Capacity : BaseCapacity;
            var storage = new Storage(capacity, resolveDefinition, ammoBalance);
            storage.RestoreFromSnapshot(snapshot);
            return storage;
        }
    }
}
