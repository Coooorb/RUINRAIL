using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class WorkshopServiceTests
    {
        private WorkshopConfig _config;
        private TraderConfig _traderConfig;
        private EconomyConfig _economy;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset");
            _traderConfig = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
        }

        private (WorkshopService workshop, PlayerProfile profile, Storage storage, TraderService trader) Build(int coins)
        {
            var profile = new PlayerProfile { BankedCoins = coins, ProfileSeed = 3 };
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            var trader = new TraderService(_traderConfig, new PriceService(_economy), banked, profile.Trader, profile.ProfileSeed, _registry.Definitions, id => _registry.TryGet(id, out var d) ? d : null);
            var workshop = new WorkshopService(_config, banked, profile.Workshop, storage, trader) { IsAtBase = true };
            return (workshop, profile, storage, trader);
        }

        // ---- Acceptance 1 ----

        [Test]
        public void Tiers_MatchApprovedCapacitiesAndCosts_NoTierBeyond120()
        {
            Assert.AreEqual(60, _config.BaseStorageCapacity);
            Assert.AreEqual(3, _config.MaxStorageTier);
            Assert.AreEqual(60, _config.StorageCapacityForTier(0));
            Assert.AreEqual(80, _config.StorageCapacityForTier(1));
            Assert.AreEqual(100, _config.StorageCapacityForTier(2));
            Assert.AreEqual(120, _config.StorageCapacityForTier(3));
            Assert.AreEqual(120, _config.StorageCapacityForTier(9), "Nothing beyond 120 in V1.");
            Assert.AreEqual(1500, _config.StorageUpgradeCostFrom(0));
            Assert.AreEqual(4000, _config.StorageUpgradeCostFrom(1));
            Assert.AreEqual(8000, _config.StorageUpgradeCostFrom(2));
            Assert.AreEqual(0, _config.StorageUpgradeCostFrom(3));
            Assert.AreEqual(2500, _traderConfig.UpgradeCostFrom(1));
            Assert.AreEqual(7000, _traderConfig.UpgradeCostFrom(2));
        }

        [Test]
        public void StorageUpgrades_DebitAndExpandAtomically_ThenRefuseAtMax()
        {
            var (workshop, profile, storage, _) = Build(1500 + 4000 + 8000);
            Assert.AreEqual(60, storage.Capacity);

            Assert.AreEqual(UpgradeError.None, workshop.BuyStorageUpgrade());
            Assert.AreEqual(80, storage.Capacity);
            Assert.AreEqual(1, profile.Workshop.StorageTier);
            Assert.AreEqual(12000, profile.BankedCoins);
            Assert.AreEqual(UpgradeError.None, workshop.BuyStorageUpgrade());
            Assert.AreEqual(100, storage.Capacity);
            Assert.AreEqual(8000, profile.BankedCoins);
            Assert.AreEqual(UpgradeError.None, workshop.BuyStorageUpgrade());
            Assert.AreEqual(120, storage.Capacity);
            Assert.AreEqual(0, profile.BankedCoins);
            Assert.IsTrue(workshop.IsStorageMaxed);

            profile.BankedCoins = 50000;
            Assert.AreEqual(UpgradeError.AlreadyMaxTier, workshop.BuyStorageUpgrade(), "No fourth tier, no charge.");
            Assert.AreEqual(50000, profile.BankedCoins);
            Assert.AreEqual(120, storage.Capacity);
        }

        // ---- Acceptance 2 ----

        [Test]
        public void InsufficientFunds_OrNotAtBase_RejectWithoutChangingAnything()
        {
            var (workshop, profile, storage, trader) = Build(1499);
            Assert.AreEqual(UpgradeError.InsufficientFunds, workshop.BuyStorageUpgrade());
            Assert.AreEqual(1499, profile.BankedCoins);
            Assert.AreEqual(60, storage.Capacity);
            Assert.AreEqual(0, profile.Workshop.StorageTier);
            Assert.AreEqual(UpgradeError.InsufficientFunds, workshop.BuyTraderUpgrade());
            Assert.AreEqual(1, trader.Level);

            profile.BankedCoins = 100000;
            workshop.IsAtBase = false;
            Assert.AreEqual(UpgradeError.NotAtBase, workshop.BuyStorageUpgrade());
            Assert.AreEqual(UpgradeError.NotAtBase, workshop.BuyTraderUpgrade());
            Assert.AreEqual(100000, profile.BankedCoins);
            Assert.AreEqual(60, storage.Capacity);
            Assert.AreEqual(1, trader.Level);
        }

        // ---- Acceptance 3 ----

        [Test]
        public void StorageExpansion_KeepsEveryItemIdAndSlot_AndAPersistedTierRestoresCapacityWithoutLoss()
        {
            var (workshop, profile, storage, _) = Build(1500);
            var items = Enumerable.Range(0, 60).Select(_ => new ItemInstance("weapon_p9_ranger")).ToList();
            foreach (var item in items) Assert.IsTrue(storage.TryAdd(item));
            Assert.AreEqual(0, storage.FreeSlots);
            var before = storage.Slots.Select(s => s?.InstanceId).ToList();

            Assert.AreEqual(UpgradeError.None, workshop.BuyStorageUpgrade());

            Assert.AreEqual(80, storage.Capacity);
            Assert.AreEqual(20, storage.FreeSlots);
            CollectionAssert.AreEqual(before, storage.Slots.Take(60).Select(s => s?.InstanceId), "Existing items keep their ids and positions.");
            Assert.IsTrue(storage.Slots.Skip(60).All(s => s == null));
            Assert.IsTrue(storage.TryAdd(new ItemInstance("armor_scrap_vest")));
            Assert.IsNotNull(storage.Slots[60]);

            // Reload: a fresh storage of base size with the persisted tier expands before use.
            var snapshot = storage.ToSnapshot();
            var reloaded = Storage.FromSnapshot(new StorageSnapshot { Capacity = 60, Slots = snapshot.Slots.Where(e => e.Slot < 60).ToArray() }, id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            Assert.AreEqual(60, reloaded.Capacity);
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            new WorkshopService(_config, banked, profile.Workshop, reloaded, null);
            Assert.AreEqual(80, reloaded.Capacity, "Persisted tier 1 restores 80 slots.");
            Assert.AreEqual(60, reloaded.OccupiedSlots);
            Assert.IsFalse(reloaded.ExpandTo(60), "Shrinking is never allowed.");
        }

        // ---- Acceptance 4 ----

        [Test]
        public void TraderUpgrade_ThroughWorkshop_TakesEffectOnNextRefresh()
        {
            var (workshop, profile, _, trader) = Build(2500);
            var events = 0;
            workshop.TraderUpgraded += _ => events++;
            Assert.AreEqual(2500, workshop.NextTraderUpgradeCost);
            Assert.AreEqual(UpgradeError.None, workshop.BuyTraderUpgrade());
            Assert.AreEqual(0, profile.BankedCoins);
            Assert.AreEqual(2, trader.Level);
            Assert.AreEqual(2, profile.Trader.Level, "Persisted.");
            Assert.AreEqual(1, events);
            Assert.AreEqual(4, trader.Offers.Count);
            trader.RefreshForEndedExpedition(1);
            Assert.AreEqual(5, trader.Offers.Count, "Upgraded count on the next applicable refresh.");
            Assert.AreEqual(7000, workshop.NextTraderUpgradeCost);
            Assert.AreEqual(UpgradeError.InsufficientFunds, workshop.BuyTraderUpgrade());
            Assert.AreEqual(2, trader.Level, "Duplicate/unaffordable purchase charges nothing.");
        }

        [Test]
        public void Workshop_HasOnlyTheTwoApprovedLines_AndNoMaterials()
        {
            var members = typeof(WorkshopService).GetMethods().Where(m => m.Name.StartsWith("Buy")).Select(m => m.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "BuyStorageUpgrade", "BuyTraderUpgrade" }, members);
            Assert.IsFalse(typeof(WorkshopConfig).GetProperties().Any(p => p.Name.ToLowerInvariant().Contains("material") || p.Name.ToLowerInvariant().Contains("scrap")));
        }
    }
}
