using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Gameplay.Items
{
    public sealed class ItemTransferService
    {
        public TransferResult Transfer(IItemContainer source, string instanceId, IItemContainer destination)
        {
            if (source == null || destination == null || string.IsNullOrEmpty(instanceId) || ReferenceEquals(source, destination))
            {
                return TransferResult.Fail(TransferError.InvalidRequest, instanceId);
            }

            var item = source.Find(instanceId);
            if (item == null)
            {
                return TransferResult.Fail(TransferError.SourceMissingItem, instanceId);
            }

            if (destination.Find(instanceId) != null)
            {
                return TransferResult.Fail(TransferError.DuplicateOwnership, instanceId);
            }

            if (!destination.CanAccept(item))
            {
                return TransferResult.Fail(TransferError.DestinationRejected, instanceId);
            }

            var quantity = item.Quantity;
            var removed = source.TryRemove(instanceId);
            if (removed == null)
            {
                return TransferResult.Fail(TransferError.SourceMissingItem, instanceId);
            }

            if (!destination.TryAdd(removed))
            {
                source.TryAdd(removed);
                return TransferResult.Fail(TransferError.DestinationRejected, instanceId);
            }

            return TransferResult.Ok(instanceId, quantity);
        }

        public TransferResult TransferQuantity(IItemContainer source, string instanceId, int quantity, IItemContainer destination)
        {
            if (source == null || destination == null || string.IsNullOrEmpty(instanceId) || ReferenceEquals(source, destination))
            {
                return TransferResult.Fail(TransferError.InvalidRequest, instanceId);
            }

            var stack = source.Find(instanceId);
            if (stack == null)
            {
                return TransferResult.Fail(TransferError.SourceMissingItem, instanceId);
            }

            if (quantity <= 0 || quantity > stack.Quantity)
            {
                return TransferResult.Fail(TransferError.InvalidQuantity, instanceId);
            }

            if (quantity == stack.Quantity)
            {
                return Transfer(source, instanceId, destination);
            }

            var portion = new ItemInstance(stack.DefinitionId, quantity, stack.Rarity) { IsAtRisk = stack.IsAtRisk };
            if (!destination.CanAccept(portion))
            {
                return TransferResult.Fail(TransferError.DestinationRejected, instanceId);
            }

            var originalQuantity = stack.Quantity;
            stack.SetQuantity(originalQuantity - quantity);
            if (!destination.TryAdd(portion))
            {
                stack.SetQuantity(originalQuantity);
                return TransferResult.Fail(TransferError.DestinationRejected, instanceId);
            }

            return TransferResult.Ok(portion.InstanceId, quantity);
        }

        public static IReadOnlyList<string> DetectDuplicateOwnership(IEnumerable<IItemContainer> containers)
        {
            return containers
                .SelectMany(c => c.Items.Select(i => i.InstanceId))
                .GroupBy(id => id)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
        }
    }
}
