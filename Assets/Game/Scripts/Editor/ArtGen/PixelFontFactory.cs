using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// An original 5x7 pixel typeface, built glyph by glyph.
    ///
    /// FINAL_ART_PRODUCTION_SPEC 18.7 requires a legally safe pixel-compatible font and calls out that `1`, `I`, `0`
    /// and `O` must be distinguishable. Shipping a third-party TTF would need licence clearance, so the face is
    /// authored here as bitmaps and assembled into a Unity Font asset with a generated atlas — original work with no
    /// licensing question, and it is what removes the release dependency on Unity's builtin LegacyRuntime.ttf.
    ///
    /// Each glyph is 5 wide by 7 tall, described as row strings read top-down. '#' is ink.
    /// </summary>
    public static class PixelFontFactory
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;
        /// <summary>One pixel of side bearing, so text does not need manual kerning to read.</summary>
        public const int Advance = GlyphWidth + 1;
        public const int LineHeight = GlyphHeight + 2;

        /// <summary>Every character the UI can render. Anything outside this maps to a visible fallback box.</summary>
        public const string Charset =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,:;!?'\"()[]-+/%*#<>=_@&…·—–";

        private static readonly Dictionary<char, string[]> Glyphs = Build();

        public static bool Has(char c) => Glyphs.ContainsKey(c);

        public static string[] Rows(char c) =>
            Glyphs.TryGetValue(c, out var rows) ? rows : Glyphs['?'];

        /// <summary>Draws one glyph with its top-left at (x, yTop). Canvas y grows upward, so rows walk downward.</summary>
        public static void DrawGlyph(PixelCanvas canvas, char ch, int x, int yTop, Color32 color)
        {
            var rows = Rows(ch);
            for (var r = 0; r < rows.Length; r++)
            {
                var row = rows[r];
                for (var i = 0; i < row.Length && i < GlyphWidth; i++)
                    if (row[i] == '#') canvas.Set(x + i, yTop - r, color);
            }
        }

        public static int MeasureWidth(string text) => string.IsNullOrEmpty(text) ? 0 : text.Length * Advance - 1;

        /// <summary>Renders a single line of text onto a tight canvas. Used for UI atlases and text-fit checks.</summary>
        public static PixelCanvas RenderLine(string text, Color32 color)
        {
            var canvas = new PixelCanvas(Mathf.Max(1, MeasureWidth(text)), GlyphHeight);
            var x = 0;
            foreach (var ch in text)
            {
                DrawGlyph(canvas, ch, x, GlyphHeight - 1, color);
                x += Advance;
            }
            return canvas;
        }

        /// <summary>Lays the whole charset out in a grid atlas; the Font asset's character rects index into this.</summary>
        /// <summary>
        /// One transparent pixel of gutter around every glyph cell.
        ///
        /// Without it the cells abut, and a quad whose UV right edge lands exactly on the cell boundary samples the
        /// first column of the *next* glyph — so an M measured six pixels wide against a six pixel advance and every
        /// word in the game rendered as a smear of touching letters. The gutter gives that half-texel of slop
        /// somewhere harmless to land.
        /// </summary>
        public const int AtlasPadding = 1;

        public static PixelCanvas BuildAtlas(out Dictionary<char, RectInt> rects, int columns = 16)
        {
            rects = new Dictionary<char, RectInt>();
            var rows = Mathf.CeilToInt((float)Charset.Length / columns);
            var cellWidth = GlyphWidth + AtlasPadding;
            var cellHeight = GlyphHeight + AtlasPadding;
            var atlas = new PixelCanvas(columns * cellWidth, rows * cellHeight);

            for (var i = 0; i < Charset.Length; i++)
            {
                var ch = Charset[i];
                var cx = (i % columns) * cellWidth;
                // Row 0 at the atlas top, which is high y in texture space. The padding row sits above each glyph,
                // so the glyph itself starts one pixel down from the cell top.
                var rowIndex = i / columns;
                var topY = atlas.Height - 1 - rowIndex * cellHeight - AtlasPadding;
                DrawGlyph(atlas, ch, cx, topY, Color.white);
                rects[ch] = new RectInt(cx, topY - (GlyphHeight - 1), GlyphWidth, GlyphHeight);
            }

            return atlas;
        }

        private static Dictionary<char, string[]> Build()
        {
            var g = new Dictionary<char, string[]>();

            void Add(char c, params string[] rows) => g[c] = rows;

            // Uppercase
            Add('A', ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#");
            Add('B', "####.", "#...#", "####.", "#...#", "#...#", "#...#", "####.");
            Add('C', ".####", "#....", "#....", "#....", "#....", "#....", ".####");
            Add('D', "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####.");
            Add('E', "#####", "#....", "####.", "#....", "#....", "#....", "#####");
            Add('F', "#####", "#....", "####.", "#....", "#....", "#....", "#....");
            Add('G', ".####", "#....", "#....", "#..##", "#...#", "#...#", ".####");
            Add('H', "#...#", "#...#", "#####", "#...#", "#...#", "#...#", "#...#");
            // I has serifs so it cannot be confused with 1 or l (spec 18.7).
            Add('I', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####");
            Add('J', "####.", "...#.", "...#.", "...#.", "...#.", "#..#.", ".##..");
            Add('K', "#...#", "#..#.", "##...", "##...", "#.#..", "#..#.", "#...#");
            Add('L', "#....", "#....", "#....", "#....", "#....", "#....", "#####");
            Add('M', "#...#", "##.##", "#.#.#", "#...#", "#...#", "#...#", "#...#");
            Add('N', "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#");
            // O is a full rounded bowl; 0 carries a slash.
            Add('O', ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            Add('P', "####.", "#...#", "#...#", "####.", "#....", "#....", "#....");
            Add('Q', ".###.", "#...#", "#...#", "#...#", "#.#.#", "#..#.", ".##.#");
            Add('R', "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#");
            Add('S', ".####", "#....", "#....", ".###.", "....#", "....#", "####.");
            Add('T', "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#..");
            Add('U', "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###.");
            Add('V', "#...#", "#...#", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            Add('W', "#...#", "#...#", "#...#", "#...#", "#.#.#", "##.##", "#...#");
            Add('X', "#...#", "#...#", ".#.#.", "..#..", ".#.#.", "#...#", "#...#");
            Add('Y', "#...#", "#...#", ".#.#.", "..#..", "..#..", "..#..", "..#..");
            Add('Z', "#####", "....#", "...#.", "..#..", ".#...", "#....", "#####");

            // Lowercase: 5x7 cell with a 5-tall x-height and real descenders.
            Add('a', ".....", ".....", ".###.", "....#", ".####", "#...#", ".####");
            Add('b', "#....", "#....", "####.", "#...#", "#...#", "#...#", "####.");
            Add('c', ".....", ".....", ".####", "#....", "#....", "#....", ".####");
            Add('d', "....#", "....#", ".####", "#...#", "#...#", "#...#", ".####");
            Add('e', ".....", ".....", ".###.", "#...#", "#####", "#....", ".####");
            Add('f', "..##.", ".#...", "####.", ".#...", ".#...", ".#...", ".#...");
            Add('g', ".....", ".####", "#...#", "#...#", ".####", "....#", ".###.");
            Add('h', "#....", "#....", "####.", "#...#", "#...#", "#...#", "#...#");
            Add('i', "..#..", ".....", ".##..", "..#..", "..#..", "..#..", ".###.");
            Add('j', "...#.", ".....", "..##.", "...#.", "...#.", "#..#.", ".##..");
            Add('k', "#....", "#....", "#..#.", "#.#..", "##...", "#.#..", "#..#.");
            Add('l', ".##..", "..#..", "..#..", "..#..", "..#..", "..#..", ".###.");
            Add('m', ".....", ".....", "##.#.", "#.#.#", "#.#.#", "#...#", "#...#");
            Add('n', ".....", ".....", "####.", "#...#", "#...#", "#...#", "#...#");
            Add('o', ".....", ".....", ".###.", "#...#", "#...#", "#...#", ".###.");
            Add('p', ".....", "####.", "#...#", "#...#", "####.", "#....", "#....");
            Add('q', ".....", ".####", "#...#", "#...#", ".####", "....#", "....#");
            Add('r', ".....", ".....", "#.##.", "##...", "#....", "#....", "#....");
            Add('s', ".....", ".....", ".####", "#....", ".###.", "....#", "####.");
            Add('t', ".#...", ".#...", "####.", ".#...", ".#...", ".#..#", "..##.");
            Add('u', ".....", ".....", "#...#", "#...#", "#...#", "#..##", ".##.#");
            Add('v', ".....", ".....", "#...#", "#...#", "#...#", ".#.#.", "..#..");
            Add('w', ".....", ".....", "#...#", "#...#", "#.#.#", "#.#.#", ".#.#.");
            Add('x', ".....", ".....", "#...#", ".#.#.", "..#..", ".#.#.", "#...#");
            Add('y', ".....", "#...#", "#...#", "#...#", ".####", "....#", ".###.");
            Add('z', ".....", ".....", "#####", "...#.", "..#..", ".#...", "#####");

            // Digits: 0 is slashed so it cannot be read as O; 1 has a base serif so it cannot be read as I or l.
            Add('0', ".###.", "#...#", "#..##", "#.#.#", "##..#", "#...#", ".###.");
            Add('1', "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###.");
            Add('2', ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####");
            Add('3', "####.", "....#", "....#", ".###.", "....#", "....#", "####.");
            Add('4', "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#.");
            Add('5', "#####", "#....", "####.", "....#", "....#", "#...#", ".###.");
            Add('6', ".###.", "#....", "#....", "####.", "#...#", "#...#", ".###.");
            Add('7', "#####", "....#", "...#.", "..#..", "..#..", "..#..", "..#..");
            Add('8', ".###.", "#...#", "#...#", ".###.", "#...#", "#...#", ".###.");
            Add('9', ".###.", "#...#", "#...#", ".####", "....#", "....#", ".###.");

            // Punctuation and symbols
            Add(' ', ".....", ".....", ".....", ".....", ".....", ".....", ".....");
            Add('.', ".....", ".....", ".....", ".....", ".....", ".##..", ".##..");
            Add(',', ".....", ".....", ".....", ".....", ".##..", ".##..", ".#...");
            Add(':', ".....", ".##..", ".##..", ".....", ".##..", ".##..", ".....");
            Add(';', ".....", ".##..", ".##..", ".....", ".##..", ".##..", ".#...");
            Add('!', "..#..", "..#..", "..#..", "..#..", "..#..", ".....", "..#..");
            Add('?', ".###.", "#...#", "....#", "...#.", "..#..", ".....", "..#..");
            Add('\'', "..#..", "..#..", ".....", ".....", ".....", ".....", ".....");
            Add('"', ".#.#.", ".#.#.", ".....", ".....", ".....", ".....", ".....");
            Add('(', "...#.", "..#..", ".#...", ".#...", ".#...", "..#..", "...#.");
            Add(')', ".#...", "..#..", "...#.", "...#.", "...#.", "..#..", ".#...");
            Add('[', ".###.", ".#...", ".#...", ".#...", ".#...", ".#...", ".###.");
            Add(']', ".###.", "...#.", "...#.", "...#.", "...#.", "...#.", ".###.");
            Add('<', "...#.", "..#..", ".#...", "#....", ".#...", "..#..", "...#.");
            Add('>', ".#...", "..#..", "...#.", "....#", "...#.", "..#..", ".#...");
            Add('-', ".....", ".....", ".....", "#####", ".....", ".....", ".....");
            Add('_', ".....", ".....", ".....", ".....", ".....", ".....", "#####");
            Add('=', ".....", ".....", "#####", ".....", "#####", ".....", ".....");
            Add('+', ".....", "..#..", "..#..", "#####", "..#..", "..#..", ".....");
            Add('/', "....#", "....#", "...#.", "..#..", ".#...", "#....", "#....");
            Add('*', ".....", "#.#.#", ".###.", "#####", ".###.", "#.#.#", ".....");
            Add('#', ".#.#.", "#####", ".#.#.", ".#.#.", "#####", ".#.#.", ".....");
            Add('%', "##..#", "##.#.", "..#..", ".#...", "#..##", "..###", ".....");
            Add('@', ".###.", "#...#", "#.###", "#.#.#", "#.###", "#....", ".###.");
            Add('&', ".##..", "#..#.", ".##..", "##.#.", "#..#.", "#...#", ".##.#");

            // Typographic punctuation the UI genuinely emits and the face was missing.
            //
            // TextFit.Clamp appends an ellipsis to every string it truncates, the HUD separates depth from biome with
            // an em dash and writes an em dash for an empty weapon or consumable slot, and the menus use a middle dot
            // between terms. Without these three characters the font had no glyph for them at all, so the most common
            // truncation marker in the whole UI drew as nothing.
            Add('…', ".....", ".....", ".....", ".....", ".....", ".....", "#.#.#");
            Add('·', ".....", ".....", ".....", "..#..", ".....", ".....", ".....");
            Add('—', ".....", ".....", ".....", "#####", ".....", ".....", ".....");
            Add('–', ".....", ".....", ".....", ".###.", ".....", ".....", ".....");

            return g;
        }

        /// <summary>Wraps text to a pixel width, for the text-fit checks in spec 24.6.</summary>
        public static List<string> Wrap(string text, int maxPixelWidth)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            var line = new StringBuilder();
            foreach (var word in text.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (MeasureWidth(candidate) <= maxPixelWidth) { line.Clear(); line.Append(candidate); continue; }
                if (line.Length > 0) lines.Add(line.ToString());
                line.Clear();
                line.Append(word);
            }

            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }
    }
}
