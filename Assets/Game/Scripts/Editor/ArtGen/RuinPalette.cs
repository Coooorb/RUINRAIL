using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// FINAL_ART_PRODUCTION_SPEC section 3 — the approved global palette, as code.
    ///
    /// Every generated sprite draws from these families so the whole game shares one colour world. A material is a
    /// 3-4 value ramp (shadow / base / light / optional highlight) per spec section 2.5; smooth gradients are
    /// forbidden, so a ramp is a short explicit array, never an interpolation.
    /// </summary>
    public static class RuinPalette
    {
        public static Color32 Hex(string hex)
        {
            var h = hex.TrimStart('#');
            return new Color32(
                (byte)System.Convert.ToInt32(h.Substring(0, 2), 16),
                (byte)System.Convert.ToInt32(h.Substring(2, 2), 16),
                (byte)System.Convert.ToInt32(h.Substring(4, 2), 16),
                255);
        }

        // --- 3.1 neutral structure ---
        public static readonly Color32 NearBlack = Hex("#111518");
        public static readonly Color32 Charcoal = Hex("#1B2023");
        public static readonly Color32 DarkSteel = Hex("#2B3235");
        public static readonly Color32 MidSteel = Hex("#4B5556");
        public static readonly Color32 PaleSteel = Hex("#788181");
        public static readonly Color32 ConcreteShadow = Hex("#343638");
        public static readonly Color32 Concrete = Hex("#555759");
        public static readonly Color32 ConcreteLight = Hex("#777873");
        public static readonly Color32 DustBeige = Hex("#9A8D73");

        // --- 3.2 rust / industrial warmth ---
        public static readonly Color32 BurntRustDark = Hex("#4B261C");
        public static readonly Color32 Rust = Hex("#8E4327");
        public static readonly Color32 OxideOrange = Hex("#C06434");
        public static readonly Color32 WarningOchre = Hex("#D49A3A");
        public static readonly Color32 DirtyYellow = Hex("#C2A34B");

        // --- 3.3 survival greens ---
        public static readonly Color32 DeepOlive = Hex("#26352A");
        public static readonly Color32 Olive = Hex("#425640");
        public static readonly Color32 CanvasGreen = Hex("#66715A");
        public static readonly Color32 SickGreen = Hex("#798D54");
        public static readonly Color32 PaleToxic = Hex("#A4C75A");

        // --- 3.4 technology accents ---
        public static readonly Color32 TerminalGreen = Hex("#6FBF8C");
        public static readonly Color32 ElectricCyan = Hex("#52A9A4");
        public static readonly Color32 ColdBlue = Hex("#568FA2");
        public static readonly Color32 AmberActive = Hex("#E7A74A");
        public static readonly Color32 EmergencyRed = Hex("#C84B42");

        // --- 2.3 approved structural darks for the selective outline ---
        public static readonly Color32 OutlineCharcoal = Hex("#111518");
        public static readonly Color32 OutlineWarm = Hex("#161A1C");
        public static readonly Color32 OutlineGreenBlack = Hex("#10181A");
        public static readonly Color32 OutlineRustBlack = Hex("#1B1715");

        /// <summary>A material ramp: shadow, base, light and an optional highlight (spec 2.5, max 4 values).</summary>
        public readonly struct Ramp
        {
            public readonly Color32 Shadow;
            public readonly Color32 Base;
            public readonly Color32 Light;
            public readonly Color32 Highlight;
            public readonly bool HasHighlight;

            public Ramp(Color32 shadow, Color32 baseColor, Color32 light)
            {
                Shadow = shadow; Base = baseColor; Light = light; Highlight = light; HasHighlight = false;
            }

            public Ramp(Color32 shadow, Color32 baseColor, Color32 light, Color32 highlight)
            {
                Shadow = shadow; Base = baseColor; Light = light; Highlight = highlight; HasHighlight = true;
            }

            /// <summary>0 = shadow, 1 = base, 2 = light, 3 = highlight (falls back to light when absent).</summary>
            public Color32 At(int step) => step switch
            {
                <= 0 => Shadow,
                1 => Base,
                2 => Light,
                _ => HasHighlight ? Highlight : Light
            };
        }

        /// <summary>Darkens toward the structural near-black without desaturating into grey mush.</summary>
        public static Color32 Darken(Color32 c, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.r, NearBlack.r, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.g, NearBlack.g, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.b, NearBlack.b, t)),
                c.a);
        }

        /// <summary>Lightens while pulling very slightly warm, which reads better than lerping to pure white.</summary>
        public static Color32 Lighten(Color32 c, float t)
        {
            t = Mathf.Clamp01(t);
            var target = new Color32(250, 246, 238, 255);
            return new Color32(
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.r, target.r, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.g, target.g, t)),
                (byte)Mathf.RoundToInt(Mathf.Lerp(c.b, target.b, t)),
                c.a);
        }

        /// <summary>Builds a 4-value ramp around a base colour using the approved shadow/light relationship.</summary>
        public static Ramp RampOf(Color32 baseColor, float shadow = 0.42f, float light = 0.26f, float highlight = 0.5f) =>
            new(Darken(baseColor, shadow), baseColor, Lighten(baseColor, light), Lighten(baseColor, highlight));

        /// <summary>Named ramps used across families so materials stay consistent between sprites.</summary>
        public static readonly Ramp SteelRamp = RampOf(MidSteel);
        public static readonly Ramp DarkSteelRamp = RampOf(DarkSteel);
        public static readonly Ramp RustRamp = RampOf(Rust);
        public static readonly Ramp OliveRamp = RampOf(Olive);
        public static readonly Ramp CanvasRamp = RampOf(CanvasGreen);
        public static readonly Ramp BeigeRamp = RampOf(DustBeige);
        public static readonly Ramp ConcreteRamp = RampOf(Concrete);
        public static readonly Ramp FleshRamp = RampOf(Hex("#8A6A55"));
        public static readonly Ramp SickFleshRamp = RampOf(Hex("#6E7A5C"));
        public static readonly Ramp LabWhiteRamp = RampOf(Hex("#B9BDB4"));

        /// <summary>Rarity accents (spec 3.5). Rarity lives in frames and glows, never painted over a whole sprite.</summary>
        public static readonly Dictionary<string, Color32> Rarity = new()
        {
            { "Common", PaleSteel },
            { "Uncommon", SickGreen },
            { "Rare", ColdBlue },
            { "Epic", Hex("#8C6BB1") },
            { "Legendary", WarningOchre }
        };
    }
}
