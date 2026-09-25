using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Per-depth merchant bookkeeping: which offer indices were bought. Lives exactly as long as the depth (the host
    /// keeps it for the party); a new depth gets a fresh state and a fresh deterministic stock.
    /// </summary>
    public sealed class DungeonMerchantState
    {
        public DungeonMerchantState(int depth)
        {
            Depth = depth;
        }

        public int Depth { get; }
        public List<int> SoldOfferIndices { get; } = new();
    }

    /// <summary>
    /// 58 Dungeon Merchant: a safe-room shop that uses Carried Coins only. Stock is derived from RunSeed + Depth on the
    /// Loot stream (a merchant salt keeps it apart from chests) so it is identical for every reopen and every party
    /// member; there is no refresh of any kind within a depth. Buying debits Carried Coins and moves the item into the
    /// buyer's container atomically (a rejected destination refunds nothing because nothing was taken). Selling
    /// dungeon-held items credits the exact 77 sell value (35 % of buy value, rounded) in Carried Coins; ammo, which is
    /// priced per bundle, pays the ammo resale rule instead (<see cref="PriceService.AmmoSellValue"/>). Banked Coins
    /// are never touched.
    /// </summary>
    public sealed class DungeonMerchantService
    {
        private const int MerchantSalt = 0x4D45; // "ME"

        private readonly DungeonMerchantConfig _config;
        private readonly PriceService _prices;
        private readonly CoinWallet _carried;
        private readonly DungeonMerchantState _state;
        private readonly List<ItemDefinition> _catalog;
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly Func<LootQuality, RarityTableDefinition> _rarityTables;
        private readonly ItemTransferService _transfer = new();
        private readonly ListItemContainer _sink = new("dungeon_merchant_sink");
        private readonly List<TraderOffer> _offers = new();

        public DungeonMerchantService(
            DungeonMerchantConfig config,
            PriceService prices,
            CoinWallet carriedWallet,
            DungeonMerchantState state,
            int runSeed,
            int partySize,
            IEnumerable<ItemDefinition> catalog,
            Func<string, ItemDefinition> resolveDefinition,
            Func<LootQuality, RarityTableDefinition> rarityTables)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _carried = carriedWallet ?? throw new ArgumentNullException(nameof(carriedWallet));
            if (_carried.Domain != CoinDomain.Carried) throw new ArgumentException("The Dungeon Merchant uses Carried Coins only.", nameof(carriedWallet));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            RunSeed = runSeed;
            PartySize = Math.Max(1, partySize);
            _catalog = (catalog ?? throw new ArgumentNullException(nameof(catalog))).Where(d => d != null).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _rarityTables = rarityTables ?? (_ => null);
            Generate();
        }

        public int RunSeed { get; }
        public int Depth => _state.Depth;
        public int PartySize { get; }
        public DungeonMerchantState State => _state;
        public IReadOnlyList<TraderOffer> Offers => _offers;
        public IItemContainer Sink => _sink;

        /// <summary>Signature of the generated stock (definition ids, rarities, prices) for determinism checks/sync.</summary>
        public string StockSignature => string.Join("|", _offers.Select(o => $"{o.Index}:{o.Definition.Id}:{o.Item.Rarity}:{o.Item.Quantity}:{o.Price}"));

        public event Action<TraderOffer> Bought;
        public event Action<ItemInstance, int> Sold;

        // ---- Stock (generated once per depth; no refresh path exists) ----

        private void Generate()
        {
            _offers.Clear();
            var seed = SeededRandom.MixSeed(RunSeed, Depth, (int)RngStream.Loot, MerchantSalt);
            var random = new SeededRandom(seed);
            var equipment = EquipmentRollService.RegularEquipment(_catalog).Cast<ItemDefinition>().ToList();
            var consumables = _catalog.OfType<ConsumableDefinition>().Where(c => c.IsDropEligible(PartySize)).Cast<ItemDefinition>().ToList();
            var ammo = _catalog.OfType<AmmoItemDefinition>().Cast<ItemDefinition>().ToList();

            var slots = _config.Slots;
            for (var i = 0; i < slots.Length; i++)
            {
                var kind = slots[i];
                if (kind == MerchantSlotKind.Random)
                {
                    kind = (MerchantSlotKind)random.NextInt(3);
                }

                var pool = kind switch
                {
                    MerchantSlotKind.Equipment => equipment,
                    MerchantSlotKind.Consumable => consumables,
                    _ => ammo
                };
                if (pool.Count == 0) continue;

                var definition = pool[random.NextInt(pool.Count)];
                var offer = CreateOffer(i, definition, random);
                offer.IsSold = _state.SoldOfferIndices.Contains(i);
                _offers.Add(offer);
            }
        }

        private TraderOffer CreateOffer(int index, ItemDefinition definition, IRandomSource random)
        {
            switch (definition)
            {
                case EquipmentItemDefinition equipment:
                    var rarity = EquipmentRollService.RollRarity(_rarityTables(_config.Quality) ?? _rarityTables(LootQuality.Standard), Depth, random);
                    var instance = EquipmentRollService.Create(equipment, rarity, random, _catalog, out var used);
                    return new TraderOffer(index, instance, used, _prices.BuyValue(used, instance.Rarity));
                case AmmoItemDefinition ammoDefinition:
                    var units = _prices.Config.TryGetAmmoBundle(ammoDefinition.AmmoType, out var bundle) ? bundle.Units : 1;
                    return new TraderOffer(index, new ItemInstance(ammoDefinition.Id, units), definition, _prices.FlatPrice(definition));
                default:
                    return new TraderOffer(index, new ItemInstance(definition.Id, 1), definition, _prices.FlatPrice(definition));
            }
        }

        // ---- Buy / Sell (Carried Coins only) ----

        public TradeError Buy(int offerIndex, IItemContainer destination) => Buy(offerIndex, destination, null);

        /// <summary>
        /// 58/84 in co-op: carried coins are per member, so the host charges the buying member's own Carried wallet.
        /// Null uses the wallet the merchant was composed with (solo: the run's one Carried wallet). Stock, prices and
        /// the sold-once rule are the same either way.
        /// </summary>
        public TradeError Buy(int offerIndex, IItemContainer destination, CoinWallet payer)
        {
            var wallet = payer ?? _carried;
            if (wallet.Domain != CoinDomain.Carried) return TradeError.InsufficientFunds;
            var offer = _offers.FirstOrDefault(o => o.Index == offerIndex);
            if (offer == null) return TradeError.NoSuchOffer;
            if (offer.IsSold) return TradeError.AlreadySold;
            if (!wallet.CanAfford(offer.Price)) return TradeError.InsufficientFunds;
            if (destination == null || !destination.CanAccept(offer.Item)) return TradeError.DestinationRejected;

            var debit = wallet.Debit(offer.Price, $"merchant_buy:{offer.Definition.Id}");
            if (!debit.Success) return TradeError.InsufficientFunds;
            if (!destination.TryAdd(offer.Item))
            {
                wallet.Credit(offer.Price, "merchant_buy_refund");
                return TradeError.DestinationRejected;
            }

            offer.IsSold = true;
            _state.SoldOfferIndices.Add(offer.Index);
            Bought?.Invoke(offer);
            return TradeError.None;
        }

        /// <summary>
        /// Co-op client: the host sold this offer (to any member); this peer's copy of the stock reads it as sold. Stock,
        /// prices and wallets are untouched — the sale itself happened once, on the host.
        /// </summary>
        public bool ApplySoldFromAuthority(int offerIndex)
        {
            var offer = _offers.FirstOrDefault(o => o.Index == offerIndex);
            if (offer == null || offer.IsSold) return false;
            offer.IsSold = true;
            if (!_state.SoldOfferIndices.Contains(offerIndex)) _state.SoldOfferIndices.Add(offerIndex);
            return true;
        }

        public int QuoteSellValue(ItemInstance item)
        {
            if (item == null || item.IsUnsellable) return 0;
            var definition = _resolveDefinition(item.DefinitionId);
            if (definition == null) return 0;
            // Ammo is priced per bundle, not per round: its payout is the 15 % ammo resale rule on the exact quantity.
            if (definition is AmmoItemDefinition ammo) return _prices.AmmoSellValue(ammo, item.Quantity);
            var unitValue = definition is EquipmentItemDefinition ? _prices.SellValue(definition, item.Rarity) : _prices.SellValue(_prices.FlatPrice(definition));
            return definition.IsStackable ? unitValue * Math.Max(1, item.Quantity) : unitValue;
        }

        /// <summary>58: dungeon-held items may be sold for Carried Coins; the item moves into the merchant sink first.</summary>
        public TradeError Sell(IItemContainer source, string instanceId) => Sell(source, instanceId, null);

        /// <summary>The selling member's own Carried wallet receives the payout (null: the composed wallet).</summary>
        public TradeError Sell(IItemContainer source, string instanceId, CoinWallet payee)
        {
            var wallet = payee ?? _carried;
            if (wallet.Domain != CoinDomain.Carried) return TradeError.NoValue;
            var item = source?.Find(instanceId);
            if (item == null) return TradeError.SourceMissingItem;
            if (item.IsUnsellable) return TradeError.Unsellable;
            var value = QuoteSellValue(item);
            if (value <= 0) return TradeError.NoValue;

            var moved = _transfer.Transfer(source, instanceId, _sink);
            if (!moved.Success) return TradeError.SourceMissingItem;

            wallet.Credit(value, $"merchant_sell:{item.DefinitionId}");
            Sold?.Invoke(item, value);
            return TradeError.None;
        }
    }
}
