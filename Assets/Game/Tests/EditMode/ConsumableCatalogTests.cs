using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ConsumableCatalogTests
    {
        private static readonly Dictionary<string, (Rarity rarity, int stack, ConsumableEffectKind kind)> Catalog = new()
        {
            ["consumable_bandage"] = (Rarity.Common, 5, ConsumableEffectKind.Heal),
            ["consumable_frag_grenade"] = (Rarity.Common, 4, ConsumableEffectKind.Grenade),
            ["consumable_medkit"] = (Rarity.Uncommon, 3, ConsumableEffectKind.Heal),
            ["consumable_combat_stim"] = (Rarity.Uncommon, 3, ConsumableEffectKind.TimedBuff),
            ["consumable_smoke_grenade"] = (Rarity.Uncommon, 3, ConsumableEffectKind.Grenade),
            ["consumable_shock_grenade"] = (Rarity.Rare, 3, ConsumableEffectKind.Grenade),
            ["consumable_damage_stim"] = (Rarity.Rare, 2, ConsumableEffectKind.TimedBuff),
            ["consumable_armor_injector"] = (Rarity.Rare, 2, ConsumableEffectKind.TimedBuff),
            ["consumable_incendiary_grenade"] = (Rarity.Rare, 3, ConsumableEffectKind.Grenade),
            ["consumable_defibrillator"] = (Rarity.Legendary, 1, ConsumableEffectKind.Revive)
        };

        private static List<ConsumableDefinition> LoadAll()
        {
            return AssetDatabase.FindAssets("t:ConsumableDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ConsumableDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .ToList();
        }

        // ---- Acceptance 1 + 4 ----

        [Test]
        public void ExactlyTenConsumables_WithFixedRarityStackAndEffect_AndNoAffixData()
        {
            var all = LoadAll();
            Assert.AreEqual(10, all.Count);
            CollectionAssert.AreEquivalent(Catalog.Keys, all.Select(c => c.Id));
            foreach (var c in all)
            {
                var e = Catalog[c.Id];
                Assert.AreEqual(e.rarity, c.FixedRarity, c.Id);
                Assert.AreEqual(e.stack, c.MaxStack, c.Id);
                Assert.AreEqual(e.kind, c.EffectKind, c.Id);
                Assert.IsTrue(c.IsStackable, c.Id);
                Assert.AreEqual(ItemCategory.Consumable, c.Category, c.Id);
                Assert.IsFalse(c is EquipmentItemDefinition, c.Id);
            }

            Assert.IsNull(typeof(ConsumableDefinition).GetProperty("AffixPool"));
            Assert.IsNull(typeof(ConsumableDefinition).GetProperty("LegendaryMechanicId"));
            var registry = ItemDefinitionRegistry.Build(all.Cast<ItemDefinition>());
            Assert.IsEmpty(registry.Problems);

            var defib = all.Single(c => c.Id == "consumable_defibrillator");
            var roll = new AffixRollService().Roll(new ItemInstance(defib.Id), defib, Rarity.Legendary, new SeededRandom(1));
            Assert.AreEqual(AffixRollError.NotEquipment, roll.Error, "Consumables never roll affixes, even at Legendary.");
        }

        // ---- Acceptance 2 ----

        [Test]
        public void StackMerging_NeverExceedsEachMax()
        {
            var all = LoadAll();
            var registry = ItemDefinitionRegistry.Build(all.Cast<ItemDefinition>());
            var inventory = PlayerInventory.FromRegistry(registry, null);
            foreach (var c in all)
            {
                inventory.RestoreFromSnapshot(null);
                Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance(c.Id, c.MaxStack)), c.Id);
                Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance(c.Id, 1)), c.Id);
                var stacks = inventory.BackpackSlots.Where(s => s != null && s.DefinitionId == c.Id).ToList();
                Assert.AreEqual(2, stacks.Count, $"{c.Id}: the overflow unit opens a second stack.");
                Assert.IsTrue(stacks.All(s => s.Quantity <= c.MaxStack), c.Id);
                Assert.AreEqual(c.MaxStack + 1, inventory.CountOf(c.Id), c.Id);
                Assert.IsFalse(inventory.TryAddToBackpack(new ItemInstance(c.Id, c.MaxStack * 8)), $"{c.Id}: more than the backpack can hold is rejected atomically.");
            }
        }

        // ---- Acceptance 3 ----

        [Test]
        public void Defibrillator_IsNeverSoloLoot_ButEligibleInCoop()
        {
            var defib = LoadAll().Single(c => c.Id == "consumable_defibrillator");
            Assert.AreEqual(DropEligibility.CoopOnly, defib.DropEligibility);
            Assert.IsFalse(defib.IsDropEligible(1));
            Assert.IsTrue(defib.IsDropEligible(2));
            Assert.IsTrue(defib.IsDropEligible(3));
            Assert.AreEqual(30, defib.ReviveHealthPercent);

            var bandage = LoadAll().Single(c => c.Id == "consumable_bandage");
            var table = ScriptableObject.CreateInstance<LootTableDefinition>();
            typeof(LootTableDefinition).GetField("_rolls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(table, new[]
            {
                new LootTableDefinition.Roll
                {
                    Label = "consumable", ChancePercent = 100,
                    Entries = new[]
                    {
                        new LootTableDefinition.Entry { Item = defib, Weight = 1000 },
                        new LootTableDefinition.Entry { Item = bandage, Weight = 1 }
                    }
                }
            });
            var rarity = ScriptableObject.CreateInstance<RarityTableDefinition>();
            var roller = new LootRoller(_ => rarity);

            for (var seed = 0; seed < 200; seed++)
            {
                var solo = roller.Roll(table, LootContext.ForSource(seed, 1, 0, LootQuality.Standard, partySize: 1));
                Assert.IsTrue(solo.Items.All(i => i.DefinitionId == "consumable_bandage"), "Solo never rolls the Defibrillator, whatever its weight.");
            }

            var coopHits = 0;
            for (var seed = 0; seed < 200; seed++)
            {
                var duo = roller.Roll(table, LootContext.ForSource(seed, 1, 0, LootQuality.Standard, partySize: 2));
                if (duo.Items.Any(i => i.DefinitionId == "consumable_defibrillator")) coopHits++;
            }

            Assert.Greater(coopHits, 150, "Duo/Trio can receive it.");

            var soloOnlyTable = ScriptableObject.CreateInstance<LootTableDefinition>();
            typeof(LootTableDefinition).GetField("_rolls", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(soloOnlyTable, new[]
            {
                new LootTableDefinition.Roll { Label = "defib", ChancePercent = 100, Entries = new[] { new LootTableDefinition.Entry { Item = defib } } }
            });
            Assert.IsTrue(roller.Roll(soloOnlyTable, LootContext.ForSource(1, 1, 0, LootQuality.Standard, partySize: 1)).IsEmpty, "A roll with only ineligible entries yields nothing rather than substituting.");

            Object.DestroyImmediate(table);
            Object.DestroyImmediate(soloOnlyTable);
            Object.DestroyImmediate(rarity);
        }

        [Test]
        public void Defibrillator_IsConsumedOnlyAfterASuccessfulReviveResponse()
        {
            var all = LoadAll();
            var registry = ItemDefinitionRegistry.Build(all.Cast<ItemDefinition>());
            var inventory = PlayerInventory.FromRegistry(registry, null);
            var stack = new ItemInstance("consumable_defibrillator", 1);
            inventory.TryEquip(stack, EquippedSlot.ActiveConsumable);
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            var stats = new PlayerStats(caps, 100);

            var solo = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null, new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a)));
            Assert.AreEqual(ConsumableUseResult.UnsupportedEffect, solo.TryUse(), "No revive system (Solo): unusable, not consumed.");
            Assert.AreEqual(1, stack.Quantity);

            ReviveRequest received = null;
            var rejected = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null,
                new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a, null, r => { received = r; return false; })));
            Assert.AreEqual(ConsumableUseResult.Started, rejected.TryUse());
            Assert.IsNotNull(received);
            Assert.AreEqual("consumable_defibrillator", received.ConsumableId);
            Assert.AreEqual(30, received.HealthPercent);
            Assert.AreEqual(1, stack.Quantity, "Revive refused (no valid Dead teammate): nothing consumed.");

            var accepted = new ConsumableUseAction(inventory, id => registry.TryGet(id, out var d) ? d : null,
                new ConsumableEffectRunner(new ConsumableTargets(stats, null, a => a, null, _ => true)));
            Assert.AreEqual(ConsumableUseResult.Started, accepted.TryUse());
            Assert.AreEqual(0, stack.Quantity, "Consumed exactly once after the successful revive response.");
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.ActiveConsumable));
            Assert.AreEqual(ConsumableUseResult.NothingEquipped, accepted.TryUse());
            Object.DestroyImmediate(caps);
        }
    }
}
