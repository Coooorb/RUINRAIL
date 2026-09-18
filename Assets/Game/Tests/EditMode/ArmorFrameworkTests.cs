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
    public class ArmorFrameworkTests
    {
        private const string ScrapVestPath = "Assets/Game/ScriptableObjects/Items/ScrapVest.asset";
        private readonly List<Object> _created = new();
        private GlobalStatCapsConfig _caps;
        private ArmorDefinition _vest;
        private ItemDefinitionRegistry _registry;
        private PlayerInventory _inventory;
        private PlayerStats _stats;
        private LoadoutStatRegistrar _registrar;

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(_caps);
            _vest = ScriptableObject.CreateInstance<ArmorDefinition>();
            _created.Add(_vest);
            Set(_vest, "_id", "armor_scrap_vest");
            Set(_vest, "_category", ItemCategory.Armor);
            Set(_vest, "_baseMaxHealth", 20);
            Set(_vest, "_baseDamageReductionPercent", 4);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _vest });
            _inventory = PlayerInventory.FromRegistry(_registry, null);
            _stats = new PlayerStats(_caps, 100);
            _registrar = new LoadoutStatRegistrar(_inventory, _stats, id => _registry.TryGet(id, out var d) ? d : null, _ => null);
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

        [Test]
        public void ScrapVestAsset_UsesApprovedValues_AndArmorAffixPool()
        {
            var vest = AssetDatabase.LoadAssetAtPath<ArmorDefinition>(ScrapVestPath);
            Assert.IsNotNull(vest);
            Assert.AreEqual("armor_scrap_vest", vest.Id);
            Assert.AreEqual("Scrap Vest", vest.DisplayName);
            Assert.AreEqual(ItemCategory.Armor, vest.Category);
            Assert.AreEqual(20, vest.BaseMaxHealth);
            Assert.AreEqual(4, vest.BaseDamageReductionPercent);
            Assert.IsEmpty(vest.AdditionalBaseProperties, "Balanced: no extra base property.");
            Assert.AreEqual("patchwork", vest.LegendaryMechanicId, "Legendary passive stays a hook id.");
            Assert.IsNotNull(vest.AffixPool);
            Assert.AreEqual("pool_armor", vest.AffixPool.Id);

            var expected = new Dictionary<string, (AffixStat stat, int min, int max)>
            {
                ["affix_max_health"] = (AffixStat.MaxHealth, 6, 10),
                ["affix_damage_reduction"] = (AffixStat.DamageReduction, 2, 5),
                ["affix_movement_speed"] = (AffixStat.MovementSpeed, 5, 9),
                ["affix_healing_received"] = (AffixStat.HealingReceived, 8, 14),
                ["affix_dash_cooldown_reduction"] = (AffixStat.DashCooldownReduction, 6, 10),
                ["affix_knockback_resistance"] = (AffixStat.KnockbackResistance, 8, 15),
                ["affix_stagger_resistance"] = (AffixStat.StaggerResistance, 8, 15)
            };
            CollectionAssert.AreEquivalent(expected.Keys, vest.AffixPool.Affixes.Select(a => a.Id));
            foreach (var affix in vest.AffixPool.Affixes)
            {
                Assert.AreEqual(expected[affix.Id].stat, affix.Stat, affix.Id);
                Assert.AreEqual(expected[affix.Id].min, affix.MinValue, affix.Id);
                Assert.AreEqual(expected[affix.Id].max, affix.MaxValue, affix.Id);
            }

            CollectionAssert.AreEquivalent(
                new[] { StatModifier.Flat(StatId.MaxHealth, 20), StatModifier.Percent(StatId.GeneralDamageReduction, 4) },
                vest.BaseModifiers());
        }

        // ---- Acceptance 1 + 2 ----

        [Test]
        public void EquipUnequip_GrantsAndRemovesExactly20HpAnd4Dr_WithoutStacking()
        {
            var vest = new ItemInstance("armor_scrap_vest");
            Assert.AreEqual(100, _stats.MaxHealth);

            Assert.IsTrue(_inventory.TryEquip(vest, EquippedSlot.Armor));
            Assert.AreEqual(120, _stats.MaxHealth);
            Assert.AreEqual(4, _stats.GetPercent(StatId.GeneralDamageReduction));
            Assert.AreEqual(1, _registrar.RegisteredSources.Count);

            Assert.AreSame(vest, _inventory.Unequip(EquippedSlot.Armor));
            Assert.AreEqual(100, _stats.MaxHealth);
            Assert.AreEqual(0, _stats.GetPercent(StatId.GeneralDamageReduction));
            Assert.AreEqual(0, _registrar.RegisteredSources.Count);

            for (var i = 0; i < 5; i++)
            {
                Assert.IsTrue(_inventory.TryEquip(vest, EquippedSlot.Armor));
                Assert.IsFalse(_inventory.TryEquip(vest, EquippedSlot.Armor), "Already equipped.");
                Assert.AreEqual(120, _stats.MaxHealth, $"iteration {i}");
                Assert.AreEqual(4, _stats.GetPercent(StatId.GeneralDamageReduction));
                Assert.AreEqual(1, _stats.SourceIds.Count, "Exactly one source while equipped.");
                _inventory.Unequip(EquippedSlot.Armor);
            }

            Assert.AreEqual(100, _stats.MaxHealth);
            Assert.AreEqual(0, _stats.SourceIds.Count);
        }

        [Test]
        public void EquipWithAffixes_AddsBaseAndRolledAffixesAsOneSource()
        {
            var maxHp = ScriptableObject.CreateInstance<AffixDefinition>();
            _created.Add(maxHp);
            Set(maxHp, "_id", "affix_max_health");
            Set(maxHp, "_stat", AffixStat.MaxHealth);
            var registrar = new LoadoutStatRegistrar(_inventory, _stats, id => _registry.TryGet(id, out var d) ? d : null, id => id == "affix_max_health" ? maxHp : null);
            _registrar.Dispose();
            _registrar = registrar;

            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Uncommon);
            vest.AddAffixRoll(new AffixRoll("affix_max_health", 10));
            _inventory.TryEquip(vest, EquippedSlot.Armor);

            Assert.AreEqual(132, _stats.MaxHealth, "(100 + 20) × 1.10");
            Assert.AreEqual(1, _stats.SourceIds.Count);
            _inventory.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(100, _stats.MaxHealth);
        }

        // ---- Acceptance 3 ----

        [Test]
        public void Armor_OccupiesOnlyTheArmorSlot()
        {
            var vest = new ItemInstance("armor_scrap_vest");
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                if (slot == EquippedSlot.Armor) continue;
                Assert.IsFalse(_inventory.TryEquip(vest, slot), $"Armor must not fit {slot}.");
            }

            Assert.IsTrue(_inventory.TryEquip(vest, EquippedSlot.Armor));
            Assert.IsFalse(_inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor), "One armor slot.");
            Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest")), "Spare armor sits in the backpack without stats.");
            Assert.AreEqual(120, _stats.MaxHealth, "Backpack armor contributes nothing.");
        }

        // ---- Acceptance 4 ----

        [Test]
        public void DamageReduction_FlowsThroughTheCentralCappedCalculation()
        {
            _inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor);
            Assert.AreEqual(96, _stats.ApplyDamageReduction(100));
            Assert.AreEqual(29, _stats.ApplyDamageReduction(30), "30 × 0.96 = 28.8 → 29");

            _stats.SetSource(new StatModifierSource("skills", StatModifier.Percent(StatId.GeneralDamageReduction, 50)));
            Assert.AreEqual(40, _stats.GetPercent(StatId.GeneralDamageReduction), "Armor + skills clamp at the 40% cap.");
            Assert.AreEqual(60, _stats.ApplyDamageReduction(100));
        }
    }
}
