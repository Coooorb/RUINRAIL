using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEditor;

namespace RuinRail.Tests
{
    /// <summary>TASK 088: the authored Railguard asset matches 45 and Ruined Metro has exactly its two Elites.</summary>
    public class RailguardDefinitionTests
    {
        private const string RailguardPath = "Assets/Game/ScriptableObjects/Enemies/Elites/Elite_Railguard.asset";

        [Test]
        public void Railguard_MatchesTheApprovedCatalog_AndItsThreeAttackMoveset()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>(RailguardPath);
            Assert.IsNotNull(elite);
            Assert.AreEqual("elite_railguard", elite.Id);
            Assert.AreEqual("Railguard", elite.DisplayName);
            Assert.AreEqual(Biome.RuinedMetro, elite.Biome);
            Assert.AreEqual(450, elite.BaseHealth);
            Assert.AreEqual(1.8f, elite.MoveSpeed, 0.001f);
            Assert.AreEqual(300, elite.BaseXp);
            Assert.AreEqual(20, elite.StrongestAttackDamageMin);
            Assert.AreEqual(25, elite.StrongestAttackDamageMax);

            Assert.AreEqual(3, elite.Moveset.Count, "Burst Cannon, Rail Sweep, Ground Shock.");
            CollectionAssert.AreEquivalent(new[] { "Burst Cannon", "Rail Sweep", "Ground Shock" }, elite.Moveset.Select(a => a.DisplayName));

            var burst = elite.Moveset.Single(a => a.DisplayName == "Burst Cannon");
            Assert.AreEqual(AttackMotion.Projectile, burst.Motion);
            Assert.AreEqual(3, burst.HitCount, "A burst of three shots.");
            Assert.AreEqual(1, burst.ProjectileCount);

            var sweep = elite.Moveset.Single(a => a.DisplayName == "Rail Sweep");
            Assert.AreEqual(AttackMotion.Projectile, sweep.Motion);
            Assert.Greater(sweep.ProjectileCount, 1, "Fanned projectiles.");
            Assert.Greater(sweep.SpreadDegrees, 0f);

            var shock = elite.Moveset.Single(a => a.DisplayName == "Ground Shock");
            Assert.AreEqual(AttackMotion.Slam, shock.Motion, "Close-range AoE denial.");
            Assert.AreEqual(20, shock.DamageMin);
            Assert.AreEqual(25, shock.DamageMax);
            Assert.LessOrEqual(shock.MaxTriggerRange, 3f);

            foreach (var attack in elite.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, elite.StrongestAttackDamageMax, $"{attack.Id} cannot exceed the strongest attack target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
                Assert.IsTrue(attack.MinTriggerRange <= attack.MaxTriggerRange);
            }

            Assert.AreEqual(shock.DamageMax, elite.Moveset.Max(a => a.DamageMax), "Ground Shock is the strongest attack.");
        }

        [Test]
        public void RuinedMetro_HasExactlyTwoElites_TunnelStalkerAndRailguard_AndNoModifierSystem()
        {
            var elites = AssetDatabase.FindAssets("t:EliteDefinition")
                .Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(e => e != null && e.Biome == Biome.RuinedMetro)
                .ToList();
            CollectionAssert.AreEquivalent(new[] { "elite_tunnel_stalker", "elite_railguard" }, elites.Select(e => e.Id));

            var gameplay = typeof(EliteDefinition).Assembly;
            var suspicious = gameplay.GetTypes().Where(t => t.Name.IndexOf("EliteModifier", System.StringComparison.OrdinalIgnoreCase) >= 0
                                                          || t.Name.IndexOf("EliteAffix", System.StringComparison.OrdinalIgnoreCase) >= 0
                                                          || t.Name.IndexOf("ElitePhase", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Assert.IsEmpty(suspicious, "43: Elites are hand-designed; no random modifier/phase systems.");
            Assert.IsFalse(typeof(EliteDefinition).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Any(f => f.Name.ToLowerInvariant().Contains("modifier") || f.Name.ToLowerInvariant().Contains("phase")));
        }
    }
}
