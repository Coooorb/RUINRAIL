using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The Shelter's station pictograms: one 16×16 symbol per station, drawn for the UI at native pixels (the panel header
    /// shows them 1:1). They replace thumbnails of the station props, which had to be shrunk from their room size and
    /// came out as unreadable smudges. Symbols, not scenes: a crate, a backpack, a price tag, a survivor, a wrench, a
    /// party of three and the Transit Car — each a single silhouette with a dark outline, warm ink and one amber accent,
    /// in <c>BaseStation</c> order.
    /// </summary>
    public static class ShelterIconFactory
    {
        public const int Size = 16;

        private static readonly Color32 Outline = RuinPalette.OutlineCharcoal;
        private static readonly Color32 Ink = RuinPalette.Hex("#E8E2D2");
        private static readonly Color32 InkShade = RuinPalette.Hex("#A7A398");
        private static readonly Color32 Amber = RuinPalette.AmberActive;
        private static readonly Color32 AmberShade = RuinPalette.Hex("#A8712C");
        private static readonly Color32 Steel = RuinPalette.PaleSteel;
        private static readonly Color32 SteelDark = RuinPalette.MidSteel;

        public static IReadOnlyList<PixelCanvas> All() => new[] { Storage(), Loadout(), Trader(), Character(), Workshop(), Multiplayer(), Transit() };

        /// <summary>A 1 px dark outline around the silhouette (outside it, so no detail pixel is darkened).</summary>
        private static PixelCanvas Finish(PixelCanvas c)
        {
            var edge = new List<(int x, int y)>();
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
                if (!c.IsOpaque(x, y) && (c.IsOpaque(x - 1, y) || c.IsOpaque(x + 1, y) || c.IsOpaque(x, y - 1) || c.IsOpaque(x, y + 1)))
                    edge.Add((x, y));
            foreach (var (x, y) in edge) c.Set(x, y, Outline);
            return c;
        }

        // y is up in the canvas (row 0 = bottom).

        /// <summary>STORAGE: a banded supply crate.</summary>
        public static PixelCanvas Storage()
        {
            var c = new PixelCanvas(Size, Size);
            c.Rect(2, 2, 12, 10, SteelDark);
            c.Rect(3, 3, 10, 8, Steel);
            c.Rect(2, 11, 12, 2, Ink);           // lid
            c.Rect(2, 7, 12, 1, SteelDark);      // band
            c.Rect(7, 6, 2, 3, Amber);           // latch
            c.Set(7, 6, AmberShade); c.Set(8, 6, AmberShade);
            return Finish(c);
        }

        /// <summary>LOADOUT: a backpack with its flap and straps.</summary>
        public static PixelCanvas Loadout()
        {
            var c = new PixelCanvas(Size, Size);
            c.Rect(4, 2, 8, 10, AmberShade);
            c.Rect(5, 3, 6, 8, Amber);
            c.Rect(3, 4, 1, 6, AmberShade);      // side pockets
            c.Rect(12, 4, 1, 6, AmberShade);
            c.Rect(4, 9, 8, 3, Ink);             // flap
            c.Rect(5, 12, 6, 1, InkShade);
            c.Rect(6, 13, 4, 1, SteelDark);      // carry loop
            c.Set(6, 14, SteelDark); c.Set(9, 14, SteelDark);
            c.Rect(7, 7, 2, 2, SteelDark);       // buckle
            return Finish(c);
        }

        /// <summary>TRADER: a coin on a price tag.</summary>
        public static PixelCanvas Trader()
        {
            var c = new PixelCanvas(Size, Size);
            // The tag, angled: a body and a punched hole.
            for (var y = 3; y <= 12; y++)
            for (var x = 2; x <= 11; x++)
                if (x + (12 - y) <= 16 && x - y >= -9) c.Set(x, y, Ink);
            c.Set(4, 10, Outline);
            c.Rect(3, 4, 6, 1, InkShade);
            // The coin over its corner.
            c.Ellipse(10.5f, 5.5f, 4.2f, 4.2f, AmberShade);
            c.Ellipse(10.5f, 5.5f, 3.2f, 3.2f, Amber);
            c.Rect(10, 4, 1, 4, AmberShade);     // the C mark
            c.Rect(10, 4, 2, 1, AmberShade);
            c.Rect(10, 7, 2, 1, AmberShade);
            return Finish(c);
        }

        /// <summary>CHARACTER: the survivor's head and shoulders.</summary>
        public static PixelCanvas Character()
        {
            var c = new PixelCanvas(Size, Size);
            c.Ellipse(8f, 4f, 6f, 3.6f, SteelDark);    // shoulders
            c.Ellipse(8f, 4f, 5f, 2.8f, Steel);
            c.Rect(3, 1, 10, 3, SteelDark);
            c.Ellipse(8f, 10.5f, 3.4f, 3.6f, Ink);     // head
            c.Rect(5, 12, 6, 2, InkShade);             // hair line
            c.Rect(6, 8, 4, 1, InkShade);
            c.Rect(7, 4, 2, 2, Amber);                 // collar tag
            return Finish(c);
        }

        /// <summary>WORKSHOP: an open-ended wrench, diagonal.</summary>
        public static PixelCanvas Workshop()
        {
            var c = new PixelCanvas(Size, Size);
            for (var i = 0; i < 9; i++) { c.Set(3 + i, 3 + i, Steel); c.Set(4 + i, 3 + i, SteelDark); c.Set(3 + i, 4 + i, Ink); }
            // Jaws at the top right, with the opening.
            c.Ellipse(12.5f, 12.5f, 3f, 3f, Steel);
            for (var x = 12; x <= 15; x++) for (var y = 12; y <= 15; y++) c.Erase(x, y);
            c.Set(11, 14, Ink); c.Set(14, 11, Ink);
            // Ring end at the bottom left, amber grip band.
            c.Ellipse(3f, 3f, 2.6f, 2.6f, Steel);
            c.Set(3, 3, Outline);
            c.Rect(6, 6, 2, 2, Amber);
            return Finish(c);
        }

        /// <summary>MULTIPLAYER: a party of three, the middle one in front.</summary>
        public static PixelCanvas Multiplayer()
        {
            var c = new PixelCanvas(Size, Size);
            foreach (var x in new[] { 4f, 12f })
            {
                c.Ellipse(x, 9.5f, 2.2f, 2.2f, InkShade);
                c.Ellipse(x, 4f, 3.2f, 2.6f, SteelDark);
            }

            c.Ellipse(8f, 10.5f, 2.6f, 2.6f, Ink);
            c.Ellipse(8f, 3.6f, 4f, 3.2f, Steel);
            c.Rect(4, 1, 8, 2, Steel);
            c.Rect(7, 4, 2, 2, Amber);
            return Finish(c);
        }

        /// <summary>TRANSIT: the Transit Car head-on, on its rails.</summary>
        public static PixelCanvas Transit()
        {
            var c = new PixelCanvas(Size, Size);
            c.Rect(3, 4, 10, 10, SteelDark);
            c.Rect(4, 5, 8, 8, Steel);
            c.Rect(5, 9, 6, 3, Ink);             // windscreen
            c.Rect(5, 9, 6, 1, InkShade);
            c.Rect(4, 6, 2, 1, Amber);           // lamps
            c.Rect(10, 6, 2, 1, Amber);
            c.Rect(3, 14, 10, 1, SteelDark);     // roof
            c.Rect(5, 2, 2, 2, SteelDark);       // wheels
            c.Rect(9, 2, 2, 2, SteelDark);
            c.Rect(1, 1, 14, 1, AmberShade);     // the rail
            return Finish(c);
        }
    }
}
