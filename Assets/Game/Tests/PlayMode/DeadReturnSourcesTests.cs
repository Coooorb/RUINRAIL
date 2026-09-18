using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 105: the two fully-Dead return sources — Defibrillator (consumed once on success) and paid Medical Station revive (500 + 30 x (Depth-1), cap 1,500).</summary>
    public class DeadReturnSourcesTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private DungeonEventConfig _eventConfig;
        private PriceService _prices;
        private GlobalStatCapsConfig _caps;

        private sealed class Authority : IAuthorityContext
        {
            public NetworkRole Role => NetworkRole.Host;
            public bool IsAuthority => true;
        }

        private sealed class Actor
        {
            public GameObject Go;
            public PlayerLifeStateComponent Life;
            public HealthComponent Health;
            public PlayerInventory Inventory;
            public PlayerConsumableUser Consumables;
        }

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _eventConfig = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            _prices = new PriceService(AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"));
            _caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(_caps);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private Actor Player(string name, PartyLifeRoster roster, Vector2 position, PartyReviveAuthority revives = null)
        {
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, Position = position, LifeRoster = roster, ParticipantId = name });
            _created.Add(go);
            var life = go.GetComponent<PlayerLifeStateComponent>();
            var health = go.GetComponent<HealthComponent>();
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var consumables = go.AddComponent<PlayerConsumableUser>();
            consumables.Configure(inventory, Resolve, new PlayerStats(_caps, 100), new PlayerCombatEvents(), health,
                requestRevive: revives != null ? request => revives.ReviveWithDefibrillator(life, request) : null);
            return new Actor { Go = go, Life = life, Health = health, Inventory = inventory, Consumables = consumables };
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        private static void MakeDead(Actor actor)
        {
            Kill(actor.Health);
            actor.Life.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, actor.Life.State);
        }

        private ItemInstance EquipDefibrillator(Actor actor)
        {
            var item = new ItemInstance("consumable_defibrillator", 1, Rarity.Legendary);
            Assert.IsTrue(actor.Inventory.TryEquip(item, EquippedSlot.ActiveConsumable));
            return item;
        }

        // ---- Defibrillator ----

        [Test]
        public void Defibrillator_RevivesTheDeadTeammate_AtThirtyPercent_AndIsConsumedOnce()
        {
            var roster = new PartyLifeRoster();
            var revives = new PartyReviveAuthority(roster, _balance);
            var dead = Player("Dead", roster, Vector2.zero, revives);
            var user = Player("User", roster, new Vector2(0.5f, 0f), revives);
            MakeDead(dead);
            EquipDefibrillator(user);

            Assert.IsTrue(user.Consumables.TryUse());
            user.Consumables.UseAction.Tick(0.01f);
            Assert.AreEqual(PlayerLifeState.Alive, dead.Life.State);
            Assert.AreEqual(Mathf.RoundToInt(dead.Health.MaxHealth * 0.3f), dead.Health.CurrentHealth, "31/84: 30% Max HP.");
            Assert.IsTrue(dead.Life.CanAct, "Control returns.");
            Assert.IsFalse(dead.Go.GetComponent<DeadSpectatorFollow>().IsSpectating, "Camera back on the player.");
            Assert.IsTrue(dead.Health.IsInvulnerable, "Revive protection applied.");
            Assert.IsNull(user.Inventory.GetEquipped(EquippedSlot.ActiveConsumable), "The single unit is consumed.");
            Assert.AreEqual(1, revives.Revives);
            Assert.IsNotNull(dead.Go.GetComponent<PlayerLifeStateComponent>(), "At-risk carried items were never dropped: nothing to restore.");
        }

        [Test]
        public void Defibrillator_IsNotConsumed_WhenNoValidDeadTeammate_LivingDownedOrSelf()
        {
            var roster = new PartyLifeRoster();
            var revives = new PartyReviveAuthority(roster, _balance);
            var mate = Player("Mate", roster, Vector2.zero, revives);
            var user = Player("User", roster, new Vector2(0.5f, 0f), revives);
            var item = EquipDefibrillator(user);

            // Living teammate: nothing to revive.
            Assert.IsTrue(user.Consumables.TryUse());
            user.Consumables.UseAction.Tick(0.01f);
            Assert.AreEqual(1, user.Inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.Quantity, "Not consumed on a living target.");

            // Downed teammate: the standard hold revives; the Defibrillator is for the fully Dead only.
            Kill(mate.Health);
            Assert.AreEqual(PlayerLifeState.Downed, mate.Life.State);
            Assert.IsTrue(user.Consumables.TryUse());
            user.Consumables.UseAction.Tick(0.01f);
            Assert.AreEqual(PlayerLifeState.Downed, mate.Life.State);
            Assert.AreEqual(1, user.Inventory.GetEquipped(EquippedSlot.ActiveConsumable)?.Quantity, "Not consumed on a Downed target.");

            // Out of reach: nothing happens either.
            mate.Life.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, mate.Life.State);
            mate.Go.transform.position = new Vector2(_balance.ReviveRangeTiles + 5f, 0f);
            Assert.IsTrue(user.Consumables.TryUse());
            user.Consumables.UseAction.Tick(0.01f);
            Assert.AreEqual(PlayerLifeState.Dead, mate.Life.State);
            Assert.AreSame(item, user.Inventory.GetEquipped(EquippedSlot.ActiveConsumable));

            // Self can never be the target: a Dead user cannot use items at all.
            Assert.IsNull(revives.FindDeadTeammateInReach(mate.Life), "A dead player never targets itself.");
            Assert.IsFalse(revives.ReviveWithDefibrillator(mate.Life, new ReviveRequest("consumable_defibrillator", 30)));
        }

        [Test]
        public void Defibrillator_HasZeroSoloDropEligibility_AndIsCoopEligible()
        {
            var definition = (ConsumableDefinition)Resolve("consumable_defibrillator");
            Assert.IsNotNull(definition);
            Assert.AreEqual(ConsumableEffectKind.Revive, definition.EffectKind);
            Assert.AreEqual(30, definition.ReviveHealthPercent);
            Assert.AreEqual(1, definition.MaxStack);
            Assert.IsFalse(definition.IsDropEligible(1), "31: zero drop chance in Solo.");
            Assert.IsTrue(definition.IsDropEligible(2));
            Assert.IsTrue(definition.IsDropEligible(3));
        }

        [Test]
        public void Defibrillator_IsUnsupportedWithoutAReviveAuthority_Solo()
        {
            var user = Player("Solo", null, Vector2.zero);
            EquipDefibrillator(user);
            Assert.IsFalse(user.Consumables.TryUse(), "No party revive authority: the use is refused, nothing consumed.");
            Assert.AreEqual(1, user.Inventory.GetEquipped(EquippedSlot.ActiveConsumable).Quantity);
        }

        // ---- Medical Station ----

        [TestCase(1, 500)]
        [TestCase(2, 530)]
        [TestCase(10, 770)]
        [TestCase(34, 1490)]
        [TestCase(35, 1500)]
        [TestCase(60, 1500)]
        public void MedicalRevivePrice_Is500Plus30PerDepth_Cap1500(int depth, int expected)
        {
            var station = new MedicalStationEvent(new DungeonEventContext(1, depth, 0, 2), _eventConfig, _prices);
            Assert.AreEqual(expected, station.ReviveCost);
        }

        [Test]
        public void MedicalStation_RevivesDeadTeammate_ChargesOnce_AndIsIdempotentPerTransaction()
        {
            var roster = new PartyLifeRoster();
            var revives = new PartyReviveAuthority(roster, _balance);
            var dead = Player("Dead", roster, Vector2.zero);
            var buyer = Player("Buyer", roster, new Vector2(3f, 0f));
            MakeDead(dead);
            var station = new MedicalStationEvent(new DungeonEventContext(1, 3, 0, 2), _eventConfig, _prices, revives);
            var wallet = new CoinWallet(CoinDomain.Carried, 2000);
            var requester = new EventActor(wallet, participantId: "Buyer", gameObject: buyer.Go);
            var host = new LootAuthorityService(new Authority());
            host.RegisterParticipant(new LootParticipant(2, "Buyer", null, wallet, buyer.Go));

            var first = host.RequestMedicalRevive("tx-rev-1", 2, station, requester, "Dead");
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict, first.Detail);
            Assert.AreEqual(560, first.Coins, "Depth 3: 500 + 30 x 2.");
            Assert.AreEqual(2000 - 560, wallet.Balance);
            Assert.AreEqual(PlayerLifeState.Alive, dead.Life.State);
            Assert.AreEqual(Mathf.RoundToInt(dead.Health.MaxHealth * _eventConfig.ReviveHealthPercent / 100f), dead.Health.CurrentHealth);
            Assert.IsTrue(dead.Life.CanAct);
            Assert.IsTrue(dead.Health.IsInvulnerable, "Revive protection through the same seam.");

            var replay = host.RequestMedicalRevive("tx-rev-1", 2, station, requester, "Dead");
            Assert.AreSame(first, replay, "Duplicate network request: same result, no second charge.");
            Assert.AreEqual(2000 - 560, wallet.Balance);

            var again = host.RequestMedicalRevive("tx-rev-2", 2, station, requester, "Dead");
            Assert.AreEqual(LootVerdict.Rejected, again.Verdict, "Target is Alive now: refused, nothing charged.");
            Assert.AreEqual(2000 - 560, wallet.Balance);
            Assert.AreEqual(1, revives.Revives);
        }

        [Test]
        public void MedicalStation_RefusesDownedOrLivingTargets_AndNeverChargesForInvalidRequests()
        {
            var roster = new PartyLifeRoster();
            var revives = new PartyReviveAuthority(roster, _balance);
            var mate = Player("Mate", roster, Vector2.zero);
            var buyer = Player("Buyer", roster, new Vector2(3f, 0f));
            var station = new MedicalStationEvent(new DungeonEventContext(1, 1, 0, 2), _eventConfig, _prices, revives);
            var wallet = new CoinWallet(CoinDomain.Carried, 2000);
            var requester = new EventActor(wallet, participantId: "Buyer", gameObject: buyer.Go);

            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "Mate").Outcome, "Living target.");
            Kill(mate.Health);
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "Mate").Outcome, "Downed target is not a paid revive.");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "Nobody").Outcome);
            Assert.AreEqual(2000, wallet.Balance, "Invalid requests are never charged.");

            mate.Life.Tick(20.01f);
            var poor = new EventActor(new CoinWallet(CoinDomain.Carried, 100), participantId: "Buyer", gameObject: buyer.Go);
            Assert.AreEqual(DungeonEventOutcome.InsufficientFunds, station.RequestRevive(poor, "Mate").Outcome);
            Assert.AreEqual(PlayerLifeState.Dead, mate.Life.State);

            // Client processes never apply a revive.
            DamageAuthority.LocalIsAuthoritative = false;
            Assert.AreEqual(DungeonEventOutcome.Unavailable, station.RequestRevive(requester, "Mate").Outcome, "Refused on a client (refunded, nothing applied).");
            Assert.AreEqual(2000, wallet.Balance);
            Assert.AreEqual(PlayerLifeState.Dead, mate.Life.State);
        }

        [Test]
        public void BothSources_CannotDoubleRevive_TheSameDeadPlayer()
        {
            var roster = new PartyLifeRoster();
            var revives = new PartyReviveAuthority(roster, _balance);
            var dead = Player("Dead", roster, Vector2.zero, revives);
            var user = Player("User", roster, new Vector2(0.5f, 0f), revives);
            MakeDead(dead);
            EquipDefibrillator(user);
            var station = new MedicalStationEvent(new DungeonEventContext(1, 1, 0, 2), _eventConfig, _prices, revives);
            var wallet = new CoinWallet(CoinDomain.Carried, 2000);
            var heals = 0;
            dead.Health.Healed += _ => heals++;

            Assert.IsTrue(user.Consumables.TryUse());
            user.Consumables.UseAction.Tick(0.01f);
            Assert.AreEqual(PlayerLifeState.Alive, dead.Life.State);
            var paid = station.RequestRevive(new EventActor(wallet, participantId: "User", gameObject: user.Go), "Dead");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, paid.Outcome, "Already Alive: the paid revive is refused.");
            Assert.AreEqual(2000, wallet.Balance);
            Assert.AreEqual(1, heals, "Healed exactly once.");
            Assert.AreEqual(1, revives.Revives);
        }
    }
}
