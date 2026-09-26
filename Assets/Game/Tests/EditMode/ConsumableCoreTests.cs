using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ConsumableCoreTests
    {
        private readonly List<Object> _created = new();
        private GlobalStatCapsConfig _caps;
        private PlayerStats _stats;
        private PlayerCombatEvents _events;
        private ItemDefinitionRegistry _registry;
        private PlayerInventory _inventory;
        private ConsumableEffectRunner _effects;
        private ConsumableUseAction _use;
        private int _current;
        private int _max;

        private static readonly Dictionary<string, (string name, Rarity rarity, int stack, float useTime, ConsumableEffectKind kind, int heal, StatId stat, int pct, float duration, bool injector)> Catalog = new()
        {
            ["consumable_bandage"] = ("Bandage", Rarity.Common, 5, 1.5f, ConsumableEffectKind.Heal, 25, StatId.MaxHealth, 0, 0f, false),
            ["consumable_medkit"] = ("Medkit", Rarity.Uncommon, 3, 3.0f, ConsumableEffectKind.Heal, 60, StatId.MaxHealth, 0, 0f, false),
            ["consumable_combat_stim"] = ("Combat Stim", Rarity.Uncommon, 3, 0.5f, ConsumableEffectKind.TimedBuff, 0, StatId.MovementSpeed, 20, 8f, false),
            ["consumable_damage_stim"] = ("Damage Stim", Rarity.Rare, 2, 0.5f, ConsumableEffectKind.TimedBuff, 0, StatId.WeaponDamage, 20, 10f, false),
            ["consumable_armor_injector"] = ("Armor Injector", Rarity.Rare, 2, 0.5f, ConsumableEffectKind.TimedBuff, 0, StatId.GeneralDamageReduction, 20, 10f, true)
        };

        [SetUp]
        public void SetUp()
        {
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(_caps);
            _stats = new PlayerStats(_caps, 100);
            _events = new PlayerCombatEvents();
            _current = 40;
            _max = 100;

            var definitions = AssetDatabase.FindAssets("t:ConsumableDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ConsumableDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Cast<ItemDefinition>()
                .ToList();
            _registry = ItemDefinitionRegistry.Build(definitions);
            _inventory = PlayerInventory.FromRegistry(_registry, null);
            _effects = new ConsumableEffectRunner(new ConsumableTargets(_stats, _events, amount =>
            {
                var before = _current;
                _current = Mathf.Min(_max, _current + amount);
                return _current - before;
            }, null, null, () => _current < _max));
            _use = new ConsumableUseAction(_inventory, id => _registry.TryGet(id, out var d) ? d : null, _effects);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ItemInstance Equip(string id, int quantity)
        {
            var stack = new ItemInstance(id, quantity);
            Assert.IsTrue(_inventory.TryEquip(stack, EquippedSlot.ActiveConsumable));
            return stack;
        }

        // ---- Acceptance 1 + 5: catalog values ----

        [Test]
        public void FiveConsumables_MatchCatalog_AndCarryNoAffixOrRarityVariants()
        {
            var assets = _registry.Definitions.OfType<ConsumableDefinition>().Where(c => Catalog.ContainsKey(c.Id)).ToList();
            CollectionAssert.AreEquivalent(Catalog.Keys, assets.Select(a => a.Id));
            foreach (var c in assets)
            {
                var e = Catalog[c.Id];
                Assert.AreEqual(e.name, c.DisplayName, c.Id);
                Assert.AreEqual(ItemCategory.Consumable, c.Category, c.Id);
                Assert.IsTrue(c.IsStackable, c.Id);
                Assert.AreEqual(e.stack, c.MaxStack, c.Id);
                Assert.AreEqual(e.rarity, c.FixedRarity, c.Id);
                Assert.AreEqual(e.useTime, c.UseTimeSeconds, 0.0001f, c.Id);
                Assert.AreEqual(e.kind, c.EffectKind, c.Id);
                Assert.AreEqual(e.heal, c.HealAmount, c.Id);
                if (e.kind == ConsumableEffectKind.TimedBuff)
                {
                    Assert.AreEqual(e.stat, c.BuffStat, c.Id);
                    Assert.AreEqual(e.pct, c.BuffPercent, c.Id);
                    Assert.AreEqual(e.duration, c.BuffDurationSeconds, 0.0001f, c.Id);
                }

                Assert.AreEqual(e.injector, c.ActivatesArmorInjectorCap, c.Id);
                Assert.AreEqual(ConsumptionPoint.OnCompletion, c.ConsumptionPoint, c.Id);
                Assert.IsFalse(c is EquipmentItemDefinition, "Consumables are not equipment: no affix pool, no Legendary mechanic.");
            }

            Assert.IsFalse(RarityRules.CanHaveAffixes(ItemCategory.Consumable));
        }

        // ---- Acceptance 2: healing ----

        [Test]
        public void Bandage_Heals25After1Point5Seconds_ClampedAtMax_WithHealingReceivedCap()
        {
            var stack = Equip("consumable_bandage", 3);
            Assert.AreEqual(ConsumableUseResult.Started, _use.TryUse());
            Assert.IsTrue(_use.IsUsing);
            _use.Tick(1.4f);
            Assert.AreEqual(40, _current, "Nothing before the channel completes.");
            Assert.AreEqual(3, stack.Quantity);
            _use.Tick(0.2f);
            Assert.AreEqual(65, _current);
            Assert.AreEqual(2, stack.Quantity, "Exactly one unit consumed on completion.");
            Assert.IsFalse(_use.IsUsing);

            _stats.SetSource(new StatModifierSource("gear", StatModifier.Percent(StatId.HealingReceived, 80)));
            Assert.AreEqual(50, _stats.GetPercent(StatId.HealingReceived), "Healing Received capped at +50%.");
            _current = 40;
            _use.TryUse();
            _use.Tick(1.5f);
            Assert.AreEqual(40 + 38, _current, "25 × 1.5 = 37.5 → 38 (rounded).");

            _current = 95;
            _use.TryUse();
            _use.Tick(1.5f);
            Assert.AreEqual(100, _current, "Clamped at Max HP.");
            Assert.AreEqual(0, stack.Quantity);
            Assert.IsNull(_inventory.GetEquipped(EquippedSlot.ActiveConsumable), "Empty stack leaves the slot.");
        }

        [Test]
        public void Medkit_Heals60After3Seconds_AndReactiveHubBonusesApply()
        {
            Equip("consumable_medkit", 1);
            _events.HealingConsumableUsed += r => r.BonusPercent += 25; // e.g. Emergency Care
            _use.TryUse();
            _use.Tick(2.9f);
            Assert.AreEqual(40, _current);
            _use.Tick(0.2f);
            Assert.AreEqual(100, _current, "60 × 1.25 = 75 → clamped from 40 to 100.");
        }

        // ---- Acceptance 3: timed buffs ----

        [Test]
        public void Stims_ApplyExactPercentForExactDuration_RemovedOnce_RefreshNotStack()
        {
            var expired = new List<string>();
            _effects.BuffExpired += id => expired.Add(id);
            Equip("consumable_combat_stim", 3);

            _use.TryUse();
            _use.Tick(0.5f);
            Assert.AreEqual(20, _stats.GetPercent(StatId.MovementSpeed));
            _use.Tick(7.9f);
            Assert.AreEqual(20, _stats.GetPercent(StatId.MovementSpeed));
            _use.Tick(0.2f);
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed), "Expired at 8s.");
            CollectionAssert.AreEqual(new[] { "consumable_combat_stim" }, expired);
            Assert.AreEqual(0, _stats.SourceIds.Count);

            _use.TryUse();
            _use.Tick(0.5f);
            _use.Tick(4f);
            _use.TryUse();
            _use.Tick(0.5f);
            Assert.AreEqual(20, _stats.GetPercent(StatId.MovementSpeed), "Re-use refreshes; never 40%.");
            Assert.AreEqual(8f, _effects.RemainingSeconds("consumable_combat_stim"), 0.001f);
            Assert.AreEqual(1, _stats.SourceIds.Count);
            _use.Tick(8.1f);
            Assert.AreEqual(2, expired.Count(id => id == "consumable_combat_stim"), "Two lifetimes (the refreshed one counts once), two expiries.");
            Assert.AreEqual(0, _stats.GetPercent(StatId.MovementSpeed));
        }

        [Test]
        public void DamageStim_And_ArmorInjector_UseCentralCaps_AndTheTemporaryDrCap()
        {
            Equip("consumable_damage_stim", 1);
            _stats.SetSource(new StatModifierSource("affixes", StatModifier.Percent(StatId.WeaponDamage, 40)));
            _use.TryUse();
            _use.Tick(0.5f);
            Assert.AreEqual(50, _stats.GetPercent(StatId.WeaponDamage), "40 + 20 clamps at +50%.");

            _inventory.Unequip(EquippedSlot.ActiveConsumable);
            Equip("consumable_armor_injector", 2);
            _stats.SetSource(new StatModifierSource("armor", StatModifier.Percent(StatId.GeneralDamageReduction, 35)));
            Assert.AreEqual(35, _stats.GetPercent(StatId.GeneralDamageReduction));
            _use.TryUse();
            _use.Tick(0.5f);
            Assert.IsTrue(_stats.ArmorInjectorActive);
            Assert.AreEqual(50, _stats.GetPercent(StatId.GeneralDamageReduction), "35 + 20 = 55 clamps at the temporary 50% cap.");
            _use.Tick(10.1f);
            Assert.IsFalse(_stats.ArmorInjectorActive);
            Assert.AreEqual(35, _stats.GetPercent(StatId.GeneralDamageReduction), "Back to the normal cap with the injector gone.");
        }

        // ---- Acceptance 4: failed / interrupted use ----

        [Test]
        public void CancelledUse_ConsumesNothing_AndCannotUnderflow()
        {
            var stack = Equip("consumable_medkit", 1);
            _use.TryUse();
            _use.Tick(2f);
            Assert.IsTrue(_use.Cancel());
            Assert.IsFalse(_use.IsUsing);
            Assert.AreEqual(1, stack.Quantity, "Interrupted channel costs nothing.");
            Assert.AreEqual(40, _current);
            Assert.IsFalse(_use.Cancel(), "Nothing to cancel.");

            Assert.AreEqual(ConsumableUseResult.Started, _use.TryUse());
            Assert.AreEqual(ConsumableUseResult.AlreadyUsing, _use.TryUse(), "Spamming the input never starts a second channel.");
            _use.Tick(3f);
            Assert.AreEqual(0, stack.Quantity);
            Assert.AreEqual(ConsumableUseResult.NothingEquipped, _use.TryUse());
            Assert.AreEqual(0, stack.Quantity, "Never below zero.");

            var empty = new ItemInstance("consumable_bandage", 1);
            _inventory.TryEquip(empty, EquippedSlot.ActiveConsumable);
            empty.SetQuantity(0);
            Assert.AreEqual(ConsumableUseResult.EmptyStack, _use.TryUse());
            Assert.IsNull(_inventory.GetEquipped(EquippedSlot.ActiveConsumable));
        }

        [Test]
        public void HealingAtFullHp_IsRejectedBeforeStart_NothingSpentOrRaised_AndWorksOnceHpIsMissing()
        {
            var started = 0;
            var healed = 0;
            var healingHooks = 0;
            _use.UseStarted += _ => started++;
            _effects.Healed += (_, _) => healed++;
            _events.HealingConsumableUsed += _ => healingHooks++;
            var healers = _registry.Definitions.OfType<ConsumableDefinition>().Where(d => d.EffectKind == ConsumableEffectKind.Heal).Select(d => d.Id).ToList();
            CollectionAssert.IsSupersetOf(healers, new[] { "consumable_bandage", "consumable_medkit" }, "every pure heal in the catalog is covered by kind, not by name");

            foreach (var id in healers)
            {
                _inventory.Unequip(EquippedSlot.ActiveConsumable);
                var stack = Equip(id, 2);
                _current = _max;
                Assert.AreEqual(ConsumableUseResult.NoEffect, _use.TryUse(), $"{id}: full HP");
                Assert.IsFalse(_use.IsUsing, $"{id}: no channel");
                Assert.AreEqual(0f, _use.RemainingSeconds);
                _use.Tick(5f);
                Assert.AreEqual(2, stack.Quantity, $"{id}: nothing spent");
                Assert.AreEqual(0, started + healed + healingHooks, $"{id}: no start, heal or reactive hook");

                _current = _max - 1;
                Assert.AreEqual(ConsumableUseResult.Started, _use.TryUse(), $"{id}: missing HP");
                _use.Tick(5f);
                Assert.AreEqual(_max, _current);
                Assert.AreEqual(1, stack.Quantity, $"{id}: one unit spent for a real heal");
                started = healed = healingHooks = 0;
            }

            // Every other kind stays usable at full HP: buffs refresh, grenades are thrown, revives spend only on success.
            _current = _max;
            foreach (var definition in _registry.Definitions.OfType<ConsumableDefinition>().Where(d => d.EffectKind != ConsumableEffectKind.Heal))
                Assert.IsTrue(_effects.WouldHaveEffect(definition), $"{definition.Id} is not blocked at full HP");
            _inventory.Unequip(EquippedSlot.ActiveConsumable);
            var stim = Equip("consumable_combat_stim", 1);
            Assert.AreEqual(ConsumableUseResult.Started, _use.TryUse());
            _use.Tick(0.5f);
            Assert.AreEqual(0, stim.Quantity, "a stim at full HP is used as before");
        }

        [Test]
        public void OnlyTheActiveSlotIsUsed_BackpackStacksStayUntouched()
        {
            var active = Equip("consumable_bandage", 2);
            _inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 5));
            _use.TryUse();
            _use.Tick(1.5f);
            Assert.AreEqual(1, active.Quantity);
            Assert.AreEqual(5, _inventory.CountOf("consumable_bandage"), "Backpack stack untouched.");
            Assert.IsNull(typeof(ConsumableUseAction).GetMethod("UseGrenade"), "No item-specific use paths: every consumable goes through the same channel.");
            Assert.IsFalse(_effects.CanRequestRevives, "Without a revive responder (Solo) revive items stay unusable.");
        }
    }
}
