using RuinRail.Core;
using UnityEngine;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// One biome's material vocabulary for the environment layer: the tints its shadows, grime, stains, cracks, paint,
    /// debris and lamp pools are drawn in, how large its floor slabs run, and how heavy each pass is.
    ///   Ruined Metro — concrete transit platform: damp soot, oil and water stains, faded safety-yellow edge strip,
    ///     concrete rubble and ticket litter, sodium/fluorescent lamp pools.
    ///   Rustworks — heavy dirty industry: oil, rust bleed and soot, faded hazard stripes, scrap and copper offcuts,
    ///     furnace glow.
    ///   Overgrown Labs — damaged biological research: biomass and dried specimen fluid, moss and roots creeping from
    ///     the walls, glass shards and loose files, bio-green and violet specimen glow.
    ///   Cryo Vaults — abandoned industrial cold storage: frost creeping from walls and corners, meltwater and coolant,
    ///     pale floor guide lines, ice chunks and amber stencil labels, cold cyan with sparse amber emergency light.
    /// Every tint is muted and kept off the saturated reds the combat telegraphs own (art/104), so dressing can never
    /// be mistaken for a danger marker.
    /// </summary>
    internal sealed class RoomEnvironmentStyle
    {
        public Biome Biome;
        public Color32 Shadow, Grime, Stain, Stain2, Crack, CrackLight, Wear, Paint, Paint2;
        public Color32 DebrisDark, Debris, DebrisLight, Accent, Accent2, Organic, OrganicLight;
        public Color32 Glow, Glow2, WallStain, WallStreak;
        /// <summary>Edge creep reads as dirt (darken) everywhere except Cryo, where it is frost (lighten).</summary>
        public bool EdgeCreepIsLight;
        public float EdgeCreepAlpha;
        public float StainDensity;
        /// <summary>Weighted slab shapes (w, h, weight) the floor tiles are merged into.</summary>
        public (int W, int H, int Weight)[] Slabs;

        public static RoomEnvironmentStyle For(Biome biome) => biome switch
        {
            Biome.Rustworks => new RoomEnvironmentStyle
            {
                Biome = biome,
                Shadow = Hex("#120C08"), Grime = Hex("#2B1F17"), Stain = Hex("#15110E"), Stain2 = Hex("#6B3A1F"),
                Crack = Hex("#1A1715"), CrackLight = Hex("#5F5955"), Wear = Hex("#7A746C"),
                Paint = Hex("#B8932F"), Paint2 = Hex("#1A1715"),
                DebrisDark = Hex("#2A2420"), Debris = Hex("#4A4440"), DebrisLight = Hex("#80766C"),
                Accent = Hex("#8E5428"), Accent2 = Hex("#683522"), Organic = Hex("#3A2A1C"), OrganicLight = Hex("#5A4636"),
                Glow = Hex("#FF8A3D"), Glow2 = Hex("#FFC86B"), WallStain = Hex("#1E1612"), WallStreak = Hex("#7A3E1E"),
                EdgeCreepAlpha = 0.5f, StainDensity = 1.3f,
                Slabs = new[] { (2, 1, 30), (2, 2, 18), (1, 1, 40), (1, 2, 12) }
            },
            Biome.OvergrownLabs => new RoomEnvironmentStyle
            {
                Biome = biome,
                Shadow = Hex("#16201B"), Grime = Hex("#34482A"), Stain = Hex("#46652A"), Stain2 = Hex("#7A6A3A"),
                Crack = Hex("#4A504A"), CrackLight = Hex("#A8AEA6"), Wear = Hex("#AEB5AC"),
                Paint = Hex("#487786"), Paint2 = Hex("#D9DBD2"),
                DebrisDark = Hex("#3E4A4E"), Debris = Hex("#6FA8B5"), DebrisLight = Hex("#D4F1F6"),
                Accent = Hex("#D9DBD2"), Accent2 = Hex("#9AA096"), Organic = Hex("#2C4524"), OrganicLight = Hex("#5F8A3A"),
                Glow = Hex("#9CFF8A"), Glow2 = Hex("#C49BFF"), WallStain = Hex("#4A5A42"), WallStreak = Hex("#3F5A30"),
                EdgeCreepAlpha = 0.55f, StainDensity = 1.2f,
                Slabs = new[] { (1, 1, 55), (2, 2, 25), (2, 1, 20) }
            },
            Biome.CryoVaults => new RoomEnvironmentStyle
            {
                Biome = biome,
                Shadow = Hex("#080D12"), Grime = Hex("#D2ECF4"), Stain = Hex("#22303A"), Stain2 = Hex("#5FA9BA"),
                Crack = Hex("#1A2127"), CrackLight = Hex("#8FA6B2"), Wear = Hex("#5E6E7A"),
                Paint = Hex("#9FD3E0"), Paint2 = Hex("#D9B85A"),
                DebrisDark = Hex("#5D7A88"), Debris = Hex("#A9D3E0"), DebrisLight = Hex("#F2FBFF"),
                Accent = Hex("#C9A85A"), Accent2 = Hex("#4B565E"), Organic = Hex("#BFE3EE"), OrganicLight = Hex("#FFFFFF"),
                Glow = Hex("#7FDFFF"), Glow2 = Hex("#FFB347"), WallStain = Hex("#1B232A"), WallStreak = Hex("#CFEAF3"),
                EdgeCreepIsLight = true, EdgeCreepAlpha = 0.4f, StainDensity = 0.9f,
                Slabs = new[] { (1, 2, 35), (2, 2, 20), (1, 1, 35), (2, 1, 10) }
            },
            _ => new RoomEnvironmentStyle
            {
                Biome = Biome.RuinedMetro,
                Shadow = Hex("#0E1214"), Grime = Hex("#2A2A25"), Stain = Hex("#22282A"), Stain2 = Hex("#33454D"),
                Crack = Hex("#25282A"), CrackLight = Hex("#6E726F"), Wear = Hex("#8A8E8A"),
                Paint = Hex("#C9A227"), Paint2 = Hex("#B9B49C"),
                DebrisDark = Hex("#333638"), Debris = Hex("#6A6E6B"), DebrisLight = Hex("#9A9A94"),
                Accent = Hex("#B9B49C"), Accent2 = Hex("#7C5A3A"), Organic = Hex("#3B4630"), OrganicLight = Hex("#566241"),
                Glow = Hex("#FFB347"), Glow2 = Hex("#A8D8FF"), WallStain = Hex("#2A2D2B"), WallStreak = Hex("#3D4446"),
                EdgeCreepAlpha = 0.42f, StainDensity = 1.1f,
                Slabs = new[] { (2, 2, 35), (2, 1, 22), (1, 2, 13), (3, 2, 12), (1, 1, 18) }
            }
        };
    }
}
