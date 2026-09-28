using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Tests
{
    public class StorageTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private PlayerInventory _inventory;
        private Storage _storage;
        private StorageService _service;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[]
            {
                Def<TestItemDefinition>("weapon_p9_ranger", ItemCategory.Weapon),
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
                Def<TestItemDefinition>("consumable_bandage", ItemCategory.Consumable, true, 5),
                Ammo("ammo_light", AmmoType.Light)
            });
            _inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            _storage = new Storage(id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);
            _service = new StorageService(_storage);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Def<T>(string id, ItemCategory category, bool stackable = false, int maxStack = 1) where T : ItemDefinition
        {
            var d = ScriptableObject.CreateInstance<T>();
            _created.Add(d);
            typeof(ItemDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, id);
            typeof(ItemDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, category);
            typeof(ItemDefinition).GetField("_isStackable", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, stackable);
            typeof(ItemDefinition).GetField("_maxStack", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, maxStack);
            return d;
        }

        private AmmoItemDefinition Ammo(string id, AmmoType type)
        {
            var d = Def<AmmoItemDefinition>(id, ItemCategory.Ammo, true, 999);
            typeof(AmmoItemDefinition).GetField("_ammoType", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, type);
            return d;
        }

        // ---- Acceptance 1 ----

        [Test]
        public void FreshStorage_HasExactlySixtySlots_AndNoUpgradeApi()
        {
            Assert.AreEqual(60, Storage.BaseCapacity);
            Assert.AreEqual(60, _storage.Capacity);
            Assert.AreEqual(60, _storage.Slots.Count);
            Assert.AreEqual(60, _storage.FreeSlots);
            Assert.IsFalse(typeof(Storage).GetMethods().Any(m => m.Name.Contains("Upgrade") || m.Name.Contains("Buy")), "Storage never sells upgrades itself: the Workshop (TASK 043) is the only path that can grow it.");
            Assert.IsNull(typeof(Storage).GetProperty("Capacity").GetSetMethod(true), "Capacity is fixed.");
        }

        // ---- Acceptance 2 ----

        [Test]
        public void InventoryToStorageAndBack_IsAtomic_AndPreservesIdsQuantitiesRarityAffixes()
        {
            var pistol = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare);
            pistol.AddAffixRoll(new AffixRoll("affix_damage", 8));
            var bandages = new ItemInstance("consumable_bandage", 4);
            _inventory.TryAddToBackpack(pistol);
            _inventory.TryAddToBackpack(bandages);
            _inventory.Add(AmmoType.Light, 50);
            var backpack = new BackpackContainer(_inventory);

            var deposit = _service.Deposit(backpack, pistol.InstanceId);
            Assert.IsTrue(deposit.Success, deposit.Error.ToString());
            Assert.AreSame(pistol, _storage.Find(pistol.InstanceId), "Same instance moved, not copied.");
            Assert.IsFalse(_inventory.Contains(pistol.InstanceId));
            Assert.AreEqual(Rarity.Rare, _storage.Find(pistol.InstanceId).Rarity);
            Assert.AreEqual(8, _storage.Find(pistol.InstanceId).AffixRolls[0].Value);

            // Stackables are absorbed into the backpack's own stacks; operate on the live stack instance.
            var liveBandages = _inventory.BackpackSlots.Single(s => s != null && s.DefinitionId == "consumable_bandage");
            Assert.IsTrue(_service.DepositQuantity(backpack, liveBandages.InstanceId, 3).Success);
            Assert.AreEqual(1, liveBandages.Quantity);
            Assert.AreEqual(3, _storage.CountOf("consumable_bandage"));

            var ammoStack = _inventory.BackpackSlots.Single(s => s != null && s.DefinitionId == "ammo_light");
            Assert.IsTrue(_service.Deposit(backpack, ammoStack.InstanceId).Success);
            Assert.AreEqual(0, _inventory.Get(AmmoType.Light));
            Assert.AreEqual(50, _storage.CountOf("ammo_light"));

            var withdraw = _service.Withdraw(pistol.InstanceId, backpack);
            Assert.IsTrue(withdraw.Success);
            Assert.AreSame(pistol, _inventory.BackpackSlots.Single(s => s != null && s.InstanceId == pistol.InstanceId));
            Assert.IsNull(_storage.Find(pistol.InstanceId));

            var storedAmmo = _storage.Items.Single(i => i.DefinitionId == "ammo_light");
            Assert.IsTrue(_service.WithdrawQuantity(storedAmmo.InstanceId, 20, backpack).Success);
            Assert.AreEqual(20, _inventory.Get(AmmoType.Light));
            Assert.AreEqual(30, _storage.CountOf("ammo_light"));
            Assert.AreEqual(50, _inventory.Get(AmmoType.Light) + _storage.CountOf("ammo_light"), "Units are conserved.");

            var containers = new IItemContainer[] { backpack, _storage };
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(containers));
        }

        [Test]
        public void Storage_StacksLikeTheBackpack_UsingApprovedAmmoLimits()
        {
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("ammo_light", 180)));
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("ammo_light", 100)));
            Assert.AreEqual(280, _storage.CountOf("ammo_light"));
            Assert.AreEqual(2, _storage.OccupiedSlots, "180 per Light stack: 180 + 100 = two slots.");
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("consumable_bandage", 5)));
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("consumable_bandage", 2)));
            Assert.AreEqual(4, _storage.OccupiedSlots, "Bandages cap at 5 per stack.");
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("weapon_p9_ranger")));
            Assert.AreEqual(5, _storage.OccupiedSlots, "Equipment is one slot.");
        }

        // ---- Acceptance 3 ----

        [Test]
        public void FullStorage_RejectsIncomingItem_WithoutSourceLoss()
        {
            for (var i = 0; i < 60; i++) Assert.IsTrue(_storage.TryAdd(new ItemInstance("weapon_p9_ranger")));
            Assert.AreEqual(0, _storage.FreeSlots);

            var vest = new ItemInstance("armor_scrap_vest");
            _inventory.TryAddToBackpack(vest);
            var backpack = new BackpackContainer(_inventory);

            var result = _service.Deposit(backpack, vest.InstanceId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(vest, backpack.Find(vest.InstanceId), "Rejected item stays in the backpack.");
            Assert.AreEqual(60, _storage.OccupiedSlots);

            _inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 3));
            var stack = _inventory.BackpackSlots.Single(s => s != null && s.DefinitionId == "consumable_bandage");
            Assert.IsFalse(_service.DepositQuantity(backpack, stack.InstanceId, 2).Success);
            Assert.AreEqual(3, stack.Quantity, "Split never happens when the destination cannot take it.");
        }

        // ---- Acceptance 4 ----

        [Test]
        public void Storage_RoundTripsThroughSnapshot()
        {
            var pistol = new ItemInstance("weapon_p9_ranger", 1, Rarity.Epic);
            pistol.AddAffixRoll(new AffixRoll("affix_damage", 9));
            pistol.AddAffixRoll(new AffixRoll("affix_range", 12));
            _storage.TryAdd(pistol);
            _storage.TryAdd(new ItemInstance("ammo_light", 77));
            _storage.TryAdd(new ItemInstance("consumable_bandage", 5));

            var json = JsonUtility.ToJson(_storage.ToSnapshot());
            var restored = Storage.FromSnapshot(JsonUtility.FromJson<StorageSnapshot>(json), id => _registry.TryGet(id, out var d) ? d : null, _ammoBalance);

            Assert.AreEqual(60, restored.Capacity);
            Assert.AreEqual(3, restored.OccupiedSlots);
            var restoredPistol = restored.Find(pistol.InstanceId);
            Assert.IsNotNull(restoredPistol);
            Assert.AreEqual(Rarity.Epic, restoredPistol.Rarity);
            CollectionAssert.AreEqual(pistol.AffixRolls.Select(a => (a.AffixId, a.Value)), restoredPistol.AffixRolls.Select(a => (a.AffixId, a.Value)));
            Assert.AreEqual(77, restored.CountOf("ammo_light"));
            Assert.AreEqual(5, restored.CountOf("consumable_bandage"));
            Assert.AreEqual(_storage.ToSnapshot().Slots.Length, restored.ToSnapshot().Slots.Length);
            Assert.IsEmpty(Storage.FromSnapshot(null, id => null, null).Items);
        }

        // ---- Requirement 5: at-risk items cannot be laundered through Storage ----

        [Test]
        public void Storage_RefusesAtRiskItems_UntilExtractionClearsTheFlag()
        {
            var atRisk = new ItemInstance("weapon_p9_ranger") { IsAtRisk = true };
            var ground = new ListItemContainer("ground");
            ground.TryAdd(atRisk);

            var result = _service.Deposit(ground, atRisk.InstanceId);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(TransferError.DestinationRejected, result.Error);
            Assert.AreSame(atRisk, ground.Find(atRisk.InstanceId));
            Assert.IsFalse(_storage.TryAdd(atRisk));

            atRisk.IsAtRisk = false; // what the Return transaction does
            Assert.IsTrue(_service.Deposit(ground, atRisk.InstanceId).Success);
        }

        [Test]
        public void Backpack_StillBehavesAsBefore_OnTheSharedSlotContainer()
        {
            Assert.AreEqual(8, _inventory.Backpack.Capacity);
            for (var i = 0; i < 8; i++) Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")));
            var extra = new ItemInstance("weapon_p9_ranger");
            Assert.IsFalse(_inventory.TryAddToBackpack(extra));
            Assert.AreEqual(0, _inventory.Add(AmmoType.Light, 10), "Full backpack takes no ammo.");
            var changed = 0;
            _inventory.BackpackChanged += () => changed++;
            _inventory.RemoveFromBackpack(0);
            Assert.AreEqual(1, changed);
        }

        // ---- Storage ↔ worn exchange (94 Storage: equip straight from Storage) ----

        private IEnumerable<string> AllIds() =>
            System.Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Select(_inventory.GetEquipped).Concat(_inventory.BackpackSlots).Concat(_storage.Items)
                .Where(i => i != null).Select(i => i.InstanceId);

        [Test]
        public void ExchangeWithEquipped_SwapsIntoTheSameCell_WithStorageAndBackpackFull_NothingLostOrDuplicated()
        {
            var worn = new ItemInstance("weapon_p9_ranger", 1, Rarity.Common) { IsUnsellable = true };
            Assert.IsTrue(_inventory.TryEquip(worn, EquippedSlot.PrimaryWeapon));
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")));
            var stored = new ItemInstance("weapon_p9_ranger", 1, Rarity.Epic);
            while (_storage.FreeSlots > 1) Assert.IsTrue(_storage.TryAdd(new ItemInstance("armor_scrap_vest")));
            Assert.IsTrue(_storage.TryAdd(stored));
            Assert.AreEqual(0, _storage.FreeSlots, "Storage full");
            var cell = _storage.Slots.ToList().IndexOf(stored);
            var before = AllIds().OrderBy(x => x).ToList();
            var storageChanges = 0;
            _storage.Changed += () => storageChanges++;

            var result = _service.ExchangeWithEquipped(_inventory, stored.InstanceId, EquippedSlot.PrimaryWeapon);

            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreSame(stored, _inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "the stored item is worn");
            Assert.AreSame(worn, _storage.Slots[cell], "the worn item took exactly the stored item's cell");
            Assert.IsTrue(worn.IsUnsellable, "its flags travel with it");
            CollectionAssert.AreEqual(before, AllIds().OrderBy(x => x).ToList(), "same items, each exactly once");
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(new IItemContainer[] { new BackpackContainer(_inventory), new EquippedSlotContainer(_inventory, EquippedSlot.PrimaryWeapon), _storage }));
            Assert.AreEqual(1, storageChanges, "one Storage change (one autosave mark)");
        }

        [Test]
        public void ExchangeWithEquipped_IntoAnEmptySlot_IsAPlainWithdraw_AndInvalidPairsChangeNothing()
        {
            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            var ammo = new ItemInstance("ammo_light", 30);
            Assert.IsTrue(_storage.TryAdd(vest));
            Assert.IsTrue(_storage.TryAdd(ammo));
            var storedAmmo = _storage.Items.First(i => i.DefinitionId == "ammo_light");
            var before = AllIds().OrderBy(x => x).ToList();

            Assert.AreEqual(TransferError.DestinationRejected, _service.ExchangeWithEquipped(_inventory, vest.InstanceId, EquippedSlot.PrimaryWeapon).Error, "armor never goes into a weapon slot");
            Assert.AreEqual(TransferError.DestinationRejected, _service.ExchangeWithEquipped(_inventory, storedAmmo.InstanceId, EquippedSlot.ActiveConsumable).Error, "ammo has no worn slot");
            Assert.AreEqual(TransferError.SourceMissingItem, _service.ExchangeWithEquipped(_inventory, "nope", EquippedSlot.Armor).Error);
            CollectionAssert.AreEqual(before, AllIds().OrderBy(x => x).ToList(), "refusals change nothing");
            Assert.IsNull(_inventory.GetEquipped(EquippedSlot.Armor));

            Assert.IsTrue(_service.ExchangeWithEquipped(_inventory, vest.InstanceId, EquippedSlot.Armor).Success);
            Assert.AreSame(vest, _inventory.GetEquipped(EquippedSlot.Armor));
            Assert.IsNull(_storage.Find(vest.InstanceId));
        }

        [Test]
        public void ExchangeWithEquipped_ConsumableStacksSwapWhole_AndAnAtRiskWornItemIsRefused()
        {
            Assert.IsTrue(_inventory.TryEquip(new ItemInstance("consumable_bandage", 2), EquippedSlot.ActiveConsumable));
            var worn = _inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.IsTrue(_storage.TryAdd(new ItemInstance("consumable_bandage", 5)));
            var stored = _storage.Items.Single();
            Assert.IsTrue(_service.ExchangeWithEquipped(_inventory, stored.InstanceId, EquippedSlot.ActiveConsumable).Success);
            Assert.AreEqual(5, _inventory.GetEquipped(EquippedSlot.ActiveConsumable).Quantity);
            Assert.AreEqual(2, _storage.Find(worn.InstanceId).Quantity, "the whole worn stack went to Storage, nothing merged away");

            // Storage never holds an at-risk item: an exchange that would put one there is refused unchanged.
            var risky = new ItemInstance("weapon_p9_ranger") { IsAtRisk = true };
            Assert.IsTrue(_inventory.TryEquip(risky, EquippedSlot.SecondaryWeapon));
            var spare = new ItemInstance("weapon_p9_ranger");
            Assert.IsTrue(_storage.TryAdd(spare));
            Assert.IsFalse(_service.ExchangeWithEquipped(_inventory, spare.InstanceId, EquippedSlot.SecondaryWeapon).Success);
            Assert.AreSame(risky, _inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.IsNotNull(_storage.Find(spare.InstanceId));
        }

        // ---- Storage ↔ backpack exchange (94 Storage: a drop onto an occupied cell) ----

        [Test]
        public void ExchangeWithBackpack_TradesPlaces_WithBothSidesFull_EachItemExactlyOnce()
        {
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")));
            var carried = _inventory.BackpackSlots[3];
            var stored = new ItemInstance("weapon_p9_ranger", 1, Rarity.Epic);
            while (_storage.FreeSlots > 1) Assert.IsTrue(_storage.TryAdd(new ItemInstance("armor_scrap_vest")));
            Assert.IsTrue(_storage.TryAdd(stored));
            var cell = _storage.Slots.ToList().IndexOf(stored);
            var before = AllIds().OrderBy(x => x).ToList();
            var bagChanges = 0;
            var storageChanges = 0;
            var consistentAtEveryChange = true;
            void Check() => consistentAtEveryChange &= AllIds().Count() == before.Count && AllIds().Distinct().Count() == before.Count;
            _inventory.BackpackChanged += () => { bagChanges++; Check(); };
            _storage.Changed += () => { storageChanges++; Check(); };

            var result = _service.ExchangeWithBackpack(_inventory, stored.InstanceId, 3);

            Assert.IsTrue(result.Success, result.Error.ToString());
            Assert.AreSame(stored, _inventory.BackpackSlots[3], "the stored item takes the backpack cell");
            Assert.AreSame(carried, _storage.Slots[cell], "the carried item takes the stored item's cell");
            CollectionAssert.AreEqual(before, AllIds().OrderBy(x => x).ToList());
            Assert.IsTrue(consistentAtEveryChange, "no observer ever sees an item twice or missing");
            Assert.AreEqual(1, bagChanges);
            Assert.AreEqual(1, storageChanges);
        }

        [Test]
        public void ExchangeWithBackpack_RefusesUnchanged_ForEmptySlots_AtRiskItems_AndSameStackableKind()
        {
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 2)));
            var stored = new ItemInstance("consumable_bandage", 3);
            Assert.IsTrue(_storage.TryAdd(stored));
            var storedBandage = _storage.Items.Single();
            var before = AllIds().OrderBy(x => x).ToList();
            Assert.IsFalse(_service.ExchangeWithBackpack(_inventory, storedBandage.InstanceId, 0).Success, "same stackable kind merges instead");
            Assert.IsFalse(_service.ExchangeWithBackpack(_inventory, storedBandage.InstanceId, 5).Success, "an empty backpack cell is a plain take");
            Assert.IsFalse(_service.ExchangeWithBackpack(_inventory, "nope", 0).Success);
            var risky = new ItemInstance("weapon_p9_ranger") { IsAtRisk = true };
            Assert.IsTrue(_inventory.TryAddToBackpack(risky));
            Assert.IsFalse(_service.ExchangeWithBackpack(_inventory, storedBandage.InstanceId, _inventory.BackpackSlots.ToList().IndexOf(risky)).Success, "Storage never takes an at-risk item");
            CollectionAssert.AreEqual(before.Append(risky.InstanceId).OrderBy(x => x).ToList(), AllIds().OrderBy(x => x).ToList(), "refusals change nothing");
            Assert.AreEqual(2, _inventory.BackpackSlots[0].Quantity);
            Assert.AreEqual(3, _storage.Items.Single().Quantity);
        }
    }
}
