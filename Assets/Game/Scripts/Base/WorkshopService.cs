using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Economy;
using UnityEngine;

namespace RuinRail.Gameplay.Base
{
    /// <summary>Persisted workshop progress (profile domain).</summary>
    [Serializable]
    public sealed class WorkshopState
    {
        public int StorageTier;
    }

    public enum UpgradeError
    {
        None,
        NotAtBase,
        AlreadyMaxTier,
        InsufficientFunds
    }

    /// <summary>
    /// Base-only permanent upgrades paid with Banked Coins: storage capacity (60→80→100→120) and trader level
    /// (4→5→6 offers). Each purchase is one atomic step: the debit is applied first (a rejected debit changes nothing),
    /// then the tier/state; storage growth never touches stored items; a maxed line cannot be charged again.
    /// </summary>
    public sealed class WorkshopService
    {
        private readonly WorkshopConfig _config;
        private readonly CoinWallet _banked;
        private readonly WorkshopState _state;
        private readonly Storage _storage;
        private readonly TraderService _trader;

        public WorkshopService(WorkshopConfig config, CoinWallet bankedWallet, WorkshopState state, Storage storage, TraderService trader)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _banked = bankedWallet ?? throw new ArgumentNullException(nameof(bankedWallet));
            if (_banked.Domain != CoinDomain.Banked) throw new ArgumentException("The Workshop uses Banked Coins only.", nameof(bankedWallet));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _trader = trader;
            ApplyPersistedStorageTier();
        }

        public bool IsAtBase { get; set; }
        public int StorageTier => _state.StorageTier;
        public int StorageCapacity => _config.StorageCapacityForTier(_state.StorageTier);
        public int NextStorageCapacity => _config.StorageCapacityForTier(_state.StorageTier + 1);
        public int NextStorageUpgradeCost => _config.StorageUpgradeCostFrom(_state.StorageTier);
        public bool IsStorageMaxed => _state.StorageTier >= _config.MaxStorageTier;
        public int MaxStorageTier => _config.MaxStorageTier;
        public int TraderLevel => _trader?.Level ?? 1;
        public int NextTraderUpgradeCost => _trader?.NextUpgradeCost ?? 0;

        public event Action<int> StorageUpgraded;
        public event Action<int> TraderUpgraded;

        /// <summary>Restores capacity for a loaded profile without touching items (persisted tier is the source of truth).</summary>
        public void ApplyPersistedStorageTier()
        {
            _storage.ExpandTo(StorageCapacity);
        }

        public UpgradeError BuyStorageUpgrade()
        {
            if (!IsAtBase) return UpgradeError.NotAtBase;
            if (IsStorageMaxed) return UpgradeError.AlreadyMaxTier;
            var cost = NextStorageUpgradeCost;
            if (!_banked.CanAfford(cost)) return UpgradeError.InsufficientFunds;
            if (!_banked.Debit(cost, $"storage_upgrade_{_state.StorageTier + 1}").Success) return UpgradeError.InsufficientFunds;

            _state.StorageTier++;
            _storage.ExpandTo(StorageCapacity);
            StorageUpgraded?.Invoke(StorageCapacity);
            return UpgradeError.None;
        }

        public UpgradeError BuyTraderUpgrade()
        {
            if (!IsAtBase) return UpgradeError.NotAtBase;
            if (_trader == null) return UpgradeError.AlreadyMaxTier;
            var result = _trader.TryUpgrade();
            switch (result)
            {
                case TradeError.None:
                    TraderUpgraded?.Invoke(_trader.Level);
                    return UpgradeError.None;
                case TradeError.AlreadyMaxLevel:
                    return UpgradeError.AlreadyMaxTier;
                default:
                    return UpgradeError.InsufficientFunds;
            }
        }
    }
}
