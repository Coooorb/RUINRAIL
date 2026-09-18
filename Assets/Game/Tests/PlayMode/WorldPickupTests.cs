using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 074: shared ground pickups, deliberate dropping, depth lifetime, attraction and duplicate guards.</summary>
    public class WorldPickupTests
    {
        private readonly List<Object> _created = new();
        private AmmoBalanceConfig _ammoBalance;
        private AmmoItemDefinition _ammo;
        private EquipmentItemDefinition _rifle;
        private ItemDefinitionRegistry _registry;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;
        private ItemDropService _drops;

        [SetUp]
        public void SetUp()
        {
            _rifle = ScriptableObject.CreateInstance<EquipmentItemDefinition>();
            _created.Add(_rifle);
            Set(_rifle, "_id", "weapon_test_rifle");
            Set(_rifle, "_category", ItemCategory.Weapon);

            _ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            _created.Add(_ammo);
            Set(_ammo, "_id", "ammo_light");
            Set(_ammo, "_category", ItemCategory.Ammo);
            Set(_ammo, "_isStackable", true);
            Set(_ammo, "_maxStack", 180);
            Set(_ammo, "_ammoType", AmmoType.Light);
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _ammo, _rifle });

            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("LootSpawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(id => _registry.TryGet(id, out var d) ? d : null);
            _drops = new ItemDropService(_spawner);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
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

        private (GameObject player, PlayerLootReceiver receiver, PlayerInventory inventory) CreatePlayer(Vector2 position, bool withDrops = true)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = position;
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            if (withDrops) receiver.SetDropService(_drops);
            return (player, receiver, inventory);
        }

        private WorldItemPickup SpawnPickup(ItemInstance item, Vector2 position)
        {
            var pickup = _spawner.CreateItemPickup(position);
            pickup.Hold(item, _spawner.CategoryOf(item));
            return pickup;
        }

        // ---- Acceptance 1: pickup and drop preserve ids/quantities exactly once ----

        [UnityTest]
        public IEnumerator Drop_MovesWholeInstanceToGround_AndTeammatePicksUpTheSameInstance()
        {
            var (_, owner, ownerInventory) = CreatePlayer(new Vector2(1f, 1f));
            var rifle = new ItemInstance("weapon_test_rifle") { Rarity = Rarity.Rare };
            Assert.IsTrue(ownerInventory.TryAddToBackpack(rifle));

            var drop = owner.TryDrop(rifle.InstanceId);
            Assert.IsTrue(drop.Success, drop.Transfer.Error.ToString());
            Assert.IsNotNull(drop.Pickup);
            Assert.AreSame(rifle, drop.Pickup.Item, "Same instance, no copy.");
            Assert.AreEqual(ItemCategory.Weapon, drop.Pickup.Category);
            Assert.IsFalse(ownerInventory.Contains(rifle.InstanceId));
            Assert.AreEqual(1, _ground.Count, "Dropped items are tracked ground state.");
            Assert.AreEqual((Vector2)drop.Pickup.transform.position, new Vector2(1f, 1f));

            var (teammate, _, teammateInventory) = CreatePlayer(new Vector2(1f, 1f));
            Assert.IsTrue(drop.Pickup.Interact(teammate));
            yield return null;

            Assert.IsTrue(teammateInventory.Contains(rifle.InstanceId));
            Assert.AreEqual(Rarity.Rare, teammateInventory.BackpackSlots.Single(s => s != null).Rarity);
            Assert.IsFalse(ownerInventory.Contains(rifle.InstanceId));
            Assert.AreEqual(0, _ground.Count);
        }

        [Test]
        public void Drop_PartialStack_SplitsQuantity_AndPickupRestoresTheTotal()
        {
            var (player, receiver, inventory) = CreatePlayer(Vector2.zero);
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("ammo_light", 30)));
            var stack = inventory.BackpackSlots.Single(s => s != null);

            var drop = receiver.TryDrop(stack.InstanceId, 10);
            Assert.IsTrue(drop.Success);
            Assert.AreEqual(10, drop.Pickup.Item.Quantity);
            Assert.AreEqual(20, inventory.Get(AmmoType.Light));
            Assert.AreNotEqual(stack.InstanceId, drop.Pickup.Item.InstanceId, "A split portion is a new instance.");
            Assert.AreEqual(ItemCategory.Ammo, drop.Pickup.Category);
            Assert.IsTrue(drop.Pickup.IsAttractionEligible);

            Assert.IsTrue(drop.Pickup.Interact(player));
            Assert.AreEqual(30, inventory.Get(AmmoType.Light), "Nothing gained, nothing lost.");
        }

        [Test]
        public void Drop_EquippedGear_LeavesTheSlotEmpty()
        {
            var (_, receiver, inventory) = CreatePlayer(Vector2.zero);
            var rifle = new ItemInstance("weapon_test_rifle");
            Assert.IsTrue(inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon));

            var drop = receiver.TryDrop(rifle.InstanceId);
            Assert.IsTrue(drop.Success, drop.Transfer.Error.ToString());
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.AreSame(rifle, drop.Pickup.Item);
        }

        [Test]
        public void Drop_RejectsUnknownInstance_InvalidQuantity_AndUnboundService_WithoutSpawningAnything()
        {
            var (_, receiver, inventory) = CreatePlayer(Vector2.zero);
            inventory.TryAddToBackpack(new ItemInstance("ammo_light", 30));
            var stack = inventory.BackpackSlots.Single(s => s != null);

            Assert.AreEqual(TransferError.SourceMissingItem, receiver.TryDrop("nope").Transfer.Error);
            Assert.AreEqual(TransferError.InvalidQuantity, receiver.TryDrop(stack.InstanceId, 0).Transfer.Error);
            Assert.AreEqual(TransferError.InvalidQuantity, receiver.TryDrop(stack.InstanceId, 31).Transfer.Error);
            Assert.AreEqual(30, inventory.Get(AmmoType.Light));
            Assert.AreEqual(0, _ground.Count, "Failed drops leave no ground pickup behind.");

            var (_, unbound, unboundInventory) = CreatePlayer(Vector2.zero, withDrops: false);
            unboundInventory.TryAddToBackpack(new ItemInstance("ammo_light", 5));
            var other = unboundInventory.BackpackSlots.Single(s => s != null);
            Assert.AreEqual(TransferError.InvalidRequest, unbound.TryDrop(other.InstanceId).Transfer.Error);
            Assert.AreEqual(5, unboundInventory.Get(AmmoType.Light));
        }

        // ---- Acceptance 2: full inventory leaves the pickup intact ----

        [UnityTest]
        public IEnumerator FullBackpack_LeavesDroppedPickupIntact()
        {
            var (player, receiver, inventory) = CreatePlayer(Vector2.zero);
            var rifle = new ItemInstance("weapon_test_rifle");
            inventory.TryAddToBackpack(rifle);
            var drop = receiver.TryDrop(rifle.InstanceId);
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_test_rifle"));

            Assert.IsFalse(drop.Pickup.Interact(player));
            yield return null;

            Assert.IsNotNull(drop.Pickup);
            Assert.AreSame(rifle, drop.Pickup.Item);
            Assert.IsFalse(drop.Pickup.IsConsumed);
            Assert.AreEqual(1, _ground.Count);
        }

        // ---- Acceptance 3: ground loot survives time within the depth, cleared at depth transition ----

        [UnityTest]
        public IEnumerator GroundLoot_SurvivesTimeWithinDepth_AndIsDiscardedWhenThePartyLeavesTheDepth()
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            using var lifetime = new GroundLootLifetime(_ground, expedition);
            expedition.Start(new PlayerProfile { TotalXp = 0 }, 11, Biome.RuinedMetro);
            Assert.AreEqual(1, _ground.DepthClears, "Entering the first depth starts from a clean ground.");

            var pickup = SpawnPickup(new ItemInstance("weapon_test_rifle"), new Vector2(2f, 2f));
            var coins = _spawner.CreateCoinPickup(new Vector2(3f, 2f));
            coins.SetAmount(7);
            Assert.AreEqual(2, _ground.Count);

            for (var i = 0; i < 60; i++) yield return null;
            yield return new WaitForSeconds(0.25f);
            Assert.IsNotNull(pickup);
            Assert.IsNotNull(coins);
            Assert.AreEqual(2, _ground.Count, "No time-based despawn on the current depth.");

            expedition.RecordBossDefeated(100);
            Assert.IsTrue(expedition.ChooseTransit(TransitChoice.DescendDeeper));
            Assert.AreEqual(2, expedition.State.Depth);
            Assert.AreEqual(2, _ground.DepthClears);
            Assert.AreEqual(0, _ground.Count);
            yield return null;
            Assert.IsTrue(pickup == null, "Ground pickup discarded at depth transition.");
            Assert.IsTrue(coins == null);

            var left = SpawnPickup(new ItemInstance("ammo_light", 4), Vector2.zero);
            expedition.Fail();
            Assert.AreEqual(0, _ground.Count, "Ending the expedition discards the ground too.");
            yield return null;
            Assert.IsTrue(left == null);
        }

        // ---- Acceptance 4: duplicate interaction / callbacks cannot duplicate ----

        [UnityTest]
        public IEnumerator DuplicateInteractionsAndReentrantCallbacks_TransferTheItemExactlyOnce()
        {
            var (player, receiver, inventory) = CreatePlayer(Vector2.zero);
            var rifle = new ItemInstance("weapon_test_rifle");
            var pickup = SpawnPickup(rifle, Vector2.zero);
            var reentrant = new List<TransferResult>();
            pickup.PickedUp += p => reentrant.Add(p.TryPickUp(receiver.Backpack, receiver.TransferService));

            Assert.IsTrue(pickup.Interact(player));
            Assert.IsFalse(pickup.Interact(player), "Second same-frame interaction is rejected.");
            Assert.IsFalse(pickup.CanInteract(player));
            Assert.AreEqual(TransferError.SourceMissingItem, pickup.TryPickUp(receiver.Backpack, receiver.TransferService).Error);
            Assert.AreEqual(1, reentrant.Count);
            Assert.IsFalse(reentrant[0].Success);
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(rifle.InstanceId, inventory.BackpackSlots.Single(s => s != null).InstanceId);
            yield return null;
            Assert.IsTrue(pickup == null);

            var coins = _spawner.CreateCoinPickup(Vector2.zero);
            coins.SetAmount(9);
            var inner = new List<bool>();
            coins.Collected += (c, _) => inner.Add(c.Interact(player));
            Assert.IsTrue(coins.Interact(player));
            Assert.IsFalse(coins.Interact(player));
            CollectionAssert.AreEqual(new[] { false }, inner);
            Assert.AreEqual(9, receiver.CarriedCoins, "Coins credited exactly once.");
        }

        [Test]
        public void TransferService_RefusesADuplicateOwnershipMove_IntoAContainerAlreadyHoldingTheInstance()
        {
            var (_, receiver, inventory) = CreatePlayer(Vector2.zero);
            var rifle = new ItemInstance("weapon_test_rifle");
            inventory.TryAddToBackpack(rifle);
            var ghost = SpawnPickup(rifle, Vector2.zero);

            var result = ghost.TryPickUp(receiver.Backpack, receiver.TransferService);
            Assert.AreEqual(TransferError.DuplicateOwnership, result.Error);
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null));
        }

        // ---- Requirement 4: Magnetic Coil attraction hook ----

        private (PickupAttractor attractor, PlayerStats stats, PlayerLootReceiver receiver, PlayerInventory inventory, GameObject player) CreateAttractingPlayer(Vector2 position)
        {
            var (player, receiver, inventory) = CreatePlayer(position);
            var stats = new PlayerStats(ScriptableObject.CreateInstance<GlobalStatCapsConfig>());
            var attractor = player.AddComponent<PickupAttractor>();
            attractor.SetStats(stats);
            return (attractor, stats, receiver, inventory, player);
        }

        private static void StepUntilSettled(PickupAttractor attractor, int steps = 200)
        {
            Physics2D.SyncTransforms();
            for (var i = 0; i < steps; i++) attractor.Step(0.02f);
        }

        [UnityTest]
        public IEnumerator MagneticCoil_PullsCoinsAndAmmoWithinThreeTiles_NeverEquipment()
        {
            var (attractor, stats, receiver, inventory, _) = CreateAttractingPlayer(Vector2.zero);
            Assert.AreEqual(0f, attractor.Radius, "No attraction without a source.");
            stats.SetSource(new StatModifierSource("accessory_magnetic_coil", StatModifier.Flat(StatId.PickupAttractionRadius, 3)));
            Assert.AreEqual(3f, attractor.Radius);

            var coins = _spawner.CreateCoinPickup(new Vector2(2.5f, 0f));
            coins.SetAmount(15);
            var ammo = SpawnPickup(new ItemInstance("ammo_light", 12), new Vector2(0f, 2.5f));
            var rifle = SpawnPickup(new ItemInstance("weapon_test_rifle"), new Vector2(-2f, 0f));
            var farAmmo = SpawnPickup(new ItemInstance("ammo_light", 5), new Vector2(6f, 0f));

            StepUntilSettled(attractor);
            yield return null;

            Assert.AreEqual(15, receiver.CarriedCoins);
            Assert.AreEqual(12, inventory.Get(AmmoType.Light));
            Assert.IsTrue(coins == null);
            Assert.IsTrue(ammo == null);
            Assert.IsNotNull(rifle, "Equipment is never auto-collected.");
            Assert.AreEqual(new Vector2(-2f, 0f), (Vector2)rifle.transform.position, "Equipment is not even moved.");
            Assert.IsNotNull(farAmmo);
            Assert.AreEqual(new Vector2(6f, 0f), (Vector2)farAmmo.transform.position, "Outside the radius: untouched.");
            Assert.AreEqual(2, attractor.Collected);
        }

        [UnityTest]
        public IEnumerator Attraction_LeavesAPickupTheReceiverCannotTake_AndNeverDuplicatesIt()
        {
            var (attractor, stats, _, inventory, _) = CreateAttractingPlayer(Vector2.zero);
            stats.SetSource(new StatModifierSource("accessory_magnetic_coil", StatModifier.Flat(StatId.PickupAttractionRadius, 3)));
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_test_rifle"));
            var ammo = SpawnPickup(new ItemInstance("ammo_light", 12), new Vector2(0f, 2f));

            StepUntilSettled(attractor);
            yield return null;

            Assert.IsNotNull(ammo);
            Assert.AreEqual(12, ammo.Item.Quantity);
            Assert.AreEqual(0, inventory.Get(AmmoType.Light));
            Assert.AreEqual(0, attractor.Collected);
        }

        [UnityTest]
        public IEnumerator RoomSweep_PullsEligibleGroundPickupsRegardlessOfRadius()
        {
            var (attractor, _, receiver, inventory, _) = CreateAttractingPlayer(Vector2.zero);
            var coins = _spawner.CreateCoinPickup(new Vector2(12f, 0f));
            coins.SetAmount(3);
            var ammo = SpawnPickup(new ItemInstance("ammo_light", 8), new Vector2(0f, 15f));
            var rifle = SpawnPickup(new ItemInstance("weapon_test_rifle"), new Vector2(1f, 0f));

            Assert.AreEqual(2, attractor.SweepAll(_ground.Tracked));
            StepUntilSettled(attractor, 400);
            yield return null;

            Assert.AreEqual(3, receiver.CarriedCoins);
            Assert.AreEqual(8, inventory.Get(AmmoType.Light));
            Assert.IsTrue(coins == null);
            Assert.IsTrue(ammo == null);
            Assert.IsNotNull(rifle);
            Assert.AreEqual(0, attractor.PulledCount);
        }
    }
}
