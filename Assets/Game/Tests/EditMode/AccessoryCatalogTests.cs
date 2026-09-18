using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Accessories;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class AccessoryCatalogTests
    {
        private static readonly Dictionary<string, (string name, StatModifier intrinsic, string passive)> Catalog = new()
        {
            ["accessory_runners_watch"] = ("Runner's Watch", StatModifier.Percent(StatId.MovementSpeed, 5), "accessory_momentum"),
            ["accessory_field_scope"] = ("Field Scope", StatModifier.Percent(StatId.ProjectileRange, 10), "steady_aim"),
            ["accessory_combat_bracelet"] = ("Combat Bracelet", StatModifier.Percent(StatId.MeleeAttackSpeed, 8), "flow_state"),
            ["accessory_quickdraw_holster"] = ("Quickdraw Holster", StatModifier.Percent(StatId.WeaponSwitchSpeed, 15), "hot_swap"),
            ["accessory_loaders_glove"] = ("Loader's Glove", StatModifier.Percent(StatId.ReloadSpeed, 10), "fresh_mag"),
            ["accessory_cooling_module"] = ("Cooling Module", StatModifier.Percent(StatId.BlasterCoolingRate, 15), "cold_start"),
            ["accessory_heat_sink"] = ("Heat Sink", StatModifier.Percent(StatId.BlasterHeatPerShotReduction, 10), "emergency_vent"),
            ["accessory_archers_ring"] = ("Archer's Ring", StatModifier.Percent(StatId.BowChargeSpeed, 12), "perfect_draw"),
            ["accessory_dash_capacitor"] = ("Dash Capacitor", StatModifier.Percent(StatId.DashCooldownReduction, 10), "discharge"),
            ["accessory_rangefinder"] = ("Rangefinder", StatModifier.Percent(StatId.ProjectileSpeed, 12), "long_shot"),
            ["accessory_trauma_pendant"] = ("Trauma Pendant", StatModifier.Percent(StatId.HealingReceived, 15), "second_pulse"),
            ["accessory_ammo_pouch"] = ("Ammo Pouch", StatModifier.Percent(StatId.AmmoStackCapacity, 25), "scavengers_reserve"),
            ["accessory_magnetic_coil"] = ("Magnetic Coil", StatModifier.Flat(StatId.PickupAttractionRadius, 3), "room_sweep"),
            ["accessory_stabilizer"] = ("Stabilizer", StatModifier.Percent(StatId.WeaponSpreadReduction, 15), "lock_in"),
            ["accessory_impact_module"] = ("Impact Module", StatModifier.Percent(StatId.Knockback, 15), "wallbreaker"),
            ["accessory_shock_charm"] = ("Shock Charm", StatModifier.Percent(StatId.StaggerPower, 15), "arc_stagger")
        };

        private static List<AccessoryDefinition> LoadAll()
        {
            return AssetDatabase.FindAssets("t:AccessoryDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<AccessoryDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
        }

        [Test]
        public void ExactlySixteenAccessories_WithApprovedIntrinsics_AndPassiveIds()
        {
            var accessories = LoadAll();
            Assert.AreEqual(16, accessories.Count);
            CollectionAssert.AreEquivalent(Catalog.Keys, accessories.Select(a => a.Id));
            foreach (var accessory in accessories)
            {
                var expected = Catalog[accessory.Id];
                Assert.AreEqual(expected.name, accessory.DisplayName, accessory.Id);
                Assert.AreEqual(ItemCategory.Accessory, accessory.Category, accessory.Id);
                CollectionAssert.AreEqual(new[] { expected.intrinsic }, accessory.BaseModifiers(), accessory.Id);
                Assert.AreEqual(expected.passive, accessory.LegendaryMechanicId, accessory.Id);
                Assert.IsInstanceOf<AccessoryPassive>(EquipmentPassiveFactory.Create(accessory.LegendaryMechanicId), accessory.Id);
                Assert.AreEqual(expected.passive, AccessoryPassiveFactory.Create(expected.passive).Id);
            }

            Assert.AreEqual(16, AccessoryPassiveFactory.KnownIds.Count);
            CollectionAssert.AreEquivalent(Catalog.Values.Select(v => v.passive), AccessoryPassiveFactory.KnownIds);
        }

        [Test]
        public void LegendaryAccessory_KeepsIntrinsic_RollsThreeAffixes_GetsPassive_NeverRmb()
        {
            var pool = ScriptableObject.CreateInstance<AffixPool>();
            var affixes = new[] { "a", "b", "c", "d" }.Select(id =>
            {
                var a = ScriptableObject.CreateInstance<AffixDefinition>();
                typeof(AffixDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(a, "affix_" + id);
                typeof(AffixDefinition).GetField("_stat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(a, AffixStat.MovementSpeed);
                typeof(AffixDefinition).GetField("_minValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(a, 5);
                typeof(AffixDefinition).GetField("_maxValue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(a, 9);
                return a;
            }).ToArray();
            typeof(AffixPool).GetField("_affixes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(pool, affixes);
            var watch = LoadAll().Single(a => a.Id == "accessory_runners_watch");
            var definition = Object.Instantiate(watch);
            typeof(EquipmentItemDefinition).GetField("_affixPool", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(definition, pool);

            var legendary = new ItemInstance(definition.Id);
            var roll = new AffixRollService().Roll(legendary, definition, Rarity.Legendary, new SeededRandom(2));
            Assert.IsTrue(roll.Success, roll.Error.ToString());
            Assert.AreEqual(3, legendary.AffixRolls.Count);
            Assert.AreEqual("accessory_momentum", AffixRollService.GetLegendaryMechanicId(legendary, definition));
            CollectionAssert.AreEqual(new[] { StatModifier.Percent(StatId.MovementSpeed, 5) }, definition.BaseModifiers(), "Intrinsic unchanged at Legendary.");
            Assert.IsNull(AffixRollService.GetLegendaryMechanicId(new ItemInstance(definition.Id, 1, Rarity.Epic), definition));
            Assert.IsFalse(typeof(AccessoryDefinition).GetProperties().Any(p => p.Name.ToLowerInvariant().Contains("special")));
            Assert.IsFalse(typeof(AccessoryPassive).GetMethods().Any(m => m.Name.Contains("Rmb") || m.Name.Contains("Activate")));

            Object.DestroyImmediate(definition);
            Object.DestroyImmediate(pool);
            foreach (var a in affixes) Object.DestroyImmediate(a);
        }

        [Test]
        public void PassiveIds_AreUniqueAcrossArmorAndAccessoryCatalogs()
        {
            var armorIds = RuinRail.Gameplay.Items.Armor.ArmorPassiveFactory.KnownIds;
            var accessoryIds = AccessoryPassiveFactory.KnownIds;
            Assert.IsEmpty(armorIds.Intersect(accessoryIds), "Scout Rig Momentum (12%/1s) and Runner's Watch Momentum (10%/3s) must stay distinct.");
        }
    }
}
