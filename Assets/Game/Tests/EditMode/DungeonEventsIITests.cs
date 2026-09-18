using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 078: Broken Machine and Supply Signal on the shared event framework.</summary>
    public class DungeonEventsIITests
    {
        private sealed class RecordingDeliverer : IRewardDeliverer
        {
            public readonly List<LootResult> Delivered = new();
            public void Deliver(LootResult loot) => Delivered.Add(loot);
        }

        private DungeonEventConfig _config;
        private PriceService _prices;
        private LootSourceCatalog _loot;
        private EventRewardRoller _rewards;
        private List<EnemyDefinition> _archetypes;
        private ItemDefinitionRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            _prices = new PriceService(economy);
            _rewards = new EventRewardRoller(_loot);
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null && d.ThreatCost > 0)
                .ToList();
            _registry = ItemDefinitionRegistry.Build(AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null));
            Assert.IsNotNull(_config.BrokenMachineTable, "Broken Machine reward table is authored.");
        }

        private static DungeonEventContext Ctx(int depth, int index = 0, int seed = 42) => new(seed, depth, index);

        private BrokenMachineEvent Machine(int depth, int seed, RecordingDeliverer deliverer = null) => new(Ctx(depth, 0, seed), _config, _prices, _rewards, deliverer ?? new RecordingDeliverer());

        // ---- Acceptance 1: Broken Machine price ----

        [TestCase(1, 100)]
        [TestCase(2, 110)]
        [TestCase(15, 240)]
        [TestCase(30, 390)]
        [TestCase(31, 400)]
        [TestCase(80, 400)]
        public void BrokenMachine_CostIs100Plus10PerDepth_CappedAt400(int depth, int expected)
        {
            Assert.AreEqual(expected, Machine(depth, 1).CostCoins);
        }

        // ---- Broken Machine behaviour ----

        [Test]
        public void BrokenMachine_SpendsTheFeeOnce_YieldsAnItemOrNothing_NeverCoins_AndIsDeterministic()
        {
            var successSeed = Enumerable.Range(0, 200).First(s => Machine(5, s).WillRepairSucceed());
            var failSeed = Enumerable.Range(0, 200).First(s => !Machine(5, s).WillRepairSucceed());

            var goodDeliverer = new RecordingDeliverer();
            var good = Machine(5, successSeed, goodDeliverer);
            var wallet = new CoinWallet(CoinDomain.Carried, 500);
            var actor = new EventActor(wallet);
            var result = good.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.Success, result.Outcome);
            Assert.AreEqual(140, result.CoinsSpent);
            Assert.AreEqual(360, wallet.Balance);
            Assert.AreEqual(1, goodDeliverer.Delivered.Count);
            Assert.AreEqual(1, result.Loot.Items.Count, "Exactly one item/ammo stack/consumable.");
            Assert.AreEqual(0, result.Loot.Coins, "Never coins.");
            Assert.IsTrue(_registry.TryGet(result.Loot.Items[0].DefinitionId, out _));
            Assert.AreEqual(DungeonEventOutcome.None, good.Activate(actor).Outcome, "One attempt per depth.");
            Assert.AreEqual(360, wallet.Balance);

            var badDeliverer = new RecordingDeliverer();
            var bad = Machine(5, failSeed, badDeliverer);
            var failed = bad.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.Failed, failed.Outcome);
            Assert.AreEqual(140, failed.CoinsSpent, "A failed repair still costs the fee (small gamble).");
            Assert.AreEqual(220, wallet.Balance);
            Assert.AreEqual(0, badDeliverer.Delivered.Count);
            Assert.AreEqual(DungeonEventPhase.Failed, bad.Phase);
            Assert.AreEqual(DungeonEventOutcome.None, bad.Activate(actor).Outcome, "No retry after failure.");
            Assert.AreEqual(220, wallet.Balance);

            // Determinism: same seed/depth => same outcome and same loot.
            var replay = Machine(5, successSeed, new RecordingDeliverer()).Activate(new EventActor(new CoinWallet(CoinDomain.Carried, 500)));
            Assert.AreEqual(result.Loot.Items[0].DefinitionId, replay.Loot.Items[0].DefinitionId);
            Assert.AreEqual(result.Loot.Items[0].Quantity, replay.Loot.Items[0].Quantity);
            Assert.AreEqual(result.Loot.Items[0].Rarity, replay.Loot.Items[0].Rarity);
        }

        [Test]
        public void BrokenMachine_SuccessRate_MatchesTheConfiguredChance()
        {
            var successes = Enumerable.Range(0, 1000).Count(s => Machine(3, s).WillRepairSucceed());
            Assert.That(successes / 1000f, Is.InRange(_config.BrokenMachineSuccessPercent / 100f - 0.06f, _config.BrokenMachineSuccessPercent / 100f + 0.06f));
        }

        [Test]
        public void BrokenMachine_InsufficientFunds_ChangesNothing()
        {
            var deliverer = new RecordingDeliverer();
            var machine = Machine(5, 3, deliverer);
            var wallet = new CoinWallet(CoinDomain.Carried, 139);
            var actor = new EventActor(wallet);
            Assert.IsFalse(machine.CanActivate(actor));
            Assert.AreEqual(DungeonEventOutcome.InsufficientFunds, machine.Activate(actor).Outcome);
            Assert.AreEqual(139, wallet.Balance);
            Assert.AreEqual(DungeonEventPhase.Available, machine.Phase);
            Assert.AreEqual(0, deliverer.Delivered.Count);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, machine.Activate(new EventActor(new CoinWallet(CoinDomain.Banked, 5000))).Outcome);
        }

        // ---- Acceptance 2 + 3: Supply Signal ----

        [Test]
        public void SupplySignal_RaisesSeededWaves_AndDeliversTheSupplyRewardOnlyAfterSurvivingTheDuration()
        {
            var deliverer = new RecordingDeliverer();
            var signal = new SupplySignalEvent(Ctx(8), _config, _rewards, deliverer, _archetypes);
            var waves = new List<EncounterPlan>();
            signal.WaveStarted += (_, p) => waves.Add(p);
            var completed = new List<DungeonEventResult>();
            signal.Completed += (_, r) => completed.Add(r);
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0));

            Assert.AreEqual(30f, signal.DurationSeconds);
            Assert.AreEqual(3, signal.PlannedWaveCount);
            Assert.AreEqual(0, signal.CostCoins);
            Assert.AreEqual(DungeonEventOutcome.Started, signal.Activate(actor).Outcome);
            Assert.IsTrue(signal.IsRunning);
            Assert.AreEqual(1, waves.Count, "First wave at activation.");
            Assert.Greater(waves[0].TotalCount, 0);
            Assert.AreEqual(EncounterDirector.Compose(Ctx(8).ForEncounter(), _archetypes).Signature, waves[0].Signature, "Waves come from the encounter director.");

            for (var i = 0; i < 9; i++) signal.Tick(1f);
            Assert.AreEqual(1, waves.Count);
            signal.Tick(1f);
            Assert.AreEqual(2, waves.Count, "Second wave at 10 s.");
            Assert.AreNotEqual(waves[0].Signature + waves[0].TargetThreat, waves[1].Signature + waves[1].TargetThreat, "Different seeded wave.");
            for (var i = 0; i < 10; i++) signal.Tick(1f);
            Assert.AreEqual(3, waves.Count);
            Assert.AreEqual(0, deliverer.Delivered.Count, "Nothing before the duration elapsed.");
            Assert.AreEqual(DungeonEventOutcome.None, signal.Activate(actor).Outcome);

            for (var i = 0; i < 9; i++) signal.Tick(1f);
            Assert.IsTrue(signal.IsRunning);
            Assert.AreEqual(1f, signal.Remaining, 0.001f);
            signal.Tick(1f);

            Assert.IsFalse(signal.IsRunning);
            Assert.AreEqual(DungeonEventPhase.Completed, signal.Phase);
            Assert.AreEqual(1, deliverer.Delivered.Count);
            Assert.IsFalse(deliverer.Delivered[0].IsEmpty, "Supply Chest reward.");
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(3, waves.Count);

            signal.Tick(5f);
            Assert.IsFalse(signal.ReportSurvived());
            Assert.IsFalse(signal.ReportPartyWiped());
            Assert.AreEqual(1, deliverer.Delivered.Count, "Idempotent after completion.");
            Assert.AreEqual(3, waves.Count);
        }

        [Test]
        public void SupplySignal_IsDeterministic_AndWipeFailsWithoutReward()
        {
            var a = new SupplySignalEvent(Ctx(8, 1, 7), _config, _rewards, new RecordingDeliverer(), _archetypes);
            var b = new SupplySignalEvent(Ctx(8, 1, 7), _config, _rewards, new RecordingDeliverer(), _archetypes);
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0));
            a.Activate(actor);
            b.Activate(actor);
            for (var i = 0; i < 30; i++) { a.Tick(1f); b.Tick(1f); }
            CollectionAssert.AreEqual(a.Waves.Select(w => w.Signature), b.Waves.Select(w => w.Signature));
            Assert.AreEqual(a.Result.Loot.Coins, b.Result.Loot.Coins);
            CollectionAssert.AreEqual(a.Result.Loot.Items.Select(i => (i.DefinitionId, i.Rarity, i.Quantity)), b.Result.Loot.Items.Select(i => (i.DefinitionId, i.Rarity, i.Quantity)));

            var deliverer = new RecordingDeliverer();
            var wiped = new SupplySignalEvent(Ctx(8, 2, 7), _config, _rewards, deliverer, _archetypes);
            wiped.Activate(actor);
            wiped.Tick(12f);
            Assert.IsTrue(wiped.ReportPartyWiped());
            Assert.AreEqual(DungeonEventPhase.Failed, wiped.Phase);
            Assert.AreEqual(0, deliverer.Delivered.Count);
            wiped.Tick(30f);
            Assert.AreEqual(0, deliverer.Delivered.Count);
            Assert.AreEqual(2, wiped.Waves.Count, "No waves after the signal ended.");
        }

        // ---- Acceptance 4: exactly the six approved event kinds ----

        [Test]
        public void ExactlySixApprovedEventKinds_Exist()
        {
            CollectionAssert.AreEquivalent(
                new[] { "CursedChest", "LockedVault", "BrokenMachine", "SupplySignal", "MedicalStation", "WeaponCache" },
                System.Enum.GetNames(typeof(DungeonEventKind)));
        }
    }
}
