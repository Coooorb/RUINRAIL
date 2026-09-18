using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 079: Medical Station (heal + revive hook) and Weapon Cache.</summary>
    public class DungeonEventsIIITests
    {
        private sealed class FakePatient : IMedicalPatient
        {
            public FakePatient(int current, int max) { CurrentHealth = current; MaxHealth = max; }
            public int CurrentHealth { get; private set; }
            public int MaxHealth { get; }
            public bool IsAlive => CurrentHealth > 0;
            public int HealCalls;
            public bool Heal(int amount)
            {
                HealCalls++;
                if (!IsAlive || amount <= 0) return false;
                var before = CurrentHealth;
                CurrentHealth = Mathf.Min(MaxHealth, CurrentHealth + amount);
                return CurrentHealth > before;
            }
        }

        private sealed class FakeReviveAuthority : IReviveAuthority
        {
            public readonly HashSet<string> Dead = new();
            public readonly List<(string id, int percent)> Revives = new();
            public bool RefuseNext;
            public bool IsDead(string participantId) => Dead.Contains(participantId);
            public bool Revive(string participantId, int healthPercent)
            {
                if (RefuseNext) { RefuseNext = false; return false; }
                if (!Dead.Remove(participantId)) return false;
                Revives.Add((participantId, healthPercent));
                return true;
            }
        }

        private DungeonEventConfig _config;
        private PriceService _prices;
        private LootSourceCatalog _loot;
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _loot = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _prices = new PriceService(economy);
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
        }

        private static DungeonEventContext Ctx(int depth, int index = 0, int seed = 42, int party = 1) => new(seed, depth, index, party);

        private MedicalStationEvent Station(int depth, IReviveAuthority authority = null) => new(Ctx(depth), _config, _prices, authority);

        // ---- Acceptance 1: both price formulas ----

        [TestCase(1, 150, 500)]
        [TestCase(2, 165, 530)]
        [TestCase(20, 435, 1070)]
        [TestCase(31, 600, 1400)]
        [TestCase(34, 600, 1490)]
        [TestCase(35, 600, 1500)]
        [TestCase(99, 600, 1500)]
        public void MedicalStation_HealAndRevivePrices_FollowTheFormulasAndCaps(int depth, int heal, int revive)
        {
            var station = Station(depth);
            Assert.AreEqual(heal, station.HealCost);
            Assert.AreEqual(revive, station.ReviveCost);
            Assert.AreEqual(heal, station.CostCoins, "Default interaction is the heal.");
        }

        // ---- Acceptance 2: heal to Max HP, charged only on a valid purchase ----

        [Test]
        public void Heal_RestoresToMaxHealth_ChargesOnce_AndRefusesFullHealthOrInsufficientFunds()
        {
            var station = Station(10);
            var patient = new FakePatient(35, 100);
            var wallet = new CoinWallet(CoinDomain.Carried, 1000);
            var actor = new EventActor(wallet, participantId: "host", patient: patient);
            var healed = new List<int>();
            station.Healed += (_, _, amount) => healed.Add(amount);

            Assert.IsTrue(station.CanActivate(actor));
            var result = station.Activate(actor);
            Assert.AreEqual(DungeonEventOutcome.Success, result.Outcome);
            Assert.IsFalse(result.IsTerminal, "The station stays in service.");
            Assert.AreEqual(285, result.CoinsSpent);
            Assert.AreEqual(715, wallet.Balance);
            Assert.AreEqual(100, patient.CurrentHealth, "Heal cannot exceed Max HP.");
            CollectionAssert.AreEqual(new[] { 65 }, healed);
            Assert.AreEqual(DungeonEventPhase.Available, station.Phase);
            Assert.AreEqual(1, station.HealsUsedBy("host"));

            // Full health: no charge.
            Assert.IsFalse(station.CanActivate(actor));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.Activate(actor).Outcome);
            Assert.AreEqual(715, wallet.Balance);

            // Per-participant use count (PROTOTYPE 1): a second heal for the same participant is refused even when hurt.
            var hurtAgain = new EventActor(wallet, participantId: "host", patient: new FakePatient(10, 100));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.Activate(hurtAgain).Outcome);
            Assert.AreEqual(715, wallet.Balance);

            // Another participant with too few coins: nothing happens.
            var poorPatient = new FakePatient(10, 100);
            var poor = new EventActor(new CoinWallet(CoinDomain.Carried, 284), participantId: "p2", patient: poorPatient);
            Assert.IsFalse(station.CanActivate(poor));
            Assert.AreEqual(DungeonEventOutcome.InsufficientFunds, station.Activate(poor).Outcome);
            Assert.AreEqual(10, poorPatient.CurrentHealth);
            Assert.AreEqual(0, poorPatient.HealCalls);

            // A dead patient or one without health cannot buy a heal.
            var dead = new EventActor(wallet, participantId: "p3", patient: new FakePatient(0, 100));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.Activate(dead).Outcome);
            var noPatient = new EventActor(wallet, participantId: "p4");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.Activate(noPatient).Outcome);
            Assert.AreEqual(715, wallet.Balance);
            Assert.AreEqual(1, station.TotalHeals);
        }

        // ---- Acceptance 3: revive hook ----

        [Test]
        public void Revive_ChargesOnlyForAConfirmedDeadTarget_AndNeverInSolo()
        {
            var wallet = new CoinWallet(CoinDomain.Carried, 5000);
            var requester = new EventActor(wallet, participantId: "host");

            var solo = Station(3);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, solo.RequestRevive(requester, "p2").Outcome, "No authority (Solo): never charges.");
            Assert.AreEqual(5000, wallet.Balance);

            var authority = new FakeReviveAuthority();
            var station = Station(3, authority);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "p2").Outcome, "Living target.");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "ghost").Outcome, "Nonexistent target.");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, null).Outcome);
            Assert.AreEqual(5000, wallet.Balance);
            Assert.AreEqual(0, authority.Revives.Count);

            authority.Dead.Add("p2");
            var poor = new EventActor(new CoinWallet(CoinDomain.Carried, 559), participantId: "p3");
            Assert.AreEqual(DungeonEventOutcome.InsufficientFunds, station.RequestRevive(poor, "p2").Outcome);
            Assert.IsTrue(authority.IsDead("p2"));
            Assert.AreEqual(559, poor.Wallet.Balance);

            authority.RefuseNext = true;
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "p2").Outcome, "Authority refused: refunded.");
            Assert.AreEqual(5000, wallet.Balance);
            Assert.AreEqual(1, station.RevivesRemaining);

            var revived = new List<(string by, string target, int cost)>();
            station.Revived += (_, by, target, cost) => revived.Add((by, target, cost));
            var ok = station.RequestRevive(requester, "p2");
            Assert.AreEqual(DungeonEventOutcome.Success, ok.Outcome);
            Assert.AreEqual(560, ok.CoinsSpent);
            Assert.AreEqual(4440, wallet.Balance);
            Assert.IsFalse(authority.IsDead("p2"));
            CollectionAssert.AreEqual(new[] { ("p2", 30) }, authority.Revives);
            CollectionAssert.AreEqual(new[] { ("host", "p2", 560) }, revived);
            Assert.AreEqual(0, station.RevivesRemaining);

            authority.Dead.Add("p3");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "p3").Outcome, "Revive uses spent for this depth.");
            Assert.AreEqual(4440, wallet.Balance);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(new EventActor(new CoinWallet(CoinDomain.Banked, 9999)), "p3").Outcome);
        }

        // ---- Acceptance 4: Weapon Cache ----

        private WeaponCacheEvent Cache(int seed = 42, int depth = 6, int index = 0) => new(Ctx(depth, index, seed), _config, _catalog, _loot.RarityTableFor);

        [Test]
        public void WeaponCache_PresentsThreeDistinctWeapons_Deterministically()
        {
            var a = Cache();
            var b = Cache();
            Assert.AreEqual(3, a.Choices.Count);
            Assert.IsTrue(a.Choices.All(c => c.Definition is WeaponDefinition));
            Assert.AreEqual(3, a.Choices.Select(c => c.Definition.Id).Distinct().Count(), "Three different weapons.");
            Assert.IsTrue(a.Choices.All(c => c.Item.Rarity != Rarity.Legendary || !string.IsNullOrEmpty(c.Definition.LegendaryMechanicId)), "Legendary rolls use the class Legendary definition.");
            Assert.AreEqual(a.ChoicesSignature, b.ChoicesSignature);
            Assert.AreNotEqual(a.ChoicesSignature, Cache(seed: 43).ChoicesSignature);
            Assert.AreNotEqual(a.ChoicesSignature, Cache(index: 1).ChoicesSignature);
            Assert.AreEqual(0, a.CostCoins);
        }

        [Test]
        public void WeaponCache_ChoosingOne_MovesItToTheChooser_AndConsumesTheCacheForEveryone()
        {
            var cache = Cache();
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0), new BackpackContainer(inventory), "host");
            var chosen = new List<(int index, string by)>();
            cache.Chosen += (_, c, by) => chosen.Add((c.Index, by));
            var completed = 0;
            cache.Completed += (_, _) => completed++;

            Assert.IsTrue(cache.CanActivate(actor));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, cache.Activate(actor).Outcome, "Activation only presents; a choice is required.");
            Assert.AreEqual(DungeonEventPhase.Available, cache.Phase);
            Assert.IsTrue(cache.CanChoose(actor, 1));
            Assert.IsFalse(cache.CanChoose(actor, 3));

            var picked = cache.Choices[1].Item;
            var result = cache.Choose(actor, 1);
            Assert.AreEqual(DungeonEventOutcome.Success, result.Outcome);
            Assert.IsTrue(inventory.Contains(picked.InstanceId));
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null));
            Assert.IsTrue(cache.IsConsumed);
            Assert.AreEqual(1, cache.ChosenIndex);
            Assert.AreEqual("host", cache.ChosenBy);
            Assert.AreEqual(1, completed);
            CollectionAssert.AreEqual(new[] { (1, "host") }, chosen);

            var teammateInventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var teammate = new EventActor(new CoinWallet(CoinDomain.Carried, 0), new BackpackContainer(teammateInventory), "p2");
            Assert.AreEqual(DungeonEventOutcome.None, cache.Choose(teammate, 0).Outcome, "Consumed for the whole party.");
            Assert.AreEqual(DungeonEventOutcome.None, cache.Choose(actor, 2).Outcome);
            Assert.AreEqual(0, teammateInventory.BackpackSlots.Count(s => s != null));
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null), "Exactly one weapon, once.");
            Assert.IsFalse(cache.CanActivate(actor));
        }

        [Test]
        public void WeaponCache_FullBackpack_RejectsWithoutConsuming()
        {
            var cache = Cache();
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++) inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger"));
            var actor = new EventActor(new CoinWallet(CoinDomain.Carried, 0), new BackpackContainer(inventory));

            Assert.IsFalse(cache.CanChoose(actor, 0));
            Assert.AreEqual(DungeonEventOutcome.Unavailable, cache.Choose(actor, 0).Outcome);
            Assert.IsFalse(cache.IsConsumed);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, cache.Choose(actor, -1).Outcome);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, cache.Choose(new EventActor(null), 0).Outcome);
            Assert.AreEqual(DungeonEventPhase.Available, cache.Phase);
        }
    }
}
