using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 126/127: Subject Omega (1,250 HP, 28–34, XP 750) and A.E.G.I.S. Core (1,050 HP, 28–34, XP 700) with four attacks each, phase 2 at 50%, pinned by the Labs arenas; six Bosses exist.</summary>
    public class LabsBossesDefinitionTests
    {
        private static BossDefinition[] Roster() => AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b != null).ToArray();

        [Test]
        public void SubjectOmega_MatchesTheApprovedCatalog_FourAttacks_PhaseTwoAddsOrganicDenial()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_SubjectOmega.asset");
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_subject_omega", boss.Id);
            Assert.AreEqual("Subject Omega", boss.DisplayName);
            Assert.AreEqual(Biome.OvergrownLabs, boss.Biome);
            Assert.AreEqual(1250, boss.BaseHealth);
            Assert.AreEqual(750, boss.BaseXp);
            Assert.AreEqual(28, boss.StrongestAttackDamageMin);
            Assert.AreEqual(34, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f);
            Assert.Less(boss.PhaseTwoTimingMultiplier, 1f, "Faster slam combinations.");
            Assert.IsFalse(boss.CanSummon);

            CollectionAssert.AreEquivalent(new[] { "Arm Slam", "Charge", "Spore Projectile Burst", "Vine Danger Zone" }, boss.Moveset.Select(a => a.DisplayName));
            var slam = boss.Moveset.Single(a => a.DisplayName == "Arm Slam");
            Assert.AreEqual(AttackMotion.Slam, slam.Motion);
            Assert.AreEqual(28, slam.DamageMin);
            Assert.AreEqual(34, slam.DamageMax);
            Assert.AreEqual(AttackMotion.Dash, boss.Moveset.Single(a => a.DisplayName == "Charge").Motion);
            var spores = boss.Moveset.Single(a => a.DisplayName == "Spore Projectile Burst");
            Assert.AreEqual(AttackMotion.Projectile, spores.Motion);
            Assert.Greater(spores.ProjectileCount, 1);
            var vines = boss.Moveset.Single(a => a.DisplayName == "Vine Danger Zone");
            Assert.AreEqual(AttackMotion.Zone, vines.Motion);
            Assert.GreaterOrEqual(vines.TelegraphSeconds, 1f);

            Assert.AreEqual(1, boss.PhaseTwoArenaHazards.Count, "Phase 2: additional temporary organic area denial.");
            Assert.AreEqual(AttackMotion.Zone, boss.PhaseTwoArenaHazards[0].Motion);
            Assert.GreaterOrEqual(boss.PhaseTwoArenaHazards[0].TelegraphSeconds, 1f, "Readable telegraphs.");
            foreach (var attack in boss.Moveset.Concat(boss.PhaseTwoArenaHazards))
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, attack.Id);
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, attack.Id);
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }

            Assert.AreEqual(slam.DamageMax, boss.Moveset.Max(a => a.DamageMax), "Arm Slam is the strongest single hit.");
        }

        [Test]
        public void AegisCore_MatchesTheApprovedCatalog_FourAttacks_PhaseTwoCombinesRingAndLine()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_AegisCore.asset");
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_aegis_core", boss.Id);
            Assert.AreEqual("A.E.G.I.S. Core", boss.DisplayName);
            Assert.AreEqual(Biome.OvergrownLabs, boss.Biome);
            Assert.AreEqual(1050, boss.BaseHealth);
            Assert.AreEqual(700, boss.BaseXp);
            Assert.AreEqual(28, boss.StrongestAttackDamageMin);
            Assert.AreEqual(34, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f);
            Assert.Less(boss.PhaseTwoTimingMultiplier, 1f);
            Assert.IsFalse(boss.CanSummon);

            CollectionAssert.AreEquivalent(new[] { "Triple Energy Burst", "Radial Projectile Ring", "Line Energy Attack", "Reposition Dash" }, boss.Moveset.Select(a => a.DisplayName));
            var triple = boss.Moveset.Single(a => a.DisplayName == "Triple Energy Burst");
            Assert.AreEqual(AttackMotion.Projectile, triple.Motion);
            Assert.AreEqual(3, triple.HitCount, "Triple.");
            var ring = boss.Moveset.Single(a => a.DisplayName == "Radial Projectile Ring");
            Assert.AreEqual(AttackMotion.Projectile, ring.Motion);
            Assert.AreEqual(360f, ring.SpreadDegrees, 0.01f);
            Assert.GreaterOrEqual(ring.ProjectileCount, 8);
            var line = boss.Moveset.Single(a => a.DisplayName == "Line Energy Attack");
            Assert.AreEqual(AttackMotion.Zone, line.Motion, "Telegraphed line.");
            Assert.AreEqual(28, line.DamageMin);
            Assert.AreEqual(34, line.DamageMax);
            Assert.Greater(line.ZoneLength, 3f * line.ZoneWidth, "A line, not a box.");
            Assert.GreaterOrEqual(line.TelegraphSeconds, 1f);
            var dash = boss.Moveset.Single(a => a.DisplayName == "Reposition Dash");
            Assert.AreEqual(AttackMotion.Dash, dash.Motion);
            Assert.AreEqual(0, dash.DamageMax, "Reposition only.");

            Assert.AreEqual(1, boss.PhaseTwoArenaHazards.Count, "Phase 2 combines familiar patterns: a ring while a line is prepared.");
            Assert.AreEqual(AttackMotion.Projectile, boss.PhaseTwoArenaHazards[0].Motion);
            Assert.AreEqual(360f, boss.PhaseTwoArenaHazards[0].SpreadDegrees, 0.01f);
            foreach (var attack in boss.Moveset.Concat(boss.PhaseTwoArenaHazards))
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, attack.Id);
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, attack.Id);
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }

            Assert.AreEqual(line.DamageMax, boss.Moveset.Max(a => a.DamageMax), "The line is the strongest single hit.");
        }

        [Test]
        public void OvergrownLabs_HasExactlyTwoBosses_ArenasPinThem_AndSixBossesExistAcrossTheBiomes()
        {
            var all = Roster();
            var labs = all.Where(b => b.Biome == Biome.OvergrownLabs).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_subject_omega", "boss_aegis_core" }, labs.Select(b => b.Id));
            Assert.AreEqual(6, all.Length, "126: six Bosses, two per biome.");
            Assert.AreEqual(6, all.Select(b => b.Id).Distinct().Count());
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }) Assert.AreEqual(2, all.Count(b => b.Biome == biome), biome.ToString());

            var arenas = RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.OvergrownLabs && d.RoomType == Dungeon.Rooms.RoomType.Boss).OrderBy(d => d.Id).ToList();
            Assert.AreEqual(2, arenas.Count);
            var pinned = arenas.Select(a => BossSelection.Select(labs, new BossSpawnRequest(Biome.OvergrownLabs, a.Tags, 1, 1, 0, Vector2.zero, null)).Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_subject_omega", "boss_aegis_core" }, pinned);
            Assert.AreEqual("boss_subject_omega", BossSelection.Select(labs, new BossSpawnRequest(Biome.OvergrownLabs, arenas.Single(a => a.Id == "labs_boss_01").Tags, 3, 1, 0, Vector2.zero, null)).Id);
            var picks = Enumerable.Range(0, 30).Select(i => BossSelection.Select(labs, new BossSpawnRequest(Biome.OvergrownLabs, null, 42, 2, i, Vector2.zero, null)).Id).Distinct().ToList();
            Assert.AreEqual(2, picks.Count);
            Assert.IsNull(BossSelection.Select(labs, new BossSpawnRequest(Biome.Rustworks, null, 1, 1, 0, Vector2.zero, null)), "Labs bosses never spawn elsewhere.");
        }
    }
}
