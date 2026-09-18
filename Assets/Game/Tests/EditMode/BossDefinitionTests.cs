using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using UnityEditor;

namespace RuinRail.Tests
{
    public class BossDefinitionTests
    {
        private const string ConductorPath = "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TheConductor.asset";

        [Test]
        public void TheConductor_UsesApprovedBaselines_FourAttacks_AndPhaseTwoAtHalfHealth()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>(ConductorPath);
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_the_conductor", boss.Id);
            Assert.AreEqual("The Conductor", boss.DisplayName);
            Assert.AreEqual(Biome.RuinedMetro, boss.Biome);
            Assert.AreEqual(1050, boss.BaseHealth);
            Assert.AreEqual(650, boss.BaseXp);
            Assert.AreEqual(30, boss.StrongestAttackDamageMin);
            Assert.AreEqual(36, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f);
            Assert.Less(boss.PhaseTwoTimingMultiplier, 1f, "Phase 2 increases pressure.");
            Assert.GreaterOrEqual(boss.StaggerResistancePercent, 90, "Boss stagger resistance is very high.");
            Assert.IsFalse(boss.IsDisplaceable);

            Assert.AreEqual(4, boss.Moveset.Count);
            CollectionAssert.AreEquivalent(new[] { "Burst Cannon", "Projectile Sweep", "Marked Rail Strike", "Emergency Dash" }, boss.Moveset.Select(a => a.DisplayName));
            Assert.AreEqual(AttackMotion.Projectile, boss.Moveset.Single(a => a.DisplayName == "Burst Cannon").Motion);
            Assert.AreEqual(AttackMotion.Projectile, boss.Moveset.Single(a => a.DisplayName == "Projectile Sweep").Motion);
            Assert.Greater(boss.Moveset.Single(a => a.DisplayName == "Projectile Sweep").ProjectileCount, 3, "A sweep is a fan.");
            Assert.Greater(boss.Moveset.Single(a => a.DisplayName == "Projectile Sweep").SpreadDegrees, 30f);
            var rail = boss.Moveset.Single(a => a.DisplayName == "Marked Rail Strike");
            Assert.AreEqual(AttackMotion.Zone, rail.Motion);
            Assert.AreEqual(30, rail.DamageMin);
            Assert.AreEqual(36, rail.DamageMax);
            Assert.GreaterOrEqual(rail.TelegraphSeconds, 1f, "Marked strikes are clearly telegraphed.");
            Assert.AreEqual(AttackMotion.Dash, boss.Moveset.Single(a => a.DisplayName == "Emergency Dash").Motion);

            foreach (var attack in boss.Moveset.Concat(boss.PhaseTwoArenaHazards))
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed (no untelegraphed hits).");
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, $"{attack.Id} exceeds the strongest single-hit target.");
            }

            Assert.AreEqual(1, boss.PhaseTwoArenaHazards.Count, "Phase 2 adds telegraphed rail-line arena hazards.");
            Assert.AreEqual(AttackMotion.Zone, boss.PhaseTwoArenaHazards[0].Motion, "Hazards reuse the marked-zone mechanic.");
            Assert.Greater(boss.PhaseTwoArenaHazards[0].TelegraphSeconds, rail.TelegraphSeconds, "Arena hazard is telegraphed at least as clearly.");
        }

        [Test]
        public void RuinedMetroHasItsTwoBosses_AndModelHasExactlyTwoPhases()
        {
            var bosses = AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_the_conductor", "boss_tunnel_maw" }, bosses.Where(b => b.Biome == Core.Biome.RuinedMetro).Select(b => b.Id), "46: two bosses per biome (TASK 089 added the Tunnel Maw).");

            Assert.IsNull(typeof(BossDefinition).GetProperty("PhaseThreeHealthFraction"));
            Assert.IsFalse(typeof(BossDefinition).GetProperties().Any(p => p.Name.Contains("Three")));
            Assert.AreEqual(typeof(int), typeof(BossController).GetProperty("Phase").PropertyType);
        }
    }
}
