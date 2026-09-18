using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 077: event framework + Cursed Chest + Locked Vault.</summary>
    public class DungeonEventsITests
    {
        private sealed class RecordingDeliverer : IRewardDeliverer
        {
            public readonly List<LootResult> Delivered = new();
            public void Deliver(LootResult loot) => Delivered.Add(loot);
        }

        private DungeonEventConfig _config;
        private EconomyConfig _economy;
        private PriceService _prices;
        private LootSourceCatalog _loot;
        private EventRewardRoller _rewards;
        private List<EnemyDefinition> _archetypes;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            Assert.IsNotNull(_config);
            Assert.IsNotNull(_loot);
            _prices = new PriceService(_economy);
            _rewards = new EventRewardRoller(_loot);
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null && d.ThreatCost > 0)
                .ToList();
            Assert.GreaterOrEqual(_archetypes.Count, 9);
            _registry = ItemDefinitionRegistry.Build(AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null));
        }

        private static DungeonEventContext Ctx(int depth, int index = 0, int seed = 42, int party = 1) => new(seed, depth, index, party);

        private static string Signature(LootResult loot) => $"{loot.Coins}|" + string.Join(",", loot.Items.Select(i => $"{i.DefinitionId}:{i.Rarity}:{i.Quantity}:" + string.Join("+", i.AffixRolls.Select(r => $"{r.AffixId}={r.Value}"))));

        // ---- Acceptance 1: Locked Vault price formula and cap ----

        [TestCase(1, 250)]
        [TestCase(2, 275)]
        [TestCase(10, 475)]
        [TestCase(30, 975)]
        [TestCase(31, 1000)]
        [TestCase(50, 1000)]
        [TestCase(200, 1000)]
        public void LockedVault_CostIs250Plus25PerDepth_CappedAt1000(int depth, int expected)
        {
            var vault = new LockedVaultEvent(Ctx(depth), _config, _prices, _rewards, new RecordingDeliverer());
            Assert.AreEqual(expected, vault.CostCoins);
            Assert.AreEqual(expected, _prices.EventPrice(DungeonEventPriceKind.LockedVault, depth));
        }

        // ---- Acceptance 2 + 4: pay once, atomic, room lifecycle signal ----

        [Test]
        public void LockedVault_DebitsCarriedCoinsAndDeliversGuaranteedLoot_ExactlyOnce()
        {
            var deliverer = new RecordingDeliverer();
            var vault = new LockedVaultEvent(Ctx(10), _config, _prices, _rewards, deliverer);
            var wallet = new CoinWallet(CoinDomain.Carried, 1000);
            var banked = new CoinWallet(CoinDomain.Banked, 999);
            var completed = new List<DungeonEventResult>();
            vault.Completed += (_, r) => completed.Add(r);
            var actor = new EventActor(wallet, participantId: "host");

            Assert.IsTrue(vault.IsLocked);
            Assert.IsTrue(vault.CanActivate(actor));
            var result = vault.Activate(actor);

            Assert.AreEqual(DungeonEventOutcome.Success, result.Outcome);
            Assert.AreEqual(475, result.CoinsSpent);
            Assert.AreEqual(525, wallet.Balance);
            Assert.AreEqual(999, banked.Balance);
            Assert.AreEqual(DungeonEventPhase.Completed, vault.Phase);
            Assert.IsFalse(vault.IsLocked);
            Assert.AreEqual("host", vault.PaidBy);
            Assert.AreEqual(1, deliverer.Delivered.Count);
            Assert.AreSame(result.Loot, deliverer.Delivered[0]);
            Assert.IsTrue(result.Loot.Items.Any(i => _registry.TryGet(i.DefinitionId, out var d) && d is EquipmentItemDefinition), "Treasure table guarantees equipment.");
            Assert.IsEmpty(result.Loot.Warnings);
            Assert.AreEqual(1, completed.Count, "Room lifecycle signal fires once.");
            Assert.AreSame(result, vault.Result);

            // Re-interaction after resolution: nothing paid, nothing delivered.
            var again = vault.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.None, again.Outcome);
            Assert.IsFalse(vault.CanActivate(actor));
            Assert.AreEqual(525, wallet.Balance);
            Assert.AreEqual(1, deliverer.Delivered.Count);
            Assert.AreEqual(1, completed.Count);
        }

        // ---- Acceptance 3: insufficient Carried Coins leave everything unchanged ----

        [Test]
        public void LockedVault_InsufficientFunds_KeepsItLocked_AndDeliversNothing()
        {
            var deliverer = new RecordingDeliverer();
            var vault = new LockedVaultEvent(Ctx(10), _config, _prices, _rewards, deliverer);
            var wallet = new CoinWallet(CoinDomain.Carried, 474);
            var actor = new EventActor(wallet);
            var completed = 0;
            vault.Completed += (_, _) => completed++;

            Assert.IsFalse(vault.CanActivate(actor));
            var result = vault.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.InsufficientFunds, result.Outcome);
            Assert.AreEqual(474, wallet.Balance);
            Assert.IsTrue(vault.IsLocked);
            Assert.AreEqual(DungeonEventPhase.Available, vault.Phase);
            Assert.IsNull(vault.Result);
            Assert.AreEqual(0, deliverer.Delivered.Count);
            Assert.AreEqual(0, completed);

            // Banked wallet is never accepted as payment.
            var bankedActor = new EventActor(new CoinWallet(CoinDomain.Banked, 5000));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, vault.Activate(bankedActor).Outcome);
            Assert.AreEqual(DungeonEventPhase.Available, vault.Phase);

            wallet.Credit(1, "top_up");
            Assert.AreEqual(DungeonEventOutcome.Success, vault.Activate(actor).Outcome);
            Assert.AreEqual(0, wallet.Balance);
        }

        [Test]
        public void EventLoot_IsDeterministic_PerSeedDepthAndIndex_AndSeparateFromChests()
        {
            var a = new LockedVaultEvent(Ctx(7, 2, 99), _config, _prices, _rewards, new RecordingDeliverer());
            var b = new LockedVaultEvent(Ctx(7, 2, 99), _config, _prices, _rewards, new RecordingDeliverer());
            var c = new LockedVaultEvent(Ctx(7, 3, 99), _config, _prices, _rewards, new RecordingDeliverer());
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 100000));
            var ra = a.Activate(actor).Loot;
            var rb = b.Activate(actor).Loot;
            var rc = c.Activate(actor).Loot;
            Assert.AreEqual(Signature(ra), Signature(rb));
            Assert.AreNotEqual(Signature(ra), Signature(rc));
            CollectionAssert.AreNotEqual(ra.Items.Select(i => i.InstanceId), rb.Items.Select(i => i.InstanceId), "Distinct instances even for identical rolls.");

            var chest = LootContext.ForSource(99, 7, 2, LootQuality.Improved);
            var chestLoot = _loot.CreateRoller().Roll(_loot.Sources.First(s => s.Kind == LootSourceKind.TreasureChest).Table, chest);
            Assert.AreNotEqual(Signature(ra), Signature(chestLoot), "Event loot never mirrors chest #2 of the depth.");
        }

        // ---- Cursed Chest ----

        [Test]
        public void CursedChest_LocksDoors_SpawnsAHarderEncounter_ThenPaysOutOnceWhenCleared()
        {
            var deliverer = new RecordingDeliverer();
            var chest = new CursedChestEvent(Ctx(12), _config, _rewards, deliverer, _archetypes);
            var plans = new List<EncounterPlanSnapshot>();
            chest.EncounterStarted += (_, p) => plans.Add(new EncounterPlanSnapshot(p.TargetThreat, p.TotalThreat, p.Signature));
            var completed = new List<DungeonEventResult>();
            chest.Completed += (_, r) => completed.Add(r);
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0));

            Assert.AreEqual(0, chest.CostCoins, "Opening is a choice, not a purchase.");
            Assert.IsTrue(chest.CanActivate(actor));
            Assert.IsFalse(chest.DoorsLocked);
            var start = chest.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.Started, start.Outcome);
            Assert.IsTrue(chest.DoorsLocked);
            Assert.AreEqual(DungeonEventPhase.InProgress, chest.Phase);
            Assert.AreEqual(1, plans.Count);

            var normal = RuinRail.Gameplay.Enemies.Encounters.EncounterDirector.Compose(Ctx(12).ForEncounter(), _archetypes);
            Assert.AreEqual(normal.TargetThreat * _config.CursedChestThreatScale, chest.Plan.TargetThreat, 0.001f, "Harder: threat target scaled.");
            Assert.Greater(chest.Plan.TotalThreat, normal.TotalThreat * 1.2f, "Composition actually got harder.");
            Assert.AreEqual(chest.ComposePlan().Signature, chest.Plan.Signature, "Deterministic plan.");

            Assert.AreEqual(DungeonEventOutcome.None, chest.Activate(actor).Outcome, "Cannot re-open while in progress.");
            Assert.AreEqual(0, deliverer.Delivered.Count, "No reward before the encounter is cleared.");

            Assert.IsTrue(chest.ReportEncounterCleared());
            Assert.IsFalse(chest.DoorsLocked);
            Assert.AreEqual(DungeonEventPhase.Completed, chest.Phase);
            Assert.AreEqual(1, deliverer.Delivered.Count);
            Assert.IsTrue(deliverer.Delivered[0].Items.Count > 0);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(DungeonEventOutcome.Success, completed[0].Outcome);

            Assert.IsFalse(chest.ReportEncounterCleared(), "Second clear report pays nothing.");
            Assert.IsFalse(chest.ReportEncounterFailed());
            Assert.AreEqual(DungeonEventOutcome.None, chest.Activate(actor).Outcome);
            Assert.AreEqual(1, deliverer.Delivered.Count);
            Assert.AreEqual(1, completed.Count);
        }

        private readonly struct EncounterPlanSnapshot
        {
            public EncounterPlanSnapshot(float target, float total, string signature) { Target = target; Total = total; Signature = signature; }
            public float Target { get; }
            public float Total { get; }
            public string Signature { get; }
        }

        [Test]
        public void CursedChest_LostEncounter_FailsWithoutReward_AndStaysSpent()
        {
            var deliverer = new RecordingDeliverer();
            var chest = new CursedChestEvent(Ctx(3), _config, _rewards, deliverer, _archetypes);
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0));
            chest.Activate(actor);

            Assert.IsTrue(chest.ReportEncounterFailed());
            Assert.AreEqual(DungeonEventPhase.Failed, chest.Phase);
            Assert.AreEqual(DungeonEventOutcome.Failed, chest.Result.Outcome);
            Assert.AreEqual(0, deliverer.Delivered.Count);
            Assert.IsFalse(chest.DoorsLocked);
            Assert.IsFalse(chest.ReportEncounterCleared());
            Assert.IsFalse(chest.CanActivate(actor));
        }

        // ---- Requirement 5: prompt data is separate from the services ----

        [Test]
        public void PromptBuilder_ReflectsCostAffordabilityAndAvailability_WithoutTouchingTheEvent()
        {
            var vault = new LockedVaultEvent(Ctx(5), _config, _prices, _rewards, new RecordingDeliverer());
            var poor = EventPromptBuilder.Build(vault, 100);
            Assert.AreEqual("Locked Vault", poor.Title);
            Assert.AreEqual("Unlock", poor.ActionLabel);
            Assert.AreEqual(350, poor.CostCoins);
            Assert.IsFalse(poor.IsAffordable);
            Assert.IsTrue(poor.IsAvailable);
            Assert.IsTrue(poor.HasCost);

            var rich = EventPromptBuilder.Build(vault, 350);
            Assert.IsTrue(rich.IsAffordable);
            Assert.AreEqual(DungeonEventPhase.Available, vault.Phase, "Building prompts never activates.");

            var chest = new CursedChestEvent(Ctx(5), _config, _rewards, new RecordingDeliverer(), _archetypes);
            var chestPrompt = EventPromptBuilder.Build(chest, 0);
            Assert.AreEqual("Cursed Chest", chestPrompt.Title);
            Assert.AreEqual("Open", chestPrompt.ActionLabel);
            Assert.IsFalse(chestPrompt.HasCost);
            Assert.IsTrue(chestPrompt.IsAffordable);
            Assert.IsTrue(typeof(IDungeonEvent).GetProperties().All(p => p.PropertyType != typeof(EventPrompt)), "Services do not own prompt data.");
        }
    }
}
