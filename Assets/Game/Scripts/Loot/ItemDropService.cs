using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>Outcome of a drop: the transfer result plus the ground pickup now owning the item (null on failure).</summary>
    public readonly struct DropResult
    {
        public DropResult(TransferResult transfer, WorldItemPickup pickup)
        {
            Transfer = transfer;
            Pickup = pickup;
        }

        public TransferResult Transfer { get; }
        public WorldItemPickup Pickup { get; }
        public bool Success => Transfer.Success;
    }

    /// <summary>
    /// 32 "Dropping": a player may drop backpack items and equipped gear (whole instances or part of a stack) onto the
    /// ground for teammates; there is no trade UI. The drop is an ordinary transfer-service move from the carried
    /// container into a fresh ground pickup: the item exists in exactly one place at every step, the pickup is only
    /// kept when the transfer succeeded, and nothing else ever removes it during the depth.
    /// </summary>
    public sealed class ItemDropService
    {
        private readonly IWorldPickupFactory _factory;
        private readonly ItemTransferService _transfer;

        public ItemDropService(IWorldPickupFactory factory, ItemTransferService transfer = null)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _transfer = transfer ?? new ItemTransferService();
        }

        /// <summary>Drops the whole instance/stack.</summary>
        public DropResult Drop(IItemContainer source, string instanceId, Vector2 position)
        {
            var stack = source?.Find(instanceId);
            return stack == null
                ? new DropResult(TransferResult.Fail(TransferError.SourceMissingItem, instanceId), null)
                : Drop(source, instanceId, stack.Quantity, position);
        }

        /// <summary>Drops a quantity of a stack (the whole instance when quantity equals the stack size).</summary>
        public DropResult Drop(IItemContainer source, string instanceId, int quantity, Vector2 position)
        {
            if (source == null || string.IsNullOrEmpty(instanceId))
            {
                return new DropResult(TransferResult.Fail(TransferError.InvalidRequest, instanceId), null);
            }

            var stack = source.Find(instanceId);
            if (stack == null)
            {
                return new DropResult(TransferResult.Fail(TransferError.SourceMissingItem, instanceId), null);
            }

            if (quantity <= 0 || quantity > stack.Quantity)
            {
                return new DropResult(TransferResult.Fail(TransferError.InvalidQuantity, instanceId), null);
            }

            var pickup = _factory.CreateItemPickup(position);
            var result = _transfer.TransferQuantity(source, instanceId, quantity, pickup);
            if (!result.Success)
            {
                UnityEngine.Object.Destroy(pickup.gameObject);
                return new DropResult(result, null);
            }

            pickup.name = $"Pickup_{pickup.Item.DefinitionId}";
            pickup.SetCategory(_factory.CategoryOf(pickup.Item));
            return new DropResult(result, pickup);
        }

        /// <summary>Drops from the first of several carried containers (backpack, equipped slots) holding the instance.</summary>
        public DropResult DropFromAny(IEnumerable<IItemContainer> sources, string instanceId, int quantity, Vector2 position)
        {
            if (sources != null)
            {
                foreach (var source in sources)
                {
                    if (source?.Find(instanceId) != null) return Drop(source, instanceId, quantity, position);
                }
            }

            return new DropResult(TransferResult.Fail(TransferError.SourceMissingItem, instanceId), null);
        }
    }
}
