using System;
using System.Collections.Generic;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.UI.Inventory
{
    /// <summary>One rendered row of a details panel: either a wrapped text row or a "key ..... value" stat row.</summary>
    public readonly struct DetailRow
    {
        public DetailRow(string key, string value, Color color)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
            Color = color;
        }

        public string Key { get; }
        public string Value { get; }
        public Color Color { get; }
        public bool IsStat => !string.IsNullOrEmpty(Value);
    }

    /// <summary>
    /// The one details layout every item window shares (Inventory, Merchant, Weapon Cache — ui/92/93): the item's
    /// description first, wrapped to the panel, then its Legendary line, then the stat rows, the affixes and, last,
    /// the comparison against the equipped item. The panels used to cut the list at their row count, which hid the
    /// tail; here the rows page instead, so every line stays reachable from mouse wheel, keyboard and controller
    /// (see <see cref="DetailPager"/>), and the description is never the part that gets lost.
    /// </summary>
    public static class ItemDetailLayout
    {
        public const string MoreHintPrefix = "MORE";

        /// <summary>Every row of a tooltip in priority order, wrapped to <paramref name="widthPixels"/>.</summary>
        public static List<DetailRow> Compose(ItemTooltip tooltip, IReadOnlyList<ComparisonLine> comparison, int widthPixels)
        {
            var rows = new List<DetailRow>();
            if (tooltip == null) return rows;
            foreach (var line in UiText.Wrap(tooltip.Description, widthPixels)) rows.Add(new DetailRow(line, string.Empty, UiTheme.Ink));
            if (!string.IsNullOrEmpty(tooltip.LegendaryText))
                foreach (var line in UiText.Wrap(tooltip.LegendaryText, widthPixels)) rows.Add(new DetailRow(line, string.Empty, UiTheme.Amber));
            foreach (var stat in tooltip.BaseStats) rows.Add(new DetailRow(stat.Label, stat.Value, UiTheme.Ink));
            foreach (var affix in tooltip.Affixes) rows.Add(new DetailRow(affix.Label, affix.Value, UiTheme.Terminal));
            if (comparison != null && comparison.Count > 0)
            {
                rows.Add(new DetailRow("— VS EQUIPPED —", string.Empty, UiTheme.InkMuted));
                // Up/down as text as well as colour (ui/90): the pixel face has no arrow glyphs, so (+) / (-) / (=).
                foreach (var line in comparison)
                    rows.Add(new DetailRow(line.Label, $"{line.Candidate} vs {line.Current} ({(line.Delta > 0 ? "+" : line.Delta < 0 ? "-" : "=")})", line.Delta > 0 ? UiTheme.Terminal : line.Delta < 0 ? UiTheme.Danger : UiTheme.InkMuted));
            }

            return rows;
        }

        /// <summary>"Label ........ value" on one line: the key left, the value right-aligned in the same box.</summary>
        public static string StatLine(string key, string value, int widthPixels)
        {
            var chars = UiText.CharsFor(widthPixels);
            var k = UiText.Fit(key, widthPixels - UiText.Width(value) - UiText.Advance);
            var pad = Math.Max(1, chars - k.Length - value.Length);
            return k + new string(' ', pad) + value;
        }

        /// <summary>The text a row renders as, fitted to the width.</summary>
        public static string Render(DetailRow row, int widthPixels) => row.IsStat ? StatLine(row.Key, row.Value, widthPixels) : UiText.Fit(row.Key, widthPixels);
    }

    /// <summary>
    /// Pages a row list through a fixed number of visible rows. When the rows overflow, the last visible row becomes
    /// the MORE hint with the page position, and the caller steps pages from the mouse wheel, PageUp/PageDown or the
    /// controller's right stick — the same step for every device, no pointer-only scrollbar.
    /// </summary>
    public sealed class DetailPager
    {
        private readonly List<DetailRow> _rows = new();

        public DetailPager(int capacity)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Capacity { get; }
        public int Offset { get; private set; }
        public int Count => _rows.Count;
        public bool Overflows => _rows.Count > Capacity;
        /// <summary>Rows shown per page when the list overflows: one row is the hint.</summary>
        public int PageRows => Overflows ? Capacity - 1 : Capacity;
        public int PageCount => Overflows ? (int)Math.Ceiling(_rows.Count / (double)PageRows) : 1;
        public int Page => Overflows ? Math.Min(PageCount - 1, Offset / PageRows) : 0;
        public bool CanPageDown => Overflows && Offset + PageRows < _rows.Count;
        public bool CanPageUp => Overflows && Offset > 0;
        public IReadOnlyList<DetailRow> Rows => _rows;

        /// <summary>Replaces the rows; a changed item resets to the first page, the same item keeps its page (clamped).</summary>
        public void SetRows(IReadOnlyList<DetailRow> rows, bool keepPage)
        {
            _rows.Clear();
            if (rows != null) _rows.AddRange(rows);
            if (!keepPage) Offset = 0;
            Clamp();
        }

        public bool PageDown()
        {
            if (!CanPageDown) return false;
            Offset += PageRows;
            Clamp();
            return true;
        }

        public bool PageUp()
        {
            if (!CanPageUp) return false;
            Offset -= PageRows;
            Clamp();
            return true;
        }

        public void Reset() => Offset = 0;

        /// <summary>The rows to draw right now, with the trailing hint row when the list continues (or scrolled) — exactly Capacity entries or fewer.</summary>
        public List<DetailRow> Visible(string hint = null)
        {
            var visible = new List<DetailRow>();
            if (!Overflows)
            {
                visible.AddRange(_rows);
                return visible;
            }

            for (var i = Offset; i < _rows.Count && visible.Count < PageRows; i++) visible.Add(_rows[i]);
            var text = $"{ItemDetailLayout.MoreHintPrefix} ({Page + 1}/{PageCount}) {hint ?? string.Empty}".TrimEnd();
            visible.Add(new DetailRow(text, string.Empty, UiTheme.Amber));
            return visible;
        }

        private void Clamp()
        {
            var max = Overflows ? Math.Max(0, (PageCount - 1) * PageRows) : 0;
            Offset = Math.Clamp(Offset, 0, max);
            if (Overflows && PageRows > 0) Offset -= Offset % PageRows;
        }
    }

    /// <summary>
    /// Reads the paging inputs for a details panel: mouse wheel, PageDown / PageUp, and the controller's right stick
    /// (edge-triggered so one flick is one page). Device-agnostic on purpose: the same rows are reachable from all three.
    /// </summary>
    public static class DetailPagingInput
    {
        private static bool _stickHeld;

        /// <summary>+1 = page down, -1 = page up, 0 = nothing this frame.</summary>
        public static int Poll()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (kb != null && kb.pageDownKey.wasPressedThisFrame) return +1;
            if (kb != null && kb.pageUpKey.wasPressedThisFrame) return -1;
            if (mouse != null)
            {
                var wheel = mouse.scroll.ReadValue().y;
                if (wheel < -0.01f) return +1;
                if (wheel > 0.01f) return -1;
            }

            if (pad != null)
            {
                var y = pad.rightStick.ReadValue().y;
                var pushed = Mathf.Abs(y) > 0.6f;
                if (pushed && !_stickHeld) { _stickHeld = true; return y < 0f ? +1 : -1; }
                if (!pushed) _stickHeld = false;
            }

            return 0;
        }

        /// <summary>The hint text for the current device.</summary>
        public static string Hint() => RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad ? "R-STICK" : "WHEEL/PGDN";
    }
}
