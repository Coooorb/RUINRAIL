using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 114: the authored Scrap Executioner asset matches 45 (500 HP, 28–34, 2.2, XP 325) with its three-attack moveset.</summary>
    public class ScrapExecutionerDefinitionTests
    {
        private const string Path = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset";

        [Test]
        public void ScrapExecutioner_MatchesTheApprovedCatalog_AndItsThreeAttackMoveset()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(Path);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_scrap_executioner", elite.Id);
            Assert.AreEqual("Scrap Executioner", elite.DisplayName);
            Assert.AreEqual(Biome.Rustworks, elite.Biome);
            Assert.AreEqual(500, elite.BaseHealth);
            Assert.AreEqual(2.2f, elite.MoveSpeed, 0.001f);
            Assert.AreEqual(325, elite.BaseXp);
            Assert.AreEqual(28, elite.StrongestAttackDamageMin);
            Assert.AreEqual(34, elite.StrongestAttackDamageMax);

            Assert.AreEqual(3, elite.Moveset.Count, "Heavy Cleave, Overhead Slam, Execution Charge.");
            CollectionAssert.AreEquivalent(new[] { "Heavy Cleave", "Overhead Slam", "Execution Charge" }, elite.Moveset.Select(a => a.DisplayName));

            var cleave = elite.Moveset.Single(a => a.DisplayName == "Heavy Cleave");
            Assert.AreEqual(AttackMotion.Stationary, cleave.Motion, "Close melee swing.");
            Assert.LessOrEqual(cleave.MaxTriggerRange, 2f);

            var slam = elite.Moveset.Single(a => a.DisplayName == "Overhead Slam");
            Assert.AreEqual(AttackMotion.Slam, slam.Motion, "AoE around the Executioner.");
            Assert.Greater(slam.HitRadius, cleave.HitRadius);

            var charge = elite.Moveset.Single(a => a.DisplayName == "Execution Charge");
            Assert.AreEqual(AttackMotion.Dash, charge.Motion, "Charge into a heavy strike.");
            Assert.Greater(charge.DashDistance, 0f);
            Assert.AreEqual(28, charge.DamageMin);
            Assert.AreEqual(34, charge.DamageMax);
            Assert.GreaterOrEqual(charge.MinTriggerRange, cleave.MaxTriggerRange, "The charge is for targets out of melee reach.");

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.Greater(attack.RecoverySeconds, 0f, $"{attack.Id} has a recovery window.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} cannot exceed the strongest attack target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
                Assert.IsTrue(attack.MinTriggerRange <= attack.MaxTriggerRange);
                Assert.Greater(attack.StaggerPower, 0f, "Heavy industrial hits carry stagger/knockback (42).");
            }

            Assert.AreEqual(charge.DamageMax, elite.Moveset.Max(a => a.DamageMax), "Execution Charge is the strongest attack.");
        }

        [Test]
        public void Rustworks_ElitePool_ContainsTheExecutioner_NoModifierOrPhaseSystem()
        {
            var elites = AssetDatabase.FindAssets("t:EliteDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && e.Biome == Biome.Rustworks)
                .ToList();
            CollectionAssert.Contains(elites.Select(e => e.Id).ToList(), "elite_scrap_executioner");
            Assert.LessOrEqual(elites.Count, 2, "45: at most two Rustworks Elites.");
            Assert.IsFalse(typeof(EliteDefinition).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Any(f => f.Name.ToLowerInvariant().Contains("modifier") || f.Name.ToLowerInvariant().Contains("phase")));
        }
    }
}
