using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.Gameplay.Base
{
    /// <summary>Approved Workshop lines (74/71/72): storage capacity tiers and trader levels, Banked Coins only. No other lines.</summary>
    [CreateAssetMenu(fileName = "WorkshopConfig", menuName = "RuinRail/Base/Workshop Config")]
    public sealed class WorkshopConfig : ScriptableObject
    {
        [Serializable]
        public struct StorageTier
        {
            [Min(1)] public int Capacity;
            [Min(0)] public int Cost;
        }

        [SerializeField] private int _baseStorageCapacity = Storage.BaseCapacity;
        [SerializeField] private StorageTier[] _storageTiers =
        {
            new() { Capacity = 80, Cost = 1500 },
            new() { Capacity = 100, Cost = 4000 },
            new() { Capacity = 120, Cost = 8000 }
        };

        public int BaseStorageCapacity => _baseStorageCapacity;
        public IReadOnlyList<StorageTier> StorageTiers => _storageTiers;
        public int MaxStorageTier => _storageTiers.Length;

        /// <summary>Capacity granted by a tier (0 = base).</summary>
        public int StorageCapacityForTier(int tier)
        {
            tier = Mathf.Clamp(tier, 0, MaxStorageTier);
            return tier == 0 ? _baseStorageCapacity : _storageTiers[tier - 1].Capacity;
        }

        public int StorageUpgradeCostFrom(int tier) => tier >= MaxStorageTier ? 0 : _storageTiers[tier].Cost;
    }
}
