using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Loot;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 072 — centralised endless-depth scaling with the approved checkpoints and caps.</summary>
    public class DepthScalingTests
    {
        private DepthScalingConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            Assert.IsNotNull(_config, "Approved config asset must exist.");
        }

        // ---- Acceptance 1: exact checkpoints ----

        [TestCase(1, 1.00f)]
        [TestCase(2, 1.08f)]
        [TestCase(3, 1.16f)]
        [TestCase(5, 1.32f)]
        [TestCase(10, 1.70f)]
        [TestCase(20, 2.35f)]
        [TestCase(30, 2.90f)]
        [TestCase(50, 3.80f)]
        [TestCase(100, 5.50f)]
        public void HealthCheckpoints_AreExact(int depth, float expected)
        {
            Assert.AreEqual(expected, DepthScaling.HealthMultiplier(depth, _config), 0.0001f);
            Assert.AreEqual(expected, DepthScaling.HealthMultiplier(depth), 0.0001f, "Built-in defaults match the asset.");
        }

        [TestCase(1, 1.00f)]
        [TestCase(5, 1.15f)]
        [TestCase(10, 1.30f)]
        [TestCase(20, 1.55f)]
        [TestCase(30, 1.75f)]
        [TestCase(50, 2.05f)]
        [TestCase(100, 2.60f)]
        public void DamageCheckpoints_AreExact(int depth, float expected)
        {
            Assert.AreEqual(expected, DepthScaling.DamageMultiplier(depth, _config), 0.0001f);
        }

        [TestCase(1, 5)]
        [TestCase(2, 5)]
        [TestCase(3, 10)]
        [TestCase(5, 10)]
        [TestCase(6, 15)]
        [TestCase(10, 15)]
        [TestCase(11, 20)]
        [TestCase(20, 20)]
        [TestCase(21, 25)]
        [TestCase(40, 25)]
        [TestCase(500, 25)]
        public void EliteChanceBands_AreExact_AndCapAt25(int depth, int expected)
        {
            Assert.AreEqual(expected, DepthScaling.EliteChancePercent(depth, _config));
        }

        [Test]
        public void Interpolation_IsLinearBetweenCheckpoints_AndDamageGrowsSlowerThanHealth()
        {
            Assert.AreEqual(1.24f, DepthScaling.HealthMultiplier(4, _config), 0.0001f, "Between 116% (D3) and 132% (D5).");
            Assert.AreEqual(2.025f, DepthScaling.HealthMultiplier(15, _config), 0.0001f);
            Assert.AreEqual(1.1125f, DepthScaling.DamageMultiplier(4, _config), 0.0001f);
            for (var depth = 2; depth <= 200; depth++)
            {
                Assert.LessOrEqual(DepthScaling.DamageMultiplier(depth, _config), DepthScaling.HealthMultiplier(depth, _config), $"depth {depth}");
                Assert.GreaterOrEqual(DepthScaling.HealthMultiplier(depth, _config), DepthScaling.HealthMultiplier(depth - 1, _config), "Monotonic.");
            }
        }

        // ---- Acceptance 2: deep values respect caps ----

        [Test]
        public void DeepValues_RespectAttackSpeedMovementThreatAndEliteCaps()
        {
            foreach (var depth in new[] { 100, 250, 1000 })
            {
                Assert.AreEqual(5.5f, DepthScaling.HealthMultiplier(depth, _config), 0.0001f, "The last checkpoint holds past Depth 100.");
                Assert.AreEqual(2.6f, DepthScaling.DamageMultiplier(depth, _config), 0.0001f);
            }

            foreach (var depth in new[] { 50, 100, 250, 1000 })
            {
                Assert.LessOrEqual(DepthScaling.AttackSpeedMultiplier(depth, _config), EnemyController.MaxAttackSpeedMultiplier);
                Assert.LessOrEqual(DepthScaling.MovementSpeedMultiplier(depth, _config), DepthScaling.MaxMovementSpeedMultiplier);
                Assert.AreEqual(25, DepthScaling.EliteChancePercent(depth, _config));
                Assert.AreEqual((14f, 18f), ThreatBudgetTable.SoloBudget(depth), "Threat budget caps.");
            }

            Assert.AreEqual(1.0f, DepthScaling.AttackSpeedMultiplier(1, _config), 0.0001f);
            Assert.AreEqual(1.05f, DepthScaling.AttackSpeedMultiplier(25, _config), 0.01f, "Slight: about +5% halfway to the cap.");
            Assert.AreEqual(1.1f, DepthScaling.AttackSpeedMultiplier(50, _config), 0.0001f, "About +10% by very deep play, then capped.");
            Assert.AreEqual(1.05f, DepthScaling.MovementSpeedMultiplier(50, _config), 0.0001f);
        }

        // ---- Acceptance 3: damage is unaffected by party size; health composes explicitly ----

        [Test]
        public void EnemyDamage_IgnoresPartySize_WhileHealthComposesDepthThenParty()
        {
            var band = DepthScaling.ScaledDamage(6, 8, 10, _config);
            Assert.AreEqual((8, 10), band, "6-8 x 130% -> 8-10 (rounded integers).");
            Assert.IsNull(typeof(DepthScaling).GetMethod("ScaledDamage").GetParameters().FirstOrDefault(p => p.Name.ToLowerInvariant().Contains("party")), "No party parameter can exist on damage.");
            Assert.AreEqual(1f, PartyScaling.EnemyDamageMultiplier(2));
            Assert.AreEqual(1f, PartyScaling.EnemyDamageMultiplier(3));

            Assert.AreEqual(30, DepthScaling.ScaledHealth(30, 1, 1, false, _config));
            Assert.AreEqual(51, DepthScaling.ScaledHealth(30, 10, 1, false, _config), "30 x 1.70.");
            Assert.AreEqual(61, DepthScaling.ScaledHealth(30, 10, 2, false, _config), "30 x 1.70 x 1.20 = 61.2 -> 61.");
            Assert.AreEqual(69, DepthScaling.ScaledHealth(30, 10, 3, false, _config), "30 x 1.70 x 1.35 = 68.85 -> 69.");
            Assert.AreEqual(1050, DepthScaling.ScaledHealth(1050, 1, 1, true, _config));
            Assert.AreEqual(2310, DepthScaling.ScaledHealth(1050, 1, 3, true, _config), "Boss: x2.20 for a trio.");
            Assert.AreEqual(1, DepthScaling.ScaledHealth(0, 1, 1, false, _config), "Never below 1.");
        }

        // ---- Acceptance 4: deterministic/pure; loot and coin hooks ----

        [Test]
        public void Service_IsPure_AndExposesLootAndCoinHooks()
        {
            for (var i = 0; i < 3; i++)
            {
                Assert.AreEqual(DepthScaling.HealthMultiplier(37, _config), DepthScaling.HealthMultiplier(37, _config));
                Assert.AreEqual(DepthScaling.EliteChancePercent(37, _config), DepthScaling.EliteChancePercent(37, _config));
            }

            var table = AssetDatabase.LoadAssetAtPath<RarityTableDefinition>("Assets/Game/ScriptableObjects/Loot/RarityTable_Standard.asset");
            CollectionAssert.AreEqual(new[] { 600, 310, 80, 9, 1 }, DepthScaling.LootRarityWeights(table, 1), "59 loot baseline at Depth 1.");
            CollectionAssert.AreEqual(new[] { 120, 260, 390, 215, 15 }, DepthScaling.LootRarityWeights(table, 30));
            CollectionAssert.AreEqual(DepthScaling.LootRarityWeights(table, 30), DepthScaling.LootRarityWeights(table, 80), "30+ holds: Legendary stays rare deep.");
            var economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            Assert.AreEqual(1f, DepthScaling.CoinRewardMultiplier(economy, 1));
            Assert.LessOrEqual(DepthScaling.CoinRewardMultiplier(economy, 500), economy.CoinRewardMultiplier(500), "Delegates to the capped economy curve.");
        }
    }
}
