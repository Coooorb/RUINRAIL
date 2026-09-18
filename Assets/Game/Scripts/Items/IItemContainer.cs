using System.Collections.Generic;

namespace RuinRail.Gameplay.Items
{
    public interface IItemContainer
    {
        string ContainerId { get; }
        IEnumerable<ItemInstance> Items { get; }
        ItemInstance Find(string instanceId);
        bool CanAccept(ItemInstance item);
        bool TryAdd(ItemInstance item);
        ItemInstance TryRemove(string instanceId);
    }
}
