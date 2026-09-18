using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// World objects, Shelter stations and UI furniture from FINAL_ART_PRODUCTION_SPEC sections 16-18.
    ///
    /// Each world object has to be identifiable as an interactable before the player reads any prompt, so every one
    /// gets a distinct outer silhouette plus one emissive cue. Shelter stations follow the same rule: spec 16 says
    /// each station must have a unique silhouette and iconography, which is what makes the hub navigable without
    /// labels.
    /// </summary>
    public static class WorldObjectFactory
    {
        public sealed class ObjectDesign
        {
            public string Id = string.Empty;
            public int Width = 32;
            public int Height = 32;
            public int Motif;
            public Color32 Body = RuinPalette.MidSteel;
            public Color32 Trim = RuinPalette.DarkSteel;
            public Color32 Glow = RuinPalette.AmberActive;
            public int Seed;
        }

        public static PixelCanvas Build(ObjectDesign d)
        {
            var c = new PixelCanvas(d.Width, d.Height);
            var body = RuinPalette.RampOf(d.Body);
            var rng = new System.Random(d.Seed);
            var w = d.Width; var h = d.Height;

            switch (d.Motif)
            {
                case 0: Crate(c, d, body, w, h, rng); break;
                case 1: CoinToken(c, d, w, h); break;
                case 2: GroundItem(c, d, body, w, h); break;
                case 3: Terminal(c, d, body, w, h, rng); break;
                case 4: MedicalStation(c, d, body, w, h); break;
                case 5: TransitCar(c, d, body, w, h, rng); break;
                case 6: Door(c, d, body, w, h); break;
                case 7: Vault(c, d, body, w, h); break;
                case 8: Machine(c, d, body, w, h, rng); break;
                case 9: Antenna(c, d, body, w, h); break;
                case 10: WeaponRack(c, d, body, w, h); break;
                case 11: Shelving(c, d, body, w, h, rng); break;
                case 12: Counter(c, d, body, w, h); break;
                case 13: Workbench(c, d, body, w, h, rng); break;
                case 14: Hazard(c, d, body, w, h, rng); break;
                case 15: CrateOpen(c, d, body, w, h, rng); break;
                default: Crate(c, d, body, w, h, rng); break;
            }

            c.SelectiveOutline(RuinPalette.OutlineCharcoal);
            return c;
        }

        // Supply chest / cursed chest / weapon cache: a rugged crate with latch and emissive strip (spec 17).
        private static void Crate(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            var top = h - 6;
            c.Rect(3, 3, w - 6, top - 3, body.Base);
            c.ShadeForm(3, 3, w - 6, top - 3, body.Base, body);
            // Lid, offset so closed/open state is readable.
            c.Rect(2, top - 3, w - 4, 6, RuinPalette.Darken(d.Body, 0.18f));
            c.ShadeForm(2, top - 3, w - 4, 6, RuinPalette.Darken(d.Body, 0.18f),
                RuinPalette.RampOf(RuinPalette.Darken(d.Body, 0.18f)));
            // Banding and corner brackets.
            foreach (var x in new[] { 5, w - 8 }) c.Rect(x, 4, 3, top - 5, RuinPalette.Darken(d.Trim, 0.1f));
            c.Rect(w / 2 - 3, top - 6, 7, 5, RuinPalette.Darken(d.Trim, 0.25f));
            c.Rect(w / 2 - 1, top - 5, 3, 3, d.Glow);
            for (var i = 0; i < 4; i++) c.Set(4 + rng.Next(w - 8), 4 + rng.Next(top - 6), RuinPalette.Rust);
        }

        // The same crate after its one reward transaction: lid thrown back, dark empty interior, emissive latch off —
        // the opened state has to read at a glance for the rest of the depth (spec 17).
        private static void CrateOpen(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            var top = h - 6;
            var bodyTop = top - 3;
            c.Rect(3, 3, w - 6, bodyTop - 3, body.Base);
            c.ShadeForm(3, 3, w - 6, bodyTop - 3, body.Base, body);
            foreach (var x in new[] { 5, w - 8 }) c.Rect(x, 4, 3, bodyTop - 6, RuinPalette.Darken(d.Trim, 0.1f));
            // Empty interior seen through the open top.
            c.Rect(5, bodyTop - 6, w - 10, 5, RuinPalette.Darken(d.Body, 0.62f));
            c.Rect(6, bodyTop - 5, w - 12, 2, RuinPalette.Darken(d.Body, 0.75f));
            // Lid thrown back behind the crate: a thinner, tilted plate above the body, one column offset.
            var lid = RuinPalette.Darken(d.Body, 0.18f);
            c.Rect(1, bodyTop + 1, w - 6, 4, lid);
            c.ShadeForm(1, bodyTop + 1, w - 6, 4, lid, RuinPalette.RampOf(lid));
            c.Line(1, bodyTop + 5, w - 6, bodyTop + 5, RuinPalette.Darken(d.Body, 0.4f));
            // Latch plate without its emissive cue: the object is spent.
            c.Rect(w / 2 - 3, bodyTop - 8, 7, 3, RuinPalette.Darken(d.Trim, 0.25f));
            for (var i = 0; i < 4; i++) c.Set(4 + rng.Next(w - 8), 4 + rng.Next(bodyTop - 10), RuinPalette.Rust);
        }

        // Coin pickup: an industrial currency token, deliberately not a fantasy gold coin (spec 17).
        private static void CoinToken(PixelCanvas c, ObjectDesign d, int w, int h)
        {
            var mid = w / 2;
            var ramp = RuinPalette.RampOf(RuinPalette.WarningOchre);
            c.Ellipse(mid, h / 2, w / 3f, h / 3.4f, ramp.Base);
            c.ShadeForm(0, 0, w, h, ramp.Base, ramp);
            // Stamped hex centre and a notched edge: a machined token, not a coin.
            c.Ellipse(mid, h / 2, w / 7f, h / 7f, RuinPalette.Darken(RuinPalette.WarningOchre, 0.45f));
            c.Set(mid - 1, h / 2 + 2, RuinPalette.Lighten(RuinPalette.WarningOchre, 0.5f));
            for (var a = 0; a < 6; a++)
            {
                var ang = a * Mathf.PI / 3f;
                c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * (w / 3f)), h / 2 + Mathf.RoundToInt(Mathf.Sin(ang) * (h / 3.4f)),
                    RuinPalette.Darken(RuinPalette.WarningOchre, 0.5f));
            }
        }

        // Item pickup: a ground silhouette with a controlled glow that does not hide the item.
        private static void GroundItem(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            var mid = w / 2;
            c.Ellipse(mid, 5, w / 3f, 2.5f, RuinPalette.Darken(RuinPalette.NearBlack, 0f));  // ground shadow
            c.Rect(mid - 6, 6, 13, 9, body.Base);
            c.ShadeForm(mid - 6, 6, 13, 9, body.Base, body);
            c.Rect(mid - 4, 8, 9, 2, RuinPalette.Darken(d.Trim, 0.15f));
            // Hollow rarity halo.
            for (var a = 0; a < 16; a++)
            {
                var ang = a * Mathf.PI * 2f / 16f;
                c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * (w / 2.6f)), 10 + Mathf.RoundToInt(Mathf.Sin(ang) * (h / 3.4f)), d.Glow);
            }
        }

        // Dungeon merchant / multiplayer terminal / character station: a powered console.
        private static void Terminal(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            c.Rect(4, 2, w - 8, h - 8, body.Base);
            c.ShadeForm(4, 2, w - 8, h - 8, body.Base, body);
            // Angled screen with scanlines.
            c.Rect(6, h - 16, w - 12, 10, RuinPalette.Darken(d.Glow, 0.72f));
            for (var y = h - 15; y < h - 7; y += 2) c.Line(7, y, w - 8, y, RuinPalette.Darken(d.Glow, 0.35f));
            c.Rect(8, h - 12, Mathf.Max(3, (w - 16) / 2), 2, d.Glow);
            // Control shelf and legs.
            c.Rect(3, h - 20, w - 6, 3, RuinPalette.Darken(d.Trim, 0.1f));
            c.Rect(5, 0, 4, 4, RuinPalette.Darken(d.Trim, 0.3f));
            c.Rect(w - 9, 0, 4, 4, RuinPalette.Darken(d.Trim, 0.3f));
            for (var i = 0; i < 3; i++) c.Set(8 + i * 4, 4, i == 0 ? RuinPalette.TerminalGreen : RuinPalette.Darken(d.Trim, 0.4f));
        }

        // Medical station: original cross language, pale panel, worn housing (spec 17).
        private static void MedicalStation(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            c.Rect(4, 3, w - 8, h - 7, body.Base);
            c.ShadeForm(4, 3, w - 8, h - 7, body.Base, body);
            c.Rect(6, 5, w - 12, h - 12, RuinPalette.Lighten(RuinPalette.LabWhiteRamp.Base, 0.15f));
            var mid = w / 2;
            var cy = h / 2;
            // Original cross: square-ended bars, not a trademarked medical mark.
            c.Rect(mid - 2, cy - 6, 5, 13, RuinPalette.TerminalGreen);
            c.Rect(mid - 6, cy - 2, 13, 5, RuinPalette.TerminalGreen);
            c.Rect(mid - 1, cy - 5, 3, 11, RuinPalette.Lighten(RuinPalette.TerminalGreen, 0.35f));
            c.Rect(4, 0, w - 8, 3, RuinPalette.Darken(d.Trim, 0.2f));
        }

        // Transit car: armoured underground transport, the object the game is named for (spec 17).
        private static void TransitCar(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            c.Rect(2, 6, w - 4, h - 12, body.Base);
            c.ShadeForm(2, 6, w - 4, h - 12, body.Base, body);
            // Roof ribs.
            for (var x = 5; x < w - 5; x += 7) c.Rect(x, h - 8, 4, 3, RuinPalette.Darken(d.Body, 0.25f));
            // Windows.
            for (var x = 6; x < w - 10; x += 12) c.Rect(x, h - 18, 8, 6, RuinPalette.Darken(RuinPalette.ColdBlue, 0.45f));
            // Doors and hazard chevrons.
            c.Rect(w / 2 - 6, 8, 13, h - 22, RuinPalette.Darken(d.Body, 0.35f));
            c.Line(w / 2, 9, w / 2, h - 15, RuinPalette.Darken(d.Trim, 0.4f));
            for (var x = 4; x < w - 4; x += 6) c.Line(x, 5, x + 3, 7, RuinPalette.WarningOchre);
            // Bogies and headlight.
            c.Rect(5, 2, 8, 4, RuinPalette.Darken(d.Trim, 0.4f));
            c.Rect(w - 13, 2, 8, 4, RuinPalette.Darken(d.Trim, 0.4f));
            c.Rect(w - 6, h / 2, 3, 4, d.Glow);
        }

        private static void Door(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            c.Rect(1, 1, w - 2, h - 2, body.Base);
            c.ShadeForm(1, 1, w - 2, h - 2, body.Base, body);
            c.Rect(w / 2 - 1, 2, 3, h - 4, RuinPalette.Darken(d.Body, 0.5f));   // centre split
            c.Rect(2, h - 7, w - 4, 3, RuinPalette.Darken(d.Trim, 0.15f));
            c.Rect(2, 4, w - 4, 3, RuinPalette.Darken(d.Trim, 0.15f));
            c.Rect(w / 2 - 5, h / 2 - 2, 4, 4, d.Glow);                          // powered state indicator
            for (var x = 3; x < w - 3; x += 6) c.Line(x, h - 3, x + 3, h - 1, RuinPalette.WarningOchre);
        }

        private static void Vault(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            c.Rect(2, 2, w - 4, h - 4, body.Base);
            c.ShadeForm(2, 2, w - 4, h - 4, body.Base, body);
            var mid = w / 2; var cy = h / 2;
            c.Ellipse(mid, cy, w / 3.2f, h / 3.2f, RuinPalette.Darken(d.Body, 0.3f));
            c.Ellipse(mid, cy, w / 4.5f, h / 4.5f, body.Light);
            // Locking spokes.
            for (var a = 0; a < 4; a++)
            {
                var ang = a * Mathf.PI / 2f + 0.4f;
                c.Line(mid, cy, mid + Mathf.RoundToInt(Mathf.Cos(ang) * (w / 3.2f)), cy + Mathf.RoundToInt(Mathf.Sin(ang) * (h / 3.2f)),
                    RuinPalette.Darken(d.Trim, 0.25f), 2);
            }
            c.Ellipse(mid, cy, 2.5f, 2.5f, d.Glow);
            c.RectOutline(2, 2, w - 4, h - 4, RuinPalette.Darken(d.Trim, 0.35f));
        }

        // Broken machine: a damaged industrial unit with exposed, sparking internals.
        private static void Machine(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            c.Rect(3, 2, w - 6, h - 6, body.Base);
            c.ShadeForm(3, 2, w - 6, h - 6, body.Base, body);
            c.Rect(6, h - 14, w - 12, 9, RuinPalette.Darken(d.Body, 0.55f));    // torn-open panel
            for (var i = 0; i < 5; i++)
            {
                var x = 8 + rng.Next(w - 16);
                c.Line(x, h - 13, x + rng.Next(3) - 1, h - 7, RuinPalette.Darken(RuinPalette.Rust, 0.1f));
            }
            c.Set(w / 2, h - 10, RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.4f));
            c.Set(w / 2 + 2, h - 12, RuinPalette.ElectricCyan);
            // Pipes and a dead gauge.
            c.Rect(4, 3, 3, h - 10, RuinPalette.Darken(d.Trim, 0.2f));
            c.Ellipse(w - 8, 7, 3, 3, RuinPalette.Darken(RuinPalette.PaleSteel, 0.1f));
            c.Set(w - 8, 7, RuinPalette.EmergencyRed);
        }

        // Supply signal: a deployed beacon mast.
        private static void Antenna(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            var mid = w / 2;
            c.Rect(mid - 7, 2, 15, 7, body.Base);
            c.ShadeForm(mid - 7, 2, 15, 7, body.Base, body);
            c.Line(mid, 9, mid, h - 6, RuinPalette.Darken(d.Trim, 0.15f), 2);
            for (var i = 0; i < 3; i++)
            {
                var y = h - 8 - i * 5;
                c.Line(mid - 5 + i, y, mid + 5 - i, y, RuinPalette.Darken(d.Trim, 0.25f));
            }
            c.Ellipse(mid, h - 4, 2.5f, 2.5f, d.Glow);
            c.Set(mid, h - 2, RuinPalette.Lighten(d.Glow, 0.5f));
            c.Line(mid - 6, 2, mid - 9, 0, RuinPalette.Darken(d.Trim, 0.3f));
            c.Line(mid + 6, 2, mid + 9, 0, RuinPalette.Darken(d.Trim, 0.3f));
        }

        // Shelter Loadout station: a weapon rack / preparation bench.
        private static void WeaponRack(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            c.Rect(2, 2, w - 4, 6, body.Base);
            c.ShadeForm(2, 2, w - 4, 6, body.Base, body);
            c.Rect(2, h - 8, w - 4, 5, RuinPalette.Darken(d.Body, 0.2f));
            for (var x = 6; x < w - 6; x += 8)
            {
                c.Line(x, 8, x + 2, h - 9, RuinPalette.Darken(RuinPalette.MidSteel, 0.15f), 2);
                c.Rect(x - 1, h - 13, 5, 3, RuinPalette.Darken(d.Trim, 0.2f));
            }
            c.Rect(3, h - 4, w - 6, 2, d.Glow);
        }

        // Shelter Storage: lockers and shelving, a strong rectangular storage read.
        private static void Shelving(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            c.Rect(2, 1, w - 4, h - 2, body.Base);
            c.ShadeForm(2, 1, w - 4, h - 2, body.Base, body);
            for (var y = 6; y < h - 4; y += 8) c.Rect(3, y, w - 6, 2, RuinPalette.Darken(d.Body, 0.4f));
            for (var y = 8; y < h - 4; y += 8)
            for (var x = 5; x < w - 8; x += 9)
                c.Rect(x, y, 7, 5, rng.Next(3) == 0 ? RuinPalette.Darken(RuinPalette.Rust, 0.1f) : RuinPalette.Darken(d.Trim, 0.05f));
            c.Rect(w - 7, h / 2 - 2, 3, 5, d.Glow);
        }

        // Shelter Trader: a counter with salvaged goods and a lamp.
        private static void Counter(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h)
        {
            c.Rect(2, 2, w - 4, h - 12, body.Base);
            c.ShadeForm(2, 2, w - 4, h - 12, body.Base, body);
            c.Rect(0, h - 12, w, 4, RuinPalette.Darken(d.Body, 0.15f));           // counter top
            for (var x = 5; x < w - 6; x += 7) c.Rect(x, h - 8, 5, 5, RuinPalette.Darken(RuinPalette.DustBeige, 0.2f));
            // Hanging lamp: the trader's recognisable cue.
            c.Line(w - 8, h - 1, w - 8, h - 6, RuinPalette.Darken(d.Trim, 0.4f));
            c.Taper(w - 8, h - 6, h - 9, 1, 4, RuinPalette.Darken(d.Trim, 0.2f));
            c.Rect(w - 11, h - 10, 7, 2, d.Glow);
        }

        // Shelter Workshop: bench, tools, powered machinery.
        private static void Workbench(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            c.Rect(1, 3, w - 2, 6, body.Base);
            c.ShadeForm(1, 3, w - 2, 6, body.Base, body);
            c.Rect(3, 0, 4, 4, RuinPalette.Darken(d.Trim, 0.3f));
            c.Rect(w - 7, 0, 4, 4, RuinPalette.Darken(d.Trim, 0.3f));
            // Tool board behind the bench.
            c.Rect(2, 9, w - 4, h - 11, RuinPalette.Darken(d.Body, 0.35f));
            for (var x = 5; x < w - 5; x += 6)
            {
                var len = 4 + rng.Next(5);
                c.Line(x, h - 4, x, h - 4 - len, RuinPalette.Darken(RuinPalette.PaleSteel, 0.2f));
                c.Set(x, h - 4 - len, RuinPalette.Darken(RuinPalette.Rust, 0.1f));
            }
            c.Rect(w - 10, 4, 7, 4, d.Glow);
        }

        // A generic hazard prop (unstable debris, pressure vent) used by room dressing.
        private static void Hazard(PixelCanvas c, ObjectDesign d, RuinPalette.Ramp body, int w, int h, System.Random rng)
        {
            for (var i = 0; i < 6; i++)
            {
                var x = 3 + rng.Next(w - 8);
                var y = 2 + rng.Next(h / 2);
                var s = 3 + rng.Next(5);
                c.Rect(x, y, s, s - 1, body.Base);
            }
            c.ShadeForm(0, 0, w, h, body.Base, body);
            for (var x = 2; x < w - 2; x += 6) c.Line(x, 1, x + 3, 3, RuinPalette.WarningOchre);
            c.Set(w / 2, h / 2, d.Glow);
        }

        /// <summary>Every world-object and Shelter role the manifest enumerates, with its motif and palette.</summary>
        public static IReadOnlyList<ObjectDesign> All() => new List<ObjectDesign>
        {
            // --- dungeon world objects ---
            new() { Id = "world_supply_chest", Motif = 0, Body = RuinPalette.Hex("#5A4E3C"), Trim = RuinPalette.Rust, Glow = RuinPalette.AmberActive, Seed = 11 },
            new() { Id = "world_supply_chest_open", Motif = 15, Body = RuinPalette.Hex("#5A4E3C"), Trim = RuinPalette.Rust, Glow = RuinPalette.AmberActive, Seed = 11 },
            new() { Id = "world_coin_pickup", Motif = 1, Width = 16, Height = 16, Seed = 12 },
            new() { Id = "world_item_pickup", Motif = 2, Width = 24, Height = 24, Body = RuinPalette.MidSteel, Glow = RuinPalette.ColdBlue, Seed = 13 },
            new() { Id = "world_dungeon_merchant", Motif = 3, Width = 32, Height = 40, Body = RuinPalette.Hex("#4A4640"), Glow = RuinPalette.WarningOchre, Seed = 14 },
            new() { Id = "world_boss_cache_gate", Motif = 7, Width = 40, Height = 40, Body = RuinPalette.Hex("#3E4548"), Glow = RuinPalette.EmergencyRed, Seed = 15 },
            new() { Id = "world_door_socket", Motif = 6, Width = 32, Height = 40, Body = RuinPalette.Hex("#46504F"), Glow = RuinPalette.TerminalGreen, Seed = 16 },
            new() { Id = "world_room_hazard", Motif = 14, Width = 32, Height = 24, Body = RuinPalette.Hex("#5A5148"), Glow = RuinPalette.EmergencyRed, Seed = 17 },
            new() { Id = "world_transit_car", Motif = 5, Width = 64, Height = 40, Body = RuinPalette.Hex("#44504E"), Trim = RuinPalette.DarkSteel, Glow = RuinPalette.AmberActive, Seed = 18 },

            // --- dungeon event objects (spec 17, six kinds) ---
            new() { Id = "event_LockedVault", Motif = 7, Width = 40, Height = 40, Body = RuinPalette.Hex("#4A5254"), Glow = RuinPalette.WarningOchre, Seed = 21 },
            new() { Id = "event_CursedChest", Motif = 0, Body = RuinPalette.Hex("#3E3646"), Trim = RuinPalette.Hex("#6B5A7A"), Glow = RuinPalette.Hex("#8C6BB1"), Seed = 22 },
            new() { Id = "event_BrokenMachine", Motif = 8, Width = 40, Height = 32, Body = RuinPalette.Hex("#4E4840"), Glow = RuinPalette.ElectricCyan, Seed = 23 },
            new() { Id = "event_SupplySignal", Motif = 9, Width = 32, Height = 40, Body = RuinPalette.Hex("#4C5450"), Glow = RuinPalette.TerminalGreen, Seed = 24 },
            new() { Id = "event_MedicalStation", Motif = 4, Width = 32, Height = 40, Body = RuinPalette.Hex("#6E7472"), Glow = RuinPalette.TerminalGreen, Seed = 25 },
            new() { Id = "event_WeaponCache", Motif = 10, Width = 40, Height = 32, Body = RuinPalette.Hex("#4A4A44"), Glow = RuinPalette.AmberActive, Seed = 26 },

            // --- Shelter stations (spec 16) ---
            new() { Id = "base_storage", Motif = 11, Width = 40, Height = 40, Body = RuinPalette.Hex("#4E5250"), Glow = RuinPalette.AmberActive, Seed = 31 },
            new() { Id = "base_loadout", Motif = 10, Width = 48, Height = 32, Body = RuinPalette.Hex("#4A4640"), Glow = RuinPalette.AmberActive, Seed = 32 },
            new() { Id = "base_trader", Motif = 12, Width = 48, Height = 32, Body = RuinPalette.Hex("#5A4E3C"), Glow = RuinPalette.AmberActive, Seed = 33 },
            new() { Id = "base_character_station", Motif = 3, Width = 32, Height = 40, Body = RuinPalette.Hex("#4A5254"), Glow = RuinPalette.TerminalGreen, Seed = 34 },
            new() { Id = "base_workshop", Motif = 13, Width = 48, Height = 32, Body = RuinPalette.Hex("#4E4840"), Glow = RuinPalette.OxideOrange, Seed = 35 },
            new() { Id = "base_multiplayer_terminal", Motif = 3, Width = 32, Height = 40, Body = RuinPalette.Hex("#3E4A4E"), Glow = RuinPalette.ElectricCyan, Seed = 36 },
            new() { Id = "base_expedition_transit", Motif = 5, Width = 64, Height = 40, Body = RuinPalette.Hex("#44504E"), Glow = RuinPalette.AmberActive, Seed = 37 }
        };
    }
}
