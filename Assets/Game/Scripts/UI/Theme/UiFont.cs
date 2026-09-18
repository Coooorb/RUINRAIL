using UnityEngine;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// The UI typeface: the project's own pixel font, loaded once from Resources.
    ///
    /// It replaces Unity's builtin LegacyRuntime.ttf, which FINAL_ART_PRODUCTION_SPEC 29.12 requires gone from the
    /// release path — a scaled TTF also blurs at 640x360, where a bitmap face renders exact pixels. The builtin font
    /// remains only as a last-resort fallback so a missing asset degrades to readable text rather than to nothing at
    /// all; the art validator fails the build if that fallback is what ships. Lives in the UI assembly so every screen
    /// (front-end and in-run HUD alike) draws with the same face and the same metrics.
    /// </summary>
    public static class UiFont
    {
        /// <summary>Where the generated RUINRAIL pixel font is loaded from at runtime.</summary>
        public const string PixelFontResource = "Fonts/ruinrail_pixel";

        private static Font _font;

        public static Font Font()
        {
            if (_font != null) return _font;
            _font = Resources.Load<Font>(PixelFontResource);
            if (_font == null)
            {
                Debug.LogError($"RUINRAIL pixel font missing at Resources/{PixelFontResource}; falling back to the builtin font.");
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return _font;
        }

        /// <summary>True when the real pixel font is what the UI is drawing with.</summary>
        public static bool UsingPixelFont => Font() != null && Font().name != "LegacyRuntime";

        /// <summary>
        /// How far a label has to be nudged down for its first line to land inside the box the layout gave it.
        ///
        /// Unity puts the first baseline at (rect top − the face's ascent). A <see cref="UnityEngine.Font"/> assembled
        /// in script reports an ascent of zero, so the baseline sits on the rect's top edge and the entire line is
        /// drawn *above* the rectangle. Deriving the nudge from the face's own ascent means a font that later carries
        /// a real one needs no change here.
        /// </summary>
        public static int TopOffset => Mathf.Max(0, UiText.GlyphHeight - Mathf.RoundToInt(Font().ascent));
    }
}
