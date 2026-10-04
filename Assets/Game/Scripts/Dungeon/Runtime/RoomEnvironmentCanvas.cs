using System;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The pixel buffer one room's environment layer is painted into (texture space: x right, y up, one pixel = one
    /// art pixel at PPU 32), plus the deterministic noise and the few primitives the painters share. Pure CPU and pure
    /// functions of its inputs, so every peer paints the same room identically.
    /// </summary>
    internal sealed class RoomEnvironmentCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        /// <summary>
        /// Per-cell permission and alpha scale applied before blending (0 = untouchable), one entry per 32x32 room cell;
        /// null means everywhere at full strength. Every rule the layer keeps is a cell rule, so a cell table is exact.
        /// </summary>
        public float[] Gate;
        private readonly int _cellsWide;

        public RoomEnvironmentCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color32[width * height];
            _cellsWide = Mathf.Max(1, width / CellPixels);
        }

        private const int CellPixels = 32;

        public float GateAt(int x, int y) => Gate == null ? 1f : Gate[(y / CellPixels) * _cellsWide + x / CellPixels];

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public Color32 Get(int x, int y) => Pixels[y * Width + x];

        /// <summary>Opaque write that ignores the gate (seam repair copies the floor's own pixels).</summary>
        public void Set(int x, int y, Color32 color)
        {
            if (In(x, y)) Pixels[y * Width + x] = color;
        }

        /// <summary>Straight-alpha "over" blend of <paramref name="color"/> at <paramref name="alpha"/> × the gate.</summary>
        public void Over(int x, int y, Color32 color, float alpha)
        {
            if (alpha <= 0f || (uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            var g = Gate == null ? 1f : Gate[(y / CellPixels) * _cellsWide + x / CellPixels];
            if (g <= 0f) return;
            var a = alpha * g * color.a * (1f / 255f);
            if (a > 1f) a = 1f;
            var sa = (int)(a * 255f + 0.5f);
            if (sa <= 0) return;
            var i = y * Width + x;
            var dst = Pixels[i];
            if (dst.a == 0 || sa >= 255)
            {
                Pixels[i] = new Color32(color.r, color.g, color.b, (byte)sa);
                return;
            }

            // Straight-alpha "over" in integers: out = src*sa + dst*da*(255-sa)/255, normalised by the out alpha.
            var dk = dst.a * (255 - sa) / 255;
            var outA = sa + dk;
            Pixels[i] = new Color32(
                (byte)((color.r * sa + dst.r * dk + outA / 2) / outA),
                (byte)((color.g * sa + dst.g * dk + outA / 2) / outA),
                (byte)((color.b * sa + dst.b * dk + outA / 2) / outA),
                (byte)outA);
        }

        /// <summary>An opaque-looking object pixel (clutter): full alpha, still gated.</summary>
        public void Solid(int x, int y, Color32 color) => Over(x, y, color, 1f);

        public void Rect(int x, int y, int w, int h, Color32 color, float alpha)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Over(xx, yy, color, alpha);
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 color, float alpha)
        {
            var dx = Math.Abs(x1 - x0);
            var dy = -Math.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx + dy;
            for (var guard = 0; guard < 4096; guard++)
            {
                Over(x0, y0, color, alpha);
                if (x0 == x1 && y0 == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>FNV-1a over the buffer: the determinism fingerprint tests compare across builds of one room.</summary>
        public ulong Fingerprint()
        {
            var h = 1469598103934665603UL;
            foreach (var p in Pixels)
            {
                h = (h ^ p.r) * 1099511628211UL;
                h = (h ^ p.g) * 1099511628211UL;
                h = (h ^ p.b) * 1099511628211UL;
                h = (h ^ p.a) * 1099511628211UL;
            }

            return h;
        }

        public int CountTouched()
        {
            var n = 0;
            foreach (var p in Pixels) if (p.a > 0) n++;
            return n;
        }

        // ---------------------------------------------------------------- noise

        public static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                var h = seed ^ ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u);
                h ^= h >> 13;
                h *= 0x85EBCA6Bu;
                h ^= h >> 16;
                h *= 0xC2B2AE35u;
                h ^= h >> 15;
                return h;
            }
        }

        /// <summary>White noise in [0,1).</summary>
        public static float White(int x, int y, uint seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Smooth value noise in [0,1) at the given lattice scale (pixels per lattice cell).</summary>
        public static float Value(float x, float y, float scale, uint seed)
        {
            // Offset into positive space so a truncating cast is a floor (callers stay within a few rooms of 0).
            var fx = x / scale + 4096f;
            var fy = y / scale + 4096f;
            var ix = (int)fx;
            var iy = (int)fy;
            var tx = fx - ix;
            var ty = fy - iy;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            var a = (Hash(ix, iy, seed) & 0xFFFFFF) * (1f / 16777216f);
            var b = (Hash(ix + 1, iy, seed) & 0xFFFFFF) * (1f / 16777216f);
            var c = (Hash(ix, iy + 1, seed) & 0xFFFFFF) * (1f / 16777216f);
            var d = (Hash(ix + 1, iy + 1, seed) & 0xFFFFFF) * (1f / 16777216f);
            var top = a + (b - a) * tx;
            var bottom = c + (d - c) * tx;
            return top + (bottom - top) * ty;
        }

        /// <summary>Two-octave fractal value noise in [0,1).</summary>
        public static float Fbm(float x, float y, float scale, uint seed) =>
            Value(x, y, scale, seed) * 0.65f + Value(x, y, scale * 0.45f, seed ^ 0x9E3779B9u) * 0.35f;

        private static readonly float[] Bayer4 =
        {
            0f / 16f, 8f / 16f, 2f / 16f, 10f / 16f,
            12f / 16f, 4f / 16f, 14f / 16f, 6f / 16f,
            3f / 16f, 11f / 16f, 1f / 16f, 9f / 16f,
            15f / 16f, 7f / 16f, 13f / 16f, 5f / 16f
        };

        /// <summary>
        /// Quantises a continuous 0..1 strength into <paramref name="steps"/> flat bands with ordered dithering between
        /// them, so gradients read as pixel-art bands instead of smooth airbrush.
        /// </summary>
        public static float Dither(float value, int x, int y, int steps)
        {
            if (value <= 0f) return 0f;
            if (value >= 1f) return 1f;
            var scaled = value * steps;
            var band = Mathf.Floor(scaled);
            var frac = scaled - band;
            var threshold = Bayer4[(y & 3) * 4 + (x & 3)];
            return (band + (frac > threshold ? 1f : 0f)) / steps;
        }

        /// <summary>
        /// Quantises a 0..1 strength into flat bands whose boundaries wander with low-frequency noise: the clustered,
        /// hand-placed look of pixel-art grime and light falloff, with no checkerboard and no perfect rings.
        /// </summary>
        public static float Bands(float value, int x, int y, int steps, uint seed, float wobble = 0.9f)
        {
            if (value <= 0f) return 0f;
            var v = value * steps + (Value(x, y, 3.5f, seed) - 0.5f) * wobble;
            return Mathf.Clamp(Mathf.Floor(v + 0.5f), 0f, steps) / steps;
        }

        public static Color32 Shade(Color32 c, float k) => new(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * k), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * k), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * k), 0, 255), c.a);

        public static Color32 Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var color);
            return color;
        }
    }
}
