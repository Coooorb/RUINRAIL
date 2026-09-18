using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Items
{
    public sealed class AmmoReserve : IAmmoReserve
    {
        private readonly Dictionary<AmmoType, int> _counts = new();

        public int Get(AmmoType type)
        {
            return _counts.TryGetValue(type, out var count) ? count : 0;
        }

        public void Set(AmmoType type, int amount)
        {
            _counts[type] = Mathf.Max(0, amount);
        }

        public int Add(AmmoType type, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            Set(type, Get(type) + amount);
            return amount;
        }

        public int Consume(AmmoType type, int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            var available = Get(type);
            var consumed = Mathf.Min(available, amount);
            Set(type, available - consumed);
            return consumed;
        }
    }
}
