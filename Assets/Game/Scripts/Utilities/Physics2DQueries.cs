using UnityEngine;

namespace RuinRail.Gameplay
{
    /// <summary>
    /// TASK 177 — the one filter every migrated Physics2D query uses.
    ///
    /// The deprecated NonAlloc query overloads queried all layers and honoured the project's
    /// <see cref="Physics2D.queriesHitTriggers"/> setting. Their supported replacements take a
    /// <see cref="ContactFilter2D"/> instead, and a default-constructed one has <c>useTriggers = false</c> — which
    /// would silently stop every trigger-based pickup, interactable and hazard from being found.
    ///
    /// <see cref="ContactFilter2D.noFilter"/> gets the layer/depth/normal filtering right but forces triggers on.
    /// Reading the project setting instead keeps the migrated calls behaving exactly like the ones they replaced,
    /// including if that setting is ever changed.
    /// </summary>
    public static class Physics2DQueries
    {
        /// <summary>No layer, depth or normal filtering; triggers exactly as the project setting dictates.</summary>
        public static ContactFilter2D LegacyQueryFilter()
        {
            var filter = ContactFilter2D.noFilter;
            filter.useTriggers = Physics2D.queriesHitTriggers;
            return filter;
        }
    }
}
