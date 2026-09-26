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
    /// Health invariant for max-HP changes: a damaged player keeps their current HP when the maximum rises, a player at
    /// full health stays full against the new maximum, a lower maximum clamps current HP (never below 1 while alive), and
    /// one equipment change is one change — the old item's modifiers leaving first never costs or grants HP. Repeated
    /// swaps therefore cannot heal.
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
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { vest, Armor("armor_blast_suit", 22), Armor("armor_scout_rig", 12) });
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

        private ArmorDefinition Armor(string id, int maxHealth)
        {
            var armor = ScriptableObject.CreateInstance<ArmorDefinition>();
            _created.Add(armor);
            Set(armor, "_id", id);
            Set(armor, "_category", ItemCategory.Armor);
            Set(armor, "_baseMaxHealth", maxHealth);
            return armor;
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
            Assert.AreEqual(120, _health.CurrentHealth, "full health stays full against the raised maximum");

            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(50)));
            Assert.AreEqual(72, _health.CurrentHealth, "50 × 0.96 = 48 applied.");
        }

        /// <summary>Armor swap exactly as the inventory does it: one atomic exchange with the backpack.</summary>
        private void SwapArmor(ItemInstance incoming)
        {
            Assert.IsTrue(_inventory.TryAddToBackpack(incoming));
            var index = System.Array.IndexOf(System.Linq.Enumerable.ToArray(_inventory.BackpackSlots), incoming);
            Assert.IsTrue(_inventory.TrySwapEquippedWithBackpack(EquippedSlot.Armor, index));
            _inventory.RemoveFromBackpack(index); // the old armor leaves the test backpack so it never fills
        }

        [Test]
        public void FullHealth_ArmorSwap_StaysFullAgainstTheNewMaximum_NeverFallsBackToBase()
        {
            var a = new ItemInstance("armor_scrap_vest");
            Assert.IsTrue(_inventory.TryEquip(a, EquippedSlot.Armor));
            Assert.AreEqual(120, _health.MaxHealth);
            Assert.AreEqual(120, _health.CurrentHealth, "full at 100/100 stays full at 120/120");

            SwapArmor(new ItemInstance("armor_blast_suit"));
            Assert.AreEqual(122, _health.MaxHealth);
            Assert.AreEqual(122, _health.CurrentHealth, "120/120 -> armor with 122 Max HP -> 122/122, not the 100 base");

            // The same change done as two steps (unequip, then equip) is still one change.
            var b = _inventory.Unequip(EquippedSlot.Armor);
            Assert.IsTrue(_inventory.TryEquip(a, EquippedSlot.Armor));
            Assert.AreEqual(120, _health.CurrentHealth, "122/122 -> 120/120 through the 100 base");
            Assert.IsTrue(_inventory.TryAddToBackpack(b));
            SwapArmor(new ItemInstance("armor_scout_rig"));
            Assert.AreEqual(112, _health.MaxHealth);
            Assert.AreEqual(112, _health.CurrentHealth, "a lower maximum clamps a full player to full");
        }

        [Test]
        public void DamagedHealth_ArmorSwaps_KeepCurrentHp_ClampOnALowerMaximum_AndRepeatedSwapsNeverHeal()
        {
            Assert.IsTrue(_inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor));
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(5)));
            var damaged = _health.CurrentHealth;
            Assert.Less(damaged, 120);
            Assert.Greater(damaged, 112);

            SwapArmor(new ItemInstance("armor_blast_suit"));
            Assert.AreEqual(122, _health.MaxHealth);
            Assert.AreEqual(damaged, _health.CurrentHealth, "a higher maximum never heals a damaged player");

            SwapArmor(new ItemInstance("armor_scout_rig"));
            Assert.AreEqual(112, _health.CurrentHealth, "a lower maximum clamps");

            // Back to a higher maximum with no damage or healing since: the pre-change state, never more.
            SwapArmor(new ItemInstance("armor_scrap_vest"));
            Assert.AreEqual(damaged, _health.CurrentHealth, "the clamp was part of the same change, so nothing was lost or gained");

            string[] cycle = { "armor_blast_suit", "armor_scout_rig", "armor_scrap_vest" };
            for (var i = 0; i < 30; i++)
            {
                SwapArmor(new ItemInstance(cycle[i % 3]));
                Assert.LessOrEqual(_health.CurrentHealth, damaged, "swap " + i + " never generates HP");
            }

            SwapArmor(new ItemInstance("armor_scrap_vest"));
            Assert.AreEqual(damaged, _health.CurrentHealth);

            // Damage after a clamp is real: raising the maximum afterwards does not restore the clamped HP.
            SwapArmor(new ItemInstance("armor_scout_rig"));
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(10)));
            var afterHit = _health.CurrentHealth;
            SwapArmor(new ItemInstance("armor_blast_suit"));
            Assert.AreEqual(afterHit, _health.CurrentHealth);
        }

        [Test]
        public void OtherMaxHpSources_AttributeFlatAndAffixPercent_FollowTheSameRule()
        {
            // Vitality-style flat Max HP and an affix/passive-style percent Max HP arrive through the same stat recompute.
            _binder.Stats.SetSource(new StatModifierSource("test_vitality", StatModifier.Flat(StatId.MaxHealth, 10)));
            Assert.AreEqual(110, _health.MaxHealth);
            Assert.AreEqual(110, _health.CurrentHealth, "full stays full");
            _binder.Stats.SetSource(new StatModifierSource("test_affix", StatModifier.Percent(StatId.MaxHealth, 10)));
            Assert.AreEqual(121, _health.MaxHealth);
            Assert.AreEqual(121, _health.CurrentHealth);

            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(21)));
            Assert.AreEqual(100, _health.CurrentHealth);
            for (var i = 0; i < 10; i++)
            {
                _binder.Stats.RemoveSource("test_affix");
                _binder.Stats.SetSource(new StatModifierSource("test_affix", StatModifier.Percent(StatId.MaxHealth, 10)));
            }

            Assert.AreEqual(100, _health.CurrentHealth, "toggling a percent source never heals");
            _binder.Stats.RemoveSource("test_affix");
            _binder.Stats.RemoveSource("test_vitality");
            Assert.AreEqual(100, _health.MaxHealth);
            Assert.AreEqual(100, _health.CurrentHealth, "100 fits the 100 maximum");
        }
    }
}
