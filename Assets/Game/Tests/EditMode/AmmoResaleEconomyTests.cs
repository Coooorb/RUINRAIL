using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>
    /// Ammo resale exploit fix: the ammo bundle price is a price for the whole bundle, never a per-round value, so the
    /// payout for an exact quantity is 15 % of its equivalent purchase value rounded down — never the 35 % item rule
    /// applied per round and multiplied by the stack. UI quote and authoritative payout are the same number.
    /// </summary>
    public class AmmoResaleEconomyTests
    {
        private EconomyConfig _economy;
        private PriceService _prices;
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private DungeonMerchantConfig _merchantConfig;
        private TraderConfig _traderConfig;
        private LootSourceCatalog _loot;

        [SetUp]
        public void SetUp()
        {
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _merchantConfig = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset");
            _traderConfig = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            _loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            Assert.IsNotNull(_economy);
            Assert.IsNotNull(_merchantConfig);
            Assert.IsNotNull(_traderConfig);
            _prices = new PriceService(_economy);
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null)
                .ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            Assert.IsEmpty(_registry.Problems);
        }

        private AmmoItemDefinition Ammo(AmmoType type) => _catalog.OfType<AmmoItemDefinition>().First(a => a.AmmoType == type);

        private DungeonMerchantService NewMerchant(CoinWallet carried) =>
            new(_merchantConfig, _prices, carried, new DungeonMerchantState(2), 42, 1, _catalog, id => _registry.TryGet(id, out var d) ? d : null, _loot.RarityTableFor);

        private TraderService NewTrader(CoinWallet banked) =>
            new(_traderConfig, _prices, banked, new TraderState(), 7, _catalog, id => _registry.TryGet(id, out var d) ? d : null);

        // ---- The rule ----

        [Test]
        public void AmmoSellPercent_IsFifteenPercentOfPurchaseValue()
        {
            Assert.AreEqual(15, _economy.AmmoSellPercentOfPurchaseValue);
        }

        [TestCase(AmmoType.Light, 60, 60, 9)]     // full bundle: 60 coins → 9
        [TestCase(AmmoType.Medium, 40, 70, 10)]   // 70 → 10.5 → 10
        [TestCase(AmmoType.Heavy, 20, 90, 13)]    // 90 → 13.5 → 13
        [TestCase(AmmoType.Shells, 12, 80, 12)]   // 80 → 12
        public void FullBundle_PaysFifteenPercentRoundedDown(AmmoType type, int units, int price, int expected)
        {
            Assert.IsTrue(_economy.TryGetAmmoBundle(type, out var bundle));
            Assert.AreEqual(units, bundle.Units);
            Assert.AreEqual(price, bundle.Price);
            Assert.AreEqual(expected, _prices.AmmoSellValue(Ammo(type), units));
            Assert.AreEqual(price, _prices.AmmoPurchaseValue(Ammo(type), units));
        }

        [TestCase(AmmoType.Light, 180, 27)]  // full stack of three bundles: 180 coins of ammo → 27 (was 20 × 180 = 3600)
        [TestCase(AmmoType.Light, 30, 4)]    // half a bundle: 30 coins → 4.5 → 4
        [TestCase(AmmoType.Light, 10, 1)]    // 10 coins → 1.5 → 1
        [TestCase(AmmoType.Light, 1, 0)]     // one round: 1 coin → 0.15 → 0
        [TestCase(AmmoType.Medium, 120, 31)] // 210 coins → 31.5 → 31 (was 25 × 120 = 3000)
        [TestCase(AmmoType.Medium, 7, 1)]    // 12.25 coins → 1.83 → 1
        [TestCase(AmmoType.Heavy, 60, 40)]   // 270 coins → 40.5 → 40 (was 30 × 60 = 1800)
        [TestCase(AmmoType.Heavy, 3, 2)]     // 13.5 coins → 2.02 → 2
        [TestCase(AmmoType.Shells, 40, 40)]  // 266.67 coins → 40
        [TestCase(AmmoType.Shells, 5, 5)]    // 33.33 coins → 5
        [TestCase(AmmoType.Shells, 1, 1)]    // 6.67 coins → 1
        public void PartialAndFullStacks_PayExactlyFifteenPercentOfEquivalentPurchaseValue_RoundedDown(AmmoType type, int quantity, int expected)
        {
            Assert.AreEqual(expected, _prices.AmmoSellValue(Ammo(type), quantity));
        }

        [Test]
        public void Payout_IsMonotonic_NeverNegative_AndNeverAboveTheOldPerRoundBug()
        {
            foreach (var type in new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells })
            {
                var ammo = Ammo(type);
                Assert.IsTrue(_economy.TryGetAmmoBundle(type, out var bundle));
                var previous = 0;
                for (var q = 0; q <= ammo.MaxStack; q++)
                {
                    var payout = _prices.AmmoSellValue(ammo, q);
                    Assert.GreaterOrEqual(payout, 0, $"{type} ×{q}");
                    Assert.GreaterOrEqual(payout, previous, $"{type} ×{q} must not pay less than ×{q - 1}");
                    Assert.LessOrEqual(payout, _prices.AmmoPurchaseValue(ammo, q) * 15 / 100 + 1, $"{type} ×{q} is bounded by 15 % of the purchase value");
                    previous = payout;
                }

                // The old formula: 35 % of the *bundle* price rounded to 5, times the quantity.
                var oldFullStack = _prices.SellValue(bundle.Price) * ammo.MaxStack;
                Assert.Greater(oldFullStack, _prices.AmmoPurchaseValue(ammo, ammo.MaxStack), $"{type}: the old formula paid more than the ammo cost — the exploit");
                Assert.Less(_prices.AmmoSellValue(ammo, ammo.MaxStack), _prices.AmmoPurchaseValue(ammo, ammo.MaxStack) / 4, $"{type}: the fixed payout is a small fraction of what the ammo costs");
            }

            Assert.AreEqual(0, _prices.AmmoSellValue(Ammo(AmmoType.Light), 0));
            Assert.AreEqual(0, _prices.AmmoSellValue(Ammo(AmmoType.Light), -5));
            Assert.AreEqual(0, _prices.AmmoSellValue(null, 60));
            Assert.GreaterOrEqual(_prices.AmmoSellValue(Ammo(AmmoType.Heavy), int.MaxValue), 0, "no overflow");
        }

        [Test]
        public void NoRarityMultiplier_AppliesToAmmo()
        {
            var ammo = Ammo(AmmoType.Light);
            var merchant = NewMerchant(new CoinWallet(CoinDomain.Carried));
            foreach (var rarity in new[] { Rarity.Common, Rarity.Uncommon, Rarity.Rare, Rarity.Epic, Rarity.Legendary })
            {
                var stack = new ItemInstance(ammo.Id, 60) { Rarity = rarity };
                Assert.AreEqual(9, merchant.QuoteSellValue(stack), $"rarity {rarity} must not change an ammo payout");
            }
        }

        // ---- The Merchant: quote == payout, once, cannot oversell ----

        [Test]
        public void DungeonMerchant_QuoteMatchesPayout_SellsOnce_AndCannotOversell()
        {
            foreach (var type in new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells })
            {
                var ammo = Ammo(type);
                var carried = new CoinWallet(CoinDomain.Carried, 100);
                var merchant = NewMerchant(carried);
                var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
                var backpack = new BackpackContainer(inventory);
                var quantity = ammo.MaxStack / 2;
                // A stackable add is absorbed into the container's own stack instance (the source is zeroed): sell what the backpack holds.
                Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance(ammo.Id, quantity)));
                var stack = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == ammo.Id);
                Assert.AreEqual(quantity, stack.Quantity);
                var expected = _prices.AmmoSellValue(ammo, quantity);
                Assert.AreEqual(expected, merchant.QuoteSellValue(stack), $"{type}: the UI quote is the authoritative payout");

                var paid = -1;
                merchant.Sold += (_, value) => paid = value;
                var before = carried.Balance;
                Assert.AreEqual(TradeError.None, merchant.Sell(backpack, stack.InstanceId));
                Assert.AreEqual(expected, carried.Balance - before, $"{type}: exact coin change");
                Assert.AreEqual(expected, paid, $"{type}: the Sold event reports the same payout");
                Assert.AreEqual(0, inventory.Get(type), $"{type}: the whole stack left the backpack — no oversell");

                // A repeated request for the same stack pays nothing more.
                Assert.AreEqual(TradeError.SourceMissingItem, merchant.Sell(backpack, stack.InstanceId));
                Assert.AreEqual(expected, carried.Balance - before, $"{type}: a retry never pays twice");
            }
        }

        [Test]
        public void DungeonMerchant_TinyStack_IsWorthNothing_AndCannotBeSoldForCoins()
        {
            var ammo = Ammo(AmmoType.Light);
            var carried = new CoinWallet(CoinDomain.Carried);
            var merchant = NewMerchant(carried);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance(ammo.Id, 1)));
            var one = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == ammo.Id);
            Assert.AreEqual(1, one.Quantity);
            Assert.AreEqual(0, merchant.QuoteSellValue(one));
            Assert.AreEqual(TradeError.NoValue, merchant.Sell(backpack, one.InstanceId), "a payout of 0 is not a sale — no minimum-1-coin micro-stack exploit");
            Assert.AreEqual(0, carried.Balance);
            Assert.AreEqual(1, inventory.Get(AmmoType.Light), "the round stays in the backpack");
        }

        [Test]
        public void DungeonMerchant_BuyThenImmediateSell_LosesMeaningfulValue()
        {
            var carried = new CoinWallet(CoinDomain.Carried, 5000);
            var merchant = NewMerchant(carried);
            var ammoOffer = merchant.Offers.FirstOrDefault(o => o.Definition is AmmoItemDefinition);
            Assert.IsNotNull(ammoOffer, "the merchant layout always has an ammo slot");
            var ammo = (AmmoItemDefinition)ammoOffer.Definition;
            Assert.IsTrue(_economy.TryGetAmmoBundle(ammo.AmmoType, out var bundle));
            Assert.AreEqual(bundle.Price, ammoOffer.Price);

            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var backpack = new BackpackContainer(inventory);
            var start = carried.Balance;
            Assert.AreEqual(TradeError.None, merchant.Buy(ammoOffer.Index, backpack));
            Assert.AreEqual(start - bundle.Price, carried.Balance);
            var stack = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == ammo.Id);
            Assert.AreEqual(bundle.Units, stack.Quantity);

            var payout = merchant.QuoteSellValue(stack);
            Assert.AreEqual(TradeError.None, merchant.Sell(backpack, stack.InstanceId));
            Assert.AreEqual(start - bundle.Price + payout, carried.Balance);
            Assert.Less(carried.Balance, start, "buy → immediate sell must lose coins");
            Assert.LessOrEqual(payout * 100, bundle.Price * 15, "the round trip returns at most 15 % of the purchase price");
            Assert.GreaterOrEqual(start - carried.Balance, bundle.Price * 85 / 100, "and loses at least 85 % of it");
        }

        // ---- The Shelter Trader uses the same rule ----

        [Test]
        public void ShelterTrader_UsesTheSameAmmoResaleRule()
        {
            var banked = new CoinWallet(CoinDomain.Banked, 0);
            var trader = NewTrader(banked);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            Assert.IsTrue(storage.TryAdd(new ItemInstance(Ammo(AmmoType.Heavy).Id, 20)));
            var heavy = storage.Items.First(i => i.DefinitionId == Ammo(AmmoType.Heavy).Id);
            Assert.AreEqual(20, heavy.Quantity);
            Assert.AreEqual(13, trader.QuoteSellValue(heavy), "Heavy ×20 = one 90-coin bundle → 13.5 → 13");
            Assert.AreEqual(TradeError.None, trader.Sell(storage, heavy.InstanceId));
            Assert.AreEqual(13, banked.Balance);
            Assert.AreEqual(TradeError.SourceMissingItem, trader.Sell(storage, heavy.InstanceId));
            Assert.AreEqual(13, banked.Balance);
        }

        // ---- Non-ammo stacks keep the approved 35 % per-unit rule ----

        [Test]
        public void ConsumableStacks_KeepTheApprovedPerUnitRule()
        {
            var merchant = NewMerchant(new CoinWallet(CoinDomain.Carried));
            var bandages = new ItemInstance("consumable_bandage", 3);
            Assert.AreEqual(60, merchant.QuoteSellValue(bandages), "Bandage 60 → 21 → 20 per unit × 3 — a consumable price is a per-unit price");
        }
    }
}
