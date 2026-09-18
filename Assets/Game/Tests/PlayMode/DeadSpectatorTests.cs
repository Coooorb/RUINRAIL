using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 104: Dead state, teammate-follow spectator, immediate team-wipe failure and dead extraction rules (84/85/15).</summary>
    public class DeadSpectatorTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        private sealed class Authority : IAuthorityContext
        {
            public NetworkRole Role => NetworkRole.Host;
            public bool IsAuthority => true;
        }

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("Spawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(Resolve);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (GameObject go, FakePlayerInputReader reader, PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var reader = new FakePlayerInputReader();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = reader, BalanceConfig = _balance, Position = position, LifeRoster = roster });
            _created.Add(go);
            return (go, reader, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        private ExpeditionService NewExpedition(out PlayerProfile profile)
        {
            profile = new PlayerProfile();
            profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = System.Array.Empty<InventorySnapshot.Entry>()
            };
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        // ---- Acceptance 1: Dead cannot move/attack/drop; spectates only living teammates ----

        [Test]
        public void Dead_CannotAct_AndDropsNothing_LocallyOrThroughTheHost()
        {
            var roster = new PartyLifeRoster();
            var (goA, readerA, a, healthA) = Player("A", roster, Vector2.zero);
            Player("B", roster, new Vector2(4f, 0f));
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var rifle = new ItemInstance("weapon_p9_ranger");
            Assert.IsTrue(inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon));
            var receiver = goA.GetComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(new CoinWallet(CoinDomain.Carried));
            receiver.SetDropService(new ItemDropService(_spawner));
            var host = new LootAuthorityService(new Authority());
            host.SetDropService(new ItemDropService(_spawner));
            host.RegisterParticipant(new LootParticipant(1, "a", new BackpackContainer(inventory), new CoinWallet(CoinDomain.Carried), goA));

            Kill(healthA);
            a.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State, "Bleedout -> Dead.");
            Assert.IsFalse(a.CanAct);
            Assert.IsTrue(goA.GetComponent<PlayerMovement>().IsOverridden, "Dead: the life state owns the body (no movement).");
            Assert.IsFalse(goA.GetComponent<PlayerDash>().TryStartDash(Vector2.right));

            Assert.IsFalse(receiver.TryDrop(rifle.InstanceId).Success, "No local drop while Dead.");
            var hostDrop = host.RequestDrop("tx-dead-drop", 1, receiver.CarriedContainers, rifle.InstanceId, 1, Vector2.zero);
            Assert.AreEqual(LootVerdict.Rejected, hostDrop.Verdict, "The host refuses the dead participant's drop.");
            var pickup = _spawner.CreateItemPickup(new Vector2(0.2f, 0f));
            pickup.Hold(new ItemInstance("weapon_p9_ranger"), ItemCategory.Weapon);
            Assert.AreEqual(LootVerdict.Rejected, host.RequestPickup("tx-dead-pick", 1, pickup).Verdict, "...and its pickups.");
            Assert.IsFalse(pickup.IsConsumed);
            Assert.AreSame(rifle, inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "Carried gear stays with the dead player: nothing on the ground for teammates.");
            Assert.AreEqual(1, _ground.Count, "Only the test pickup exists on the ground; no death drop.");
        }

        [Test]
        public void Spectator_FollowsOnlyLivingTeammates_CyclesInRosterOrder_NeverFreeRoams()
        {
            var roster = new PartyLifeRoster();
            var (goA, readerA, a, healthA) = Player("A", roster, Vector2.zero);
            var (goB, _, b, healthB) = Player("B", roster, new Vector2(10f, 0f));
            var (goC, _, c, _) = Player("C", roster, new Vector2(20f, 0f));
            var follow = goA.GetComponent<DeadSpectatorFollow>();
            Assert.IsFalse(follow.IsSpectating);
            Assert.AreSame(goA.transform, follow.FollowTarget, "Alive: follows itself.");

            Kill(healthA);
            Assert.IsFalse(follow.IsSpectating, "Downed is not Dead: no spectating yet.");
            a.Tick(20.01f);
            Assert.IsTrue(follow.IsSpectating);
            Assert.AreSame(goB.transform, follow.FollowTarget, "First living teammate in roster order.");
            Assert.AreEqual((Vector2)goB.transform.position, follow.FollowPosition);

            readerA.RaiseInteract();
            Assert.AreSame(goC.transform, follow.FollowTarget, "Interact cycles to the next teammate.");
            readerA.RaiseInteract();
            Assert.AreSame(goB.transform, follow.FollowTarget, "...and wraps around.");

            // A teammate that dies is no longer a valid target; a Downed one still is (living, revivable).
            Kill(healthB);
            Assert.AreEqual(PlayerLifeState.Downed, b.State);
            CollectionAssert.AreEqual(new[] { b, c }, follow.Selector.Candidates);
            b.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, b.State);
            CollectionAssert.AreEqual(new[] { c }, follow.Selector.Candidates);
            follow.Selector.Refresh();
            Assert.AreSame(goC.transform, follow.FollowTarget);

            // No free position: the only way to move the view is to pick another teammate.
            var members = typeof(DeadSpectatorFollow).GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly).Select(m => m.Name.ToLowerInvariant()).ToList();
            Assert.IsFalse(members.Any(n => n.Contains("setposition") || n.Contains("freeroam") || n.Contains("pan") || n.Contains("scroll")), "No free-roam API.");
        }

        // ---- Acceptance 2: team wipe = immediate one-time failure ----

        [Test]
        public void TeamWipe_FailsTheExpeditionImmediately_Once_WithoutWaitingForBleedouts()
        {
            var roster = new PartyLifeRoster();
            var (_, _, a, healthA) = Player("A", roster, Vector2.zero);
            var (_, _, b, healthB) = Player("B", roster, new Vector2(4f, 0f));
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 5, Biome.RuinedMetro, 2);
            using var binding = new PartyExpeditionBinding(expedition, roster, a);
            var ended = 0;
            expedition.ExpeditionEnded += _ => ended++;

            Kill(healthA);
            Assert.IsTrue(expedition.IsExpeditionActive, "One Downed player with an Alive teammate: the run continues.");
            Kill(healthB);
            Assert.IsFalse(expedition.IsExpeditionActive, "Nobody Alive: failed at once, A's 20 s bleedout is still running.");
            Assert.AreEqual(PlayerLifeState.Downed, a.State);
            Assert.AreEqual(ExpeditionOutcome.Failed, expedition.LastSummary.Outcome);
            Assert.AreEqual(1, ended);

            a.Tick(25f);
            Assert.AreEqual(PlayerLifeState.Dead, a.State);
            Assert.AreEqual(1, ended, "Later deaths never re-fail the closed transaction.");
            Assert.AreEqual(1, binding.WipeFailures);
        }

        // ---- Acceptance 4: Return/Descend rules for a Dead player ----

        [Test]
        public void DeadPlayer_DescendsAsSpectator_ButLosesAtRiskStateWhenThePartyReturns()
        {
            var roster = new PartyLifeRoster();
            var (_, _, local, healthLocal) = Player("Local", roster, Vector2.zero);
            Player("Mate", roster, new Vector2(4f, 0f));
            var expedition = NewExpedition(out var profile);
            var state = expedition.Start(profile, 5, Biome.RuinedMetro, 2);
            using var binding = new PartyExpeditionBinding(expedition, roster, local);
            expedition.AddCarriedCoins(120);
            var rifleId = state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;

            Kill(healthLocal);
            local.Tick(20.01f);
            Assert.AreEqual(PlayerLifeState.Dead, local.State);
            Assert.IsTrue(expedition.IsLocalPlayerDead);

            // The living party descends: the dead player rides along (state untouched, still at risk).
            expedition.RecordBossDefeated(0);
            expedition.Descend();
            Assert.AreEqual(2, expedition.State.Depth);
            Assert.IsTrue(expedition.IsExpeditionActive);
            Assert.AreEqual(120, expedition.State.CarriedCoins);
            Assert.IsNotNull(expedition.State.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon));

            // The party Returns while this player is still Dead: at-risk gear/loot/coins are lost; XP was already committed.
            var bankedBefore = profile.BankedCoins;
            var summary = expedition.ReturnWithParty();
            Assert.AreEqual(ExpeditionOutcome.Failed, summary.Outcome);
            Assert.AreEqual(120, summary.CoinsLost);
            Assert.AreEqual(bankedBefore, profile.BankedCoins, "Nothing banked.");
            Assert.IsNull(profile.SafeLoadout, "No carried gear secured.");
            CollectionAssert.Contains(summary.LostItems.Select(l => l.InstanceId).ToList(), rifleId);
            Assert.AreSame(summary, expedition.ReturnWithParty(), "Closed transaction replays the same summary.");
        }

        [Test]
        public void LivingPlayer_ReturnsNormally_AndRevivedPlayerSecuresGear()
        {
            var roster = new PartyLifeRoster();
            var (_, _, local, healthLocal) = Player("Local", roster, Vector2.zero);
            Player("Mate", roster, new Vector2(4f, 0f));
            var expedition = NewExpedition(out var profile);
            expedition.Start(profile, 5, Biome.RuinedMetro, 2);
            using var binding = new PartyExpeditionBinding(expedition, roster, local);
            expedition.AddCarriedCoins(80);

            // Downed (not Dead) at Return time still counts as living for extraction; a revived player likewise.
            Kill(healthLocal);
            Assert.IsFalse(expedition.IsLocalPlayerDead);
            Assert.IsTrue(local.ReturnToAlive(30));
            var summary = expedition.ReturnWithParty();
            Assert.AreEqual(ExpeditionOutcome.Extracted, summary.Outcome);
            Assert.AreEqual(80, summary.CoinsExtracted);
            Assert.IsNotNull(profile.SafeLoadout);
        }
    }
}
