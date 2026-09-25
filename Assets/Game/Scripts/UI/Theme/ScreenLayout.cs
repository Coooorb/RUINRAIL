using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Navigation;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// A rectangle in reference-screen pixels with the origin at the top-left and Y growing downward, which is how
    /// the screens are authored and how a screenshot is read. The uGUI builder converts to anchored positions.
    /// </summary>
    public readonly struct UiRect
    {
        public UiRect(int x, int y, int width, int height)
        {
            X = x; Y = y; Width = width; Height = height;
        }

        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }

        public int Right => X + Width;
        public int Bottom => Y + Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public bool Overlaps(UiRect other) =>
            !IsEmpty && !other.IsEmpty &&
            X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

        /// <summary>True when this rectangle lies entirely inside <paramref name="container"/>.</summary>
        public bool Within(UiRect container) =>
            X >= container.X && Y >= container.Y && Right <= container.Right && Bottom <= container.Bottom;

        public UiRect Inset(int by) => new(X + by, Y + by, Width - by * 2, Height - by * 2);

        public override string ToString() => $"({X},{Y} {Width}x{Height})";
    }

    /// <summary>One measured piece of text: what will actually be drawn, and the box it is guaranteed to stay inside.</summary>
    public readonly struct MeasuredText
    {
        public MeasuredText(string text, UiRect bounds, int scale)
        {
            Text = text ?? string.Empty;
            Bounds = bounds;
            Scale = scale;
        }

        /// <summary>The string after any truncation the layout had to apply — never the caller's untrimmed input.</summary>
        public string Text { get; }
        public UiRect Bounds { get; }
        public int Scale { get; }
    }

    /// <summary>
    /// Metrics of the RUINRAIL pixel font at the 640x360 reference.
    ///
    /// The face is a bitmap font: Unity renders it at the size it was authored at, and a larger heading is an integer
    /// scale of that face, never a resampled one. So every text box in the layout is measured in whole glyph cells,
    /// which is what makes overlap a thing the layout can rule out rather than notice afterwards.
    /// </summary>
    public static class UiText
    {
        /// <summary>Horizontal advance of one glyph at scale 1 (PixelFontFactory.Advance).</summary>
        public const int Advance = TextFit.PixelsPerChar;
        /// <summary>Cap height of the face at scale 1 (PixelFontFactory.GlyphHeight).</summary>
        public const int GlyphHeight = 7;
        /// <summary>Baseline-to-baseline distance at scale 1 (PixelFontFactory.LineHeight).</summary>
        public const int LineHeight = 9;

        public static int Width(string text, int scale = 1) => (text?.Length ?? 0) * Advance * Math.Max(1, scale);
        public static int Height(int lines = 1, int scale = 1) =>
            lines <= 0 ? 0 : (LineHeight * (lines - 1) + GlyphHeight) * Math.Max(1, scale);

        /// <summary>How many whole glyphs fit in a pixel width at the given scale.</summary>
        public static int CharsFor(int widthPixels, int scale = 1) => Math.Max(0, widthPixels / (Advance * Math.Max(1, scale)));

        /// <summary>Truncates with an ellipsis so the result is guaranteed to fit the width. Never clips mid-glyph.</summary>
        public static string Fit(string text, int widthPixels, int scale = 1) =>
            TextFit.Clamp(text, CharsFor(widthPixels, scale));

        /// <summary>
        /// Word-wraps a sentence into lines that each fit the width (whole glyph cells, never mid-glyph). A single word
        /// longer than a line is split hard rather than clipped, so nothing the writer put in is lost.
        /// </summary>
        public static System.Collections.Generic.List<string> Wrap(string text, int widthPixels, int scale = 1)
        {
            var lines = new System.Collections.Generic.List<string>();
            var chars = CharsFor(widthPixels, scale);
            if (string.IsNullOrWhiteSpace(text) || chars <= 0) return lines;
            var current = new System.Text.StringBuilder();
            foreach (var rawWord in text.Split(' '))
            {
                var word = rawWord;
                while (word.Length > chars)
                {
                    if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
                    lines.Add(word.Substring(0, chars));
                    word = word.Substring(chars);
                }

                if (word.Length == 0) continue;
                if (current.Length == 0) current.Append(word);
                else if (current.Length + 1 + word.Length <= chars) current.Append(' ').Append(word);
                else { lines.Add(current.ToString()); current.Clear(); current.Append(word); }
            }

            if (current.Length > 0) lines.Add(current.ToString());
            return lines;
        }
    }

    /// <summary>
    /// The measured geometry of the Shelter / Main Menu front-end at 640x360.
    ///
    /// This type exists because the previous screens positioned text by hand: the profile block was a fixed-width
    /// label dropped at a fixed coordinate, so a long display name or a three-digit level ran straight through the
    /// line underneath it. Here the header reserves space from the measured strings first and truncates what cannot
    /// fit, so overlap is not a thing to notice in a screenshot — it is a thing the geometry cannot produce.
    ///
    /// It is pure arithmetic with no Unity types beyond none at all, so the regression tests exercise the real
    /// layout rather than a re-implementation of it.
    /// </summary>
    public static class ScreenLayout
    {
        public const int Width = UiTheme.ScreenWidth;
        public const int Height = UiTheme.ScreenHeight;

        public static UiRect Screen => new(0, 0, Width, Height);
        public static UiRect Header => new(0, 0, Width, UiTheme.HeaderHeight);
        public static UiRect TabBar => new(0, UiTheme.HeaderHeight, Width, UiTheme.TabBarHeight);
        public static UiRect Content => new(0, UiTheme.ContentTop, Width, UiTheme.ContentHeight);
        public static UiRect Footer => new(0, UiTheme.ContentBottom, Width, UiTheme.FooterHeight);

        // ---- content columns (zone 4) ----
        public const int LeftColumnWidth = 150;
        public const int RightColumnWidth = 152;
        private const int ColumnGap = 4;

        public static UiRect LeftColumn => new(
            UiTheme.ScreenMargin, UiTheme.ContentTop + ColumnGap,
            LeftColumnWidth, UiTheme.ContentHeight - ColumnGap * 2);

        public static UiRect RightColumn => new(
            Width - UiTheme.ScreenMargin - RightColumnWidth, UiTheme.ContentTop + ColumnGap,
            RightColumnWidth, UiTheme.ContentHeight - ColumnGap * 2);

        public static UiRect MainColumn
        {
            get
            {
                var x = LeftColumn.Right + ColumnGap;
                return new UiRect(x, UiTheme.ContentTop + ColumnGap, RightColumn.X - ColumnGap - x, UiTheme.ContentHeight - ColumnGap * 2);
            }
        }

        // ---------------- header ----------------

        /// <summary>
        /// The header, measured. <paramref name="title"/> is the screen identity, the rest is the profile block that
        /// used to collide with it.
        /// </summary>
        public sealed class HeaderLayout
        {
            public MeasuredText Title;
            public MeasuredText Subtitle;
            public MeasuredText ProfileName;
            public MeasuredText ProfileStats;
            public UiRect ProfileBlock;
            public UiRect IdentityBlock;

            /// <summary>Every text box of the header, for the overlap and containment assertions.</summary>
            public IEnumerable<MeasuredText> Texts
            {
                get
                {
                    yield return Title;
                    yield return Subtitle;
                    yield return ProfileName;
                    yield return ProfileStats;
                }
            }
        }

        /// <summary>The wordmark is drawn at double scale; the subtitle sits under it at single scale.</summary>
        public const int TitleScale = 2;

        /// <summary>
        /// Builds the header from the strings that will actually be drawn.
        ///
        /// The profile block is laid out first from its own measured width and pinned to the right margin. The
        /// identity block then gets whatever is left, and both are truncated to their reservation — so the two can
        /// never meet however long the name is or however many digits the level and coin count carry.
        /// </summary>
        public static HeaderLayout BuildHeader(string title, string subtitle, string displayName, int level, int coins)
        {
            const int pad = UiTheme.ScreenMargin;
            const int gutter = UiTheme.PadLarge;

            var stats = $"LV {Math.Max(0, level)}   {Math.Max(0, coins)} C";
            var name = TextFit.Clamp(displayName ?? string.Empty, TextFit.MaxDisplayNameChars);

            // The profile block reserves the wider of its two lines, and never more than half the header.
            var wanted = Math.Max(UiText.Width(name), UiText.Width(stats));
            var profileWidth = Math.Min(wanted, Width / 2 - pad - gutter);
            var profileX = Width - pad - profileWidth;

            // Whatever the profile did not take belongs to the identity block, minus the gutter between them.
            var identityWidth = Math.Max(0, profileX - gutter - pad);

            var titleText = UiText.Fit(title, identityWidth, TitleScale);
            var subtitleText = UiText.Fit(subtitle, identityWidth);

            var titleHeight = UiText.Height(1, TitleScale);
            var layout = new HeaderLayout
            {
                IdentityBlock = new UiRect(pad, 0, identityWidth, UiTheme.HeaderHeight),
                ProfileBlock = new UiRect(profileX, 0, profileWidth, UiTheme.HeaderHeight),
                Title = new MeasuredText(titleText, new UiRect(pad, 3, identityWidth, titleHeight), TitleScale),
                Subtitle = new MeasuredText(subtitleText, new UiRect(pad, 3 + titleHeight + 2, identityWidth, UiText.Height()), 1),
                ProfileName = new MeasuredText(
                    UiText.Fit(name, profileWidth),
                    new UiRect(profileX, 4, profileWidth, UiText.Height()), 1),
                ProfileStats = new MeasuredText(
                    UiText.Fit(stats, profileWidth),
                    new UiRect(profileX, 4 + UiText.LineHeight + 2, profileWidth, UiText.Height()), 1)
            };

            return layout;
        }

        // ---------------- primary navigation ----------------

        /// <summary>One tab of the primary navigation bar, with the width its own label needs.</summary>
        public readonly struct TabSlot
        {
            public TabSlot(string id, string label, UiRect bounds, bool isExit)
            {
                Id = id; Label = label; Bounds = bounds; IsExit = isExit;
            }

            public string Id { get; }
            public string Label { get; }
            public UiRect Bounds { get; }
            /// <summary>LEAVE is an action, not a persistent content section, and is placed and styled apart.</summary>
            public bool IsExit { get; }
        }

        public const int TabHeight = 16;
        public const int TabGap = 2;
        /// <summary>
        /// Horizontal room a tab reserves beyond its label.
        ///
        /// It has to be at least the inner inset a control puts around its own text, or the tab is sized for a label
        /// the control then truncates — which is how every tab in the bar ended up reading "STORA…", "MULTIPLAY…".
        /// </summary>
        private const int TabPadding = 14;
        private const int MinTabWidth = 40;

        /// <summary>
        /// Lays the content tabs out left to right at the width each label actually needs, and pins the exit action
        /// to the right margin.
        ///
        /// Tabs are deliberately not equal-width: forcing MULTIPLAYER and TRADER into one width either crams the long
        /// label or wastes the short one, and the polish pass asks for readable labels over a tidy grid.
        /// </summary>
        public static IReadOnlyList<TabSlot> BuildTabs(IReadOnlyList<(string Id, string Label)> contentTabs, (string Id, string Label)? exit = null)
        {
            var slots = new List<TabSlot>();
            var y = UiTheme.HeaderHeight + (UiTheme.TabBarHeight - TabHeight) / 2;
            var x = UiTheme.ScreenMargin;

            var exitWidth = exit.HasValue ? Math.Max(MinTabWidth, UiText.Width(exit.Value.Label) + TabPadding) : 0;
            var limit = Width - UiTheme.ScreenMargin - (exit.HasValue ? exitWidth + UiTheme.PadLarge : 0);

            foreach (var (id, label) in contentTabs ?? Array.Empty<(string, string)>())
            {
                var width = Math.Max(MinTabWidth, UiText.Width(label) + TabPadding);
                if (x + width > limit) width = Math.Max(MinTabWidth, limit - x);
                if (width < MinTabWidth) break;
                slots.Add(new TabSlot(id, UiText.Fit(label, width - TabPadding), new UiRect(x, y, width, TabHeight), false));
                x += width + TabGap;
            }

            if (exit.HasValue)
                slots.Add(new TabSlot(exit.Value.Id, exit.Value.Label,
                    new UiRect(Width - UiTheme.ScreenMargin - exitWidth, y, exitWidth, TabHeight), true));

            return slots;
        }

        /// <summary>Stacks rows down a column at a fixed pitch, which is how every list panel is built.</summary>
        public static IReadOnlyList<UiRect> Rows(UiRect column, int rowHeight, int gap, int count, int topOffset = 0)
        {
            var rows = new List<UiRect>();
            var y = column.Y + topOffset;
            for (var i = 0; i < count; i++)
            {
                if (y + rowHeight > column.Bottom) break;
                rows.Add(new UiRect(column.X, y, column.Width, rowHeight));
                y += rowHeight + gap;
            }

            return rows;
        }

        /// <summary>How many rows of a given pitch a column can show without running past its own bottom edge.</summary>
        public static int RowCapacity(UiRect column, int rowHeight, int gap, int topOffset = 0)
        {
            var usable = column.Height - topOffset;
            if (usable < rowHeight) return 0;
            return 1 + (usable - rowHeight) / Math.Max(1, rowHeight + gap);
        }

        /// <summary>Reports every pair of boxes that overlap. An empty result is the condition the screens must hold.</summary>
        public static IReadOnlyList<string> Overlaps(IEnumerable<(string Name, UiRect Bounds)> boxes)
        {
            var list = boxes.Where(b => !b.Bounds.IsEmpty).ToList();
            var problems = new List<string>();
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
                if (list[i].Bounds.Overlaps(list[j].Bounds))
                    problems.Add($"{list[i].Name} {list[i].Bounds} overlaps {list[j].Name} {list[j].Bounds}");
            return problems;
        }
    }
}
