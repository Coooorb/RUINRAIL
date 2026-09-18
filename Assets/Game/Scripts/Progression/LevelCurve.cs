using System;

namespace RuinRail.Gameplay.Progression
{
    /// <summary>
    /// Approved V1 leveling curve (player/13_LEVELING_AND_SKILL_POINTS): XPToNextLevel(L) = 250 + 50(L−1) + 5(L−1)²,
    /// whole numbers only, cap Level 61 (60 level-ups → 60 skill points), total 454,550 XP.
    /// </summary>
    public static class LevelCurve
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 61;
        public const int SkillPointsPerLevel = 1;

        /// <summary>XP needed to advance from <paramref name="level"/> to level + 1; 0 at or above the cap.</summary>
        public static int XpToNextLevel(int level)
        {
            if (level < MinLevel || level >= MaxLevel) return 0;
            var n = level - 1;
            return 250 + 50 * n + 5 * n * n;
        }

        /// <summary>Cumulative XP required to reach <paramref name="level"/> from Level 1.</summary>
        public static int TotalXpForLevel(int level)
        {
            level = Math.Clamp(level, MinLevel, MaxLevel);
            var total = 0;
            for (var l = MinLevel; l < level; l++) total += XpToNextLevel(l);
            return total;
        }

        public static int TotalXpToMaxLevel => TotalXpForLevel(MaxLevel);

        /// <summary>Level reached with a lifetime XP total (never above the cap).</summary>
        public static int LevelForTotalXp(int totalXp)
        {
            var level = MinLevel;
            var remaining = Math.Max(0, totalXp);
            while (level < MaxLevel && remaining >= XpToNextLevel(level))
            {
                remaining -= XpToNextLevel(level);
                level++;
            }

            return level;
        }

        /// <summary>XP accumulated inside the current level (0 at the cap).</summary>
        public static int XpIntoCurrentLevel(int totalXp)
        {
            var level = LevelForTotalXp(totalXp);
            return level >= MaxLevel ? 0 : Math.Max(0, totalXp) - TotalXpForLevel(level);
        }

        /// <summary>Skill points earned by lifetime XP: one per level gained after Level 1.</summary>
        public static int SkillPointsEarned(int totalXp) => (LevelForTotalXp(totalXp) - MinLevel) * SkillPointsPerLevel;
    }
}
