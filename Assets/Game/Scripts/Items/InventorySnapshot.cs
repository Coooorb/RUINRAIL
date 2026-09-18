using System;

namespace RuinRail.Gameplay.Items
{
    [Serializable]
    public sealed class InventorySnapshot
    {
        [Serializable]
        public sealed class Entry
        {
            public int Slot;
            public ItemInstanceSnapshot Item;
        }

        public Entry[] Equipped;
        public Entry[] Backpack;
    }
}
