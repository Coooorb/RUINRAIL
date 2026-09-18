using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The two full-screen front-end backdrops: the Shelter interior the hub is staged in, and the approach to it
    /// that the Main Menu sits over.
    ///
    /// FINAL_ART_PRODUCTION_SPEC section 16 describes the Shelter as the safest place in RUINRAIL and still an
    /// improvised, repaired, resource-limited one — warmer and more human than the dungeons without ever becoming
    /// comfortable. That is the whole brief for these two images: a reinforced underground room of salvaged
    /// bulkheads, patched cable runs, crates and work surfaces, lit by a handful of practical amber lamps against
    /// cold structural dark.
    ///
    /// They are authored at screen pixels (PPU 1) at the 640x360 reference, because they are canvas art rather than
    /// world art, and they are composed for the screens that sit on top of them: the centre band stays legible
    /// through the content plate, and the outer thirds are deliberately quieter because the left and right columns
    /// cover them. A backdrop that competes with the UI is a backdrop that has to be turned off.
    /// </summary>
    public static class ShelterSceneFactory
    {
        public const int Width = 640;
        public const int Height = 360;

        // --- Shelter palette (spec 16): charcoal, worn steel, warm beige, canvas green, amber practicals ---
        private static readonly Color32 Dark = RuinPalette.Hex("#12171A");
        private static readonly Color32 WallDeep = RuinPalette.Hex("#1B2326");
        private static readonly Color32 WallBase = RuinPalette.Hex("#333D41");
        private static readonly Color32 WallLit = RuinPalette.Hex("#3A4649");
        private static readonly Color32 BeamSteel = RuinPalette.Hex("#525C58");
        private static readonly Color32 FloorBase = RuinPalette.Hex("#3C3D37");
        private static readonly Color32 FloorLit = RuinPalette.Hex("#5E5744");
        private static readonly Color32 Beige = RuinPalette.Hex("#8C7A5C");
        private static readonly Color32 Canvas = RuinPalette.CanvasGreen;
        private static readonly Color32 Lamp = RuinPalette.AmberActive;

        // =====================================================================
        //  Shelter interior — the hub backdrop
        // =====================================================================

        /// <summary>The Shelter as a staged room: bulkhead wall, transit door, work side, storage side, lamps.</summary>
        public static PixelCanvas Shelter()
        {
            var c = new PixelCanvas(Width, Height);
            var rng = new System.Random(20260916);

            const int floorTop = 118;     // where the floor meets the back wall
            const int ceilingLine = 300;  // underside of the structural ceiling

            Ground(c, rng, floorTop);
            BulkheadWall(c, rng, floorTop, ceilingLine);
            Ceiling(c, rng, ceilingLine);

            TransitDoor(c, 258, floorTop - 6, 124, 142);
            StorageBay(c, rng, 14, floorTop - 4);
            WorkBay(c, rng, 470, floorTop - 4);
            CableRuns(c, rng, ceilingLine);

            // Three practical lamps: the only warm light in the room, and the reason the floor has shape at all.
            Practical(c, 118, 262);
            Practical(c, 320, 286);
            Practical(c, 524, 262);
            FloorPool(c, 118, floorTop - 30, 78);
            FloorPool(c, 320, floorTop - 40, 104);
            FloorPool(c, 524, floorTop - 30, 78);

            Vignette(c, 0.40f);
            return c;
        }

        /// <summary>Concrete slab floor with a broad wear lane and sparse, grouped grime — never an even speckle.</summary>
        private static void Ground(PixelCanvas c, System.Random rng, int floorTop)
        {
            c.Rect(0, 0, Width, floorTop, FloorBase);

            // Broad slab seams, widening toward the viewer so the floor reads as receding.
            for (var i = 0; i < 5; i++)
            {
                var y = floorTop - 10 - i * i * 4;
                if (y <= 2) break;
                c.Line(0, y, Width - 1, y, RuinPalette.Darken(FloorBase, 0.30f));
            }

            for (var x = 64; x < Width; x += 128)
                c.Line(x, 0, x, floorTop - 12, RuinPalette.Darken(FloorBase, 0.22f));

            // A worn traffic lane down the middle: where everyone walks to the transit door.
            c.Rect(240, 0, 160, floorTop - 14, RuinPalette.Lighten(FloorBase, 0.06f));
            c.Line(240, 0, 240, floorTop - 14, RuinPalette.Darken(FloorBase, 0.18f));
            c.Line(399, 0, 399, floorTop - 14, RuinPalette.Darken(FloorBase, 0.18f));

            // Stencilled floor marking flanking the lane. An original shape, not lettering.
            for (var i = 0; i < 3; i++)
            {
                c.Rect(214, 16 + i * 22, 18, 4, RuinPalette.Darken(RuinPalette.WarningOchre, 0.35f));
                c.Rect(408, 16 + i * 22, 18, 4, RuinPalette.Darken(RuinPalette.WarningOchre, 0.35f));
            }

            c.Grime(0, 0, Width, floorTop, RuinPalette.Darken(FloorBase, 0.22f), 3311, 0.010f, 4);
            c.Grime(0, 0, Width, 40, RuinPalette.Darken(FloorBase, 0.30f), 991, 0.009f, 5);
        }

        /// <summary>Salvaged bulkhead panelling: large plates, structural beams, patched repairs.</summary>
        private static void BulkheadWall(PixelCanvas c, System.Random rng, int floorTop, int ceilingLine)
        {
            c.Rect(0, floorTop, Width, ceilingLine - floorTop, WallBase);

            // Large plate divisions. 80 px plates, which is a broad form at this scale rather than a texture.
            for (var x = 0; x <= Width; x += 80)
            {
                c.Line(x, floorTop, x, ceilingLine, RuinPalette.Darken(WallBase, 0.32f));
                if (x + 1 < Width) c.Line(x + 1, floorTop, x + 1, ceilingLine, RuinPalette.Lighten(WallBase, 0.06f));
            }

            // Two structural beams across the room, the heavier one at head height.
            Beam(c, floorTop + 96, 10);
            Beam(c, floorTop + 8, 6);

            // Rivet lines along the beams only, so the fastenings read as structure and not as noise.
            for (var x = 10; x < Width; x += 20)
            {
                c.Set(x, floorTop + 100, RuinPalette.Lighten(BeamSteel, 0.35f));
                c.Set(x, floorTop + 99, RuinPalette.Darken(BeamSteel, 0.4f));
            }

            // Patched repairs: a handful of mismatched plates welded over damage.
            Patch(c, 96, floorTop + 118, 52, 34, Beige);
            Patch(c, 416, floorTop + 112, 44, 40, RuinPalette.Rust);
            Patch(c, 196, floorTop + 22, 38, 26, RuinPalette.Darken(Canvas, 0.1f));

            // Upper wall falls away into the dark; the lamps below never reach it.
            for (var y = ceilingLine - 40; y < ceilingLine; y++)
            {
                var t = (y - (ceilingLine - 40)) / 40f;
                for (var x = 0; x < Width; x++)
                    c.Set(x, y, Color32.Lerp(c.Get(x, y), Dark, t * 0.55f));
            }

            c.Grime(0, floorTop, Width, ceilingLine - floorTop, RuinPalette.Darken(WallBase, 0.20f), 7717, 0.008f, 4);
        }

        private static void Beam(PixelCanvas c, int y, int thickness)
        {
            c.Rect(0, y, Width, thickness, BeamSteel);
            c.Line(0, y + thickness - 1, Width - 1, y + thickness - 1, RuinPalette.Lighten(BeamSteel, 0.22f));
            c.Line(0, y, Width - 1, y, RuinPalette.Darken(BeamSteel, 0.45f));
        }

        private static void Patch(PixelCanvas c, int x, int y, int w, int h, Color32 tone)
        {
            c.Rect(x, y, w, h, RuinPalette.Darken(tone, 0.35f));
            c.Rect(x + 1, y + 1, w - 2, h - 2, RuinPalette.Darken(tone, 0.15f));
            c.Line(x + 1, y + h - 2, x + w - 2, y + h - 2, RuinPalette.Lighten(tone, 0.18f));
            // Weld tacks at the corners.
            foreach (var (px, py) in new[] { (x + 2, y + 2), (x + w - 3, y + 2), (x + 2, y + h - 3), (x + w - 3, y + h - 3) })
                c.Set(px, py, RuinPalette.Lighten(BeamSteel, 0.3f));
        }

        /// <summary>Structural ceiling with conduit runs. Mostly dark: the UI header sits over it.</summary>
        private static void Ceiling(PixelCanvas c, System.Random rng, int ceilingLine)
        {
            c.Rect(0, ceilingLine, Width, Height - ceilingLine, Dark);
            c.Line(0, ceilingLine, Width - 1, ceilingLine, RuinPalette.Darken(BeamSteel, 0.25f));

            for (var i = 0; i < 4; i++)
            {
                var y = ceilingLine + 8 + i * 12;
                if (y >= Height - 2) break;
                var tone = i % 2 == 0 ? RuinPalette.Darken(BeamSteel, 0.35f) : RuinPalette.Darken(RuinPalette.Rust, 0.45f);
                c.Rect(0, y, Width, 3, tone);
                c.Line(0, y + 2, Width - 1, y + 2, RuinPalette.Darken(tone, 0.4f));
                // Pipe hangers at wide intervals.
                for (var x = 40 + i * 13; x < Width; x += 96) c.Rect(x, y, 2, 6, RuinPalette.Darken(BeamSteel, 0.5f));
            }
        }

        /// <summary>The way out: a heavy transit bulkhead, powered, the strongest focal point in the room.</summary>
        private static void TransitDoor(PixelCanvas c, int x, int y, int w, int h)
        {
            // Recessed frame, so the door reads as set into the wall rather than stuck onto it.
            c.Rect(x - 8, y - 4, w + 16, h + 10, RuinPalette.Darken(WallBase, 0.45f));
            c.RectOutline(x - 8, y - 4, w + 16, h + 10, RuinPalette.Darken(BeamSteel, 0.2f));

            c.Rect(x, y, w, h, RuinPalette.Darken(WallDeep, 0.25f));
            c.Rect(x + 4, y + 4, w - 8, h - 8, RuinPalette.Hex("#232C30"));

            // Two leaves with a central seam and heavy horizontal ribs.
            c.Line(x + w / 2, y + 4, x + w / 2, y + h - 5, RuinPalette.Darken(WallDeep, 0.5f), 2);
            for (var ry = y + 14; ry < y + h - 14; ry += 22)
            {
                c.Rect(x + 8, ry, w - 16, 6, RuinPalette.Hex("#2E383C"));
                c.Line(x + 8, ry + 5, x + w - 9, ry + 5, RuinPalette.Lighten(BeamSteel, 0.12f));
                c.Line(x + 8, ry, x + w - 9, ry, RuinPalette.Darken(WallDeep, 0.4f));
            }

            // Hazard chevrons down the jambs, restrained (spec 14/16: warning paint used sparingly).
            for (var cy = y + 8; cy < y + h - 8; cy += 16)
            {
                c.Rect(x + 1, cy, 3, 8, RuinPalette.Darken(RuinPalette.WarningOchre, 0.28f));
                c.Rect(x + w - 4, cy, 3, 8, RuinPalette.Darken(RuinPalette.WarningOchre, 0.28f));
            }

            // Powered indicator and its glow: the door is live, and the room is told so.
            var lampY = y + h - 8;
            c.Rect(x + w / 2 - 8, lampY, 16, 4, RuinPalette.Darken(Lamp, 0.55f));
            c.Rect(x + w / 2 - 6, lampY + 1, 12, 2, Lamp);
            Glow(c, x + w / 2, lampY + 2, 34, Lamp, 0.34f);

            // Warm spill onto the floor directly under the door.
            Glow(c, x + w / 2, y - 2, 46, RuinPalette.Darken(Lamp, 0.25f), 0.18f);
        }

        /// <summary>Storage side: shelving, crates, sacks. Rectangular storage read (spec 16).</summary>
        private static void StorageBay(PixelCanvas c, System.Random rng, int x, int y)
        {
            // Shelving frame.
            const int shelfW = 128;
            const int shelfH = 104;
            c.Rect(x, y, shelfW, shelfH, RuinPalette.Darken(BeamSteel, 0.5f));
            c.Rect(x + 2, y + 2, shelfW - 4, shelfH - 4, RuinPalette.Darken(WallBase, 0.2f));
            for (var i = 0; i < 3; i++)
            {
                var sy = y + 6 + i * 32;
                c.Rect(x + 2, sy, shelfW - 4, 3, RuinPalette.Darken(BeamSteel, 0.25f));
                c.Line(x + 2, sy + 2, x + shelfW - 3, sy + 2, RuinPalette.Lighten(BeamSteel, 0.15f));
            }

            c.Line(x + shelfW / 2, y, x + shelfW / 2, y + shelfH - 1, RuinPalette.Darken(BeamSteel, 0.4f));

            // Crates and bundles on the shelves, in the shelter's beige and canvas families.
            Crate(c, x + 8, y + 10, 26, 20, Beige);
            Crate(c, x + 38, y + 10, 20, 20, Canvas);
            Crate(c, x + 70, y + 10, 30, 20, RuinPalette.Darken(Beige, 0.2f));
            Crate(c, x + 10, y + 42, 34, 22, Canvas);
            Crate(c, x + 52, y + 42, 24, 22, Beige);
            Crate(c, x + 84, y + 42, 22, 22, RuinPalette.Darken(Canvas, 0.15f));
            Crate(c, x + 14, y + 74, 28, 18, Beige);
            Crate(c, x + 56, y + 74, 40, 18, RuinPalette.Darken(Beige, 0.25f));

            // A couple of crates stacked on the floor in front.
            Crate(c, x + 26, y - 26, 32, 24, Canvas);
            Crate(c, x + 62, y - 20, 26, 18, Beige);
        }

        private static void Crate(PixelCanvas c, int x, int y, int w, int h, Color32 tone)
        {
            c.Rect(x, y, w, h, RuinPalette.Darken(tone, 0.35f));
            c.Rect(x + 1, y + 1, w - 2, h - 2, tone);
            c.Line(x + 1, y + h - 2, x + w - 2, y + h - 2, RuinPalette.Lighten(tone, 0.22f));
            c.Line(x + 1, y + 1, x + w - 2, y + 1, RuinPalette.Darken(tone, 0.45f));
            // One banding strap, which is what makes a rectangle read as a crate.
            c.Line(x + 1, y + h / 2, x + w - 2, y + h / 2, RuinPalette.Darken(tone, 0.5f));
        }

        /// <summary>Work side: bench, tool board, rack silhouette, a small powered terminal.</summary>
        private static void WorkBay(PixelCanvas c, System.Random rng, int x, int y)
        {
            // Tool board on the wall.
            c.Rect(x + 10, y + 50, 108, 58, RuinPalette.Darken(Beige, 0.45f));
            c.RectOutline(x + 10, y + 50, 108, 58, RuinPalette.Darken(BeamSteel, 0.3f));
            for (var i = 0; i < 6; i++)
            {
                var tx = x + 18 + i * 17;
                var len = 16 + (i % 3) * 8;
                c.Rect(tx, y + 96 - len, 3, len, RuinPalette.Darken(BeamSteel, 0.15f));
                c.Rect(tx - 1, y + 96 - len, 5, 4, RuinPalette.Darken(RuinPalette.Rust, 0.2f));
            }

            // Bench: heavy top, open frame, clutter on it.
            c.Rect(x, y + 34, 128, 8, RuinPalette.Darken(Beige, 0.25f));
            c.Line(x, y + 41, x + 127, y + 41, RuinPalette.Lighten(Beige, 0.2f));
            c.Rect(x + 4, y, 6, 34, RuinPalette.Darken(BeamSteel, 0.35f));
            c.Rect(x + 118, y, 6, 34, RuinPalette.Darken(BeamSteel, 0.35f));
            c.Rect(x + 4, y + 10, 120, 3, RuinPalette.Darken(BeamSteel, 0.45f));

            Crate(c, x + 14, y + 12, 26, 18, Canvas);
            Crate(c, x + 86, y + 12, 30, 16, RuinPalette.Darken(Beige, 0.3f));

            // Parts and a clamp on the bench top.
            c.Rect(x + 20, y + 42, 18, 6, RuinPalette.Darken(BeamSteel, 0.1f));
            c.Rect(x + 46, y + 42, 10, 9, RuinPalette.Darken(RuinPalette.Rust, 0.1f));
            c.Rect(x + 62, y + 42, 22, 4, RuinPalette.Darken(BeamSteel, 0.25f));

            // Small powered terminal: the restrained tech accent of the room (spec 16).
            c.Rect(x + 92, y + 42, 26, 22, RuinPalette.Darken(WallDeep, 0.1f));
            c.RectOutline(x + 92, y + 42, 26, 22, RuinPalette.Darken(BeamSteel, 0.2f));
            c.Rect(x + 95, y + 46, 20, 14, RuinPalette.Darken(RuinPalette.TerminalGreen, 0.62f));
            for (var i = 0; i < 4; i++)
                c.Line(x + 97, y + 48 + i * 3, x + 97 + 8 + (i * 5) % 9, y + 48 + i * 3, RuinPalette.Darken(RuinPalette.TerminalGreen, 0.22f));
            Glow(c, x + 105, y + 53, 20, RuinPalette.TerminalGreen, 0.13f);
        }

        /// <summary>Patched cable runs draped across the wall — the single most shelter-like piece of dressing.</summary>
        private static void CableRuns(PixelCanvas c, System.Random rng, int ceilingLine)
        {
            for (var run = 0; run < 3; run++)
            {
                var y0 = ceilingLine - 6 - run * 5;
                var sag = 10 + run * 6;
                var tone = run == 1 ? RuinPalette.Darken(RuinPalette.Rust, 0.3f) : RuinPalette.Darken(BeamSteel, 0.45f);

                // Two catenary spans between anchor points, drawn as a parabola per span.
                for (var span = 0; span < 4; span++)
                {
                    var x0 = span * 160;
                    var x1 = x0 + 160;
                    for (var x = x0; x < x1 && x < Width; x++)
                    {
                        var t = (x - x0) / 160f;
                        var drop = Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * sag);
                        c.Set(x, y0 - drop, tone);
                        if (run == 0) c.Set(x, y0 - drop - 1, RuinPalette.Darken(tone, 0.3f));
                    }

                    // Anchor bracket.
                    if (x0 < Width) c.Rect(x0 - 1, y0 - 1, 3, 5, RuinPalette.Darken(BeamSteel, 0.3f));
                }
            }
        }

        /// <summary>A practical lamp: shade, filament and the cone it throws. The Shelter's only warmth.</summary>
        private static void Practical(PixelCanvas c, int x, int y)
        {
            c.Rect(x - 1, y + 6, 2, 14, RuinPalette.Darken(BeamSteel, 0.5f));   // flex
            c.Taper(x, y + 6, y, 9, 3, RuinPalette.Darken(BeamSteel, 0.2f));    // conical shade
            c.Line(x - 8, y + 1, x + 8, y + 1, RuinPalette.Lighten(BeamSteel, 0.25f));
            c.Rect(x - 2, y - 2, 4, 3, RuinPalette.Lighten(Lamp, 0.45f));       // filament
            Glow(c, x, y - 1, 38, Lamp, 0.42f);

            // The cast cone, widening downward and fading out well before the floor.
            for (var d = 2; d < 78; d++)
            {
                var half = 5 + d / 2;
                var a = 0.24f * (1f - d / 78f);
                for (var dx = -half; dx <= half; dx++)
                {
                    var px = x + dx;
                    var py = y - d;
                    if (!c.InBounds(px, py)) continue;
                    var edge = 1f - Mathf.Abs(dx) / (float)half;
                    c.Set(px, py, Color32.Lerp(c.Get(px, py), Lamp, a * edge));
                }
            }
        }

        /// <summary>The pool of light a lamp leaves on the floor, which is what gives the ground its form.</summary>
        private static void FloorPool(PixelCanvas c, int cx, int cy, int radius)
        {
            for (var y = cy - radius / 2; y <= cy + radius / 2; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (!c.InBounds(x, y)) continue;
                var nx = (x - cx) / (float)radius;
                var ny = (y - cy) / (radius / 2f);
                var d = nx * nx + ny * ny;
                if (d >= 1f) continue;
                c.Set(x, y, Color32.Lerp(c.Get(x, y), FloorLit, (1f - d) * 0.68f));
            }
        }

        // =====================================================================
        //  Main menu — the approach to the Shelter
        // =====================================================================

        /// <summary>
        /// The Main Menu backdrop: the transit tunnel that runs to the Shelter door.
        ///
        /// Composed as one strong perspective so the screen has a focal point behind the title and the primary
        /// action, and heavily vignetted so the menu chrome reads over it at any window size.
        /// </summary>
        public static PixelCanvas MainMenu()
        {
            var c = new PixelCanvas(Width, Height);
            var rng = new System.Random(4211);

            const int horizon = 196;
            const int vanishX = 340;

            c.Rect(0, 0, Width, Height, RuinPalette.Hex("#0E1315"));

            // Tunnel walls converging on the vanishing point, built from broad wedges rather than lines.
            for (var y = 0; y < Height; y++)
            {
                var t = Mathf.Clamp01(Mathf.Abs(y - horizon) / (float)horizon);
                var half = Mathf.RoundToInt(Mathf.Lerp(48f, 360f, t));
                var tone = Color32.Lerp(RuinPalette.Hex("#1A2124"), RuinPalette.Hex("#0C1012"), 1f - t * 0.8f);
                for (var x = 0; x < Width; x++)
                {
                    var inside = Mathf.Abs(x - vanishX) < half;
                    if (!inside) c.Set(x, y, tone);
                }
            }

            // Tunnel ring ribs, spaced so they compress toward the vanishing point.
            for (var i = 1; i <= 9; i++)
            {
                var t = i / 9f;
                var half = Mathf.RoundToInt(Mathf.Lerp(52f, 330f, t * t));
                var top = horizon + Mathf.RoundToInt(Mathf.Lerp(24f, 164f, t * t));
                var bottom = horizon - Mathf.RoundToInt(Mathf.Lerp(24f, 196f, t * t));
                var tone = RuinPalette.Darken(BeamSteel, 0.35f + 0.4f * (1f - t));
                c.Line(vanishX - half, bottom, vanishX - half, top, tone, 2);
                c.Line(vanishX + half, bottom, vanishX + half, top, tone, 2);
                c.Line(vanishX - half, top, vanishX + half, top, tone, 2);
            }

            // Rails running out of frame toward the door.
            for (var side = -1; side <= 1; side += 2)
            {
                for (var y = 0; y < horizon - 22; y++)
                {
                    var t = y / (float)(horizon - 22);
                    var offset = Mathf.RoundToInt(Mathf.Lerp(150f, 16f, t));
                    c.Set(vanishX + side * offset, y, RuinPalette.Lighten(BeamSteel, 0.20f));
                    c.Set(vanishX + side * offset + 1, y, RuinPalette.Darken(BeamSteel, 0.35f));
                }
            }

            // Sleepers between the rails, compressing with distance.
            for (var y = 0; y < horizon - 26; y += 6)
            {
                var t = y / (float)(horizon - 26);
                var offset = Mathf.RoundToInt(Mathf.Lerp(150f, 16f, t));
                var step = Mathf.Max(3, Mathf.RoundToInt(Mathf.Lerp(9f, 3f, t)));
                if (y % step >= 3) continue;
                c.Line(vanishX - offset, y, vanishX + offset, y, RuinPalette.Darken(RuinPalette.Hex("#3A342C"), 0.35f));
            }

            // The Shelter door at the end of the tunnel: small, lit, unmistakably the destination.
            TransitDoor(c, vanishX - 30, horizon - 20, 60, 74);
            Glow(c, vanishX, horizon + 10, 120, Lamp, 0.16f);

            // Emergency lamps down the left wall, receding.
            for (var i = 1; i <= 4; i++)
            {
                var t = i / 4f;
                var half = Mathf.RoundToInt(Mathf.Lerp(60f, 320f, t * t));
                var y = horizon + Mathf.RoundToInt(Mathf.Lerp(18f, 120f, t * t));
                var x = vanishX - half + 6;
                if (x < 2) continue;
                c.Rect(x, y, 3, 2, Lamp);
                Glow(c, x + 1, y + 1, 14 + i * 5, Lamp, 0.22f);
            }

            // Foreground debris keeps the bottom edge from reading as empty ground.
            for (var i = 0; i < 16; i++)
            {
                var x = rng.Next(Width);
                var y = rng.Next(28);
                var w = 3 + rng.Next(7);
                c.Rect(x, y, w, 2, RuinPalette.Darken(RuinPalette.Hex("#3A342C"), 0.4f));
            }

            Vignette(c, 0.78f);
            return c;
        }

        // ---------------- shared light and shade ----------------

        /// <summary>A soft radial light contribution, added to whatever is already there.</summary>
        private static void Glow(PixelCanvas c, int cx, int cy, int radius, Color32 tone, float strength)
        {
            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (!c.InBounds(x, y)) continue;
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1f) continue;
                var a = strength * (1f - d) * (1f - d);
                c.Set(x, y, Color32.Lerp(c.Get(x, y), tone, a));
            }
        }

        /// <summary>
        /// Darkens toward the frame edges.
        ///
        /// This is here for the UI as much as for the mood: the header, tab bar, footer and side columns all sit on
        /// the outer edges, and a quiet backdrop underneath them is what lets thin 1 px chrome stay readable.
        /// </summary>
        private static void Vignette(PixelCanvas c, float strength)
        {
            var cx = Width / 2f;
            var cy = Height / 2f;
            var max = Mathf.Sqrt(cx * cx + cy * cy);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / max;
                var a = Mathf.Clamp01((d - 0.35f) / 0.65f) * strength;
                if (a <= 0f) continue;
                c.Set(x, y, Color32.Lerp(c.Get(x, y), Dark, a));
            }
        }
    }
}
