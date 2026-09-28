using System;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Base
{
    /// <summary>
    /// Minimal Base-side boundary for later Storage UI: every move between a player's containers and Storage goes
    /// through the central ItemTransferService, so both sides stay consistent and nothing is duplicated or lost.
    /// </summary>
    public sealed class StorageService
    {
        private readonly ItemTransferService _transfer;

        public StorageService(Storage storage, ItemTransferService transfer = null)
        {
            Storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _transfer = transfer ?? new ItemTransferService();
        }

        public Storage Storage { get; }

        public TransferResult Deposit(IItemContainer source, string instanceId) => _transfer.Transfer(source, instanceId, Storage);

        public TransferResult DepositQuantity(IItemContainer source, string instanceId, int quantity) => _transfer.TransferQuantity(source, instanceId, quantity, Storage);

        public TransferResult Withdraw(string instanceId, IItemContainer destination) => _transfer.Transfer(Storage, instanceId, destination);

        public TransferResult WithdrawQuantity(string instanceId, int quantity, IItemContainer destination) => _transfer.TransferQuantity(Storage, instanceId, quantity, destination);

        /// <summary>
        /// Exchanges a stored item with the item in backpack slot <paramref name="backpackIndex"/> (94 Storage: a drop onto
        /// an occupied cell). Each takes the other's exact place, so neither side needs a free slot. Both slots are swapped
        /// before either container reports a change, so no observer ever sees an item in two places or in none. Refused
        /// unchanged when either item is missing, the backpack item is at risk, or the two are the same stackable kind
        /// (those merge through an ordinary transfer instead).
        /// </summary>
        public TransferResult ExchangeWithBackpack(PlayerInventory inventory, string storedInstanceId, int backpackIndex)
        {
            if (inventory == null || string.IsNullOrEmpty(storedInstanceId) || backpackIndex < 0 || backpackIndex >= inventory.BackpackSlots.Count)
                return TransferResult.Fail(TransferError.InvalidRequest, storedInstanceId);
            var stored = Storage.Find(storedInstanceId);
            var carried = inventory.BackpackSlots[backpackIndex];
            if (stored == null || carried == null) return TransferResult.Fail(TransferError.SourceMissingItem, storedInstanceId);
            if (carried.IsAtRisk || inventory.Contains(storedInstanceId) || Storage.Contains(carried.InstanceId)) return TransferResult.Fail(TransferError.DuplicateOwnership, storedInstanceId);
            var definition = inventory.Resolve(stored.DefinitionId);
            if (definition == null || inventory.Resolve(carried.DefinitionId) == null) return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            if (definition.IsStackable && stored.DefinitionId == carried.DefinitionId) return TransferResult.Fail(TransferError.InvalidRequest, storedInstanceId);

            if (inventory.Backpack.ExchangeAt(backpackIndex, stored) != carried) return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            if (Storage.ExchangeStored(storedInstanceId, carried) != stored)
            {
                inventory.Backpack.ExchangeAt(backpackIndex, carried); // unreachable after the checks above; restores the exact prior state
                return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            }

            inventory.Backpack.RaiseChanged();
            return TransferResult.Ok(storedInstanceId, stored.Quantity);
        }

        /// <summary>
        /// Equips a stored item straight into <paramref name="slot"/> (94 Storage). An empty slot is an ordinary withdraw.
        /// An occupied one is an exchange: the worn item takes exactly the stored item's Storage cell, so neither a free
        /// Storage cell nor a free backpack slot is needed. Everything is validated before anything moves; the commit
        /// cannot fail part-way, and each item is only ever in one place (the worn item leaves its slot before it enters
        /// Storage). An invalid pairing, an at-risk worn item or a missing item changes nothing.
        /// </summary>
        public TransferResult ExchangeWithEquipped(PlayerInventory inventory, string storedInstanceId, EquippedSlot slot)
        {
            if (inventory == null || string.IsNullOrEmpty(storedInstanceId)) return TransferResult.Fail(TransferError.InvalidRequest, storedInstanceId);
            var stored = Storage.Find(storedInstanceId);
            if (stored == null) return TransferResult.Fail(TransferError.SourceMissingItem, storedInstanceId);
            var definition = inventory.Resolve(stored.DefinitionId);
            if (definition == null || !PlayerInventory.IsSlotCompatible(definition.Category, slot)) return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            if (inventory.Contains(storedInstanceId)) return TransferResult.Fail(TransferError.DuplicateOwnership, storedInstanceId);

            var worn = inventory.GetEquipped(slot);
            if (worn == null) return Withdraw(storedInstanceId, new EquippedSlotContainer(inventory, slot));
            if (worn.IsAtRisk || Storage.Contains(worn.InstanceId)) return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);

            var released = inventory.Unequip(slot);
            var taken = Storage.ExchangeStored(storedInstanceId, released);
            if (taken == null)
            {
                inventory.TryEquip(released, slot); // unreachable after the checks above; restores the exact prior state
                return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            }

            if (!inventory.TryEquip(taken, slot))
            {
                Storage.ExchangeStored(released.InstanceId, taken);
                inventory.TryEquip(released, slot);
                return TransferResult.Fail(TransferError.DestinationRejected, storedInstanceId);
            }

            return TransferResult.Ok(storedInstanceId, taken.Quantity);
        }
    }
}
