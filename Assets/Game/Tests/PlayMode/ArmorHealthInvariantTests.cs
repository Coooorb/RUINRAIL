using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Health invariant for max-HP changes: equipping armor raises the maximum but never grants current HP; unequipping
    /// lowers the maximum and clamps current HP (never below 1 while alive). Repeated re-equips therefore cannot heal.
    /// </summary>
    public class ArmorHealthInvariantTests
    {
        private readonly List<Object> _created = new();
        private HealthComponent _health;
        private PlayerInventory _inventory;
        private PlayerStatsBinder _binder;
        private LoadoutStatRegistrar _registrar;

        [SetUp]
        public void SetUp()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();
            _created.Add(config);
            var vest = ScriptableObject.CreateInstance<ArmorDefinition>();
            _created.Add(vest);
            Set(vest, "_id", "armor_scrap_vest");
            Set(vest, "_category", ItemCategory.Armor);
            Set(vest, "_baseMaxHealth", 20);
            Set(vest, "_baseDamageReductionPercent", 4);
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { vest });
            _inventory = PlayerInventory.FromRegistry(registry, null);

            var player = new GameObject("Player");
            _created.Add(player);
            _health = player.AddComponent<HealthComponent>();
            _health.SetMaxHealth(100);
            _binder = player.AddComponent<PlayerStatsBinder>();
            _binder.Configure(caps, config);
            _registrar = new LoadoutStatRegistrar(_inventory, _binder.Stats, id => registry.TryGet(id, out var d) ? d : null, _ => null);
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
        public void Equip_RaisesMaxWithoutHealing_UnequipClampsCurrent_ReequipNeverHeals()
        {
            _health.TryApplyDamage(new DamageRequest(40));
            Assert.AreEqual(60, _health.CurrentHealth);

            var vest = new ItemInstance("armor_scrap_vest");
            _inventory.TryEquip(vest, EquippedSlot.Armor);
            Assert.AreEqual(120, _health.MaxHealth);
            Assert.AreEqual(60, _health.CurrentHealth, "Equipping never grants current HP.");

            _health.Heal(1000);
            Assert.AreEqual(120, _health.CurrentHealth);
            _inventory.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(100, _health.MaxHealth);
            Assert.AreEqual(100, _health.CurrentHealth, "Unequip clamps current HP to the new maximum.");

            for (var i = 0; i < 10; i++)
            {
                _inventory.TryEquip(vest, EquippedSlot.Armor);
                _inventory.Unequip(EquippedSlot.Armor);
            }

            Assert.AreEqual(100, _health.MaxHealth);
            Assert.AreEqual(100, _health.CurrentHealth, "Re-equip cycles duplicate nothing.");

            _health.TryApplyDamage(new DamageRequest(99));
            Assert.AreEqual(1, _health.CurrentHealth);
            _inventory.TryEquip(vest, EquippedSlot.Armor);
            _inventory.Unequip(EquippedSlot.Armor);
            Assert.AreEqual(1, _health.CurrentHealth, "Clamp never kills or heals.");
            Assert.IsTrue(_health.IsAlive);
        }

        [Test]
        public void IncomingDamage_UsesArmorDr_ThroughTheBinder()
        {
            _inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor);
            Assert.AreEqual(120, _health.MaxHealth);
            Assert.AreEqual(100, _health.CurrentHealth);

            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(50)));
            Assert.AreEqual(52, _health.CurrentHealth, "50 × 0.96 = 48 applied.");
        }
    }
}
