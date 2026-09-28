using RuinRail.Core;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// The biome treatment of dungeon chests (presentation only): one multiply tint on the chest's world art for the
    /// biome the depth is in — Ruined Metro steel blue, Rustworks rust and ember, Overgrown Labs bio green, the same
    /// palette language the biome's enemies wear — so the shared crate reads as part of each biome. The Boss Cache takes
    /// the biome tint leaned toward a gold accent and is drawn larger, so it still reads as the depth's big reward next
    /// to a normal chest. The state sprites (closed / opened / cache gate) swap under the tint, so every state keeps it;
    /// loot, spawn rules and interaction are untouched, and every peer derives it from the same layout biome.
    /// </summary>
    public static class ChestBiomePalette
    {
        public static readonly Color RuinedMetro = new(0.64f, 0.80f, 1f);
        public static readonly Color Rustworks = new(1f, 0.76f, 0.56f);
        public static readonly Color OvergrownLabs = new(0.76f, 1f, 0.68f);
        private static readonly Color BossGold = new(1f, 0.88f, 0.52f);

        /// <summary>How much larger the Boss Cache is drawn than a normal chest (its collider and interaction are unchanged).</summary>
        public const float BossCacheScale = 1.35f;

        public static Color TintFor(Biome biome, LootSourceKind kind)
        {
            var tint = biome switch
            {
                Biome.RuinedMetro => RuinedMetro,
                Biome.Rustworks => Rustworks,
                Biome.OvergrownLabs => OvergrownLabs,
                _ => Color.white
            };
            return kind == LootSourceKind.BossCache ? Color.Lerp(tint, BossGold, 0.55f) : tint;
        }

        /// <summary>Applies the biome treatment to a chest's world art.</summary>
        public static void Apply(SupplyChest chest, Biome biome)
        {
            var renderer = chest != null && chest.Visual != null ? chest.Visual.Renderer : null;
            if (renderer == null) return;
            renderer.color = TintFor(biome, chest.Kind);
            renderer.transform.localScale = Vector3.one * (chest.Kind == LootSourceKind.BossCache ? BossCacheScale : 1f);
        }
    }
}
