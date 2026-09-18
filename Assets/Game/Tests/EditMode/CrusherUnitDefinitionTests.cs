using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 115: the authored Crusher Unit asset matches 45 (475 HP, 26–32, 2.5, XP 325) with its three-attack moveset; Rustworks has exactly its two Elites.</summary>
    public class CrusherUnitDefinitionTests
    {
        private const string Path = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_CrusherUnit.asset";

        [Test]
        public void CrusherUnit_MatchesTheApprovedCatalog_AndItsThreeAttackMoveset()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(Path);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_crusher_unit", elite.Id);
            Assert.AreEqual("Crusher Unit", elite.DisplayName);
            Assert.AreEqual(Biome.Rustworks, elite.Biome);
            Assert.AreEqual(475, elite.BaseHealth);
            Assert.AreEqual(2.5f, elite.MoveSpeed, 0.001f);
            Assert.AreEqual(325, elite.BaseXp);
            Assert.AreEqual(26, elite.StrongestAttackDamageMin);
            Assert.AreEqual(32, elite.StrongestAttackDamageMax);

            Assert.AreEqual(3, elite.Moveset.Count, "Crusher Charge, Scrap Barrage, Hydraulic Slam.");
            CollectionAssert.AreEquivalent(new[] { "Crusher Charge", "Scrap Barrage", "Hydraulic Slam" }, elite.Moveset.Select(a => a.DisplayName));

            var charge = elite.Moveset.Single(a => a.DisplayName == "Crusher Charge");
            Assert.AreEqual(AttackMotion.Dash, charge.Motion);
            Assert.AreEqual(26, charge.DamageMin);
            Assert.AreEqual(32, charge.DamageMax);
            Assert.Greater(charge.DashDistance, 0f);
            Assert.GreaterOrEqual(charge.RecoverySeconds, 1.5f, "Wall-collision recovery: the charge leaves the machine open.");

            var barrage = elite.Moveset.Single(a => a.DisplayName == "Scrap Barrage");
            Assert.AreEqual(AttackMotion.Projectile, barrage.Motion);
            Assert.Greater(barrage.ProjectileCount, 1, "A spread of scrap.");
            Assert.Greater(barrage.SpreadDegrees, 0f);

            var slam = elite.Moveset.Single(a => a.DisplayName == "Hydraulic Slam");
            Assert.AreEqual(AttackMotion.Zone, slam.Motion, "Rectangular telegraphed zone.");
            Assert.Greater(slam.ZoneLength, 0f);
            Assert.Greater(slam.ZoneWidth, 0f);

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.Greater(attack.RecoverySeconds, 0f, $"{attack.Id} has a recovery window.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} cannot exceed the strongest attack target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
                Assert.IsTrue(attack.MinTriggerRange <= attack.MaxTriggerRange);
            }

            Assert.AreEqual(charge.DamageMax, elite.Moveset.Max(a => a.DamageMax), "Crusher Charge is the strongest attack.");
        }

        [Test]
        public void Rustworks_HasExactlyTwoElites_ExecutionerAndCrusher_AndNoModifierSystem()
        {
            var elites = AssetDatabase.FindAssets("t:EliteDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && e.Biome == Biome.Rustworks)
                .ToList();
            CollectionAssert.AreEquivalent(new[] { "elite_scrap_executioner", "elite_crusher_unit" }, elites.Select(e => e.Id));
            Assert.IsTrue(elites.All(e => e.BaseXp == 325), "45: both Rustworks Elites award 325 XP.");
            var gameplay = typeof(EliteDefinition).Assembly;
            Assert.IsEmpty(gameplay.GetTypes().Where(t => t.Name.IndexOf("EliteModifier", System.StringComparison.OrdinalIgnoreCase) >= 0 || t.Name.IndexOf("ElitePhase", System.StringComparison.OrdinalIgnoreCase) >= 0));
        }
    }
}
