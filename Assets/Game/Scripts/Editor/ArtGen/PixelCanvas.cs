using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// A hard-edged pixel drawing surface implementing the global rules in FINAL_ART_PRODUCTION_SPEC section 2.
    ///
    /// Everything here writes fully opaque or fully transparent pixels — there is no blending anywhere, because
    /// spec 2.2 forbids anti-alias fringe and a single semi-transparent edge pixel would fail the acceptance gate.
    /// Shading is applied as discrete ramp steps keyed to the canonical north-west key light (2.4), and the outline
    /// pass is selective: it darkens the exterior silhouette edge only, never every internal form (2.3).
    /// </summary>
    public sealed class PixelCanvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly Color32[] _pixels;
        private readonly bool[] _opaque;

        private static readonly Color32 Clear = new(0, 0, 0, 0);

        public PixelCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            _pixels = new Color32[width * height];
            _opaque = new bool[width * height];
            Clear_();
        }

        private void Clear_()
        {
            for (var i = 0; i < _pixels.Length; i++) { _pixels[i] = Clear; _opaque[i] = false; }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
        public bool IsOpaque(int x, int y) => InBounds(x, y) && _opaque[y * Width + x];
        public Color32 Get(int x, int y) => InBounds(x, y) ? _pixels[y * Width + x] : Clear;

        /// <summary>Writes one fully opaque pixel. The only way anything becomes visible.</summary>
        public void Set(int x, int y, Color32 c)
        {
            if (!InBounds(x, y)) return;
            c.a = 255;
            _pixels[y * Width + x] = c;
            _opaque[y * Width + x] = true;
        }

        /// <summary>
        /// Writes one pixel keeping the alpha it was given. Sprite art is opaque by rule (<see cref="Set"/>); this is
        /// for the handful of canvas-space overlays that *are* a falloff — the low-HP vignette — where a stepped
        /// opaque ring would read as banding rather than as a soft frame. A fully transparent write erases.
        /// </summary>
        public void SetTranslucent(int x, int y, Color32 c)
        {
            if (!InBounds(x, y)) return;
            if (c.a == 0) { Erase(x, y); return; }
            _pixels[y * Width + x] = c;
            _opaque[y * Width + x] = true;
        }

        public void Erase(int x, int y)
        {
            if (!InBounds(x, y)) return;
            _pixels[y * Width + x] = Clear;
            _opaque[y * Width + x] = false;
        }

        // ---- primitives ----

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Set(xx, yy, c);
        }

        public void RectOutline(int x, int y, int w, int h, Color32 c)
        {
            for (var xx = x; xx < x + w; xx++) { Set(xx, y, c); Set(xx, y + h - 1, c); }
            for (var yy = y; yy < y + h; yy++) { Set(x, yy, c); Set(x + w - 1, yy, c); }
        }

        /// <summary>Filled ellipse. Used for heads, shoulders, masses and blob shadows.</summary>
        public void Ellipse(float cx, float cy, float rx, float ry, Color32 c)
        {
            if (rx <= 0f || ry <= 0f) return;
            var x0 = Mathf.FloorToInt(cx - rx); var x1 = Mathf.CeilToInt(cx + rx);
            var y0 = Mathf.FloorToInt(cy - ry); var y1 = Mathf.CeilToInt(cy + ry);
            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var dx = (x + 0.5f - cx) / rx;
                var dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy <= 1f) Set(x, y, c);
            }
        }

        /// <summary>Bresenham line with an optional thickness, for limbs, barrels, cables and rails.</summary>
        public void Line(int x0, int y0, int x1, int y1, Color32 c, int thickness = 1)
        {
            var dx = Mathf.Abs(x1 - x0); var sx = x0 < x1 ? 1 : -1;
            var dy = -Mathf.Abs(y1 - y0); var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            while (true)
            {
                Dot(x0, y0, c, thickness);
                if (x0 == x1 && y0 == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        public void Dot(int x, int y, Color32 c, int thickness = 1)
        {
            if (thickness <= 1) { Set(x, y, c); return; }
            var r = thickness / 2;
            for (var yy = -r; yy <= r; yy++)
            for (var xx = -r; xx <= r; xx++)
                if (xx * xx + yy * yy <= r * r + 1) Set(x + xx, y + yy, c);
            }

        /// <summary>A trapezoid column: the workhorse for torsos, legs, barrels and tapered plates.</summary>
        public void Taper(int cx, int yTop, int yBottom, int halfTop, int halfBottom, Color32 c)
        {
            if (yBottom < yTop) (yTop, yBottom) = (yBottom, yTop);
            var span = Mathf.Max(1, yBottom - yTop);
            for (var y = yTop; y <= yBottom; y++)
            {
                var t = (float)(y - yTop) / span;
                var half = Mathf.RoundToInt(Mathf.Lerp(halfTop, halfBottom, t));
                for (var x = cx - half; x <= cx + half; x++) Set(x, y, c);
            }
        }

        // ---- shading ----

        /// <summary>
        /// Applies the canonical north-west key light (spec 2.4) across a region: upper/left-facing planes take the
        /// light step, lower/right planes take the shadow step. Discrete steps only — no gradient (2.5).
        /// </summary>
        public void ShadeRegion(int x, int y, int w, int h, RuinPalette.Ramp ramp, int lightWidth = 1)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                if (!IsOpaque(xx, yy)) continue;

                // A pixel is lit when the surface opens up/left of it, shadowed when it opens down/right.
                var openUp = !IsOpaque(xx, yy + 1);
                var openLeft = !IsOpaque(xx - 1, yy);
                var openDown = !IsOpaque(xx, yy - 1);
                var openRight = !IsOpaque(xx + 1, yy);

                var nearUpLeft = WithinOpenDistance(xx, yy, 0, 1, lightWidth) || WithinOpenDistance(xx, yy, -1, 0, lightWidth);
                var nearDownRight = WithinOpenDistance(xx, yy, 0, -1, lightWidth) || WithinOpenDistance(xx, yy, 1, 0, lightWidth);

                int step;
                if (openUp || openLeft) step = 2;
                else if (openDown || openRight) step = 0;
                else if (nearUpLeft) step = 2;
                else if (nearDownRight) step = 0;
                else step = 1;

                Set(xx, yy, ramp.At(step));
            }
        }

        private bool WithinOpenDistance(int x, int y, int dx, int dy, int distance)
        {
            for (var i = 1; i <= distance; i++)
                if (!IsOpaque(x + dx * i, y + dy * i)) return true;
            return false;
        }

        /// <summary>
        /// Shades one drawn form volumetrically: the left third takes the light step, the right quarter the shadow
        /// step, the rest the base, with the form's top row lifted a further step. This reads as a rounded volume lit
        /// from the north-west (spec 2.4) rather than the edge-only rim that openness-testing produces, and it stays
        /// in discrete ramp steps so no gradient appears (2.5).
        ///
        /// Only pixels currently matching <paramref name="formColor"/> are touched, so shading a part never repaints
        /// the parts drawn before it.
        /// </summary>
        public void ShadeForm(int x, int y, int w, int h, Color32 formColor, RuinPalette.Ramp ramp)
        {
            if (w <= 0 || h <= 0) return;

            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                if (!IsOpaque(xx, yy)) continue;
                if (!Same(Get(xx, yy), formColor)) continue;

                // Horizontal position within the form drives the core light-to-shadow read.
                var t = w <= 1 ? 0.5f : (float)(xx - x) / (w - 1);
                int step;
                if (t < 0.30f) step = 2;
                else if (t > 0.76f) step = 0;
                else step = 1;

                // Top surface catches the key light; the lowest row grounds into shadow.
                var topmost = !IsOpaque(xx, yy + 1);
                var bottommost = !IsOpaque(xx, yy - 1);
                if (topmost && step < 3) step++;
                if (bottommost && step > 0) step--;

                Set(xx, yy, ramp.At(step));
            }
        }

        private static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// <summary>Draws a form in its ramp's base colour and immediately shades it as one volume.</summary>
        public void Form(System.Action<Color32> draw, RuinPalette.Ramp ramp, int x, int y, int w, int h)
        {
            draw(ramp.Base);
            ShadeForm(x, y, w, h, ramp.Base, ramp);
        }

        /// <summary>
        /// Selective exterior outline (spec 2.3): darkens only pixels on the silhouette edge, leaving internal forms
        /// separated by value. Emissive pixels are exempt so bright tech can break the outline as the spec allows.
        /// </summary>
        public void SelectiveOutline(Color32 outline, HashSet<int> emissive = null)
        {
            var edges = new List<int>();
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!IsOpaque(x, y)) continue;
                var index = y * Width + x;
                if (emissive != null && emissive.Contains(index)) continue;
                if (!IsOpaque(x - 1, y) || !IsOpaque(x + 1, y) || !IsOpaque(x, y - 1) || !IsOpaque(x, y + 1))
                    edges.Add(index);
            }

            foreach (var index in edges)
            {
                var c = _pixels[index];
                // Blend the existing colour toward the structural dark so the outline stays coloured, not pure black.
                _pixels[index] = new Color32(
                    (byte)((c.r * 32 + outline.r * 224) / 256),
                    (byte)((c.g * 32 + outline.g * 224) / 256),
                    (byte)((c.b * 32 + outline.b * 224) / 256),
                    255);
            }
        }

        /// <summary>Deterministic pseudo-random grime, in 2-3 px clusters per the minimum cluster rule (spec 2.2).</summary>
        public void Grime(int x, int y, int w, int h, Color32 c, int seed, float density = 0.06f, int clusterSize = 2)
        {
            var rng = new System.Random(seed);
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                if (!IsOpaque(xx, yy)) continue;
                if (rng.NextDouble() > density) continue;
                for (var cy = 0; cy < clusterSize; cy++)
                for (var cx = 0; cx < clusterSize; cx++)
                    if (IsOpaque(xx + cx, yy + cy)) Set(xx + cx, yy + cy, c);
            }
        }

        /// <summary>Marks pixels as emissive so the outline pass leaves them alone.</summary>
        public HashSet<int> MarkEmissive(int x, int y, int w, int h)
        {
            var set = new HashSet<int>();
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                if (IsOpaque(xx, yy)) set.Add(yy * Width + xx);
            return set;
        }

        // ---- output ----

        public void Blit(PixelCanvas source, int offsetX, int offsetY)
        {
            for (var y = 0; y < source.Height; y++)
            for (var x = 0; x < source.Width; x++)
                if (source.IsOpaque(x, y)) Set(x + offsetX, y + offsetY, source.Get(x, y));
        }

        /// <summary>Horizontal mirror. Spec 4.1 permits it for facings when handedness stays readable.</summary>
        public PixelCanvas MirroredX()
        {
            var flipped = new PixelCanvas(Width, Height);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (IsOpaque(x, y)) flipped.Set(Width - 1 - x, y, Get(x, y));
            return flipped;
        }

        /// <summary>Shifts content, used for animation frames (bob, recoil, step).</summary>
        public PixelCanvas Shifted(int dx, int dy)
        {
            var moved = new PixelCanvas(Width, Height);
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
                if (IsOpaque(x, y)) moved.Set(x + dx, y + dy, Get(x, y));
            return moved;
        }

        public int OpaqueCount()
        {
            var n = 0;
            foreach (var o in _opaque) if (o) n++;
            return n;
        }

        /// <summary>Number of distinct colours used — the acceptance gate checks this stays controlled (spec 2.5).</summary>
        public int DistinctColors()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < _pixels.Length; i++)
            {
                if (!_opaque[i]) continue;
                var c = _pixels[i];
                seen.Add((c.r << 16) | (c.g << 8) | c.b);
            }
            return seen.Count;
        }

        /// <summary>Tight bounds of the drawn content, for verifying occupancy against the spec's target regions.</summary>
        public RectInt ContentBounds()
        {
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (var y = 0; y < Height; y++)
            for (var x = 0; x < Width; x++)
            {
                if (!IsOpaque(x, y)) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }

            return maxX < 0 ? new RectInt(0, 0, 0, 0) : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        public Texture2D ToTexture()
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(_pixels);
            tex.Apply();
            return tex;
        }

        public byte[] ToPng()
        {
            var tex = ToTexture();
            var bytes = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            return bytes;
        }

        /// <summary>Composites frames left-to-right into one sheet row. Frames must share dimensions.</summary>
        public static PixelCanvas Row(IReadOnlyList<PixelCanvas> frames)
        {
            if (frames == null || frames.Count == 0) throw new ArgumentException("no frames");
            var w = frames[0].Width; var h = frames[0].Height;
            var sheet = new PixelCanvas(w * frames.Count, h);
            for (var i = 0; i < frames.Count; i++) sheet.Blit(frames[i], i * w, 0);
            return sheet;
        }

        /// <summary>Composites a grid of frames: one row per facing, one column per frame (spec 21 slicing rule).</summary>
        public static PixelCanvas Grid(IReadOnlyList<IReadOnlyList<PixelCanvas>> rows)
        {
            var w = rows[0][0].Width; var h = rows[0][0].Height;
            var cols = 0;
            foreach (var r in rows) cols = Mathf.Max(cols, r.Count);
            var sheet = new PixelCanvas(w * cols, h * rows.Count);
            for (var r = 0; r < rows.Count; r++)
            for (var c = 0; c < rows[r].Count; c++)
                // Row 0 sits at the top of the sheet, which is the bottom in texture space.
                sheet.Blit(rows[r][c], c * w, (rows.Count - 1 - r) * h);
            return sheet;
        }
    }
}
