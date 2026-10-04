using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>How one telegraph is drawn this frame (all presentation).</summary>
    public struct TelegraphLook
    {
        /// <summary>The danger colour (normal enemies amber-orange, Elites/Bosses red).</summary>
        public Color Colour;

        /// <summary>0..1 toward the strike: the fill sweeps out from the source and reaches the far edge exactly at impact.</summary>
        public float Progress;

        /// <summary>The final-warning blink is lit (the edge goes white-hot).</summary>
        public bool Warning;

        /// <summary>0..1 impact flash: the footprint lights up the moment it strikes, then fades.</summary>
        public float Flash;

        /// <summary>Elite/Boss: a heavier edge.</summary>
        public bool Heavy;
    }

    /// <summary>
    /// One ground danger marker, painted at runtime on the world pixel grid (32 px per tile, never rotated or stretched, so
    /// it sits on the same grid as the floor art at any angle). It draws the union of the attack's footprint shapes
    /// exactly: every pixel whose square touches the real footprint is part of the marker, so the outer edge never lies
    /// inside the area that can hurt. Inside: a bright outer edge on a dark backing (readable on pale and dark floors), a
    /// transparent centre with a light world-anchored pattern (hatch for areas, chevrons along a dash, a centre dash line on
    /// a shot lane), and a fill that sweeps from the source to the far edge as the strike approaches. Drawn on the
    /// ground-details layer above hazards and loot glows and below every character, projectile and pickup.
    /// The geometry is rebuilt only when the footprint moves by a pixel or turns; the colours are repainted only when the
    /// quantised look changes.
    /// </summary>
    public sealed class TelegraphMarkerView : MonoBehaviour
    {
        public const int PixelsPerUnit = SortingConvention.PixelsPerUnit;

        /// <summary>A shot lane is drawn at least this half-width (tiles) so it reads; the real path is never wider than what is drawn.</summary>
        public const float MinLaneHalfWidth = 0.25f;

        /// <summary>Sorting order on the ground-details layer: above hazard footprints (10) and loot glows.</summary>
        public const int SortingOrder = 40;

        // A pixel belongs to the marker when its square overlaps the footprint: centre within half a pixel diagonal.
        private const float Margin = 0.7072f / PixelsPerUnit;

        private const byte BandEdge = 1, BandBacking = 2, BandInterior = 3;
        private const byte PatternHatch = 1, PatternChevron = 2, PatternCentre = 4;

        public enum Pattern
        {
            /// <summary>Areas (rings, boxes): a sparse diagonal hatch.</summary>
            Hatch,
            /// <summary>A dash lane: chevrons pointing along the run.</summary>
            Chevrons,
            /// <summary>A shot lane: a dashed centre line.</summary>
            CentreLine
        }

        public enum Sweep
        {
            /// <summary>The fill grows out from the source point (rings, shot lanes).</summary>
            Radial,
            /// <summary>The fill advances along the shape's direction from its near end (boxes, dash lanes).</summary>
            Along
        }

        private SpriteRenderer _renderer;
        private Texture2D _texture;
        private Sprite _sprite;
        private float[] _distance = Array.Empty<float>();
        private byte[] _progress = Array.Empty<byte>();
        private byte[] _pattern = Array.Empty<byte>();
        private int[] _covered = Array.Empty<int>();
        private byte[] _band = Array.Empty<byte>();
        private int _coveredCount;
        private int _w, _h, _x0, _y0;
        private long _shapeKey = long.MinValue;
        private int _lookKey = int.MinValue;

        /// <summary>Times the footprint was rasterised / the colours repainted (diagnostics: both stay rare).</summary>
        public int Rebuilds { get; private set; }
        public int Repaints { get; private set; }

        public bool IsVisible => _renderer != null && _renderer.enabled && _coveredCount > 0;
        public SpriteRenderer Renderer => _renderer;

        /// <summary>World-space rectangle the painted pixels cover.</summary>
        public Rect WorldRect => new((float)_x0 / PixelsPerUnit, (float)_y0 / PixelsPerUnit, (float)_w / PixelsPerUnit, (float)_h / PixelsPerUnit);

        /// <summary>Pixels the marker paints (its footprint on the grid).</summary>
        public int CoveredPixels => _coveredCount;

        public static TelegraphMarkerView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var view = go.AddComponent<TelegraphMarkerView>();
            view._renderer = go.AddComponent<SpriteRenderer>();
            view._renderer.sortingLayerName = SortingConvention.LayerOf(SortingRole.Hazard);
            view._renderer.sortingOrder = SortingOrder;
            view._renderer.enabled = false;
            return view;
        }

        public void Hide()
        {
            if (_renderer != null) _renderer.enabled = false;
        }

        /// <summary>True when a world pixel (integer grid coordinates) is painted by the marker.</summary>
        public bool PaintsPixel(int worldX, int worldY)
        {
            var x = worldX - _x0;
            var y = worldY - _y0;
            if (x < 0 || y < 0 || x >= _w || y >= _h) return false;
            var i = y * _w + x;
            return _coveredCount > 0 && _stamp[i] == _build;
        }

        /// <summary>
        /// Rasterises the footprint (the union of <paramref name="shapes"/>) when it differs from the one already painted.
        /// Shot lanes (<see cref="Pattern.CentreLine"/>) are widened to <see cref="MinLaneHalfWidth"/> for readability.
        /// </summary>
        public void SetFootprint(IReadOnlyList<AttackFootprint.Shape> shapes, Vector2 source, Pattern pattern, Sweep sweep, bool heavy) =>
            SetFootprint(shapes, source, pattern, sweep, heavy, FrameBudgetMs);

        /// <summary>
        /// As above with an explicit per-call time budget: a heavy footprint (a full ring of lanes, a 30-tile rail zone) is
        /// rasterised over a few consecutive frames instead of in one, so it never costs a visible hitch; it completes within
        /// the first frames of a warning that lasts at least 0.3 s. <see cref="float.MaxValue"/> builds it at once.
        /// </summary>
        public void SetFootprint(IReadOnlyList<AttackFootprint.Shape> shapes, Vector2 source, Pattern pattern, Sweep sweep, bool heavy, float budgetMs)
        {
            if (_renderer == null) return;
            var key = KeyOf(shapes, source, pattern, sweep, heavy);
            if (key != _shapeKey || _coveredCount == 0 && IsBuildComplete)
            {
                _shapeKey = key;
                Rebuilds++;
                BeginBuild(shapes, source, pattern, sweep, heavy);
            }

            if (!IsBuildComplete) ContinueBuild(budgetMs);
        }

        /// <summary>Finishes a footprint still being rasterised (tests, tools).</summary>
        public void FinishBuild()
        {
            if (!IsBuildComplete) ContinueBuild(float.MaxValue);
        }

        /// <summary>Milliseconds of rasterisation one frame may spend on one marker.</summary>
        public const float FrameBudgetMs = 2.5f;

        /// <summary>The whole footprint has been rasterised (a heavy one takes a few frames).</summary>
        public bool IsBuildComplete { get; private set; } = true;

        /// <summary>Rasterisation slices run in total (one per frame a build spends time in; diagnostics).</summary>
        public int BuildSlices { get; private set; }

        /// <summary>Frames the last footprint took to rasterise.</summary>
        public int LastBuildFrames { get; private set; }

        private static long KeyOf(IReadOnlyList<AttackFootprint.Shape> shapes, Vector2 source, Pattern pattern, Sweep sweep, bool heavy)
        {
            unchecked
            {
                long h = 17;
                h = h * 31 + (int)pattern;
                h = h * 31 + (int)sweep;
                h = h * 31 + (heavy ? 1 : 0);
                h = h * 31 + Mathf.RoundToInt(source.x * PixelsPerUnit);
                h = h * 31 + Mathf.RoundToInt(source.y * PixelsPerUnit);
                for (var n = 0; n < shapes.Count; n++)
                {
                    var s = shapes[n];
                    h = h * 31 + (int)s.Form;
                    h = h * 31 + Mathf.RoundToInt(s.Start.x * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.Start.y * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.End.x * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.End.y * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.Size.x * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.Size.y * PixelsPerUnit * 4f);
                    h = h * 31 + Mathf.RoundToInt(s.AngleDegrees * 20f);
                }

                return h;
            }
        }

        private static AttackFootprint.Shape Drawn(AttackFootprint.Shape s, Pattern pattern) =>
            pattern == Pattern.CentreLine && s.Form == FootprintForm.Capsule && s.Radius < MinLaneHalfWidth
                ? AttackFootprint.Shape.Capsule(s.Start, s.End, MinLaneHalfWidth, s.Direction)
                : s;

        private readonly List<AttackFootprint.Shape> _shapes = new();
        private Vector2 _source;
        private Pattern _buildPattern;
        private Sweep _buildSweep;
        private int _edgeWidth = 1;
        private int _nextShape;
        private int _nextRow = -1;

        private void BeginBuild(IReadOnlyList<AttackFootprint.Shape> shapes, Vector2 source, Pattern pattern, Sweep sweep, bool heavy)
        {
            const int Ppu = PixelsPerUnit;
            ClearPainted();
            _shapes.Clear();
            _coveredCount = 0;
            _lookKey = int.MinValue;
            LastBuildFrames = 0;
            if (shapes == null || shapes.Count == 0) { IsBuildComplete = true; Hide(); return; }
            for (var n = 0; n < shapes.Count; n++) _shapes.Add(Drawn(shapes[n], pattern));
            _source = source;
            _buildPattern = pattern;
            _buildSweep = sweep;
            _edgeWidth = heavy ? 2 : 1;

            // Bounds of the whole union on the world pixel grid (plus a pixel of slack for the conservative edge), and the
            // farthest reach of the fill, known up front so a footprint built over several frames fills consistently.
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            var reachMax = 0.0001f;
            foreach (var s in _shapes)
            {
                var (lo, hi) = BoundsOf(s);
                minX = Mathf.Min(minX, lo.x); minY = Mathf.Min(minY, lo.y);
                maxX = Mathf.Max(maxX, hi.x); maxY = Mathf.Max(maxY, hi.y);
                reachMax = Mathf.Max(reachMax, ReachOf(s, source, sweep));
            }

            _reachMax = reachMax;
            _x0 = Mathf.FloorToInt(minX * Ppu) - 1;
            _y0 = Mathf.FloorToInt(minY * Ppu) - 1;
            _w = Mathf.CeilToInt(maxX * Ppu) + 1 - _x0;
            _h = Mathf.CeilToInt(maxY * Ppu) + 1 - _y0;
            var size = _w * _h;
            if (_distance.Length < size)
            {
                _distance = new float[size];
                _progress = new byte[size];
                _pattern = new byte[size];
                _band = new byte[size];
                _reach = new float[size];
                _stamp = new int[size];
                _covered = new int[size];
                _texIndex = new int[size];
                _dirtyStamp = new int[size];
                _dirty = new int[size];
                _build = 0;
                _slice = 0;
            }

            // Only the pixels the footprint touches are visited (a stamp per build marks them; nothing is cleared).
            _build++;
            _nextShape = 0;
            _nextRow = -1;
            _indexed = 0;
            IsBuildComplete = false;

            // Texture: grow-only, so a footprint that turns a little does not reallocate every frame.
            var texW = Mathf.Max(8, Mathf.CeilToInt(_w / 32f) * 32);
            var texH = Mathf.Max(8, Mathf.CeilToInt(_h / 32f) * 32);
            if (_texture == null || _texture.width < texW || _texture.height < texH)
            {
                var keepW = _texture != null ? _texture.width : 0;
                var keepH = _texture != null ? _texture.height : 0;
                if (_texture != null) Release(_texture);
                _texture = new Texture2D(Mathf.Max(texW, keepW), Mathf.Max(texH, keepH), TextureFormat.RGBA32, false, false)
                {
                    name = "TelegraphMarker", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                };
                var fresh = _texture.GetPixelData<Color32>(0);
                for (var i = 0; i < fresh.Length; i++) fresh[i] = default;
                _spriteW = -1;
            }

            if (_spriteW != _texture.width || _spriteH != _texture.height)
            {
                if (_sprite != null) Release(_sprite);
                _spriteW = _texture.width;
                _spriteH = _texture.height;
                _sprite = Sprite.Create(_texture, new Rect(0, 0, _spriteW, _spriteH), Vector2.zero, Ppu, 0, SpriteMeshType.FullRect);
                _sprite.hideFlags = HideFlags.DontSave;
                _renderer.sprite = _sprite;
            }

            // The sprite's bottom-left pixel is world pixel (x0, y0): the marker sits on the floor's own grid.
            transform.position = new Vector3((float)_x0 / Ppu, (float)_y0 / Ppu, 0f);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        /// <summary>The farthest fill reach a shape can produce (radial: from the source; along: down its own length).</summary>
        private static float ReachOf(AttackFootprint.Shape s, Vector2 source, Sweep sweep)
        {
            if (sweep == Sweep.Along)
                return s.Form switch
                {
                    FootprintForm.Box => s.Size.x,
                    FootprintForm.Circle => s.Radius * 2f,
                    _ => Vector2.Distance(s.Start, s.End) + s.Radius * 2f
                };

            switch (s.Form)
            {
                case FootprintForm.Circle:
                    return Vector2.Distance(source, s.Centre) + s.Radius;
                case FootprintForm.Box:
                {
                    var hx = s.Direction * (s.Size.x * 0.5f);
                    var hy = new Vector2(-s.Direction.y, s.Direction.x) * (s.Size.y * 0.5f);
                    return Mathf.Max(Mathf.Max(Vector2.Distance(source, s.Centre + hx + hy), Vector2.Distance(source, s.Centre + hx - hy)),
                        Mathf.Max(Vector2.Distance(source, s.Centre - hx + hy), Vector2.Distance(source, s.Centre - hx - hy)));
                }
                default:
                    return Mathf.Max(Vector2.Distance(source, s.Start), Vector2.Distance(source, s.End)) + s.Radius;
            }
        }

        /// <summary>Rasterises shapes/rows until the budget is spent; the rest continues next frame.</summary>
        private void ContinueBuild(float budgetMs)
        {
            const int Ppu = PixelsPerUnit;
            var started = Stopwatch.GetTimestamp();
            var budgetTicks = budgetMs >= float.MaxValue * 0.5f ? long.MaxValue : (long)(budgetMs * Stopwatch.Frequency / 1000.0);
            var radial = _buildSweep == Sweep.Radial;
            float srcX = _source.x, srcY = _source.y;
            var outOfTime = false;
            _slice++;
            _dirtyCount = 0;
            for (var n = _nextShape; n < _shapes.Count && !outOfTime; n++)
            {
                var s = _shapes[n];
                var (lo, hi) = BoundsOf(s);
                // The shape in plain floats: every pixel is projected onto its axis once (along / across) and the exact
                // signed distance, the fill reach and the pattern all come from that one projection.
                var form = s.Form;
                float sx = s.Start.x, sy = s.Start.y, dx = s.Direction.x, dy = s.Direction.y, radius = s.Radius;
                var segment = form == FootprintForm.Capsule ? Vector2.Distance(s.Start, s.End) : 0f;
                float halfL = s.Size.x * 0.5f, halfW = s.Size.y * 0.5f;
                var reachBias = form == FootprintForm.Box ? 0f : radius;
                var rowStart = Mathf.Max(0, Mathf.FloorToInt(lo.y * Ppu) - _y0 - 1);
                var rowEnd = Mathf.Min(_h - 1, Mathf.CeilToInt(hi.y * Ppu) - _y0 + 1);
                if (n == _nextShape && _nextRow >= 0) rowStart = _nextRow;
                for (var row = rowStart; row <= rowEnd; row++)
                {
                    if (budgetTicks != long.MaxValue && Stopwatch.GetTimestamp() - started > budgetTicks)
                    {
                        _nextShape = n;
                        _nextRow = row;
                        outOfTime = true;
                        break;
                    }

                    var wy = (_y0 + row + 0.5f) / Ppu;
                    if (!SpanOf(s, wy, out var xa, out var xb)) continue;
                    var colStart = Mathf.Max(0, Mathf.FloorToInt(xa * Ppu) - _x0 - 1);
                    var colEnd = Mathf.Min(_w - 1, Mathf.CeilToInt(xb * Ppu) - _x0 + 1);
                    var rowBase = row * _w;
                    var ly = wy - sy;
                    for (var col = colStart; col <= colEnd; col++)
                    {
                        var px = (_x0 + col + 0.5f) / Ppu;
                        var lx = px - sx;
                        var along = lx * dx + ly * dy;
                        var across = dx * ly - dy * lx;
                        float d;
                        if (form == FootprintForm.Circle) d = Mathf.Sqrt(lx * lx + ly * ly) - radius;
                        else if (form == FootprintForm.Capsule)
                        {
                            var past = along < 0f ? along : along > segment ? along - segment : 0f;
                            d = Mathf.Sqrt(past * past + across * across) - radius;
                        }
                        else
                        {
                            var a = Mathf.Abs(along - halfL) - halfL;
                            var b = Mathf.Abs(across) - halfW;
                            var oa = a > 0f ? a : 0f;
                            var ob = b > 0f ? b : 0f;
                            var inside = a > b ? a : b;
                            d = Mathf.Sqrt(oa * oa + ob * ob) + (inside < 0f ? inside : 0f);
                        }

                        if (d > Margin) continue;
                        var i = rowBase + col;
                        if (_stamp[i] != _build)
                        {
                            _stamp[i] = _build;
                            _distance[i] = float.MaxValue;
                            _covered[_coveredCount++] = i;
                        }

                        // Progress threshold and pattern come from the shape that owns the pixel most deeply.
                        if (d >= _distance[i]) continue;
                        _distance[i] = d;
                        if (_dirtyStamp[i] != _slice) { _dirtyStamp[i] = _slice; _dirty[_dirtyCount++] = i; }
                        float reach;
                        if (radial) { var rx = px - srcX; var ry = wy - srcY; reach = Mathf.Sqrt(rx * rx + ry * ry); }
                        else { reach = along + reachBias; if (reach < 0f) reach = 0f; }
                        _reach[i] = reach;
                        _pattern[i] = PatternOf(_buildPattern, s, col + _x0, row + _y0, along, across);
                    }
                }
            }

            LastBuildFrames++;
            BuildSlices++;

            // Bands and the quantised progress threshold of the pixels this slice touched or deepened (a later shape may
            // deepen an earlier one's pixel; it is re-classified in the slice that does so), and their texture positions.
            var scale = 255f / _reachMax;
            for (var k = 0; k < _dirtyCount; k++)
            {
                var i = _dirty[k];
                var depth = (Margin - _distance[i]) * Ppu; // pixels in from the drawn edge
                _band[i] = depth < _edgeWidth ? BandEdge : depth < _edgeWidth + 1 ? BandBacking : BandInterior;
                var t = (int)(_reach[i] * scale + 0.5f);
                _progress[i] = (byte)(t < 0 ? 0 : t > 255 ? 255 : t);
            }

            var stride = _texture.width;
            for (var k = _indexed; k < _coveredCount; k++)
            {
                var i = _covered[k];
                var y = i / _w;
                _texIndex[k] = y * stride + (i - y * _w);
            }

            _indexed = _coveredCount;
            if (!outOfTime) IsBuildComplete = true;
            _lookKey = int.MinValue; // what was added must be painted
            LastBuildMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            MaxBuildMs = Math.Max(MaxBuildMs, LastBuildMs);
        }

        /// <summary>Erases what the previous footprint painted (only those pixels) before a new one is laid down.</summary>
        private void ClearPainted()
        {
            if (_texture == null || _paintedCount == 0) { _paintedCount = 0; return; }
            var data = _texture.GetPixelData<Color32>(0);
            for (var k = 0; k < _paintedCount; k++) data[_texIndex[k]] = default;
            _paintedCount = 0; // uploaded with the paint that always follows
        }

        private int _spriteW = -1, _spriteH = -1;
        private float _reachMax = 1f;
        private float[] _reach = Array.Empty<float>();
        private int[] _stamp = Array.Empty<int>();
        private int[] _texIndex = Array.Empty<int>();
        private int _build;
        private int _slice;
        private int[] _dirtyStamp = Array.Empty<int>();
        private int[] _dirty = Array.Empty<int>();
        private int _dirtyCount;
        private int _indexed;
        private int _paintedCount;

        /// <summary>Cost of the last / worst rasterisation and paint in milliseconds (diagnostics).</summary>
        public double LastBuildMs { get; private set; }
        public double MaxBuildMs { get; private set; }
        public double LastPaintMs { get; private set; }
        public double MaxPaintMs { get; private set; }

        private static byte PatternOf(Pattern pattern, AttackFootprint.Shape s, int worldX, int worldY, float along, float across)
        {
            switch (pattern)
            {
                case Pattern.Chevrons:
                {
                    var a = Mathf.FloorToInt(along * PixelsPerUnit);
                    var c = Mathf.FloorToInt(Mathf.Abs(across) * PixelsPerUnit);
                    var half = Mathf.FloorToInt(s.Radius * PixelsPerUnit);
                    if (c > half - 4) return 0;
                    var k = ((a - c) % 16 + 16) % 16;
                    return k < 2 ? PatternChevron : (byte)0;
                }
                case Pattern.CentreLine:
                {
                    var a = Mathf.FloorToInt(along * PixelsPerUnit);
                    return Mathf.Abs(across) * PixelsPerUnit < 1f && ((a % 8) + 8) % 8 < 4 ? PatternCentre : (byte)0;
                }
                default:
                    return ((worldX + worldY) & 7) == 0 ? PatternHatch : (byte)0;
            }
        }

        private static (Vector2 lo, Vector2 hi) BoundsOf(AttackFootprint.Shape s)
        {
            var r = Margin + 1f / PixelsPerUnit;
            switch (s.Form)
            {
                case FootprintForm.Circle:
                    return (s.Centre - Vector2.one * (s.Radius + r), s.Centre + Vector2.one * (s.Radius + r));
                case FootprintForm.Box:
                {
                    var hx = s.Direction * (s.Size.x * 0.5f);
                    var hy = new Vector2(-s.Direction.y, s.Direction.x) * (s.Size.y * 0.5f);
                    var ex = Mathf.Abs(hx.x) + Mathf.Abs(hy.x) + r;
                    var ey = Mathf.Abs(hx.y) + Mathf.Abs(hy.y) + r;
                    return (s.Centre - new Vector2(ex, ey), s.Centre + new Vector2(ex, ey));
                }
                default:
                {
                    var lo = Vector2.Min(s.Start, s.End) - Vector2.one * (s.Radius + r);
                    var hi = Vector2.Max(s.Start, s.End) + Vector2.one * (s.Radius + r);
                    return (lo, hi);
                }
            }
        }

        /// <summary>The x-interval of the row y = <paramref name="y"/> that may lie within the (slightly inflated) shape.</summary>
        private static bool SpanOf(AttackFootprint.Shape s, float y, out float xa, out float xb)
        {
            var r = Margin + 1f / PixelsPerUnit;
            xa = float.MaxValue;
            xb = float.MinValue;
            switch (s.Form)
            {
                case FootprintForm.Circle:
                    Disc(s.Centre, s.Radius + r, y, ref xa, ref xb);
                    break;
                case FootprintForm.Box:
                {
                    var hx = s.Direction * (s.Size.x * 0.5f + r);
                    var hy = new Vector2(-s.Direction.y, s.Direction.x) * (s.Size.y * 0.5f + r);
                    Quad(s.Centre - hx - hy, s.Centre + hx - hy, s.Centre + hx + hy, s.Centre - hx + hy, y, ref xa, ref xb);
                    break;
                }
                default:
                {
                    var n = new Vector2(-s.Direction.y, s.Direction.x) * (s.Radius + r);
                    Quad(s.Start - n, s.End - n, s.End + n, s.Start + n, y, ref xa, ref xb);
                    Disc(s.Start, s.Radius + r, y, ref xa, ref xb);
                    Disc(s.End, s.Radius + r, y, ref xa, ref xb);
                    break;
                }
            }

            return xa <= xb;
        }

        private static void Disc(Vector2 c, float radius, float y, ref float xa, ref float xb)
        {
            var dy = y - c.y;
            var sq = radius * radius - dy * dy;
            if (sq < 0f) return;
            var half = Mathf.Sqrt(sq);
            xa = Mathf.Min(xa, c.x - half);
            xb = Mathf.Max(xb, c.x + half);
        }

        private static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float y, ref float xa, ref float xb)
        {
            Edge(a, b, y, ref xa, ref xb);
            Edge(b, c, y, ref xa, ref xb);
            Edge(c, d, y, ref xa, ref xb);
            Edge(d, a, y, ref xa, ref xb);
        }

        private static void Edge(Vector2 p, Vector2 q, float y, ref float xa, ref float xb)
        {
            if ((y < p.y && y < q.y) || (y > p.y && y > q.y)) return;
            float x;
            if (Mathf.Abs(q.y - p.y) < 0.000001f)
            {
                xa = Mathf.Min(xa, Mathf.Min(p.x, q.x));
                xb = Mathf.Max(xb, Mathf.Max(p.x, q.x));
                return;
            }

            x = p.x + (q.x - p.x) * ((y - p.y) / (q.y - p.y));
            xa = Mathf.Min(xa, x);
            xb = Mathf.Max(xb, x);
        }

        /// <summary>Paints the marker in its current look (only when the quantised look changed) and shows it.</summary>
        public void Paint(in TelegraphLook look)
        {
            if (_renderer == null || _coveredCount == 0 || _texture == null || !IsBuildComplete) { Hide(); return; }
            var progressStep = Mathf.Clamp(Mathf.RoundToInt(look.Progress * 24f), 0, 24);
            var flashStep = Mathf.Clamp(Mathf.CeilToInt(look.Flash * 5f), 0, 5);
            // Built from bytes: no boxing, so an unchanged marker costs nothing per frame.
            var colour = (Color32)look.Colour;
            var key = progressStep | (flashStep << 6) | ((look.Warning ? 1 : 0) << 10) | ((look.Heavy ? 1 : 0) << 11) | ((colour.r ^ (colour.g << 5) ^ (colour.b << 10)) << 12);
            _renderer.enabled = true;
            if (key == _lookKey) return;
            _lookKey = key;
            Repaints++;

            var started = Stopwatch.GetTimestamp();
            var baseColour = look.Colour;
            baseColour.a = 1f;
            var progress = progressStep / 24f;
            var flash = flashStep / 5f;
            var edge = look.Warning ? Color.Lerp(baseColour, Color.white, 0.62f) : baseColour;
            edge.a = look.Warning ? 1f : Mathf.Lerp(0.72f, 1f, progress);
            if (flash > 0f) { edge = Color.Lerp(edge, Color.white, 0.5f * flash); edge.a = 1f; }
            var bright = Color.Lerp(baseColour, Color.white, 0.3f);
            var lit = Color.Lerp(baseColour, Color.white, 0.4f);
            // Every colour a pixel can take this paint, worked out once.
            Color32 edgeC = edge;
            Color32 backingC = new Color(0.06f, 0.04f, 0.05f, 0.55f);
            Color32 frontC = new Color(bright.r, bright.g, bright.b, 0.5f);
            Color32 markedFilledC = new Color(baseColour.r, baseColour.g, baseColour.b, 0.6f);
            Color32 markedC = new Color(baseColour.r, baseColour.g, baseColour.b, 0.28f);
            Color32 fillC = new Color(baseColour.r, baseColour.g, baseColour.b, look.Heavy ? 0.2f : 0.16f);
            Color32 litC = lit;
            var flashAlpha = (byte)Mathf.RoundToInt(255f * 0.5f * flash);
            var threshold = Mathf.RoundToInt(progress * 255f);
            var anyFill = progress > 0f;
            var showFront = progress < 0.999f;
            // The advancing front is a ~3 px line whatever the length of the sweep.
            var frontBand = Mathf.Clamp(Mathf.RoundToInt(255f * (3f / PixelsPerUnit) / Mathf.Max(0.01f, _reachMax)), 2, 40);

            var data = _texture.GetPixelData<Color32>(0);
            for (var k = 0; k < _coveredCount; k++)
            {
                var i = _covered[k];
                Color32 c;
                switch (_band[i])
                {
                    case BandEdge:
                        c = edgeC;
                        break;
                    case BandBacking:
                        c = backingC;
                        break;
                    default:
                    {
                        int t = _progress[i];
                        var filled = anyFill && t <= threshold;
                        if (filled && showFront && t > threshold - frontBand) c = frontC;
                        else if (_pattern[i] != 0) c = filled ? markedFilledC : markedC;
                        else c = filled ? fillC : default;
                        if (flashAlpha > 0) c = new Color32(litC.r, litC.g, litC.b, c.a > flashAlpha ? c.a : flashAlpha);
                        break;
                    }
                }

                data[_texIndex[k]] = c;
            }

            _paintedCount = _coveredCount;
            _texture.Apply(false, false);
            LastPaintMs = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            MaxPaintMs = Math.Max(MaxPaintMs, LastPaintMs);
        }

        private void OnDestroy()
        {
            Release(_sprite);
            Release(_texture);
        }

        private static void Release(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }
    }
}
