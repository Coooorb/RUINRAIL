using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 045 — the Base return summary (base/76_EXPEDITION_SUMMARY).</summary>
    public class ExpeditionSummaryTests
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

        private ExpeditionService NewService()
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        private static PlayerProfile ProfileWithLoadout()
        {
            var snapshot = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
            };
            return new PlayerProfile { BankedCoins = 100, TotalXp = 0, SafeLoadout = snapshot };
        }

        /// <summary>Runs a two-depth expedition: 250 coins, a found Rare vest, one boss, XP 400 (level 1 -> 2 at 250? see LevelCurve).</summary>
        private static ItemInstance PlayRun(ExpeditionService service, PlayerProfile profile, int seed = 77)
        {
            service.Start(profile, seed, Biome.RuinedMetro);
            service.AddCarriedCoins(250);
            var found = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare) { IsAtRisk = true };
            Assert.IsTrue(service.State.Inventory.TryAddToBackpack(found));
            service.RecordRoomCleared();
            service.RecordEnemyDefeated(50);
            service.RecordBossDefeated(300);
            service.ChooseTransit(TransitChoice.DescendDeeper);
            service.RecordEnemyDefeated(50);
            return found;
        }

        // ---- Acceptance 1: successful return summarizes committed safe state from the transaction results ----

        [Test]
        public void SuccessfulReturn_SummarizesSecuredItemsBankedCoinsAndXp_FromTheTransaction()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var levelBefore = profile.Level;
            var found = PlayRun(service, profile);
            var carriedIds = AllIds(service.State.Inventory);

            var summary = service.Return();

            Assert.IsTrue(summary.IsSuccess);
            Assert.AreEqual(ExpeditionOutcome.Extracted, summary.Outcome);
            Assert.AreEqual(77, summary.RunSeed);
            Assert.AreEqual(2, summary.DepthReached);
            CollectionAssert.AreEqual(service.State.BiomeHistory, summary.Biomes);
            Assert.AreEqual(Biome.RuinedMetro, summary.Biomes[0]);
            Assert.AreEqual(1, summary.RoomsCleared);
            Assert.AreEqual(2, summary.EnemiesDefeated);
            Assert.AreEqual(1, summary.BossesDefeated);
            Assert.AreEqual(400, summary.XpEarned);
            Assert.AreEqual(400, profile.TotalXp);
            Assert.AreEqual(levelBefore, summary.LevelBefore);
            Assert.AreEqual(profile.Level, summary.LevelAfter);

            // Coins: the summary carries the Carried->Banked receipt itself.
            Assert.AreEqual(CoinTransactionKind.Transfer, summary.CoinResult.Kind);
            Assert.AreEqual(CoinDomain.Banked, summary.CoinResult.Domain);
            Assert.AreEqual(250, summary.CoinsExtracted);
            Assert.AreEqual(350, summary.CoinResult.BalanceAfter);
            Assert.AreEqual(350, profile.BankedCoins);
            Assert.AreEqual(0, summary.CoinsLost);

            // Items: every carried instance is listed as secured with its identity and rarity; nothing lost.
            CollectionAssert.AreEquivalent(carriedIds, summary.SecuredItems.Select(i => i.InstanceId));
            var vest = summary.SecuredItems.Single(i => i.InstanceId == found.InstanceId);
            Assert.AreEqual("armor_scrap_vest", vest.DefinitionId);
            Assert.AreEqual(Rarity.Rare, vest.Rarity);
            Assert.AreEqual(1, vest.Quantity);
            Assert.AreEqual(40, summary.SecuredItems.Single(i => i.DefinitionId == "ammo_light").Quantity);
            Assert.IsEmpty(summary.LostItems);
            CollectionAssert.AreEquivalent(carriedIds, profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).Concat(profile.SafeLoadout.Backpack.Select(e => e.Item.InstanceId)));
            Assert.AreEqual(1, summary.ExpeditionIndex);
            Assert.AreEqual(1, profile.ExpeditionsEnded);
        }

        // ---- Acceptance 2: failure summarizes at-risk losses while XP remains ----

        [Test]
        public void Failure_SummarizesLostItemsAndCoins_WhileXpStaysCommitted()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            var found = PlayRun(service, profile);
            var carriedIds = AllIds(service.State.Inventory);

            var summary = service.Fail();

            Assert.IsFalse(summary.IsSuccess);
            Assert.AreEqual(ExpeditionOutcome.Failed, summary.Outcome);
            Assert.AreEqual(2, summary.DepthReached);
            Assert.AreEqual(400, summary.XpEarned);
            Assert.AreEqual(400, profile.TotalXp, "XP is permanent even on failure.");
            Assert.AreEqual(0, summary.CoinsExtracted);
            Assert.AreEqual(250, summary.CoinsLost);
            Assert.AreEqual(100, profile.BankedCoins, "Banked coins untouched by failure.");
            Assert.AreEqual(100, summary.CoinResult.BalanceAfter);
            Assert.IsEmpty(summary.SecuredItems);
            CollectionAssert.AreEquivalent(carriedIds, summary.LostItems.Select(i => i.InstanceId));
            Assert.AreEqual(Rarity.Rare, summary.LostItems.Single(i => i.InstanceId == found.InstanceId).Rarity);
            Assert.IsNull(profile.SafeLoadout, "Carried gear destroyed.");
            Assert.AreEqual(1, summary.ExpeditionIndex);
        }

        // ---- Acceptance 3: reopening the summary never reapplies rewards; deterministic across "scene transitions" ----

        [Test]
        public void ReopeningTheSummary_IsReadOnly_AndNeverReappliesRewards()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            PlayRun(service, profile);
            var summary = service.Return();
            var coins = profile.BankedCoins;
            var xp = profile.TotalXp;
            var loadoutJson = JsonUtility.ToJson(profile.SafeLoadout);

            // "Reopen" any number of times, including after the service reference would be dropped by a scene load.
            for (var i = 0; i < 3; i++)
            {
                var reopened = service.LastSummary;
                Assert.AreSame(summary, reopened);
                Assert.AreEqual(250, reopened.CoinsExtracted);
                Assert.AreEqual(400, reopened.XpEarned);
                Assert.AreEqual(coins, profile.BankedCoins);
                Assert.AreEqual(xp, profile.TotalXp);
                Assert.AreEqual(loadoutJson, JsonUtility.ToJson(profile.SafeLoadout));
                Assert.AreEqual(1, profile.ExpeditionsEnded);
            }

            Assert.IsFalse(service.IsExpeditionActive);
            Assert.AreSame(summary, service.Return(), "A replayed Return on the closed transaction yields the same summary and commits nothing.");
            Assert.AreSame(summary, service.Fail(), "A replayed Fail on the closed transaction loses nothing.");
            Assert.AreEqual(coins, profile.BankedCoins);
            Assert.AreEqual(loadoutJson, JsonUtility.ToJson(profile.SafeLoadout));
            Assert.AreEqual(1, profile.ExpeditionsEnded);

            // Read-only model: no public setters and no mutable public fields.
            var type = typeof(ExpeditionSummary);
            Assert.IsEmpty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            Assert.IsTrue(type.GetProperties().All(p => p.GetSetMethod(false) == null), "Summary properties are get-only.");
            Assert.IsFalse(typeof(ItemSummaryLine).GetProperties().Any(p => p.GetSetMethod(false) != null));
        }

        [Test]
        public void SummaryIsBuiltFromTransactionCapture_NotFromInventoryAfterMutation()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            PlayRun(service, profile);
            var summary = service.Fail();
            // After Fail the inventory is empty, yet the summary still knows what was lost.
            Assert.AreEqual(0, AllIds(service.State.Inventory).Count);
            Assert.AreEqual(3, summary.LostItems.Count);
            Assert.AreEqual(0, service.State.CarriedCoins);
            Assert.AreEqual(250, summary.CoinsLost);
        }

        // ---- Acceptance 4: depth/outcome/index available for Trader refresh and tutorial hooks; sequence counts persist ----

        [Test]
        public void ExpeditionIndexAndDepth_AreExposedForTraderRefresh_AndPersistWithTheProfile()
        {
            var service = NewService();
            var profile = ProfileWithLoadout();
            ExpeditionSummary fromEvent = null;
            service.ExpeditionEnded += s => fromEvent = s;

            PlayRun(service, profile);
            var first = service.Return();
            Assert.AreSame(first, fromEvent);
            Assert.AreEqual(1, first.ExpeditionIndex);
            Assert.AreEqual(2, first.DepthReached);

            PlayRun(service, profile, 78);
            var second = service.Fail();
            Assert.AreEqual(2, second.ExpeditionIndex);
            Assert.AreEqual(78, second.RunSeed);
            Assert.AreEqual(ExpeditionOutcome.Failed, second.Outcome);

            var restored = JsonUtility.FromJson<PlayerProfile>(JsonUtility.ToJson(profile));
            Assert.AreEqual(2, restored.ExpeditionsEnded, "Ended-expedition counter persists.");
        }

        private static List<string> AllIds(PlayerInventory inventory)
        {
            var ids = new List<string>();
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) ids.Add(e.InstanceId);
            }

            ids.AddRange(inventory.BackpackSlots.Where(i => i != null).Select(i => i.InstanceId));
            return ids;
        }
    }
}
