using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>58: exactly four chest/source types. No coloured chest tiers.</summary>
    public enum LootSourceKind
    {
        SupplyChest,
        EquipmentChest,
        TreasureChest,
        BossCache
    }

    /// <summary>
    /// Maps each approved source kind to its loot table and source quality, and holds the rarity table per quality.
    /// Chest components are configured through this catalog, so a chest type is data (table + quality), never a class.
    /// </summary>
    [CreateAssetMenu(fileName = "LootSourceCatalog", menuName = "RuinRail/Loot/Loot Source Catalog")]
    public sealed class LootSourceCatalog : ScriptableObject
    {
        [Serializable]
        public struct Source
        {
            public LootSourceKind Kind;
            public LootTableDefinition Table;
            public LootQuality Quality;
        }

        [SerializeField] private Source[] _sources = Array.Empty<Source>();
        [SerializeField] private RarityTableDefinition[] _rarityTables = Array.Empty<RarityTableDefinition>();

        public IReadOnlyList<Source> Sources => _sources;
        public IReadOnlyList<RarityTableDefinition> RarityTables => _rarityTables;

        public bool TryGet(LootSourceKind kind, out Source source)
        {
            foreach (var s in _sources)
            {
                if (s.Kind == kind) { source = s; return true; }
            }

            source = default;
            return false;
        }

        public RarityTableDefinition RarityTableFor(LootQuality quality)
        {
            return _rarityTables.FirstOrDefault(t => t != null && t.Quality == quality) ?? _rarityTables.FirstOrDefault(t => t != null && t.Quality == LootQuality.Standard);
        }

        public LootRoller CreateRoller() => new(RarityTableFor);

        /// <summary>Configures a chest for a source kind: table, quality and a per-source deterministic context.</summary>
        public bool Configure(SupplyChest chest, LootSourceKind kind, int runSeed, int depth, int sourceIndex, int partySize = 1, IReadOnlyCollection<Items.AmmoType> usefulAmmoTypes = null, LootSpawner spawner = null)
        {
            if (chest == null || !TryGet(kind, out var source) || source.Table == null) return false;
            var context = LootContext.ForSource(runSeed, depth, sourceIndex, source.Quality, partySize, usefulAmmoTypes);
            chest.Configure(source.Table, source.Quality, context, CreateRoller(), spawner);
            chest.SetKind(kind);
            return true;
        }
    }
}
