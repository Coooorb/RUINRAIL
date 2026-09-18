using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>Source quality selects the rarity table (58: Treasure/Boss use improved / strongly improved tables).</summary>
    public enum LootQuality
    {
        Standard,
        Improved,
        StronglyImproved
    }

    /// <summary>
    /// Data-driven loot table: an ordered list of rolls. Each roll fires with a chance and picks one weighted entry;
    /// an entry is either an item (with quantity range) or coins (definition left empty, quantity = coin amount).
    /// Equipment entries get their rarity from the source-quality rarity table and their affixes from AffixRollService.
    /// </summary>
    [CreateAssetMenu(fileName = "LootTable_", menuName = "RuinRail/Loot/Loot Table")]
    public sealed class LootTableDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Leave empty for a Coins entry.")]
            public ItemDefinition Item;
            [Tooltip("Legendary-only definition produced instead of Item when the rarity roll lands on Legendary.")]
            public EquipmentItemDefinition LegendaryVariant;
            [Min(1)] public int Weight = 1;
            [Min(1)] public int MinQuantity = 1;
            [Min(1)] public int MaxQuantity = 1;

            public bool IsCoins => Item == null;
        }

        [Serializable]
        public sealed class Roll
        {
            public string Label;
            [Range(0, 100)] public int ChancePercent = 100;
            public Entry[] Entries = Array.Empty<Entry>();
        }

        [SerializeField] private string _id;
        [SerializeField] private Roll[] _rolls = Array.Empty<Roll>();

        public string Id => _id;
        public IReadOnlyList<Roll> Rolls => _rolls;
    }
}
