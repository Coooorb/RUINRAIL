using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Per-biome prop, door, rail, decal, foreground and environment-VFX packages
    /// (FINAL_ART_PRODUCTION_SPEC sections 13-15).
    ///
    /// Each package is a sheet of 32x32 cells so room dressing can pick from it on the same grid the tiles use.
    /// Content follows each biome's named prop list — benches and ticket machines for Metro, presses and scrap bins
    /// for Rustworks, lab benches and containment for Labs — drawn from that biome's palette so dressing never
    /// reads as imported from elsewhere.
    ///
    /// Props are deliberately quieter in value than characters and hazards: spec 24.5 requires that dressing never
    /// hides a navigable corridor or an actor.
    /// </summary>
    public static class BiomeDressingFactory
    {
        public const int Cell = 32;

        public static readonly string[] Categories = { "props", "doors", "rail_transit", "decals", "foreground", "environment_vfx" };

        /// <summary>How many 32x32 variants each package carries.</summary>
        public static int VariantCount(string category) => category switch
        {
            "props" => 6,
            "doors" => 3,
            "rail_transit" => 4,
            "decals" => 6,
            "foreground" => 4,
            "environment_vfx" => 4,
            _ => 4
        };

        public static PixelCanvas BuildPackage(TileFactory.Biome biome, string category)
        {
            var frames = new List<PixelCanvas>();
            for (var i = 0; i < VariantCount(category); i++) frames.Add(BuildVariant(biome, category, i));
            return PixelCanvas.Row(frames);
        }

        public static PixelCanvas BuildVariant(TileFactory.Biome biome, string category, int variant)
        {
            var c = new PixelCanvas(Cell, Cell);
            var rng = new System.Random((int)biome * 7919 + category.GetHashCode() + variant * 31);
            var p = PaletteFor(biome);

            switch (category)
            {
                case "props": Prop(c, p, variant, rng, biome); break;
                case "doors": DoorProp(c, p, variant, biome); break;
                case "rail_transit": RailProp(c, p, variant, rng, biome); break;
                case "decals": Decal(c, p, variant, rng, biome); break;
                case "foreground": Foreground(c, p, variant, rng, biome); break;
                default: EnvironmentVfx(c, p, variant, rng, biome); break;
            }

            // Decals and environment VFX sit flat on the floor and read better without a hard outline.
            if (category is not ("decals" or "environment_vfx"))
                c.SelectiveOutline(p.Outline);

            return c;
        }

        private readonly struct Palette
        {
            public readonly RuinPalette.Ramp Body;
            public readonly RuinPalette.Ramp Metal;
            public readonly Color32 Accent;
            public readonly Color32 Glow;
            public readonly Color32 Outline;
            public readonly Color32 Organic;

            public Palette(RuinPalette.Ramp body, RuinPalette.Ramp metal, Color32 accent, Color32 glow, Color32 outline, Color32 organic)
            {
                Body = body; Metal = metal; Accent = accent; Glow = glow; Outline = outline; Organic = organic;
            }
        }

        private static Palette PaletteFor(TileFactory.Biome biome) => biome switch
        {
            TileFactory.Biome.RuinedMetro => new Palette(
                RuinPalette.RampOf(RuinPalette.Hex("#4A5254")), RuinPalette.SteelRamp,
                RuinPalette.WarningOchre, RuinPalette.AmberActive, RuinPalette.OutlineCharcoal, RuinPalette.DeepOlive),
            TileFactory.Biome.Rustworks => new Palette(
                RuinPalette.RampOf(RuinPalette.Hex("#57504A")), RuinPalette.RampOf(RuinPalette.Rust),
                RuinPalette.DirtyYellow, RuinPalette.OxideOrange, RuinPalette.OutlineRustBlack, RuinPalette.BurntRustDark),
            _ => new Palette(
                RuinPalette.RampOf(RuinPalette.Hex("#9BA29C")), RuinPalette.RampOf(RuinPalette.PaleSteel),
                RuinPalette.ElectricCyan, RuinPalette.TerminalGreen, RuinPalette.OutlineGreenBlack, RuinPalette.Olive)
        };

        // ---- props: the biome's named obstacle furniture ----
        private static void Prop(PixelCanvas c, Palette p, int variant, System.Random rng, TileFactory.Biome biome)
        {
            switch (variant % 6)
            {
                case 0: // bench / lab bench / conveyor stub
                    c.Rect(2, 8, 28, 6, p.Body.Base);
                    c.ShadeForm(2, 8, 28, 6, p.Body.Base, p.Body);
                    c.Rect(4, 3, 3, 6, p.Metal.Base);
                    c.Rect(25, 3, 3, 6, p.Metal.Base);
                    c.ShadeForm(4, 3, 24, 6, p.Metal.Base, p.Metal);
                    break;
                case 1: // cabinet / press / server rack
                    c.Rect(6, 2, 20, 26, p.Body.Base);
                    c.ShadeForm(6, 2, 20, 26, p.Body.Base, p.Body);
                    for (var y = 6; y < 26; y += 6) c.Line(8, y, 23, y, RuinPalette.Darken(p.Body.Base, 0.4f));
                    c.Rect(20, 22, 3, 2, p.Glow);
                    break;
                case 2: // crate / scrap bin / crate of samples
                    c.Rect(4, 4, 24, 20, p.Body.Base);
                    c.ShadeForm(4, 4, 24, 20, p.Body.Base, p.Body);
                    c.RectOutline(4, 4, 24, 20, RuinPalette.Darken(p.Body.Base, 0.45f));
                    c.Line(4, 14, 27, 14, RuinPalette.Darken(p.Body.Base, 0.35f));
                    c.Rect(13, 12, 6, 4, p.Metal.Shadow);
                    break;
                case 3: // pipe manifold / conduit cluster
                    for (var i = 0; i < 3; i++)
                    {
                        var x = 5 + i * 8;
                        c.Rect(x, 2, 5, 28, p.Metal.Base);
                        c.ShadeForm(x, 2, 5, 28, p.Metal.Base, p.Metal);
                        c.Rect(x - 1, 12 + i * 3, 7, 3, RuinPalette.Darken(p.Metal.Base, 0.3f));
                    }
                    break;
                case 4: // debris cluster
                    for (var i = 0; i < 7; i++)
                    {
                        var w = 4 + rng.Next(7);
                        c.Rect(2 + rng.Next(26 - w), 2 + rng.Next(12), w, 3 + rng.Next(4),
                            i % 2 == 0 ? p.Body.Shadow : p.Metal.Shadow);
                    }
                    c.ShadeForm(0, 0, Cell, Cell, p.Body.Shadow, p.Body);
                    break;
                default: // biome signature: electrical box / furnace unit / containment pod
                    c.Rect(7, 3, 18, 24, p.Metal.Base);
                    c.ShadeForm(7, 3, 18, 24, p.Metal.Base, p.Metal);
                    c.Rect(10, 10, 12, 12, RuinPalette.Darken(p.Glow, 0.6f));
                    c.Rect(11, 11, 10, 10, RuinPalette.Darken(p.Glow, 0.25f));
                    if (biome == TileFactory.Biome.OvergrownLabs) Vines(c, p.Organic, rng, 3);
                    break;
            }
        }

        private static void DoorProp(PixelCanvas c, Palette p, int variant, TileFactory.Biome biome)
        {
            var open = variant == 1;
            var locked = variant == 2;

            c.Rect(1, 0, 30, 30, p.Metal.Base);
            c.ShadeForm(1, 0, 30, 30, p.Metal.Base, p.Metal);
            c.RectOutline(1, 0, 30, 30, RuinPalette.Darken(p.Metal.Base, 0.5f));

            if (open)
            {
                // Leaves retracted to the sides: open state must read instantly (spec 13 doors).
                c.Rect(7, 2, 18, 26, RuinPalette.NearBlack);
                c.Rect(2, 2, 5, 26, p.Body.Base);
                c.Rect(25, 2, 5, 26, p.Body.Base);
            }
            else
            {
                c.Rect(15, 2, 2, 26, RuinPalette.Darken(p.Metal.Base, 0.55f));
                c.Rect(3, 4, 26, 3, RuinPalette.Darken(p.Body.Base, 0.2f));
                c.Rect(3, 23, 26, 3, RuinPalette.Darken(p.Body.Base, 0.2f));
            }

            c.Rect(13, 13, 6, 4, locked ? RuinPalette.EmergencyRed : p.Glow);
            for (var x = 2; x < 30; x += 6) c.Line(x, 0, x + 3, 2, p.Accent);
        }

        private static void RailProp(PixelCanvas c, Palette p, int variant, System.Random rng, TileFactory.Biome biome)
        {
            switch (variant % 4)
            {
                case 0: // track with sleepers
                    for (var y = 1; y < Cell; y += 8) c.Rect(0, y, Cell, 4, RuinPalette.Darken(p.Body.Base, 0.35f));
                    c.Rect(7, 0, 4, Cell, p.Metal.Light);
                    c.Rect(21, 0, 4, Cell, p.Metal.Light);
                    c.ShadeForm(7, 0, 18, Cell, p.Metal.Light, p.Metal);
                    break;
                case 1: // platform edge with a safety stripe
                    c.Rect(0, 0, Cell, 18, p.Body.Base);
                    c.ShadeForm(0, 0, Cell, 18, p.Body.Base, p.Body);
                    c.Rect(0, 18, Cell, 4, p.Accent);
                    c.Rect(0, 22, Cell, 2, RuinPalette.Darken(p.Accent, 0.45f));
                    break;
                case 2: // signal box
                    c.Rect(9, 2, 14, 20, p.Metal.Base);
                    c.ShadeForm(9, 2, 14, 20, p.Metal.Base, p.Metal);
                    c.Rect(12, 16, 3, 3, RuinPalette.EmergencyRed);
                    c.Rect(17, 16, 3, 3, p.Glow);
                    c.Line(16, 22, 16, 30, RuinPalette.Darken(p.Metal.Base, 0.4f));
                    break;
                default: // cable conduit run
                    for (var i = 0; i < 4; i++)
                    {
                        var y = 5 + i * 6;
                        c.Line(0, y, Cell - 1, y + rng.Next(3) - 1, RuinPalette.Darken(p.Body.Base, 0.3f), 2);
                    }
                    c.Rect(12, 2, 8, 28, RuinPalette.Darken(p.Metal.Base, 0.15f));
                    break;
            }
        }

        private static void Decal(PixelCanvas c, Palette p, int variant, System.Random rng, TileFactory.Biome biome)
        {
            switch (variant % 6)
            {
                case 0: // directional arrow, faded
                    c.Taper(16, 22, 12, 0, 7, RuinPalette.Darken(p.Accent, 0.3f));
                    c.Rect(13, 6, 6, 8, RuinPalette.Darken(p.Accent, 0.3f));
                    break;
                case 1: // stain
                    for (var i = 0; i < 5; i++)
                        c.Ellipse(8 + rng.Next(16), 8 + rng.Next(16), 4 + rng.Next(4), 3 + rng.Next(3),
                            RuinPalette.Darken(p.Body.Base, 0.35f));
                    break;
                case 2: // maintenance number block
                    for (var i = 0; i < 3; i++)
                        PixelFontFactory.DrawGlyph(c, (char)('0' + rng.Next(10)), 8 + i * 6, 20,
                            RuinPalette.Darken(p.Body.Light, 0.15f));
                    break;
                case 3: // scorch
                    for (var i = 0; i < 24; i++)
                    {
                        var a = rng.NextDouble() * Math.PI * 2;
                        var r = rng.Next(12);
                        c.Set(16 + (int)(Math.Cos(a) * r), 16 + (int)(Math.Sin(a) * r), RuinPalette.Darken(p.Outline, 0f));
                    }
                    break;
                case 4: // cracks
                    for (var i = 0; i < 3; i++)
                    {
                        int x = rng.Next(Cell), y = rng.Next(Cell);
                        for (var s = 0; s < 12; s++)
                        {
                            c.Set(x, y, RuinPalette.Darken(p.Body.Base, 0.45f));
                            x += rng.Next(3) - 1; y += rng.Next(3) - 1;
                        }
                    }
                    break;
                default: // organic growth / oil film / grime, per biome
                    var col = biome == TileFactory.Biome.OvergrownLabs ? p.Organic
                        : biome == TileFactory.Biome.Rustworks ? RuinPalette.Darken(RuinPalette.BurntRustDark, 0.1f)
                        : RuinPalette.Darken(RuinPalette.DeepOlive, 0.2f);
                    for (var i = 0; i < 6; i++)
                        c.Ellipse(rng.Next(Cell), rng.Next(Cell), 3 + rng.Next(4), 2 + rng.Next(3), col);
                    break;
            }
        }

        private static void Foreground(PixelCanvas c, Palette p, int variant, System.Random rng, TileFactory.Biome biome)
        {
            // Foreground pieces hang above the play field, so they are darker and read as occluders.
            var dark = RuinPalette.Darken(p.Body.Base, 0.45f);
            switch (variant % 4)
            {
                case 0: // overhead beam
                    c.Rect(0, 20, Cell, 10, dark);
                    c.Line(0, 20, Cell - 1, 20, RuinPalette.Darken(dark, 0.3f));
                    for (var x = 3; x < Cell; x += 8) c.Rect(x, 22, 3, 6, RuinPalette.Darken(dark, 0.2f));
                    break;
                case 1: // hanging cables
                    for (var i = 0; i < 4; i++)
                    {
                        var x = 4 + i * 7;
                        c.Line(x, Cell - 1, x + rng.Next(3) - 1, 14 + rng.Next(8), dark, 2);
                    }
                    break;
                case 2: // pipe run overhead
                    c.Rect(0, 24, Cell, 7, dark);
                    c.Line(0, 30, Cell - 1, 30, RuinPalette.Darken(dark, 0.35f));
                    c.Rect(10, 21, 5, 4, RuinPalette.Darken(dark, 0.15f));
                    break;
                default: // vegetation / smoke canopy
                    var col = biome == TileFactory.Biome.OvergrownLabs ? RuinPalette.Darken(p.Organic, 0.25f) : dark;
                    for (var i = 0; i < 8; i++)
                        c.Ellipse(rng.Next(Cell), 20 + rng.Next(12), 4 + rng.Next(5), 3 + rng.Next(4), col);
                    break;
            }
        }

        private static void EnvironmentVfx(PixelCanvas c, Palette p, int variant, System.Random rng, TileFactory.Biome biome)
        {
            switch (variant % 4)
            {
                case 0: // dust motes / sparks / spores
                    for (var i = 0; i < 12; i++) c.Set(rng.Next(Cell), rng.Next(Cell), p.Glow);
                    break;
                case 1: // steam or gas wisp
                    for (var i = 0; i < 5; i++)
                        c.Ellipse(10 + rng.Next(12), 6 + i * 5, 5 - i / 2f, 2.5f, RuinPalette.Darken(p.Body.Light, 0.1f));
                    break;
                case 2: // dripping / leak
                    for (var i = 0; i < 3; i++)
                    {
                        var x = 8 + i * 8;
                        c.Line(x, 30, x, 30 - 6 - rng.Next(10), p.Glow);
                    }
                    break;
                default: // flickering light pool on the floor
                    c.Ellipse(16, 16, 12, 7, RuinPalette.Darken(p.Glow, 0.7f));
                    c.Ellipse(16, 16, 8, 4.5f, RuinPalette.Darken(p.Glow, 0.5f));
                    break;
            }
        }

        private static void Vines(PixelCanvas c, Color32 col, System.Random rng, int count)
        {
            for (var i = 0; i < count; i++)
            {
                int x = rng.Next(Cell), y = rng.Next(Cell);
                for (var s = 0; s < 12; s++)
                {
                    c.Set(x, y, col);
                    x += rng.Next(3) - 1;
                    y += rng.Next(3) - 1;
                    if (x < 0 || x >= Cell || y < 0 || y >= Cell) break;
                }
            }
        }

        /// <summary>
        /// The final lighting values for a biome (spec 20), replacing the neutral placeholder tints.
        ///
        /// Intensity stays at or above the readability floor the runtime enforces: lighting is atmosphere, never a
        /// visibility mechanic, so the tint colours are gentle and no channel is crushed.
        /// </summary>
        public static (Color32 ambient, float intensity) LightingFor(TileFactory.Biome biome) => biome switch
        {
            // Every channel stays at or above the 0.75 readability floor the profile enforces (191/255). The tint is
            // therefore a lean, not a wash — which is the point: lighting carries mood, never visibility.
            //
            // Cool grey-green tunnel air with failing fixtures.
            TileFactory.Biome.RuinedMetro => (RuinPalette.Hex("#CCD8D0"), 1f),
            // Warm steel: heat sources are local props, so the ambient only leans warm.
            TileFactory.Biome.Rustworks => (RuinPalette.Hex("#D8CCC2"), 1f),
            // Cold pale cyan-grey laboratory light.
            _ => (RuinPalette.Hex("#C8D6D8"), 1f)
        };
    }
}
