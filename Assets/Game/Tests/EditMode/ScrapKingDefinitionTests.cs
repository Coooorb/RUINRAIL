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
    /// <summary>TASK 117: Scrap King (1,000 HP, 22–28 strongest, XP 650) with its four attacks and an aggressive phase 2; Rustworks has exactly its two Bosses, each arena pins one.</summary>
    public class ScrapKingDefinitionTests
    {
        private const string ScrapKingPath = "Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_ScrapKing.asset";

        private static BossDefinition[] Roster() => AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(b => b != null).ToArray();

        [Test]
        public void ScrapKing_MatchesTheApprovedCatalog_WithFourAttacks_AndAnAggressivePhaseTwo()
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>(ScrapKingPath);
            Assert.IsNotNull(boss);
            Assert.AreEqual("boss_scrap_king", boss.Id);
            Assert.AreEqual("Scrap King", boss.DisplayName);
            Assert.AreEqual(Biome.Rustworks, boss.Biome);
            Assert.AreEqual(1000, boss.BaseHealth);
            Assert.AreEqual(650, boss.BaseXp);
            Assert.AreEqual(22, boss.StrongestAttackDamageMin);
            Assert.AreEqual(28, boss.StrongestAttackDamageMax);
            Assert.AreEqual(0.5f, boss.PhaseTwoHealthFraction, 0.0001f, "Phase 2 at 50% HP.");
            Assert.Less(boss.PhaseTwoTimingMultiplier, 0.8f, "Shed armor: markedly more movement/aggression than the other bosses' phase 2.");
            Assert.Greater(boss.MoveSpeed, 2.5f, "Mobile wasteland fighter.");
            Assert.IsFalse(boss.CanSummon);

            Assert.AreEqual(4, boss.Moveset.Count, "Automatic Burst, Grenade Throw, Combat Roll, Heavy Melee Swing.");
            CollectionAssert.AreEquivalent(new[] { "Automatic Burst", "Grenade Throw", "Combat Roll", "Heavy Melee Swing" }, boss.Moveset.Select(a => a.DisplayName));
            var burst = boss.Moveset.Single(a => a.DisplayName == "Automatic Burst");
            Assert.AreEqual(AttackMotion.Projectile, burst.Motion);
            Assert.Greater(burst.HitCount, 1, "Automatic: several shots per burst.");
            var grenade = boss.Moveset.Single(a => a.DisplayName == "Grenade Throw");
            Assert.AreEqual(AttackMotion.Zone, grenade.Motion, "Marked blast area.");
            Assert.GreaterOrEqual(grenade.TelegraphSeconds, 1f);
            var roll = boss.Moveset.Single(a => a.DisplayName == "Combat Roll");
            Assert.AreEqual(AttackMotion.Dash, roll.Motion, "Reposition, not an attack.");
            Assert.AreEqual(0, roll.DamageMax, "The roll deals no damage.");
            var swing = boss.Moveset.Single(a => a.DisplayName == "Heavy Melee Swing");
            Assert.AreEqual(AttackMotion.Stationary, swing.Motion);
            Assert.AreEqual(22, swing.DamageMin);
            Assert.AreEqual(28, swing.DamageMax);
            Assert.LessOrEqual(swing.MaxTriggerRange, 2.5f, "Close range only.");

            foreach (var attack in boss.Moveset)
            {
                Assert.Greater(attack.TelegraphSeconds, 0f, $"{attack.Id} must be telegraphed.");
                Assert.LessOrEqual(attack.DamageMax, boss.StrongestAttackDamageMax, $"{attack.Id} within the strongest single-hit target.");
                Assert.LessOrEqual(attack.DamageMin, attack.DamageMax);
            }

            Assert.AreEqual(swing.DamageMax, boss.Moveset.Max(a => a.DamageMax), "Heavy Melee Swing is the strongest single hit.");
        }

        [Test]
        public void Rustworks_HasExactlyTwoBosses_AndEachArenaPinsItsBossByTag()
        {
            var rust = Roster().Where(b => b.Biome == Biome.Rustworks).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_the_foundry_titan", "boss_scrap_king" }, rust.Select(b => b.Id));

            var arenas = RoomValidationTools.LoadAllRoomDefinitions().Where(d => d.Biome == Biome.Rustworks && d.RoomType == Dungeon.Rooms.RoomType.Boss).OrderBy(d => d.Id).ToList();
            Assert.AreEqual(2, arenas.Count);
            var pinned = arenas.Select(a => BossSelection.Select(rust, new BossSpawnRequest(Biome.Rustworks, a.Tags, 1, 1, 0, Vector2.zero, null)).Id).ToList();
            CollectionAssert.AreEquivalent(new[] { "boss_the_foundry_titan", "boss_scrap_king" }, pinned, "Each arena binds a different Rustworks boss through its boss:<id> tag.");
            Assert.AreEqual("boss_scrap_king", BossSelection.Select(rust, new BossSpawnRequest(Biome.Rustworks, arenas.Single(a => a.Id == "rust_boss_02").Tags, 7, 3, 5, Vector2.zero, null)).Id);

            var picks = Enumerable.Range(0, 30).Select(i => BossSelection.Select(rust, new BossSpawnRequest(Biome.Rustworks, null, 42, 2, i, Vector2.zero, null)).Id).Distinct().ToList();
            Assert.AreEqual(2, picks.Count, "Both bosses are reachable by seed in untagged arenas.");
            Assert.AreEqual(6, Roster().Length, "126: six Bosses across the three biomes.");
        }
    }
}
