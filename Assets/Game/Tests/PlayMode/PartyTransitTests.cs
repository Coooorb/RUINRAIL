using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 107: the party vote runs over the life roster — living voters only, one party transition, dead-return warning, no split party.</summary>
    public class PartyTransitTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
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

        private (PlayerLifeStateComponent life, HealthComponent health) Player(string name, PartyLifeRoster roster, Vector2 position)
        {
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = name, IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, Position = position, LifeRoster = roster, ParticipantId = name });
            _created.Add(go);
            return (go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<HealthComponent>());
        }

        private ExpeditionService NewExpedition(string localId, out PlayerProfile profile)
        {
            profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = System.Array.Empty<InventorySnapshot.Entry>()
                }
            };
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance, null, localId);
        }

        private static void Kill(HealthComponent health) => health.TryApplyDamage(new DamageRequest(999999));

        [Test]
        public void BossDefeated_OpensAVoteForLivingPlayersOnly_UnanimousDescendMovesTheWholeParty()
        {
            var roster = new PartyLifeRoster();
            var (a, _) = Player("A", roster, Vector2.zero);
            var (b, _) = Player("B", roster, new Vector2(3f, 0f));
            var (c, healthC) = Player("C", roster, new Vector2(6f, 0f));
            Kill(healthC);
            c.Tick(20.01f);
            Assert.IsTrue(c.IsDead);

            var expedition = NewExpedition("A", out var profile);
            expedition.Start(profile, 9, Biome.RuinedMetro, 3);
            using var binding = new PartyExpeditionBinding(expedition, roster, a);
            var transit = expedition.RecordBossDefeated(0);

            CollectionAssert.AreEquivalent(new[] { "A", "B" }, transit.LivingPlayers, "Dead C has no vote.");
            CollectionAssert.AreEqual(new[] { "C" }, transit.DeadPlayers);
            Assert.IsTrue(transit.RequiresReturnWarning, "86: warn before Return while C is Dead.");
            StringAssert.Contains("C is Dead", transit.ReturnWarning.Message);
            Assert.IsFalse(transit.Submit("C", TransitChoice.ReturnToShelter), "A dead vote is unavailable.");

            Assert.IsFalse(transit.Submit("A", TransitChoice.DescendDeeper));
            Assert.AreEqual(1, expedition.State.Depth, "No transition until everyone living agreed.");
            Assert.IsTrue(transit.Submit("B", TransitChoice.DescendDeeper));
            Assert.AreEqual(2, expedition.State.Depth, "One party transition: Depth 2 for everyone, the dead spectator included.");
            Assert.IsTrue(expedition.IsExpeditionActive);
            Assert.AreEqual(1, transit.Resolutions);
        }

        [Test]
        public void OneReturn_ReturnsTheParty_LocalDeadLosesAtRisk_LivingExtracts()
        {
            var roster = new PartyLifeRoster();
            var (a, healthA) = Player("A", roster, Vector2.zero);
            var (b, _) = Player("B", roster, new Vector2(3f, 0f));
            Player("C", roster, new Vector2(6f, 0f));

            // Peer A (local player A) and peer B (local player B) each run their own transaction; A dies before the vote.
            var expeditionA = NewExpedition("A", out var profileA);
            expeditionA.Start(profileA, 9, Biome.RuinedMetro, 3);
            expeditionA.AddCarriedCoins(90);
            using var bindingA = new PartyExpeditionBinding(expeditionA, roster, a);
            var expeditionB = NewExpedition("B", out var profileB);
            expeditionB.Start(profileB, 9, Biome.RuinedMetro, 3);
            expeditionB.AddCarriedCoins(70);
            using var bindingB = new PartyExpeditionBinding(expeditionB, roster, b);

            Kill(healthA);
            a.Tick(20.01f);
            var transitA = expeditionA.RecordBossDefeated(0);
            var transitB = expeditionB.RecordBossDefeated(0);
            CollectionAssert.AreEquivalent(new[] { "B", "C" }, transitB.LivingPlayers);

            // Living C votes Return: the party returns; B's Descend is irrelevant.
            transitB.Submit("B", TransitChoice.DescendDeeper);
            transitA.Submit("B", TransitChoice.DescendDeeper);
            Assert.IsTrue(transitB.Submit("C", TransitChoice.ReturnToShelter));
            Assert.IsTrue(transitA.Submit("C", TransitChoice.ReturnToShelter));

            Assert.AreEqual(ExpeditionOutcome.Extracted, expeditionB.LastSummary.Outcome, "Living B extracts normally.");
            Assert.AreEqual(70, expeditionB.LastSummary.CoinsExtracted);
            Assert.AreEqual(ExpeditionOutcome.Failed, expeditionA.LastSummary.Outcome, "Dead A loses its at-risk state on the party Return.");
            Assert.AreEqual(90, expeditionA.LastSummary.CoinsLost);
            Assert.IsNull(profileA.SafeLoadout);
        }

        [Test]
        public void AVoterDyingOrExpiringDuringTheVote_DoesNotDeadlock()
        {
            var roster = new PartyLifeRoster();
            var (a, _) = Player("A", roster, Vector2.zero);
            var (b, healthB) = Player("B", roster, new Vector2(3f, 0f));
            var (c, _) = Player("C", roster, new Vector2(6f, 0f));
            var expedition = NewExpedition("A", out var profile);
            expedition.Start(profile, 9, Biome.RuinedMetro, 3);
            using var binding = new PartyExpeditionBinding(expedition, roster, a);
            var transit = expedition.RecordBossDefeated(0);

            transit.Submit("A", TransitChoice.DescendDeeper);
            transit.Submit("C", TransitChoice.DescendDeeper);
            Assert.AreEqual(TransitDecisionState.Open, transit.State, "Waiting for B (perhaps disconnected, in reconnect grace).");

            // B never answers: the grace expires and the authority declares B Dead -> B leaves the vote -> unanimity of A and C.
            Assert.IsTrue(b.MarkDeadByAuthority("reconnect_grace_expired"));
            Assert.AreEqual(TransitDecisionState.Resolved, transit.State);
            Assert.AreEqual(TransitChoice.DescendDeeper, transit.Result);
            Assert.AreEqual(2, expedition.State.Depth);
            Assert.IsTrue(b.IsDead);
        }

        [Test]
        public void RevivedPlayer_JoinsTheOpenVote()
        {
            var roster = new PartyLifeRoster();
            var (a, _) = Player("A", roster, Vector2.zero);
            var (b, healthB) = Player("B", roster, new Vector2(3f, 0f));
            Kill(healthB);
            b.Tick(20.01f);
            var expedition = NewExpedition("A", out var profile);
            expedition.Start(profile, 9, Biome.RuinedMetro, 2);
            using var binding = new PartyExpeditionBinding(expedition, roster, a);
            var transit = expedition.RecordBossDefeated(0);
            CollectionAssert.AreEquivalent(new[] { "A" }, transit.LivingPlayers);

            Assert.IsTrue(b.ReturnToAlive(30), "Defibrillator at the boss room.");
            CollectionAssert.AreEquivalent(new[] { "A", "B" }, transit.LivingPlayers);
            Assert.IsFalse(transit.RequiresReturnWarning);
            Assert.IsFalse(transit.Submit("A", TransitChoice.DescendDeeper), "B must agree now.");
            Assert.IsTrue(transit.Submit("B", TransitChoice.DescendDeeper));
            Assert.AreEqual(2, expedition.State.Depth);
        }
    }
}
