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

    /// <summary>
    /// A container that can say how much of an item it could take right now: for a stack, the room left in its
    /// matching stacks plus its free slots (existing stack caps, no new rule); for a single item, 1 or 0. A pickup uses
    /// it to take the part of a stack that fits instead of refusing the whole stack.
    /// </summary>
    public interface IStackRoom
    {
        int RoomFor(ItemInstance item);
    }
}
