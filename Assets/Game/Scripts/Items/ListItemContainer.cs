using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Unbounded, unstacked holding container (ground drops, loot piles, test fixtures).
    /// </summary>
    public sealed class ListItemContainer : IItemContainer
    {
        private readonly List<ItemInstance> _items = new();

        public ListItemContainer(string containerId)
        {
            ContainerId = containerId;
        }

        public string ContainerId { get; }

        public IEnumerable<ItemInstance> Items => _items;

        public int Count => _items.Count;

        public ItemInstance Find(string instanceId)
        {
            return _items.FirstOrDefault(i => i.InstanceId == instanceId);
        }

        public bool CanAccept(ItemInstance item)
        {
            return item != null && Find(item.InstanceId) == null;
        }

        public bool TryAdd(ItemInstance item)
        {
            if (!CanAccept(item))
            {
                return false;
            }

            _items.Add(item);
            return true;
        }

        public ItemInstance TryRemove(string instanceId)
        {
            var item = Find(instanceId);
            if (item != null)
            {
                _items.Remove(item);
            }

            return item;
        }
    }
}
