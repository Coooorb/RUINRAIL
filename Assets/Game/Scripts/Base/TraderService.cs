using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;

namespace RuinRail.Gameplay.Base
{
    /// <summary>Persisted trader progress (profile domain): level, how many refreshes happened, which offers were bought.</summary>
    [Serializable]
    public sealed class TraderState
    {
        public int Level = 1;
        public int RefreshCount;
        public int LastRefreshedExpedition = -1;
        public List<int> SoldOfferIndices = new();
    }

    public sealed class TraderOffer
    {
        public TraderOffer(int index, ItemInstance item, ItemDefinition definition, int price)
        {
            Index = index;
            Item = item;
            Definition = definition;
            Price = price;
        }

        public int Index { get; }
        public ItemInstance Item { get; }
        public ItemDefinition Definition { get; }
        public int Price { get; }
        public bool IsSold { get; internal set; }
    }

    public enum TradeError
    {
        None,
        NoSuchOffer,
        AlreadySold,
        InsufficientFunds,
        DestinationRejected,
        SourceMissingItem,
        Unsellable,
        NoValue,
        AlreadyMaxLevel
    }

    /// <summary>
    /// Shelter Trader (base/72_TRADER): deterministic offers per (profile seed, refresh count, level), refreshed once
    /// per ended expedition; buying debits Banked Coins and moves the item atomically, selling moves the item into the
    /// trader sink and credits the exact 35 % rounded value; Starter Kit items are unsellable; level upgrades 4→5→6.
    /// </summary>
    public sealed class TraderService
    {
        private readonly TraderConfig _config;
        private readonly PriceService _prices;
        private readonly CoinWallet _banked;
        private readonly TraderState _state;
        private readonly int _profileSeed;
        private readonly List<ItemDefinition> _catalog;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly ItemTransferService _transfer = new();
        private readonly ListItemContainer _sink = new("trader_sink");
        private readonly AffixRollService _affixes = new();
        private readonly List<TraderOffer> _offers = new();

        public TraderService(TraderConfig config, PriceService prices, CoinWallet bankedWallet, TraderState state, int profileSeed, IEnumerable<ItemDefinition> catalog, Func<string, ItemDefinition> resolveDefinition)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _banked = bankedWallet ?? throw new ArgumentNullException(nameof(bankedWallet));
            if (_banked.Domain != CoinDomain.Banked) throw new ArgumentException("The Trader uses Banked Coins only.", nameof(bankedWallet));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _profileSeed = profileSeed;
            _catalog = (catalog ?? throw new ArgumentNullException(nameof(catalog))).Where(d => d != null).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            Regenerate();
        }

        public int Level => _state.Level;
        public int OfferCount => _config.GetLevel(Level).Offers;
        public IReadOnlyList<TraderOffer> Offers => _offers;
        public TraderState State => _state;
        public IItemContainer Sink => _sink;

        public event Action Refreshed;
        public event Action<TraderOffer> Bought;
        public event Action<ItemInstance, int> Sold;

        // ---- Refresh: once per ended expedition ----

        /// <summary>Refreshes the stock for the given ended expedition; the same expedition index never refreshes twice.</summary>
        public bool RefreshForEndedExpedition(int expeditionIndex)
        {
            if (expeditionIndex <= _state.LastRefreshedExpedition) return false;
            _state.LastRefreshedExpedition = expeditionIndex;
            _state.RefreshCount++;
            _state.SoldOfferIndices.Clear();
            Regenerate();
            Refreshed?.Invoke();
            return true;
        }

        private void Regenerate()
        {
            _offers.Clear();
            var level = _config.GetLevel(Level);
            var random = new SeededRandom(SeededRandom.MixSeed(_profileSeed, _state.RefreshCount, Level, 0x7A4D));
            var equipment = _catalog.Where(d => d is EquipmentItemDefinition && d.Category != ItemCategory.Consumable).ToList();
            var consumables = _catalog.OfType<ConsumableDefinition>().Where(c => c.DropEligibility == DropEligibility.Any).Cast<ItemDefinition>().ToList();
            var ammo = _catalog.OfType<AmmoItemDefinition>().Cast<ItemDefinition>().ToList();

            for (var i = 0; i < level.Offers; i++)
            {
                var pool = PickCategory(random, equipment, consumables, ammo);
                if (pool.Count == 0) continue;
                var definition = pool[random.NextInt(pool.Count)];
                var offer = CreateOffer(i, definition, level, random);
                offer.IsSold = _state.SoldOfferIndices.Contains(i);
                _offers.Add(offer);
            }
        }

        private List<ItemDefinition> PickCategory(IRandomSource random, List<ItemDefinition> equipment, List<ItemDefinition> consumables, List<ItemDefinition> ammo)
        {
            var weights = new[] { (equipment, _config.EquipmentWeight), (consumables, _config.ConsumableWeight), (ammo, _config.AmmoWeight) }
                .Where(t => t.Item1.Count > 0 && t.Item2 > 0).ToList();
            if (weights.Count == 0) return new List<ItemDefinition>();
            var pick = random.NextInt(weights.Sum(t => t.Item2));
            foreach (var (pool, weight) in weights)
            {
                pick -= weight;
                if (pick < 0) return pool;
            }

            return weights[^1].Item1;
        }

        private TraderOffer CreateOffer(int index, ItemDefinition definition, TraderConfig.Level level, IRandomSource random)
        {
            switch (definition)
            {
                case EquipmentItemDefinition equipment:
                    var rarity = RollRarity(level.RarityWeights, random);
                    var instance = new ItemInstance(equipment.Id, 1);
                    var roll = _affixes.Roll(instance, equipment, rarity, random);
                    if (!roll.Success) instance.Rarity = Rarity.Common;
                    return new TraderOffer(index, instance, definition, _prices.BuyValue(definition, instance.Rarity));
                case AmmoItemDefinition ammoDefinition:
                    var units = _prices.Config.TryGetAmmoBundle(ammoDefinition.AmmoType, out var bundle) ? bundle.Units : 1;
                    return new TraderOffer(index, new ItemInstance(ammoDefinition.Id, units), definition, _prices.FlatPrice(definition));
                default:
                    return new TraderOffer(index, new ItemInstance(definition.Id, 1), definition, _prices.FlatPrice(definition));
            }
        }

        private static Rarity RollRarity(int[] weights, IRandomSource random)
        {
            var total = weights.Sum();
            var pick = random.NextInt(Math.Max(1, total));
            for (var r = 0; r < weights.Length; r++)
            {
                pick -= weights[r];
                if (pick < 0) return (Rarity)r;
            }

            return Rarity.Common;
        }

        // ---- Buy / Sell ----

        public TradeError Buy(int offerIndex, IItemContainer destination)
        {
            var offer = _offers.FirstOrDefault(o => o.Index == offerIndex);
            if (offer == null) return TradeError.NoSuchOffer;
            if (offer.IsSold) return TradeError.AlreadySold;
            if (!_banked.CanAfford(offer.Price)) return TradeError.InsufficientFunds;
            if (destination == null || !destination.CanAccept(offer.Item)) return TradeError.DestinationRejected;

            var debit = _banked.Debit(offer.Price, $"buy:{offer.Definition.Id}");
            if (!debit.Success) return TradeError.InsufficientFunds;
            if (!destination.TryAdd(offer.Item))
            {
                _banked.Credit(offer.Price, "buy_refund");
                return TradeError.DestinationRejected;
            }

            offer.IsSold = true;
            _state.SoldOfferIndices.Add(offer.Index);
            Bought?.Invoke(offer);
            return TradeError.None;
        }

        public int QuoteSellValue(ItemInstance item)
        {
            if (item == null || item.IsUnsellable) return 0;
            var definition = _resolveDefinition(item.DefinitionId);
            if (definition == null) return 0;
            var unitValue = definition is EquipmentItemDefinition ? _prices.SellValue(definition, item.Rarity) : _prices.SellValue(_prices.FlatPrice(definition));
            return definition.IsStackable ? unitValue * Math.Max(1, item.Quantity) : unitValue;
        }

        public TradeError Sell(IItemContainer source, string instanceId)
        {
            var item = source?.Find(instanceId);
            if (item == null) return TradeError.SourceMissingItem;
            if (item.IsUnsellable) return TradeError.Unsellable;
            var value = QuoteSellValue(item);
            if (value <= 0) return TradeError.NoValue;

            var moved = _transfer.Transfer(source, instanceId, _sink);
            if (!moved.Success) return TradeError.SourceMissingItem;

            _banked.Credit(value, $"sell:{item.DefinitionId}");
            Sold?.Invoke(item, value);
            return TradeError.None;
        }

        // ---- Level ----

        public int NextUpgradeCost => _config.UpgradeCostFrom(Level);

        public TradeError TryUpgrade()
        {
            if (Level >= _config.MaxLevel) return TradeError.AlreadyMaxLevel;
            var cost = NextUpgradeCost;
            if (!_banked.CanAfford(cost)) return TradeError.InsufficientFunds;
            if (!_banked.Debit(cost, "trader_upgrade").Success) return TradeError.InsufficientFunds;
            _state.Level++;
            return TradeError.None;
        }
    }
}
