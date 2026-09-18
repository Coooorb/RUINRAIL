using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEditor;

namespace RuinRail.Tests
{
    public class EliteDefinitionTests
    {
        private const string TunnelStalkerPath = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_TunnelStalker.asset";

        [Test]
        public void TunnelStalker_UsesApprovedBaselines_AndDocumentedThreeAttackMoveset()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(TunnelStalkerPath);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_tunnel_stalker", elite.Id);
            Assert.AreEqual("Tunnel Stalker", elite.DisplayName);
            Assert.AreEqual(Biome.RuinedMetro, elite.Biome);
            Assert.AreEqual(350, elite.BaseHealth);
            Assert.AreEqual(3.6f, elite.MoveSpeed, 0.001f);
            Assert.AreEqual(250, elite.BaseXp);
            Assert.AreEqual(24, elite.StrongestAttackDamageMin);
            Assert.AreEqual(30, elite.StrongestAttackDamageMax);

            Assert.AreEqual(3, elite.Moveset.Count, "Lunge, Claw Combo, Tunnel Rush.");
            CollectionAssert.AreEquivalent(new[] { "Lunge", "Claw Combo", "Tunnel Rush" }, elite.Moveset.Select(a => a.DisplayName));

            var rush = elite.Moveset.Single(a => a.DisplayName == "Tunnel Rush");
            Assert.AreEqual(AttackMotion.Dash, rush.Motion);
            Assert.AreEqual(24, rush.DamageMin);
            Assert.AreEqual(30, rush.DamageMax);
            Assert.Greater(rush.TelegraphSeconds, 0.5f, "Tunnel Rush is clearly telegraphed.");
            Assert.Greater(rush.RecoverySeconds, 1f, "Tunnel Rush has a recovery window.");

            var claw = elite.Moveset.Single(a => a.DisplayName == "Claw Combo");
            Assert.AreEqual(AttackMotion.Stationary, claw.Motion);
            Assert.AreEqual(3, claw.HitCount);

            var lunge = elite.Moveset.Single(a => a.DisplayName == "Lunge");
            Assert.AreEqual(AttackMotion.Dash, lunge.Motion);
            Assert.Less(lunge.DashDistance, rush.DashDistance);

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} cannot exceed the strongest attack target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
                Assert.IsTrue(attack.MinTriggerRange <= attack.MaxTriggerRange);
            }

            Assert.AreEqual(rush.DamageMax, elite.Moveset.Max(a => a.DamageMax), "The documented strongest attack is the strongest.");
        }

        [Test]
        public void EliteModel_HasNoRandomModifierOrPhaseConcept()
        {
            var eliteType = typeof(EliteDefinition);
            Assert.IsFalse(eliteType.GetProperties().Any(p => p.Name.Contains("Modifier") || p.Name.Contains("Affix") || p.Name.Contains("Phase")));
            Assert.IsFalse(typeof(EliteController).GetProperties().Any(p => p.Name.Contains("Phase")));
            CollectionAssert.AreEquivalent(new[] { "Idle", "Chase", "Telegraph", "Attacking", "Recovery", "Dead" }, System.Enum.GetNames(typeof(MovesetActorState)));
        }

        [Test]
        public void AttackTriggerRanges_CoverAllDistances_WithoutGapsForTheStalker()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(TunnelStalkerPath);
            for (var d = 0f; d <= 9f; d += 0.25f)
            {
                var distance = d;
                Assert.IsTrue(elite.Moveset.Any(a => a.IsInTriggerRange(distance)), $"No attack covers distance {distance}.");
            }
        }
    }
}
