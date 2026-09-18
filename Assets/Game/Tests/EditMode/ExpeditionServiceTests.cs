using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ExpeditionServiceTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[]
            {
                Def<TestItemDefinition>("weapon_p9_ranger", ItemCategory.Weapon),
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
                Ammo("ammo_light", AmmoType.Light)
            });
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

        private ExpeditionService NewService(ITransitResolutionPolicy policy = null)
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(
                id => _registry.TryGet(id, out var d) ? d : null,
                t => ammoByType.TryGetValue(t, out var a) ? a : null,
                _ammoBalance,
                policy);
        }

        private static PlayerProfile ProfileWithLoadout()
        {
            var snapshot = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
            };
            return new PlayerProfile { BankedCoins = 100, TotalXp = 500, SafeLoadout = snapshot };
        }

        // ---- Start transaction ----

        [Test]
        public void Start_MovesSafeLoadoutIntoAtRiskCarriedState_AndRemovesTheSafeCopy()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var pistolId = profile.SafeLoadout.Equipped[0].Item.InstanceId;

            var state = service.Start(profile, 42, Biome.RuinedMetro);

            Assert.IsNull(profile.SafeLoadout, "Anti-exploit: no pre-run snapshot remains on the profile.");
            Assert.AreEqual(pistolId, state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId);
            Assert.IsTrue(state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).IsAtRisk);
            Assert.IsTrue(state.Inventory.BackpackSlots[0].IsAtRisk);
            Assert.AreEqual(40, state.Inventory.Get(AmmoType.Light));
            Assert.AreEqual(1, state.Depth);
            Assert.AreEqual(Biome.RuinedMetro, state.Biome);
            Assert.AreEqual(0, state.CarriedCoins);
            Assert.AreEqual(100, profile.BankedCoins, "Start never touches banked coins.");
            Assert.Throws<System.InvalidOperationException>(() => service.Start(profile, 1, Biome.Rustworks));
        }

        // ---- Acceptance 1: Return secures exactly once ----

        [Test]
        public void Return_SecuresCarriedItemsAndCoinsExactlyOnce_AndEndsTheExpedition()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var state = service.Start(profile, 42, Biome.RuinedMetro);
            var found = new ItemInstance("armor_scrap_vest") { IsAtRisk = true };
            state.Inventory.TryAddToBackpack(found);
            service.AddCarriedCoins(250);
            service.RecordEnemyDefeated(12);
            var ended = 0;
            service.ExpeditionEnded += _ => ended++;

            service.RecordBossDefeated(650);
            Assert.IsTrue(service.ChooseTransit(TransitChoice.ReturnToShelter));

            Assert.IsFalse(service.IsExpeditionActive);
            Assert.AreEqual(ExpeditionOutcome.Extracted, state.Outcome);
            Assert.AreEqual(350, profile.BankedCoins, "100 banked + 250 carried.");
            Assert.AreEqual(500 + 12 + 650, profile.TotalXp);
            Assert.AreEqual(0, state.CarriedCoins);
            Assert.IsNotNull(profile.SafeLoadout);
            var securedIds = profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).Concat(profile.SafeLoadout.Backpack.Select(e => e.Item.InstanceId)).ToList();
            CollectionAssert.Contains(securedIds, found.InstanceId);
            Assert.IsTrue(profile.SafeLoadout.Backpack.All(e => !e.Item.IsAtRisk));
            Assert.IsTrue(profile.SafeLoadout.Equipped.All(e => !e.Item.IsAtRisk));
            Assert.AreEqual(1, ended);
            Assert.AreEqual(250, service.LastSummary.CoinsExtracted);
            Assert.AreEqual(1, service.LastSummary.BossesDefeated);
            CollectionAssert.Contains(service.LastSummary.ExtractedItemIds, found.InstanceId);

            // TASK 047: a replayed Return on the closed transaction is idempotent (same summary, nothing re-committed).
            Assert.AreSame(service.LastSummary, service.Return());
            Assert.AreEqual(1, ended);
            Assert.AreEqual(350, profile.BankedCoins);
        }

        // ---- Acceptance 2: Descend keeps risk ----

        [Test]
        public void Descend_IncrementsDepth_SelectsBiome_AndKeepsEverythingAtRisk()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var state = service.Start(profile, 7, Biome.RuinedMetro);
            var inventoryBefore = state.Inventory;
            var pistol = state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            service.AddCarriedCoins(90);
            var depthsEntered = new List<int>();
            service.DepthEntered += s => depthsEntered.Add(s.Depth);

            Assert.Throws<System.InvalidOperationException>(() => service.Descend(), "No descent before the boss falls.");
            service.RecordBossDefeated(650);
            Assert.IsTrue(service.ChooseTransit(TransitChoice.DescendDeeper));

            Assert.IsTrue(service.IsExpeditionActive);
            Assert.AreEqual(2, state.Depth);
            Assert.AreSame(inventoryBefore, state.Inventory, "Same inventory object: nothing copied.");
            Assert.AreSame(pistol, state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(pistol.IsAtRisk);
            Assert.AreEqual(90, state.CarriedCoins);
            Assert.AreEqual(100, profile.BankedCoins);
            Assert.AreEqual(1150, profile.TotalXp, "XP is permanent and already committed; coins and items are not.");
            Assert.IsNull(profile.SafeLoadout);
            Assert.IsFalse(state.BossDefeatedThisDepth);
            Assert.IsNull(service.Transit);
            CollectionAssert.AreEqual(new[] { 2 }, depthsEntered, "Subscribed after Start: only the descent raises DepthEntered here.");
            Assert.AreEqual(2, state.BiomeHistory.Count);
            Assert.AreEqual(BiomeSelector.SelectNext(Biome.RuinedMetro, 7, 2), state.Biome, "Biome choice is seeded from RunSeed + next depth.");
            Assert.AreEqual(new DepthRunContext(7, 2, state.Biome).Depth, state.CurrentDepth.Depth);
        }

        // ---- Acceptance 3: duplicate transit input ----

        [Test]
        public void DuplicateTransitInput_CannotDoubleCommit()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            service.Start(profile, 42, Biome.RuinedMetro);
            service.AddCarriedCoins(100);
            var transit = service.RecordBossDefeated(650);
            var sameTransit = service.RecordBossDefeated(650);
            Assert.AreSame(transit, sameTransit, "Boss defeat is recorded once per depth.");
            Assert.AreEqual(650, service.State.Stats.XpEarned);

            var resolved = 0;
            transit.Resolved += (_, _) => resolved++;
            Assert.IsTrue(service.ChooseTransit(TransitChoice.ReturnToShelter));
            Assert.IsFalse(service.ChooseTransit(TransitChoice.ReturnToShelter));
            Assert.IsFalse(service.ChooseTransit(TransitChoice.DescendDeeper));
            Assert.IsFalse(transit.Submit("local", TransitChoice.DescendDeeper));

            Assert.AreEqual(1, resolved);
            Assert.AreEqual(200, profile.BankedCoins);
            Assert.AreEqual(TransitDecisionState.Resolved, transit.State);
            Assert.AreEqual(TransitChoice.ReturnToShelter, transit.Result);
        }

        // ---- Acceptance 4: failure never secures ----

        [Test]
        public void Fail_DestroysAtRiskItemsAndCoins_KeepsXp_NeverSecures()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var state = service.Start(profile, 42, Biome.RuinedMetro);
            service.AddCarriedCoins(300);
            service.RecordEliteDefeated(250);
            service.RecordBossDefeated(650);
            service.ChooseTransit(TransitChoice.DescendDeeper);

            var summary = service.Fail();

            Assert.AreEqual(ExpeditionOutcome.Failed, summary.Outcome);
            Assert.AreEqual(2, summary.DepthReached);
            Assert.AreEqual(900, summary.XpEarned);
            Assert.AreEqual(0, summary.CoinsExtracted);
            Assert.IsEmpty(summary.ExtractedItemIds);
            Assert.AreEqual(100, profile.BankedCoins, "Carried coins are lost.");
            Assert.AreEqual(1400, profile.TotalXp, "XP is permanent even on failure.");
            Assert.IsNull(profile.SafeLoadout, "Nothing secured; the pre-run loadout is gone too.");
            Assert.IsNull(state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(0, state.Inventory.BackpackSlots.Count(s => s != null));
            Assert.IsFalse(service.IsExpeditionActive);
            // TASK 047: replayed Fail/Return on the closed transaction are idempotent (same summary, no second loss, no commit).
            Assert.AreSame(service.LastSummary, service.Fail());
            Assert.AreSame(service.LastSummary, service.Return());
            Assert.IsNull(profile.SafeLoadout);
            Assert.AreEqual(100, profile.BankedCoins);
        }

        [Test]
        public void TransitAfterFailure_IsInert()
        {
            var service = NewService();
            service.Start(ProfileWithLoadout(), 1, Biome.RuinedMetro);
            var transit = service.RecordBossDefeated(650);
            service.Fail();
            Assert.IsFalse(transit.Submit("local", TransitChoice.ReturnToShelter) && service.Profile.BankedCoins != 100);
            Assert.AreEqual(100, service.Profile.BankedCoins);
            Assert.IsNull(service.Profile.SafeLoadout);
        }

        // ---- Policy seam for co-op voting ----

        private sealed class UnanimousPolicy : ITransitResolutionPolicy
        {
            public TransitChoice? Resolve(IReadOnlyDictionary<string, TransitChoice> choices, IReadOnlyCollection<string> living)
            {
                if (choices.Values.Any(c => c == TransitChoice.ReturnToShelter)) return TransitChoice.ReturnToShelter;
                return living.All(choices.ContainsKey) ? TransitChoice.DescendDeeper : null;
            }
        }

        [Test]
        public void TransitDecision_AcceptsAnotherPolicy_WithoutTouchingTransactions()
        {
            var decision = new TransitDecision(new UnanimousPolicy(), new[] { "a", "b", "c" });
            decision.Open();
            Assert.IsFalse(decision.Submit("a", TransitChoice.DescendDeeper));
            Assert.IsFalse(decision.Submit("dead", TransitChoice.ReturnToShelter), "Dead players have no vote.");
            Assert.IsFalse(decision.Submit("b", TransitChoice.DescendDeeper));
            Assert.IsTrue(decision.Submit("c", TransitChoice.DescendDeeper));
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result);

            var veto = new TransitDecision(new UnanimousPolicy(), new[] { "a", "b" });
            veto.Open();
            Assert.IsTrue(veto.Submit("a", TransitChoice.ReturnToShelter), "Any Return returns the whole party.");
            Assert.AreEqual(TransitChoice.ReturnToShelter, veto.Result);

            var solo = new TransitDecision(new SoloTransitPolicy(), new[] { "local" });
            Assert.IsFalse(solo.Submit("local", TransitChoice.DescendDeeper), "Closed decisions ignore input.");
            solo.Open();
            Assert.IsTrue(solo.Submit("local", TransitChoice.DescendDeeper), "Solo resolves immediately.");
        }

        [Test]
        public void BiomeSelector_WeightsRepeatsDown_AndIsDeterministic()
        {
            Assert.AreEqual(40, BiomeSelector.WeightFor(Biome.RuinedMetro, Biome.Rustworks));
            Assert.AreEqual(20, BiomeSelector.WeightFor(Biome.Rustworks, Biome.Rustworks));
            var counts = new Dictionary<Biome, int>();
            var rng = new SeededRandom(3);
            for (var i = 0; i < 5000; i++)
            {
                var b = BiomeSelector.SelectNext(Biome.Rustworks, rng);
                counts[b] = counts.TryGetValue(b, out var c) ? c + 1 : 1;
            }

            Assert.That(counts[Biome.Rustworks], Is.InRange(700, 1300), "~20% repeat");
            Assert.That(counts[Biome.RuinedMetro], Is.InRange(1700, 2300), "~40%");
            Assert.That(counts[Biome.OvergrownLabs], Is.InRange(1700, 2300), "~40%");
            Assert.AreEqual(BiomeSelector.SelectNext(Biome.RuinedMetro, 99, 2), BiomeSelector.SelectNext(Biome.RuinedMetro, 99, 2));
        }
    }
}
