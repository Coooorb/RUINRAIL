using System;
using System.Collections.Generic;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Deterministic application boundary: sums integer percent bonuses per stat across rolled affixes and applies
    /// the global cap once on the combined total (never per-source).
    /// </summary>
    public static class AffixStatAggregator
    {
        public static int TotalPercent(IEnumerable<AffixRoll> rolls, Func<string, AffixDefinition> resolveAffix, AffixStat stat)
        {
            if (rolls == null || resolveAffix == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var roll in rolls)
            {
                var definition = resolveAffix(roll.AffixId);
                if (definition != null && definition.Stat == stat)
                {
                    total += Math.Max(0, roll.Value);
                }
            }

            return total;
        }

        public static int ApplyCap(int totalPercent, AffixStat stat, GlobalStatCapsConfig caps)
        {
            if (totalPercent <= 0)
            {
                return 0;
            }

            var cap = caps == null ? 0 : caps.GetCapPercent(stat);
            return cap > 0 ? Math.Min(totalPercent, cap) : totalPercent;
        }

        public static int CappedTotalPercent(IEnumerable<AffixRoll> rolls, Func<string, AffixDefinition> resolveAffix, AffixStat stat, GlobalStatCapsConfig caps)
        {
            return ApplyCap(TotalPercent(rolls, resolveAffix, stat), stat, caps);
        }
    }
}
