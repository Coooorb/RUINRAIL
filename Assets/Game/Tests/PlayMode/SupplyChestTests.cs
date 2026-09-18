using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class SupplyChestTests
    {
        private readonly List<Object> _created = new();
        private RarityTableDefinition _rarity;
        private LootTableDefinition _table;
        private LootRoller _roller;
        private AmmoBalanceConfig _ammoBalance;
        private AmmoItemDefinition _ammo;
        private EquipmentItemDefinition _rifle;
        private AffixPool _pool;

        [SetUp]
        public void SetUp()
        {
            _rarity = ScriptableObject.CreateInstance<RarityTableDefinition>();
            _created.Add(_rarity);
            Set(_rarity, "_bands", new[]
            {
                new RarityTableDefinition.Band { Depth = 1, CommonPermille = 0, UncommonPermille = 0, RarePermille = 1000, EpicPermille = 0, LegendaryPermille = 0 }
            });
            _roller = new LootRoller(_ => _rarity);

            var affixes = new[] { Affix("a1", 6, 10), Affix("a2", 5, 9), Affix("a3", 8, 14) };
            _pool = ScriptableObject.CreateInstance<AffixPool>();
            _created.Add(_pool);
            Set(_pool, "_id", "pool_test");
            Set(_pool, "_affixes", affixes);

            _rifle = ScriptableObject.CreateInstance<EquipmentItemDefinition>();
            _created.Add(_rifle);
            Set(_rifle, "_id", "weapon_test_rifle");
            Set(_rifle, "_category", ItemCategory.Weapon);
            Set(_rifle, "_affixPool", _pool);

            _ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            _created.Add(_ammo);
            Set(_ammo, "_id", "ammo_light");
            Set(_ammo, "_category", ItemCategory.Ammo);
            Set(_ammo, "_isStackable", true);
            Set(_ammo, "_maxStack", 180);
            Set(_ammo, "_ammoType", AmmoType.Light);
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);

            _table = ScriptableObject.CreateInstance<LootTableDefinition>();
            _created.Add(_table);
            Set(_table, "_id", "loot_test");
            Set(_table, "_rolls", new[]
            {
                new LootTableDefinition.Roll { Label = "coins", ChancePercent = 100, Entries = new[] { new LootTableDefinition.Entry { Item = null, MinQuantity = 12, MaxQuantity = 12 } } },
                new LootTableDefinition.Roll { Label = "ammo", ChancePercent = 100, Entries = new[] { new LootTableDefinition.Entry { Item = _ammo, MinQuantity = 30, MaxQuantity = 30 } } },
                new LootTableDefinition.Roll { Label = "gear", ChancePercent = 100, Entries = new[] { new LootTableDefinition.Entry { Item = _rifle } } }
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null)
            {
                info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                type = type.BaseType;
            }

            info.SetValue(target, value);
        }

        private AffixDefinition Affix(string id, int min, int max)
        {
            var a = ScriptableObject.CreateInstance<AffixDefinition>();
            _created.Add(a);
            Set(a, "_id", id);
            Set(a, "_minValue", min);
            Set(a, "_maxValue", max);
            return a;
        }

        private (SupplyChest chest, LootSpawner spawner, GameObject root) CreateChest(int sourceIndex = 0)
        {
            var root = new GameObject("Room");
            _created.Add(root);
            var chestObject = new GameObject("SupplyChest");
            chestObject.transform.SetParent(root.transform, false);
            chestObject.transform.position = new Vector3(3f, 3f, 0f);
            chestObject.AddComponent<BoxCollider2D>().isTrigger = true;
            var spawner = chestObject.AddComponent<LootSpawner>();
            var chest = chestObject.AddComponent<SupplyChest>();
            chest.Configure(_table, LootQuality.Standard, LootContext.ForSource(5, 1, sourceIndex, LootQuality.Standard), _roller, spawner);
            return (chest, spawner, root);
        }

        private (GameObject player, PlayerLootReceiver receiver, PlayerInteractor interactor, FakePlayerInputReader input, PlayerInventory inventory) CreatePlayer(Vector2 position)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = position;
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _ammo, _rifle });
            var inventory = PlayerInventory.FromRegistry(registry, _ammoBalance);
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            var interactor = player.AddComponent<PlayerInteractor>();
            var input = new FakePlayerInputReader();
            interactor.SetInputReader(input);
            return (player, receiver, interactor, input, inventory);
        }

        [Test]
        public void Chest_OpensExactlyOnce_AndRepeatInteractionsRollNothing()
        {
            var (chest, _, _) = CreateChest();
            var openedEvents = 0;
            chest.Opened += (_, _) => openedEvents++;

            Assert.IsFalse(chest.IsOpened);
            Assert.IsTrue(chest.TryOpen(out var first));
            Assert.IsTrue(chest.IsOpened);
            Assert.AreEqual(12, first.Coins);
            Assert.AreEqual(2, first.Items.Count);

            Assert.IsFalse(chest.TryOpen(out var second));
            Assert.IsNull(second);
            Assert.IsFalse(chest.Interact(new GameObject("x")));
            Assert.IsFalse(chest.CanInteract(null));
            Assert.AreEqual(1, openedEvents);
            Assert.AreSame(first, chest.LastResult);
            Assert.AreEqual(3, chest.SpawnedPickups.Count, "Ammo pickup + rifle pickup + coin pile, spawned once.");
        }

        [Test]
        public void Chest_LootIsDeterministic_ForFixedSeedDepthAndSourceIndex()
        {
            var (a, _, _) = CreateChest(sourceIndex: 4);
            var (b, _, _) = CreateChest(sourceIndex: 4);
            a.TryOpen(out var ra);
            b.TryOpen(out var rb);

            var gearA = ra.Items.Single(i => i.DefinitionId == "weapon_test_rifle");
            var gearB = rb.Items.Single(i => i.DefinitionId == "weapon_test_rifle");
            Assert.AreEqual(Rarity.Rare, gearA.Rarity);
            Assert.AreEqual(2, gearA.AffixRolls.Count);
            CollectionAssert.AreEqual(gearA.AffixRolls.Select(r => (r.AffixId, r.Value)), gearB.AffixRolls.Select(r => (r.AffixId, r.Value)));
            Assert.AreNotEqual(gearA.InstanceId, gearB.InstanceId);
        }

        [Test]
        public void ReentrantOpenedCallback_CannotRollASecondTime()
        {
            var (chest, _, _) = CreateChest();
            var innerResults = new List<bool>();
            chest.Opened += (c, _) => innerResults.Add(c.TryOpen(out _));

            Assert.IsTrue(chest.TryOpen(out _));
            CollectionAssert.AreEqual(new[] { false }, innerResults);
            Assert.AreEqual(3, chest.SpawnedPickups.Count);
        }

        [UnityTest]
        public IEnumerator Pickups_TransferIntoInventoryExactlyOnce_WithRarityAndAffixesIntact()
        {
            var (chest, _, _) = CreateChest();
            chest.TryOpen(out var result);
            var rolled = result.Items.Single(i => i.DefinitionId == "weapon_test_rifle");
            var rolledAffixes = rolled.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray();
            var (player, receiver, _, _, inventory) = CreatePlayer(new Vector2(3f, 3f));
            yield return null;

            var pickups = chest.SpawnedPickups.Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null).ToList();
            var coins = chest.SpawnedPickups.Select(g => g.GetComponent<CoinPickup>()).Single(c => c != null);
            Assert.AreEqual(2, pickups.Count);

            foreach (var pickup in pickups)
            {
                Assert.IsTrue(pickup.Interact(player), $"pickup {pickup.name}");
            }

            Assert.IsTrue(coins.Interact(player));
            yield return null;

            Assert.AreEqual(30, inventory.Get(AmmoType.Light));
            Assert.AreEqual(1, inventory.CountOf("weapon_test_rifle"));
            var stored = inventory.BackpackSlots.Single(s => s != null && s.DefinitionId == "weapon_test_rifle");
            Assert.AreEqual(rolled.InstanceId, stored.InstanceId, "Same instance, no copy.");
            Assert.AreEqual(Rarity.Rare, stored.Rarity);
            CollectionAssert.AreEqual(rolledAffixes, stored.AffixRolls.Select(r => (r.AffixId, r.Value)).ToArray());
            Assert.AreEqual(12, receiver.CarriedCoins);
            Assert.IsTrue(pickups.All(p => p == null), "Pickups despawn after a successful transfer.");
            Assert.IsTrue(coins == null);

            // Repeating on the (now destroyed) pickups can never duplicate.
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null && s.DefinitionId == "weapon_test_rifle"));
        }

        [UnityTest]
        public IEnumerator Pickup_StaysInWorld_WhenInventoryIsFull()
        {
            var (chest, _, _) = CreateChest();
            chest.TryOpen(out _);
            var (player, _, _, _, inventory) = CreatePlayer(new Vector2(3f, 3f));
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_test_rifle"));
            yield return null;

            var rifle = chest.SpawnedPickups.Select(g => g.GetComponent<WorldItemPickup>()).Single(p => p != null && p.Item.DefinitionId == "weapon_test_rifle");
            var held = rifle.Item;

            Assert.IsFalse(rifle.Interact(player));
            yield return null;

            Assert.IsNotNull(rifle);
            Assert.AreSame(held, rifle.Item, "Item stays on the ground, never lost.");
            Assert.AreEqual(PlayerInventory.BackpackCapacity, inventory.BackpackSlots.Count(s => s != null));
        }

        [UnityTest]
        public IEnumerator InteractInput_OpensNearestChest_ThenPicksUpLoot()
        {
            var (chest, _, _) = CreateChest();
            var (player, receiver, interactor, input, inventory) = CreatePlayer(new Vector2(3.5f, 3f));
            yield return new WaitForFixedUpdate();

            input.RaiseInteract();
            Assert.IsTrue(chest.IsOpened, "Interact opens the chest in reach.");
            yield return new WaitForFixedUpdate();

            for (var i = 0; i < 5; i++)
            {
                input.RaiseInteract();
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(30, inventory.Get(AmmoType.Light));
            Assert.AreEqual(1, inventory.CountOf("weapon_test_rifle"));
            Assert.AreEqual(12, receiver.CarriedCoins);
            Assert.IsNull(interactor.FindNearestInteractable(), "Nothing left to interact with.");

            var far = CreatePlayer(new Vector2(20f, 20f));
            far.input.RaiseInteract();
            Assert.IsNull(far.interactor.FindNearestInteractable(), "Out of reach finds nothing.");
        }
    }
}
