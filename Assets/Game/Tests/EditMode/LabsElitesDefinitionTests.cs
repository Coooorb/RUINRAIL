using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 124/125: Mutated Brute (525 HP, 28–34, 2.4, XP 350) and Prototype X-7 (375 HP, 5 x 8–10 salvo, 3.4, XP 300) match 45; Labs has exactly these two Elites.</summary>
    public class LabsElitesDefinitionTests
    {
        private const string BrutePath = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_MutatedBrute.asset";
        private const string X7Path = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_PrototypeX7.asset";

        [Test]
        public void MutatedBrute_MatchesTheApprovedCatalog_AndItsThreeAttackMoveset()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(BrutePath);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_mutated_brute", elite.Id);
            Assert.AreEqual("Mutated Brute", elite.DisplayName);
            Assert.AreEqual(Biome.OvergrownLabs, elite.Biome);
            Assert.AreEqual(525, elite.BaseHealth, "Very high HP mutant.");
            Assert.AreEqual(2.4f, elite.MoveSpeed, 0.001f);
            Assert.AreEqual(350, elite.BaseXp);
            Assert.AreEqual(28, elite.StrongestAttackDamageMin);
            Assert.AreEqual(34, elite.StrongestAttackDamageMax);

            Assert.AreEqual(3, elite.Moveset.Count, "Double Slam, Mutation Leap, Roar Rush.");
            CollectionAssert.AreEquivalent(new[] { "Double Slam", "Mutation Leap", "Roar Rush" }, elite.Moveset.Select(a => a.DisplayName));
            var slam = elite.Moveset.Single(a => a.DisplayName == "Double Slam");
            Assert.AreEqual(AttackMotion.Slam, slam.Motion);
            Assert.AreEqual(2, slam.HitCount, "Double: two slams per use.");
            var leap = elite.Moveset.Single(a => a.DisplayName == "Mutation Leap");
            Assert.AreEqual(AttackMotion.Zone, leap.Motion, "Telegraphed landing area.");
            Assert.AreEqual(28, leap.DamageMin);
            Assert.AreEqual(34, leap.DamageMax);
            Assert.GreaterOrEqual(leap.TelegraphSeconds, 1f);
            var rush = elite.Moveset.Single(a => a.DisplayName == "Roar Rush");
            Assert.AreEqual(AttackMotion.Dash, rush.Motion);
            Assert.LessOrEqual(rush.RecoverySeconds, 0.6f, "Short aggression window: little recovery after the rush.");

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} within the strongest target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
                Assert.IsTrue(attack.MinTriggerRange <= attack.MaxTriggerRange);
            }

            Assert.AreEqual(leap.DamageMax, elite.Moveset.Max(a => a.DamageMax), "Mutation Leap is the strongest hit.");
        }

        [Test]
        public void PrototypeX7_MatchesTheApprovedCatalog_RangedMoveset_WithBlinkThenSalvo()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(X7Path);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_prototype_x7", elite.Id);
            Assert.AreEqual("Prototype X-7", elite.DisplayName);
            Assert.AreEqual(Biome.OvergrownLabs, elite.Biome);
            Assert.AreEqual(375, elite.BaseHealth);
            Assert.AreEqual(3.4f, elite.MoveSpeed, 0.001f, "Mobile ranged prototype.");
            Assert.AreEqual(300, elite.BaseXp);
            Assert.AreEqual(8, elite.StrongestAttackDamageMin, "Per projectile of the 5 x 8-10 salvo.");
            Assert.AreEqual(10, elite.StrongestAttackDamageMax);

            CollectionAssert.AreEquivalent(new[] { "Energy Burst", "Radial Pulse", "Blink Shot", "Broad Salvo" }, elite.Moveset.Select(a => a.DisplayName), "Three documented mechanics: burst, radial pulse, blink reposition followed by the broad salvo.");
            var burst = elite.Moveset.Single(a => a.DisplayName == "Energy Burst");
            Assert.AreEqual(AttackMotion.Projectile, burst.Motion);
            Assert.Greater(burst.HitCount, 1, "A burst of shots.");
            var pulse = elite.Moveset.Single(a => a.DisplayName == "Radial Pulse");
            Assert.AreEqual(AttackMotion.Projectile, pulse.Motion);
            Assert.AreEqual(360f, pulse.SpreadDegrees, 0.01f, "Radial ring.");
            Assert.GreaterOrEqual(pulse.ProjectileCount, 6);
            var blink = elite.Moveset.Single(a => a.DisplayName == "Blink Shot");
            Assert.AreEqual(AttackMotion.Dash, blink.Motion, "Reposition.");
            Assert.AreEqual(0, blink.DamageMax, "The blink itself deals no damage.");
            Assert.Greater(blink.DashSpeed, 15f, "A blink, not a walk.");
            var salvo = elite.Moveset.Single(a => a.DisplayName == "Broad Salvo");
            Assert.AreEqual(AttackMotion.Projectile, salvo.Motion);
            Assert.AreEqual(5, salvo.ProjectileCount, "45: 5 x 8-10 salvo.");
            Assert.AreEqual(8, salvo.DamageMin);
            Assert.AreEqual(10, salvo.DamageMax);
            Assert.Greater(salvo.SpreadDegrees, 45f, "Broad.");
            Assert.Less(elite.Moveset.ToList().IndexOf(blink), elite.Moveset.ToList().IndexOf(salvo), "Blink precedes the salvo in the rotation.");

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} within the per-projectile target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }
        }

        [Test]
        public void OvergrownLabs_HasExactlyTwoElites_AndSixElitesExistAcrossTheThreeBiomes()
        {
            var all = AssetDatabase.FindAssets("t:EliteDefinition").Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(e => e != null).ToList();
            CollectionAssert.AreEquivalent(new[] { "elite_mutated_brute", "elite_prototype_x7" }, all.Where(e => e.Biome == Biome.OvergrownLabs).Select(e => e.Id));
            Assert.AreEqual(6, all.Count, "126: six Elites, two per biome.");
            Assert.AreEqual(6, all.Select(e => e.Id).Distinct().Count());
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                Assert.AreEqual(2, all.Count(e => e.Biome == biome), biome.ToString());
            }
        }
    }
}
