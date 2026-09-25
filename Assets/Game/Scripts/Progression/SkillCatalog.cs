using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Stats;

namespace RuinRail.Gameplay.Progression
{
    /// <summary>
    /// The player-facing side of the six permanent attributes (player/13_LEVELING_AND_SKILL_POINTS). Names and one-line
    /// purposes are authored here; every number shown to the player is formatted from
    /// <see cref="SkillRules.ModifiersForRank"/>, so a displayed value can never drift from the ranks the run's stat
    /// pipeline actually applies. Descriptions therefore carry no numerals at all — the effect lines carry them.
    /// </summary>
    public static class SkillCatalog
    {
        /// <summary>One Skill Point buys exactly one rank (player/13: every level grants exactly 1 Skill Point).</summary>
        public const int PointCostPerRank = 1;

        /// <summary>Widest player-facing line the Character panel's data column can draw without clipping.</summary>
        public const int MaxLineCharacters = 32;

        public const string MaxedText = "MAX";
        public const string PointCostText = "1 POINT";
        public const string NoBonusText = "No bonus yet.";

        public static string DisplayName(SkillId skill) => skill switch
        {
            SkillId.Vitality => "VITALITY",
            SkillId.Power => "POWER",
            SkillId.Mobility => "MOBILITY",
            SkillId.Recovery => "RECOVERY",
            SkillId.Handling => "HANDLING",
            SkillId.Resilience => "RESILIENCE",
            _ => skill.ToString().ToUpperInvariant()
        };

        /// <summary>What the attribute does, in words only: the numbers live in <see cref="EffectRows"/>.</summary>
        public static string Description(SkillId skill) => skill switch
        {
            SkillId.Vitality => "Raises your maximum health.",
            SkillId.Power => "Raises the damage you deal.",
            SkillId.Mobility => "Raises your movement speed.",
            SkillId.Recovery => "Raises the healing you get.",
            SkillId.Handling => "Faster reloads and swaps.",
            SkillId.Resilience => "Resists knockback and stagger.",
            _ => string.Empty
        };

        /// <summary>The modifiers a rank is worth, straight from the authoritative rule.</summary>
        public static IReadOnlyList<StatModifier> ModifiersAt(SkillId skill, int rank) =>
            SkillRules.ModifiersForRank(skill, rank).ToList();

        /// <summary>The stats the attribute contributes to, in the pipeline's own order (rank-independent).</summary>
        public static IReadOnlyList<StatId> AffectedStats(SkillId skill) => ModifiersAt(skill, 1).Select(m => m.Stat).ToList();

        /// <summary>"Max HP +6" / "Reload Speed +3%, Weapon Switch Speed +3%" — formatted, never authored.</summary>
        public static string EffectText(SkillId skill, int rank)
        {
            var modifiers = ModifiersAt(skill, rank);
            return modifiers.Count == 0 ? NoBonusText : string.Join(", ", modifiers.Select(Describe));
        }

        /// <summary>The effect one more Skill Point would buy, or null at the rank cap.</summary>
        public static string NextRankEffectText(SkillId skill, int rank) =>
            rank >= SkillRules.MaxRank ? null : EffectText(skill, rank + 1);

        /// <summary>
        /// One panel line per affected stat: "Max HP +6-&gt;+8" below the cap, "Max HP +20 MAX" at it. The unit modifiers
        /// of rank 1 supply the stat and the kind, so rank 0 still prints an honest "+0" rather than nothing.
        /// </summary>
        public static IReadOnlyList<string> EffectRows(SkillId skill, int rank)
        {
            var unit = ModifiersAt(skill, 1);
            var now = ModifiersAt(skill, rank);
            var rows = new List<string>(unit.Count);
            var maxed = rank >= SkillRules.MaxRank;
            var next = maxed ? null : ModifiersAt(skill, rank + 1);
            for (var i = 0; i < unit.Count; i++)
            {
                var label = StatLabels.Of(unit[i].Stat);
                var nowText = StatLabels.Format(i < now.Count ? now[i] : new StatModifier(unit[i].Stat, unit[i].Kind, 0));
                rows.Add(maxed
                    ? $"{label} {nowText} {MaxedText}"
                    : $"{label} {nowText}->{StatLabels.Format(next[i])}");
            }

            return rows;
        }

        public static string RankText(int rank) => $"{rank} / {SkillRules.MaxRank}";

        private static string Describe(StatModifier modifier) => $"{StatLabels.Of(modifier.Stat)} {StatLabels.Format(modifier)}";
    }
}
