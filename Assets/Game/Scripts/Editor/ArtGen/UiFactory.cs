using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// UI frames, bars, slots and input glyphs from FINAL_ART_PRODUCTION_SPEC section 18.
    ///
    /// The UI thesis is post-apocalyptic industrial terminal: dark metal, thin warm accents, squared or chamfered
    /// corners, 1-2 px borders on a 4 px spacing grid. Panels and bars are authored as 9-sliceable sprites with
    /// integer borders so they scale without breaking pixel alignment (18.3, 21).
    ///
    /// Focus states are drawn with a shape change (corner brackets) as well as a colour change, because 18.4 requires
    /// the focus to be visible without relying on colour alone.
    /// </summary>
    public static class UiFactory
    {
        public static readonly Color32 PanelFill = RuinPalette.Hex("#1E2426");
        public static readonly Color32 PanelEdge = RuinPalette.Hex("#3A4446");
        public static readonly Color32 PanelHighlight = RuinPalette.Hex("#55605F");
        public static readonly Color32 Accent = RuinPalette.Hex("#C08A3E");

        /// <summary>Panel frame, 9-sliced with a 4 px border.</summary>
        public static PixelCanvas Panel(int size = 24, bool emphasized = false)
        {
            var c = new PixelCanvas(size, size);
            c.Rect(0, 0, size, size, PanelFill);
            c.RectOutline(0, 0, size, size, PanelEdge);
            if (emphasized) c.RectOutline(1, 1, size - 2, size - 2, PanelHighlight);
            // Chamfered corners: squared-off industrial, never a rounded mobile card.
            foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) }) c.Erase(x, y);
            // Inner top light edge for a metal read.
            c.Line(2, size - 2, size - 3, size - 2, RuinPalette.Darken(PanelHighlight, 0.25f));
            return c;
        }

        /// <summary>Button in one of its four states (18.4).</summary>
        public enum ButtonState { Normal, Focus, Pressed, Disabled }

        public static PixelCanvas Button(int width = 24, int height = 16, ButtonState state = ButtonState.Normal)
        {
            var c = new PixelCanvas(width, height);
            var fill = state switch
            {
                ButtonState.Pressed => RuinPalette.Darken(PanelFill, 0.3f),
                ButtonState.Disabled => RuinPalette.Hex("#22262733"),
                _ => RuinPalette.Hex("#262D2F")
            };
            var edge = state switch
            {
                ButtonState.Focus => Accent,
                ButtonState.Disabled => RuinPalette.Hex("#333A3B"),
                _ => PanelEdge
            };

            c.Rect(0, 0, width, height, fill);
            c.RectOutline(0, 0, width, height, edge);
            foreach (var (x, y) in new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1) }) c.Erase(x, y);

            if (state == ButtonState.Focus)
            {
                // Corner brackets: the shape cue that makes focus readable without colour (18.4).
                foreach (var (bx, by, dx, dy) in new[] { (1, 1, 1, 1), (width - 2, 1, -1, 1), (1, height - 2, 1, -1), (width - 2, height - 2, -1, -1) })
                {
                    c.Set(bx, by, Accent);
                    c.Set(bx + dx, by, Accent);
                    c.Set(bx, by + dy, Accent);
                }
            }
            else if (state == ButtonState.Normal)
            {
                c.Line(2, height - 2, width - 3, height - 2, RuinPalette.Darken(PanelHighlight, 0.3f));
            }

            return c;
        }

        /// <summary>Inventory slot with an optional rarity frame (18.5).</summary>
        public static PixelCanvas InventorySlot(int size = 20, Color32? rarity = null, bool selected = false)
        {
            var c = new PixelCanvas(size, size);
            c.Rect(0, 0, size, size, RuinPalette.Hex("#171C1E"));
            c.RectOutline(0, 0, size, size, rarity ?? PanelEdge);
            if (rarity.HasValue) c.RectOutline(1, 1, size - 2, size - 2, RuinPalette.Darken(rarity.Value, 0.5f));
            foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) }) c.Erase(x, y);
            if (selected)
                foreach (var (bx, by, dx, dy) in new[] { (1, 1, 1, 1), (size - 2, 1, -1, 1), (1, size - 2, 1, -1), (size - 2, size - 2, -1, -1) })
                {
                    c.Set(bx + dx, by, Accent);
                    c.Set(bx, by + dy, Accent);
                }
            return c;
        }

        /// <summary>
        /// The HUD dash icon (16×16, spec 18 / 91 Player State): two amber chevrons driving right with a fading
        /// motion trail behind them. Drawn once at screen pixels; the HUD tints and overlays it for the cooldown and
        /// disabled states rather than shipping one sprite per state.
        /// </summary>
        public static PixelCanvas DashIcon(int size = 16)
        {
            var c = new PixelCanvas(size, size);
            var amber = RuinPalette.AmberActive;
            var bright = RuinPalette.Lighten(amber, 0.35f);
            var dark = RuinPalette.Darken(amber, 0.45f);
            var trail = RuinPalette.Hex("#8A6A34");
            // Two chevrons driving right (apexes at x = 9 and x = 13), two pixels thick, four rows each side of the centre line.
            for (var i = 0; i < 4; i++)
            {
                c.Set(8 - i, 7 - i, amber); c.Set(8 - i, 8 + i, amber);
                c.Set(9 - i, 7 - i, i == 0 ? bright : amber); c.Set(9 - i, 8 + i, amber);
                c.Set(12 - i, 7 - i, amber); c.Set(12 - i, 8 + i, amber);
                c.Set(13 - i, 7 - i, i == 0 ? bright : amber); c.Set(13 - i, 8 + i, amber);
            }

            // Speed lines trailing off to the left: shorter and darker as they leave the body.
            c.Line(1, 7, 4, 7, trail); c.Line(1, 8, 4, 8, trail);
            c.Line(2, 4, 4, 4, dark); c.Line(2, 11, 4, 11, dark);
            c.Line(3, 2, 4, 2, dark); c.Line(3, 13, 4, 13, dark);
            return c;
        }

        /// <summary>
        /// The HUD coin/token icon (12×12, spec 18 / 91 Top Information): a struck brass token — dark rim, lit
        /// upper-left edge, a stamped bar mark in the middle. Authored once at screen pixels; the coin readout is the
        /// icon plus the number, never the word "COINS".
        /// </summary>
        public static PixelCanvas CoinIcon(int size = 12)
        {
            var c = new PixelCanvas(size, size);
            var gold = RuinPalette.AmberActive;
            var rim = RuinPalette.Darken(gold, 0.5f);
            var lit = RuinPalette.Lighten(gold, 0.45f);
            var stamp = RuinPalette.Darken(gold, 0.62f);
            var centre = (size - 1) / 2f;
            var radius = size / 2f - 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - centre;
                var dy = y - centre;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > radius) continue;
                if (d > radius - 1f) c.Set(x, y, rim);
                else c.Set(x, y, gold);
            }

            // Lit upper-left arc so the token reads as metal rather than as a flat disc.
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - centre;
                var dy = y - centre;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d <= radius - 1f && d > radius - 2.2f && dx <= 0 && dy >= 0) c.Set(x, y, lit);
            }

            // Stamped mark: a short vertical bar with two serifs, the token's own device.
            var mid = size / 2;
            c.Rect(mid - 1, mid - 2, 2, 5, stamp);
            c.Rect(mid - 2, mid + 2, 4, 1, stamp);
            c.Rect(mid - 2, mid - 3, 4, 1, stamp);
            return c;
        }

        /// <summary>
        /// The low-HP danger vignette (ui/91 Player State): a soft red frame that darkens toward the screen edge and
        /// leaves the play area clear. Authored at a quarter of the 640×360 reference and stretched, because it is a
        /// smooth falloff rather than pixel art; the HUD fades and pulses it by tinting the image.
        /// </summary>
        public static PixelCanvas LowHealthVignette(int width = 160, int height = 90)
        {
            var c = new PixelCanvas(width, height);
            var red = RuinPalette.EmergencyRed;
            var cx = (width - 1) / 2f;
            var cy = (height - 1) / 2f;
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Elliptical distance: 0 at the centre, 1 at the frame edge.
                var nx = (x - cx) / cx;
                var ny = (y - cy) / cy;
                var d = Mathf.Sqrt(nx * nx + ny * ny) / Mathf.Sqrt(2f);
                // Clear inside 0.42, then a smooth ramp to full at the edge; the centre never tints.
                var t = Mathf.InverseLerp(0.42f, 1f, d);
                var alpha = Mathf.RoundToInt(255f * t * t);
                if (alpha <= 0) continue;
                c.SetTranslucent(x, y, new Color32(red.r, red.g, red.b, (byte)Mathf.Clamp(alpha, 0, 255)));
            }

            return c;
        }

        /// <summary>A horizontal bar: HP, XP, heat or charge. 9-sliced horizontally.</summary>
        public static PixelCanvas Bar(int width, int height, Color32 fillColor, float fraction = 1f, bool frame = true)
        {
            var c = new PixelCanvas(width, height);
            c.Rect(0, 0, width, height, RuinPalette.Hex("#141819"));
            var inner = Mathf.RoundToInt((width - 2) * Mathf.Clamp01(fraction));
            if (inner > 0)
            {
                c.Rect(1, 1, inner, height - 2, fillColor);
                // Top light line so the fill reads as a lit surface rather than a flat block.
                c.Line(1, height - 2, inner, height - 2, RuinPalette.Lighten(fillColor, 0.3f));
                c.Line(1, 1, inner, 1, RuinPalette.Darken(fillColor, 0.35f));
            }
            if (frame) c.RectOutline(0, 0, width, height, PanelEdge);
            return c;
        }

        /// <summary>Keyboard/mouse and controller glyphs for every action in spec 18.8.</summary>
        public static readonly IReadOnlyList<string> GlyphActions = new[]
        {
            "Move", "Aim", "Fire", "Special", "Dash", "Reload", "Interact",
            "Weapon1", "Weapon2", "WeaponSwap", "Consumable", "Inventory", "Pause"
        };

        public enum Device { Keyboard, Controller }

        /// <summary>The label drawn inside a keyboard glyph, from the project's default bindings.</summary>
        public static string KeyboardLabel(string action) => action switch
        {
            "Move" => "WASD",
            "Aim" => "MS",
            "Fire" => "LMB",
            "Special" => "RMB",
            "Dash" => "SPC",
            "Reload" => "R",
            "Interact" => "E",
            "Weapon1" => "1",
            "Weapon2" => "2",
            "WeaponSwap" => "Q",
            "Consumable" => "F",
            "Inventory" => "TAB",
            "Pause" => "ESC",
            _ => "?"
        };

        public static string ControllerLabel(string action) => action switch
        {
            "Move" => "LS",
            "Aim" => "RS",
            "Fire" => "RT",
            "Special" => "LT",
            "Dash" => "A",
            "Reload" => "X",
            "Interact" => "Y",
            "Weapon1" => "LB",
            "Weapon2" => "RB",
            "WeaponSwap" => "B",
            "Consumable" => "DN",
            "Inventory" => "BK",
            "Pause" => "ST",
            _ => "?"
        };

        /// <summary>
        /// One input glyph: a key cap for keyboard, a rounded pad button for controller. The silhouette differs
        /// between devices so the player can tell which prompt they are reading at the smallest rendered size (18.8).
        /// </summary>
        public static PixelCanvas Glyph(string action, Device device)
        {
            var label = device == Device.Keyboard ? KeyboardLabel(action) : ControllerLabel(action);
            var textWidth = PixelFontFactory.MeasureWidth(label);
            var width = Mathf.Max(14, textWidth + 6);
            const int height = 13;

            var c = new PixelCanvas(width, height);
            var face = RuinPalette.Hex("#2A3133");
            var edge = RuinPalette.Hex("#6A7476");

            if (device == Device.Keyboard)
            {
                // Square key cap with a raised top edge.
                c.Rect(0, 0, width, height, face);
                c.RectOutline(0, 0, width, height, edge);
                c.Line(1, height - 2, width - 2, height - 2, RuinPalette.Lighten(face, 0.25f));
                c.Line(1, 1, width - 2, 1, RuinPalette.Darken(face, 0.4f));
                foreach (var (x, y) in new[] { (0, 0), (width - 1, 0), (0, height - 1), (width - 1, height - 1) }) c.Erase(x, y);
            }
            else
            {
                // Rounded pad button, visibly different in outline from a key cap.
                c.Ellipse(width / 2f, height / 2f, width / 2f, height / 2f, face);
                for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    if (!c.IsOpaque(x, y)) continue;
                    if (!c.IsOpaque(x - 1, y) || !c.IsOpaque(x + 1, y) || !c.IsOpaque(x, y - 1) || !c.IsOpaque(x, y + 1))
                        c.Set(x, y, edge);
                }
            }

            var tx = (width - textWidth) / 2;
            var ty = height - 3;
            foreach (var ch in label)
            {
                PixelFontFactory.DrawGlyph(c, ch, tx, ty, RuinPalette.Hex("#DCE0D8"));
                tx += PixelFontFactory.Advance;
            }

            return c;
        }

        /// <summary>Main-menu backdrop: a wide industrial plate with a rail motif and vignette bands.</summary>
        public static PixelCanvas MenuBackground(int width = 160, int height = 90)
        {
            var c = new PixelCanvas(width, height);
            var rng = new System.Random(7);
            c.Rect(0, 0, width, height, RuinPalette.Hex("#191E20"));
            // Horizon band and distant tunnel arch.
            c.Rect(0, height / 3, width, height / 3, RuinPalette.Hex("#22292B"));
            c.Ellipse(width / 2f, height / 3f, width / 3.6f, height / 2.6f, RuinPalette.Hex("#141A1C"));
            // Rails converging toward the arch.
            for (var i = 0; i < 2; i++)
            {
                var x0 = width / 2 + (i == 0 ? -width / 5 : width / 5);
                c.Line(x0, 0, width / 2 + (i == 0 ? -4 : 4), height / 3, RuinPalette.Hex("#3A4244"), 2);
            }
            for (var y = 2; y < height / 3; y += 5)
            {
                var t = (float)y / (height / 3f);
                var half = Mathf.RoundToInt(Mathf.Lerp(width / 5f, 5f, t));
                c.Line(width / 2 - half, y, width / 2 + half, y, RuinPalette.Hex("#2E3638"));
            }
            // Sparse amber emergency lights along the walls.
            for (var i = 0; i < 6; i++)
            {
                var x = rng.Next(width);
                var y = height / 3 + rng.Next(height / 3);
                c.Set(x, y, RuinPalette.AmberActive);
                c.Set(x, y + 1, RuinPalette.Darken(RuinPalette.AmberActive, 0.45f));
            }
            // Top grime band so the title has something to sit against.
            c.Rect(0, height - 6, width, 6, RuinPalette.Hex("#14191A"));
            return c;
        }
    }
}
