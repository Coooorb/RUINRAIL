using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Paints one room's environment layer, pass by pass, in a fixed order (later passes sit on earlier ones):
    /// slab seams → mottle → wear lanes → stains → biome markings → cracks → story flats → wall patches → edge creep →
    /// bleed → grit → contact shadows → clutter → story props → lamp pools; then the wall face. Every random draw comes from the room's seeded stream
    /// in that order, so the result is a pure function of (room, run seed, depth, node id).
    /// </summary>
    internal sealed class RoomEnvironmentPainter
    {
        private const int T = RoomEnvironmentMap.Tile;
        private enum Side { N = 0, E = 1, S = 2, W = 3 }

        private readonly RoomEnvironmentMap _m;
        private readonly RoomEnvironmentStyle _s;
        private readonly SeededRandom _rng;
        private readonly RoomEnvironmentCanvas _f;
        private readonly RoomEnvironmentCanvas _w;
        private readonly List<RectInt> _placed = new();
        private readonly List<(int X, int Y, int R, Color32 C)> _lamps = new();
        private List<Vector2Int> _story = new();
        private Side _storySide;
        private uint _seed;
        private Side _damp;

        public int MergedCells { get; private set; }
        public int ClutterPlaced { get; private set; }
        public int GlowsPlaced => _lamps.Count;
        public string Vignette { get; private set; } = string.Empty;
        public bool SeamsRepaired { get; private set; }
        public readonly HashSet<Vector2Int> ClutterCells = new();

        public RoomEnvironmentPainter(RoomEnvironmentMap map, RoomEnvironmentStyle style, SeededRandom rng, RoomEnvironmentCanvas floor, RoomEnvironmentCanvas walls)
        {
            _m = map;
            _s = style;
            _rng = rng;
            _f = floor;
            _w = walls;
            var n = map.W * map.H;
            _decalGate = new float[n];
            _floorGate = new float[n];
            _patchGate = new float[n];
            _clutterGate = new float[n];
            var wallGate = new float[n];
            for (var y = 0; y < map.H; y++)
            for (var x = 0; x < map.W; x++)
            {
                var i = map.I(x, y);
                var decal = map.Decal(x, y);
                _floorGate[i] = decal ? 1f : 0f;
                _decalGate[i] = !decal ? 0f : map.Core(x, y) ? RoomEnvironmentDressing.CoreAlphaScale : 1f;
                _patchGate[i] = decal && !map.Door[i] && !map.MarkerZone[i] && map.Dist[i] <= RoomEnvironmentDressing.ClutterMaxWallDistance ? 1f : 0f;
                _clutterGate[i] = map.Clutter(x, y) ? 1f : 0f;
                wallGate[i] = map.WallPaint(x, y) ? 1f : 0f;
            }

            _w.Gate = wallGate;
        }

        private readonly float[] _decalGate, _floorGate, _patchGate, _clutterGate;

        public void PaintAll()
        {
            _seed = (uint)_rng.NextInt(int.MaxValue);
            _damp = (Side)_rng.NextInt(4);
            ChooseStorySite();

            SeamRepair();
            UseGate(_decalGate);
            Mottle();
            WearLanes();
            Stains();
            Markings();
            Cracks();
            StoryFlats();
            UseGate(_patchGate);
            WallPatches();
            UseGate(_floorGate);
            EdgeCreep();
            Bleed();
            Grit();
            Shadows();
            UseGate(_clutterGate);
            Clutter();
            StoryProps();
            UseGate(_floorGate);
            Lamps();
            WallFace();
        }

        // ================================================================ gates

        private void UseGate(float[] gate) => _f.Gate = gate;



        // ================================================================ helpers

        private int Next(int max) => max <= 1 ? 0 : _rng.NextInt(max);
        private float NextF() => _rng.NextFloat();
        private bool Chance(float p) => _rng.NextFloat() < p;

        private int OpenCount()
        {
            var n = 0;
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
                if (_m.Decal(x, y)) n++;
            return n;
        }

        private readonly List<Vector2Int> _cellsScratch = new();

        /// <summary>A random cell satisfying <paramref name="ok"/>, the nearer-to-a-wall of two draws when biased.</summary>
        private Vector2Int? PickCell(Func<int, int, bool> ok, bool biasWalls)
        {
            _cellsScratch.Clear();
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
                if (ok(x, y)) _cellsScratch.Add(new Vector2Int(x, y));
            if (_cellsScratch.Count == 0) return null;
            var a = _cellsScratch[Next(_cellsScratch.Count)];
            if (!biasWalls) return a;
            var b = _cellsScratch[Next(_cellsScratch.Count)];
            return _m.DistAt(a.x, a.y) <= _m.DistAt(b.x, b.y) ? a : b;
        }

        private bool SolidOn(int cx, int cy, Side side) => side switch
        {
            Side.N => _m.Solid(cx, cy + 1),
            Side.E => _m.Solid(cx + 1, cy),
            Side.S => _m.Solid(cx, cy - 1),
            _ => _m.Solid(cx - 1, cy)
        };

        /// <summary>Pixel distance to the nearest wall edge within one cell, and which side it is on (4 = a corner).</summary>
        private float WallEdge(int px, int py, out int side)
        {
            int lx = px % T, ly = py % T;
            var mask = _m.Around[_m.I(px / T, py / T)];
            var best = 999f;
            side = -1;
            if ((mask & RoomEnvironmentMap.BitN) != 0 && T - 1 - ly < best) { best = T - 1 - ly; side = 0; }
            if ((mask & RoomEnvironmentMap.BitE) != 0 && T - 1 - lx < best) { best = T - 1 - lx; side = 1; }
            if ((mask & RoomEnvironmentMap.BitS) != 0 && ly < best) { best = ly; side = 2; }
            if ((mask & RoomEnvironmentMap.BitW) != 0 && lx < best) { best = lx; side = 3; }
            if ((mask & RoomEnvironmentMap.Corners) == 0 || best < 2f) return best;
            float rx = T - 1 - lx, ty = T - 1 - ly;
            if ((mask & RoomEnvironmentMap.BitNW) != 0) { var d = Mathf.Sqrt(lx * lx + ty * ty); if (d < best) { best = d; side = 4; } }
            if ((mask & RoomEnvironmentMap.BitNE) != 0) { var d = Mathf.Sqrt(rx * rx + ty * ty); if (d < best) { best = d; side = 4; } }
            if ((mask & RoomEnvironmentMap.BitSW) != 0) { var d = Mathf.Sqrt(lx * lx + ly * ly); if (d < best) { best = d; side = 4; } }
            if ((mask & RoomEnvironmentMap.BitSE) != 0) { var d = Mathf.Sqrt(rx * rx + ly * ly); if (d < best) { best = d; side = 4; } }
            return best;
        }

        private float ObstacleEdge(int px, int py)
        {
            int lx = px % T, ly = py % T;
            var mask = _m.ObstacleAround[_m.I(px / T, py / T)];
            var best = 999f;
            if ((mask & RoomEnvironmentMap.BitN) != 0) best = Mathf.Min(best, T - 1 - ly);
            if ((mask & RoomEnvironmentMap.BitE) != 0) best = Mathf.Min(best, T - 1 - lx);
            if ((mask & RoomEnvironmentMap.BitS) != 0) best = Mathf.Min(best, ly);
            if ((mask & RoomEnvironmentMap.BitW) != 0) best = Mathf.Min(best, lx);
            return best;
        }

        private static float Ramp(float t, float length) => t >= length ? 0f : Mathf.Pow(1f - t / length, 1.2f);

        /// <summary>The pixel just inside <paramref name="cell"/> against its wall on <paramref name="side"/>, at <paramref name="inset"/> px.</summary>
        private static Vector2Int Hug(Vector2Int cell, Side side, int inset, int along)
        {
            int x0 = cell.x * T, y0 = cell.y * T;
            return side switch
            {
                Side.N => new Vector2Int(x0 + along, y0 + T - 1 - inset),
                Side.S => new Vector2Int(x0 + along, y0 + inset),
                Side.E => new Vector2Int(x0 + T - 1 - inset, y0 + along),
                _ => new Vector2Int(x0 + inset, y0 + along)
            };
        }

        private Side? WallSideOf(int cx, int cy)
        {
            var sides = new List<Side>();
            foreach (Side s in Enum.GetValues(typeof(Side))) if (SolidOn(cx, cy, s)) sides.Add(s);
            if (sides.Count == 0) return null;
            return sides[Next(sides.Count)];
        }

        // ================================================================ 1. slab seams

        private void SeamRepair()
        {
            if (!RoomEnvironmentTileReader.Available) return;
            var slab = new int[_m.W * _m.H];
            for (var i = 0; i < slab.Length; i++) slab[i] = -1;

            bool Mergeable(int x, int y)
            {
                if (!_m.Decal(x, y)) return false;
                var i = _m.I(x, y);
                return _m.FloorIdentity[i] && _m.FloorSprite[i] != null && RoomEnvironmentTileReader.Read(_m.FloorSprite[i]) != null;
            }

            bool Fits(int x, int y, int w, int h, Sprite sprite)
            {
                for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                {
                    if (!_m.InRoom(xx, yy) || !Mergeable(xx, yy) || slab[_m.I(xx, yy)] >= 0) return false;
                    if (_m.FloorSprite[_m.I(xx, yy)] != sprite) return false;
                }

                return true;
            }

            var total = 0;
            foreach (var shape in _s.Slabs) total += shape.Weight;
            var id = 0;
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
            {
                if (slab[_m.I(x, y)] >= 0 || !Mergeable(x, y)) continue;
                var roll = Next(total);
                var pick = _s.Slabs[0];
                foreach (var shape in _s.Slabs) { if (roll < shape.Weight) { pick = shape; break; } roll -= shape.Weight; }
                var sprite = _m.FloorSprite[_m.I(x, y)];
                foreach (var (w, h) in new[] { (pick.W, pick.H), (2, 1), (1, 2), (1, 1) })
                {
                    if (!Fits(x, y, w, h, sprite)) continue;
                    for (var yy = y; yy < y + h; yy++)
                    for (var xx = x; xx < x + w; xx++)
                        slab[_m.I(xx, yy)] = id;
                    id++;
                    break;
                }
            }

            const int lo = 2, hi = T - 3;
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
            {
                var me = slab[_m.I(x, y)];
                if (me < 0) continue;
                var left = _m.InRoom(x - 1, y) && slab[_m.I(x - 1, y)] == me;
                var right = _m.InRoom(x + 1, y) && slab[_m.I(x + 1, y)] == me;
                var down = _m.InRoom(x, y - 1) && slab[_m.I(x, y - 1)] == me;
                var up = _m.InRoom(x, y + 1) && slab[_m.I(x, y + 1)] == me;
                if (!left && !right && !down && !up) continue;
                var tile = RoomEnvironmentTileReader.Read(_m.FloorSprite[_m.I(x, y)]);
                MergedCells++;
                for (var ly = 0; ly < T; ly++)
                for (var lx = 0; lx < T; lx++)
                {
                    var sx = lx;
                    var sy = ly;
                    if (left && lx < lo) sx = lo;
                    if (right && lx > hi) sx = hi;
                    if (down && ly < lo) sy = lo;
                    if (up && ly > hi) sy = hi;
                    if (sx == lx && sy == ly) continue;
                    _f.Set(x * T + lx, y * T + ly, tile[sy * T + sx]);
                }
            }

            SeamsRepaired = MergedCells > 0;
        }

        // ================================================================ 1b. mottle

        /// <summary>
        /// Large, soft tonal patches across the whole floor (2x2-pixel cells of low-frequency noise): the slow variation
        /// of a real surface that breaks the tile repeat even in the open middle of a room, at a contrast far below
        /// anything that moves.
        /// </summary>
        private void Mottle()
        {
            var seed = _seed + 151u;
            var dark = _s.EdgeCreepIsLight ? _s.Stain : _s.Grime;
            for (var by = 0; by < _f.Height; by += 2)
            for (var bx = 0; bx < _f.Width; bx += 2)
            {
                if (!_m.Decal(bx / T, by / T)) continue;
                if (_f.GateAt(bx, by) <= 0f) continue;
                var v = Value(bx, by, 72f, seed) * 0.75f + Value(bx, by, 24f, seed + 1u) * 0.25f;
                Color32 color;
                float alpha;
                if (v < 0.42f) { color = dark; alpha = 0.28f * Bands((0.42f - v) / 0.42f * 1.8f, bx, by, 2, seed + 2u); }
                else if (v > 0.6f) { color = _s.Wear; alpha = 0.15f * Bands((v - 0.6f) / 0.4f * 1.8f, bx, by, 2, seed + 3u); }
                else continue;
                if (alpha <= 0f) continue;
                _f.Over(bx, by, color, alpha);
                _f.Over(bx + 1, by, color, alpha);
                _f.Over(bx, by + 1, color, alpha);
                _f.Over(bx + 1, by + 1, color, alpha);
            }
        }

        // ================================================================ 2. wear lanes

        private void WearLanes()
        {
            var centre = new Vector2(_m.W * T * 0.5f, _m.H * T * 0.5f);
            foreach (var socket in _m.Sockets)
            {
                var cells = socket.Cells();
                if (cells.Length == 0) continue;
                var door = Vector2.zero;
                foreach (var c in cells) door += new Vector2(c.x * T + T * 0.5f, c.y * T + T * 0.5f);
                door /= cells.Length;
                var half = 13f + Next(9);
                var seed = _seed + 11u + (uint)socket.Cell.x * 31u + (uint)socket.Cell.y;
                var min = Vector2.Min(door, centre) - Vector2.one * half;
                var max = Vector2.Max(door, centre) + Vector2.one * half;
                for (var y = Mathf.Max(0, (int)min.y) & ~1; y < Mathf.Min(_f.Height, (int)max.y); y += 2)
                for (var x = Mathf.Max(0, (int)min.x) & ~1; x < Mathf.Min(_f.Width, (int)max.x); x += 2)
                {
                    var d = DistanceToSegment(new Vector2(x, y), door, centre);
                    if (d > half) continue;
                    var s = (1f - d / half) * (0.35f + 0.9f * Value(x, y, 11f, seed));
                    var a = 0.1f * Bands(Mathf.Clamp01(s), x, y, 3, seed);
                    if (a <= 0f) continue;
                    _f.Over(x, y, _s.Wear, a);
                    _f.Over(x + 1, y, _s.Wear, a);
                    _f.Over(x, y + 1, _s.Wear, a);
                    _f.Over(x + 1, y + 1, _s.Wear, a);
                }

                // Scuffs along the lane: short strokes in the walking direction.
                var dir = (centre - door).normalized;
                var length = (centre - door).magnitude;
                for (var i = 0; i < 4 + (int)(length / 40f); i++)
                {
                    var p = door + dir * (NextF() * length) + new Vector2(-dir.y, dir.x) * ((NextF() - 0.5f) * half * 1.4f);
                    var q = p + dir * (3 + Next(4));
                    _f.Line((int)p.x, (int)p.y, (int)q.x, (int)q.y, _s.Wear, 0.16f);
                }
            }
        }

        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = ab.sqrMagnitude < 0.001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        // ================================================================ 3. stains

        private void Stains()
        {
            var count = Mathf.Clamp(Mathf.RoundToInt(OpenCount() * 0.07f * _s.StainDensity), 3, 42);
            for (var i = 0; i < count; i++)
            {
                var cell = PickCell(_m.Decal, biasWalls: true);
                if (cell == null) return;
                var core = _m.Core(cell.Value.x, cell.Value.y);
                var r = core ? 5 + Next(8) : 7 + Next(17);
                var cx = cell.Value.x * T + Next(T);
                var cy = cell.Value.y * T + Next(T);
                var second = Chance(0.35f);
                var color = second ? _s.Stain2 : _s.Stain;
                var alpha = core ? 0.18f : 0.26f + NextF() * 0.16f;
                var seed = _seed + 101u * (uint)(i + 1);
                var sheen = _s.Biome == Biome.Rustworks && !second;
                for (var y = cy - r - 4; y <= cy + r + 4; y++)
                for (var x = cx - r - 4; x <= cx + r + 4; x++)
                {
                    var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / r;
                    var edge = 0.6f + 0.55f * Fbm(x, y, 5f, seed);
                    if (d > edge) continue;
                    var inner = d < edge * 0.55f;
                    _f.Over(x, y, color, alpha * (inner ? 1f : 0.55f));
                    if (sheen && inner && (Hash(x, y, seed) % 13u) == 0) _f.Over(x, y, _s.CrackLight, 0.28f);
                }
            }
        }

        // ================================================================ 4. biome markings

        private static readonly Dictionary<char, string> Glyphs = new()
        {
            { '0', "111101101101111" }, { '1', "010110010010111" }, { '2', "111001111100111" }, { '3', "111001111001111" },
            { '4', "101101111001001" }, { '5', "111100111001111" }, { '6', "111100111101111" }, { '7', "111001001001001" },
            { '8', "111101111101111" }, { '9', "111101111001111" }, { 'A', "010101111101101" }, { 'B', "110101110101110" },
            { 'C', "111100100100111" }, { 'D', "110101101101110" }, { 'E', "111100111100111" }, { 'F', "111100111100100" },
            { '-', "000000111000000" }
        };

        /// <summary>A worn floor stencil, 2 px per glyph pixel, top-left at (x, y).</summary>
        private void Stencil(string text, int x, int y, Color32 color, float alpha, uint seed)
        {
            var pen = x;
            foreach (var ch in text)
            {
                if (!Glyphs.TryGetValue(ch, out var bits)) { pen += 8; continue; }
                for (var r = 0; r < 5; r++)
                for (var c = 0; c < 3; c++)
                {
                    if (bits[r * 3 + c] != '1') continue;
                    for (var sy = 0; sy < 2; sy++)
                    for (var sx = 0; sx < 2; sx++)
                    {
                        var px = pen + c * 2 + sx;
                        var py = y - r * 2 - sy;
                        if (Fbm(px, py, 3f, seed) < 0.3f) continue; // worn away
                        _f.Over(px, py, color, alpha);
                    }
                }

                pen += 8;
            }
        }

        private List<Vector2Int> RingCells(Side side)
        {
            var cells = new List<Vector2Int>();
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
                if (_m.Decal(x, y) && !_m.Door[_m.I(x, y)] && SolidOn(x, y, side)) cells.Add(new Vector2Int(x, y));
            return cells;
        }

        private Side LongestSide(params Side[] sides)
        {
            var best = sides[0];
            var bestCount = -1;
            foreach (var s in sides)
            {
                var n = RingCells(s).Count;
                if (n > bestCount) { best = s; bestCount = n; }
            }

            return best;
        }

        /// <summary>A painted band parallel to the wall on <paramref name="side"/>, between two insets, with a pattern.</summary>
        private void Band(IEnumerable<Vector2Int> cells, Side side, int from, int to, Func<int, int, int, Color32?> pattern, float alpha, uint seed)
        {
            foreach (var cell in cells)
            for (var along = 0; along < T; along++)
            for (var inset = from; inset <= to; inset++)
            {
                var p = Hug(cell, side, inset, along);
                var color = pattern(p.x, p.y, inset - from);
                if (color == null) continue;
                var wear = Fbm(p.x, p.y, 7f, seed);
                _f.Over(p.x, p.y, color.Value, alpha * (wear < 0.32f ? 0.25f : 1f));
            }
        }

        private string StencilText()
        {
            string Digit() => ((char)('0' + Next(10))).ToString();
            string Letter() => ((char)('A' + Next(6))).ToString();
            return _s.Biome switch
            {
                Biome.Rustworks => Letter() + Digit(),
                Biome.OvergrownLabs => Letter() + "-" + Digit(),
                Biome.CryoVaults => Letter() + Digit() + Digit(),
                _ => "0" + Digit()
            };
        }

        private void Markings()
        {
            var seed = _seed + 401u;
            switch (_s.Biome)
            {
                case Biome.RuinedMetro:
                {
                    // Platform edge: a tactile safety strip along the longest horizontal wall.
                    var side = LongestSide(Side.N, Side.S);
                    Band(RingCells(side), side, 5, 11, (x, y, k) => k == 0 || k == 6 ? Shade(_s.Paint, 0.8f) : ((x / 2 + y / 2) & 1) == 0 ? _s.Paint : Shade(_s.Paint, 0.75f), 0.5f, seed);
                    break;
                }
                case Biome.Rustworks:
                {
                    // Faded hazard stripes along one or two wall bays.
                    foreach (var side in new[] { LongestSide(Side.N, Side.S), LongestSide(Side.E, Side.W) })
                    {
                        var ring = RingCells(side);
                        if (ring.Count < 3) continue;
                        var start = Next(ring.Count - 2);
                        var run = ring.GetRange(start, Mathf.Min(ring.Count - start, 2 + Next(4)));
                        Band(run, side, 2, 8, (x, y, _) => ((x + y) / 4 & 1) == 0 ? _s.Paint : _s.Paint2, 0.34f, seed + (uint)side);
                    }

                    break;
                }
                case Biome.OvergrownLabs:
                {
                    var side = LongestSide(Side.N, Side.S, Side.E, Side.W);
                    Band(RingCells(side), side, 12, 13, (_, _, _) => _s.Paint, 0.36f, seed);
                    break;
                }
                default:
                {
                    // Cold-storage guide lines: two thin pale lines along one horizontal and one vertical wall.
                    foreach (var side in new[] { LongestSide(Side.N, Side.S), LongestSide(Side.E, Side.W) })
                        Band(RingCells(side), side, 14, 17, (_, _, k) => k == 0 || k == 3 ? _s.Paint : (Color32?)null, 0.3f, seed + (uint)side);
                    break;
                }
            }

            // A worn bay / platform / sector stencil against a wall, away from doors and markers.
            var at = PickCell((x, y) => _m.Decal(x, y) && !_m.Door[_m.I(x, y)] && !_m.MarkerZone[_m.I(x, y)] && _m.DistAt(x, y) == 1 && SolidOn(x, y, Side.N), false);
            if (at != null)
            {
                var color = _s.Biome == Biome.CryoVaults ? _s.Paint2 : _s.Biome == Biome.OvergrownLabs ? _s.Paint : _s.Biome == Biome.Rustworks ? _s.Paint : _s.Paint2;
                Stencil(StencilText(), at.Value.x * T + 4, at.Value.y * T + T - 8, color, 0.36f, seed + 7u);
            }
        }

        // ================================================================ 5. cracks

        private void Cracks()
        {
            var factor = _s.Biome switch { Biome.RuinedMetro => 1.25f, Biome.Rustworks => 0.75f, Biome.CryoVaults => 0.95f, _ => 1f };
            var count = Mathf.Clamp(Mathf.RoundToInt(OpenCount() * 0.04f * factor), 2, 34);
            for (var i = 0; i < count; i++)
            {
                var cell = PickCell(_m.Decal, biasWalls: true);
                if (cell == null) return;
                var core = _m.Core(cell.Value.x, cell.Value.y);
                var length = core ? 8 + Next(10) : 12 + Next(32);
                var x = cell.Value.x * T + Next(T);
                var y = cell.Value.y * T + Next(T);
                var scratch = _s.Biome == Biome.Rustworks && Chance(0.6f);
                Crack(x, y, NextF() * Mathf.PI * 2f, length, core ? 0.36f : 0.55f, true, scratch);
            }
        }

        private void Crack(float x, float y, float angle, int length, float alpha, bool branch, bool scratch)
        {
            var frost = _s.Biome == Biome.CryoVaults;
            var main = scratch || frost ? _s.CrackLight : _s.Crack;
            var edge = scratch || frost ? _s.Crack : _s.CrackLight;
            var jitter = scratch ? 0.12f : 0.75f;
            for (var step = 0; step < length; step++)
            {
                x += Mathf.Cos(angle);
                y += Mathf.Sin(angle);
                angle += (NextF() - 0.5f) * jitter;
                var px = Mathf.RoundToInt(x);
                var py = Mathf.RoundToInt(y);
                _f.Over(px + 1, py - 1, edge, scratch ? 0.12f : 0.18f);
                _f.Over(px, py, main, scratch ? alpha * 0.45f : frost ? alpha * 0.7f : alpha);
                if (branch && !scratch && Chance(0.05f)) Crack(x, y, angle + (Chance(0.5f) ? 0.9f : -0.9f), length / 2, alpha * 0.8f, false, false);
            }
        }

        // ================================================================ story site

        private RoomMarkerRole[] AnchorRoles() => _m.Type switch
        {
            RoomType.Merchant => new[] { RoomMarkerRole.MerchantAnchor, RoomMarkerRole.InteractableSpawn },
            RoomType.Event => new[] { RoomMarkerRole.EventAnchor, RoomMarkerRole.InteractableSpawn },
            RoomType.MedicalRecovery => new[] { RoomMarkerRole.InteractableSpawn, RoomMarkerRole.EventAnchor },
            RoomType.Start => new[] { RoomMarkerRole.PlayerSpawn },
            RoomType.Treasure or RoomType.Loot => new[] { RoomMarkerRole.ChestSpawn, RoomMarkerRole.LootSpawn },
            RoomType.Boss => new[] { RoomMarkerRole.BossAnchor },
            _ => Array.Empty<RoomMarkerRole>()
        };

        /// <summary>
        /// The wall run (2–4 cells along one wall) the room's story is told on: next to the category's anchor when it has
        /// one, otherwise the run farthest from every door. Empty when no run is free.
        /// </summary>
        private void ChooseStorySite()
        {
            Vector2? anchor = null;
            foreach (var role in AnchorRoles())
            {
                foreach (var marker in _m.Markers)
                    if (marker.Role == role) { anchor = marker.Rect.center; break; }
                if (anchor != null) break;
            }

            var bestScore = float.MinValue;
            foreach (Side side in Enum.GetValues(typeof(Side)))
            {
                var ring = new List<Vector2Int>();
                for (var y = 0; y < _m.H; y++)
                for (var x = 0; x < _m.W; x++)
                    if (_m.Clutter(x, y) && _m.DistAt(x, y) == 1 && SolidOn(x, y, side)) ring.Add(new Vector2Int(x, y));
                for (var i = 0; i < ring.Count; i++)
                {
                    var run = new List<Vector2Int> { ring[i] };
                    for (var j = i + 1; j < ring.Count && run.Count < 4; j++)
                    {
                        var last = run[run.Count - 1];
                        var step = ring[j] - last;
                        if (step == Vector2Int.right || step == Vector2Int.up) run.Add(ring[j]);
                        else if ((side is Side.N or Side.S) && step.y != 0) break;
                    }

                    if (run.Count < 2) continue;
                    var mid = (Vector2)(run[0] + run[run.Count - 1]) * 0.5f + Vector2.one * 0.5f;
                    var doorDistance = 99f;
                    foreach (var socket in _m.Sockets) doorDistance = Mathf.Min(doorDistance, Vector2.Distance(mid, socket.Cell));
                    var score = run.Count * 2f + Mathf.Min(doorDistance, 8f) + White(i, (int)side, _seed) * 1.5f;
                    if (anchor != null) score -= Vector2.Distance(mid, anchor.Value) * 1.6f;
                    if (score > bestScore) { bestScore = score; _story = run; _storySide = side; }
                }
            }

            if (_story.Count > 0) Vignette = _m.Type.ToString();
        }

        private Vector2Int StoryPixel(int index, int inset, float along)
        {
            var cell = _story[Mathf.Clamp(index, 0, _story.Count - 1)];
            return Hug(cell, _storySide, inset, Mathf.Clamp((int)(along * T), 0, T - 1));
        }

        // ================================================================ 6. story flats

        private void StoryFlats()
        {
            switch (_m.Type)
            {
                case RoomType.Boss:
                    for (var i = 0; i < 4; i++)
                    {
                        var c = PickCell((x, y) => _m.Decal(x, y) && _m.DistAt(x, y) == 1, false);
                        if (c != null) Crack(c.Value.x * T + 16, c.Value.y * T + 16, NextF() * Mathf.PI * 2f, 40 + Next(40), 0.5f, true, false);
                    }

                    for (var i = 0; i < 2; i++)
                    {
                        var c = PickCell((x, y) => _m.Decal(x, y) && _m.DistAt(x, y) <= 2 && !_m.MarkerZone[_m.I(x, y)], false);
                        if (c != null) Scorch(c.Value.x * T + 16, c.Value.y * T + 16, 16 + Next(10));
                    }

                    break;
                case RoomType.Combat:
                    if (Chance(0.6f))
                    {
                        var c = PickCell((x, y) => _m.Decal(x, y) && _m.DistAt(x, y) == 1 && !_m.Door[_m.I(x, y)], false);
                        if (c != null) Scorch(c.Value.x * T + 16, c.Value.y * T + 16, 9 + Next(6));
                    }

                    break;
                case RoomType.Event when _story.Count > 0:
                {
                    var p = StoryPixel(_story.Count / 2, 14, 0.5f);
                    Scorch(p.x, p.y, 18 + Next(8));
                    break;
                }
                case RoomType.Merchant when _story.Count >= 2:
                    Rug();
                    break;
                case RoomType.MedicalRecovery when _story.Count > 0:
                    ClinicalPatch();
                    break;
                case RoomType.Treasure:
                    VaultFrame();
                    break;
                case RoomType.Start:
                    Footprints();
                    break;
            }
        }

        private void Scorch(int cx, int cy, int r)
        {
            var seed = _seed + (uint)(cx * 7 + cy * 13);
            for (var y = cy - r * 2; y <= cy + r * 2; y++)
            for (var x = cx - r * 2; x <= cx + r * 2; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var angle = Mathf.Atan2(dy, dx);
                var ray = Value(angle * 9f + 20f, 0f, 1f, seed); // soot streaks radiate unevenly
                var reach = r * (0.75f + 0.9f * ray);
                if (d > reach) continue;
                var s = 1f - d / reach;
                _f.Over(x, y, _s.Shadow, 0.55f * Bands(Mathf.Clamp01(s * 1.3f), x, y, 3, seed));
            }
        }

        private void Rug()
        {
            var a = StoryPixel(0, 4, 0.1f);
            var b = StoryPixel(_story.Count - 1, 26, 0.9f);
            var rect = RectFrom(a, b);
            var baseColor = _s.Biome switch
            {
                Biome.Rustworks => Hex("#4E3A2A"), Biome.OvergrownLabs => Hex("#3E5560"), Biome.CryoVaults => Hex("#5A4636"), _ => Hex("#5B3A2E")
            };
            var stripe = Shade(_s.Accent, 0.85f);
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
            {
                var edge = Mathf.Min(Mathf.Min(x - rect.xMin, rect.xMax - 1 - x), Mathf.Min(y - rect.yMin, rect.yMax - 1 - y));
                var color = edge == 0 ? Shade(baseColor, 0.7f) : edge == 2 ? stripe : (x + y) % 6 == 0 ? Shade(baseColor, 1.15f) : baseColor;
                var worn = Fbm(x, y, 4f, _seed + 7u) < 0.25f ? 0.6f : 1f;
                _f.Over(x, y, color, 0.82f * worn);
            }

            // Fringe on the short ends.
            var vertical = rect.height > rect.width;
            for (var i = 0; i < (vertical ? rect.width : rect.height); i += 2)
            {
                if (vertical)
                {
                    _f.Over(rect.xMin + i, rect.yMin - 1, stripe, 0.7f);
                    _f.Over(rect.xMin + i, rect.yMax, stripe, 0.7f);
                }
                else
                {
                    _f.Over(rect.xMin - 1, rect.yMin + i, stripe, 0.7f);
                    _f.Over(rect.xMax, rect.yMin + i, stripe, 0.7f);
                }
            }
        }

        private void ClinicalPatch()
        {
            var a = StoryPixel(0, 2, 0f);
            var b = StoryPixel(_story.Count - 1, 29, 1f);
            var rect = RectFrom(a, b);
            var clean = _s.Biome == Biome.OvergrownLabs ? Hex("#E4ECE6") : Hex("#C9D6D2");
            var line = Hex("#6FA79A");
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
            {
                var border = x == rect.xMin || y == rect.yMin || x == rect.xMax - 1 || y == rect.yMax - 1;
                _f.Over(x, y, border ? line : clean, border ? 0.32f : (x % 8 == 0 || y % 8 == 0) ? 0.14f : 0.09f);
            }
        }

        private void VaultFrame()
        {
            foreach (var marker in _m.Markers)
            {
                if (marker.Role != RoomMarkerRole.ChestSpawn) continue;
                var r = marker.Rect;
                var rect = new RectInt((r.xMin - 1) * T + 6, (r.yMin - 1) * T + 6, (r.width + 2) * T - 12, (r.height + 2) * T - 12);
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    if ((x / 4 & 1) == 0) continue;
                    _f.Over(x, rect.yMin, _s.Paint2, 0.3f);
                    _f.Over(x, rect.yMax - 1, _s.Paint2, 0.3f);
                }

                for (var y = rect.yMin; y < rect.yMax; y++)
                {
                    if ((y / 4 & 1) == 0) continue;
                    _f.Over(rect.xMin, y, _s.Paint2, 0.3f);
                    _f.Over(rect.xMax - 1, y, _s.Paint2, 0.3f);
                }
            }
        }

        private void Footprints()
        {
            RoomMarker spawn = null;
            foreach (var marker in _m.Markers) if (marker.Role == RoomMarkerRole.PlayerSpawn) { spawn = marker; break; }
            if (spawn == null || _m.Sockets.Count == 0) return;
            var from = (spawn.Rect.center) * T;
            var to = Vector2.zero;
            var best = float.MaxValue;
            foreach (var socket in _m.Sockets)
            {
                var p = ((Vector2)socket.Cell + Vector2.one * 0.5f) * T;
                var d = Vector2.Distance(p, from);
                if (d < best) { best = d; to = p; }
            }

            var dir = (to - from).normalized;
            var side = new Vector2(-dir.y, dir.x);
            var length = Vector2.Distance(from, to);
            for (float t = 20f, k = 0; t < length - 10f; t += 7f, k++)
            {
                var p = from + dir * t + side * (k % 2 == 0 ? 3f : -3f);
                _f.Rect((int)p.x, (int)p.y, 2, 3, _s.Shadow, 0.18f);
            }
        }

        private static RectInt RectFrom(Vector2Int a, Vector2Int b) =>
            new(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Abs(a.x - b.x) + 1, Mathf.Abs(a.y - b.y) + 1);

        // ================================================================ 7. edge creep

        private void EdgeCreep()
        {
            var seed = _seed + 701u;
            for (var cy = 0; cy < _m.H; cy++)
            for (var cx = 0; cx < _m.W; cx++)
            {
                if (!_m.Decal(cx, cy)) continue;
                var nearWall = _m.DistAt(cx, cy) <= 1 || _m.Solid(cx + 1, cy + 1) || _m.Solid(cx - 1, cy + 1) || _m.Solid(cx + 1, cy - 1) || _m.Solid(cx - 1, cy - 1);
                var nearObstacle = _m.ObstacleAdjacent(cx, cy);
                if (!nearWall && !nearObstacle) continue;
                var core = _m.Core(cx, cy);
                for (var ly = 0; ly < T; ly += 2)
                for (var lx = 0; lx < T; lx += 2)
                {
                    var x = cx * T + lx;
                    var y = cy * T + ly;
                    var s = 0f;
                    if (nearWall)
                    {
                        var e = WallEdge(x, y, out var side);
                        var factor = side == (int)_damp ? 1.55f : side == 4 ? 1.3f : 0.85f;
                        var width = (8f + 20f * Value(x, y, 22f, seed)) * factor;
                        s = 1f - e / width;
                    }

                    if (nearObstacle) s = Mathf.Max(s, 0.65f * (1f - ObstacleEdge(x, y) / (4f + 6f * Value(x, y, 9f, seed + 3u))));
                    if (s <= 0f) continue;
                    var v = s + (Value(x, y, 4f, seed + 5u) - 0.5f) * 0.6f;
                    if (v <= 0.15f) continue;
                    var color = _s.Grime;
                    if (_s.Biome == Biome.OvergrownLabs && Value(x, y, 9f, seed + 9u) > 0.56f) color = _s.Organic;
                    // Around cover in the open middle the creep stays a faint rim, so actors never read against it.
                    var alpha = _s.EdgeCreepAlpha * (core ? 0.35f : 1f) * Bands(Mathf.Clamp01(v), x, y, 2, seed + 6u);
                    if (alpha <= 0f) continue;
                    for (var k = 0; k < 4; k++)
                    {
                        var px = x + (k & 1);
                        var py = y + (k >> 1);
                        _f.Over(px, py, color, alpha);
                        if (_s.EdgeCreepIsLight && !core && v > 0.7f && White(px, py, seed) < 0.05f) _f.Over(px, py, _s.OrganicLight, 0.6f);
                    }
                }
            }
        }

        /// <summary>
        /// Larger flat shapes that grow out of a wall foot: moss carpets in the Labs, water damage in the Metro,
        /// oil-soaked floor in the Rustworks, ice sheets in the Cryo Vaults. Heavier on the room's damp wall.
        /// </summary>
        private void WallPatches()
        {
            var ring = new List<(Vector2Int Cell, Side Side)>();
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
            {
                if (!_m.Decal(x, y) || _m.Door[_m.I(x, y)] || _m.MarkerZone[_m.I(x, y)] || _m.DistAt(x, y) != 1) continue;
                foreach (Side side in Enum.GetValues(typeof(Side)))
                    if (SolidOn(x, y, side)) { ring.Add((new Vector2Int(x, y), side)); break; }
            }

            if (ring.Count == 0) return;
            var count = Mathf.Clamp(Mathf.RoundToInt(ring.Count * (_s.Biome == Biome.OvergrownLabs ? 0.22f : 0.09f)), 2, _s.Biome == Biome.OvergrownLabs ? 14 : 9);
            for (var i = 0; i < count; i++)
            {
                var pick = ring[Next(ring.Count)];
                if (pick.Side != _damp && Chance(0.35f)) pick = ring[Next(ring.Count)];
                var at = Hug(pick.Cell, pick.Side, 0, Next(T));
                var r = _s.Biome == Biome.OvergrownLabs ? 18 + Next(18) : 14 + Next(14);
                var seed = _seed + 901u * (uint)(i + 1);
                for (var y = at.y - r; y <= at.y + r; y++)
                for (var x = at.x - r; x <= at.x + r; x++)
                {
                    var d = Mathf.Sqrt((x - at.x) * (x - at.x) + (y - at.y) * (y - at.y)) / r;
                    var edge = 0.55f + 0.55f * Fbm(x, y, 6f, seed);
                    if (d > edge) continue;
                    var inner = d < edge * 0.6f;
                    switch (_s.Biome)
                    {
                        case Biome.OvergrownLabs:
                            _f.Over(x, y, inner ? Shade(_s.Organic, 0.85f) : _s.Organic, inner ? 0.62f : 0.42f);
                            if (White(x / 2, y / 2, seed) < 0.16f) _f.Over(x, y, _s.OrganicLight, 0.8f);
                            break;
                        case Biome.Rustworks:
                            _f.Over(x, y, inner ? _s.Stain : _s.Stain2, inner ? 0.5f : 0.3f);
                            if (inner && White(x, y, seed) < 0.03f) _f.Over(x, y, Hex("#7F8A96"), 0.5f);
                            break;
                        case Biome.CryoVaults:
                            _f.Over(x, y, inner ? _s.Organic : _s.Stain2, inner ? 0.34f : 0.22f);
                            if (inner && White(x, y, seed) < 0.05f) _f.Over(x, y, _s.OrganicLight, 0.7f);
                            break;
                        default:
                            _f.Over(x, y, inner ? _s.Stain2 : _s.Stain, inner ? 0.42f : 0.3f);
                            if (!inner && Fbm(x, y, 3f, seed + 1u) > 0.62f) _f.Over(x, y, _s.Organic, 0.35f);
                            if (inner && White(x, y, seed) < 0.03f) _f.Over(x, y, _s.CrackLight, 0.5f);
                            break;
                    }
                }
            }
        }

        /// <summary>What runs off the wall onto the floor below it: rust, damp, moss or meltwater streaks.</summary>
        private void Bleed()
        {
            var (color, chance, alpha) = _s.Biome switch
            {
                Biome.Rustworks => (_s.Stain2, 0.16f, 0.42f),
                Biome.OvergrownLabs => (_s.Organic, 0.1f, 0.4f),
                Biome.CryoVaults => (_s.Stain2, 0.07f, 0.3f),
                _ => (_s.Stain, 0.1f, 0.38f)
            };
            var seed = _seed + 751u;
            for (var cy = 0; cy < _m.H; cy++)
            for (var cx = 0; cx < _m.W; cx++)
            {
                if (!_m.Decal(cx, cy) || !_m.Solid(cx, cy + 1)) continue;
                for (var lx = 0; lx < T; lx++)
                {
                    var x = cx * T + lx;
                    var local = chance * ((int)_damp == 0 ? 1.8f : 1f);
                    if (White(x / 2, cy, seed) >= local) continue;
                    var length = 5 + (int)(White(x, cy, seed + 1u) * 16f);
                    var width = White(x, cy, seed + 2u) < 0.4f ? 2 : 1;
                    for (var k = 0; k < length; k++)
                    for (var w = 0; w < width; w++)
                        _f.Over(x + w, cy * T + T - 1 - k, color, alpha * (1f - k / (float)length * 0.7f));
                }
            }
        }

        /// <summary>
        /// Accumulated grit along every wall foot and around obstacles: clumped particles, dense at the wall and thinning
        /// out within ~12 px — concrete grit, metal filings and rust flakes, dust and leaf litter, snow crystals. Kept
        /// off doorways (traffic sweeps them) and off markers.
        /// </summary>
        private void Grit()
        {
            var seed = _seed + 771u;
            var tones = _s.Biome switch
            {
                Biome.Rustworks => new[] { _s.DebrisDark, _s.Debris, _s.Accent2, _s.Stain2 },
                Biome.OvergrownLabs => new[] { _s.DebrisDark, _s.Accent2, _s.Organic, _s.OrganicLight },
                Biome.CryoVaults => new[] { _s.Debris, _s.DebrisLight, _s.Organic, _s.DebrisDark },
                _ => new[] { _s.DebrisDark, _s.Debris, _s.DebrisLight, _s.Grime }
            };
            for (var cy = 0; cy < _m.H; cy++)
            for (var cx = 0; cx < _m.W; cx++)
            {
                if (!_m.Decal(cx, cy)) continue;
                var i = _m.I(cx, cy);
                if (_m.Door[i] || _m.MarkerZone[i]) continue;
                var nearWall = _m.DistAt(cx, cy) <= 1 || _m.Solid(cx + 1, cy + 1) || _m.Solid(cx - 1, cy + 1) || _m.Solid(cx + 1, cy - 1) || _m.Solid(cx - 1, cy - 1);
                var nearObstacle = _m.ObstacleAdjacent(cx, cy);
                if (!nearWall && !nearObstacle) continue;
                var core = _m.Core(cx, cy);
                for (var ly = 0; ly < T; ly++)
                for (var lx = 0; lx < T; lx++)
                {
                    var x = cx * T + lx;
                    var y = cy * T + ly;
                    var e = nearWall ? WallEdge(x, y, out var side) : 999f;
                    if (nearObstacle) e = Mathf.Min(e, ObstacleEdge(x, y) * 1.6f);
                    if (e > 13f) continue;
                    var p = Mathf.Pow(1f - e / 13f, 2f) * (0.25f + 0.9f * Value(x, y, 9f, seed));
                    if (White(x, y, seed + 1u) >= p * (core ? 0.2f : 0.55f)) continue;
                    var tone = tones[(int)(White(x, y, seed + 2u) * tones.Length) % tones.Length];
                    var alpha = core ? 0.2f : 0.85f;
                    _f.Over(x, y, tone, alpha);
                    if (White(x, y, seed + 3u) < 0.3f) _f.Over(x + 1, y, tone, alpha);
                }
            }
        }

        // ================================================================ 8. contact shadows

        private void Shadows()
        {
            const float max = 0.52f;
            for (var cy = 0; cy < _m.H; cy++)
            for (var cx = 0; cx < _m.W; cx++)
            {
                if (!_m.Decal(cx, cy)) continue;
                bool sn = _m.Solid(cx, cy + 1), se = _m.Solid(cx + 1, cy), ss = _m.Solid(cx, cy - 1), sw = _m.Solid(cx - 1, cy);
                bool snw = _m.Solid(cx - 1, cy + 1), sne = _m.Solid(cx + 1, cy + 1);
                bool on = _m.IsObstacle(cx, cy + 1), ow = _m.IsObstacle(cx - 1, cy), oe = _m.IsObstacle(cx + 1, cy), os = _m.IsObstacle(cx, cy - 1);
                bool onw = _m.IsObstacle(cx - 1, cy + 1);
                if (!(sn || se || ss || sw || snw || sne || on || ow || oe || os || onw)) continue;
                for (var ly = 0; ly < T; ly++)
                for (var lx = 0; lx < T; lx++)
                {
                    float top = T - 1 - ly, right = T - 1 - lx;
                    var a = 0f;
                    // Walls: the light comes from the top-left, so the wall above throws the long shadow.
                    if (sn) a = Mathf.Max(a, Ramp(top, 13f) * 0.5f);
                    if (sw) a = Mathf.Max(a, Ramp(lx, 7f) * 0.34f);
                    if (se) a = Mathf.Max(a, Ramp(right, 3f) * 0.22f);
                    if (ss) a = Mathf.Max(a, Ramp(ly, 3f) * 0.2f);
                    if (snw && !sn && !sw) a = Mathf.Max(a, Ramp(Mathf.Sqrt(lx * lx + top * top), 9f) * 0.34f);
                    if (sne && !sn && !se) a = Mathf.Max(a, Ramp(Mathf.Sqrt(right * right + top * top), 6f) * 0.28f);
                    // Obstacles: grounded by a short cast shadow below and to the right, and a thin contact line.
                    if (on) a = Mathf.Max(a, Ramp(top, 9f) * 0.48f);
                    if (ow) a = Mathf.Max(a, Ramp(lx, 5f) * 0.34f);
                    if (onw && !on && !ow) a = Mathf.Max(a, Ramp(Mathf.Sqrt(lx * lx + top * top), 7f) * 0.3f);
                    if (oe) a = Mathf.Max(a, Ramp(right, 2f) * 0.2f);
                    if (os) a = Mathf.Max(a, Ramp(ly, 2f) * 0.18f);
                    if (a <= 0f) continue;
                    var x = cx * T + lx;
                    var y = cy * T + ly;
                    _f.Over(x, y, _s.Shadow, max * Bands(a / max, x, y, 3, _seed + 801u, 0.45f));
                }
            }
        }

        // ================================================================ 9. clutter

        private enum Piece { Pile, RootMat, Cot, Panel, Pallet, Puddle, Cardboard, Rubble, Papers, Cable, Pipe, Crate, Casings, Planks, Shards, Scrap, Ice, Roots, Bag, Tickets, Straw, OpenCrate, MedCrate, Bandages, Lantern }

        private (Piece Piece, int Weight)[] Palette() => _s.Biome switch
        {
            Biome.Rustworks => new[] { (Piece.Scrap, 22), (Piece.Rubble, 10), (Piece.Pipe, 14), (Piece.Cable, 10), (Piece.Crate, 8), (Piece.Planks, 6), (Piece.Panel, 16), (Piece.Pallet, 8), (Piece.Puddle, 10) },
            Biome.OvergrownLabs => new[] { (Piece.Shards, 14), (Piece.Papers, 16), (Piece.Roots, 30), (Piece.Crate, 8), (Piece.Cable, 8), (Piece.Rubble, 6), (Piece.Panel, 12), (Piece.Puddle, 8), (Piece.Cardboard, 6) },
            Biome.CryoVaults => new[] { (Piece.Ice, 20), (Piece.Crate, 12), (Piece.Pipe, 12), (Piece.Cable, 10), (Piece.Shards, 10), (Piece.Papers, 4), (Piece.Panel, 14), (Piece.Puddle, 10), (Piece.Pallet, 8) },
            _ => new[] { (Piece.Rubble, 22), (Piece.Papers, 14), (Piece.Cable, 8), (Piece.Bag, 8), (Piece.Pipe, 6), (Piece.Crate, 8), (Piece.Tickets, 10), (Piece.Panel, 14), (Piece.Puddle, 10), (Piece.Cardboard, 8) }
        };

        private static Vector2Int SizeOf(Piece piece) => piece switch
        {
            Piece.Cot => new Vector2Int(28, 13),
            Piece.Panel => new Vector2Int(24, 14),
            Piece.Pallet => new Vector2Int(20, 14),
            Piece.Puddle => new Vector2Int(24, 13),
            Piece.Cardboard => new Vector2Int(15, 11),
            Piece.Rubble => new Vector2Int(20, 14),
            Piece.Papers => new Vector2Int(16, 11),
            Piece.Cable => new Vector2Int(20, 6),
            Piece.Pipe => new Vector2Int(22, 6),
            Piece.Crate => new Vector2Int(13, 11),
            Piece.Casings => new Vector2Int(14, 10),
            Piece.Planks => new Vector2Int(16, 8),
            Piece.Shards => new Vector2Int(12, 8),
            Piece.Scrap => new Vector2Int(16, 12),
            Piece.Ice => new Vector2Int(18, 13),
            Piece.Roots => new Vector2Int(28, 20),
            Piece.Bag => new Vector2Int(13, 9),
            Piece.Tickets => new Vector2Int(14, 8),
            Piece.Straw => new Vector2Int(16, 10),
            Piece.OpenCrate => new Vector2Int(16, 12),
            Piece.MedCrate => new Vector2Int(10, 9),
            Piece.Bandages => new Vector2Int(12, 7),
            Piece.Lantern => new Vector2Int(6, 7),
            _ => new Vector2Int(10, 10)
        };

        private void Clutter()
        {
            var candidates = new List<Vector2Int>();
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
                if (_m.Clutter(x, y)) candidates.Add(new Vector2Int(x, y));
            CornerPiles();
            var density = _m.Type == RoomType.Boss ? 0.42f : _s.Biome is Biome.Rustworks or Biome.OvergrownLabs ? 0.42f : 0.36f;
            var budget = Mathf.Min(60, Mathf.RoundToInt(candidates.Count * density));
            var palette = Palette();
            var total = 0;
            foreach (var p in palette) total += p.Weight;

            for (var i = 0; i < budget && candidates.Count > 0; i++)
            {
                var at = Next(candidates.Count);
                var cell = candidates[at];
                candidates.RemoveAt(at);
                var roll = Next(total);
                var piece = palette[0].Piece;
                foreach (var p in palette) { if (roll < p.Weight) { piece = p.Piece; break; } roll -= p.Weight; }
                if (_m.Type == RoomType.Combat && Chance(0.18f)) piece = Chance(0.5f) ? Piece.Casings : Piece.Planks;
                PlaceNear(cell, piece);
            }
        }

        /// <summary>Places a piece hugging the cell's wall (or obstacle); refuses any footprint that leaves clutter ground or overlaps.</summary>
        private bool PlaceNear(Vector2Int cell, Piece piece)
        {
            var side = WallSideOf(cell.x, cell.y);
            var size = SizeOf(piece);
            var horizontal = side is null or Side.N or Side.S;
            var w = horizontal ? size.x : size.y;
            var h = horizontal ? size.y : size.x;
            var inset = 2 + Next(5);
            Vector2Int origin;
            if (side == null)
            {
                origin = new Vector2Int(cell.x * T + Next(Mathf.Max(1, T - w)), cell.y * T + Next(Mathf.Max(1, T - h)));
            }
            else
            {
                var anchor = Hug(cell, side.Value, inset, Next(T));
                origin = side switch
                {
                    Side.N => new Vector2Int(anchor.x - w / 2, anchor.y - h + 1),
                    Side.S => new Vector2Int(anchor.x - w / 2, anchor.y),
                    Side.E => new Vector2Int(anchor.x - w + 1, anchor.y - h / 2),
                    _ => new Vector2Int(anchor.x, anchor.y - h / 2)
                };
            }

            return Place(new RectInt(origin.x, origin.y, w, h), piece, horizontal);
        }

        private bool Place(RectInt rect, Piece piece, bool horizontal)
        {
            for (var y = rect.yMin; y < rect.yMax; y++)
            for (var x = rect.xMin; x < rect.xMax; x++)
                if (!_f.In(x, y) || !_m.Clutter(x / T, y / T)) return false;
            var padded = new RectInt(rect.xMin - 2, rect.yMin - 2, rect.width + 4, rect.height + 4);
            foreach (var other in _placed) if (other.Overlaps(padded)) return false;
            _placed.Add(rect);
            for (var y = rect.yMin / T; y <= (rect.yMax - 1) / T; y++)
            for (var x = rect.xMin / T; x <= (rect.xMax - 1) / T; x++)
                ClutterCells.Add(new Vector2Int(x, y));
            Draw(piece, rect, horizontal);
            ClutterPlaced++;
            return true;
        }

        /// <summary>Debris gathers in corners: a heap in most free corner cells, the room's heaviest dressing.</summary>
        private void CornerPiles()
        {
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
            {
                if (!_m.Clutter(x, y)) continue;
                var vertical = SolidOn(x, y, Side.N) ? Side.N : SolidOn(x, y, Side.S) ? Side.S : (Side?)null;
                var horizontal = SolidOn(x, y, Side.W) ? Side.W : SolidOn(x, y, Side.E) ? Side.E : (Side?)null;
                if (vertical == null || horizontal == null || !Chance(0.75f)) continue;
                var w = 18 + Next(8);
                var h = 14 + Next(6);
                var ox = horizontal == Side.W ? x * T + 1 : x * T + T - 1 - w;
                var oy = vertical == Side.S ? y * T + 1 : y * T + T - 1 - h;
                if (_s.Biome == Biome.OvergrownLabs) { w += 8; h += 6; ox = horizontal == Side.W ? x * T + 1 : x * T + T - 1 - w; oy = vertical == Side.S ? y * T + 1 : y * T + T - 1 - h; }
                var rect = new RectInt(ox, oy, w, h);
                _corner = new Vector2Int(horizontal == Side.W ? rect.xMin : rect.xMax - 1, vertical == Side.S ? rect.yMin : rect.yMax - 1);
                if (!Place(rect, _s.Biome == Biome.OvergrownLabs ? Piece.RootMat : Piece.Pile, true)) continue;
            }
        }

        private Vector2Int _corner;

        /// <summary>Roots and leaves spreading out of a corner (Overgrown Labs).</summary>
        private void RootMat(RectInt r)
        {
            var away = new Vector2(r.center.x - _corner.x, r.center.y - _corner.y).normalized;
            var baseAngle = Mathf.Atan2(away.y, away.x);
            for (var root = 0; root < 7 + Next(4); root++)
            {
                float x = _corner.x, y = _corner.y;
                var angle = baseAngle + (NextF() - 0.5f) * 1.6f;
                var length = 10 + Next(r.width);
                for (var step = 0; step < length; step++)
                {
                    x += Mathf.Cos(angle);
                    y += Mathf.Sin(angle);
                    angle += (NextF() - 0.5f) * 0.5f;
                    var px = Mathf.RoundToInt(x);
                    var py = Mathf.RoundToInt(y);
                    if (!r.Contains(new Vector2Int(px, py))) break;
                    _f.Solid(px, py, _s.Organic);
                    if (step < length / 3) _f.Solid(px + 1, py, _s.Organic); // thicker near the corner
                    if (step % 4 == 2)
                    {
                        _f.Solid(px, py + 1, _s.OrganicLight);
                        _f.Solid(px - 1, py + 1, Shade(_s.OrganicLight, 0.8f));
                    }
                }
            }

            // Moss under the mat.
            for (var y = r.yMin; y < r.yMax; y++)
            for (var x = r.xMin; x < r.xMax; x++)
            {
                var d = Vector2.Distance(new Vector2(x, y), _corner) / r.width;
                if (d < 0.7f && Value(x, y, 4f, _seed + 9u) > 0.35f + d * 0.5f) _f.Over(x, y, _s.Organic, 0.45f);
            }
        }

        private void Cot(RectInt r, bool horizontal)
        {
            var frame = _s.DebrisDark;
            var cloth = _s.Biome == Biome.OvergrownLabs ? Hex("#C9CEC4") : _s.Biome == Biome.CryoVaults ? Hex("#AEB9BE") : Hex("#9C9F92");
            DropShadow(r.xMin, r.yMin, r.width, r.height);
            var length = horizontal ? r.width : r.height;
            var breadth = horizontal ? r.height : r.width;
            for (var t = 0; t < length; t++)
            for (var k = 0; k < breadth; k++)
            {
                var x = horizontal ? r.xMin + t : r.xMin + k;
                var y = horizontal ? r.yMin + k : r.yMin + t;
                var edge = t == 0 || k == 0 || t == length - 1 || k == breadth - 1;
                Color32 c = edge ? frame : t < 6 ? Shade(cloth, 1.12f) : t == 6 ? Shade(cloth, 0.8f) : (t + k) % 9 == 0 ? Shade(cloth, 0.9f) : cloth;
                _f.Solid(x, y, c);
            }

            _f.Rect(horizontal ? r.xMin + length / 2 : r.xMin + 2, horizontal ? r.yMin + 2 : r.yMin + length / 2, 4, 3, Hex("#6E5A44"), 0.6f); // an old stain
        }

        private void Pile(RectInt r)
        {
            var (dark, mid, light) = (_s.DebrisDark, _s.Debris, _s.DebrisLight);
            // A loose skirt of grit first, then the heap, then a couple of biome pieces on top.
            for (var i = 0; i < 14; i++) _f.Over(r.xMin + Next(r.width), r.yMin + Next(r.height), dark, 0.7f);
            var inner = new RectInt(r.xMin + 2, r.yMin + 2, r.width - 4, r.height - 4);
            Chunks(inner, 6 + Next(4), 2, 6, dark, mid, light);
            switch (_s.Biome)
            {
                case Biome.RuinedMetro: Papers(inner, 1 + Next(2), _s.Accent); break;
                case Biome.Rustworks: Bits(inner, 3, _s.Accent, 2, 1); break;
                case Biome.OvergrownLabs: Roots(r); break;
                default: Bits(inner, 4, _s.DebrisLight, 1, 1); break;
            }
        }

        private void Draw(Piece piece, RectInt r, bool horizontal)
        {
            switch (piece)
            {
                case Piece.Rubble: Chunks(r, 4 + Next(5), 2, 5, _s.DebrisDark, _s.Debris, _s.DebrisLight); break;
                case Piece.Ice: Chunks(r, 3 + Next(4), 3, 6, _s.DebrisDark, _s.Debris, _s.DebrisLight); break;
                case Piece.Scrap: Scrap(r); break;
                case Piece.Papers: Papers(r, 2 + Next(3), _s.Accent); break;
                case Piece.Tickets: Bits(r, 6 + Next(6), _s.Accent, 2, 1); break;
                case Piece.Straw: Straw(r); break;
                case Piece.Shards: Shards(r); break;
                case Piece.Cable: Cable(r, horizontal); break;
                case Piece.Pipe: Pipe(r, horizontal); break;
                case Piece.Crate: Box(r.xMin, r.yMin, Mathf.Min(r.width, 12), Mathf.Min(r.height, 10), _s.Accent2, false, false); break;
                case Piece.Panel: Panel(r); break;
                case Piece.Pallet: Pallet(r, horizontal); break;
                case Piece.Puddle: Puddle(r); break;
                case Piece.Cardboard: Cardboard(r); break;
                case Piece.OpenCrate: Box(r.xMin, r.yMin, 10, 9, _s.Accent2, true, false); Plank(r.xMin + 11, r.yMin + 1, 3, 10, Hex("#6B4A2E")); break;
                case Piece.MedCrate: Box(r.xMin, r.yMin, 10, 9, _s.Biome == Biome.Rustworks ? Hex("#55605A") : Hex("#4F6E68"), false, true); break;
                case Piece.Casings: Casings(r); break;
                case Piece.Planks: Plank(r.xMin, r.yMin + 1, horizontal ? 14 : 3, horizontal ? 3 : 14, Hex("#6B4A2E")); Plank(r.xMin + 2, r.yMin + 4, horizontal ? 12 : 3, horizontal ? 3 : 12, Hex("#5A3E26")); break;
                case Piece.Roots: Roots(r); break;
                case Piece.Bag: Bag(r); break;
                case Piece.Bandages: Bits(r, 4 + Next(3), Hex("#D8D2C0"), 4, 1); Bits(r, 2, Hex("#9A8F7A"), 1, 1); break;
                case Piece.Lantern: Lantern(r.xMin, r.yMin); break;
                case Piece.Pile: Pile(r); break;
                case Piece.RootMat: RootMat(r); break;
                case Piece.Cot: Cot(r, horizontal); break;
            }
        }

        private void DropShadow(int x, int y, int w, int h)
        {
            for (var yy = y - 1; yy < y + h - 1; yy++)
            for (var xx = x + 1; xx < x + w + 1; xx++)
                _f.Over(xx, yy, _s.Shadow, 0.28f);
        }

        private void Chunks(RectInt r, int count, int min, int max, Color32 dark, Color32 mid, Color32 light)
        {
            for (var i = 0; i < count; i++)
            {
                var w = min + Next(max - min + 1);
                var h = Mathf.Max(2, w - 1 - Next(2));
                var x = r.xMin + Next(Mathf.Max(1, r.width - w));
                var y = r.yMin + 1 + Next(Mathf.Max(1, r.height - h - 1));
                DropShadow(x, y, w, h);
                for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                {
                    var outline = xx == x || yy == y || xx == x + w - 1 || yy == y + h - 1;
                    var corner = (xx == x || xx == x + w - 1) && (yy == y || yy == y + h - 1);
                    if (corner && w > 2 && h > 2) continue; // rounded chips
                    var c = outline ? dark : yy == y + h - 2 && xx < x + w - 2 ? light : mid;
                    _f.Solid(xx, yy, c);
                }
            }
        }

        /// <summary>A fallen ceiling panel / steel offcut / ceiling tile / insulation panel, one corner broken away.</summary>
        private void Panel(RectInt r)
        {
            var (body, rim, top) = _s.Biome switch
            {
                Biome.Rustworks => (_s.Debris, _s.DebrisDark, _s.DebrisLight),
                Biome.OvergrownLabs => (_s.Accent2, Shade(_s.Accent2, 0.6f), _s.Accent),
                Biome.CryoVaults => (_s.Accent2, Shade(_s.Accent2, 0.6f), _s.Debris),
                _ => (_s.Debris, _s.DebrisDark, _s.DebrisLight)
            };
            var w = r.width - 2;
            var h = r.height - 2;
            var x0 = r.xMin + 1;
            var y0 = r.yMin + 1;
            var broken = Next(4);
            DropShadow(x0, y0, w, h);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var cx = broken % 2 == 0 ? x : w - 1 - x;
                var cy = broken < 2 ? y : h - 1 - y;
                if (cx + cy < 5) continue; // the missing corner
                var edge = x == 0 || y == 0 || x == w - 1 || y == h - 1 || cx + cy == 5;
                var c = edge ? rim : y == h - 2 ? top : body;
                if (_s.Biome == Biome.OvergrownLabs && !edge && (x % 6 == 0 || y % 6 == 0)) c = Shade(body, 0.88f);
                if (_s.Biome == Biome.Rustworks && !edge && (x == 2 || x == w - 3) && (y == 2 || y == h - 3)) c = top;
                _f.Solid(x0 + x, y0 + y, c);
            }

            // A crack across it.
            var cx0 = x0 + w / 3 + Next(w / 3);
            for (var y = 1; y < h - 1; y++) _f.Over(cx0 + (y % 3 == 0 ? 1 : 0), y0 + y, rim, 0.8f);
        }

        private void Pallet(RectInt r, bool horizontal)
        {
            var wood = _s.Biome == Biome.CryoVaults ? Hex("#5E5A50") : Hex("#6B5034");
            DropShadow(r.xMin, r.yMin, r.width, r.height);
            for (var i = 0; i < (horizontal ? r.height : r.width); i += 3)
            for (var t = 0; t < (horizontal ? r.width : r.height); t++)
            {
                var x = horizontal ? r.xMin + t : r.xMin + i;
                var y = horizontal ? r.yMin + i : r.yMin + t;
                _f.Solid(x, y, t % 9 == 0 ? Shade(wood, 0.6f) : i % 6 == 0 ? Shade(wood, 1.2f) : wood);
                if (horizontal) _f.Over(x, y + 1, Shade(wood, 0.5f), 0.9f); else _f.Over(x + 1, y, Shade(wood, 0.5f), 0.9f);
            }

            if (_s.Biome == Biome.CryoVaults) Bits(r, 6, _s.DebrisLight, 2, 1);
        }

        /// <summary>Standing liquid: water / oil / specimen fluid / ice, with a lighter rim and a few reflection pixels.</summary>
        private void Puddle(RectInt r)
        {
            var (fill, rim, shine, alpha) = _s.Biome switch
            {
                Biome.Rustworks => (Hex("#141110"), Hex("#2A2420"), Hex("#6E7A86"), 0.75f),
                Biome.OvergrownLabs => (Hex("#5B7A3A"), Hex("#7E9C55"), Hex("#C8E89A"), 0.6f),
                Biome.CryoVaults => (Hex("#8FC3D3"), Hex("#C9E9F2"), Hex("#FFFFFF"), 0.55f),
                _ => (Hex("#2A3438"), Hex("#3A464B"), Hex("#8FA3AA"), 0.6f)
            };
            var cx = r.center.x;
            var cy = r.center.y;
            var seed = _seed + (uint)(r.xMin * 13 + r.yMin);
            for (var y = r.yMin; y < r.yMax; y++)
            for (var x = r.xMin; x < r.xMax; x++)
            {
                var dx = (x - cx) / (r.width * 0.5f);
                var dy = (y - cy) / (r.height * 0.5f);
                var d = Mathf.Sqrt(dx * dx + dy * dy) + (Value(x, y, 4f, seed) - 0.5f) * 0.45f;
                if (d > 1f) continue;
                _f.Over(x, y, d > 0.82f ? rim : fill, alpha);
            }

            for (var i = 0; i < 3; i++)
            {
                var x = (int)cx - 4 + Next(8);
                var y = (int)cy - 2 + Next(4);
                _f.Over(x, y, shine, 0.7f);
                _f.Over(x + 1, y, shine, 0.5f);
            }
        }

        private void Cardboard(RectInt r)
        {
            var card = Hex("#8A7350");
            DropShadow(r.xMin, r.yMin, r.width, r.height);
            for (var y = r.yMin; y < r.yMax; y++)
            for (var x = r.xMin; x < r.xMax; x++)
            {
                var edge = x == r.xMin || y == r.yMin || x == r.xMax - 1 || y == r.yMax - 1;
                var fold = x == r.xMin + r.width / 2 || y == r.yMin + r.height / 3;
                _f.Solid(x, y, edge ? Shade(card, 0.6f) : fold ? Shade(card, 0.82f) : card);
            }

            _f.Rect(r.xMin + 3, r.yMin + 3, 4, 2, Hex("#3A3028"), 0.7f); // a printed mark
        }

        private void Scrap(RectInt r)
        {
            // A gear, a bent bar and a few bolts.
            var gx = r.xMin + 4;
            var gy = r.yMin + 5;
            DropShadow(gx - 3, gy - 3, 7, 7);
            for (var y = -3; y <= 3; y++)
            for (var x = -3; x <= 3; x++)
            {
                var d = Mathf.Sqrt(x * x + y * y);
                if (d > 3.4f || d < 1.2f) continue;
                var tooth = d > 2.6f && (Mathf.Abs(x) == 3 || Mathf.Abs(y) == 3) && (x == 0 || y == 0 || Mathf.Abs(x) == Mathf.Abs(y));
                if (d > 2.6f && !tooth && d > 2.9f) continue;
                _f.Solid(gx + x, gy + y, y > 0 ? _s.DebrisLight : _s.Debris);
            }

            Plank(r.xMin + 8, r.yMin + 2, 5, 2, _s.Accent);
            Bits(r, 3, _s.DebrisLight, 1, 1);
        }

        private void Papers(RectInt r, int count, Color32 paper)
        {
            for (var i = 0; i < count; i++)
            {
                var w = 4 + Next(2);
                var h = 3 + Next(2);
                if (Chance(0.5f)) (w, h) = (h, w);
                var x = r.xMin + Next(Mathf.Max(1, r.width - w));
                var y = r.yMin + Next(Mathf.Max(1, r.height - h));
                for (var yy = y; yy < y + h; yy++)
                for (var xx = x; xx < x + w; xx++)
                    _f.Over(xx, yy, xx == x + w / 2 ? Shade(paper, 0.82f) : paper, 0.85f);
                _f.Over(x + 1, y + h - 1, Shade(paper, 0.6f), 0.6f); // a line of print
            }
        }

        private void Bits(RectInt r, int count, Color32 color, int w, int h)
        {
            for (var i = 0; i < count; i++)
            {
                var x = r.xMin + Next(Mathf.Max(1, r.width - w));
                var y = r.yMin + Next(Mathf.Max(1, r.height - h));
                var vertical = Chance(0.5f);
                for (var k = 0; k < (vertical ? h : w); k++)
                for (var j = 0; j < (vertical ? w : h); j++)
                    _f.Over(x + (vertical ? j : k), y + (vertical ? k : j), color, 0.9f);
            }
        }

        private void Straw(RectInt r)
        {
            var straw = Hex("#B59E68");
            for (var i = 0; i < 9; i++)
            {
                var x = r.xMin + Next(r.width);
                var y = r.yMin + Next(r.height);
                _f.Line(x, y, x + Next(5) - 2, y + Next(3) - 1, straw, 0.7f);
            }
        }

        private void Shards(RectInt r)
        {
            for (var i = 0; i < 4 + Next(4); i++)
            {
                var x = r.xMin + Next(Mathf.Max(1, r.width - 3));
                var y = r.yMin + Next(Mathf.Max(1, r.height - 3));
                _f.Solid(x, y, _s.Debris);
                _f.Solid(x + 1, y, _s.Debris);
                _f.Solid(x, y + 1, _s.DebrisLight);
                if (Chance(0.5f)) _f.Solid(x + 2, y, _s.DebrisDark);
            }
        }

        private void Cable(RectInt r, bool horizontal)
        {
            var length = horizontal ? r.width : r.height;
            var amplitude = 1 + Next(2);
            var phase = NextF() * 6f;
            for (var t = 0; t < length; t++)
            {
                var off = Mathf.RoundToInt(Mathf.Sin(t * 0.35f + phase) * amplitude) + 2;
                var x = horizontal ? r.xMin + t : r.xMin + off;
                var y = horizontal ? r.yMin + off : r.yMin + t;
                _f.Over(horizontal ? x : x + 1, horizontal ? y - 1 : y, _s.Shadow, 0.3f);
                _f.Solid(x, y, _s.DebrisDark);
                if (t % 3 == 0) _f.Solid(horizontal ? x : x - 1, horizontal ? y + 1 : y, _s.Debris);
            }
        }

        private void Pipe(RectInt r, bool horizontal)
        {
            var length = horizontal ? r.width : r.height;
            var tone = _s.Biome == Biome.CryoVaults ? _s.DebrisDark : _s.Debris;
            DropShadow(r.xMin, r.yMin, horizontal ? length : 4, horizontal ? 4 : length);
            for (var t = 0; t < length; t++)
            for (var k = 0; k < 4; k++)
            {
                var flange = t == 1 || t == length - 2;
                var c = k == 0 ? _s.DebrisDark : k == 3 ? (_s.Biome == Biome.CryoVaults ? _s.DebrisLight : Shade(tone, 1.35f)) : flange ? Shade(tone, 0.8f) : tone;
                _f.Solid(horizontal ? r.xMin + t : r.xMin + k, horizontal ? r.yMin + k : r.yMin + t, c);
            }
        }

        private void Box(int x, int y, int w, int h, Color32 wood, bool open, bool cross)
        {
            DropShadow(x, y, w, h);
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
            {
                var outline = xx == x || yy == y || xx == x + w - 1 || yy == y + h - 1;
                Color32 c;
                if (outline) c = Shade(wood, 0.55f);
                else if (open) c = yy == y + h - 2 ? Shade(wood, 0.8f) : Shade(wood, 0.35f);
                else c = (yy - y) % 3 == 0 ? Shade(wood, 0.8f) : yy == y + h - 2 ? Shade(wood, 1.25f) : wood;
                _f.Solid(xx, yy, c);
            }

            if (!cross) return;
            var mx = x + w / 2;
            var my = y + h / 2;
            var white = Hex("#D9DCD4");
            for (var k = -1; k <= 1; k++)
            {
                _f.Solid(mx + k, my, white);
                _f.Solid(mx, my + k, white);
            }
        }

        private void Plank(int x, int y, int w, int h, Color32 wood)
        {
            DropShadow(x, y, w, h);
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                _f.Solid(xx, yy, yy == y + h - 1 || xx == x ? Shade(wood, 1.25f) : yy == y ? Shade(wood, 0.6f) : wood);
        }

        private void Casings(RectInt r)
        {
            var brass = Hex("#A88A44");
            var shine = Hex("#D8BE72");
            for (var i = 0; i < 5 + Next(5); i++)
            {
                var x = r.xMin + Next(r.width - 1);
                var y = r.yMin + Next(r.height);
                _f.Solid(x, y, brass);
                _f.Solid(x + 1, y, shine);
            }
        }

        private void Roots(RectInt r)
        {
            var x = (float)(r.xMin + Next(Mathf.Max(1, r.width / 3)));
            var y = (float)(r.yMin + r.height / 2);
            var angle = NextF() * 0.8f - 0.4f;
            for (var step = 0; step < r.width + 6; step++)
            {
                x += Mathf.Cos(angle);
                y += Mathf.Sin(angle);
                angle += (NextF() - 0.5f) * 0.6f;
                var px = Mathf.RoundToInt(x);
                var py = Mathf.RoundToInt(y);
                if (!r.Contains(new Vector2Int(px, py))) break;
                _f.Solid(px, py, _s.Organic);
                if (step % 5 == 2)
                {
                    _f.Solid(px, py + 1, _s.OrganicLight);
                    _f.Solid(px + 1, py + 1, _s.OrganicLight);
                }
            }
        }

        private void Bag(RectInt r)
        {
            var cloth = _s.Biome == Biome.RuinedMetro ? Hex("#2E3330") : Hex("#4A4F3A");
            DropShadow(r.xMin, r.yMin, 10, 7);
            for (var y = 0; y < 7; y++)
            for (var x = 0; x < 10; x++)
            {
                if ((x == 0 || x == 9) && (y == 0 || y == 6)) continue;
                var c = y == 5 ? Shade(cloth, 1.4f) : x == 5 ? Shade(cloth, 0.75f) : cloth;
                _f.Solid(r.xMin + x, r.yMin + y, c);
            }
        }

        private void Lantern(int x, int y)
        {
            var frame = Hex("#2A2622");
            for (var yy = 0; yy < 6; yy++)
            for (var xx = 0; xx < 5; xx++)
                _f.Solid(x + xx, y + yy, xx == 0 || xx == 4 || yy == 0 || yy == 5 ? frame : Hex("#FFD27A"));
        }

        // ================================================================ 10. story props

        private void StoryProps()
        {
            if (_story.Count == 0) return;
            void At(int index, float along, Piece piece)
            {
                var p = StoryPixel(index, 3 + Next(4), along);
                var size = SizeOf(piece);
                var horizontal = _storySide is Side.N or Side.S;
                var w = horizontal ? size.x : size.y;
                var h = horizontal ? size.y : size.x;
                var origin = _storySide switch
                {
                    Side.N => new Vector2Int(p.x - w / 2, p.y - h + 1),
                    Side.S => new Vector2Int(p.x - w / 2, p.y),
                    Side.E => new Vector2Int(p.x - w + 1, p.y - h / 2),
                    _ => new Vector2Int(p.x, p.y - h / 2)
                };
                Place(new RectInt(origin.x, origin.y, w, h), piece, horizontal);
            }

            var last = _story.Count - 1;
            switch (_m.Type)
            {
                case RoomType.Merchant:
                    At(0, 0.2f, Piece.Crate);
                    At(0, 0.65f, Piece.Crate);
                    At(last, 0.7f, Piece.Bag);
                    At(last, 0.25f, Piece.Lantern);
                    Lamp(StoryPixel(last, 8, 0.3f), 34, _s.Biome == Biome.CryoVaults ? _s.Glow2 : Hex("#FFC872"), 0.2f, false);
                    break;
                case RoomType.MedicalRecovery:
                    At(last / 2, 0.5f, Piece.Cot);
                    At(0, 0.3f, Piece.MedCrate);
                    At(last, 0.6f, Piece.MedCrate);
                    At(last / 2, 0.5f, Piece.Bandages);
                    Lamp(StoryPixel(last / 2, 4, 0.5f), 30, Hex("#E6F4EE"), 0.12f, true);
                    break;
                case RoomType.Event:
                    At(0, 0.3f, Piece.Papers);
                    At(last, 0.6f, _s.Biome is Biome.OvergrownLabs or Biome.CryoVaults ? Piece.Shards : Piece.Rubble);
                    Lamp(StoryPixel(last / 2, 6, 0.5f), 28, _s.Glow2, 0.14f, true);
                    break;
                case RoomType.Start:
                    At(0, 0.3f, Piece.Bag);
                    At(last, 0.5f, Piece.Crate);
                    At(last, 0.1f, Piece.Lantern);
                    Lamp(StoryPixel(last, 8, 0.15f), 34, Hex("#FFC872"), 0.16f, false);
                    break;
                case RoomType.Loot:
                case RoomType.Treasure:
                    At(0, 0.35f, Piece.OpenCrate);
                    At(last, 0.4f, Piece.OpenCrate);
                    At(last / 2 + (last > 1 ? 1 : 0), 0.6f, Piece.Straw);
                    if (_m.Type == RoomType.Treasure) At(0, 0.8f, Piece.Papers);
                    break;
                case RoomType.Boss:
                    At(0, 0.4f, Piece.Rubble);
                    At(last, 0.6f, Piece.Rubble);
                    At(last / 2, 0.5f, Piece.Planks);
                    break;
                case RoomType.Combat:
                    At(0, 0.5f, Piece.Casings);
                    At(last, 0.5f, Piece.Planks);
                    break;
            }
        }

        // ================================================================ 11. lamp pools

        private void Lamps()
        {
            var count = 1 + (_m.W * _m.H > 300 ? 1 : 0) + Next(2);
            for (var i = 0; i < count; i++)
            {
                var preferNorth = Chance(0.55f);
                var cell = PickCell((x, y) => _m.Decal(x, y) && !_m.Door[_m.I(x, y)] && _m.DistAt(x, y) == 1 && (!preferNorth || SolidOn(x, y, Side.N)), false)
                           ?? PickCell((x, y) => _m.Decal(x, y) && !_m.Door[_m.I(x, y)] && _m.DistAt(x, y) == 1, false);
                if (cell == null) return;
                var side = WallSideOf(cell.Value.x, cell.Value.y) ?? Side.N;
                var warm = _s.Biome == Biome.CryoVaults ? Chance(0.2f) : !Chance(0.3f);
                Lamp(Hug(cell.Value, side, 0, 16), 40 + Next(20), warm ? _s.Glow : _s.Glow2, 0.34f, true, side);
            }
        }

        /// <summary>A dithered light pool on the floor (and spilling onto the wall), optionally with a fixture on the wall.</summary>
        private void Lamp(Vector2Int at, int radius, Color32 color, float strength, bool fixture, Side? wall = null)
        {
            _lamps.Add((at.x, at.y, radius, color));
            color = Color32.Lerp(color, new Color32(255, 255, 255, 255), 0.45f);
            var saved = _f.Gate;
            _f.Gate = _floorGate;
            for (var y = at.y - radius; y <= at.y + radius; y++)
            for (var x = at.x - radius; x <= at.x + radius; x++)
            {
                var d = Mathf.Sqrt((x - at.x) * (x - at.x) + (y - at.y) * (y - at.y));
                if (d > radius) continue;
                var s = Mathf.Pow(1f - d / radius, 2f);
                _f.Over(x, y, color, strength * Bands(s, x, y, 4, (uint)(at.x * 31 + at.y), 1.2f));
                _w.Over(x, y, color, strength * 0.75f * Bands(s, x, y, 3, (uint)(at.x * 17 + at.y), 1.2f));
            }

            _f.Gate = saved;
            if (!fixture || wall == null) return;
            // The fixture on the wall face the pool hangs from.
            var dir = wall.Value switch { Side.N => Vector2Int.up, Side.S => Vector2Int.down, Side.E => Vector2Int.right, _ => Vector2Int.left };
            var c = at + dir * 4;
            for (var y = -2; y <= 2; y++)
            for (var x = -3; x <= 3; x++)
            {
                var edge = Mathf.Abs(x) == 3 || Mathf.Abs(y) == 2;
                _w.Over(c.x + x, c.y + y, edge ? Hex("#1E1F1E") : color, 0.95f);
            }

            _w.Over(c.x, c.y, Hex("#FFFFFF"), 0.8f);
        }

        // ================================================================ 12. wall face

        private void WallFace()
        {
            var seed = _seed + 1201u;
            var cells = new List<Vector2Int>();
            for (var y = 0; y < _m.H; y++)
            for (var x = 0; x < _m.W; x++)
                if (_m.WallPaint(x, y)) cells.Add(new Vector2Int(x, y));
            if (cells.Count == 0) return;

            foreach (var cell in cells)
            {
                var floorBelow = _m.Open(cell.x, cell.y - 1);
                for (var ly = 0; ly < T; ly += 2)
                for (var lx = 0; lx < T; lx += 2)
                {
                    var x = cell.x * T + lx;
                    var y = cell.y * T + ly;
                    var n = Fbm(x, y, 10f, seed);
                    var stain = n > 0.55f ? 0.42f * Bands((n - 0.55f) * 3f, x, y, 2, seed + 1u) : 0f;
                    var extra = 0f;
                    var extraColor = _s.Organic;
                    if (_s.Biome == Biome.OvergrownLabs && Value(x, y, 7f, seed + 3u) > 0.6f) extra = 0.5f;
                    else if (_s.Biome == Biome.CryoVaults)
                    {
                        var f = Value(x, y, 6f, seed + 3u);
                        if (f > 0.6f) { extra = 0.4f * Bands((f - 0.6f) * 3f, x, y, 2, seed + 4u); extraColor = _s.WallStreak; }
                    }

                    for (var k = 0; k < 4; k++)
                    {
                        var px = x + (k & 1);
                        var py = y + (k >> 1);
                        if (stain > 0f) _w.Over(px, py, _s.WallStain, stain);
                        // Grime gathers at the foot of a wall that meets the floor.
                        if (floorBelow && ly + (k >> 1) < 6) _w.Over(px, py, _s.Shadow, 0.32f * (1f - (ly + (k >> 1)) / 6f > 0.5f ? 1f : 0.5f));
                        if (extra > 0f) _w.Over(px, py, extraColor, extra);
                    }
                }

                // Streaks running down from the top of the wall.
                var streaks = _s.Biome == Biome.Rustworks ? Next(4) : Next(3);
                for (var i = 0; i < streaks; i++)
                {
                    var sx = cell.x * T + Next(T);
                    var top = cell.y * T + T - 1 - Next(8);
                    var length = 5 + Next(12);
                    for (var k = 0; k < length; k++) _w.Over(sx, top - k, _s.WallStreak, 0.38f * (1f - k / (float)length));
                }

                // Chipped faces.
                for (var i = 0; i < Next(3); i++)
                {
                    var cx = cell.x * T + 2 + Next(T - 5);
                    var cy = cell.y * T + 2 + Next(T - 5);
                    _w.Rect(cx, cy, 2, 2, _s.Shadow, 0.5f);
                    _w.Over(cx - 1, cy + 2, _s.CrackLight, 0.35f);
                }

                if (_s.Biome == Biome.CryoVaults && floorBelow)
                    for (var i = 0; i < Next(3); i++)
                    {
                        var ix = cell.x * T + Next(T);
                        var len = 2 + Next(5);
                        for (var k = 0; k < len; k++) _w.Over(ix, cell.y * T + k, k == 0 ? _s.DebrisLight : _s.Debris, 0.7f);
                    }

                if (_m.Type is RoomType.Combat or RoomType.Boss && Chance(0.12f))
                    for (var i = 0; i < 3 + Next(5); i++)
                    {
                        var hx = cell.x * T + 3 + Next(T - 6);
                        var hy = cell.y * T + 3 + Next(T - 6);
                        _w.Over(hx, hy, _s.Shadow, 0.75f);
                        _w.Over(hx + 1, hy + 1, _s.CrackLight, 0.3f);
                    }
            }

            // Biome wall furniture along a run of wall cells that face the floor.
            var facing = cells.FindAll(c => _m.Open(c.x, c.y - 1));
            if (facing.Count == 0) return;
            switch (_s.Biome)
            {
                case Biome.RuinedMetro:
                    for (var i = 0; i < 1 + Next(2); i++) Poster(facing[Next(facing.Count)]);
                    WallCable(facing);
                    break;
                case Biome.Rustworks:
                    WallCable(facing);
                    for (var i = 0; i < 1 + Next(2); i++) WarningPlate(facing[Next(facing.Count)]);
                    break;
                case Biome.OvergrownLabs:
                    for (var i = 0; i < 4 + Next(4); i++) Vine(facing[Next(facing.Count)]);
                    break;
                default:
                    WallCable(facing);
                    break;
            }
        }

        private void Poster(Vector2Int cell)
        {
            var colors = new[] { Hex("#7A5A4A"), Hex("#4A5A7A"), Hex("#8A8460"), Hex("#5E6E52") };
            var paper = colors[Next(colors.Length)];
            var x0 = cell.x * T + 4 + Next(10);
            var y0 = cell.y * T + 8 + Next(6);
            for (var y = 0; y < 14; y++)
            for (var x = 0; x < 11; x++)
            {
                if (x + (13 - y) < 4 && Chance(0.8f)) continue; // torn corner
                var c = y >= 10 ? Shade(paper, 1.3f) : (y == 4 || y == 6) && x > 1 && x < 9 ? Shade(paper, 0.7f) : paper;
                _w.Over(x0 + x, y0 + y, c, 0.85f);
            }
        }

        private void WarningPlate(Vector2Int cell)
        {
            var x0 = cell.x * T + 6 + Next(12);
            var y0 = cell.y * T + 12 + Next(8);
            for (var y = 0; y < 6; y++)
            for (var x = 0; x < 10; x++)
                _w.Over(x0 + x, y0 + y, ((x + y) / 2 & 1) == 0 ? _s.Paint : _s.Paint2, 0.6f);
        }

        private void WallCable(List<Vector2Int> facing)
        {
            facing.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            var start = Next(facing.Count);
            var y0 = facing[start].y;
            var height = 18 + Next(8);
            var color = _s.Biome == Biome.CryoVaults ? Hex("#2A3238") : Hex("#1C1E1D");
            for (var i = start; i < facing.Count && facing[i].y == y0 && i < start + 8; i++)
            {
                if (i > start && facing[i].x != facing[i - 1].x + 1) break;
                for (var lx = 0; lx < T; lx++)
                {
                    var sag = Mathf.RoundToInt(Mathf.Sin(lx / (float)T * Mathf.PI) * 2f);
                    var x = facing[i].x * T + lx;
                    var y = facing[i].y * T + height - sag;
                    _w.Over(x, y, color, 0.85f);
                    _w.Over(x, y + 1, _s.CrackLight, 0.2f);
                    if (_s.Biome == Biome.CryoVaults && lx % 7 == 3) _w.Over(x, y + 1, _s.DebrisLight, 0.6f);
                }

                _w.Rect(facing[i].x * T, facing[i].y * T + height - 1, 2, 3, Shade(color, 1.8f), 0.9f); // clip
            }
        }

        private void Vine(Vector2Int cell)
        {
            float x = cell.x * T + Next(T);
            float y = cell.y * T;
            var angle = Mathf.PI * 0.5f + (NextF() - 0.5f) * 0.6f;
            var length = 18 + Next(28);
            for (var step = 0; step < length; step++)
            {
                x += Mathf.Cos(angle);
                y += Mathf.Sin(angle);
                angle += (NextF() - 0.5f) * 0.7f;
                var px = Mathf.RoundToInt(x);
                var py = Mathf.RoundToInt(y);
                _w.Over(px, py, _s.Organic, 0.95f);
                if (step < length / 2) _w.Over(px + 1, py, _s.Organic, 0.9f);
                if (step % 4 == 1)
                {
                    var side = Chance(0.5f) ? 1 : -1;
                    _w.Over(px + side, py, _s.OrganicLight, 0.9f);
                    _w.Over(px + side * 2, py + 1, _s.OrganicLight, 0.8f);
                }
            }
        }
    }
}
