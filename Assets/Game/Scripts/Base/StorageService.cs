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
    }
}
