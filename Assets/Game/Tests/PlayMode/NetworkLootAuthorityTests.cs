using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 099: host arbitration of pickups/drops/coins/chests/events/purchases with transaction idempotency and rejoin snapshots.</summary>
    public class NetworkLootAuthorityTests
    {
        private readonly List<Object> _created = new();
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        private sealed class Authority : IAuthorityContext
        {
            public Authority(NetworkRole role) { Role = role; }
            public NetworkRole Role { get; }
            public bool IsAuthority => Role != NetworkRole.Client;
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("Spawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(id => _registry.TryGet(id, out var d) ? d : null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private (LootParticipant participant, PlayerInventory inventory, CoinWallet wallet) Participant(ulong clientId, int coins = 0)
        {
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var wallet = new CoinWallet(CoinDomain.Carried, coins);
            return (new LootParticipant(clientId, $"p{clientId}", new BackpackContainer(inventory), wallet), inventory, wallet);
        }

        private LootAuthorityService Host(params LootParticipant[] participants)
        {
            var host = new LootAuthorityService(new Authority(NetworkRole.Host));
            foreach (var p in participants) host.RegisterParticipant(p);
            host.SetDropService(new ItemDropService(_spawner));
            return host;
        }

        // ---- Acceptance 1: race for one item ----

        [UnityTest]
        public IEnumerator TwoClientsRaceForOneItem_ExactlyOneWins_RetriesAreIdempotent()
        {
            var (a, invA, _) = Participant(1);
            var (b, invB, _) = Participant(2);
            var host = Host(a, b);
            var pickup = _spawner.CreateItemPickup(Vector2.zero);
            var item = new ItemInstance("weapon_p9_ranger") { Rarity = Rarity.Rare };
            pickup.Hold(item, ItemCategory.Weapon);
            var resolved = new List<LootTransactionResult>();
            host.Resolved += resolved.Add;

            var first = host.RequestPickup("tx-a-1", 1, pickup);
            var second = host.RequestPickup("tx-b-1", 2, pickup);
            var retryA = host.RequestPickup("tx-a-1", 1, pickup);
            var retryB = host.RequestPickup("tx-b-1", 2, pickup);
            yield return null;

            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreEqual(LootVerdict.AlreadyTaken, second.Verdict);
            Assert.AreSame(first, retryA, "Same transaction id: stored result, no re-execution.");
            Assert.AreSame(second, retryB);
            Assert.IsTrue(invA.Contains(item.InstanceId));
            Assert.IsFalse(invB.Contains(item.InstanceId));
            Assert.AreEqual(1, invA.BackpackSlots.Count(s => s != null) + invB.BackpackSlots.Count(s => s != null), "Exactly one copy exists.");
            Assert.AreEqual(2, resolved.Count, "Retries do not resolve again.");
            Assert.AreEqual(2, host.Ledger.Count);
        }

        [Test]
        public void Client_CannotResolveLoot_Locally()
        {
            var client = new LootAuthorityService(new Authority(NetworkRole.Client));
            var (p, _, _) = Participant(1);
            client.RegisterParticipant(p);
            var pickup = _spawner.CreateItemPickup(Vector2.zero);
            pickup.Hold(new ItemInstance("weapon_p9_ranger"), ItemCategory.Weapon);
            var result = client.RequestPickup("tx-1", 1, pickup);
            Assert.AreEqual(LootVerdict.NotAuthority, result.Verdict);
            Assert.IsFalse(pickup.IsConsumed);
            Assert.AreEqual(0, client.Ledger.Count);
        }

        [UnityTest]
        public IEnumerator DropThenPickupByTeammate_MovesTheInstanceOnce()
        {
            var (a, invA, _) = Participant(1);
            var (b, invB, _) = Participant(2);
            var host = Host(a, b);
            var rifle = new ItemInstance("weapon_p9_ranger");
            invA.TryAddToBackpack(rifle);
            var drop = host.RequestDrop("tx-drop", 1, new IItemContainer[] { a.Backpack }, rifle.InstanceId, 1, new Vector2(3f, 3f));
            Assert.AreEqual(LootVerdict.Accepted, drop.Verdict);
            Assert.AreEqual(rifle.InstanceId, drop.InstanceId);
            Assert.IsFalse(invA.Contains(rifle.InstanceId));
            Assert.AreEqual(1, _ground.Count);
            Assert.AreSame(drop, host.RequestDrop("tx-drop", 1, new IItemContainer[] { a.Backpack }, rifle.InstanceId, 1, Vector2.zero), "Idempotent drop.");
            Assert.AreEqual(1, _ground.Count);

            var pickup = _ground.Tracked[0].GetComponent<WorldItemPickup>();
            Assert.AreEqual(LootVerdict.Accepted, host.RequestPickup("tx-pick", 2, pickup).Verdict);
            yield return null;
            Assert.IsTrue(invB.Contains(rifle.InstanceId));
            Assert.AreEqual(0, _ground.Count);
        }

        // ---- Acceptance 3: coins ----

        [UnityTest]
        public IEnumerator CoinPile_DistributesOnceAcrossTheParty_ConservingTheTotal()
        {
            var (a, _, wa) = Participant(1);
            var (b, _, wb) = Participant(2);
            var (c, _, wc) = Participant(3);
            var host = Host(a, b, c);
            var pile = _spawner.CreateCoinPickup(Vector2.zero);
            pile.SetAmount(101);

            var first = host.RequestCoins("tx-coins", 2, pile);
            var retry = host.RequestCoins("tx-coins", 2, pile);
            var other = host.RequestCoins("tx-coins-other", 3, pile);
            yield return null;

            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreEqual(101, first.Coins);
            Assert.AreSame(first, retry);
            Assert.AreEqual(LootVerdict.AlreadyTaken, other.Verdict);
            Assert.AreEqual(101, wa.Balance + wb.Balance + wc.Balance, "Total conserved.");
            CollectionAssert.AreEqual(new[] { 34, 34, 33 }, new[] { wa.Balance, wb.Balance, wc.Balance }, "Even split with the deterministic remainder policy.");
            Assert.AreEqual(3, host.LastDistribution.Shares.Count);
            Assert.IsTrue(pile == null || pile.IsCollected);
        }

        // ---- Acceptance 2 + 5: chests, events, purchases execute once ----

        [Test]
        public void ChestOpen_ByTwoClients_OpensOnce()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            var chestObject = new GameObject("Chest");
            _created.Add(chestObject);
            var chest = chestObject.AddComponent<SupplyChest>();
            catalog.Configure(chest, LootSourceKind.SupplyChest, 42, 1, 0, 1, null, _spawner);
            var (a, _, _) = Participant(1);
            var (b, _, _) = Participant(2);
            var host = Host(a, b);

            var first = host.RequestChestOpen("tx-chest-a", 1, chest);
            var second = host.RequestChestOpen("tx-chest-b", 2, chest);
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreEqual(LootVerdict.AlreadyTaken, second.Verdict);
            Assert.IsTrue(chest.IsOpened);
            Assert.AreEqual(chest.SpawnedPickups.Count, _ground.Count, "Loot spawned exactly once.");
            Assert.AreSame(first, host.RequestChestOpen("tx-chest-a", 1, chest));
        }

        [Test]
        public void EventActivation_AndMerchantPurchase_ExecuteOncePerTransaction()
        {
            var prices = new PriceService(AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"));
            var eventConfig = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            var (a, invA, wa) = Participant(1, 5000);
            var (b, invB, wb) = Participant(2, 5000);
            var host = Host(a, b);

            var vault = new LockedVaultEvent(new DungeonEventContext(9, 1, 0), eventConfig, prices, new EventRewardRoller(catalog), new LootSpawnerDeliverer(_spawner, () => Vector2.zero));
            var activate = host.RequestEventActivate("tx-vault-a", 1, vault, new EventActor(wa, a.Backpack, "p1"));
            var again = host.RequestEventActivate("tx-vault-b", 2, vault, new EventActor(wb, b.Backpack, "p2"));
            Assert.AreEqual(LootVerdict.Accepted, activate.Verdict);
            Assert.AreEqual(250, activate.Coins);
            Assert.AreEqual(4750, wa.Balance);
            Assert.AreEqual(LootVerdict.AlreadyTaken, again.Verdict);
            Assert.AreEqual(5000, wb.Balance, "The second client is never charged.");
            Assert.AreSame(activate, host.RequestEventActivate("tx-vault-a", 1, vault, new EventActor(wa, a.Backpack, "p1")));
            Assert.AreEqual(4750, wa.Balance, "Retry never charges again.");

            var merchantConfig = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset");
            var merchant = new DungeonMerchantService(merchantConfig, prices, wa, new DungeonMerchantState(1), 9, 2, _catalog, id => _registry.TryGet(id, out var d) ? d : null, catalog.RarityTableFor);
            var offer = merchant.Offers.First(o => o.Definition is EquipmentItemDefinition);
            var buy = host.RequestMerchantBuy("tx-buy", 1, merchant, offer.Index);
            Assert.AreEqual(LootVerdict.Accepted, buy.Verdict);
            Assert.AreEqual(offer.Price, buy.Coins);
            Assert.AreEqual(4750 - offer.Price, wa.Balance);
            Assert.AreSame(buy, host.RequestMerchantBuy("tx-buy", 1, merchant, offer.Index), "Duplicate purchase transaction: no second debit.");
            Assert.AreEqual(4750 - offer.Price, wa.Balance);
            Assert.AreEqual(LootVerdict.AlreadyTaken, host.RequestMerchantBuy("tx-buy-2", 1, merchant, offer.Index).Verdict);
            Assert.AreEqual(1, invA.BackpackSlots.Count(s => s != null && s.InstanceId == offer.Item.InstanceId));
        }

        // ---- Acceptance 4: rejoin sync ----

        [UnityTest]
        public IEnumerator GroundSnapshot_RestoresTheSameInstances_ForALateJoiner_WithoutReRolling()
        {
            var pickup = _spawner.CreateItemPickup(new Vector2(2f, 3f));
            var rifle = new ItemInstance("weapon_p9_ranger", 1, Rarity.Epic) { IsAtRisk = true };
            pickup.Hold(rifle, ItemCategory.Weapon);
            var ammo = _spawner.CreateItemPickup(new Vector2(4f, 3f));
            ammo.Hold(new ItemInstance("ammo_light", 12), ItemCategory.Ammo);
            _spawner.CreateCoinPickup(new Vector2(6f, 3f)).SetAmount(40);
            var snapshot = GroundLootSnapshot.Capture(_ground, 1);
            Assert.AreEqual(3, snapshot.Pickups.Count);
            var json = JsonUtility.ToJson(snapshot);
            var restored = JsonUtility.FromJson<GroundLootSnapshot>(json);

            var clientGround = new GroundLootRegistry();
            var clientSpawnerObject = new GameObject("ClientSpawner");
            _created.Add(clientSpawnerObject);
            var clientSpawner = clientSpawnerObject.AddComponent<LootSpawner>();
            clientSpawner.SetRegistry(clientGround);
            Assert.AreEqual(3, restored.Restore(clientSpawner, id => _registry.TryGet(id, out var d) ? d : null));
            var clientRifle = clientGround.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).First(p => p != null && p.Item.DefinitionId == "weapon_p9_ranger");
            Assert.AreEqual(rifle.InstanceId, clientRifle.Item.InstanceId, "Same instance id on the joiner: no re-roll, no second identity.");
            Assert.AreEqual(Rarity.Epic, clientRifle.Item.Rarity);
            Assert.IsTrue(clientRifle.Item.IsAtRisk);
            Assert.AreEqual(12, clientGround.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).First(p => p != null && p.Item.DefinitionId == "ammo_light").Item.Quantity);
            Assert.AreEqual(40, clientGround.Tracked.Select(g => g.GetComponent<CoinPickup>()).First(c => c != null).Amount);
            yield return null;
            foreach (var go in clientGround.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }

        [Test]
        public void ResolvedRoomState_RestoresOpenedChest_WithoutRegeneratingRewards()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            var chestObject = new GameObject("Chest");
            _created.Add(chestObject);
            var chest = chestObject.AddComponent<SupplyChest>();
            catalog.Configure(chest, LootSourceKind.SupplyChest, 42, 1, 0, 1, null, _spawner);
            chest.RestoreOpened();
            Assert.IsFalse(chest.TryOpen(out _), "A joiner's copy of an opened chest never rolls.");
            Assert.AreEqual(0, _ground.Count);
        }
    }
}
