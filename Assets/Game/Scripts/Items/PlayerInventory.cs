using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// The player's carried state: five equipped slots plus an 8-slot backpack (an ItemSlotContainer sharing the
    /// storage stacking rules). Ammo lives only in backpack stacks, so the inventory is the weapon's IAmmoReserve.
    /// </summary>
    public sealed class PlayerInventory : IAmmoReserve
    {
        public const int BackpackCapacity = 8;

        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly Func<AmmoType, AmmoItemDefinition> _resolveAmmoDefinition;
        private readonly Dictionary<EquippedSlot, ItemInstance> _equipped = new();
        private readonly ItemSlotContainer _backpack;

        public event Action<EquippedSlot, ItemInstance> EquippedChanged;
        public event Action BackpackChanged;

        /// <summary>
        /// True for the carried expedition inventory: every item that enters (found loot, ammo, handoffs) is marked at-risk
        /// (113: found loot is at risk until extraction), so nothing inside it can ever be accepted by Storage mid-run.
        /// </summary>
        public bool MarksIncomingAtRisk { get; set; }

        public PlayerInventory(
            Func<string, ItemDefinition> resolveDefinition,
            Func<AmmoType, AmmoItemDefinition> resolveAmmoDefinition,
            AmmoBalanceConfig ammoBalance)
        {
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _resolveAmmoDefinition = resolveAmmoDefinition ?? throw new ArgumentNullException(nameof(resolveAmmoDefinition));
            _backpack = new ItemSlotContainer("backpack", BackpackCapacity, _resolveDefinition, ammoBalance);
            _backpack.Changed += () => BackpackChanged?.Invoke();
        }

        public static PlayerInventory FromRegistry(ItemDefinitionRegistry registry, AmmoBalanceConfig ammoBalance)
        {
            var ammoByType = registry.Definitions
                .OfType<AmmoItemDefinition>()
                .GroupBy(d => d.AmmoType)
                .ToDictionary(g => g.Key, g => g.First());

            return new PlayerInventory(
                id => registry.TryGet(id, out var definition) ? definition : null,
                type => ammoByType.TryGetValue(type, out var definition) ? definition : null,
                ammoBalance);
        }

        public IReadOnlyList<ItemInstance> BackpackSlots => _backpack.Slots;

        /// <summary>Read-only definition lookup for presentation (HUD/inventory UI); never mutates.</summary>
        public ItemDefinition Resolve(string definitionId) => string.IsNullOrEmpty(definitionId) ? null : _resolveDefinition(definitionId);

        /// <summary>The backpack as a transfer-service container (same object the inventory mutates).</summary>
        public ItemSlotContainer Backpack => _backpack;

        public ItemInstance GetEquipped(EquippedSlot slot)
        {
            return _equipped.TryGetValue(slot, out var item) ? item : null;
        }

        public bool Contains(string instanceId)
        {
            return _equipped.Values.Any(i => i.InstanceId == instanceId) || _backpack.Contains(instanceId);
        }

        public int MaxStackFor(ItemDefinition definition) => _backpack.MaxStackFor(definition);

        /// <summary>Ammo Pouch seam: percent bonus applied to backpack ammo stack limits (read from the stat pipeline).</summary>
        public void SetAmmoCapacityBonusProvider(Func<int> bonusPercent) => _backpack.SetAmmoCapacityBonusProvider(bonusPercent);

        public static bool IsSlotCompatible(ItemCategory category, EquippedSlot slot)
        {
            return slot switch
            {
                EquippedSlot.PrimaryWeapon => category == ItemCategory.Weapon,
                EquippedSlot.SecondaryWeapon => category == ItemCategory.Weapon,
                EquippedSlot.Armor => category == ItemCategory.Armor,
                EquippedSlot.Accessory => category == ItemCategory.Accessory,
                EquippedSlot.ActiveConsumable => category == ItemCategory.Consumable,
                _ => false
            };
        }

        /// <param name="ignoreCurrentOwnership">
        /// When true, the "already held by this inventory" check is skipped so a transfer service can validate a
        /// move between two containers of the same inventory before the source releases the item.
        /// </param>
        public bool CanEquip(ItemInstance item, EquippedSlot slot, bool ignoreCurrentOwnership = false)
        {
            if (item == null || (!ignoreCurrentOwnership && Contains(item.InstanceId)))
            {
                return false;
            }

            var definition = _resolveDefinition(item.DefinitionId);
            if (definition == null || !IsSlotCompatible(definition.Category, slot))
            {
                return false;
            }

            return !_equipped.ContainsKey(slot);
        }

        public bool TryEquip(ItemInstance item, EquippedSlot slot)
        {
            if (!CanEquip(item, slot))
            {
                return false;
            }

            if (MarksIncomingAtRisk) item.IsAtRisk = true;
            _equipped[slot] = item;
            EquippedChanged?.Invoke(slot, item);
            return true;
        }

        public ItemInstance Unequip(EquippedSlot slot)
        {
            if (!_equipped.TryGetValue(slot, out var item))
            {
                return null;
            }

            _equipped.Remove(slot);
            EquippedChanged?.Invoke(slot, null);
            return item;
        }

        public bool CanAddToBackpack(ItemInstance item, bool ignoreCurrentOwnership = false)
        {
            if (item == null || (!ignoreCurrentOwnership && Contains(item.InstanceId)))
            {
                return false;
            }

            return _backpack.CanAdd(item);
        }

        public bool TryAddToBackpack(ItemInstance item)
        {
            if (!CanAddToBackpack(item)) return false;
            if (MarksIncomingAtRisk) item.IsAtRisk = true;
            return _backpack.TryAdd(item);
        }

        public ItemInstance RemoveFromBackpack(int slotIndex) => _backpack.RemoveAt(slotIndex);

        /// <summary>Manual backpack reorder: slot <paramref name="from"/> to exactly slot <paramref name="to"/> (move / swap / stack merge), raising BackpackChanged.</summary>
        public SlotMoveResult MoveBackpackSlot(int from, int to) => _backpack.TryMove(from, to);

        public int CountOf(string definitionId) => _backpack.CountOf(definitionId);

        // ---- IAmmoReserve (ammo lives only in backpack stacks) ----

        public int Get(AmmoType type)
        {
            var ammoDefinition = _resolveAmmoDefinition(type);
            return ammoDefinition == null ? 0 : _backpack.CountOf(ammoDefinition.Id);
        }

        public int Add(AmmoType type, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            var ammoDefinition = _resolveAmmoDefinition(type);
            if (ammoDefinition == null)
            {
                return 0;
            }

            var toAdd = Mathf.Min(amount, _backpack.CapacityForStack(ammoDefinition.Id, _backpack.MaxStackFor(ammoDefinition)));
            if (toAdd <= 0)
            {
                return 0;
            }

            _backpack.TryAdd(new ItemInstance(ammoDefinition.Id, toAdd) { IsAtRisk = MarksIncomingAtRisk });
            return toAdd;
        }

        public int Consume(AmmoType type, int amount)
        {
            var ammoDefinition = _resolveAmmoDefinition(type);
            return ammoDefinition == null ? 0 : _backpack.ConsumeQuantity(ammoDefinition.Id, amount);
        }

        // ---- Snapshot ----

        public InventorySnapshot ToSnapshot()
        {
            return new InventorySnapshot
            {
                Equipped = _equipped.Select(kv => new InventorySnapshot.Entry { Slot = (int)kv.Key, Item = kv.Value.ToSnapshot() }).ToArray(),
                Backpack = _backpack.ToEntries()
            };
        }

        public void RestoreFromSnapshot(InventorySnapshot snapshot)
        {
            _equipped.Clear();
            if (snapshot == null)
            {
                _backpack.Clear();
                return;
            }

            foreach (var entry in snapshot.Equipped ?? Array.Empty<InventorySnapshot.Entry>())
            {
                _equipped[(EquippedSlot)entry.Slot] = ItemInstance.FromSnapshot(entry.Item);
            }

            _backpack.RestoreFromEntries(snapshot.Backpack);
        }
    }
}
