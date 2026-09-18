using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Tests
{
    public class ProgressionTests
    {
        // ---- Acceptance 1 + 2: curve ----

        [Test]
        public void LevelCurve_MatchesEveryApprovedCheckpoint_AndStopsAt61()
        {
            Assert.AreEqual(250, LevelCurve.XpToNextLevel(1));
            Assert.AreEqual(1105, LevelCurve.XpToNextLevel(10));
            Assert.AreEqual(5905, LevelCurve.XpToNextLevel(30));
            Assert.AreEqual(20605, LevelCurve.XpToNextLevel(60));
            Assert.AreEqual(454550, LevelCurve.TotalXpToMaxLevel);
            Assert.AreEqual(0, LevelCurve.XpToNextLevel(61), "No progression past the cap.");
            Assert.AreEqual(0, LevelCurve.XpToNextLevel(0));
            for (var level = 1; level < 61; level++)
            {
                var n = level - 1;
                Assert.AreEqual(250 + 50 * n + 5 * n * n, LevelCurve.XpToNextLevel(level), $"L{level}");
            }

            Assert.AreEqual(1, LevelCurve.LevelForTotalXp(0));
            Assert.AreEqual(1, LevelCurve.LevelForTotalXp(249));
            Assert.AreEqual(2, LevelCurve.LevelForTotalXp(250));
            Assert.AreEqual(61, LevelCurve.LevelForTotalXp(454550));
            Assert.AreEqual(61, LevelCurve.LevelForTotalXp(10_000_000), "Capped.");
            Assert.AreEqual(60, LevelCurve.LevelForTotalXp(454549));
            Assert.AreEqual(60, LevelCurve.SkillPointsEarned(454550), "Exactly 60 points by max level.");
            Assert.AreEqual(60, LevelCurve.SkillPointsEarned(int.MaxValue));
            Assert.AreEqual(0, LevelCurve.XpIntoCurrentLevel(454550));
            Assert.AreEqual(5, LevelCurve.XpIntoCurrentLevel(255));
        }

        [Test]
        public void ProgressionService_CommitsXpImmediately_GrantsOnePointPerLevel()
        {
            var profile = new PlayerProfile();
            var progression = new ProgressionService(profile);
            var levelUps = 0;
            progression.LevelledUp += (_, _) => levelUps++;

            Assert.AreEqual(0, progression.AddXp(249));
            Assert.AreEqual(1, progression.Level);
            Assert.AreEqual(0, progression.UnspentSkillPoints);
            Assert.AreEqual(1, progression.AddXp(1));
            Assert.AreEqual(2, progression.Level);
            Assert.AreEqual(1, progression.UnspentSkillPoints);
            Assert.AreEqual(250, profile.TotalXp, "XP is written to the permanent profile at once.");
            Assert.AreEqual(2, progression.AddXp(305 + 370), "L2→3 needs 305, L3→4 needs 370.");
            Assert.AreEqual(4, progression.Level);
            Assert.AreEqual(3, progression.UnspentSkillPoints);
            Assert.AreEqual(2, levelUps, "One event per AddXp that crossed a level (the second crossed two).");

            progression.AddXp(454550);
            Assert.AreEqual(61, progression.Level);
            Assert.IsTrue(progression.IsMaxLevel);
            Assert.AreEqual(60, progression.UnspentSkillPoints);
            Assert.AreEqual(0, progression.AddXp(99999), "Past the cap nothing more is granted.");
            Assert.AreEqual(60, progression.UnspentSkillPoints);
            Assert.AreEqual(0, progression.AddXp(-5));
        }

        // ---- Acceptance 3: rank cap and atomic overspend ----

        [Test]
        public void Skills_CapAtRank10_AndOverspendIsRejectedAtomically()
        {
            var profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpForLevel(13) };
            var progression = new ProgressionService(profile);
            Assert.AreEqual(12, progression.UnspentSkillPoints);

            Assert.AreEqual(SkillSpendError.NotAtBase, progression.TrySpend(SkillId.Vitality), "Points are spent only in the Shelter.");
            progression.IsAtBase = true;
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Vitality, 10));
            Assert.AreEqual(10, profile.Skills.Vitality);
            Assert.AreEqual(2, progression.UnspentSkillPoints);
            Assert.AreEqual(SkillSpendError.RankAtMax, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(10, profile.Skills.Vitality);
            Assert.AreEqual(2, progression.UnspentSkillPoints, "Rejected spend changes nothing.");
            Assert.AreEqual(SkillSpendError.NoUnspentPoints, progression.TrySpend(SkillId.Power, 3));
            Assert.AreEqual(0, profile.Skills.Power);
            Assert.AreEqual(2, progression.UnspentSkillPoints, "Partial spends never happen.");
            Assert.AreEqual(SkillSpendError.InvalidAmount, progression.TrySpend(SkillId.Power, 0));
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Power, 2));
            Assert.AreEqual(0, progression.UnspentSkillPoints);
            Assert.AreEqual(12, progression.SpentSkillPoints);

            Assert.AreEqual(12, progression.ResetSkills());
            Assert.AreEqual(12, progression.UnspentSkillPoints);
            Assert.AreEqual(0, profile.Skills.TotalSpent);
            Assert.AreEqual(13, progression.Level, "Respec leaves level and XP untouched.");
        }

        [Test]
        public void Reconcile_KeepsSpentPlusUnspentEqualToEarned()
        {
            var profile = new PlayerProfile { TotalXp = LevelCurve.TotalXpForLevel(5), UnspentSkillPoints = 99 };
            profile.Skills.Mobility = 3;
            var progression = new ProgressionService(profile);
            Assert.AreEqual(1, progression.UnspentSkillPoints, "4 earned − 3 spent.");

            var corrupt = new PlayerProfile { TotalXp = 0 };
            corrupt.Skills.Power = 5;
            new ProgressionService(corrupt);
            Assert.AreEqual(0, corrupt.Skills.TotalSpent, "Spent above earned is not possible.");
        }

        // ---- Acceptance 4: exact modifiers through caps ----

        [Test]
        public void EachSkill_ProducesExactPerRankModifiers_ThroughCentralCaps()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            var stats = new PlayerStats(caps, 100);
            var allocation = new SkillAllocation();
            stats.SetSource(new SkillStatSource(allocation));

            allocation.SetRank(SkillId.Vitality, 10);
            allocation.SetRank(SkillId.Power, 10);
            allocation.SetRank(SkillId.Mobility, 10);
            allocation.SetRank(SkillId.Recovery, 10);
            allocation.SetRank(SkillId.Handling, 10);
            allocation.SetRank(SkillId.Resilience, 10);
            stats.Recompute();

            Assert.AreEqual(20, stats.GetFlat(StatId.MaxHealth));
            Assert.AreEqual(120, stats.MaxHealth);
            Assert.AreEqual(10, stats.GetPercent(StatId.WeaponDamage));
            Assert.AreEqual(10, stats.GetPercent(StatId.MovementSpeed));
            Assert.AreEqual(20, stats.GetPercent(StatId.HealingReceived));
            Assert.AreEqual(10, stats.GetPercent(StatId.ReloadSpeed));
            Assert.AreEqual(10, stats.GetPercent(StatId.WeaponSwitchSpeed));
            Assert.AreEqual(20, stats.GetPercent(StatId.KnockbackResistance));
            Assert.AreEqual(20, stats.GetPercent(StatId.StaggerResistance));

            for (var rank = 0; rank <= 10; rank++)
            {
                allocation.SetRank(SkillId.Mobility, rank);
                stats.Recompute();
                Assert.AreEqual(rank, stats.GetPercent(StatId.MovementSpeed), $"Mobility rank {rank} = +{rank}%");
            }

            stats.SetSource(new StatModifierSource("gear", StatModifier.Percent(StatId.MovementSpeed, 25)));
            Assert.AreEqual(30, stats.GetPercent(StatId.MovementSpeed), "10 + 25 clamps at the +30% cap.");
            Assert.IsFalse(allocation.SetRankAndReturn(SkillId.Mobility, 11), "SetRank clamps to 10.");
            Assert.AreEqual(10, allocation.GetRank(SkillId.Mobility));
            Object.DestroyImmediate(caps);
        }

        // ---- Acceptance 5: expedition XP is permanent regardless of extraction ----

        [Test]
        public void ExpeditionXp_ReachesTheProfileImmediately_EvenBeforeAnyExtractionOrFailure()
        {
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[0]);
            var service = new ExpeditionService(id => registry.TryGet(id, out var d) ? d : null, _ => null, null);
            var profile = new PlayerProfile { TotalXp = 200 };
            var state = service.Start(profile, 1, Biome.RuinedMetro);
            Assert.IsFalse(service.Progression.IsAtBase, "No skill spending mid-expedition.");

            service.RecordEnemyDefeated(30);
            service.RecordEnemyDefeated(30);
            Assert.AreEqual(260, profile.TotalXp, "Committed while the run is still active.");
            Assert.AreEqual(2, profile.Level);
            Assert.AreEqual(1, profile.UnspentSkillPoints);
            Assert.AreEqual(60, state.Stats.XpEarned);
            Assert.AreEqual(SkillSpendError.NotAtBase, service.Progression.TrySpend(SkillId.Power));

            service.Fail();
            Assert.AreEqual(260, profile.TotalXp, "Failure keeps every XP point and adds none twice.");
            Assert.AreEqual(1, profile.UnspentSkillPoints);
        }
    }

    internal static class SkillAllocationTestExtensions
    {
        public static bool SetRankAndReturn(this SkillAllocation allocation, SkillId skill, int rank)
        {
            allocation.SetRank(skill, rank);
            return allocation.GetRank(skill) == rank;
        }
    }
}
