using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 076: in-expedition merchant on Carried Coins, deterministic per depth, no refresh, atomic trades.</summary>
    public class DungeonMerchantTests
    {
        private DungeonMerchantConfig _config;
        private EconomyConfig _economy;
        private PriceService _prices;
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private LootSourceCatalog _loot;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset");
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            Assert.IsNotNull(_config);
            Assert.IsNotNull(_loot);
            _prices = new PriceService(_economy);
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            Assert.IsEmpty(_registry.Problems);
        }

        private (DungeonMerchantService merchant, CoinWallet carried, DungeonMerchantState state) NewMerchant(int coins, int runSeed = 42, int depth = 3, int partySize = 1, DungeonMerchantState state = null)
        {
            var carried = new CoinWallet(CoinDomain.Carried, coins);
            state ??= new DungeonMerchantState(depth);
            var merchant = new DungeonMerchantService(_config, _prices, carried, state, runSeed, partySize, _catalog, id => _registry.TryGet(id, out var d) ? d : null, _loot.RarityTableFor);
            return (merchant, carried, state);
        }

        // ---- Acceptance 1: deterministic per depth/seed, stable across reopen ----

        [Test]
        public void Layout_IsFiveSlots_TwoEquipmentOneConsumableOneAmmoOneRandom()
        {
            CollectionAssert.AreEqual(new[] { MerchantSlotKind.Equipment, MerchantSlotKind.Equipment, MerchantSlotKind.Consumable, MerchantSlotKind.Ammo, MerchantSlotKind.Random }, _config.Slots);
            var (merchant, _, _) = NewMerchant(0);
            Assert.AreEqual(5, merchant.Offers.Count);
            Assert.IsTrue(merchant.Offers[0].Definition is EquipmentItemDefinition && merchant.Offers[0].Definition.Category != ItemCategory.Consumable);
            Assert.IsTrue(merchant.Offers[1].Definition is EquipmentItemDefinition && merchant.Offers[1].Definition.Category != ItemCategory.Consumable);
            Assert.IsInstanceOf<ConsumableDefinition>(merchant.Offers[2].Definition);
            Assert.IsInstanceOf<AmmoItemDefinition>(merchant.Offers[3].Definition);
            Assert.IsTrue(merchant.Offers.All(o => o.Price > 0), "Every offer has a V1 price.");
            Assert.IsTrue(merchant.Offers.All(o => o.Definition is not ConsumableDefinition c || c.IsDropEligible(1)), "Solo: no Defibrillator.");
        }

        [Test]
        public void Stock_IsDeterministic_ForSeedAndDepth_AndStableAcrossReopen()
        {
            var (a, _, stateA) = NewMerchant(0, runSeed: 7, depth: 4);
            var (b, _, _) = NewMerchant(0, runSeed: 7, depth: 4);
            Assert.AreEqual(a.StockSignature, b.StockSignature);
            CollectionAssert.AreEqual(a.Offers.Select(o => o.Item.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray()), b.Offers.Select(o => o.Item.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray()));

            // Reopening within the depth = the same service/state; rebuilding from the same state yields the same stock.
            var (reopened, _, _) = NewMerchant(0, runSeed: 7, depth: 4, state: stateA);
            Assert.AreEqual(a.StockSignature, reopened.StockSignature);

            var (otherDepth, _, _) = NewMerchant(0, runSeed: 7, depth: 5);
            var (otherSeed, _, _) = NewMerchant(0, runSeed: 8, depth: 4);
            Assert.AreNotEqual(a.StockSignature, otherDepth.StockSignature);
            Assert.AreNotEqual(a.StockSignature, otherSeed.StockSignature);
        }

        [Test]
        public void NoRefreshApi_Exists()
        {
            var methods = typeof(DungeonMerchantService).GetMethods().Select(m => m.Name.ToLowerInvariant()).ToArray();
            Assert.IsFalse(methods.Any(m => m.Contains("refresh") || m.Contains("reroll") || m.Contains("regenerate")), "58: no in-room refresh.");
        }

        [Test]
        public void Quality_ScalesWithDepth_ThroughTheRarityTable()
        {
            int RarePlus(int depth)
            {
                var count = 0;
                for (var seed = 0; seed < 300; seed++)
                {
                    var (m, _, _) = NewMerchant(0, seed, depth);
                    count += m.Offers.Take(2).Count(o => o.Item.Rarity >= Rarity.Rare);
                }

                return count;
            }

            Assert.Greater(RarePlus(30), RarePlus(1) + 60, "Deeper stock rolls markedly more Rare+ equipment.");
        }

        // ---- Acceptance 2 + 3: buy once, Carried debited once, Banked untouched ----

        [Test]
        public void Buy_DebitsCarriedCoinsOnce_MovesTheItem_AndTheOfferCannotBeBoughtTwice()
        {
            var (merchant, carried, state) = NewMerchant(100000);
            var banked = new CoinWallet(CoinDomain.Banked, 777);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            var offer = merchant.Offers[0];
            var item = offer.Item;
            Assert.AreEqual(_prices.BuyValue(offer.Definition, item.Rarity), offer.Price);
            var bought = 0;
            merchant.Bought += _ => bought++;

            Assert.AreEqual(TradeError.None, merchant.Buy(0, backpack));
            Assert.AreEqual(100000 - offer.Price, carried.Balance);
            Assert.IsTrue(inventory.Contains(item.InstanceId));
            Assert.IsTrue(offer.IsSold);
            CollectionAssert.Contains(state.SoldOfferIndices, 0);

            Assert.AreEqual(TradeError.AlreadySold, merchant.Buy(0, backpack));
            Assert.AreEqual(100000 - offer.Price, carried.Balance, "Second purchase debits nothing.");
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null && s.InstanceId == item.InstanceId));
            Assert.AreEqual(1, bought);
            Assert.AreEqual(777, banked.Balance, "Banked Coins are never involved.");
            Assert.AreEqual(TradeError.NoSuchOffer, merchant.Buy(99, backpack));

            // Reopening after a purchase within the depth keeps the sold state.
            var (reopened, _, _) = NewMerchant(0, state: state);
            Assert.IsTrue(reopened.Offers[0].IsSold);
            Assert.AreEqual(TradeError.AlreadySold, reopened.Buy(0, backpack));
        }

        [Test]
        public void AmmoOffer_IsTheApprovedBundle_AndLandsAsUnitsInTheBackpack()
        {
            var (merchant, carried, _) = NewMerchant(10000);
            var offer = merchant.Offers[3];
            var ammo = (AmmoItemDefinition)offer.Definition;
            Assert.IsTrue(_economy.TryGetAmmoBundle(ammo.AmmoType, out var bundle));
            Assert.AreEqual(bundle.Units, offer.Item.Quantity);
            Assert.AreEqual(bundle.Price, offer.Price);

            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            Assert.AreEqual(TradeError.None, merchant.Buy(3, new BackpackContainer(inventory)));
            Assert.AreEqual(bundle.Units, inventory.Get(ammo.AmmoType));
            Assert.AreEqual(10000 - bundle.Price, carried.Balance);
        }

        // ---- Requirement 4: insufficient funds / full destination consume nothing ----

        [Test]
        public void InsufficientFunds_AndFullBackpack_ConsumeNoCoinsAndNoOffer()
        {
            var (merchant, carried, _) = NewMerchant(0);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            var offer = merchant.Offers[0];

            Assert.AreEqual(TradeError.InsufficientFunds, merchant.Buy(0, backpack));
            Assert.AreEqual(0, carried.Balance);
            Assert.IsFalse(offer.IsSold);

            carried.Credit(offer.Price + 5, "test");
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger"));
            Assert.AreEqual(TradeError.DestinationRejected, merchant.Buy(0, backpack));
            Assert.AreEqual(offer.Price + 5, carried.Balance, "Rejected destination costs nothing.");
            Assert.IsFalse(offer.IsSold);
            Assert.IsFalse(inventory.Contains(offer.Item.InstanceId));
        }

        // ---- Requirement 5: selling is defined by 58 ("dungeon-held items for Carried Coins") ----

        [Test]
        public void Sell_CreditsThe35PercentRoundedValueInCarriedCoins_AndMovesTheItemOut()
        {
            var (merchant, carried, _) = NewMerchant(0);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            var rifle = new ItemInstance("weapon_p9_ranger") { Rarity = Rarity.Rare };
            inventory.TryAddToBackpack(rifle);
            var definition = _registry.TryGet("weapon_p9_ranger", out var d) ? d : null;
            var expected = _prices.SellValue(definition, Rarity.Rare);
            Assert.AreEqual(expected, merchant.QuoteSellValue(rifle));
            Assert.Greater(expected, 0);
            Assert.AreEqual(0, expected % _economy.RoundingStep);

            Assert.AreEqual(TradeError.None, merchant.Sell(backpack, rifle.InstanceId));
            Assert.AreEqual(expected, carried.Balance);
            Assert.IsFalse(inventory.Contains(rifle.InstanceId));
            Assert.IsNotNull(merchant.Sink.Find(rifle.InstanceId));
            Assert.AreEqual(TradeError.SourceMissingItem, merchant.Sell(backpack, rifle.InstanceId), "Cannot sell twice.");
            Assert.AreEqual(expected, carried.Balance);

            var starter = new ItemInstance("weapon_p9_ranger") { IsUnsellable = true };
            inventory.TryAddToBackpack(starter);
            Assert.AreEqual(TradeError.Unsellable, merchant.Sell(backpack, starter.InstanceId));
        }

        [Test]
        public void Merchant_RejectsBankedWallet()
        {
            Assert.Throws<System.ArgumentException>(() => new DungeonMerchantService(_config, _prices, new CoinWallet(CoinDomain.Banked), new DungeonMerchantState(1), 1, 1, _catalog, id => null, _loot.RarityTableFor));
        }
    }
}
