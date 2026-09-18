using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 075: world coin pickups credit Carried Coins (solo full, party even split), never item slots, once.</summary>
    public class CoinPickupTests
    {
        private readonly List<Object> _created = new();
        private AmmoBalanceConfig _ammoBalance;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _registry = ItemDefinitionRegistry.Build(System.Array.Empty<ItemDefinition>());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private (GameObject player, PlayerLootReceiver receiver, PlayerInventory inventory) CreatePlayer()
        {
            var player = new GameObject("Player");
            _created.Add(player);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            return (player, receiver, inventory);
        }

        private CoinPickup CreateCoins(int amount)
        {
            var go = new GameObject("Coins");
            _created.Add(go);
            var coins = go.AddComponent<CoinPickup>();
            coins.SetAmount(amount);
            return coins;
        }

        [Test]
        public void SoloPickup_CreditsTheExactAmountToCarriedCoins_Once_WithoutTouchingItemSlots()
        {
            var (player, receiver, inventory) = CreatePlayer();
            var banked = new CoinWallet(CoinDomain.Banked, 500);
            var coins = CreateCoins(45);
            var changes = new List<int>();
            receiver.CarriedCoinsChanged += changes.Add;

            Assert.IsTrue(coins.Interact(player));
            Assert.IsFalse(coins.Interact(player), "Duplicate pickup is rejected.");
            Assert.IsFalse(coins.CanInteract(player));

            Assert.AreEqual(45, receiver.CarriedCoins);
            Assert.AreEqual(CoinDomain.Carried, receiver.Wallet.Domain);
            Assert.AreEqual(500, banked.Balance, "World coins never credit Banked Coins.");
            CollectionAssert.AreEqual(new[] { 45 }, changes);
            Assert.AreEqual(0, inventory.BackpackSlots.Count(s => s != null), "No inventory slot is consumed.");
            Assert.AreEqual(45, receiver.LastCoinDistribution.Total);
            Assert.AreEqual(45, receiver.LastCoinDistribution.ShareFor("local"));
        }

        [Test]
        public void PartyPickup_SplitsEvenlyAcrossParticipants_ConservingTheTotal()
        {
            var (host, hostReceiver, hostInventory) = CreatePlayer();
            var (_, p2Receiver, _) = CreatePlayer();
            var (_, p3Receiver, _) = CreatePlayer();
            var distributor = new PartyCoinDistributor(
                new CoinParticipant("host", hostReceiver.Wallet),
                new CoinParticipant("p2", p2Receiver.Wallet),
                new CoinParticipant("p3", p3Receiver.Wallet));
            hostReceiver.SetCoinDistributor(distributor);
            p2Receiver.SetCoinDistributor(distributor);
            p3Receiver.SetCoinDistributor(distributor);

            var pile = CreateCoins(11);
            Assert.IsTrue(pile.Interact(host));
            Assert.IsFalse(pile.Interact(host));

            Assert.AreEqual(11, hostReceiver.CarriedCoins + p2Receiver.CarriedCoins + p3Receiver.CarriedCoins);
            Assert.AreEqual(4, hostReceiver.CarriedCoins);
            Assert.AreEqual(4, p2Receiver.CarriedCoins);
            Assert.AreEqual(3, p3Receiver.CarriedCoins);
            Assert.AreEqual(0, hostInventory.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(11, hostReceiver.LastCoinDistribution.Total);
            Assert.AreEqual(3, hostReceiver.LastCoinDistribution.Shares.Count);

            // The next pickup, by any participant, rotates the odd coin.
            var second = CreateCoins(11);
            var p2 = p2Receiver.gameObject;
            Assert.IsTrue(second.Interact(p2));
            Assert.AreEqual(22, hostReceiver.CarriedCoins + p2Receiver.CarriedCoins + p3Receiver.CarriedCoins);
            Assert.AreEqual(7, hostReceiver.CarriedCoins);
            Assert.AreEqual(8, p2Receiver.CarriedCoins);
            Assert.AreEqual(7, p3Receiver.CarriedCoins);
        }

        [Test]
        public void EmptyRoster_RefusesThePickup_LeavingItInTheWorld()
        {
            var (player, receiver, _) = CreatePlayer();
            receiver.SetCoinDistributor(new PartyCoinDistributor());
            var coins = CreateCoins(9);

            Assert.IsFalse(coins.Interact(player));
            Assert.IsFalse(coins.IsCollected);
            Assert.AreEqual(0, receiver.CarriedCoins);

            receiver.SetCoinDistributor(null);
            Assert.IsTrue(coins.Interact(player));
            Assert.AreEqual(9, receiver.CarriedCoins);
        }
    }
}
