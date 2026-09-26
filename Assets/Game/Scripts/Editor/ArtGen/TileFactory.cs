using System;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>The five logic tile categories every biome must supply, matching the room baker's tilemaps.</summary>
    public enum TileRole { Floor, FloorDetail, Wall, Obstacle, Hazard }

    /// <summary>
    /// Generates the 32x32 biome tiles described in FINAL_ART_PRODUCTION_SPEC sections 13-15.
    ///
    /// The floors are built around one rule that the first pass got wrong: a large room is mostly floor, so the floor
    /// must be mostly calm. A 32x32 tile carrying its own noise, cracks and paint is fine in isolation and becomes
    /// wallpaper the moment it is laid out a few hundred times — the eye stops reading a surface and starts reading a
    /// grid. So the base tile of every biome is near-flat structural material with a seam, its variation comes from
    /// low-frequency value noise that wraps at the tile edge (no seam artefacts, no repeating motif), and everything
    /// interesting — wear, damage, panels, rust, moss — lives in the rarer family members that the room baker places
    /// in clusters.
    ///
    /// That is the same division the spec asks for in its screen-balance targets: 55-75% calm material, structure
    /// next, detail last. Every tile is generated from a fixed seed, so the set is reproducible byte-for-byte.
    /// </summary>
    public static class TileFactory
    {
        public const int Size = 32;

        public enum Biome { RuinedMetro, Rustworks, OvergrownLabs }

        /// <summary>
        /// The floor family every biome supplies (spec B1.4 of the polish pass).
        ///
        /// Ordered by how much of a room they should cover: <see cref="Base"/> is the great majority of every room,
        /// <see cref="Accent"/> is a handful of tiles. The room baker's weighting depends on this order.
        /// </summary>
        public enum FloorFamily
        {
            /// <summary>Calm structural material. The overwhelming majority of any room.</summary>
            Base,
            /// <summary>The same material, walked on: broad wear, no new structure.</summary>
            Worn,
            /// <summary>Damage, grouped rather than scattered.</summary>
            Cracked,
            /// <summary>A different constructed element — access panel, grate, utility plate.</summary>
            Utility,
            /// <summary>The biome's loudest floor note, used sparingly.</summary>
            Accent
        }

        public static readonly FloorFamily[] FloorFamilies = (FloorFamily[])Enum.GetValues(typeof(FloorFamily));

        /// <summary>Detail-layer variants, drawn over the base floor rather than replacing it (spec B6).</summary>
        public enum DetailKind
        {
            /// <summary>Broad grime, the quietest member and the one that may be used most.</summary>
            Grime,
            /// <summary>A linear marking: safety line, route stripe, lane edge.</summary>
            Marking,
            /// <summary>A recessed service feature: drain, cable channel, vent.</summary>
            Service,
            /// <summary>The biome's characteristic contamination or residue, localized.</summary>
            Residue
        }

        public static readonly DetailKind[] DetailKinds = (DetailKind[])Enum.GetValues(typeof(DetailKind));

        /// <summary>How many variants a role ships. Only the floor and its detail layer have a family.</summary>
        public static int VariantCount(TileRole role) => role switch
        {
            TileRole.Floor => FloorFamilies.Length,
            TileRole.FloorDetail => DetailKinds.Length,
            _ => 1
        };

        public static PixelCanvas Build(Biome biome, TileRole role, int variant = 0)
        {
            var c = new PixelCanvas(Size, Size);
            var seed = (int)biome * 977 + (int)role * 131 + variant * 17 + 5;

            switch (role)
            {
                case TileRole.Floor:
                    Floor(c, biome, (FloorFamily)Mathf.Clamp(variant, 0, FloorFamilies.Length - 1), seed);
                    break;
                case TileRole.FloorDetail:
                    Detail(c, biome, (DetailKind)Mathf.Clamp(variant, 0, DetailKinds.Length - 1), seed);
                    break;
                default:
                    Structure(c, biome, role, seed);
                    break;
            }

            return c;
        }

        // =====================================================================
        //  Floors
        // =====================================================================

        private static void Floor(PixelCanvas c, Biome biome, FloorFamily family, int seed)
        {
            switch (biome)
            {
                case Biome.RuinedMetro: MetroFloor(c, family, seed); break;
                case Biome.Rustworks: RustworksFloor(c, family, seed); break;
                default: LabsFloor(c, family, seed); break;
            }
        }

        // ---------------- Ruined Metro (spec 13) ----------------

        private static readonly Color32 MetroConcrete = RuinPalette.Hex("#4E5153");
        private static readonly Color32 MetroPlatform = RuinPalette.Hex("#585A57");

        private static void MetroFloor(PixelCanvas c, FloorFamily family, int seed)
        {
            var rng = new System.Random(seed);
            var ramp = CalmRamp(MetroConcrete);

            switch (family)
            {
                case FloorFamily.Base:
                    // Poured concrete slab. The cast seam on two edges is the only structure; laid out, the seams
                    // form the slab lattice a platform actually has.
                    Material(c, ramp, seed, cells: 4, shadowAt: 0.30f, lightAt: 0.76f);
                    SlabSeam(c, RuinPalette.Darken(MetroConcrete, 0.34f), RuinPalette.Lighten(MetroConcrete, 0.10f));
                    break;

                case FloorFamily.Worn:
                    // Walked-on concrete: one broad polished region, no new edges.
                    Material(c, ramp, seed, cells: 4, shadowAt: 0.34f, lightAt: 0.66f);
                    SlabSeam(c, RuinPalette.Darken(MetroConcrete, 0.34f), RuinPalette.Lighten(MetroConcrete, 0.10f));
                    Region(c, seed + 11, RuinPalette.Lighten(MetroConcrete, 0.085f), 0.40f, 3);
                    Region(c, seed + 29, RuinPalette.Darken(MetroConcrete, 0.11f), 0.24f, 3);
                    break;

                case FloorFamily.Cracked:
                    Material(c, ramp, seed, cells: 4, shadowAt: 0.30f, lightAt: 0.74f);
                    SlabSeam(c, RuinPalette.Darken(MetroConcrete, 0.34f), RuinPalette.Lighten(MetroConcrete, 0.10f));
                    // Two long cracks sharing an origin, so the damage reads as one event rather than two marks.
                    CrackSystem(c, rng, RuinPalette.Darken(MetroConcrete, 0.52f), RuinPalette.Darken(MetroConcrete, 0.30f));
                    Chip(c, rng, RuinPalette.Darken(MetroConcrete, 0.55f));
                    break;

                case FloorFamily.Utility:
                    // Maintenance access panel: one large steel plate set into the concrete.
                    Material(c, ramp, seed, cells: 4, shadowAt: 0.30f, lightAt: 0.78f);
                    SlabSeam(c, RuinPalette.Darken(MetroConcrete, 0.34f), RuinPalette.Lighten(MetroConcrete, 0.10f));
                    AccessPanel(c, 4, 4, 24, 24, RuinPalette.MidSteel, directional: true);
                    break;

                default:
                    // Station platform tile: a rectangular tile rhythm with worn grout and one chipped square.
                    Material(c, CalmRamp(MetroPlatform), seed, cells: 4, shadowAt: 0.30f, lightAt: 0.76f);
                    Grout(c, 8, RuinPalette.Darken(MetroPlatform, 0.34f), RuinPalette.Lighten(MetroPlatform, 0.12f));
                    var gx = rng.Next(4) * 8;
                    var gy = rng.Next(4) * 8;
                    c.Rect(gx + 1, gy + 1, 6, 6, RuinPalette.Darken(MetroConcrete, 0.42f));
                    c.Rect(gx + 2, gy + 2, 4, 4, RuinPalette.Darken(MetroConcrete, 0.30f));
                    break;
            }
        }

        // ---------------- Rustworks (spec 14) ----------------

        private static readonly Color32 SteelPlate = RuinPalette.Hex("#393E41");
        private static readonly Color32 SootConcrete = RuinPalette.Hex("#34352F");
        private static readonly Color32 HeatSteel = RuinPalette.Hex("#4A3A30");

        private static void RustworksFloor(PixelCanvas c, FloorFamily family, int seed)
        {
            var rng = new System.Random(seed);
            var plate = CalmRamp(SteelPlate);

            switch (family)
            {
                case FloorFamily.Base:
                    // A large blackened steel plate. Two rivets on one diagonal and nothing else — spec 14 asks for
                    // "fewer but clearer rivets", and this is the tile the player looks at in almost every frame, so
                    // its flat mass matters more than its fastenings. The rust that used to be sprayed over every
                    // tile is now a thing that happens at seams and at machines.
                    Material(c, plate, seed, cells: 4, shadowAt: 0.25f, lightAt: 0.81f);
                    PlateEdge(c, RuinPalette.Darken(SteelPlate, 0.42f), RuinPalette.Lighten(SteelPlate, 0.16f));
                    foreach (var (rx, ry) in new[] { (5, 5), (26, 26) }) Rivet(c, rx, ry);
                    break;

                case FloorFamily.Worn:
                    // Soot-stained industrial concrete beside the plating.
                    Material(c, WornRamp(SootConcrete), seed, cells: 4, shadowAt: 0.32f, lightAt: 0.74f);
                    Region(c, seed + 7, RuinPalette.Darken(SootConcrete, 0.17f), 0.38f, 3);
                    Region(c, seed + 23, RuinPalette.Lighten(SootConcrete, 0.06f), 0.26f, 3);
                    SlabSeam(c, RuinPalette.Darken(SootConcrete, 0.40f), RuinPalette.Lighten(SootConcrete, 0.08f));
                    break;

                case FloorFamily.Cracked:
                    // Heat-damaged plate: the steel has taken a burnt tint, not a coat of orange paint.
                    //
                    // Two earlier attempts here are worth recording. An orange gradient anchored to one edge made
                    // every cluster resolve into clean vertical stripes; rebuilding it from the wrapping noise field
                    // joined the clusters up but turned them into a polka-dot grid, because a field that wraps over a
                    // 32 px tile is periodic by construction and a loud periodic field is a visible pattern. The
                    // answer was not a better pattern but less contrast: the discolouration is now a material change
                    // at the same quiet level as the rest of the floor, so a cluster reads as a warm zone near the
                    // machinery. Full-strength orange stays where spec 14 puts it — hazards and machinery.
                    Material(c, CalmRamp(HeatSteel), seed, cells: 4, shadowAt: 0.28f, lightAt: 0.78f);
                    PlateEdge(c, RuinPalette.Darken(HeatSteel, 0.42f), RuinPalette.Lighten(HeatSteel, 0.16f));
                    Region(c, seed + 13, RuinPalette.Darken(RuinPalette.BurntRustDark, 0.10f), 0.28f, 3);
                    foreach (var (rx, ry) in new[] { (5, 5), (26, 26) }) Rivet(c, rx, ry);
                    break;

                case FloorFamily.Utility:
                    // Service walkway grating: strongly directional, and a zone rather than a room (spec B3).
                    //
                    // The void between the bars is a dark steel rather than a hole onto black. A full-contrast grate
                    // is the loudest thing that can sit under the player's feet, and Rustworks already spends its
                    // contrast budget on machinery and hazards — so the slots read as depth without competing with
                    // the things that matter.
                    Material(c, CalmRamp(RuinPalette.Hex("#2A2F33")), seed, cells: 4, shadowAt: 0.34f, lightAt: 0.80f);
                    for (var x = 2; x < Size; x += 6)
                    {
                        c.Rect(x, 0, 4, Size, RuinPalette.Darken(SteelPlate, 0.06f));
                        c.Line(x, 0, x, Size - 1, RuinPalette.Lighten(SteelPlate, 0.10f));
                        c.Line(x + 3, 0, x + 3, Size - 1, RuinPalette.Darken(SteelPlate, 0.30f));
                    }
                    // Cross bearers, which is what stops a grate reading as a barcode.
                    for (var y = 6; y < Size; y += 13) c.Rect(0, y, Size, 2, RuinPalette.Darken(SteelPlate, 0.18f));
                    break;

                default:
                    // Oil: a dark pool creeping from a seam, with a shallow edge where the plate still shows through.
                    //
                    // The rim is near-black rather than rust-coloured. A saturated rim at this size stopped reading as
                    // a fluid edge and started reading as a painted marking, which is not what an oil stain is.
                    Material(c, plate, seed, cells: 4, shadowAt: 0.30f, lightAt: 0.74f);
                    PlateEdge(c, RuinPalette.Darken(SteelPlate, 0.42f), RuinPalette.Lighten(SteelPlate, 0.16f));
                    RegionRimmed(c, seed + 31, RuinPalette.Hex("#0F1012"), RuinPalette.Darken(SteelPlate, 0.55f), 0.42f);
                    break;
            }
        }

        // ---------------- Overgrown Labs (spec 15) ----------------

        private static readonly Color32 LabPanel = RuinPalette.Hex("#7C837C");
        private static readonly Color32 LabUtility = RuinPalette.Hex("#5A625E");

        private static void LabsFloor(PixelCanvas c, FloorFamily family, int seed)
        {
            var rng = new System.Random(seed);
            var panel = CalmRamp(LabPanel);

            switch (family)
            {
                case FloorFamily.Base:
                    // A large clean-room panel: cold green-grey, one thin seam, almost no incident.
                    Material(c, panel, seed, cells: 4, shadowAt: 0.32f, lightAt: 0.76f);
                    SlabSeam(c, RuinPalette.Darken(LabPanel, 0.28f), RuinPalette.Lighten(LabPanel, 0.14f));
                    break;

                case FloorFamily.Worn:
                    Material(c, panel, seed, cells: 4, shadowAt: 0.34f, lightAt: 0.68f);
                    SlabSeam(c, RuinPalette.Darken(LabPanel, 0.28f), RuinPalette.Lighten(LabPanel, 0.14f));
                    Region(c, seed + 5, RuinPalette.Darken(LabPanel, 0.10f), 0.36f, 3);
                    Region(c, seed + 17, RuinPalette.Lighten(LabPanel, 0.07f), 0.22f, 3);
                    break;

                case FloorFamily.Cracked:
                    // A broken panel: the crack network opens onto the structure underneath.
                    //
                    // The opening is a dark grey-green rather than a near-black hole. There is only one Cracked tile
                    // per biome, so any strong localized feature sits at the same spot in every copy of it — a black
                    // rectangle would repeat far more obviously than the damage it is meant to depict.
                    Material(c, panel, seed, cells: 4, shadowAt: 0.32f, lightAt: 0.74f);
                    SlabSeam(c, RuinPalette.Darken(LabPanel, 0.28f), RuinPalette.Lighten(LabPanel, 0.14f));
                    CrackSystem(c, rng, RuinPalette.Darken(LabPanel, 0.42f), RuinPalette.Darken(LabPanel, 0.22f));
                    BrokenPanel(c, rng);
                    break;

                case FloorFamily.Utility:
                    // Darker maintenance flooring with recessed grooves, the facility's back-of-house surface.
                    //
                    // No access panel here on purpose. A panel centred in the tile is a feature at a fixed position,
                    // so a cluster of these tiles reads as a grid of stamped squares — the exact repetition the pass
                    // is trying to remove. The grooves run edge to edge instead, so neighbouring tiles join into one
                    // continuous ribbed surface.
                    Material(c, CalmRamp(LabUtility), seed, cells: 4, shadowAt: 0.32f, lightAt: 0.76f);
                    for (var y = 5; y < Size; y += 10)
                    {
                        c.Line(0, y, Size - 1, y, RuinPalette.Darken(LabUtility, 0.22f));
                        c.Line(0, y + 1, Size - 1, y + 1, RuinPalette.Lighten(LabUtility, 0.10f));
                    }
                    break;

                default:
                    // Vegetation intrusion: one clustered moss mass with roots running out of it, never scattered
                    // green pixels (spec B4).
                    Material(c, panel, seed, cells: 4, shadowAt: 0.32f, lightAt: 0.74f);
                    SlabSeam(c, RuinPalette.Darken(LabPanel, 0.28f), RuinPalette.Lighten(LabPanel, 0.14f));
                    MossPatch(c, seed + 3, rng);
                    break;
            }
        }

        // =====================================================================
        //  Floor detail layer (spec B6)
        // =====================================================================

        /// <summary>
        /// The detail layer: mostly transparent, so it reads as something lying on the floor rather than as a second
        /// full-coverage texture. Anything opaque here would just be another floor.
        /// </summary>
        private static void Detail(PixelCanvas c, Biome biome, DetailKind kind, int seed)
        {
            var rng = new System.Random(seed);

            switch (kind)
            {
                case DetailKind.Grime:
                    // A single broad smudge with a soft, irregular boundary.
                    RegionAlpha(c, seed, GrimeTone(biome), 0.42f, 3);
                    break;

                case DetailKind.Marking:
                    Marking(c, biome, rng);
                    break;

                case DetailKind.Service:
                    Service(c, biome, rng);
                    break;

                default:
                    Residue(c, biome, seed, rng);
                    break;
            }
        }

        private static Color32 GrimeTone(Biome biome) => biome switch
        {
            Biome.RuinedMetro => RuinPalette.Hex("#2E3133"),
            Biome.Rustworks => RuinPalette.Hex("#1E1F21"),
            _ => RuinPalette.Hex("#565E56")
        };

        /// <summary>Painted markings: faded, chipped, and always one long form rather than a repeated motif.</summary>
        private static void Marking(PixelCanvas c, Biome biome, System.Random rng)
        {
            var tone = biome switch
            {
                Biome.RuinedMetro => RuinPalette.Darken(RuinPalette.WarningOchre, 0.28f),
                Biome.Rustworks => RuinPalette.Darken(RuinPalette.DirtyYellow, 0.30f),
                _ => RuinPalette.Darken(RuinPalette.ColdBlue, 0.25f)
            };

            // A lane stripe that runs the full width, so consecutive tiles form one continuous line.
            var y = 13 + rng.Next(6);
            for (var x = 0; x < Size; x++)
            {
                // Chipped paint: gaps are long, not per-pixel, so the line stays a line.
                if ((x + rng.Next(2)) % 17 < 2) continue;
                c.Set(x, y, tone);
                c.Set(x, y + 1, tone);
                c.Set(x, y + 2, RuinPalette.Darken(tone, 0.35f));
            }
        }

        /// <summary>Recessed service features: drains, cable channels, vents. Always dark and always constructed.</summary>
        private static void Service(PixelCanvas c, Biome biome, System.Random rng)
        {
            var dark = RuinPalette.Hex("#15191B");
            var rim = biome == Biome.Rustworks ? RuinPalette.Darken(RuinPalette.Rust, 0.3f) : RuinPalette.Darken(RuinPalette.MidSteel, 0.25f);

            if (biome == Biome.RuinedMetro)
            {
                // Cable channel: a recessed trough running the width of the tile.
                c.Rect(0, 12, Size, 8, rim);
                c.Rect(0, 13, Size, 6, dark);
                for (var x = 1; x < Size; x += 3) c.Set(x, 16, RuinPalette.Darken(RuinPalette.ElectricCyan, 0.55f));
                c.Line(0, 19, Size - 1, 19, RuinPalette.Lighten(rim, 0.18f));
                return;
            }

            // Drain: a square grate set into the surface.
            const int x0 = 9;
            const int y0 = 9;
            c.Rect(x0 - 1, y0 - 1, 16, 16, rim);
            c.Rect(x0, y0, 14, 14, dark);
            for (var i = 0; i < 14; i += 3) c.Rect(x0, y0 + i, 14, 2, RuinPalette.Darken(rim, 0.15f));
            c.Line(x0 - 1, y0 + 14, x0 + 14, y0 + 14, RuinPalette.Lighten(rim, 0.2f));
        }

        /// <summary>The biome's characteristic residue, in one cluster: rust bloom, moss, water damage.</summary>
        private static void Residue(PixelCanvas c, Biome biome, int seed, System.Random rng)
        {
            switch (biome)
            {
                case Biome.RuinedMetro:
                    // Water damage seeping from one edge.
                    RegionAlpha(c, seed, RuinPalette.Hex("#3A3E3A"), 0.34f, 4);
                    RegionAlpha(c, seed + 91, RuinPalette.Darken(RuinPalette.Rust, 0.45f), 0.14f, 3);
                    break;

                case Biome.Rustworks:
                    // Rust bloom, which belongs where metal meets metal and nowhere else.
                    //
                    // Kept dark and small. Spec 14 reserves bright orange for machinery and hazards, and a detail
                    // layer painting oxide orange across a quarter of its cell put saturated marks on ordinary floor
                    // — which is the habit that made the whole biome read as orange in the first place.
                    RegionAlpha(c, seed, RuinPalette.Darken(RuinPalette.Rust, 0.35f), 0.22f, 3);
                    RegionAlpha(c, seed + 57, RuinPalette.Darken(RuinPalette.OxideOrange, 0.40f), 0.05f, 2);
                    break;

                default:
                    MossPatch(c, seed, rng, onEmptyCanvas: true);
                    break;
            }
        }

        // =====================================================================
        //  Walls, obstacles and hazards
        // =====================================================================

        private static void Structure(PixelCanvas c, Biome biome, TileRole role, int seed)
        {
            var rng = new System.Random(seed);
            switch (biome)
            {
                case Biome.RuinedMetro: MetroStructure(c, role, seed, rng); break;
                case Biome.Rustworks: RustworksStructure(c, role, seed, rng); break;
                default: LabsStructure(c, role, seed, rng); break;
            }
        }

        private static void MetroStructure(PixelCanvas c, TileRole role, int seed, System.Random rng)
        {
            switch (role)
            {
                case TileRole.Wall:
                    // Station wall: tiled upper course over a concrete base, with a strong top light edge.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#6A6E6B"), 0.22f, 0.12f), seed, cells: 4, shadowAt: 0.34f, lightAt: 0.76f);
                    for (var y = 2; y < Size; y += 6)
                    {
                        Seam(c, RuinPalette.Darken(RuinPalette.Concrete, 0.5f), y, true);
                        for (var x = (y / 6) % 2 == 0 ? 0 : 6; x < Size; x += 12)
                            c.Line(x, y, x, Mathf.Min(Size - 1, y + 5), RuinPalette.Darken(RuinPalette.Concrete, 0.45f));
                    }
                    c.Rect(0, Size - 3, Size, 3, RuinPalette.Lighten(RuinPalette.ConcreteLight, 0.2f));
                    c.Rect(0, 0, Size, 2, RuinPalette.Darken(RuinPalette.Concrete, 0.55f));
                    break;

                case TileRole.Obstacle:
                    // Service cabinet / crate mass with a powered indicator.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#3C4446"), 0.24f, 0.10f), seed, cells: 4, shadowAt: 0.36f, lightAt: 0.80f);
                    c.Rect(3, 3, Size - 6, Size - 6, RuinPalette.MidSteel);
                    c.ShadeForm(3, 3, Size - 6, Size - 6, RuinPalette.MidSteel, RuinPalette.SteelRamp);
                    c.RectOutline(3, 3, Size - 6, Size - 6, RuinPalette.Darken(RuinPalette.DarkSteel, 0.3f));
                    for (var y = 8; y < Size - 8; y += 5)
                        c.Line(6, y, Size - 7, y, RuinPalette.Darken(RuinPalette.MidSteel, 0.35f));
                    c.Rect(Size - 9, Size - 10, 3, 2, RuinPalette.TerminalGreen);
                    Region(c, seed + 41, RuinPalette.Rust, 0.16f, 2);
                    break;

                case TileRole.Hazard:
                    MetroHazard(c, seed, rng, 0);
                    break;
            }
        }

        private static void RustworksStructure(PixelCanvas c, TileRole role, int seed, System.Random rng)
        {
            switch (role)
            {
                case TileRole.Wall:
                    // Riveted steel panel wall with heat discolouration toward the base.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#4A4440"), 0.24f, 0.12f), seed, cells: 4, shadowAt: 0.34f, lightAt: 0.76f);
                    for (var y = 0; y < Size; y += 11) c.Rect(0, y, Size, 2, RuinPalette.Darken(RuinPalette.Rust, 0.3f));
                    for (var y = 3; y < Size; y += 11)
                    for (var x = 3; x < Size; x += 7)
                        c.Set(x, y, RuinPalette.Lighten(RuinPalette.MidSteel, 0.3f));
                    c.Rect(0, Size - 3, Size, 3, RuinPalette.Lighten(RuinPalette.Hex("#6A5F55"), 0.15f));
                    c.Rect(0, 0, Size, 3, RuinPalette.Darken(RuinPalette.BurntRustDark, 0.25f));
                    Region(c, seed + 61, RuinPalette.OxideOrange, 0.14f, 2);
                    break;

                case TileRole.Obstacle:
                    // Furnace / press unit with a hot core slot.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#2E2A28"), 0.26f, 0.10f), seed, cells: 4, shadowAt: 0.36f, lightAt: 0.82f);
                    c.Rect(2, 2, Size - 4, Size - 4, RuinPalette.Hex("#57504A"));
                    c.ShadeForm(2, 2, Size - 4, Size - 4, RuinPalette.Hex("#57504A"), RuinPalette.RampOf(RuinPalette.Hex("#57504A")));
                    c.RectOutline(2, 2, Size - 4, Size - 4, RuinPalette.Darken(RuinPalette.BurntRustDark, 0.2f));
                    c.Rect(8, 10, Size - 16, 7, RuinPalette.Darken(RuinPalette.OxideOrange, 0.55f));
                    c.Rect(9, 11, Size - 18, 5, RuinPalette.OxideOrange);
                    c.Rect(10, 12, Size - 20, 3, RuinPalette.Lighten(RuinPalette.AmberActive, 0.35f));
                    Region(c, seed + 71, RuinPalette.Rust, 0.18f, 2);
                    break;

                case TileRole.Hazard:
                    RustworksHazard(c, seed, rng, 0);
                    break;
            }
        }

        private static void LabsStructure(PixelCanvas c, TileRole role, int seed, System.Random rng)
        {
            switch (role)
            {
                case TileRole.Wall:
                    // Composite panel wall with an observation band and vegetation breakthrough.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#A2A79E"), 0.20f, 0.10f), seed, cells: 4, shadowAt: 0.34f, lightAt: 0.78f);
                    for (var x = 0; x < Size; x += 10) c.Rect(x, 0, 2, Size, RuinPalette.Darken(RuinPalette.Hex("#A2A79E"), 0.28f));
                    c.Rect(0, 12, Size, 8, RuinPalette.Darken(RuinPalette.ColdBlue, 0.45f));
                    c.Rect(0, 13, Size, 6, RuinPalette.Darken(RuinPalette.ColdBlue, 0.2f));
                    c.Rect(0, Size - 3, Size, 3, RuinPalette.Lighten(RuinPalette.Hex("#A2A79E"), 0.25f));
                    Vines(c, RuinPalette.DeepOlive, rng, 3);
                    break;

                case TileRole.Obstacle:
                    // Containment / server rack, cracked open with growth inside.
                    Material(c, RuinPalette.RampOf(RuinPalette.Hex("#6E736C"), 0.24f, 0.10f), seed, cells: 4, shadowAt: 0.36f, lightAt: 0.82f);
                    c.Rect(3, 2, Size - 6, Size - 4, RuinPalette.Hex("#B4B8B0"));
                    c.ShadeForm(3, 2, Size - 6, Size - 4, RuinPalette.Hex("#B4B8B0"), RuinPalette.LabWhiteRamp);
                    c.RectOutline(3, 2, Size - 6, Size - 4, RuinPalette.Darken(RuinPalette.Charcoal, 0.1f));
                    c.Rect(7, 8, Size - 14, 14, RuinPalette.Darken(RuinPalette.ElectricCyan, 0.55f));
                    c.Rect(8, 9, Size - 16, 12, RuinPalette.Darken(RuinPalette.ElectricCyan, 0.3f));
                    Vines(c, RuinPalette.Olive, rng, 3);
                    c.Rect(Size - 9, 4, 3, 2, RuinPalette.TerminalGreen);
                    break;

                case TileRole.Hazard:
                    LabsHazard(c, seed, rng, 0);
                    break;
            }
        }

        // =====================================================================
        //  Damaging floor hazards (animated)
        // =====================================================================

        /// <summary>Frames in every hazard loop. Frame 0 is the static tile <see cref="Build"/> has always produced.</summary>
        public const int HazardFrames = 8;

        /// <summary>
        /// One frame of a biome's damaging floor hazard loop. The hazard is the one floor surface that must never be
        /// read as floor, and a still tile is exactly what the eye learns to ignore, so each biome's hazard moves in
        /// the way its material would: the Metro rail flickers with current and throws sparks, the Rustworks grate
        /// flows with heat, the Labs pool churns and bubbles. Every loop closes (phase is a whole turn over
        /// <see cref="HazardFrames"/>), the palette is the tile's own, and frame 0 equals the static tile exactly.
        /// </summary>
        public static PixelCanvas BuildHazardFrame(Biome biome, int frame)
        {
            var c = new PixelCanvas(Size, Size);
            var seed = (int)biome * 977 + (int)TileRole.Hazard * 131 + 5;
            var rng = new System.Random(seed);
            frame = ((frame % HazardFrames) + HazardFrames) % HazardFrames;
            switch (biome)
            {
                case Biome.RuinedMetro: MetroHazard(c, seed, rng, frame); break;
                case Biome.Rustworks: RustworksHazard(c, seed, rng, frame); break;
                default: LabsHazard(c, seed, rng, frame); break;
            }

            return c;
        }

        private static float HazardPhase(int frame) => frame * Mathf.PI * 2f / HazardFrames;

        /// <summary>0 at frame 0, 1 half way through the loop: how far a frame is from the static tile.</summary>
        private static float HazardSwing(int frame) => 0.5f - 0.5f * Mathf.Cos(HazardPhase(frame));

        // Irregular so the current reads as a flicker rather than a smooth breathing glow.
        private static readonly float[] MetroCurrent = { 0f, 0.45f, 0.12f, 0.6f, 0.05f, 0.32f, 0.7f, 0.18f };

        private static void MetroHazard(PixelCanvas c, int seed, System.Random rng, int frame)
        {
            // Electrified rail panel. Loudest thing in the Metro set by design (24.5).
            Material(c, RuinPalette.RampOf(RuinPalette.Hex("#2A2F31"), 0.26f, 0.10f), seed, cells: 4, shadowAt: 0.36f, lightAt: 0.82f);
            var current = MetroCurrent[frame];
            var rail = RuinPalette.Lighten(RuinPalette.ElectricCyan, current);
            var rails = 0;
            for (var x = 2; x < Size; x += 8, rails++)
            {
                c.Rect(x, 2, 4, Size - 4, RuinPalette.Darken(RuinPalette.MidSteel, 0.2f));
                c.Line(x + 1, 4, x + 1, Size - 5, rail);
                if (frame == 0) continue;
                // A pulse of current running down each rail, staggered so neighbouring rails never move in step.
                var y = 4 + (frame * 5 + rails * 9) % (Size - 11);
                c.Line(x + 1, y, x + 1, y + 2, RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.8f));
                c.Set(x + 2, y + 1, RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.55f));
            }

            // Arcs jump to new places every frame (frame 0 keeps the static tile's arcs).
            Arc(c, RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.5f + current * 0.4f), frame == 0 ? rng : new System.Random(seed + frame * 7919), frame % 2 == 0 ? 4 : 3);
            c.RectOutline(0, 0, Size, Size, RuinPalette.Darken(RuinPalette.ElectricCyan, 0.4f - current * 0.3f));
        }

        private static void RustworksHazard(PixelCanvas c, int seed, System.Random rng, int frame)
        {
            // Molten / hot surface. Bright orange is reserved for exactly this (spec 20).
            Material(c, RuinPalette.RampOf(RuinPalette.Hex("#3A2318"), 0.26f, 0.10f), seed, cells: 4, shadowAt: 0.38f, lightAt: 0.84f);
            var w = HazardPhase(frame);
            var swing = HazardSwing(frame);
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                // Both waves advance by a whole turn over the loop: the heat flows across the grate and comes back.
                var n = (Mathf.Sin(x * 0.55f + y * 0.31f + w) + Mathf.Sin(y * 0.47f - x * 0.19f + w)) * 0.5f;
                if (swing > 0f && n > 1f - 0.16f * swing) c.Set(x, y, RuinPalette.Lighten(RuinPalette.AmberActive, 0.2f));
                else if (n > 0.45f) c.Set(x, y, RuinPalette.OxideOrange);
                else if (n > 0.12f) c.Set(x, y, RuinPalette.Darken(RuinPalette.OxideOrange, 0.35f));
            }

            // The embers stay where they are and breathe: bright at rest, dimmer as the flow peaks.
            Blobs(c, RuinPalette.Lighten(RuinPalette.AmberActive, 0.45f - 0.25f * swing), rng, 5, 2);
            c.RectOutline(0, 0, Size, Size, RuinPalette.Darken(RuinPalette.BurntRustDark, 0.3f));
        }

        private static void LabsHazard(PixelCanvas c, int seed, System.Random rng, int frame)
        {
            // Contamination pool: the only place toxic green appears at full strength.
            Material(c, RuinPalette.RampOf(RuinPalette.Hex("#2E3A2A"), 0.26f, 0.10f), seed, cells: 4, shadowAt: 0.38f, lightAt: 0.84f);
            var w = HazardPhase(frame);
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                // Two waves turning against each other: the surface churns in place instead of sliding like the heat.
                var n = (Mathf.Sin(x * 0.4f + y * 0.26f + w) + Mathf.Sin(y * 0.52f - x * 0.23f - w)) * 0.5f;
                if (n > 0.5f) c.Set(x, y, RuinPalette.PaleToxic);
                else if (n > 0.08f) c.Set(x, y, RuinPalette.SickGreen);
            }

            Blobs(c, RuinPalette.Lighten(RuinPalette.PaleToxic, 0.4f), rng, 4, 2);

            // Bubbles rise and pop at fixed spots, each on its own third of the loop.
            if (frame > 0)
            {
                var spots = new System.Random(seed + 101);
                var t = frame / (float)HazardFrames;
                for (var i = 0; i < 3; i++)
                {
                    var bx = 5 + spots.Next(Size - 10);
                    var by = 5 + spots.Next(Size - 10);
                    var p = (t + i / 3f) % 1f;
                    var rim = RuinPalette.Lighten(RuinPalette.PaleToxic, 0.55f);
                    if (p < 0.3f) c.Set(bx, by, rim);
                    else if (p < 0.6f) { c.Set(bx - 1, by, rim); c.Set(bx + 1, by, rim); c.Set(bx, by - 1, rim); c.Set(bx, by + 1, rim); }
                    else if (p < 0.8f)
                    {
                        // Popped: a few droplets thrown out from where the bubble was.
                        c.Set(bx - 2, by, rim); c.Set(bx + 2, by, rim); c.Set(bx, by - 2, rim); c.Set(bx, by + 2, rim);
                    }
                }
            }

            c.RectOutline(0, 0, Size, Size, RuinPalette.Darken(RuinPalette.DeepOlive, 0.3f));
        }

        // =====================================================================
        //  Surface construction helpers
        // =====================================================================

        /// <summary>
        /// The low-contrast ramp every floor material is mottled with.
        ///
        /// The contrast is deliberately tiny. Broad shapes fixed the old salt-and-pepper problem but introduced a
        /// worse one: a large, high-contrast blob is *more* recognisable when repeated than fine noise is, so a floor
        /// of them reads as a stamped grid. Three values a hair apart give the surface material character without
        /// giving the eye a shape to latch onto, and the variety a room needs comes from the floor family and the
        /// detail layer instead — which is where the polish pass wants it.
        /// </summary>
        private static RuinPalette.Ramp CalmRamp(Color32 baseColor) => RampToDelta(baseColor, 0.013f);

        /// <summary>A slightly more present mottling, for the members that are meant to read as worn or damaged.</summary>
        private static RuinPalette.Ramp WornRamp(Color32 baseColor) => RampToDelta(baseColor, 0.024f);

        /// <summary>
        /// A ramp whose steps are a fixed <em>luminance</em> distance from the base, rather than a fixed lerp factor.
        ///
        /// A constant lerp factor is not a constant amount of contrast: darkening a pale lab panel by 7.5% toward
        /// black moves it nearly twice as far in luminance as darkening dark Metro concrete by the same factor. That
        /// is why the Labs floor still showed a visible repeating blob at a setting where the Metro floor did not.
        /// Targeting the luminance delta directly gives all three biomes the same amount of mottling, whatever value
        /// their material sits at.
        /// </summary>
        private static RuinPalette.Ramp RampToDelta(Color32 baseColor, float delta)
        {
            var lum = Luminance(baseColor);
            var toBlack = Mathf.Max(0.02f, lum - Luminance(RuinPalette.NearBlack));
            var toWhite = Mathf.Max(0.02f, Luminance(new Color32(250, 246, 238, 255)) - lum);
            return RuinPalette.RampOf(baseColor, Mathf.Clamp(delta / toBlack, 0.02f, 0.5f), Mathf.Clamp(delta / toWhite, 0.02f, 0.5f));
        }

        private static float Luminance(Color32 c) => (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;

        /// <summary>
        /// The base material of a tile: three values of one ramp laid out in broad, connected regions.
        ///
        /// The shape comes from value noise on a lattice that wraps at the tile edge, so laying the tile out produces
        /// no seam and no recognisable motif. This replaces the old 2x2 random speckle, which was the single biggest
        /// cause of the wallpaper read: random per-cell noise is identical in every copy of the tile, so the eye finds
        /// the repeat immediately. Broad regions have no small features to recognise — provided their contrast stays
        /// low enough that the region itself is not a feature, which is what <see cref="CalmRamp"/> is for.
        /// </summary>
        private static void Material(PixelCanvas c, RuinPalette.Ramp ramp, int seed, int cells, float shadowAt, float lightAt)
        {
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var n = PeriodicNoise(x, y, cells, seed);
                var col = n < shadowAt ? ramp.Shadow : n > lightAt ? ramp.Light : ramp.Base;
                c.Set(x, y, col);
            }
        }

        /// <summary>
        /// Value noise on a wrapping lattice: smooth, deterministic, and periodic over the tile.
        ///
        /// <paramref name="cells"/> is the lattice resolution across the tile, so 4 means an 8 px feature size —
        /// squarely in the "prefer 4-12 px structures over random 1 px noise" band the polish pass asks for.
        /// </summary>
        private static float PeriodicNoise(int x, int y, int cells, int seed)
        {
            var step = Size / (float)cells;
            var fx = x / step;
            var fy = y / step;
            var x0 = Mathf.FloorToInt(fx);
            var y0 = Mathf.FloorToInt(fy);
            var tx = Smooth(fx - x0);
            var ty = Smooth(fy - y0);

            var v00 = Lattice(x0, y0, cells, seed);
            var v10 = Lattice(x0 + 1, y0, cells, seed);
            var v01 = Lattice(x0, y0 + 1, cells, seed);
            var v11 = Lattice(x0 + 1, y0 + 1, cells, seed);

            return Mathf.Lerp(Mathf.Lerp(v00, v10, tx), Mathf.Lerp(v01, v11, tx), ty);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>One lattice corner value, wrapped so the far edge samples the same corner as the near one.</summary>
        private static float Lattice(int gx, int gy, int cells, int seed)
        {
            gx = ((gx % cells) + cells) % cells;
            gy = ((gy % cells) + cells) % cells;
            var h = gx * 374761393 + gy * 668265263 + seed * 2246822519;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        /// <summary>
        /// A connected region of one colour, covering roughly <paramref name="coverage"/> of the tile.
        ///
        /// It is thresholded value noise rather than scattered dots, so the result is one or two blobs with soft
        /// boundaries — a stain, a wear patch, a scorch — instead of a spray.
        /// </summary>
        private static void Region(PixelCanvas c, int seed, Color32 tone, float coverage, int cells)
        {
            var cut = 1f - Mathf.Clamp01(coverage);
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if (PeriodicNoise(x, y, cells, seed) > cut)
                    c.Set(x, y, tone);
        }

        /// <summary>The same region, but drawn onto an otherwise empty canvas — the detail layer's basic move.</summary>
        private static void RegionAlpha(PixelCanvas c, int seed, Color32 tone, float coverage, int cells) =>
            Region(c, seed, tone, coverage, cells);

        /// <summary>A region with a distinct rim, so a pool reads as having depth rather than being a flat shape.</summary>
        private static void RegionRimmed(PixelCanvas c, int seed, Color32 core, Color32 rim, float coverage)
        {
            var cut = 1f - Mathf.Clamp01(coverage);
            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var n = PeriodicNoise(x, y, 3, seed);
                if (n > cut + 0.10f) c.Set(x, y, core);
                else if (n > cut) c.Set(x, y, rim);
            }
        }

        /// <summary>A cast seam along two edges. Tiled, these form the slab lattice of a real poured floor.</summary>
        private static void SlabSeam(PixelCanvas c, Color32 dark, Color32 light)
        {
            c.Line(0, 0, Size - 1, 0, dark);
            c.Line(0, 0, 0, Size - 1, dark);
            c.Line(0, 1, Size - 1, 1, light);
            c.Line(1, 0, 1, Size - 1, light);
        }

        /// <summary>A steel plate boundary: a hard dark edge with a lit lip, on all four sides.</summary>
        private static void PlateEdge(PixelCanvas c, Color32 dark, Color32 light)
        {
            c.RectOutline(0, 0, Size, Size, dark);
            c.Line(1, 1, Size - 2, 1, light);
            c.Line(1, 1, 1, Size - 2, light);
        }

        /// <summary>A grout lattice at the given pitch: tile rhythm without the brick-wall offset the spec rejects.</summary>
        private static void Grout(PixelCanvas c, int pitch, Color32 dark, Color32 light)
        {
            for (var y = 0; y < Size; y += pitch)
            {
                c.Line(0, y, Size - 1, y, dark);
                c.Line(0, y + 1, Size - 1, y + 1, light);
            }

            for (var x = 0; x < Size; x += pitch)
            {
                c.Line(x, 0, x, Size - 1, dark);
                c.Line(x + 1, 0, x + 1, Size - 1, light);
            }
        }

        private static void Rivet(PixelCanvas c, int x, int y)
        {
            c.Set(x, y, RuinPalette.Lighten(RuinPalette.MidSteel, 0.18f));
            c.Set(x + 1, y, RuinPalette.Lighten(RuinPalette.MidSteel, 0.07f));
            c.Set(x, y - 1, RuinPalette.Darken(RuinPalette.MidSteel, 0.30f));
            c.Set(x + 1, y - 1, RuinPalette.Darken(RuinPalette.MidSteel, 0.30f));
        }

        /// <summary>A constructed access panel set into the surface: the "utility" member of every floor family.</summary>
        private static void AccessPanel(PixelCanvas c, int x, int y, int w, int h, Color32 tone, bool directional)
        {
            c.Rect(x, y, w, h, RuinPalette.Darken(tone, 0.12f));
            c.RectOutline(x, y, w, h, RuinPalette.Darken(tone, 0.5f));
            c.Line(x + 1, y + h - 2, x + w - 2, y + h - 2, RuinPalette.Lighten(tone, 0.22f));

            if (directional)
                for (var gy = y + 4; gy < y + h - 3; gy += 5)
                    c.Line(x + 3, gy, x + w - 4, gy, RuinPalette.Darken(tone, 0.3f));

            foreach (var (bx, by) in new[] { (x + 3, y + 3), (x + w - 4, y + 3), (x + 3, y + h - 4), (x + w - 4, y + h - 4) })
            {
                c.Set(bx, by, RuinPalette.Darken(tone, 0.55f));
                c.Set(bx, by + 1, RuinPalette.Lighten(tone, 0.25f));
            }
        }

        /// <summary>
        /// One crack event: a main fracture with a branch, both drifting in a consistent direction.
        ///
        /// Cracks are grouped rather than placed independently, because a surface with two unrelated cracks in every
        /// 32 px square is a texture, and a surface where damage happens in one place is a floor.
        /// </summary>
        private static void CrackSystem(PixelCanvas c, System.Random rng, Color32 dark, Color32 soft)
        {
            var x = rng.Next(6, Size - 6);
            var y = rng.Next(4, Size - 4);
            var dirX = rng.Next(2) == 0 ? 1 : -1;

            void Run(int cx, int cy, int length, int dx)
            {
                for (var s = 0; s < length; s++)
                {
                    c.Set(cx, cy, dark);
                    // A soft shoulder on one side gives the fracture width without widening the dark line.
                    c.Set(cx, cy + 1, soft);
                    cx += rng.Next(3) == 0 ? dx : 0;
                    cy += rng.Next(3) - 1;
                    if (cx < 0 || cx >= Size || cy < 0 || cy >= Size) return;
                }
            }

            Run(x, y, 18 + rng.Next(10), dirX);
            Run(x + dirX * 2, y, 8 + rng.Next(8), -dirX);
        }

        /// <summary>A broken corner: the surface gone, the darker structure below showing through.</summary>
        private static void Chip(PixelCanvas c, System.Random rng, Color32 dark)
        {
            var x = rng.Next(2) == 0 ? 0 : Size - 10;
            var y = rng.Next(2) == 0 ? 0 : Size - 10;
            for (var dy = 0; dy < 9; dy++)
            for (var dx = 0; dx < 9 - dy; dx++)
                c.Set(x + dx, y + dy, dx + dy < 6 ? dark : RuinPalette.Lighten(dark, 0.15f));
        }

        /// <summary>A panel that has given way, exposing the structure underneath in one connected area.</summary>
        private static void BrokenPanel(PixelCanvas c, System.Random rng)
        {
            var x = rng.Next(5, 18);
            var y = rng.Next(5, 18);
            var w = 6 + rng.Next(4);
            var h = 5 + rng.Next(4);
            c.Rect(x, y, w, h, RuinPalette.Darken(LabPanel, 0.38f));
            c.RectOutline(x, y, w, h, RuinPalette.Darken(LabPanel, 0.52f));
            // A couple of surviving fragments inside the opening, so the edge is not a clean rectangle.
            c.Rect(x + 1, y + h - 2, 3, 1, RuinPalette.Darken(LabPanel, 0.20f));
            c.Rect(x + w - 4, y + 1, 3, 1, RuinPalette.Darken(LabPanel, 0.20f));
        }


        /// <summary>
        /// A moss colony: one dense cluster with roots reaching out of it.
        ///
        /// Deliberately not scattered dots. The Labs floor used to distribute single green pixels over every tile,
        /// which read as static rather than as anything growing; a colony with a centre and tendrils reads as growth
        /// and leaves the rest of the panel clean.
        /// </summary>
        private static void MossPatch(PixelCanvas c, int seed, System.Random rng, bool onEmptyCanvas = false)
        {
            var cx = 8 + rng.Next(16);
            var cy = 8 + rng.Next(16);

            for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / 14f;
                var n = PeriodicNoise(x, y, 4, seed);
                var mass = (1f - Mathf.Clamp01(d)) * 0.8f + n * 0.4f;
                if (mass > 0.78f) c.Set(x, y, RuinPalette.Olive);
                else if (mass > 0.62f) c.Set(x, y, RuinPalette.DeepOlive);
                // The fading edge of the colony stays a dark olive rather than drifting to grey-green: a moss patch
                // that desaturates at its rim reads as a stain instead of as something growing.
                else if (mass > 0.52f && !onEmptyCanvas) c.Set(x, y, RuinPalette.Darken(RuinPalette.Olive, 0.30f));
            }

            // Runners leaving the colony, which is what connects one mossy tile to the next.
            for (var i = 0; i < 3; i++)
            {
                var x = cx;
                var y = cy;
                var dx = rng.Next(3) - 1;
                var dy = rng.Next(3) - 1;
                if (dx == 0 && dy == 0) dx = 1;
                for (var s = 0; s < 14 + rng.Next(10); s++)
                {
                    c.Set(x, y, RuinPalette.DeepOlive);
                    x += dx + (rng.Next(4) == 0 ? rng.Next(3) - 1 : 0);
                    y += dy + (rng.Next(4) == 0 ? rng.Next(3) - 1 : 0);
                    if (x < 0 || x >= Size || y < 0 || y >= Size) break;
                }
            }
        }

        // ---------------- legacy shared helpers still used by the structure tiles ----------------

        private static void Seam(PixelCanvas c, Color32 col, int at, bool horizontal)
        {
            if (horizontal) c.Line(0, at, Size - 1, at, col);
            else c.Line(at, 0, at, Size - 1, col);
        }

        private static void Blobs(PixelCanvas c, Color32 col, System.Random rng, int count, int radius)
        {
            for (var i = 0; i < count; i++)
                c.Ellipse(rng.Next(Size), rng.Next(Size), radius, Mathf.Max(1, radius - 1), col);
        }

        private static void Vines(PixelCanvas c, Color32 col, System.Random rng, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var x = rng.Next(Size);
                var y = rng.Next(Size);
                var len = 8 + rng.Next(14);
                for (var s = 0; s < len; s++)
                {
                    c.Set(x, y, col);
                    if (s % 4 == 0) c.Set(x + 1, y, RuinPalette.Darken(col, 0.2f));
                    x += rng.Next(3) - 1;
                    y += rng.Next(3) - 1;
                    if (x < 0 || x >= Size || y < 0 || y >= Size) break;
                }
            }
        }

        private static void Arc(PixelCanvas c, Color32 col, System.Random rng, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var x = rng.Next(Size);
                var y = rng.Next(Size);
                for (var s = 0; s < 5; s++)
                {
                    c.Set(x, y, col);
                    x += rng.Next(3) - 1;
                    y += 1;
                    if (x < 0 || x >= Size || y >= Size) break;
                }
            }
        }
    }
}
