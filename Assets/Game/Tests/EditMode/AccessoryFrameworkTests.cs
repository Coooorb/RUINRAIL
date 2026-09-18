using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class AccessoryFrameworkTests
    {
        private readonly List<Object> _created = new();
        private GlobalStatCapsConfig _caps;
        private AccessoryDefinition _watch;
        private AccessoryDefinition _pouch;
        private AmmoItemDefinition _ammo;
        private AmmoBalanceConfig _ammoBalance;
        private ItemDefinitionRegistry _registry;
        private PlayerInventory _inventory;
        private PlayerStats _stats;
        private LoadoutStatRegistrar _registrar;

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(_caps);
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _watch = Accessory("accessory_runners_watch", new StatModifierEntry(StatId.MovementSpeed, StatModifierKind.Percent, 5));
            _pouch = Accessory("accessory_ammo_pouch", new StatModifierEntry(StatId.AmmoStackCapacity, StatModifierKind.Percent, 25));
            _ammo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            _created.Add(_ammo);
            Set(_ammo, "_id", "ammo_light");
            Set(_ammo, "_category", ItemCategory.Ammo);
            Set(_ammo, "_isStackable", true);
            Set(_ammo, "_ammoType", AmmoType.Light);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _watch, _pouch, _ammo });
            _inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            _stats = new PlayerStats(_caps, 100);
            _registrar = new LoadoutStatRegistrar(_inventory, _stats, id => _registry.TryGet(id, out var d) ? d : null, _ => null);
            _inventory.SetAmmoCapacityBonusProvider(() => _stats.GetPercent(StatId.AmmoStackCapacity));
        }

        [TearDown]
        public void TearDown()
        {
            _registrar.Dispose();
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

        private AccessoryDefinition Accessory(string id, params StatModifierEntry[] intrinsic)
        {
            var d = ScriptableObject.CreateInstance<AccessoryDefinition>();
            _created.Add(d);
            Set(d, "_id", id);
            Set(d, "_category", ItemCategory.Accessory);
            Set(d, "_intrinsic", intrinsic);
            return d;
        }

        [Test]
        public void RunnersWatchAsset_HasTheApprovedIntrinsic()
        {
            var watch = AssetDatabase.LoadAssetAtPath<AccessoryDefinition>("Assets/Game/ScriptableObjects/Items/RunnersWatch.asset");
            Assert.IsNotNull(watch);
            Assert.AreEqual("accessory_runners_watch", watch.Id);
            Assert.AreEqual("Runner's Watch", watch.DisplayName);
            Assert.AreEqual(ItemCategory.Accessory, watch.Category);
            CollectionAssert.AreEqual(new[] { StatModifier.Percent(StatId.MovementSpeed, 5) }, watch.BaseModifiers());
        }

        // ---- Acceptance 1 ----

        [Test]
        public void AccessorySlot_AcceptsOnlyAccessories_AndExactlyOne()
        {
            var watch = new ItemInstance("accessory_runners_watch");
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                if (slot == EquippedSlot.Accessory) continue;
                Assert.IsFalse(_inventory.TryEquip(watch, slot), $"Accessory must not fit {slot}.");
            }

            Assert.IsFalse(_inventory.TryEquip(new ItemInstance("ammo_light", 10), EquippedSlot.Accessory));
            Assert.IsTrue(_inventory.TryEquip(watch, EquippedSlot.Accessory));
            Assert.IsFalse(_inventory.TryEquip(new ItemInstance("accessory_ammo_pouch"), EquippedSlot.Accessory), "One accessory slot.");
            Assert.AreEqual(5, _stats.GetPercent(StatId.MovementSpeed));
            Assert.AreEqual(0, _stats.GetPercent(StatId.AmmoStackCapacity), "Only the equipped accessory contributes.");
        }

        // ---- Acceptance 2 ----

        [Test]
        public void Intrinsic_AppliesOnceAndRemovesOnce_AcrossRepeatedEquipCycles()
        {
            var watch = new ItemInstance("accessory_runners_watch");
            for (var i = 0; i < 6; i++)
            {
                _inventory.TryEquip(watch, EquippedSlot.Accessory);
                Assert.AreEqual(5, _stats.GetPercent(StatId.MovementSpeed), $"cycle {i}");
                Assert.AreEqual(1, _stats.SourceIds.Count);
                _inventory.Unequip(EquippedSlot.Accessory);
                Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed));
                Assert.AreEqual(0, _stats.SourceIds.Count);
            }

            _inventory.TryEquip(watch, EquippedSlot.Accessory);
            _inventory.Unequip(EquippedSlot.Accessory);
            _inventory.TryEquip(new ItemInstance("accessory_ammo_pouch"), EquippedSlot.Accessory);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Swapping accessories replaces the intrinsic.");
            Assert.AreEqual(25, _stats.GetPercent(StatId.AmmoStackCapacity));
        }

        // ---- Acceptance 3 ----

        [Test]
        public void Intrinsic_IsIdenticalAtEveryRarity_AffixesOnlyAddOnTop()
        {
            var affix = ScriptableObject.CreateInstance<AffixDefinition>();
            _created.Add(affix);
            Set(affix, "_id", "affix_movement_speed");
            Set(affix, "_stat", AffixStat.MovementSpeed);
            _registrar.Dispose();
            _registrar = new LoadoutStatRegistrar(_inventory, _stats, id => _registry.TryGet(id, out var d) ? d : null, id => id == "affix_movement_speed" ? affix : null);

            foreach (Rarity rarity in System.Enum.GetValues(typeof(Rarity)))
            {
                var watch = new ItemInstance("accessory_runners_watch", 1, rarity);
                _inventory.TryEquip(watch, EquippedSlot.Accessory);
                Assert.AreEqual(5, _stats.GetPercent(StatId.MovementSpeed), $"{rarity}: intrinsic independent of rarity.");
                _inventory.Unequip(EquippedSlot.Accessory);
            }

            var rare = new ItemInstance("accessory_runners_watch", 1, Rarity.Rare);
            rare.AddAffixRoll(new AffixRoll("affix_movement_speed", 9));
            _inventory.TryEquip(rare, EquippedSlot.Accessory);
            Assert.AreEqual(14, _stats.GetPercent(StatId.MovementSpeed), "5 intrinsic + 9 affix.");
            Assert.AreEqual(1, _stats.SourceIds.Count, "Still one source for the item.");
        }

        // ---- Acceptance 4 + behaviour hook ----

        [Test]
        public void AccessoryStats_RespectGlobalCaps()
        {
            _inventory.TryEquip(new ItemInstance("accessory_runners_watch"), EquippedSlot.Accessory);
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.MovementSpeed, 8)));
            _stats.SetSource(new StatModifierSource("skills", StatModifier.Percent(StatId.MovementSpeed, 20)));
            Assert.AreEqual(30, _stats.GetPercent(StatId.MovementSpeed), "5 + 8 + 20 = 33 clamps at +30%.");
            Assert.AreEqual(1.3f, _stats.GetMultiplier(StatId.MovementSpeed), 0.0001f);
        }

        [Test]
        public void AmmoPouchIntrinsic_RaisesBackpackAmmoLimits_ThroughTheNarrowHook()
        {
            Assert.AreEqual(180, _inventory.MaxStackFor(_ammo));
            _inventory.TryEquip(new ItemInstance("accessory_ammo_pouch"), EquippedSlot.Accessory);
            Assert.AreEqual(225, _inventory.MaxStackFor(_ammo), "180 × 1.25");
            Assert.AreEqual(225, _inventory.Add(AmmoType.Light, 225));
            Assert.AreEqual(1, _inventory.BackpackSlots.Count(s => s != null), "Fits one stack with the pouch.");
            _inventory.Unequip(EquippedSlot.Accessory);
            Assert.AreEqual(180, _inventory.MaxStackFor(_ammo));
            Assert.AreEqual(225, _inventory.Get(AmmoType.Light), "Existing units are never destroyed when the limit drops.");
        }

        [Test]
        public void NoAccessoryTypeSwitch_ExistsInTheFramework()
        {
            var source = typeof(EquippedItemStatSource);
            Assert.IsNull(source.GetMethod("ApplyAccessory"));
            Assert.IsNotNull(typeof(EquipmentItemDefinition).GetMethod("BaseModifiers"), "Definitions describe their own base modifiers.");
            Assert.IsTrue(typeof(EquipmentItemDefinition).GetMethod("BaseModifiers").IsVirtual);
        }
    }
}
