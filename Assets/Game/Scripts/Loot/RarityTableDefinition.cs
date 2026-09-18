using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Depth-banded rarity weights (dungeon/59_DEPTH_SCALING "Loot Rarity V1 Baseline Targets"), stored in permille so
    /// 0.1% Legendary is exact. Weights between bands are linearly interpolated; beyond the last band they hold.
    /// One asset per source quality (standard / improved / strongly improved).
    /// </summary>
    [CreateAssetMenu(fileName = "RarityTable_", menuName = "RuinRail/Loot/Rarity Table")]
    public sealed class RarityTableDefinition : ScriptableObject
    {
        [Serializable]
        public struct Band
        {
            [Min(1)] public int Depth;
            [Min(0)] public int CommonPermille;
            [Min(0)] public int UncommonPermille;
            [Min(0)] public int RarePermille;
            [Min(0)] public int EpicPermille;
            [Min(0)] public int LegendaryPermille;

            public int Total => CommonPermille + UncommonPermille + RarePermille + EpicPermille + LegendaryPermille;

            public int Of(Rarity rarity)
            {
                return rarity switch
                {
                    Rarity.Common => CommonPermille,
                    Rarity.Uncommon => UncommonPermille,
                    Rarity.Rare => RarePermille,
                    Rarity.Epic => EpicPermille,
                    Rarity.Legendary => LegendaryPermille,
                    _ => 0
                };
            }
        }

        [SerializeField] private LootQuality _quality = LootQuality.Standard;
        [SerializeField] private Band[] _bands = Array.Empty<Band>();

        public LootQuality Quality => _quality;
        public IReadOnlyList<Band> Bands => _bands;

        /// <summary>Interpolated permille weight per rarity at the given depth (Common..Legendary order).</summary>
        public int[] WeightsAt(int depth)
        {
            var result = new int[5];
            if (_bands.Length == 0)
            {
                result[0] = 1000;
                return result;
            }

            var ordered = _bands.OrderBy(b => b.Depth).ToArray();
            if (depth <= ordered[0].Depth)
            {
                return Fill(ordered[0]);
            }

            for (var i = 0; i + 1 < ordered.Length; i++)
            {
                var a = ordered[i];
                var b = ordered[i + 1];
                if (depth >= a.Depth && depth <= b.Depth)
                {
                    var t = (depth - a.Depth) / (float)(b.Depth - a.Depth);
                    for (var r = 0; r < 5; r++)
                    {
                        result[r] = Mathf.RoundToInt(Mathf.Lerp(a.Of((Rarity)r), b.Of((Rarity)r), t));
                    }

                    return result;
                }
            }

            return Fill(ordered[^1]);
        }

        private static int[] Fill(Band band)
        {
            return new[] { band.CommonPermille, band.UncommonPermille, band.RarePermille, band.EpicPermille, band.LegendaryPermille };
        }
    }
}
