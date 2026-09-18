using System;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using UnityEditor;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 101 — 83_COOP_SCALING multipliers fixed at expedition start; damage per hit never party-scales.</summary>
    public sealed class CoopScalingTests
    {
        private DepthScalingConfig _config;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        // ---- Acceptance 1: exact multipliers ----

        [TestCase(1, 1.00f, 1.00f, 1.00f, 10)]
        [TestCase(2, 1.40f, 1.20f, 1.65f, 14)]
        [TestCase(3, 1.75f, 1.35f, 2.20f, 18)]
        public void ApprovedTable_Threat_NormalHp_BossHp_Damage_Cap(int party, float threat, float normalHp, float bossHp, int cap)
        {
            Assert.AreEqual(threat, PartyScaling.ThreatMultiplier(party), 0.0001f);
            Assert.AreEqual(normalHp, PartyScaling.NormalEnemyHealthMultiplier(party), 0.0001f);
            Assert.AreEqual(bossHp, PartyScaling.BossHealthMultiplier(party), 0.0001f);
            Assert.AreEqual(1f, PartyScaling.EnemyDamageMultiplier(party), 0.0001f, "Enemy damage always 100%.");
            Assert.AreEqual(cap, PartyScaling.ActiveNormalCap(party));
        }

        [Test]
        public void PartySize_IsClampedToOneThroughThree()
        {
            Assert.AreEqual(PartyScaling.ThreatMultiplier(1), PartyScaling.ThreatMultiplier(0));
            Assert.AreEqual(PartyScaling.ThreatMultiplier(3), PartyScaling.ThreatMultiplier(7));
            Assert.AreEqual(PartyScaling.BossHealthMultiplier(3), PartyScaling.BossHealthMultiplier(99));
            Assert.AreEqual(1, new ExpeditionState(1, Biome.RuinedMetro, null, null, 0).StartingPartySize);
            Assert.AreEqual(3, new ExpeditionState(1, Biome.RuinedMetro, null, null, 5).StartingPartySize);
        }

        // ---- Acceptance 2: per-hit damage identical across party sizes ----

        [Test]
        public void ScaledDamageBand_AndRoller_AreIdenticalForEveryPartySize()
        {
            foreach (var depth in new[] { 1, 5, 12, 30 })
            {
                var band = DepthScaling.ScaledDamage(18, 22, depth, _config);
                // The API has no party parameter at all: the same band is what every party size gets.
                var rollers = Enumerable.Range(1, 3).Select(_ => new DepthScaledDamageRoller(new FixedRoller(20), depth, _config)).ToList();
                var results = rollers.Select(r => r.Roll(18, 22)).Distinct().ToList();
                Assert.AreEqual(1, results.Count, $"depth {depth}");
                Assert.GreaterOrEqual(results[0], band.min);
                Assert.LessOrEqual(results[0], band.max);
            }
        }

        private sealed class FixedRoller : RuinRail.Gameplay.Combat.IDamageRoller
        {
            private readonly int _value;
            public FixedRoller(int value) { _value = value; }
            public int Roll(int min, int max) => Math.Clamp(_value, min, max);
        }

        // ---- Requirement 3: composition order base -> depth -> party, rounded once ----

        [Test]
        public void Health_ComposesDepthThenParty_RoundedOnce_NeverBelowOne()
        {
            // Documented policy (DepthScaling.ScaledHealth): base x depth curve x party multiplier, rounded ONCE to the
            // nearest integer (half away from zero), floor 1. Verified against the formula across bases/depths/parties.
            foreach (var baseHealth in new[] { 1, 7, 30, 33, 45, 300, 1150 })
            {
                foreach (var depth in new[] { 1, 4, 10, 27 })
                {
                    foreach (var party in new[] { 1, 2, 3 })
                    {
                        AssertRoundedOnce(baseHealth * DepthScaling.HealthMultiplier(depth, _config) * PartyScaling.NormalEnemyHealthMultiplier(party), DepthScaling.ScaledHealth(baseHealth, depth, party, false, _config), $"normal base {baseHealth} depth {depth} party {party}");
                        AssertRoundedOnce(baseHealth * DepthScaling.HealthMultiplier(depth, _config) * PartyScaling.BossHealthMultiplier(party), DepthScaling.ScaledHealth(baseHealth, depth, party, true, _config), $"boss base {baseHealth} depth {depth} party {party}");
                    }
                }
            }

            Assert.AreEqual(1, DepthScaling.ScaledHealth(0, 1, 3, true, _config));
            // A case where rounding twice would diverge: 30 x 1.24 (D4) = 37.2 -> 37, x 1.20 = 44.4 -> 44; rounded once: 44.64 -> 45.
            Assert.AreEqual(45, DepthScaling.ScaledHealth(30, 4, 2, false, _config), "Single rounding after both multipliers.");
        }

        /// <summary>The product is rounded once to the nearest integer (floor 1); an exact .5 product may land either way by float precision.</summary>
        private static void AssertRoundedOnce(double product, int actual, string label)
        {
            var frac = product - Math.Floor(product);
            if (Math.Abs(frac - 0.5) < 0.002)
            {
                Assert.IsTrue(actual == Math.Max(1, (int)Math.Floor(product)) || actual == Math.Max(1, (int)Math.Ceiling(product)), $"{label}: midpoint {product} -> {actual}");
                return;
            }

            Assert.AreEqual(Math.Max(1, (int)Math.Round(product)), actual, $"{label}: {product}");
        }

        // ---- Requirement 4: Elites use the normal curve ----

        [Test]
        public void Elite_UsesNormalEnemyHealthCurve_NotTheBossMultiplier()
        {
            // 88: Railguard base 300 at Depth 1, trio -> 405 (x1.35), never 660 (x2.20).
            Assert.AreEqual(405, DepthScaling.ScaledHealth(300, 1, 3, false, _config));
            Assert.AreEqual(660, DepthScaling.ScaledHealth(300, 1, 3, true, _config));
            var eliteSource = System.IO.File.ReadAllText("Assets/Game/Scripts/Dungeon/Runtime/EliteEngagement.cs");
            StringAssert.Contains("DepthScaling.ScaledHealth(_definition.BaseHealth, _depth, _partySize, false, _scaling)", eliteSource, "EliteEngagement scales on the normal curve.");
            Assert.AreEqual(1, PartyScaling.ActiveEliteCap);
        }

        // ---- Acceptance 3/Requirement 5: captured at start, never rubber-bands ----

        [Test]
        public void ExpeditionStart_CapturesPartySize_AndLeavingMembersNeverLowerIt()
        {
            var lobby = new PartyLobby(0, Resolve);
            for (ulong c = 0; c < 3; c++)
            {
                lobby.Join(c, "p" + c);
                lobby.SetLoadout(c, new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = Array.Empty<InventorySnapshot.Entry>()
                });
                lobby.SetReady(c, true);
            }

            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(0, 77, Biome.RuinedMetro, out var snapshot));
            var slot = SaveSlotService.CreateNew();
            slot.Profile.SafeLoadout = snapshot.Members[0].Loadout;
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            var state = new ExpeditionStartCoordinator().Apply(snapshot, expedition, slot.Profile);
            Assert.AreEqual(3, state.StartingPartySize);

            // Two members disconnect / die mid-run: the captured scaling is unchanged for every downstream consumer.
            lobby.Leave(1);
            lobby.Leave(2);
            Assert.AreEqual(1, lobby.Members.Count);
            Assert.AreEqual(3, state.StartingPartySize);
            Assert.AreEqual(3, snapshot.PartySize);
            var context = new DungeonRuntimeContext(state.RunSeed, state.Depth, state.StartingPartySize, Array.Empty<RuinRail.Gameplay.Enemies.EnemyDefinition>(), null);
            Assert.AreEqual(3, context.PartySize);
            Assert.AreEqual(1.75f, PartyScaling.ThreatMultiplier(context.PartySize), 0.0001f);

            // Descending keeps it too (no re-read from the roster on a new depth).
            expedition.RecordBossDefeated(0);
            expedition.Descend();
            Assert.AreEqual(2, expedition.State.Depth);
            Assert.AreEqual(3, expedition.State.StartingPartySize);
            Assert.AreEqual(1, new ExpeditionService(Resolve, t => null, _ammoBalance).Start(SaveSlotService.CreateNew().Profile, 1, Biome.RuinedMetro).StartingPartySize, "Solo default.");
        }

        // ---- Acceptance 4: encounter threat respects the party multiplier and caps ----

        [Test]
        public void EncounterBudget_UsesPartyThreatMultiplier_AndActiveCap()
        {
            var archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Enemies.EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            foreach (var depth in new[] { 1, 8, 20 })
            {
                var (min, max) = ThreatBudgetTable.SoloBudget(depth);
                foreach (var party in new[] { 1, 2, 3 })
                {
                    var plan = EncounterDirector.Compose(new EncounterContext(4242, depth, party, Biome.RuinedMetro, 2), archetypes);
                    var m = PartyScaling.ThreatMultiplier(party);
                    Assert.AreEqual(min * m, plan.BudgetMin, 0.001f, $"depth {depth} party {party} min");
                    Assert.AreEqual(max * m, plan.BudgetMax, 0.001f, $"depth {depth} party {party} max");
                    Assert.GreaterOrEqual(plan.TargetThreat, plan.BudgetMin - 0.001f);
                    Assert.LessOrEqual(plan.TargetThreat, plan.BudgetMax + 0.001f);
                    Assert.LessOrEqual(plan.TotalCount, PartyScaling.ActiveNormalCap(party));
                    Assert.AreEqual(PartyScaling.ActiveNormalCap(party), plan.ActiveCap);
                    Assert.LessOrEqual(plan.TotalThreat, plan.TargetThreat + 0.001f);
                }
            }
        }
    }
}
