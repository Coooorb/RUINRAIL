using System.Collections;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// What the pixel font actually draws, measured off the rendered frame.
    ///
    /// Every text budget in the UI — every truncation, every reserved width, every "does this label fit" assertion —
    /// is built on <see cref="UiText.Advance"/> being the real advance and <see cref="UiText.GlyphHeight"/> being the
    /// real cap height. Those are constants in the runtime assembly and the glyphs live in a generated font asset, so
    /// nothing but a render connects the two. This suite renders known strings and reads the pixels back, which is the
    /// only way to catch the font asset drifting away from the numbers the layout is computed from.
    ///
    /// The font's face metrics were wrong exactly this way before this pass: it shipped with a line spacing of 0.1 px,
    /// so every multi-line label drew its lines on top of each other.
    /// </summary>
    public sealed class PixelFontRenderingTests
    {
        private GameObject _canvas;

        [TearDown]
        public void TearDown()
        {
            if (_canvas != null) Object.DestroyImmediate(_canvas);
        }

        /// <summary>
        /// Renders strings in white on an opaque black full-screen ground, on a canvas above everything else.
        ///
        /// The ground and the sorting order matter: the capture composites every canvas in the scene, and a probe that
        /// measured stray pixels from some other screen would report nonsense about the font.
        /// </summary>
        private UiScreenCapture.Result Render(string name, params (string Text, UiRect Bounds, int Scale)[] lines)
        {
            var canvas = UiKit.Canvas("FontSpecimen", 500);
            _canvas = canvas.gameObject;
            var root = UiKit.ReferenceRoot(canvas.transform);
            UiKit.Plate(root, ScreenLayout.Screen, Color.black, "Ground");

            foreach (var (text, bounds, scale) in lines)
                UiKit.Label(root, text, bounds, scale, TextAnchor.UpperLeft, Color.white);

            Canvas.ForceUpdateCanvases();
            return UiScreenCapture.Capture(name);
        }

        /// <summary>
        /// The bounding box of the lit pixels inside one layout rectangle.
        ///
        /// The search is restricted to the box the text was placed in, and the captured texture is bottom-up while the
        /// layout is top-down, so the rows are flipped on the way in. Both details were wrong in the first version of
        /// this probe, which is why it reported a 562 px wide letter M.
        /// </summary>
        private static (int MinX, int MaxX, int MinY, int MaxY) LitBounds(UiScreenCapture.Result shot, UiRect box)
        {
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;

            // The whole frame is searched, not just the layout box: the probe canvas paints an opaque black ground
            // over everything and sorts above it, so anything lit is this probe's own text — and a probe that only
            // looked inside the box could not report text that landed outside it, which is the interesting failure.
            const int rowFrom = 0;
            var rowTo = UiScreenCapture.Height;
            const int colFrom = 0;
            var colTo = UiScreenCapture.Width;

            for (var row = rowFrom; row < rowTo; row++)
            for (var x = colFrom; x < colTo; x++)
            {
                var p = shot.Pixels[row * UiScreenCapture.Width + x];
                if (p.r < 200 || p.g < 200 || p.b < 200) continue;
                var y = UiScreenCapture.Height - 1 - row;   // back to top-down layout space
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            return (minX, maxX, minY, maxY);
        }

        /// <summary>
        /// Whether the capture maps one layout pixel onto exactly one captured pixel.
        ///
        /// This has to be established before any glyph measurement means anything: if the canvas sits half a pixel
        /// off the grid, a plate authored one pixel wide rasterises across two, and a five-pixel glyph samples its
        /// five texels across six columns — which looks exactly like a broken font and is not one.
        /// </summary>
        [UnityTest]
        public IEnumerator TheCaptureIsPixelAligned_SoGlyphMeasurementsMeanSomething()
        {
            var canvas = UiKit.Canvas("PixelGridProbe", 500);
            _canvas = canvas.gameObject;
            var root = UiKit.ReferenceRoot(canvas.transform);
            UiKit.Plate(root, ScreenLayout.Screen, Color.black, "Ground");
            UiKit.Plate(root, new UiRect(40, 40, 1, 9), Color.white, "OnePixelWide");

            Canvas.ForceUpdateCanvases();
            var shot = UiScreenCapture.Capture("font_probe_pixelgrid");
            yield return null;

            var thin = LitBounds(shot, new UiRect(30, 30, 20, 30));
            Assert.AreEqual(1, thin.MaxX - thin.MinX + 1,
                "A one pixel plate rasterised across more than one column: the canvas is off the pixel grid, " +
                "and every pixel-art measurement downstream of that is meaningless.");
            Assert.AreEqual(40, thin.MinX, "A plate placed at x=40 must land on x=40.");
        }

        [UnityTest]
        public IEnumerator Advance_MatchesTheNumberTheTextBudgetsAreBuiltOn()
        {
            // M fills the glyph cell edge to edge, so the lit span of a run of Ms is
            // (count - 1) * advance + glyphWidth. Two runs give the advance exactly.
            var box = new UiRect(40, 40, 200, 20);

            var one = Render("font_probe_one", ("M", box, 1));
            var oneSpan = LitBounds(one, box);
            yield return null;
            Object.DestroyImmediate(_canvas);

            var four = Render("font_probe_four", ("MMMM", box, 1));
            var fourSpan = LitBounds(four, box);
            yield return null;

            Assert.AreNotEqual(int.MaxValue, oneSpan.MinX, "Nothing was drawn; the font is not rendering at all.");

            var glyphWidth = oneSpan.MaxX - oneSpan.MinX + 1;
            var fourWidth = fourSpan.MaxX - fourSpan.MinX + 1;
            var advance = (fourWidth - glyphWidth) / 3f;

            Assert.AreEqual(UiText.Advance, advance, 0.01f,
                $"The font advances {advance} px per glyph but every text budget in the UI assumes {UiText.Advance}. " +
                $"('M' measured {glyphWidth} px wide, 'MMMM' measured {fourWidth} px.)");

            Assert.LessOrEqual(glyphWidth, UiText.Advance - 1,
                $"A glyph is {glyphWidth} px wide against a {UiText.Advance} px advance, so consecutive letters touch " +
                "or overlap and the text reads as a smear rather than as words.");
        }

        [UnityTest]
        public IEnumerator CapHeightAndLineSpacing_MatchTheLayoutConstants()
        {
            var box = new UiRect(40, 40, 200, 60);

            var single = Render("font_probe_line1", ("MMM", box, 1));
            var singleRows = LitBounds(single, box);
            yield return null;
            Object.DestroyImmediate(_canvas);

            var wrapped = Render("font_probe_line2", ("MMM\nMMM", box, 1));
            var wrappedRows = LitBounds(wrapped, box);
            yield return null;

            var capHeight = singleRows.MaxY - singleRows.MinY + 1;
            Assert.AreEqual(UiText.GlyphHeight, capHeight,
                $"The face renders {capHeight} px tall where the layout reserves {UiText.GlyphHeight}.");

            var twoLines = wrappedRows.MaxY - wrappedRows.MinY + 1;
            Assert.AreEqual(UiText.Height(2), twoLines,
                $"Two lines occupy {twoLines} px where the layout reserves {UiText.Height(2)}. " +
                "A line spacing that disagrees here is what stacks a multi-line label on top of itself.");

            // And the text has to land inside the box the layout gave it, not above or below it. Unity positions the
            // first baseline from the face's ascent, and a script-built font has none unless one is written in, so
            // this is the assertion that catches the glyphs floating out of their rectangle.
            Assert.AreEqual(box.Y, singleRows.MinY,
                $"The first line starts at y={singleRows.MinY} for a box whose top is y={box.Y}. " +
                "Every label in the UI is placed by its top edge, so a baseline offset here moves the whole interface.");
            Assert.LessOrEqual(wrappedRows.MaxY, box.Bottom,
                $"The last line ends at y={wrappedRows.MaxY}, past the bottom of its {box.Height} px box.");
        }

        [UnityTest]
        public IEnumerator Scaling_IsAWholeMultipleOfTheAuthoredFace()
        {
            // Measured on its own frame: the whole-frame scan would otherwise report the widest line on the sheet.
            var titleBox = new UiRect(20, 20, 400, 32);
            var title = Render("font_probe_title", ("RUINRAIL", titleBox, 4));
            var band = LitBounds(title, titleBox);
            yield return null;
            Object.DestroyImmediate(_canvas);

            // A specimen sheet for human inspection: every family the UI actually draws, at the scales it uses them.
            Render("font_specimen_runtime",
                ("RUINRAIL", new UiRect(20, 20, 400, 32), 4),
                ("ABCDEFGHIJKLM", new UiRect(20, 70, 400, 16), 2),
                ("nopqrstuvwxyz", new UiRect(20, 95, 400, 16), 2),
                ("0123456789 1I0O", new UiRect(20, 120, 400, 16), 2),
                ("The quick brown fox jumps; 12,345 C … · —", new UiRect(20, 150, 600, 12), 1),
                ("STORAGE LOADOUT TRADER CHARACTER WORKSHOP", new UiRect(20, 170, 600, 12), 1));

            var titleWidth = band.MaxX - band.MinX + 1;
            // The last glyph contributes its own width, not a full advance, so the lit span is one inter-glyph gap
            // short of the reserved width.
            var expected = UiText.Width("RUINRAIL", 4) - (UiText.Advance - 5) * 4;

            Assert.AreEqual(expected, titleWidth, 4,
                $"The wordmark renders {titleWidth} px wide at scale 4; the layout reserves {UiText.Width("RUINRAIL", 4)} " +
                "and an integer scale of a bitmap face must land on exactly that.");

            yield return null;
        }

        [UnityTest]
        public IEnumerator TheFontIsTheProjectsOwn_NotUnitysBuiltin()
        {
            Assert.IsTrue(UiKit.UsingPixelFont,
                "The release path must not fall back to LegacyRuntime.ttf (FINAL_ART_PRODUCTION_SPEC 29.12).");

            var font = UiKit.Font();
            Debug.Log($"RUINRAIL pixel face: fontSize={font.fontSize}, ascent={font.ascent}, lineHeight={font.lineHeight}, glyphs={font.characterInfo.Length}");
            Assert.Greater(font.characterInfo.Length, 80, "The face carries the whole charset.");
            foreach (var ch in "RUINRAIL0123456789…·—")
                Assert.IsTrue(font.characterInfo.Any(c => c.index == ch),
                    $"The face has no glyph for '{ch}', so the UI draws nothing where it uses that character.");

            yield break;
        }
    }
}
