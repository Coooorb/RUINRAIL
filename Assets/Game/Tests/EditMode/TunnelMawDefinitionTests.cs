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
    /// <summary>TASK 089: Tunnel Maw asset matches 46; both Ruined Metro bosses bind to arenas by data tags.</summary>
    public class TunnelMawDefinitionTests
    {
        private const string TunnelMawPath = "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset";

        private static BossDefinition[] Roster() => AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b != null).ToArray();

        [Test]
        public void TunnelMaw_MatchesTheApprovedCatalog_WithFourAttacks_PhaseTwoAtHalf_AndLimitedSwarmRoar()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>(TunnelMawPath);
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_tunnel_maw", boss.Id);
            Assert.AreEqual("Tunnel Maw", boss.DisplayName);
            Assert.AreEqual(Biome.RuinedMetro, boss.Biome);
            Assert.AreEqual(1150, boss.BaseHealth);
            Assert.AreEqual(700, boss.BaseXp);
            Assert.AreEqual(28, boss.StrongestAttackDamageMin);
            Assert.AreEqual(34, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f, "Phase 2 at 50% HP.");
            Assert.Less(boss.PhaseTwoTimingMultiplier, 1f, "Phase 2 speeds known mechanics.");
            Assert.GreaterOrEqual(boss.StaggerResistancePercent, 90, "Boss stagger resistance is very high.");
            Assert.AreEqual(100, boss.KnockbackResistancePercent);

            Assert.AreEqual(4, boss.Moveset.Count, "Bite/Lunge, Claw Sweep, Marked Leap, Roar.");
            CollectionAssert.AreEquivalent(new[] { "Bite/Lunge", "Claw Sweep", "Marked Leap", "Roar" }, boss.Moveset.Select(a => a.DisplayName));
            var bite = boss.Moveset.Single(a => a.DisplayName == "Bite/Lunge");
            Assert.AreEqual(AttackMotion.Dash, bite.Motion);
            var claw = boss.Moveset.Single(a => a.DisplayName == "Claw Sweep");
            Assert.AreEqual(AttackMotion.Stationary, claw.Motion);
            var leap = boss.Moveset.Single(a => a.DisplayName == "Marked Leap");
            Assert.AreEqual(AttackMotion.Zone, leap.Motion, "Marked landing area.");
            Assert.AreEqual(28, leap.DamageMin);
            Assert.AreEqual(34, leap.DamageMax);
            Assert.GreaterOrEqual(leap.TelegraphSeconds, 1f, "The leap is clearly marked before it lands.");
            var roar = boss.Moveset.Single(a => a.DisplayName == "Roar");
            Assert.AreSame(roar, boss.SummonAttack, "Roar brings limited Swarm pressure.");
            Assert.IsNotNull(boss.SummonDefinition);
            Assert.AreEqual("swarm", boss.SummonDefinition.Id);
            Assert.IsTrue(boss.CanSummon);
            Assert.LessOrEqual(boss.MaxLivingSummons, 4, "Limited pressure, not an army.");

            Assert.AreEqual(1, boss.PhaseTwoArenaHazards.Count, "Phase 2 adds the burrow emergence.");
            var burrow = boss.PhaseTwoArenaHazards[0];
            Assert.AreEqual(AttackMotion.Zone, burrow.Motion, "Underground movement and emergence stay visibly telegraphed.");
            Assert.GreaterOrEqual(burrow.TelegraphSeconds, 1f);

            foreach (var attack in boss.Moveset.Concat(boss.PhaseTwoArenaHazards))
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed (no cheap untelegraphed one-shots).");
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, $"{attack.Id} within the strongest single-hit target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }

            Assert.AreEqual(leap.DamageMax, boss.Moveset.Max(a => a.DamageMax), "Marked Leap is the strongest single hit.");
        }

        [Test]
        public void RuinedMetro_HasExactlyTwoBosses_AndEachArenaPinsItsBossByTag()
        {
            var metro = Roster().Where(b => b.Biome == Biome.RuinedMetro).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_the_conductor", "boss_tunnel_maw" }, metro.Select(b => b.Id));

            var arenas = RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.RuinedMetro && d.RoomType == Dungeon.Rooms.RoomType.Boss).OrderBy(d => d.Id).ToList();
            Assert.AreEqual(2, arenas.Count);
            var pinned = arenas.Select(a => BossSelection.Select(metro, new BossSpawnRequest(Biome.RuinedMetro, a.Tags, 1, 1, 0, Vector2.zero, null)).Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_the_conductor", "boss_tunnel_maw" }, pinned, "Each arena binds a different Ruined Metro boss through its boss:<id> tag.");
            Assert.AreEqual("boss_tunnel_maw", BossSelection.Select(metro, new BossSpawnRequest(Biome.RuinedMetro, arenas.Single(a => a.Id == "metro_boss_02").Tags, 7, 3, 5, Vector2.zero, null)).Id, "The second arena is the Tunnel Maw nest.");

            // Untagged arenas pick deterministically by seed among the biome's bosses.
            var a = BossSelection.Select(metro, new BossSpawnRequest(Biome.RuinedMetro, null, 42, 2, 9, Vector2.zero, null));
            var b = BossSelection.Select(metro, new BossSpawnRequest(Biome.RuinedMetro, null, 42, 2, 9, Vector2.zero, null));
            Assert.AreSame(a, b);
            var picks = Enumerable.Range(0, 30).Select(i => BossSelection.Select(metro, new BossSpawnRequest(Biome.RuinedMetro, null, 42, 2, i, Vector2.zero, null)).Id).Distinct().ToList();
            Assert.AreEqual(2, picks.Count, "Both bosses are reachable by seed.");
            Assert.IsNull(BossSelection.Select(metro, new BossSpawnRequest(Biome.Rustworks, null, 1, 1, 0, Vector2.zero, null)));
        }
    }
}
