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
    /// <summary>TASK 116: The Foundry Titan (1,350 HP, 30–36 strongest, XP 800) with its four attacks, phase 2 at 50% with burning zones, pinned by the foundry arena.</summary>
    public class FoundryTitanDefinitionTests
    {
        private const string TitanPath = "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TheFoundryTitan.asset";

        private static BossDefinition[] Roster() => AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b != null).ToArray();

        [Test]
        public void FoundryTitan_MatchesTheApprovedCatalog_WithFourAttacks_PhaseTwoAtHalf_AndBurningZones()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>(TitanPath);
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_the_foundry_titan", boss.Id);
            Assert.AreEqual("The Foundry Titan", boss.DisplayName);
            Assert.AreEqual(Biome.Rustworks, boss.Biome);
            Assert.AreEqual(1350, boss.BaseHealth);
            Assert.AreEqual(800, boss.BaseXp);
            Assert.AreEqual(30, boss.StrongestAttackDamageMin);
            Assert.AreEqual(36, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f, "Phase 2 at 50% HP.");
            Assert.Less(boss.PhaseTwoTimingMultiplier, 1f, "Phase 2 (reactor overload) moderately speeds known patterns.");
            Assert.GreaterOrEqual(boss.StaggerResistancePercent, 90);
            Assert.AreEqual(100, boss.KnockbackResistancePercent);
            Assert.IsFalse(boss.CanSummon, "The Titan fights alone.");

            Assert.AreEqual(4, boss.Moveset.Count, "Hydraulic Slam, Marked Rocket Barrage, Arm Sweep, Furnace Blast.");
            CollectionAssert.AreEquivalent(new[] { "Hydraulic Slam", "Marked Rocket Barrage", "Arm Sweep", "Furnace Blast" }, boss.Moveset.Select(a => a.DisplayName));
            var slam = boss.Moveset.Single(a => a.DisplayName == "Hydraulic Slam");
            Assert.AreEqual(AttackMotion.Slam, slam.Motion);
            Assert.AreEqual(30, slam.DamageMin);
            Assert.AreEqual(36, slam.DamageMax);
            var barrage = boss.Moveset.Single(a => a.DisplayName == "Marked Rocket Barrage");
            Assert.AreEqual(AttackMotion.Zone, barrage.Motion, "Marked impact area.");
            Assert.GreaterOrEqual(barrage.TelegraphSeconds, 1f, "Rockets are clearly marked before impact.");
            var sweep = boss.Moveset.Single(a => a.DisplayName == "Arm Sweep");
            Assert.AreEqual(AttackMotion.Stationary, sweep.Motion);
            var blast = boss.Moveset.Single(a => a.DisplayName == "Furnace Blast");
            Assert.AreEqual(AttackMotion.Projectile, blast.Motion, "Cone of furnace fire.");
            Assert.Greater(blast.ProjectileCount, 1);
            Assert.Greater(blast.SpreadDegrees, 0f);

            Assert.AreEqual(1, boss.PhaseTwoArenaHazards.Count, "Phase 2: attacks leave short-lived burning zones.");
            var burn = boss.PhaseTwoArenaHazards[0];
            Assert.AreEqual(AttackMotion.Zone, burn.Motion);
            Assert.GreaterOrEqual(burn.TelegraphSeconds, 1f);
            Assert.Less(burn.DamageMax, slam.DamageMin, "A burning zone is pressure, not a one-shot.");

            foreach (var attack in boss.Moveset.Concat(boss.PhaseTwoArenaHazards))
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, $"{attack.Id} within the strongest single-hit target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }

            Assert.AreEqual(slam.DamageMax, boss.Moveset.Max(a => a.DamageMax), "Hydraulic Slam is the strongest single hit.");
        }

        [Test]
        public void FoundryArena_PinsTheTitan_AndTheTitanIsNeverPickedOutsideRustworks()
        {
            var rust = Roster().Where(b => b.Biome == Biome.Rustworks).ToList();
            CollectionAssert.Contains(rust.Select(b => b.Id).ToList(), "boss_the_foundry_titan");
            Assert.LessOrEqual(rust.Count, 2, "46: two Bosses per biome.");

            var arenas = RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.Rustworks && d.RoomType == Dungeon.Rooms.RoomType.Boss).OrderBy(d => d.Id).ToList();
            Assert.AreEqual(2, arenas.Count);
            var foundry = arenas.Single(a => a.Id == "rust_boss_01");
            Assert.AreEqual("boss_the_foundry_titan", BossSelection.Select(rust, new BossSpawnRequest(Biome.Rustworks, foundry.Tags, 1, 1, 0, Vector2.zero, null)).Id, "The foundry floor binds the Titan.");
            Assert.IsNull(BossSelection.Select(Roster().Where(b => b.Biome == Biome.RuinedMetro).ToList(), new BossSpawnRequest(Biome.Rustworks, foundry.Tags, 1, 1, 0, Vector2.zero, null)), "A Metro roster has no Rustworks boss.");
            Assert.IsNull(BossSelection.Select(rust, new BossSpawnRequest(Biome.RuinedMetro, null, 1, 1, 0, Vector2.zero, null)), "The Titan never spawns in the Metro.");
        }
    }
}
