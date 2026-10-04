using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The damaging-floor art of a room, painted per hazard cell so a hazard reads as one shaped, framed installation
    /// instead of a repeated tile: the region's outer boundary carries a clear edge (warning-striped track lip, riveted
    /// furnace frame, corroded foam rim, frost crust) and the interior is one continuous animated surface across cells.
    ///   Ruined Metro — Electrified Rail: a ballast trench with sleepers, a live third rail on ceramic insulators,
    ///     current pulsing along it and arcs jumping to the ballast, framed by worn yellow-black edge stripes.
    ///   Rustworks — Furnace Grate: a riveted iron frame and grate bars over a flickering furnace pit, embers rising.
    ///   Overgrown Labs — Acid Pool: toxic green liquid deepening toward the middle, drifting highlights, bubbles that
    ///     swell and pop, a corroded foam rim.
    ///   Cryo Vaults — Cryo Coolant Leak: pale coolant with a shimmering caustic surface, vapour drifting across it and
    ///     a white frost crust along its edge.
    ///
    /// Presentation only, and the gameplay contract is kept exactly: the painted cells are the cells the room already
    /// paints (same cells, same count), each re-skinned in place with a runtime <see cref="HazardAnimatedTile"/> whose
    /// collider type is None like the original, whose name keeps the biome stem, and which loops 8 frames at the
    /// original tile's rate; damage, ticks, the RoomHazard trigger boxes and every hazard definition are untouched.
    /// Every pixel is a pure function of (biome, room cell, frame), so every peer draws the same floor.
    /// </summary>
    public static class HazardArt
    {
        public const int Frames = 8;
        private const int T = GridConstants.TileSizePixels;

        /// <summary>Re-skins the room's painted hazard cells. Returns the owner of the runtime art, or null when there are none.</summary>
        public static RoomHazardArt Apply(RoomRoot root)
        {
            if (root == null || root.Grid == null || root.Definition == null) return null;
            var map = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Hazards);
            if (map == null) return null;
            var cells = new List<Vector3Int>();
            var original = new Dictionary<Vector3Int, TileBase>();
            foreach (var cell in map.cellBounds.allPositionsWithin)
            {
                var tile = map.GetTile(cell);
                if (tile == null) continue;
                cells.Add(cell);
                original[cell] = tile;
            }

            if (cells.Count == 0) return null;
            var biome = root.Definition.Biome;
            var stem = biome.ToString().ToLowerInvariant();
            var set = new HashSet<Vector3Int>(cells);
            var owner = root.gameObject.GetComponent<RoomHazardArt>() ?? root.gameObject.AddComponent<RoomHazardArt>();

            var texture = new Texture2D(Frames * T, cells.Count * T, TextureFormat.RGBA32, false, false)
            {
                name = $"HazardArt_{stem}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color32[texture.width * texture.height];
            var painter = new HazardPainter(biome, set);
            for (var i = 0; i < cells.Count; i++)
            for (var f = 0; f < Frames; f++)
            for (var ly = 0; ly < T; ly++)
            for (var lx = 0; lx < T; lx++)
                pixels[(i * T + ly) * texture.width + f * T + lx] = painter.Pixel(cells[i], lx, ly, f);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            owner.Own(texture);
            var print = 1469598103934665603UL;
            foreach (var p in pixels) print = (print ^ (uint)(p.r | p.g << 8 | p.b << 16 | p.a << 24)) * 1099511628211UL;
            owner.Fingerprint = print;

            for (var i = 0; i < cells.Count; i++)
            {
                var frames = new Sprite[Frames];
                for (var f = 0; f < Frames; f++)
                {
                    frames[f] = Sprite.Create(texture, new Rect(f * T, i * T, T, T), new Vector2(0.5f, 0.5f), T, 0, SpriteMeshType.FullRect);
                    frames[f].name = $"{stem}_hazard_{cells[i].x}_{cells[i].y}_{f}";
                    owner.Own(frames[f]);
                }

                var source = original[cells[i]] as HazardAnimatedTile;
                var tile = ScriptableObject.CreateInstance<HazardAnimatedTile>();
                tile.name = $"{stem}_hazard_cell_{cells[i].x}_{cells[i].y}";
                // One continuous surface across cells: every cell plays the same clock (its frames already differ by place).
                tile.Configure(frames, source != null ? source.FramesPerSecond : 6f, false);
                owner.Own(tile);
                map.SetTile(cells[i], tile);
            }

            owner.Cells = cells.Count;
            return owner;
        }
    }

    /// <summary>Owns a room's runtime hazard art and releases it with the room.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomHazardArt : MonoBehaviour
    {
        private readonly List<Object> _owned = new();

        public int Cells { get; internal set; }

        /// <summary>FNV-1a over the painted frames: the determinism fingerprint tests compare across builds of one room.</summary>
        public ulong Fingerprint { get; internal set; }

        internal void Own(Object o) => _owned.Add(o);

        private void OnDestroy()
        {
            foreach (var o in _owned)
            {
                if (o == null) continue;
                if (Application.isPlaying) Destroy(o);
                else DestroyImmediate(o);
            }

            _owned.Clear();
        }
    }

    internal sealed class HazardPainter
    {
        private const int T = GridConstants.TileSizePixels;
        private readonly Biome _biome;
        private readonly HashSet<Vector3Int> _cells;

        public HazardPainter(Biome biome, HashSet<Vector3Int> cells)
        {
            _biome = biome;
            _cells = cells;
        }

        private bool Has(Vector3Int c) => _cells.Contains(c);

        private static readonly Dictionary<string, Color32> Colors = new();

        /// <summary>A cached palette colour (the painter asks for the same few dozen colours per pixel).</summary>
        private static Color32 H(string hex)
        {
            if (!Colors.TryGetValue(hex, out var c)) Colors[hex] = c = Hex(hex);
            return c;
        }

        /// <summary>Pixels from the region's outer boundary (0 on the boundary), from this cell's own neighbourhood.</summary>
        private float Edge(Vector3Int c, int lx, int ly)
        {
            var e = 99f;
            bool n = Has(c + Vector3Int.up), s = Has(c + Vector3Int.down), w = Has(c + Vector3Int.left), ea = Has(c + Vector3Int.right);
            if (!n) e = Mathf.Min(e, T - 1 - ly);
            if (!s) e = Mathf.Min(e, ly);
            if (!w) e = Mathf.Min(e, lx);
            if (!ea) e = Mathf.Min(e, T - 1 - lx);
            // Inner corners of an L-shaped region.
            if (n && w && !Has(c + new Vector3Int(-1, 1, 0))) e = Mathf.Min(e, Mathf.Sqrt(lx * lx + (T - 1 - ly) * (T - 1 - ly)));
            if (n && ea && !Has(c + new Vector3Int(1, 1, 0))) e = Mathf.Min(e, Mathf.Sqrt((T - 1 - lx) * (T - 1 - lx) + (T - 1 - ly) * (T - 1 - ly)));
            if (s && w && !Has(c + new Vector3Int(-1, -1, 0))) e = Mathf.Min(e, Mathf.Sqrt(lx * lx + ly * ly));
            if (s && ea && !Has(c + new Vector3Int(1, -1, 0))) e = Mathf.Min(e, Mathf.Sqrt((T - 1 - lx) * (T - 1 - lx) + ly * ly));
            return e;
        }

        public Color32 Pixel(Vector3Int cell, int lx, int ly, int frame)
        {
            var gx = cell.x * T + lx;
            var gy = cell.y * T + ly;
            var t = frame / (float)HazardArt.Frames;
            var e = Edge(cell, lx, ly);
            return _biome switch
            {
                Biome.Rustworks => Furnace(cell, gx, gy, lx, ly, e, t),
                Biome.OvergrownLabs => Acid(gx, gy, e, t),
                Biome.CryoVaults => Coolant(gx, gy, e, t),
                _ => Rail(cell, gx, gy, lx, ly, e, t)
            };
        }

        private static Color32 Mix(Color32 a, Color32 b, float k) => Color32.Lerp(a, b, Mathf.Clamp01(k));

        /// <summary>A loop-safe noise: value noise sampled around a circle in time, so frame 8 meets frame 0.</summary>
        private static float Loop(float x, float y, float scale, float t, float radius, uint seed)
        {
            var a = t * Mathf.PI * 2f;
            return Value(x + Mathf.Cos(a) * radius * scale, y + Mathf.Sin(a) * radius * scale, scale, seed);
        }

        // ---------------------------------------------------------------- Ruined Metro: electrified rail

        private Color32 Rail(Vector3Int cell, int gx, int gy, int lx, int ly, float e, float t)
        {
            var horizontal = Has(cell + Vector3Int.left) || Has(cell + Vector3Int.right) || !(Has(cell + Vector3Int.up) || Has(cell + Vector3Int.down));
            var along = horizontal ? gx : gy;
            var across = horizontal ? ly : lx;

            // The trench edge: dark lip, then worn yellow-black warning stripes.
            if (e < 1f) return H("#0B0E10");
            if (e < 4f)
            {
                var stripe = ((gx + gy) / 3 & 1) == 0;
                var worn = White(gx / 2, gy / 2, 5u) < 0.12f;
                return stripe && !worn ? (e < 2f ? H("#A8861E") : H("#C9A227")) : H("#1E2224");
            }

            // Ballast: dark gravel with sleepers across the track.
            var c = White(gx, gy, 3u) < 0.18f ? H("#323A3E") : White(gx, gy, 4u) < 0.12f ? H("#262D31") : H("#1B2124");
            if (e < 6f) c = Mix(c, H("#07090A"), 0.5f); // shadow under the lip
            if (along % 10 < 3 && across > 6 && across < 25) c = along % 10 == 0 ? H("#4A403A") : H("#3A322D");

            // The third rail on its insulators.
            var pulse = 0.5f + 0.5f * Mathf.Sin((t * 2f - along / 28f) * Mathf.PI * 2f);
            if (across >= 13 && across <= 18)
            {
                if (along % 16 < 3 && across >= 17) return H("#C9B98A"); // insulator
                var rail = across == 18 ? H("#C2CACC") : across >= 16 ? H("#8A9396") : across == 13 ? H("#2A3134") : H("#5F686C");
                if (across == 18) rail = Mix(rail, H("#B8FBFF"), 0.35f + 0.65f * pulse);
                return rail;
            }

            // Current bleeding off the rail onto the ballast.
            var near = Mathf.Min(Mathf.Abs(across - 13), Mathf.Abs(across - 18));
            if (near <= 4) c = Mix(c, H("#3FC9E0"), (0.35f - near * 0.07f) * (0.4f + 0.6f * pulse));

            // Arcs: each 24-px segment fires on some frames, a jagged bolt from the rail toward the trench edge.
            var seg = Mathf.FloorToInt(along / 24f);
            var frame = Mathf.RoundToInt(t * HazardArt.Frames);
            if (White(seg, frame, 9u) < 0.45f)
            {
                var root = seg * 24 + 4 + (int)(White(seg, frame, 10u) * 16f);
                var up = White(seg, frame, 11u) < 0.5f;
                var from = up ? 19 : 4;
                var to = up ? 27 : 12;
                if (across >= from && across <= to)
                {
                    var jag = (int)(White(seg * 31 + across / 2, frame, 12u) * 3f) - 1;
                    var at = root + jag;
                    if (along == at) return H("#F2FFFF");
                    if (Mathf.Abs(along - at) == 1) return Mix(c, H("#7FEFFF"), 0.75f);
                }
            }

            return c;
        }

        // ---------------------------------------------------------------- Rustworks: furnace grate

        private Color32 Furnace(Vector3Int cell, int gx, int gy, int lx, int ly, float e, float t)
        {
            // Heavy riveted iron frame on the region's boundary.
            if (e < 1f) return H("#0E0B0A");
            if (e < 4f)
            {
                var rivet = Mathf.Approximately(Mathf.Floor(e), 2f) && (gx + gy) % 8 == 0;
                if (rivet) return H("#A89A8C");
                return e < 2f ? H("#5E5853") : e < 3f ? H("#3E3A37") : H("#2A2624");
            }

            // The fire below, seen through the grate: a loop-safe flicker, deep red at the frame, hot in the middle.
            var horizontal = Has(cell + Vector3Int.left) || Has(cell + Vector3Int.right) || !(Has(cell + Vector3Int.up) || Has(cell + Vector3Int.down));
            var along = horizontal ? gx : gy;
            var across = horizontal ? ly : lx;
            var heat = 0.55f * Loop(gx, gy, 9f, t, 0.9f, 21u) + 0.35f * Loop(gx, gy, 4f, t, 1.3f, 22u);
            heat *= 0.5f + 0.5f * Mathf.Clamp01((e - 4f) / 7f);
            Color32 fire = heat < 0.3f ? H("#3A0E06") : heat < 0.42f ? H("#6E1A0A") : heat < 0.54f ? H("#A8300E") : heat < 0.66f ? H("#E0581A") : heat < 0.78f ? H("#FF9A34") : H("#FFD878");

            // Heavy transverse bars every 6 px on two longitudinal rails, lit on the leading edge.
            var bar = along % 6;
            if (bar < 2) return Mix(bar == 0 ? H("#55504B") : H("#2A2522"), H("#B4461A"), heat > 0.6f ? 0.3f : 0.08f);
            if (across == 10 || across == 21) return Mix(H("#1E1A18"), H("#8E3A16"), heat > 0.6f ? 0.3f : 0.1f);

            // Embers lifting off the hottest openings, one stream per opening, looping with the animation.
            var slot = along / 6;
            if (White(slot, across / 11, 23u) < 0.35f)
            {
                var ex = slot * 6 + 3 + (int)(White(slot, across / 11, 24u) * 2f);
                var ey = (int)(((White(slot, across / 11, 25u) + t) % 1f) * 11f) + (across / 11) * 11;
                if (along == ex && across == ey) return H("#FFF2B0");
            }

            return fire;
        }

        // ---------------------------------------------------------------- Overgrown Labs: acid pool

        private Color32 Acid(int gx, int gy, float e, float t)
        {
            if (e < 1f) return H("#16220F");
            if (e < 4f)
            {
                // Corroded foam rim: frothy and alive.
                var froth = Loop(gx, gy, 2.5f, t, 1.5f, 31u);
                return froth > 0.62f ? H("#D8F27A") : froth > 0.4f ? H("#A6CF4E") : H("#6E9A30");
            }

            var depth = Mathf.Clamp01((e - 4f) / 12f);
            var c = Mix(H("#86C93A"), H("#3C7F1F"), depth);
            // Darker swirls of something denser in the acid.
            var swirl = Loop(gx, gy, 9f, t, 0.8f, 32u);
            if (swirl > 0.66f) c = Mix(c, H("#2E5E18"), 0.55f);
            // Sparse glints that wander over the surface.
            if (Loop(gx, gy, 5f, t, 1.4f, 33u) > 0.8f && White(gx, gy, 38u) < 0.35f) c = H("#C8F27E");

            // Bubbles: one per 10-px patch, swelling over the loop at its own phase, then popping into droplets.
            var px = Mathf.FloorToInt(gx / 10f);
            var py = Mathf.FloorToInt(gy / 10f);
            if (White(px, py, 34u) < 0.4f)
            {
                var cx = px * 10 + 3 + (int)(White(px, py, 35u) * 4f);
                var cy = py * 10 + 3 + (int)(White(px, py, 36u) * 4f);
                var phase = (t + White(px, py, 37u)) % 1f;
                var d = Mathf.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy));
                if (phase < 0.75f)
                {
                    var r = 1f + phase * 3f;
                    if (r < 1.6f) { if (gx == cx && gy == cy) return H("#C8F07A"); }
                    else
                    {
                        if (d < r + 0.5f && d >= r - 0.5f) return H("#B6E05A");
                        if (d < r - 0.5f) return d < 1f && gx <= cx ? H("#EAFFC0") : Mix(c, H("#9AD84A"), 0.5f);
                    }
                }
                else if (Mathf.Abs(d - 3.5f) < 0.5f && (gx * 3 + gy) % 4 == 0) return H("#B6E05A");
            }

            return c;
        }

        // ---------------------------------------------------------------- Cryo Vaults: coolant leak

        private Color32 Coolant(int gx, int gy, float e, float t)
        {
            if (e < 1f) return H("#122029");
            // Jagged frost crust along the edge.
            var crust = 3f + 3f * Value(gx, gy, 3f, 41u);
            if (e < crust)
            {
                var glitter = White(gx, gy, 42u) < 0.08f && Mathf.Sin((t + White(gx, gy, 43u)) * Mathf.PI * 2f) > 0.6f;
                return glitter ? H("#FFFFFF") : e < crust * 0.5f ? H("#BFE3EE") : H("#E6F6FB");
            }

            var depth = Mathf.Clamp01((e - crust) / 12f);
            var c = Mix(H("#8FDCEC"), H("#2F7F99"), depth);
            // Caustic shimmer on the surface.
            var a = t * Mathf.PI * 2f;
            var wave = Mathf.Sin(gx / 5f + Mathf.Sin(gy / 7f + a) * 1.6f + a);
            if (Mathf.Abs(wave) < 0.12f) c = Mix(c, H("#E2FBFF"), 0.75f);
            // Vapour drifting over it, on a loop-safe path.
            var vapour = Loop(gx, gy, 10f, t, 1.2f, 44u);
            if (vapour > 0.66f) c = Mix(c, H("#F4FCFF"), 0.45f);
            // A few slivers of ice afloat.
            if (White(gx / 3, gy / 2, 45u) < 0.015f) c = H("#F2FBFF");
            return c;
        }
    }
}
