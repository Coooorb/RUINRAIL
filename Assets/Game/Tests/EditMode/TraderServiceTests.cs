using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class TraderServiceTests
    {
        private TraderConfig _config;
        private EconomyConfig _economy;
        private PriceService _prices;
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _prices = new PriceService(_economy);
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            Assert.IsEmpty(_registry.Problems);
        }

        private (TraderService trader, PlayerProfile profile, CoinWallet banked) NewTrader(int coins = 5000, int seed = 42)
        {
            var profile = new PlayerProfile { BankedCoins = coins, ProfileSeed = seed };
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var trader = new TraderService(_config, _prices, banked, profile.Trader, profile.ProfileSeed, _catalog, id => _registry.TryGet(id, out var d) ? d : null);
            return (trader, profile, banked);
        }

        // ---- Acceptance 1 ----

        [Test]
        public void Config_MatchesApprovedLevels_AndOfferCountsFollowLevel()
        {
            Assert.AreEqual(3, _config.MaxLevel);
            Assert.AreEqual(4, _config.GetLevel(1).Offers);
            Assert.AreEqual(5, _config.GetLevel(2).Offers);
            Assert.AreEqual(6, _config.GetLevel(3).Offers);
            Assert.AreEqual(2500, _config.UpgradeCostFrom(1));
            Assert.AreEqual(7000, _config.UpgradeCostFrom(2));
            Assert.AreEqual(0, _config.UpgradeCostFrom(3));
            CollectionAssert.AreEqual(new[] { 500, 350, 130, 19, 1 }, _config.GetLevel(1).RarityWeights);
            CollectionAssert.AreEqual(new[] { 350, 400, 200, 47, 3 }, _config.GetLevel(2).RarityWeights);
            CollectionAssert.AreEqual(new[] { 250, 380, 280, 84, 6 }, _config.GetLevel(3).RarityWeights);

            var (trader, _, _) = NewTrader();
            Assert.AreEqual(1, trader.Level);
            Assert.AreEqual(4, trader.Offers.Count, "Level 1 starts with 4 offers.");
            Assert.IsTrue(trader.Offers.All(o => o.Price > 0));
            Assert.IsTrue(trader.Offers.All(o => o.Definition is not ConsumableDefinition c || c.DropEligibility == DropEligibility.Any), "No Defibrillator on the shelf.");
        }

        [Test]
        public void OfferRarityDistribution_FollowsTheLevelTable()
        {
            int CountEquipment(int level, out int rarePlus, out int common)
            {
                rarePlus = 0;
                common = 0;
                var total = 0;
                for (var seed = 0; seed < 400; seed++)
                {
                    var (trader, _, _) = NewTrader(0, seed);
                    for (var l = 1; l < level; l++) trader.State.Level++;
                    trader.RefreshForEndedExpedition(1);
                    // Only families with an authored affix pool can roll above Common (accessory/bow/blaster pools are pending).
                    foreach (var offer in trader.Offers.Where(o => o.Definition is EquipmentItemDefinition e && e.AffixPool != null))
                    {
                        total++;
                        if (offer.Item.Rarity >= Rarity.Rare) rarePlus++;
                        if (offer.Item.Rarity == Rarity.Common) common++;
                    }
                }

                return total;
            }

            var total1 = CountEquipment(1, out var rare1, out var common1);
            var total3 = CountEquipment(3, out var rare3, out var common3);
            Assert.Greater(total1, 150);
            Assert.That(common1 / (float)total1, Is.InRange(0.42f, 0.58f), "Level 1: ~50% Common");
            Assert.That(rare1 / (float)total1, Is.InRange(0.10f, 0.21f), "Level 1: ~15% Rare+");
            Assert.That(common3 / (float)total3, Is.InRange(0.18f, 0.32f), "Level 3: ~25% Common");
            Assert.That(rare3 / (float)total3, Is.InRange(0.30f, 0.44f), "Level 3: ~37% Rare+");
        }

        // ---- Acceptance 2 + 3 ----

        [Test]
        public void Buy_DebitsExactPriceAndMovesItemOnce_RepeatedInputIsRejected()
        {
            var (trader, profile, _) = NewTrader(100000);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            var offer = trader.Offers[0];
            var item = offer.Item;
            var expectedPrice = offer.Definition is EquipmentItemDefinition ? _prices.BuyValue(offer.Definition, item.Rarity) : _prices.FlatPrice(offer.Definition);
            Assert.AreEqual(expectedPrice, offer.Price, "Price = authored base × rarity, never affix quality.");

            Assert.AreEqual(TradeError.None, trader.Buy(0, backpack));
            Assert.AreEqual(100000 - offer.Price, profile.BankedCoins);
            Assert.IsTrue(inventory.Contains(item.InstanceId) || inventory.CountOf(item.DefinitionId) >= item.Quantity, "Item (or its stack units) arrived in the backpack.");
            Assert.IsTrue(offer.IsSold);
            CollectionAssert.Contains(profile.Trader.SoldOfferIndices, 0);

            Assert.AreEqual(TradeError.AlreadySold, trader.Buy(0, backpack));
            Assert.AreEqual(100000 - offer.Price, profile.BankedCoins, "Second click changes nothing.");
            Assert.AreEqual(TradeError.NoSuchOffer, trader.Buy(99, backpack));
        }

        [Test]
        public void Buy_InsufficientFundsOrFullDestination_LeavesEverythingUnchanged()
        {
            var (trader, profile, _) = NewTrader(0);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            Assert.AreEqual(TradeError.InsufficientFunds, trader.Buy(0, backpack));
            Assert.AreEqual(0, profile.BankedCoins);
            Assert.IsFalse(trader.Offers[0].IsSold);
            Assert.AreEqual(0, inventory.BackpackSlots.Count(s => s != null));

            profile.BankedCoins = 100000;
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger"));
            var full = trader.Offers.First(o => o.Definition is EquipmentItemDefinition || !_registry.TryGet(o.Definition.Id, out var d) || !d.IsStackable);
            Assert.AreEqual(TradeError.DestinationRejected, trader.Buy(full.Index, backpack));
            Assert.AreEqual(100000, profile.BankedCoins, "No debit when the destination cannot take the item.");
            Assert.IsFalse(full.IsSold);
        }

        [Test]
        public void Sell_MovesItemToSinkAndCreditsExact35PercentRoundedValue_Once()
        {
            var (trader, profile, _) = NewTrader(0);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var pistol = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare);
            pistol.AddAffixRoll(new AffixRoll("affix_damage", 10));
            storage.TryAdd(pistol);
            var bandages = new ItemInstance("consumable_bandage", 3);
            storage.TryAdd(bandages);
            var liveBandages = storage.Items.Single(i => i.DefinitionId == "consumable_bandage");

            Assert.AreEqual(165, trader.QuoteSellValue(pistol), "P9 Rare buy 475 → sell 166.25 → 165; affixes irrelevant.");
            Assert.AreEqual(TradeError.None, trader.Sell(storage, pistol.InstanceId));
            Assert.AreEqual(165, profile.BankedCoins);
            Assert.IsNull(storage.Find(pistol.InstanceId));
            Assert.IsNotNull(trader.Sink.Find(pistol.InstanceId), "Sold items leave the player's ownership.");
            Assert.AreEqual(TradeError.SourceMissingItem, trader.Sell(storage, pistol.InstanceId), "Selling twice is impossible.");
            Assert.AreEqual(165, profile.BankedCoins);

            Assert.AreEqual(60, trader.QuoteSellValue(liveBandages), "Bandage 60 → 21 → 20 per unit × 3.");
            Assert.AreEqual(TradeError.None, trader.Sell(storage, liveBandages.InstanceId));
            Assert.AreEqual(225, profile.BankedCoins);
        }

        // ---- Acceptance 4 ----

        [Test]
        public void StarterKitItems_CannotBeSold()
        {
            var (trader, profile, _) = NewTrader(0);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var starterPistol = new ItemInstance("weapon_p9_ranger") { IsUnsellable = true };
            storage.TryAdd(starterPistol);

            Assert.AreEqual(0, trader.QuoteSellValue(starterPistol));
            Assert.AreEqual(TradeError.Unsellable, trader.Sell(storage, starterPistol.InstanceId));
            Assert.AreEqual(0, profile.BankedCoins);
            Assert.IsNotNull(storage.Find(starterPistol.InstanceId), "Refused sale keeps the item.");

            var json = JsonUtility.ToJson(starterPistol.ToSnapshot());
            Assert.IsTrue(ItemInstance.FromSnapshot(JsonUtility.FromJson<ItemInstanceSnapshot>(json)).IsUnsellable, "Flag persists.");
        }

        // ---- Acceptance 5 ----

        [Test]
        public void Refresh_OncePerEndedExpedition_DeterministicAndNotOnReopen()
        {
            var (trader, profile, _) = NewTrader(0, 7);
            var initial = Signature(trader);
            var (again, _, _) = NewTrader(0, 7);
            Assert.AreEqual(initial, Signature(again), "Reconstructing the service (reopening the UI) shows the same stock.");

            Assert.IsTrue(trader.RefreshForEndedExpedition(1));
            var afterFirst = Signature(trader);
            Assert.AreNotEqual(initial, afterFirst);
            Assert.AreEqual(1, profile.Trader.RefreshCount);
            Assert.IsFalse(trader.RefreshForEndedExpedition(1), "The same ended expedition never refreshes twice.");
            Assert.IsFalse(trader.RefreshForEndedExpedition(0));
            Assert.AreEqual(afterFirst, Signature(trader));
            Assert.AreEqual(1, profile.Trader.RefreshCount);

            trader.Buy(0, new ListItemContainer("bag"));
            Assert.IsTrue(trader.RefreshForEndedExpedition(2), "Failure or extraction: any ended expedition refreshes.");
            Assert.IsEmpty(profile.Trader.SoldOfferIndices, "Sold flags reset with the new stock.");
            Assert.IsFalse(trader.Offers[0].IsSold);

            var (rebuilt, _, _) = NewTrader(0, 7);
            rebuilt.State.RefreshCount = 2;
            rebuilt.State.LastRefreshedExpedition = 2;
            var (rebuilt2, _, _) = NewTrader(0, 7);
            rebuilt2.RefreshForEndedExpedition(1);
            rebuilt2.RefreshForEndedExpedition(2);
            Assert.AreEqual(Signature(trader), Signature(rebuilt2), "Stock is a pure function of profile seed + refresh count + level.");
        }

        [Test]
        public void Upgrade_Costs2500Then7000_AndRaisesOffersOnNextRefresh()
        {
            var (trader, profile, _) = NewTrader(2499);
            Assert.AreEqual(TradeError.InsufficientFunds, trader.TryUpgrade());
            Assert.AreEqual(1, trader.Level);
            profile.BankedCoins = 2500 + 7000;
            Assert.AreEqual(TradeError.None, trader.TryUpgrade());
            Assert.AreEqual(2, trader.Level);
            Assert.AreEqual(7000, profile.BankedCoins);
            Assert.AreEqual(4, trader.Offers.Count, "Current stock unchanged until the next refresh.");
            trader.RefreshForEndedExpedition(1);
            Assert.AreEqual(5, trader.Offers.Count);
            Assert.AreEqual(TradeError.None, trader.TryUpgrade());
            Assert.AreEqual(3, trader.Level);
            Assert.AreEqual(0, profile.BankedCoins);
            trader.RefreshForEndedExpedition(2);
            Assert.AreEqual(6, trader.Offers.Count);
            Assert.AreEqual(TradeError.AlreadyMaxLevel, trader.TryUpgrade());
        }

        private static string Signature(TraderService trader) =>
            string.Join("|", trader.Offers.Select(o => $"{o.Index}:{o.Definition.Id}:{o.Item.Rarity}:{o.Item.Quantity}:{o.Price}:{string.Join(",", o.Item.AffixRolls.Select(a => a.AffixId + a.Value))}"));
    }
}
