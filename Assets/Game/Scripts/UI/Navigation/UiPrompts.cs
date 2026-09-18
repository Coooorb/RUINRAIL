using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Onboarding;
using UnityEngine;

namespace RuinRail.UI.Navigation
{
    /// <summary>Menu actions that need a consistent glyph across every screen (ui/90).</summary>
    public enum UiAction
    {
        Confirm,
        Cancel,
        Navigate,
        Interact,
        Rebind,
        Tab,
        Pause
    }

    /// <summary>
    /// Consistent, device-aware prompt text ("Enter: Confirm" / "A: Confirm"): menu glyphs follow the active device;
    /// gameplay-action glyphs come from the live bindings through <see cref="SchemeGlyphs"/>. Switching device only
    /// changes text — bindings are never touched.
    /// </summary>
    public sealed class UiPrompts
    {
        private readonly SchemeGlyphs _glyphs;

        public UiPrompts(SchemeGlyphs glyphs = null)
        {
            _glyphs = glyphs ?? new SchemeGlyphs(InputScheme.KeyboardMouse);
        }

        public InputDeviceKind Device => ActiveInputDevice.Current;

        public static string MenuGlyph(UiAction action, InputDeviceKind device) => (action, device) switch
        {
            (UiAction.Confirm, InputDeviceKind.Gamepad) => "A",
            (UiAction.Confirm, _) => "Enter",
            (UiAction.Cancel, InputDeviceKind.Gamepad) => "B",
            (UiAction.Cancel, _) => "Esc",
            (UiAction.Navigate, InputDeviceKind.Gamepad) => "D-Pad",
            (UiAction.Navigate, _) => "Arrows",
            (UiAction.Tab, InputDeviceKind.Gamepad) => "LB/RB",
            (UiAction.Tab, _) => "Q/E",
            (UiAction.Rebind, InputDeviceKind.Gamepad) => "X",
            (UiAction.Rebind, _) => "Enter",
            (UiAction.Pause, InputDeviceKind.Gamepad) => "Menu",
            (UiAction.Pause, _) => "Esc",
            _ => string.Empty
        };

        public static string Verb(UiAction action) => action switch
        {
            UiAction.Confirm => "Confirm",
            UiAction.Cancel => "Back",
            UiAction.Navigate => "Navigate",
            UiAction.Interact => "Interact",
            UiAction.Rebind => "Rebind",
            UiAction.Tab => "Switch tab",
            UiAction.Pause => "Pause",
            _ => action.ToString()
        };

        /// <summary>"Enter: Confirm" — the glyph then the English verb, everywhere.</summary>
        public string For(UiAction action)
        {
            if (action == UiAction.Interact)
            {
                _glyphs.SetScheme(Device == InputDeviceKind.Gamepad ? InputScheme.Gamepad : InputScheme.KeyboardMouse);
                return $"{_glyphs.For("Interact")}: {Verb(action)}";
            }

            return $"{MenuGlyph(action, Device)}: {Verb(action)}";
        }

        /// <summary>
        /// Both devices for one action: "Enter / A: Confirm".
        ///
        /// Keyboard first, always, however the player is holding the game right now. A hint line that reorders itself
        /// under the player is harder to read than one that stays put, and the point of the combined form is that it
        /// never tells a pad player the menu is arrows-only or a desk player that it is a controller game.
        /// </summary>
        public static string CombinedGlyph(UiAction action)
        {
            var keyboard = MenuGlyph(action, InputDeviceKind.KeyboardMouse);
            var gamepad = MenuGlyph(action, InputDeviceKind.Gamepad);
            return keyboard == gamepad ? keyboard : $"{keyboard} / {gamepad}";
        }

        /// <summary>"Enter / A: Confirm" — both devices, one verb.</summary>
        public static string Combined(UiAction action) => $"{CombinedGlyph(action)}: {Verb(action)}";

        /// <summary>
        /// The standard footer of every menu, naming every way in: confirm, back, pointer and step navigation.
        ///
        /// The mouse is listed because it genuinely works now — before this pass the menus carried no event system at
        /// all, so a hint promising mouse support would have been a lie.
        /// </summary>
        public string Footer() =>
            $"{Combined(UiAction.Confirm)}   {Combined(UiAction.Cancel)}   Mouse: Select   {Combined(UiAction.Navigate)}";
    }

    /// <summary>
    /// ui/90: rarity is never colour alone. Every rarity has its text label, a distinct border pattern and a marker
    /// glyph, and the colours are luminance-ordered so a grayscale or colour-impaired view still separates them.
    /// </summary>
    public readonly struct RarityStyle
    {
        public RarityStyle(Rarity rarity, string label, Color color, string border, string marker)
        {
            Rarity = rarity;
            Label = label;
            Color = color;
            Border = border;
            Marker = marker;
        }

        public Rarity Rarity { get; }
        /// <summary>Always shown next to the name (e.g. "RARE").</summary>
        public string Label { get; }
        public Color Color { get; }
        /// <summary>Border treatment name for the slot frame (none / single / double / dashed / starred).</summary>
        public string Border { get; }
        /// <summary>Text marker that precedes the name (distinct per rarity, readable in pixel fonts).</summary>
        public string Marker { get; }
        public float Luminance => 0.2126f * Color.r + 0.7152f * Color.g + 0.0722f * Color.b;

        /// <summary>"◆◆ RARE Name" — the name line every list, tooltip and drop label uses.</summary>
        public string Decorate(string name) => string.IsNullOrEmpty(Marker) ? $"{Label} {name}" : $"{Marker} {Label} {name}";

        public static RarityStyle For(Rarity rarity) => rarity switch
        {
            Rarity.Common => new RarityStyle(rarity, "COMMON", new Color(0.40f, 0.40f, 0.40f), "none", ""),
            Rarity.Uncommon => new RarityStyle(rarity, "UNCOMMON", new Color(0.30f, 0.65f, 0.30f), "single", "+"),
            Rarity.Rare => new RarityStyle(rarity, "RARE", new Color(0.45f, 0.72f, 1.00f), "double", "++"),
            Rarity.Epic => new RarityStyle(rarity, "EPIC", new Color(0.55f, 0.22f, 0.75f), "dashed", "+++"),
            _ => new RarityStyle(rarity, "LEGENDARY", new Color(1.00f, 0.82f, 0.25f), "starred", "*")
        };
    }

    /// <summary>Text budgets at the 640×360 reference (ui/90: compact, legible): ellipsis instead of clipping.</summary>
    public static class TextFit
    {
        /// <summary>V1 FINAL (TASK 179) glyph advance of the placeholder pixel font at reference scale.</summary>
        public const int PixelsPerChar = 6;
        public const int MaxDisplayNameChars = 16;
        /// <summary>HUD party line: 200 px panel → 33 chars; a 16-char name + ": DOWNED 20s" (12) fits.</summary>
        public const int PartyLineChars = 33;
        /// <summary>Inventory / tooltip name line: 260 px → 43 chars including the rarity marker + label.</summary>
        public const int ItemNameLineChars = 43;
        /// <summary>Base station list row: 300 px → 50 chars.</summary>
        public const int StationRowChars = 50;

        public static int CharsFor(int widthPixels) => Math.Max(0, widthPixels / PixelsPerChar);

        public static string Clamp(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0) return string.Empty;
            if (text.Length <= maxChars) return text;
            return maxChars <= 1 ? text.Substring(0, maxChars) : text.Substring(0, maxChars - 1) + "…";
        }

        public static bool Fits(string text, int maxChars) => (text?.Length ?? 0) <= maxChars;
    }
}
