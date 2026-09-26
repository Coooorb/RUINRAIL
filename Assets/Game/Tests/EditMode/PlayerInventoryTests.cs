using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Tests
{
    public class PlayerInventoryTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private PlayerInventory _inventory;
        private System.Collections.Generic.List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);

            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[]
            {
                Def<TestItemDefinition>("weapon_p9_ranger", ItemCategory.Weapon),
                Def<TestItemDefinition>("weapon_field_knife", ItemCategory.Weapon),
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
                Def<TestItemDefinition>("accessory_runner_watch", ItemCategory.Accessory),
                Def<TestItemDefinition>("consumable_bandage", ItemCategory.Consumable, stackable: true, maxStack: 5),
                Def<TestItemDefinition>("consumable_medkit", ItemCategory.Consumable, stackable: true, maxStack: 3),
                Ammo("ammo_light", AmmoType.Light),
                Ammo("ammo_medium", AmmoType.Medium),
                Ammo("ammo_heavy", AmmoType.Heavy),
                Ammo("ammo_shells", AmmoType.Shells)
            });
            Assert.IsEmpty(_registry.Problems);

            _inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
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

        [Test]
        public void Topology_ExactlyEightBackpackSlots_AndFiveEquippedSlots()
        {
            Assert.AreEqual(8, PlayerInventory.BackpackCapacity);
            Assert.AreEqual(8, _inventory.BackpackSlots.Count);
            CollectionAssert.AreEquivalent(
                new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.Accessory, EquippedSlot.ActiveConsumable },
                System.Enum.GetValues(typeof(EquippedSlot)));
        }

        [Test]
        public void Equipment_OccupiesOneSlot_AndInvalidSlotAssignmentIsRejectedWithoutLoss()
        {
            var armor = new ItemInstance("armor_scrap_vest");

            Assert.IsFalse(_inventory.TryEquip(armor, EquippedSlot.PrimaryWeapon), "Armor must not fit a weapon slot.");
            Assert.IsFalse(_inventory.TryEquip(armor, EquippedSlot.ActiveConsumable));
            Assert.IsFalse(_inventory.Contains(armor.InstanceId), "Rejected item must not be partially stored.");

            Assert.IsTrue(_inventory.TryEquip(armor, EquippedSlot.Armor));
            Assert.AreSame(armor, _inventory.GetEquipped(EquippedSlot.Armor));
            Assert.IsTrue(_inventory.Contains(armor.InstanceId));
        }

        [Test]
        public void PrimaryAndSecondary_AreEquivalent_AndClassAgnostic()
        {
            var knife = new ItemInstance("weapon_field_knife");
            var pistol = new ItemInstance("weapon_p9_ranger");

            Assert.IsTrue(_inventory.TryEquip(knife, EquippedSlot.PrimaryWeapon), "Melee in Primary must be allowed.");
            Assert.IsTrue(_inventory.TryEquip(pistol, EquippedSlot.SecondaryWeapon), "Ranged in Secondary must be allowed.");

            var knife2 = new ItemInstance("weapon_field_knife");
            _inventory.Unequip(EquippedSlot.SecondaryWeapon);
            Assert.IsTrue(_inventory.TryEquip(knife2, EquippedSlot.SecondaryWeapon), "Two melee weapons must be allowed.");

            Assert.IsTrue(WeaponSlotMapping.TryToWeaponSlot(EquippedSlot.PrimaryWeapon, out var p) && p == WeaponSlot.Primary);
            Assert.IsTrue(WeaponSlotMapping.TryToWeaponSlot(EquippedSlot.SecondaryWeapon, out var s) && s == WeaponSlot.Secondary);
            Assert.IsFalse(WeaponSlotMapping.TryToWeaponSlot(EquippedSlot.Armor, out _));
        }

        [Test]
        public void OccupiedEquippedSlot_RejectsSecondItem_WithoutDuplication()
        {
            var a = new ItemInstance("weapon_p9_ranger");
            var b = new ItemInstance("weapon_p9_ranger");
            Assert.IsTrue(_inventory.TryEquip(a, EquippedSlot.PrimaryWeapon));

            Assert.IsFalse(_inventory.TryEquip(b, EquippedSlot.PrimaryWeapon));
            Assert.AreSame(a, _inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsFalse(_inventory.Contains(b.InstanceId));
        }

        [Test]
        public void SameInstance_CannotBeStoredTwice()
        {
            var a = new ItemInstance("weapon_p9_ranger");
            Assert.IsTrue(_inventory.TryAddToBackpack(a));
            Assert.IsFalse(_inventory.TryAddToBackpack(a));
            Assert.IsFalse(_inventory.TryEquip(a, EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(1, _inventory.BackpackSlots.Count(s => s != null));
        }

        [Test]
        public void Backpack_OverflowIsRejected_AtomicallyWithoutLoss()
        {
            for (var i = 0; i < 8; i++)
            {
                Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")));
            }

            var ninth = new ItemInstance("armor_scrap_vest");
            Assert.IsFalse(_inventory.TryAddToBackpack(ninth));
            Assert.IsFalse(_inventory.Contains(ninth.InstanceId));
            Assert.AreEqual(8, _inventory.BackpackSlots.Count(s => s != null));
        }

        [Test]
        public void IdenticalConsumables_Stack_UpToDefinitionLimit()
        {
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 3)));
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 2)));
            Assert.AreEqual(1, _inventory.BackpackSlots.Count(s => s != null), "5 bandages fit one stack (max 5).");
            Assert.AreEqual(5, _inventory.CountOf("consumable_bandage"));

            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 1)));
            Assert.AreEqual(2, _inventory.BackpackSlots.Count(s => s != null), "A 6th bandage opens a second stack.");
            Assert.AreEqual(6, _inventory.CountOf("consumable_bandage"));
        }

        [Test]
        public void DifferentConsumables_NeverShareAStack()
        {
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 1)));
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 1)));
            Assert.AreEqual(2, _inventory.BackpackSlots.Count(s => s != null));
        }

        [Test]
        public void StackOverflow_IsRejectedAtomically()
        {
            for (var i = 0; i < 7; i++)
            {
                Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")));
            }

            // one free slot: 5 bandages fit, 6 do not (would need 2 slots)
            var six = new ItemInstance("consumable_bandage", 6);
            Assert.IsFalse(_inventory.TryAddToBackpack(six));
            Assert.AreEqual(6, six.Quantity, "Rejected stack must keep its full quantity.");
            Assert.AreEqual(0, _inventory.CountOf("consumable_bandage"));

            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 5)));
            Assert.AreEqual(5, _inventory.CountOf("consumable_bandage"));
        }

        [Test]
        public void Ammo_StacksPerType_UsingApprovedLimits()
        {
            Assert.AreEqual(180, _inventory.Add(AmmoType.Light, 180));
            Assert.AreEqual(1, _inventory.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(180, _inventory.Get(AmmoType.Light));

            Assert.AreEqual(10, _inventory.Add(AmmoType.Light, 10));
            Assert.AreEqual(2, _inventory.BackpackSlots.Count(s => s != null), "Light ammo beyond 180 opens a new stack.");

            Assert.AreEqual(40, _inventory.Add(AmmoType.Shells, 40));
            Assert.AreEqual(3, _inventory.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(1, _inventory.Add(AmmoType.Shells, 1));
            Assert.AreEqual(4, _inventory.BackpackSlots.Count(s => s != null), "Shells stack limit is 40.");
        }

        [Test]
        public void Ammo_Consume_DrainsStacks_AndNeverGoesNegative()
        {
            _inventory.Add(AmmoType.Heavy, 60);
            _inventory.Add(AmmoType.Heavy, 5);
            Assert.AreEqual(65, _inventory.Get(AmmoType.Heavy));

            Assert.AreEqual(62, _inventory.Consume(AmmoType.Heavy, 62));
            Assert.AreEqual(3, _inventory.Get(AmmoType.Heavy));
            Assert.AreEqual(3, _inventory.Consume(AmmoType.Heavy, 100));
            Assert.AreEqual(0, _inventory.Get(AmmoType.Heavy));
            Assert.AreEqual(0, _inventory.BackpackSlots.Count(s => s != null), "Emptied ammo stacks free their slots.");
        }

        [Test]
        public void Ammo_Add_WhenBackpackFull_AddsOnlyWhatFits()
        {
            for (var i = 0; i < 7; i++)
            {
                _inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest"));
            }

            Assert.AreEqual(120, _inventory.Add(AmmoType.Medium, 500), "Only one Medium stack (120) fits in the last slot.");
            Assert.AreEqual(0, _inventory.Add(AmmoType.Medium, 1));
        }

        [Test]
        public void Coins_HaveNoItemCategory()
        {
            Assert.IsFalse(System.Enum.GetNames(typeof(ItemCategory)).Any(n => n.ToLowerInvariant().Contains("coin")));
        }

        [Test]
        public void Snapshot_RoundTripsEquippedAndBackpack()
        {
            var pistol = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare) { IsAtRisk = true };
            pistol.AddAffixRoll(new AffixRoll("affix_test", 4));
            Assert.IsTrue(_inventory.TryEquip(pistol, EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 2)));
            _inventory.Add(AmmoType.Light, 50);

            var json = JsonUtility.ToJson(_inventory.ToSnapshot());
            var restored = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            restored.RestoreFromSnapshot(JsonUtility.FromJson<InventorySnapshot>(json));

            Assert.AreEqual(pistol.InstanceId, restored.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);
            Assert.AreEqual(Rarity.Rare, restored.GetEquipped(EquippedSlot.PrimaryWeapon).Rarity);
            Assert.IsTrue(restored.GetEquipped(EquippedSlot.PrimaryWeapon).IsAtRisk);
            Assert.AreEqual(2, restored.CountOf("consumable_medkit"));
            Assert.AreEqual(50, restored.Get(AmmoType.Light));
        }

        [Test]
        public void Inventories_AreIndependentPerPlayer()
        {
            var other = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            _inventory.Add(AmmoType.Light, 30);
            Assert.AreEqual(30, _inventory.Get(AmmoType.Light));
            Assert.AreEqual(0, other.Get(AmmoType.Light));
        }

        private string State() => JsonUtility.ToJson(_inventory.ToSnapshot());

        [Test]
        public void SwapEquippedWithBackpack_FullBackpack_ExchangesInPlace_AndNeverShowsAnItemTwice()
        {
            var worn = new ItemInstance("armor_scrap_vest");
            Assert.IsTrue(_inventory.TryEquip(worn, EquippedSlot.Armor));
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance(i == 5 ? "armor_scrap_vest" : "weapon_p9_ranger")));
            var incoming = _inventory.BackpackSlots[5];
            var seen = new System.Collections.Generic.List<string>();
            void Check() => seen.Add(string.Join(",", _inventory.BackpackSlots.Select(b => b.InstanceId).Append(_inventory.GetEquipped(EquippedSlot.Armor)?.InstanceId).GroupBy(x => x).Where(g => g.Key != null && g.Count() > 1).Select(g => g.Key)));
            _inventory.EquippedChanged += (_, _) => Check();
            _inventory.BackpackChanged += Check;

            Assert.IsTrue(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.Armor, 5));
            Assert.AreSame(incoming, _inventory.GetEquipped(EquippedSlot.Armor));
            Assert.AreSame(worn, _inventory.BackpackSlots[5]);
            Assert.IsTrue(_inventory.BackpackSlots.All(b => b != null));
            Assert.AreEqual(2, seen.Count, "one EquippedChanged, one BackpackChanged");
            Assert.IsTrue(seen.All(string.IsNullOrEmpty), "listeners only ever see the final state: " + string.Join(" | ", seen));
        }

        [Test]
        public void SwapEquippedWithBackpack_InvalidRequests_ChangeNothing()
        {
            Assert.IsTrue(_inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(_inventory.TryEquip(new ItemInstance("consumable_bandage", 2), EquippedSlot.ActiveConsumable));
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")));
            Assert.AreEqual(30, _inventory.Add(AmmoType.Light, 30));
            var before = State();
            var events = 0;
            _inventory.EquippedChanged += (_, _) => events++;
            _inventory.BackpackChanged += () => events++;

            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.PrimaryWeapon, 0), "armor into a weapon slot");
            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.ActiveConsumable, 1), "ammo into the consumable slot");
            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.Armor, 0), "an empty worn slot has nothing to swap");
            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.PrimaryWeapon, 4), "an empty backpack slot");
            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.PrimaryWeapon, -1));
            Assert.IsFalse(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.PrimaryWeapon, PlayerInventory.BackpackCapacity));
            Assert.IsFalse(_inventory.TrySwapEquipped(EquippedSlot.PrimaryWeapon, EquippedSlot.ActiveConsumable), "weapon and consumable never trade slots");
            Assert.IsFalse(_inventory.TrySwapEquipped(EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon), "an empty weapon slot is a move, not a swap");
            Assert.IsFalse(_inventory.TrySwapEquipped(EquippedSlot.PrimaryWeapon, EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(before, State());
            Assert.AreEqual(0, events, "a refused swap raises nothing");
        }

        [Test]
        public void SwapWeaponSlots_KeepsOneStatSourcePerWornWeapon()
        {
            var p9 = new ItemInstance("weapon_p9_ranger");
            var knife = new ItemInstance("weapon_field_knife");
            Assert.IsTrue(_inventory.TryEquip(p9, EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(_inventory.TryEquip(knife, EquippedSlot.SecondaryWeapon));
            var stats = new RuinRail.Gameplay.Stats.PlayerStats(null);
            using var registrar = new RuinRail.Gameplay.Stats.LoadoutStatRegistrar(_inventory, stats, id => _registry.TryGet(id, out var d) ? d : null, null);

            Assert.IsTrue(_inventory.TrySwapEquipped(EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon));
            Assert.AreSame(knife, _inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.AreSame(p9, _inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.AreEqual(RuinRail.Gameplay.Stats.EquippedItemStatSource.SourceIdFor(knife), registrar.RegisteredSources[EquippedSlot.PrimaryWeapon]);
            Assert.AreEqual(RuinRail.Gameplay.Stats.EquippedItemStatSource.SourceIdFor(p9), registrar.RegisteredSources[EquippedSlot.SecondaryWeapon]);
            CollectionAssert.IsSupersetOf(stats.SourceIds, new[] { RuinRail.Gameplay.Stats.EquippedItemStatSource.SourceIdFor(knife), RuinRail.Gameplay.Stats.EquippedItemStatSource.SourceIdFor(p9) }, "both worn weapons still feed the stats");
        }
    }
}
