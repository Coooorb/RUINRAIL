using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>Exact magnitudes, durations, cooldowns and room-boundary resets of the nine Legendary passives.</summary>
    public class ArmorPassiveTests
    {
        private GlobalStatCapsConfig _caps;
        private PlayerStats _stats;
        private PlayerCombatEvents _events;
        private int _current;
        private int _max;
        private int _healedTotal;
        private int _dashResets;
        private PassiveContext _context;

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _stats = new PlayerStats(_caps, 100);
            _events = new PlayerCombatEvents();
            _current = 100;
            _max = 100;
            _healedTotal = 0;
            _dashResets = 0;
            _context = new PassiveContext(_stats, _events, () => _current, () => _max,
                amount => { _healedTotal += amount; _current = Mathf.Min(_max, _current + amount); return amount > 0; },
                () => _dashResets++);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_caps);
        }

        private void SetHealth(int current, int max)
        {
            _current = current;
            _max = max;
            _events.RaiseHealthChanged(current, max);
        }

        [Test]
        public void Patchwork_Heals6PercentOfMaxHp_OnCombatRoomClear_Only()
        {
            var passive = new PatchworkPassive();
            passive.Attach(_context);
            SetHealth(50, 150);
            _events.RaiseCombatRoomEntered();
            Assert.AreEqual(0, _healedTotal);
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(9, _healedTotal, "6% of 150 = 9");
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(18, _healedTotal, "Every cleared Combat Room heals once.");
            passive.Detach();
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(18, _healedTotal, "Detached passive is silent.");
        }

        [Test]
        public void Momentum_Grants12PercentMoveForExactlyOneSecondAfterDash()
        {
            var passive = new MomentumPassive();
            passive.Attach(_context);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed));
            _events.RaiseDashed();
            Assert.AreEqual(12, _stats.GetPercent(StatId.MovementSpeed));
            passive.Tick(0.9f);
            Assert.AreEqual(12, _stats.GetPercent(StatId.MovementSpeed));
            passive.Tick(0.2f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Expired at 1s.");
            _events.RaiseDashed();
            passive.Tick(0.5f);
            _events.RaiseDashed();
            passive.Tick(0.8f);
            Assert.AreEqual(12, _stats.GetPercent(StatId.MovementSpeed), "Re-dash refreshes the single buff (never stacks).");
            Assert.AreEqual(1, _stats.SourceIds.Count);
            passive.Detach();
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Detach clears the temporary source.");
        }

        [Test]
        public void Anchored_NegatesOneStaggerEvery8Seconds()
        {
            var passive = new AnchoredPassive();
            passive.Attach(_context);
            Assert.IsTrue(_events.RaiseStaggerIncoming().IsNegated);
            Assert.IsFalse(_events.RaiseStaggerIncoming().IsNegated, "Second stagger inside the cooldown lands.");
            passive.Tick(7.9f);
            Assert.IsFalse(_events.RaiseStaggerIncoming().IsNegated);
            passive.Tick(0.2f);
            var negated = _events.RaiseStaggerIncoming();
            Assert.IsTrue(negated.IsNegated);
            Assert.AreEqual("anchored", negated.NegatedBy);
        }

        [Test]
        public void LastStand_Adds15PercentDr_OnlyBelow25PercentHp_SubjectToCap()
        {
            var passive = new LastStandPassive();
            passive.Attach(_context);
            Assert.IsFalse(passive.IsActive);
            SetHealth(25, 100);
            Assert.IsFalse(passive.IsActive, "Exactly 25% is not below 25%.");
            SetHealth(24, 100);
            Assert.IsTrue(passive.IsActive);
            Assert.AreEqual(15, _stats.GetPercent(StatId.GeneralDamageReduction));
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.GeneralDamageReduction, 30)));
            Assert.AreEqual(40, _stats.GetPercent(StatId.GeneralDamageReduction), "30 + 15 clamps at 40.");
            _stats.ArmorInjectorActive = true;
            Assert.AreEqual(45, _stats.GetPercent(StatId.GeneralDamageReduction), "Temporary cap 50 lets the full 45 through.");
            SetHealth(60, 100);
            Assert.IsFalse(passive.IsActive);
            Assert.AreEqual(30, _stats.GetPercent(StatId.GeneralDamageReduction));
        }

        [Test]
        public void ShockAbsorber_IgnoresExplosionKnockback_NotStagger()
        {
            var passive = new ShockAbsorberPassive();
            passive.Attach(_context);
            Assert.IsTrue(_events.RaiseExplosionKnockbackIncoming().IsNegated);
            Assert.IsTrue(_events.RaiseExplosionKnockbackIncoming().IsNegated, "No cooldown: every explosion knockback is ignored.");
            Assert.IsFalse(_events.RaiseStaggerIncoming().IsNegated);
            passive.Detach();
            Assert.IsFalse(_events.RaiseExplosionKnockbackIncoming().IsNegated);
        }

        [Test]
        public void EmergencyCare_Boosts25Percent_OnlyFirstHealingConsumablePerCombatRoom()
        {
            var passive = new EmergencyCarePassive();
            passive.Attach(_context);
            Assert.AreEqual(40, _events.RaiseHealingConsumableUsed(40).FinalAmount, "Outside a Combat Room: no bonus.");
            _events.RaiseCombatRoomEntered();
            Assert.AreEqual(50, _events.RaiseHealingConsumableUsed(40).FinalAmount, "First heal in room: +25%.");
            Assert.AreEqual(40, _events.RaiseHealingConsumableUsed(40).FinalAmount, "Second heal in the same room: none.");
            _events.RaiseCombatRoomCleared();
            _events.RaiseCombatRoomEntered();
            Assert.AreEqual(50, _events.RaiseHealingConsumableUsed(40).FinalAmount, "Resets on the next Combat Room.");
        }

        [Test]
        public void Adrenaline_Plus10Move_For3Seconds_WithInternalCooldown5()
        {
            var passive = new AdrenalinePassive();
            passive.Attach(_context);
            _events.RaiseEnemyKilled();
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed));
            passive.Tick(2.9f);
            _events.RaiseEnemyKilled();
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed));
            passive.Tick(0.2f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Kill during ICD did not refresh; buff expired at 3s.");
            passive.Tick(1.8f);
            _events.RaiseEnemyKilled();
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "4.9s after the trigger the 5s ICD still blocks.");
            passive.Tick(0.2f);
            _events.RaiseEnemyKilled();
            Assert.AreEqual(10, _stats.GetPercent(StatId.MovementSpeed), "ICD elapsed.");
        }

        [Test]
        public void ExoLock_TriggersOnHitOf20PlusFinalDamage_For5Seconds_Cooldown8()
        {
            var passive = new ExoLockPassive();
            passive.Attach(_context);
            _events.RaiseDamageTaken(19);
            Assert.AreEqual(0, _stats.GetPercent(StatId.StaggerResistance));
            _events.RaiseDamageTaken(20);
            Assert.AreEqual(30, _stats.GetPercent(StatId.StaggerResistance));
            Assert.AreEqual(30, _stats.GetPercent(StatId.KnockbackResistance));
            passive.Tick(4.9f);
            Assert.IsTrue(passive.IsBuffActive);
            passive.Tick(0.2f);
            Assert.IsFalse(passive.IsBuffActive);
            Assert.AreEqual(0, _stats.GetPercent(StatId.StaggerResistance));
            _events.RaiseDamageTaken(50);
            Assert.AreEqual(0, _stats.GetPercent(StatId.StaggerResistance), "5.1s after the trigger the 8s cooldown blocks.");
            passive.Tick(3f);
            _events.RaiseDamageTaken(25);
            Assert.AreEqual(30, _stats.GetPercent(StatId.StaggerResistance));
            _stats.SetSource(new StatModifierSource("exo", StatModifier.Percent(StatId.StaggerResistance, 25)));
            Assert.AreEqual(50, _stats.GetPercent(StatId.StaggerResistance), "25 base + 30 clamps at the 50% cap.");
        }

        [Test]
        public void SecondWind_ResetsDashCooldown_OnFirstDropBelow30Percent_OncePerCombatRoom()
        {
            var passive = new SecondWindPassive();
            passive.Attach(_context);
            SetHealth(20, 100);
            Assert.AreEqual(0, _dashResets, "Outside a Combat Room nothing triggers.");
            SetHealth(80, 100);
            _events.RaiseCombatRoomEntered();
            SetHealth(30, 100);
            Assert.AreEqual(0, _dashResets, "Exactly 30% is not below.");
            SetHealth(29, 100);
            Assert.AreEqual(1, _dashResets);
            SetHealth(10, 100);
            SetHealth(50, 100);
            SetHealth(5, 100);
            Assert.AreEqual(1, _dashResets, "Once per Combat Room.");
            _events.RaiseCombatRoomCleared();
            _events.RaiseCombatRoomEntered();
            SetHealth(60, 100);
            SetHealth(10, 100);
            Assert.AreEqual(2, _dashResets, "Fresh room, fresh trigger.");
        }

        // ---- Registrar: passive follows the Legendary armor instance ----

        [Test]
        public void Registrar_AttachesPassiveOnlyForLegendaryArmor_AndDetachesOnUnequip()
        {
            var vest = ScriptableObject.CreateInstance<ArmorDefinition>();
            Set(vest, "_id", "armor_scrap_vest");
            Set(vest, "_category", ItemCategory.Armor);
            Set(vest, "_legendaryMechanicId", "patchwork");
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { vest });
            var inventory = PlayerInventory.FromRegistry(registry, null);
            using var registrar = new EquipmentPassiveRegistrar(inventory, id => registry.TryGet(id, out var d) ? d : null, _context);

            inventory.TryEquip(new ItemInstance("armor_scrap_vest", 1, Rarity.Epic), EquippedSlot.Armor);
            Assert.IsNull(registrar.ArmorPassive, "Epic armor has no passive.");
            inventory.Unequip(EquippedSlot.Armor);

            var legendary = new ItemInstance("armor_scrap_vest", 1, Rarity.Legendary);
            inventory.TryEquip(legendary, EquippedSlot.Armor);
            Assert.IsInstanceOf<PatchworkPassive>(registrar.ArmorPassive);
            SetHealth(50, 100);
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(6, _healedTotal);

            inventory.Unequip(EquippedSlot.Armor);
            Assert.IsNull(registrar.ArmorPassive);
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(6, _healedTotal, "Detached: no further heals.");

            for (var i = 0; i < 3; i++)
            {
                inventory.TryEquip(legendary, EquippedSlot.Armor);
                inventory.Unequip(EquippedSlot.Armor);
            }

            inventory.TryEquip(legendary, EquippedSlot.Armor);
            _events.RaiseCombatRoomCleared();
            Assert.AreEqual(12, _healedTotal, "Exactly one attached passive after repeated cycles.");
            Object.DestroyImmediate(vest);
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
    }
}
