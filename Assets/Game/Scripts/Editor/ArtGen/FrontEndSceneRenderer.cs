using System;
using System.Collections.Generic;
using RuinRail.App;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// A small pixel raycaster for the front-end backdrops: a one-point-perspective shell (floor, ceiling, two walls,
    /// end wall) plus axis-aligned boxes, each with a procedural material, lit by point lamps (with box shadows),
    /// faded into fog with distance, and finished in pixel-art light bands instead of smooth gradients. It gives the
    /// Main Menu tunnel and the Shelter room real structure, depth and practical lighting from one consistent camera
    /// (<see cref="FrontEndScenes"/>), which flat front-on drawing could not.
    /// </summary>
    internal sealed class FrontEndSceneRenderer
    {
        public delegate Color Surface(Vector3 p, Vector3 n);

        public sealed class Box
        {
            public Vector3 Min, Max;
            public Surface Mat;
            public bool Emissive;
            public bool CastsShadow = true;
        }

        public readonly struct Lamp
        {
            public Lamp(Vector3 p, Color c, float intensity, float range) { Position = p; Color = c; Intensity = intensity; Range = range; }
            public Vector3 Position { get; }
            public Color Color { get; }
            public float Intensity { get; }
            public float Range { get; }
        }

        public FrontEndScenes.View View;
        public float XMin, XMax, YMin, YMax, ZEnd;
        public Surface Floor, Ceiling, Left, Right, End;
        public readonly List<Box> Boxes = new();
        public readonly List<Lamp> Lamps = new();
        public Color Ambient = new(0.16f, 0.17f, 0.18f);
        public Color Fog = new(0.05f, 0.06f, 0.07f);
        public float FogDensity = 0.03f;
        /// <summary>Light is quantised to this many bands (pixel-art shading), with a small ordered dither at the edges.</summary>
        public int Bands = 7;

        private static readonly float[] Bayer = { 0f, 0.5f, 0.125f, 0.625f, 0.75f, 0.25f, 0.875f, 0.375f, 0.1875f, 0.6875f, 0.0625f, 0.5625f, 0.9375f, 0.4375f, 0.8125f, 0.3125f };

        public Vector2 Project(Vector3 p) => View.Project(p);

        public PixelCanvas Render(int width, int height)
        {
            var c = new PixelCanvas(width, height);
            for (var py = 0; py < height; py++)
            for (var px = 0; px < width; px++)
            {
                var d = new Vector3((px + 0.5f - View.Cx) / View.Focal, (py + 0.5f - View.Cy) / View.Focal, 1f);
                if (!Trace(Vector3.zero, d, float.MaxValue, out var t, out var n, out var mat, out var emissive)) { c.Set(px, py, Fog); continue; }
                var p = d * t;
                var albedo = mat(p, n);
                Color lit;
                if (emissive) lit = albedo;
                else
                {
                    var light = Ambient;
                    foreach (var lamp in Lamps)
                    {
                        var v = lamp.Position - p;
                        var dist = v.magnitude;
                        if (dist < 0.0001f) continue;
                        var l = v / dist;
                        var lambert = Mathf.Max(0f, Vector3.Dot(n, l)) * 0.85f + 0.15f;
                        var att = lamp.Intensity / (1f + (dist / lamp.Range) * (dist / lamp.Range));
                        if (att < 0.01f) continue;
                        if (Occluded(p + n * 0.01f, l, dist - 0.05f)) continue;
                        light += lamp.Color * (lambert * att);
                    }

                    // Pixel-art light: a few flat bands, dithered only on their edges.
                    var dither = Bayer[(py & 3) * 4 + (px & 3)] - 0.5f;
                    light = new Color(Band(light.r, dither), Band(light.g, dither), Band(light.b, dither));
                    lit = new Color(albedo.r * light.r, albedo.g * light.g, albedo.b * light.b);
                }

                var fog = 1f - Mathf.Exp(-t * FogDensity);
                c.Set(px, py, Color.Lerp(lit, Fog, fog));
            }

            return c;
        }

        private float Band(float v, float dither) => Mathf.Max(0f, Mathf.Floor(v * Bands + 0.5f + dither * 0.35f) / Bands);

        private bool Occluded(Vector3 o, Vector3 d, float maxT)
        {
            foreach (var b in Boxes)
            {
                if (!b.CastsShadow || b.Emissive) continue;
                if (HitBox(o, d, b, out var t, out _) && t > 0.001f && t < maxT) return true;
            }

            return false;
        }

        private bool Trace(Vector3 o, Vector3 d, float maxT, out float bestT, out Vector3 normal, out Surface mat, out bool emissive)
        {
            var best = maxT;
            var hitNormal = Vector3.zero;
            Surface hitMat = null;
            var hitEmissive = false;

            void Plane(float t, Vector3 n, Surface m, Func<Vector3, bool> inside)
            {
                if (t <= 0.001f || t >= best || m == null) return;
                var p = o + d * t;
                if (!inside(p)) return;
                best = t; hitNormal = n; hitMat = m; hitEmissive = false;
            }

            const float e = 0.0005f;
            if (d.y < 0f) Plane((YMin - o.y) / d.y, Vector3.up, Floor, p => p.z <= ZEnd + e && p.x >= XMin - e && p.x <= XMax + e);
            if (d.y > 0f) Plane((YMax - o.y) / d.y, Vector3.down, Ceiling, p => p.z <= ZEnd + e && p.x >= XMin - e && p.x <= XMax + e);
            if (d.x < 0f) Plane((XMin - o.x) / d.x, Vector3.right, Left, p => p.z <= ZEnd + e && p.y >= YMin - e && p.y <= YMax + e);
            if (d.x > 0f) Plane((XMax - o.x) / d.x, Vector3.left, Right, p => p.z <= ZEnd + e && p.y >= YMin - e && p.y <= YMax + e);
            if (d.z > 0f) Plane((ZEnd - o.z) / d.z, Vector3.back, End, p => true);

            foreach (var b in Boxes)
            {
                if (!HitBox(o, d, b, out var t, out var n) || t >= best || t <= 0.001f) continue;
                best = t; hitNormal = n; hitMat = b.Mat; hitEmissive = b.Emissive;
            }

            bestT = best;
            normal = hitNormal;
            mat = hitMat;
            emissive = hitEmissive;
            return mat != null;
        }

        private static bool HitBox(Vector3 o, Vector3 d, Box b, out float t, out Vector3 n)
        {
            var tmin = float.MinValue;
            var tmax = float.MaxValue;
            n = Vector3.zero;
            for (var axis = 0; axis < 3; axis++)
            {
                var oa = o[axis];
                var da = d[axis];
                var lo = b.Min[axis];
                var hi = b.Max[axis];
                if (Mathf.Abs(da) < 1e-6f)
                {
                    if (oa < lo || oa > hi) { t = 0f; return false; }
                    continue;
                }

                var t1 = (lo - oa) / da;
                var t2 = (hi - oa) / da;
                var sign = -1f;
                if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1f; }
                if (t1 > tmin)
                {
                    tmin = t1;
                    n = Vector3.zero;
                    n[axis] = sign;
                }

                tmax = Mathf.Min(tmax, t2);
                if (tmin > tmax) { t = 0f; return false; }
            }

            t = tmin;
            return tmin > 0f;
        }

        // ---------------- shared material helpers ----------------

        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                var h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// <summary>Smooth value noise at a world scale.</summary>
        public static float Noise(float x, float y, float scale, int seed)
        {
            x /= scale;
            y /= scale;
            var ix = Mathf.FloorToInt(x);
            var iy = Mathf.FloorToInt(y);
            var fx = x - ix;
            var fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            var a = Hash(ix, iy, seed);
            var b = Hash(ix + 1, iy, seed);
            var cc = Hash(ix, iy + 1, seed);
            var dd = Hash(ix + 1, iy + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(cc, dd, fx), fy);
        }

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        /// <summary>Grime: darkens a material in noise patches, heavier low on a wall.</summary>
        public static Color Grime(Color c, float u, float v, float amount, int seed)
        {
            var n = Noise(u, v, 0.9f, seed) * 0.7f + Noise(u, v, 0.25f, seed + 1) * 0.3f;
            return n > 0.55f ? Color.Lerp(c, c * 0.62f, Mathf.Clamp01((n - 0.55f) * 3f) * amount) : c;
        }
    }
}
