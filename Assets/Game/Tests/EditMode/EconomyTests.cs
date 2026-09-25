using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class EconomyTests
    {
        private EconomyConfig _config;
        private PriceService _prices;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            Assert.IsNotNull(_config);
            _prices = new PriceService(_config);
        }

        private static T LoadItem<T>(string id) where T : ItemDefinition
        {
            return AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Single(d => d.Id == id);
        }

        // ---- Acceptance 1: exact prices and formulas ----

        [Test]
        public void RarityMultipliers_SellPercent_AndRounding_MatchApprovedValues()
        {
            Assert.AreEqual(100, _config.RarityPercent(Rarity.Common));
            Assert.AreEqual(135, _config.RarityPercent(Rarity.Uncommon));
            Assert.AreEqual(190, _config.RarityPercent(Rarity.Rare));
            Assert.AreEqual(300, _config.RarityPercent(Rarity.Epic));
            Assert.AreEqual(500, _config.RarityPercent(Rarity.Legendary));
            Assert.AreEqual(35, _config.SellPercentOfBuyValue);
            Assert.AreEqual(5, _config.RoundingStep);
            Assert.AreEqual(2500, _config.SkillRespecPrice);

            // P9 Ranger (Pistol 250): Common 250, Uncommon 337.5 → 340, Rare 475, Epic 750, Legendary 1250.
            Assert.AreEqual(250, _prices.BuyValue(250, Rarity.Common));
            Assert.AreEqual(340, _prices.BuyValue(250, Rarity.Uncommon));
            Assert.AreEqual(475, _prices.BuyValue(250, Rarity.Rare));
            Assert.AreEqual(750, _prices.BuyValue(250, Rarity.Epic));
            Assert.AreEqual(1250, _prices.BuyValue(250, Rarity.Legendary));
            // Sell = 35% rounded to nearest 5: 250 → 87.5 → 90; 340 → 119 → 120; 475 → 166.25 → 165; 1250 → 437.5 → 440.
            Assert.AreEqual(90, _prices.SellValue(250));
            Assert.AreEqual(120, _prices.SellValue(340));
            Assert.AreEqual(165, _prices.SellValue(475));
            Assert.AreEqual(440, _prices.SellValue(1250));
            // Blaster (650) Uncommon: 877.5 → 880; Accessory (300) Rare: 570.
            Assert.AreEqual(880, _prices.BuyValue(650, Rarity.Uncommon));
            Assert.AreEqual(570, _prices.BuyValue(300, Rarity.Rare));
        }

        [Test]
        public void BasePrices_AreDataDriven_ForEveryAuthoredItemType()
        {
            var expectedWeapon = new Dictionary<WeaponClass, int>
            {
                [WeaponClass.Pistol] = 250, [WeaponClass.Smg] = 350, [WeaponClass.AssaultRifle] = 450, [WeaponClass.BattleRifle] = 500,
                [WeaponClass.Shotgun] = 450, [WeaponClass.Sniper] = 600, [WeaponClass.Bow] = 400, [WeaponClass.RocketLauncher] = 700,
                [WeaponClass.Blaster] = 650, [WeaponClass.Knife] = 250, [WeaponClass.Spear] = 350
            };
            foreach (var kv in expectedWeapon)
            {
                Assert.IsTrue(_config.TryGetWeaponClassPrice(kv.Key, out var price), kv.Key.ToString());
                Assert.AreEqual(kv.Value, price, kv.Key.ToString());
            }

            Assert.AreEqual(250, _prices.FlatPrice(LoadItem<RangedWeaponDefinition>("weapon_p9_ranger")));
            Assert.AreEqual(450, _prices.FlatPrice(LoadItem<RangedWeaponDefinition>("weapon_breacher_12")));
            Assert.AreEqual(650, _prices.FlatPrice(LoadItem<BlasterWeaponDefinition>("weapon_pulse_carbine_b1")));
            Assert.AreEqual(400, _prices.FlatPrice(LoadItem<BowWeaponDefinition>("weapon_recurve_bow")));
            Assert.AreEqual(250, _prices.FlatPrice(LoadItem<MeleeWeaponDefinition>("weapon_field_knife")));

            var expectedArmor = new Dictionary<string, int>
            {
                ["armor_scrap_vest"] = 300, ["armor_scout_rig"] = 350, ["armor_riot_armor"] = 450, ["armor_heavy_plate"] = 650, ["armor_blast_suit"] = 500,
                ["armor_medic_harness"] = 450, ["armor_combat_harness"] = 400, ["armor_reinforced_exo_rig"] = 600, ["armor_runner_suit"] = 350
            };
            foreach (var kv in expectedArmor) Assert.AreEqual(kv.Value, _prices.FlatPrice(LoadItem<ArmorDefinition>(kv.Key)), kv.Key);

            foreach (var accessory in AssetDatabase.FindAssets("t:AccessoryDefinition").Select(g => AssetDatabase.LoadAssetAtPath<AccessoryDefinition>(AssetDatabase.GUIDToAssetPath(g))))
            {
                Assert.AreEqual(300, _prices.FlatPrice(accessory), accessory.Id);
            }

            var expectedConsumable = new Dictionary<string, int>
            {
                ["consumable_bandage"] = 60, ["consumable_frag_grenade"] = 80, ["consumable_medkit"] = 150, ["consumable_combat_stim"] = 140,
                ["consumable_smoke_grenade"] = 110, ["consumable_shock_grenade"] = 220, ["consumable_damage_stim"] = 250,
                ["consumable_armor_injector"] = 250, ["consumable_incendiary_grenade"] = 220, ["consumable_defibrillator"] = 1500
            };
            foreach (var kv in expectedConsumable) Assert.AreEqual(kv.Value, _prices.FlatPrice(LoadItem<ConsumableDefinition>(kv.Key)), kv.Key);

            var bundles = new Dictionary<AmmoType, (int units, int price)> { [AmmoType.Light] = (60, 60), [AmmoType.Medium] = (40, 70), [AmmoType.Heavy] = (20, 90), [AmmoType.Shells] = (12, 80) };
            foreach (var kv in bundles)
            {
                Assert.IsTrue(_config.TryGetAmmoBundle(kv.Key, out var bundle));
                Assert.AreEqual(kv.Value.units, bundle.Units, kv.Key.ToString());
                Assert.AreEqual(kv.Value.price, bundle.Price, kv.Key.ToString());
            }

            Assert.AreEqual(1250, _prices.BuyValue(LoadItem<RangedWeaponDefinition>("weapon_p9_ranger"), new ItemInstance("weapon_p9_ranger", 1, Rarity.Legendary)));
            Assert.AreEqual(165, _prices.SellValue(LoadItem<RangedWeaponDefinition>("weapon_p9_ranger"), Rarity.Rare));
        }

        [Test]
        public void EventPrices_FollowDepthFormulasWithCaps()
        {
            Assert.AreEqual(250, _prices.EventPrice(DungeonEventPriceKind.LockedVault, 1));
            Assert.AreEqual(475, _prices.EventPrice(DungeonEventPriceKind.LockedVault, 10));
            Assert.AreEqual(1000, _prices.EventPrice(DungeonEventPriceKind.LockedVault, 31), "250 + 25 × 30 = 1000 exactly at cap");
            Assert.AreEqual(1000, _prices.EventPrice(DungeonEventPriceKind.LockedVault, 100));
            Assert.AreEqual(100, _prices.EventPrice(DungeonEventPriceKind.BrokenMachine, 1));
            Assert.AreEqual(400, _prices.EventPrice(DungeonEventPriceKind.BrokenMachine, 50));
            Assert.AreEqual(150, _prices.EventPrice(DungeonEventPriceKind.MedicalStationHeal, 1));
            Assert.AreEqual(300, _prices.EventPrice(DungeonEventPriceKind.MedicalStationHeal, 11));
            Assert.AreEqual(600, _prices.EventPrice(DungeonEventPriceKind.MedicalStationHeal, 40));
            Assert.AreEqual(500, _prices.EventPrice(DungeonEventPriceKind.MedicalStationRevive, 1));
            Assert.AreEqual(1500, _prices.EventPrice(DungeonEventPriceKind.MedicalStationRevive, 60));
            // 59 Deep-Depth Reward Continuation: exactly flat through Depth 30, then a bounded square-root curve.
            Assert.AreEqual(1f, _config.CoinRewardMultiplier(1), 0.0001f);
            Assert.AreEqual(1f, _config.CoinRewardMultiplier(30), 0.0001f, "D1-D30 coin rewards are exactly unchanged.");
            Assert.AreEqual(1.22f, _config.CoinRewardMultiplier(40), 0.01f);
            Assert.AreEqual(1.31f, _config.CoinRewardMultiplier(50), 0.01f);
            Assert.AreEqual(1.59f, _config.CoinRewardMultiplier(100), 0.01f);
            Assert.AreEqual(1.75f, _config.CoinRewardMultiplier(1000), 0.01f, "the curve stops at its authored cap.");
            Assert.AreEqual(1f, _config.XpRewardMultiplier(30), 0.0001f, "D1-D30 XP is exactly unchanged.");
            Assert.AreEqual(_config.CoinRewardMultiplier(75), _config.XpRewardMultiplier(75), 0.0001f, "coins and XP share the curve.");
            Assert.AreEqual(40, _prices.ScaleCoinReward(40, 20), "the curve does not touch Depth 20.");
        }

        // ---- Acceptance 2 + 3 + 4: domains and atomic debits ----

        [Test]
        public void Wallet_RejectsNegativeAndInsufficient_AtomicallyAndEmitsReceipts()
        {
            var wallet = new CoinWallet(CoinDomain.Carried, 100);
            var receipts = new List<CoinReceipt>();
            wallet.Changed += r => receipts.Add(r);

            Assert.AreEqual(CoinError.InvalidAmount, wallet.Credit(0).Error);
            Assert.AreEqual(CoinError.InvalidAmount, wallet.Credit(-5).Error);
            Assert.AreEqual(CoinError.InvalidAmount, wallet.Debit(-5).Error);
            var tooMuch = wallet.Debit(101, "vault");
            Assert.IsFalse(tooMuch.Success);
            Assert.AreEqual(CoinError.InsufficientFunds, tooMuch.Error);
            Assert.AreEqual(100, wallet.Balance, "Failed debit leaves the balance unchanged.");
            Assert.IsEmpty(receipts);

            var ok = wallet.Debit(100, "vault");
            Assert.IsTrue(ok.Success);
            Assert.AreEqual(0, wallet.Balance);
            Assert.AreEqual(CoinTransactionKind.Debit, ok.Receipt.Kind);
            Assert.AreEqual(CoinDomain.Carried, ok.Receipt.Domain);
            Assert.AreEqual(100, ok.Receipt.Amount);
            Assert.AreEqual(0, ok.Receipt.BalanceAfter);
            Assert.AreEqual("vault", ok.Receipt.Reason);
            Assert.AreEqual(CoinError.InsufficientFunds, wallet.Debit(1).Error, "Never negative.");
            Assert.IsFalse(wallet.CanAfford(1));
            Assert.AreEqual(1, receipts.Count);
        }

        [Test]
        public void CarriedAndBanked_NeverCross_ExceptThroughTheExtractionTransfer()
        {
            var profile = new PlayerProfile { BankedCoins = 100 };
            var banked = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            var carried = new CoinWallet(CoinDomain.Carried);
            carried.Credit(250, "pickup");
            Assert.AreEqual(100, profile.BankedCoins, "Pickups never touch the bank.");

            Assert.IsFalse(CoinTransfer.Move(carried, carried, 10, "self").Success);
            var partial = CoinTransfer.Move(carried, banked, 300, "test");
            Assert.IsFalse(partial.Success);
            Assert.AreEqual(250, carried.Balance);
            Assert.AreEqual(100, banked.Balance);

            var move = CoinTransfer.MoveAll(carried, banked, "extraction");
            Assert.IsTrue(move.Success);
            Assert.AreEqual(CoinTransactionKind.Transfer, move.Receipt.Kind);
            Assert.AreEqual(250, move.Receipt.Amount);
            Assert.AreEqual(0, carried.Balance);
            Assert.AreEqual(350, profile.BankedCoins, "The profile field is the banked balance.");
            Assert.AreEqual(CoinDomain.Banked, banked.Domain);
            Assert.IsTrue(CoinTransfer.MoveAll(carried, banked, "again").Success, "Empty transfer is a harmless no-op.");
            Assert.AreEqual(350, profile.BankedCoins);
            Assert.AreEqual(2, System.Enum.GetValues(typeof(CoinDomain)).Length, "Exactly Carried and Banked — one currency.");
        }

        [Test]
        public void ExpeditionService_UsesTheWallets_ReturnBanks_FailDestroys()
        {
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[0]);
            var service = new ExpeditionService(id => registry.TryGet(id, out var d) ? d : null, _ => null, null);
            var profile = new PlayerProfile { BankedCoins = 100 };
            var state = service.Start(profile, 1, Biome.RuinedMetro);
            Assert.AreEqual(CoinDomain.Carried, state.CarriedWallet.Domain);
            Assert.AreEqual(CoinDomain.Banked, service.BankedWallet.Domain);

            service.AddCarriedCoins(120);
            Assert.AreEqual(120, state.CarriedCoins);
            Assert.AreEqual(CoinError.InsufficientFunds, state.CarriedWallet.Debit(121, "event").Error, "Event prices debit Carried Coins atomically.");
            Assert.IsTrue(state.CarriedWallet.Debit(_prices.EventPrice(DungeonEventPriceKind.BrokenMachine, 1), "broken_machine").Success);
            Assert.AreEqual(20, state.CarriedCoins);
            Assert.AreEqual(100, profile.BankedCoins);

            service.RecordBossDefeated(650);
            service.ChooseTransit(TransitChoice.ReturnToShelter);
            Assert.AreEqual(120, profile.BankedCoins, "Only extraction moves Carried into Banked.");
            Assert.AreEqual(0, state.CarriedCoins);

            var failing = new ExpeditionService(id => registry.TryGet(id, out var d) ? d : null, _ => null, null);
            var profile2 = new PlayerProfile { BankedCoins = 100 };
            var state2 = failing.Start(profile2, 2, Biome.Rustworks);
            failing.AddCarriedCoins(500);
            failing.Fail();
            Assert.AreEqual(0, state2.CarriedCoins);
            Assert.AreEqual(100, profile2.BankedCoins, "Failure destroys Carried Coins and never banks them.");
        }
    }
}
