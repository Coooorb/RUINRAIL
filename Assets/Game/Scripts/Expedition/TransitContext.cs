using System.Collections.Generic;
using System.Globalization;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>
    /// The factual context shown beside a Transit decision: where the party is, where descending leads, what is at risk
    /// and what the personal best is.
    ///
    /// Deliberately free of advice. There is no "recommended", no "best choice", no prediction and no comparison of the
    /// two options — the player is given the numbers they already own and decides. The deep-depth reward line appears
    /// only once the bonus is actually in force, so it reports a live multiplier rather than promising one.
    /// </summary>
    public static class TransitContext
    {
        /// <summary>One line per fact, in display order. Empty when there is no active expedition.</summary>
        public static List<string> Lines(ExpeditionService expedition)
        {
            var lines = new List<string>();
            if (expedition?.State == null) return lines;

            var state = expedition.State;
            lines.Add($"Current depth: {state.Depth}");
            lines.Add($"Next depth: {state.Depth + 1}");
            lines.Add(expedition.DeepestDepthReached > 0
                ? $"Personal best: depth {expedition.DeepestDepthReached}"
                : "Personal best: none recorded yet");
            lines.Add($"Carried coins at risk: {state.CarriedCoins}");

            var (xp, coins) = expedition.DeepDepthRewardMultipliers;
            var nextXp = expedition.RewardMultipliersAt(state.Depth + 1);
            if (xp > 1f || coins > 1f)
                lines.Add($"Deep-depth reward bonus here: XP x{Format(xp)}, coins x{Format(coins)}");
            if (nextXp.Xp > 1f || nextXp.Coins > 1f)
                lines.Add($"Deep-depth reward bonus at depth {state.Depth + 1}: XP x{Format(nextXp.Xp)}, coins x{Format(nextXp.Coins)}");

            return lines;
        }

        private static string Format(float multiplier) => multiplier.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
