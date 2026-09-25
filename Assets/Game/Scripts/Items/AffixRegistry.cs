using System;
using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Gameplay.Items
{
    /// <summary>
    /// Every affix any item family can roll, by id.
    ///
    /// An <see cref="AffixRoll"/> persisted on an item stores only the affix id, and <see cref="AffixRollService"/>
    /// only ever picks from the rolled item's own <see cref="AffixPool"/> — so the pools reachable from the item
    /// registry are the complete, authoritative set of ids a save can contain. Pools share the same affix assets, so
    /// an id resolves to one definition regardless of which family rolled it.
    ///
    /// This exists because the run's composition root had no way to turn a persisted affix id back into an effect and
    /// passed a resolver that always returned null: every affix on every equipped item contributed nothing to the stat
    /// pipeline while still being priced, rolled and shown in tooltips.
    /// </summary>
    public sealed class AffixRegistry
    {
        private readonly Dictionary<string, AffixDefinition> _byId = new(StringComparer.Ordinal);

        public AffixRegistry(IEnumerable<AffixDefinition> affixes)
        {
            foreach (var affix in affixes ?? Enumerable.Empty<AffixDefinition>())
            {
                if (affix == null || string.IsNullOrEmpty(affix.Id)) continue;
                _byId[affix.Id] = affix;
            }
        }

        public int Count => _byId.Count;
        public IEnumerable<AffixDefinition> All => _byId.Values;

        /// <summary>Builds the registry from every affix pool reachable through the item definitions.</summary>
        public static AffixRegistry FromDefinitions(IEnumerable<ItemDefinition> definitions) =>
            new((definitions ?? Enumerable.Empty<ItemDefinition>())
                .OfType<EquipmentItemDefinition>()
                .Select(d => d.AffixPool)
                .Where(p => p != null)
                .SelectMany(p => p.Affixes));

        /// <summary>The affix an id names, or null for an id no current pool contains (a legacy save's removed affix).</summary>
        public AffixDefinition Get(string id) => id != null && _byId.TryGetValue(id, out var affix) ? affix : null;
    }
}
