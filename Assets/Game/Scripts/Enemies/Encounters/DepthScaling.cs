using System;
using System.Linq;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Encounters
{
    /// <summary>
    /// Pure depth scaling service: the same depth/context always yields the same multipliers. Composition order for
    /// enemy health is explicit (base -> depth -> party); enemy damage takes depth only, never party size (83).
    /// </summary>
    public static class DepthScaling
    {
        private static DepthScalingConfig _defaults;

        /// <summary>Built-in approved anchors, used when no config asset is supplied.</summary>
        public static DepthScalingConfig Defaults => _defaults != null ? _defaults : _defaults = ScriptableObject.CreateInstance<DepthScalingConfig>();

        public static float Interpolate(DepthScalingConfig.Anchor[] anchors, int depth)
        {
            if (anchors == null || anchors.Length == 0) return 1f;
            var ordered = anchors.OrderBy(a => a.Depth).ToArray();
            depth = Mathf.Max(1, depth);
            if (depth <= ordered[0].Depth) return ordered[0].Percent / 100f;
            if (depth >= ordered[^1].Depth) return ordered[^1].Percent / 100f; // past the last checkpoint the curve holds (cap)
            for (var i = 0; i < ordered.Length - 1; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];
                if (depth < a.Depth || depth > b.Depth) continue;
                var t = (depth - a.Depth) / (float)(b.Depth - a.Depth);
                return Mathf.Lerp(a.Percent, b.Percent, t) / 100f;
            }

            return ordered[^1].Percent / 100f;
        }

        public static float HealthMultiplier(int depth, DepthScalingConfig config = null) => Interpolate((config != null ? config : Defaults).HealthPercent, depth);
        public static float DamageMultiplier(int depth, DepthScalingConfig config = null) => Interpolate((config != null ? config : Defaults).DamagePercent, depth);

        /// <summary>Attack-frequency multiplier, clamped to the readability cap shared with EnemyController.</summary>
        public static float AttackSpeedMultiplier(int depth, DepthScalingConfig config = null)
        {
            return Mathf.Min(EnemyController.MaxAttackSpeedMultiplier, Interpolate((config != null ? config : Defaults).AttackSpeedPercent, depth));
        }

        public const float MaxMovementSpeedMultiplier = 1.1f;
        public static float MovementSpeedMultiplier(int depth, DepthScalingConfig config = null)
        {
            return Mathf.Min(MaxMovementSpeedMultiplier, Interpolate((config != null ? config : Defaults).MovementSpeedPercent, depth));
        }

        public const int MaxEliteChancePercent = 25;
        public static int EliteChancePercent(int depth, DepthScalingConfig config = null)
        {
            var bands = (config != null ? config : Defaults).EliteChancePercent;
            var percent = 0;
            foreach (var band in bands.OrderBy(b => b.FromDepth))
            {
                if (depth >= band.FromDepth) percent = band.Percent;
            }

            return Mathf.Min(MaxEliteChancePercent, percent);
        }

        /// <summary>Explicit composition: base -> depth curve -> party multiplier (83), rounded once, never below 1.</summary>
        public static int ScaledHealth(int baseHealth, int depth, int partySize = 1, bool isBoss = false, DepthScalingConfig config = null)
        {
            var party = isBoss ? PartyScaling.BossHealthMultiplier(partySize) : PartyScaling.NormalEnemyHealthMultiplier(partySize);
            return Mathf.Max(1, Mathf.RoundToInt(baseHealth * HealthMultiplier(depth, config) * party));
        }

        /// <summary>Integer damage band at depth. Party size is deliberately not a parameter (83: never party-scale damage).</summary>
        public static (int min, int max) ScaledDamage(int baseMin, int baseMax, int depth, DepthScalingConfig config = null)
        {
            var multiplier = DamageMultiplier(depth, config);
            var min = Mathf.Max(0, Mathf.RoundToInt(baseMin * multiplier));
            var max = Mathf.Max(min, Mathf.RoundToInt(baseMax * multiplier));
            return (min, max);
        }

        /// <summary>Loot rarity weights for a depth come from the authored table (59 baseline is RarityTable_Standard).</summary>
        public static int[] LootRarityWeights(RarityTableDefinition table, int depth) => table != null ? table.WeightsAt(depth) : Array.Empty<int>();

        /// <summary>Coin reward multiplier hook: EconomyConfig owns the (capped) curve.</summary>
        public static float CoinRewardMultiplier(EconomyConfig economy, int depth) => economy != null ? economy.CoinRewardMultiplier(depth) : 1f;
    }
}
