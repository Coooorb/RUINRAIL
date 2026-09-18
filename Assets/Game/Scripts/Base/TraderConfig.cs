using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Base
{
    /// <summary>
    /// Base Trader data (base/72_TRADER): offers per level (4/5/6), upgrade costs (2,500 / 7,000 Banked Coins) and
    /// the per-level offer rarity tables (permille). Category mix weights are V1 FINAL (TASK 179) (72 lists categories, not weights).
    /// </summary>
    [CreateAssetMenu(fileName = "TraderConfig", menuName = "RuinRail/Base/Trader Config")]
    public sealed class TraderConfig : ScriptableObject
    {
        [Serializable]
        public struct Level
        {
            [Min(1)] public int Offers;
            [Min(0)] public int UpgradeCost;
            public int CommonPermille;
            public int UncommonPermille;
            public int RarePermille;
            public int EpicPermille;
            public int LegendaryPermille;

            public int[] RarityWeights => new[] { CommonPermille, UncommonPermille, RarePermille, EpicPermille, LegendaryPermille };
        }

        [SerializeField] private Level[] _levels =
        {
            new() { Offers = 4, UpgradeCost = 0, CommonPermille = 500, UncommonPermille = 350, RarePermille = 130, EpicPermille = 19, LegendaryPermille = 1 },
            new() { Offers = 5, UpgradeCost = 2500, CommonPermille = 350, UncommonPermille = 400, RarePermille = 200, EpicPermille = 47, LegendaryPermille = 3 },
            new() { Offers = 6, UpgradeCost = 7000, CommonPermille = 250, UncommonPermille = 380, RarePermille = 280, EpicPermille = 84, LegendaryPermille = 6 }
        };

        [Header("Category mix per offer (weights, V1 FINAL (TASK 179))")]
        [SerializeField, Min(0)] private int _equipmentWeight = 50;
        [SerializeField, Min(0)] private int _consumableWeight = 30;
        [SerializeField, Min(0)] private int _ammoWeight = 20;

        public int MaxLevel => _levels.Length;
        public IReadOnlyList<Level> Levels => _levels;
        public int EquipmentWeight => _equipmentWeight;
        public int ConsumableWeight => _consumableWeight;
        public int AmmoWeight => _ammoWeight;

        public Level GetLevel(int level) => _levels[Mathf.Clamp(level, 1, MaxLevel) - 1];

        /// <summary>Cost to go from <paramref name="currentLevel"/> to the next one; 0 when already at max.</summary>
        public int UpgradeCostFrom(int currentLevel) => currentLevel >= MaxLevel ? 0 : _levels[currentLevel].UpgradeCost;
    }
}
