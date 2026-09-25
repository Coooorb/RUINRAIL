using System;
using RuinRail.Gameplay.Loot;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// Rolls an event reward from an approved loot source kind (58 tables) at an event-configured quality, on the
    /// event's own deterministic loot substream. Pure: the same context always yields the same result.
    /// </summary>
    public sealed class EventRewardRoller
    {
        private readonly LootSourceCatalog _catalog;
        private readonly LootRoller _roller;

        public EventRewardRoller(LootSourceCatalog catalog, RuinRail.Gameplay.Economy.EconomyConfig economy = null)
        {
            _catalog = catalog != null ? catalog : throw new ArgumentNullException(nameof(catalog));
            _roller = catalog.CreateRoller(economy);
        }

        public LootResult Roll(DungeonEventContext context, LootSourceKind source, LootQuality quality, int salt = 0)
        {
            if (!_catalog.TryGet(source, out var entry) || entry.Table == null)
            {
                var empty = new LootResult();
                empty.Warnings.Add($"Loot source {source} is not configured in the catalog.");
                return empty;
            }

            return _roller.Roll(entry.Table, context.ForLoot(quality, salt));
        }

        /// <summary>Rolls an explicitly authored table (event-specific pools such as the Broken Machine's).</summary>
        public LootResult Roll(DungeonEventContext context, LootTableDefinition table, LootQuality quality, int salt = 0)
        {
            if (table == null)
            {
                var empty = new LootResult();
                empty.Warnings.Add("Event loot table is not assigned.");
                return empty;
            }

            return _roller.Roll(table, context.ForLoot(quality, salt));
        }
    }
}
