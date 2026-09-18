using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ItemTransferServiceTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private PlayerInventory _inventory;
        private BackpackContainer _backpack;
        private ListItemContainer _ground;
        private ItemTransferService _service;
        private readonly System.Collections.Generic.List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);

            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[]
            {
                Def<TestItemDefinition>("weapon_p9_ranger", ItemCategory.Weapon),
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
                Def<TestItemDefinition>("consumable_bandage", ItemCategory.Consumable, stackable: true, maxStack: 5),
                Ammo("ammo_light", AmmoType.Light)
            });
            Assert.IsEmpty(_registry.Problems);

            _inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            _backpack = new BackpackContainer(_inventory);
            _ground = new ListItemContainer("ground");
            _service = new ItemTransferService();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Def<T>(string id, ItemCategory category, bool stackable = false, int maxStack = 1) where T : ItemDefinition
        {
            var d = ScriptableObject.CreateInstance<T>();
            d.name = id;
            typeof(ItemDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, id);
            typeof(ItemDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, category);
            typeof(ItemDefinition).GetField("_isStackable", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, stackable);
            typeof(ItemDefinition).GetField("_maxStack", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, maxStack);
            _created.Add(d);
            return d;
        }

        private AmmoItemDefinition Ammo(string id, AmmoType type)
        {
            var d = Def<AmmoItemDefinition>(id, ItemCategory.Ammo, stackable: true, maxStack: 999);
            typeof(AmmoItemDefinition).GetField("_ammoType", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, type);
            return d;
        }

        private EquippedSlotContainer Slot(EquippedSlot slot) => new(_inventory, slot);

        // ---- Acceptance 1: exactly-once transfer ----

        [Test]
        public void Transfer_GroundToBackpack_MovesInstanceExactlyOnce_PreservingIdentity()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _ground.TryAdd(pistol);

            var result = _service.Transfer(_ground, pistol.InstanceId, _backpack);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(TransferError.None, result.Error);
            Assert.AreEqual(pistol.InstanceId, result.InstanceId);
            Assert.IsNull(_ground.Find(pistol.InstanceId));
            Assert.AreSame(pistol, _backpack.Find(pistol.InstanceId), "Equipment must keep its unique instance identity.");
            Assert.AreEqual(1, _backpack.Items.Count());
            Assert.AreEqual(0, _ground.Count);
        }

        [Test]
        public void Transfer_SameInstanceTwice_SecondAttemptFailsWithoutDuplicating()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _ground.TryAdd(pistol);

            Assert.IsTrue(_service.Transfer(_ground, pistol.InstanceId, _backpack).Success);
            var second = _service.Transfer(_ground, pistol.InstanceId, _backpack);

            Assert.IsFalse(second.Success);
            Assert.AreEqual(TransferError.SourceMissingItem, second.Error);
            Assert.AreEqual(1, _backpack.Items.Count());
        }

        [Test]
        public void Transfer_BackpackToEquippedSlot_AndBack_RoundTripsSameInstance()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _backpack.TryAdd(pistol);
            var primary = Slot(EquippedSlot.PrimaryWeapon);

            Assert.IsTrue(_service.Transfer(_backpack, pistol.InstanceId, primary).Success);
            Assert.AreSame(pistol, _inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsNull(_backpack.Find(pistol.InstanceId));

            Assert.IsTrue(_service.Transfer(primary, pistol.InstanceId, _backpack).Success);
            Assert.IsNull(_inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.AreSame(pistol, _backpack.Find(pistol.InstanceId));
        }

        // ---- Acceptance 2: failed validation leaves both sides unchanged ----

        [Test]
        public void Transfer_IncompatibleEquippedSlot_LeavesBothSidesUnchanged()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _backpack.TryAdd(pistol);
            var armorSlot = Slot(EquippedSlot.Armor);

            var result = _service.Transfer(_backpack, pistol.InstanceId, armorSlot);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(pistol, _backpack.Find(pistol.InstanceId));
            Assert.IsNull(_inventory.GetEquipped(EquippedSlot.Armor));
        }

        [Test]
        public void Transfer_OccupiedEquippedSlot_LeavesBothSidesUnchanged()
        {
            var equipped = new ItemInstance("armor_scrap_vest");
            var spare = new ItemInstance("armor_scrap_vest");
            _inventory.TryEquip(equipped, EquippedSlot.Armor);
            _backpack.TryAdd(spare);

            var result = _service.Transfer(_backpack, spare.InstanceId, Slot(EquippedSlot.Armor));

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(equipped, _inventory.GetEquipped(EquippedSlot.Armor));
            Assert.AreSame(spare, _backpack.Find(spare.InstanceId));
        }

        [Test]
        public void Transfer_FullBackpack_LeavesBothSidesUnchanged()
        {
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
            {
                Assert.IsTrue(_backpack.TryAdd(new ItemInstance("weapon_p9_ranger")));
            }

            var extra = new ItemInstance("weapon_p9_ranger");
            _ground.TryAdd(extra);

            var result = _service.Transfer(_ground, extra.InstanceId, _backpack);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(extra, _ground.Find(extra.InstanceId));
            Assert.AreEqual(PlayerInventory.BackpackCapacity, _backpack.Items.Count());
        }

        [Test]
        public void Transfer_MissingSourceItem_FailsWithoutMutation()
        {
            var result = _service.Transfer(_ground, "not-here", _backpack);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.SourceMissingItem, result.Error);
            Assert.AreEqual(0, _backpack.Items.Count());
        }

        [Test]
        public void Transfer_InvalidRequests_ReturnInvalidRequest_WithoutThrowing()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _ground.TryAdd(pistol);

            Assert.AreEqual(TransferError.InvalidRequest, _service.Transfer(null, pistol.InstanceId, _backpack).Error);
            Assert.AreEqual(TransferError.InvalidRequest, _service.Transfer(_ground, pistol.InstanceId, null).Error);
            Assert.AreEqual(TransferError.InvalidRequest, _service.Transfer(_ground, null, _backpack).Error);
            Assert.AreEqual(TransferError.InvalidRequest, _service.Transfer(_ground, pistol.InstanceId, _ground).Error);
            Assert.AreSame(pistol, _ground.Find(pistol.InstanceId));
        }

        // ---- Acceptance 3: split + merge preserves totals ----

        [Test]
        public void TransferQuantity_SplitsStack_WithoutCreatingOrDeletingUnits()
        {
            var bandages = new ItemInstance("consumable_bandage", 5);
            _ground.TryAdd(bandages);

            var result = _service.TransferQuantity(_ground, bandages.InstanceId, 2, _backpack);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Quantity);
            Assert.AreNotEqual(bandages.InstanceId, result.InstanceId, "Split portion is a new stack instance.");
            Assert.AreEqual(3, bandages.Quantity);
            Assert.AreEqual(2, _inventory.CountOf("consumable_bandage"));
            Assert.AreEqual(5, bandages.Quantity + _inventory.CountOf("consumable_bandage"));
        }

        [Test]
        public void TransferQuantity_MergesIntoExistingBackpackStack_PreservingTotal()
        {
            _backpack.TryAdd(new ItemInstance("consumable_bandage", 3));
            var pile = new ItemInstance("consumable_bandage", 4);
            _ground.TryAdd(pile);

            var result = _service.TransferQuantity(_ground, pile.InstanceId, 2, _backpack);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(5, _inventory.CountOf("consumable_bandage"));
            Assert.AreEqual(1, _backpack.Items.Count(), "Merge must fill the existing stack rather than open a new slot.");
            Assert.AreEqual(2, pile.Quantity);
            Assert.AreEqual(7, pile.Quantity + _inventory.CountOf("consumable_bandage"));
        }

        [Test]
        public void TransferQuantity_FullQuantity_MovesWholeStackInstance()
        {
            var ammo = new ItemInstance("ammo_light", 30);
            _ground.TryAdd(ammo);

            var result = _service.TransferQuantity(_ground, ammo.InstanceId, 30, _backpack);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(30, result.Quantity);
            Assert.IsNull(_ground.Find(ammo.InstanceId));
            Assert.AreEqual(30, _inventory.Get(AmmoType.Light));
        }

        [Test]
        public void TransferQuantity_InvalidAmounts_FailWithoutMutation()
        {
            var bandages = new ItemInstance("consumable_bandage", 5);
            _ground.TryAdd(bandages);

            Assert.AreEqual(TransferError.InvalidQuantity, _service.TransferQuantity(_ground, bandages.InstanceId, 0, _backpack).Error);
            Assert.AreEqual(TransferError.InvalidQuantity, _service.TransferQuantity(_ground, bandages.InstanceId, -1, _backpack).Error);
            Assert.AreEqual(TransferError.InvalidQuantity, _service.TransferQuantity(_ground, bandages.InstanceId, 6, _backpack).Error);
            Assert.AreEqual(5, bandages.Quantity);
            Assert.AreEqual(0, _inventory.CountOf("consumable_bandage"));
        }

        [Test]
        public void TransferQuantity_DestinationLacksCapacity_LeavesSourceStackUnchanged()
        {
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
            {
                Assert.IsTrue(_backpack.TryAdd(new ItemInstance("weapon_p9_ranger")));
            }

            var bandages = new ItemInstance("consumable_bandage", 5);
            _ground.TryAdd(bandages);

            var result = _service.TransferQuantity(_ground, bandages.InstanceId, 2, _backpack);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreEqual(5, bandages.Quantity);
            Assert.AreEqual(0, _inventory.CountOf("consumable_bandage"));
        }

        // ---- Acceptance 4: duplicate ownership detectable ----

        [Test]
        public void Transfer_WhenDestinationAlreadyHoldsInstance_FailsWithDuplicateOwnership()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            var otherGround = new ListItemContainer("ground_b");
            _ground.TryAdd(pistol);
            otherGround.TryAdd(pistol); // corrupted state simulated directly on the raw container

            var result = _service.Transfer(_ground, pistol.InstanceId, otherGround);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DuplicateOwnership, result.Error);
            Assert.IsNotNull(_ground.Find(pistol.InstanceId));
        }

        [Test]
        public void DetectDuplicateOwnership_ReportsInstancesHeldByMultipleContainers()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            var vest = new ItemInstance("armor_scrap_vest");
            var otherGround = new ListItemContainer("ground_b");
            _ground.TryAdd(pistol);
            otherGround.TryAdd(pistol);
            _backpack.TryAdd(vest);

            var duplicates = ItemTransferService.DetectDuplicateOwnership(new IItemContainer[] { _ground, otherGround, _backpack });

            CollectionAssert.AreEquivalent(new[] { pistol.InstanceId }, duplicates);
        }

        [Test]
        public void DetectDuplicateOwnership_AfterValidTransfers_ReportsNothing()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            var vest = new ItemInstance("armor_scrap_vest");
            _ground.TryAdd(pistol);
            _ground.TryAdd(vest);

            _service.Transfer(_ground, pistol.InstanceId, _backpack);
            _service.Transfer(_backpack, pistol.InstanceId, Slot(EquippedSlot.PrimaryWeapon));
            _service.Transfer(_ground, vest.InstanceId, Slot(EquippedSlot.Armor));

            var containers = new IItemContainer[] { _ground, _backpack, Slot(EquippedSlot.PrimaryWeapon), Slot(EquippedSlot.Armor) };
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(containers));
        }

        private sealed class AcceptsButFailsOnAddContainer : IItemContainer
        {
            public string ContainerId => "broken";
            public System.Collections.Generic.IEnumerable<ItemInstance> Items => System.Array.Empty<ItemInstance>();
            public ItemInstance Find(string instanceId) => null;
            public bool CanAccept(ItemInstance item) => true;
            public bool TryAdd(ItemInstance item) => false;
            public ItemInstance TryRemove(string instanceId) => null;
        }

        [Test]
        public void Transfer_WhenDestinationAddFailsAfterValidation_RollsBackToSource()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _backpack.TryAdd(pistol);

            var result = _service.Transfer(_backpack, pistol.InstanceId, new AcceptsButFailsOnAddContainer());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(pistol, _backpack.Find(pistol.InstanceId), "Item must return to its source; it can never be lost.");
        }

        [Test]
        public void TransferQuantity_WhenDestinationAddFailsAfterValidation_RestoresSourceQuantity()
        {
            var bandages = new ItemInstance("consumable_bandage", 5);
            _ground.TryAdd(bandages);

            var result = _service.TransferQuantity(_ground, bandages.InstanceId, 2, new AcceptsButFailsOnAddContainer());

            Assert.IsFalse(result.Success);
            Assert.AreEqual(5, bandages.Quantity);
        }

        // ---- Acceptance 5: container adapters expose the underlying inventory faithfully ----

        [Test]
        public void EquippedSlotContainer_ReportsOnlyItsOwnSlotItem()
        {
            var pistol = new ItemInstance("weapon_p9_ranger");
            _inventory.TryEquip(pistol, EquippedSlot.PrimaryWeapon);

            Assert.AreSame(pistol, Slot(EquippedSlot.PrimaryWeapon).Find(pistol.InstanceId));
            Assert.IsNull(Slot(EquippedSlot.SecondaryWeapon).Find(pistol.InstanceId));
            Assert.AreEqual(1, Slot(EquippedSlot.PrimaryWeapon).Items.Count());
            Assert.AreEqual(0, Slot(EquippedSlot.SecondaryWeapon).Items.Count());
        }
    }
}
