using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// The UI's text component for the pixel face: a <see cref="Text"/> whose glyph rows always sample texel centres.
    ///
    /// Unity's text generator emits every glyph quad half a pixel off the grid, with UVs padded by half a texel, so that
    /// each screen-pixel centre lands on a texel centre. A pixel-perfect canvas then rounds the whole mesh back onto
    /// whole pixels — which moves every pixel centre onto a texel boundary. With the point-filtered atlas the GPU then
    /// picks the row above or below by float error, so, depending only on where a label sits on screen, a glyph row was
    /// dropped (the "missing bottom row" on labels, buttons and prompts). Horizontally the rounded quad already samples
    /// inside the texels, so only the vertical part of that rounding is undone here; the glyph rows then sit strictly
    /// inside their pixels at every position and every integer UI scale, with no filtering change.
    ///
    /// The same pass puts the glyph cell's top on the rect's top row: the RUINRAIL face declares an ascent of 7 while
    /// its 7-row cells span −1..+6 around the baseline, so drawn where Unity puts them they sit one row below the box
    /// the layout gave the label (the old rounding artefact happened to hide that by sampling one row off). The lift is
    /// read from the face (ascent − cell top), in glyph pixels, so a scaled label moves by whole scaled pixels and the
    /// label's RectTransform stays exactly its layout box.
    /// </summary>
    public sealed class PixelText : Text
    {
        private static Font _liftFont;
        private static int _lift;

        /// <summary>Rows between the face's ascent line and its glyph cell top (0 for a face whose cells start at the ascent).</summary>
        public static int CellLift(Font face)
        {
            if (face == null) return 0;
            if (ReferenceEquals(face, _liftFont)) return _lift;
            _liftFont = face;
            _lift = !face.dynamic && face.GetCharacterInfo('M', out var cell, face.fontSize) ? Mathf.Max(0, Mathf.RoundToInt(face.ascent) - cell.maxY) : 0;
            return _lift;
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            base.OnPopulateMesh(toFill);
            if (font == null || toFill.currentVertCount == 0) return;
            var generated = cachedTextGenerator.verts;
            if (generated == null || generated.Count == 0) return;

            // The base applied one uniform rounding offset; the generator's own first vertex tells how far it moved in y.
            var first = new UIVertex();
            toFill.PopulateUIVertex(ref first, 0);
            var dy = generated[0].position.y / pixelsPerUnit - first.position.y + CellLift(font) / pixelsPerUnit;
            if (Mathf.Abs(dy) < 0.0001f) return;
            var v = new UIVertex();
            for (var i = 0; i < toFill.currentVertCount; i++)
            {
                toFill.PopulateUIVertex(ref v, i);
                v.position.y += dy;
                toFill.SetUIVertex(v, i);
            }
        }
    }
}
