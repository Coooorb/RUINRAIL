using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// The solid cover of every shipped room (Start, Combat incl. elite encounters, Non-Combat, Boss), re-skinned in place (the hazard-art pattern): every cluster of obstacle cells
    /// is split into rectangular pieces of at most 3×2, and each piece is painted as one whole biome object sized to its
    /// footprint — Metro service cabinets, jersey barriers, ticket kiosks, crate stacks; Rustworks riveted machines, scrap
    /// crates, boiler drums, pipe manifolds; Labs cabinets, specimen tanks, server racks, overgrown crates; Cryo pods,
    /// frosted containers, coolant tanks, consoles. A room draws from a curated three-family subset of its biome and
    /// neighbouring pieces never repeat a family, so a room reads as furnished, not stamped. The painting is sliced back
    /// into one tile per original cell with the original collider type: the cells, the grid collider and every gameplay
    /// rule stay exactly as authored. Deterministic per (run seed, depth, node), identical on every peer.
    /// </summary>
    public static class ObstacleArt
    {
        private const int T = GridConstants.TileSizePixels;

        public enum Family
        {
            MetroCabinet, MetroBarrier, MetroKiosk, MetroCrates, MetroRubble,
            RustMachine, RustCrate, RustBoiler, RustBale, RustManifold,
            LabsCabinet, LabsTank, LabsRack, LabsPlanter, LabsBench,
            CryoPod, CryoContainer, CryoTank, CryoConsole, CryoIceCrate
        }

        public static IReadOnlyList<Family> FamiliesOf(Biome biome) => biome switch
        {
            Biome.Rustworks => new[] { Family.RustMachine, Family.RustCrate, Family.RustBoiler, Family.RustBale, Family.RustManifold },
            Biome.OvergrownLabs => new[] { Family.LabsCabinet, Family.LabsTank, Family.LabsRack, Family.LabsPlanter, Family.LabsBench },
            Biome.CryoVaults => new[] { Family.CryoPod, Family.CryoContainer, Family.CryoTank, Family.CryoConsole, Family.CryoIceCrate },
            _ => new[] { Family.MetroCabinet, Family.MetroBarrier, Family.MetroKiosk, Family.MetroCrates, Family.MetroRubble }
        };

        /// <summary>Whether a family can be built at a footprint (tanks and drums want a near-square base, benches and barriers a wide one).</summary>
        public static bool Fits(Family family, int w, int h) => family switch
        {
            Family.RustBoiler or Family.CryoTank => w == h, // a round drum needs a square base, or it leaves solid ground undrawn
            Family.MetroBarrier or Family.LabsBench or Family.RustManifold => w >= 2 && h == 1,
            Family.MetroKiosk or Family.LabsTank or Family.CryoPod => w == 1,
            _ => true
        };

        /// <summary>The pieces a room's obstacle cells are built as (tests / diagnostics).</summary>
        public readonly struct Piece
        {
            public Piece(Vector3Int origin, int w, int h, Family family)
            {
                Origin = origin;
                W = w;
                H = h;
                Family = family;
            }

            public Vector3Int Origin { get; }
            public int W { get; }
            public int H { get; }
            public Family Family { get; }
        }

        public static RoomObstacleArt Apply(RoomRoot root, int runSeed, int depth, int nodeId)
        {
            if (root == null || root.Grid == null || root.Definition == null) return null;
            var map = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Obstacles);
            if (map == null) return null;
            var cells = new HashSet<Vector3Int>();
            foreach (var cell in map.cellBounds.allPositionsWithin)
                if (map.GetTile(cell) != null) cells.Add(cell);
            if (cells.Count == 0) return null;

            var biome = root.Definition.Biome;
            var seed = (uint)Hash(runSeed * 92821 + depth * 6151 + nodeId * 7919, (int)biome, 0x0B57AC1Eu);
            var pieces = Split(cells, biome, seed);
            var owner = root.gameObject.GetComponent<RoomObstacleArt>() ?? root.gameObject.AddComponent<RoomObstacleArt>();
            owner.Pieces = pieces;

            // One texture per room: the pieces stacked in a column, each painted at its whole footprint.
            var width = pieces.Max(p => p.W) * T;
            var height = pieces.Sum(p => p.H * T);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = $"ObstacleArt_{biome}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[width * height];
            var rowOffset = 0;
            var offsets = new List<int>();
            var print = 1469598103934665603UL;
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                var canvas = new RoomEnvironmentCanvas(piece.W * T, piece.H * T);
                ObstaclePainter.Paint(canvas, biome, piece.Family, piece.W, piece.H, Hash(piece.Origin.x, piece.Origin.y, seed));
                for (var y = 0; y < canvas.Height; y++)
                for (var x = 0; x < canvas.Width; x++)
                {
                    var c = canvas.Pixels[y * canvas.Width + x];
                    pixels[(rowOffset + y) * width + x] = c;
                    print = (print ^ (uint)(c.r | c.g << 8 | c.b << 16 | c.a << 24)) * 1099511628211UL;
                }

                offsets.Add(rowOffset);
                rowOffset += canvas.Height;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            owner.Own(texture);
            owner.Fingerprint = print;

            var stem = biome.ToString().ToLowerInvariant();
            for (var i = 0; i < pieces.Count; i++)
            {
                var piece = pieces[i];
                for (var dy = 0; dy < piece.H; dy++)
                for (var dx = 0; dx < piece.W; dx++)
                {
                    var cell = piece.Origin + new Vector3Int(dx, dy, 0);
                    var sprite = Sprite.Create(texture, new Rect(dx * T, offsets[i] + dy * T, T, T), new Vector2(0.5f, 0.5f), T, 0, SpriteMeshType.FullRect);
                    sprite.name = $"{stem}_obstacle_{piece.Family}_{cell.x}_{cell.y}";
                    sprite.hideFlags = HideFlags.DontSave;
                    owner.Own(sprite);
                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.name = sprite.name;
                    tile.sprite = sprite;
                    // The cell keeps exactly the collision it was authored with.
                    tile.colliderType = map.GetColliderType(cell);
                    tile.hideFlags = HideFlags.DontSave;
                    owner.Own(tile);
                    map.SetTile(cell, tile);
                    map.SetTransformMatrix(cell, Matrix4x4.identity);
                }
            }

            owner.Cells = cells.Count;
            return owner;
        }

        /// <summary>
        /// Clusters split into rectangles (widest first, at most 3×2 / 1×3) and a family per piece: a curated subset of the
        /// biome for the room, never repeating the last two pieces or an edge-touching piece when another object fits.
        /// </summary>
        public static List<Piece> Split(HashSet<Vector3Int> cells, Biome biome, uint seed)
        {
            var free = new HashSet<Vector3Int>(cells);
            var ordered = cells.OrderBy(c => c.y).ThenBy(c => c.x).ToList();
            var families = FamiliesOf(biome);
            var start = (int)(seed % (uint)families.Count);
            var subset = new[] { families[start], families[(start + 1) % families.Count], families[(start + 3) % families.Count] };
            var pieces = new List<Piece>();
            var placed = new Dictionary<Vector3Int, Family>();
            Family? last = null, beforeLast = null;
            foreach (var origin in ordered)
            {
                if (!free.Contains(origin)) continue;
                var w = 1;
                while (w < 3 && free.Contains(origin + new Vector3Int(w, 0, 0))) w++;
                var h = 1;
                var maxH = w == 1 ? 3 : 2;
                while (h < maxH && Enumerable.Range(0, w).All(dx => free.Contains(origin + new Vector3Int(dx, h, 0)))) h++;
                for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < w; dx++)
                    free.Remove(origin + new Vector3Int(dx, dy, 0));

                // The room's curated subset first, topped up from the biome's others when few of them fit this footprint.
                var candidates = subset.Where(f => Fits(f, w, h)).ToList();
                for (var k = 0; k < families.Count && candidates.Count < 4; k++)
                {
                    var extra = families[(start + 2 + k) % families.Count];
                    if (Fits(extra, w, h) && !candidates.Contains(extra)) candidates.Add(extra);
                }

                // Never the object placed just before (nor the one before that, when enough fit), never an object this piece
                // touches; a hashed pick among what is left, so long cover runs do not stamp a fixed A, B, C rhythm.
                var touching = new HashSet<Family>();
                for (var dx = -1; dx <= w; dx++)
                for (var dy = -1; dy <= h; dy++)
                    if ((dx < 0 || dx >= w) != (dy < 0 || dy >= h) // edge neighbours, not corners
                        && placed.TryGetValue(origin + new Vector3Int(dx, dy, 0), out var f)) touching.Add(f);
                var pool = candidates.Where(f => f != last).ToList();
                if (pool.Count == 0) pool = families.Where(f => Fits(f, w, h) && f != last).ToList();
                foreach (var narrower in new System.Func<Family, bool>[] { f => f != beforeLast, f => !touching.Contains(f) })
                {
                    var kept = pool.Where(narrower).ToList();
                    if (kept.Count > 0) pool = kept;
                }

                var family = pool.Count > 0 ? pool[(int)(Hash(origin.x, origin.y, seed ^ 0x5EEDu) % (uint)pool.Count)] : candidates[0];
                pieces.Add(new Piece(origin, w, h, family));
                for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < w; dx++)
                    placed[origin + new Vector3Int(dx, dy, 0)] = family;
                beforeLast = last;
                last = family;
            }

            return pieces;
        }
    }

    /// <summary>Owns a room's runtime obstacle art and releases it with the room.</summary>
    [DisallowMultipleComponent]
    public sealed class RoomObstacleArt : MonoBehaviour
    {
        private readonly List<Object> _owned = new();

        public int Cells { get; internal set; }
        public IReadOnlyList<ObstacleArt.Piece> Pieces { get; internal set; } = new List<ObstacleArt.Piece>();

        /// <summary>FNV-1a over the painted pieces: the determinism fingerprint.</summary>
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

    /// <summary>Paints one cover object over a whole w×h-cell footprint, three-quarter view, outlined, filling the footprint so it reads as solid.</summary>
    internal static class ObstaclePainter
    {
        private const int T = GridConstants.TileSizePixels;

        private static readonly Color32 SteelDark = Hex("#2C3236"), Steel = Hex("#566065"), SteelLight = Hex("#8C979C");
        private static readonly Color32 Amber = Hex("#FFB347"), Green = Hex("#7CE08A"), Cyan = Hex("#7FE6F0"), Red = Hex("#E0533A");

        public static void Paint(RoomEnvironmentCanvas c, Biome biome, ObstacleArt.Family family, int w, int h, uint seed)
        {
            var q = new PropPainter(c, Hex("#0E1113"), Hex("#050607"), seed);
            var W = c.Width;
            var H = c.Height;
            // The body fills the footprint (a 2 px rim for the outline and the cast shadow): cover reads as solid.
            int x0 = 2, y0 = 3, bw = W - 6, bh = H - 5;
            var front = Mathf.Clamp(Mathf.RoundToInt(bh * 0.42f), 9, 26);
            var r = new System.Random((int)seed);
            switch (family)
            {
                // ---------------------------------------------------------------- Ruined Metro
                case ObstacleArt.Family.MetroCabinet:
                {
                    q.Block(x0, y0, bw, bh, front, Hex("#2A3238"), Hex("#4A5761"), Hex("#6E7E8A"), Hex("#36424B"));
                    // Louvres on the front, a status lamp, a maintenance stencil on top.
                    for (var y = y0 + 3; y < y0 + front - 2; y += 3) q.Line(x0 + 3, y, x0 + bw - 5, y, Hex("#26303A"));
                    q.Rect(x0 + bw - 7, y0 + front - 4, 2, 2, r.Next(2) == 0 ? Green : Amber);
                    q.Rect(x0 + 4, y0 + front + 3, Mathf.Min(10, bw - 8), 2, Hex("#C9A227"));
                    for (var x = x0 + 4; x < x0 + bw - 4; x += 14) q.Rect(x, y0 + bh - 5, 6, 2, Hex("#3A4650"));
                    break;
                }
                case ObstacleArt.Family.MetroBarrier:
                {
                    // Jersey barrier run: chamfered concrete, a hazard-tape band, bolted joints.
                    q.Block(x0, y0, bw, bh - 2, front, Hex("#5C5F5C"), Hex("#8A8D88"), Hex("#B0B3AC"), Hex("#6E716C"));
                    for (var x = x0; x < x0 + bw; x++) if ((x / 4) % 2 == 0) q.Rect(x, y0 + front / 2, 1, 3, Hex("#D9B227")); else q.Rect(x, y0 + front / 2, 1, 3, Hex("#1C1E20"));
                    for (var x = x0 + T - 2; x < x0 + bw; x += T) q.Line(x, y0 + 1, x, y0 + bh - 4, Hex("#4A4D4A"));
                    for (var x = x0 + 5; x < x0 + bw - 5; x += 9) q.Rect(x, y0 + bh - 6, 3, 2, Hex("#5C5F5C"));
                    break;
                }
                case ObstacleArt.Family.MetroKiosk:
                {
                    // Ticket / vending kiosk: dark body, lit screen, coin slot, a cracked glass corner.
                    q.Block(x0 + 1, y0, bw - 2, bh, Mathf.Min(bh - 6, front + 8), Hex("#1F262C"), Hex("#33404A"), Hex("#56687A"), Hex("#2A3540"));
                    var sy = y0 + Mathf.Min(bh - 6, front + 8) - 12;
                    q.Rect(x0 + 4, sy, bw - 10, 8, Hex("#123B44"));
                    q.Rect(x0 + 5, sy + 5, bw - 14, 1, Cyan);
                    q.Rect(x0 + 5, sy + 2, (bw - 14) / 2, 1, Hex("#3E9AA8"));
                    q.Glow(x0 + bw / 2, sy + 4, 7f, Cyan, 0.15f);
                    q.Rect(x0 + bw / 2 - 2, y0 + 4, 4, 2, Hex("#C9A227"));
                    q.Line(x0 + 4, sy + 7, x0 + 8, sy + 3, Hex("#7AA9B4"));
                    break;
                }
                case ObstacleArt.Family.MetroCrates:
                {
                    // A stack of transit freight crates of two sizes, banded.
                    StackCrates(q, r, x0, y0, bw, bh, Hex("#4E3E2C"), Hex("#6E5A40"), Hex("#8C7350"), Hex("#5A4834"), Hex("#2E2A24"));
                    break;
                }
                case ObstacleArt.Family.MetroRubble:
                {
                    // A collapsed slab with exposed rebar: still a solid block at the base.
                    q.Block(x0, y0, bw, bh - 8, front - 2, Hex("#4C4F4C"), Hex("#727570"), Hex("#9A9D98"), Hex("#5C5F5C"));
                    q.Jagged(x0 + 3, y0 + bh - 14, bw - 6, 12, 4, Hex("#4C4F4C"), Hex("#7A7D78"), Hex("#A4A7A2"), Hex("#5C5F5C"));
                    for (var k = 0; k < 2 + w; k++)
                    {
                        var x = x0 + 5 + r.Next(Mathf.Max(1, bw - 10));
                        q.Line(x, y0 + bh - 10, x + r.Next(-3, 4), y0 + bh - 3, Hex("#7A4A2A"));
                    }

                    // Fracture lines down the face and spalled patches on the top, so the slab is not a flat block.
                    for (var k = 0; k < 1 + w; k++)
                    {
                        var cx = x0 + 6 + r.Next(Mathf.Max(1, bw - 12));
                        q.Line(cx, y0 + 2, cx + r.Next(-2, 3), y0 + front / 2, Hex("#3A3C3A"));
                        q.Line(cx + 1, y0 + front / 2, cx + r.Next(-3, 4), y0 + front - 3, Hex("#3A3C3A"));
                    }

                    for (var k = 0; k < 2 + w; k++)
                        q.Rect(x0 + 4 + r.Next(Mathf.Max(1, bw - 12)), y0 + front + 2 + r.Next(Mathf.Max(1, bh - front - 18)), 3 + r.Next(3), 2, Hex("#5E615C"));
                    break;
                }

                // ---------------------------------------------------------------- Rustworks
                case ObstacleArt.Family.RustMachine:
                {
                    q.Block(x0, y0, bw, bh, front, Hex("#3E2216"), Hex("#6E3C24"), Hex("#955A36"), Hex("#542E1C"));
                    for (var x = x0 + 3; x < x0 + bw - 3; x += 5) { q.Px(x, y0 + front - 2, Hex("#C08850")); q.Px(x, y0 + bh - 3, Hex("#C08850")); }
                    var gx = x0 + bw - 9;
                    q.Disc(gx, y0 + front + (bh - front) / 2, 4, SteelDark, Steel, SteelLight);
                    q.Line(gx, y0 + front + (bh - front) / 2, gx + 2, y0 + front + (bh - front) / 2 + 2, Red);
                    q.Rect(x0 + 4, y0 + 3, Mathf.Min(12, bw - 10), front - 6, Hex("#2A1810"));
                    q.Glow(x0 + 9, y0 + 5, 6f, Amber, 0.25f);
                    q.Rect(x0 + 6, y0 + 4, 3, 2, Amber);
                    break;
                }
                case ObstacleArt.Family.RustCrate:
                {
                    q.Block(x0, y0, bw, bh, front, Hex("#2C2A26"), Hex("#4C463E"), Hex("#6E665A"), Hex("#3A3630"));
                    for (var x = x0; x < x0 + bw; x++) if (((x + 2) / 4) % 2 == 0) q.Rect(x, y0 + front - 4, 1, 3, Hex("#D9832A")); else q.Rect(x, y0 + front - 4, 1, 3, Hex("#1A1816"));
                    q.Rect(x0 + 4, y0 + 3, 6, 4, Hex("#8A7A5A"));
                    for (var x = x0 + 1; x < x0 + bw - 1; x += Mathf.Max(8, bw / 3)) q.Line(x, y0 + front + 1, x, y0 + bh - 2, Hex("#2C2A26"));
                    for (var k = 0; k < 3 * w; k++) q.Px(x0 + r.Next(bw), y0 + r.Next(front), Hex("#7A3A1A"));
                    break;
                }
                case ObstacleArt.Family.RustBoiler:
                {
                    // A riveted boiler drum on its cradle, seen from above, a pressure valve on top.
                    var cx = W / 2 - 1;
                    var cy = H / 2 + 1;
                    var rad = Mathf.Min(bw, bh) / 2 - 1;
                    q.Block(x0 + 2, y0, bw - 4, Mathf.Max(8, bh / 3), 5, Hex("#2A1810"), Hex("#4A2C1C"), Hex("#6A4028"), Hex("#3A2216"));
                    q.Disc(cx, cy, rad, Hex("#4A2416"), Hex("#7E4228"), Hex("#A8643A"));
                    q.Ring(cx, cy, rad - 2, Hex("#3A1C10"));
                    for (var k = 0; k < 10; k++)
                    {
                        var a = k * Mathf.PI * 2f / 10f;
                        q.Px(cx + Mathf.RoundToInt(Mathf.Cos(a) * (rad - 3)), cy + Mathf.RoundToInt(Mathf.Sin(a) * (rad - 3)), Hex("#C08850"));
                    }

                    q.Disc(cx, cy, Mathf.Max(2, rad / 4), SteelDark, Steel, SteelLight);
                    q.Rect(cx - 1, cy + rad / 4 + 1, 2, 3, Red);
                    break;
                }
                case ObstacleArt.Family.RustBale:
                {
                    // Compacted scrap bale: mixed metal strata held by two wire bands.
                    q.Block(x0, y0, bw, bh, front, Hex("#3A322A"), Hex("#5A4E40"), Hex("#7A6A58"), Hex("#463C32"));
                    var cols = new[] { Hex("#6E3C24"), Hex("#566065"), Hex("#4E5A3E"), Hex("#8C5A30"), Hex("#3C4446") };
                    for (var k = 0; k < 10 * w * h; k++)
                    {
                        var x = x0 + 2 + r.Next(Mathf.Max(1, bw - 6));
                        var y = y0 + 2 + r.Next(Mathf.Max(1, bh - 5));
                        q.Rect(x, y, 2 + r.Next(4), 1 + r.Next(2), cols[r.Next(cols.Length)]);
                    }

                    q.Line(x0 + bw / 3, y0, x0 + bw / 3, y0 + bh - 1, Hex("#B8B0A0"));
                    q.Line(x0 + 2 * bw / 3, y0, x0 + 2 * bw / 3, y0 + bh - 1, Hex("#B8B0A0"));
                    break;
                }
                case ObstacleArt.Family.RustManifold:
                {
                    // A pipe manifold block: two horizontal pipes with flanges and valve wheels.
                    q.Block(x0, y0, bw, bh, front, Hex("#2A1810"), Hex("#4A2C1C"), Hex("#6A4028"), Hex("#3A2216"));
                    for (var p = 0; p < 2; p++)
                    {
                        var py = y0 + front + 3 + p * 6;
                        q.Rect(x0 + 1, py, bw - 4, 4, Hex("#7E4228"));
                        q.Rect(x0 + 1, py + 3, bw - 4, 1, Hex("#A8643A"));
                        for (var x = x0 + 6; x < x0 + bw - 6; x += T) q.Rect(x, py - 1, 2, 6, Hex("#3A1C10"));
                    }

                    for (var x = x0 + T / 2; x < x0 + bw - 4; x += T)
                    {
                        q.Ring(x, y0 + front / 2 + 1, 3, Red);
                        q.Px(x, y0 + front / 2 + 1, Hex("#3A1C10"));
                    }

                    break;
                }

                // ---------------------------------------------------------------- Overgrown Labs
                case ObstacleArt.Family.LabsCabinet:
                {
                    q.Block(x0, y0, bw, bh, front, Hex("#8A8E86"), Hex("#C4C8BE"), Hex("#E2E5DC"), Hex("#A6AAA0"));
                    for (var x = x0 + 3; x + 12 <= x0 + bw - 3; x += 15)
                    {
                        q.Rect(x, y0 + front + 3, 11, bh - front - 7, Hex("#1A3A3A"));
                        q.Rect(x + 1, y0 + bh - 7, 9, 1, Hex("#4FB8B0"));
                    }

                    q.Rect(x0 + 2, y0 + front - 4, bw - 6, 1, Hex("#3E9A90"));
                    q.Vines(x0, y0 + front, Mathf.Max(6, bw / 2), bh - front, Hex("#2C4524"), Hex("#5F8A3A"));
                    break;
                }
                case ObstacleArt.Family.LabsTank:
                {
                    // A specimen tank: metal base and cap, glass column of green fluid, rising bubbles.
                    var top = y0 + bh - 6;
                    q.Block(x0 + 1, y0, bw - 2, 7, 5, Hex("#6E7268"), Hex("#9EA298"), Hex("#C2C6BC"), Hex("#868A80"));
                    q.Rect(x0 + 4, y0 + 7, bw - 8, top - y0 - 7, Hex("#2E6A4A"));
                    q.Rect(x0 + 4, y0 + 7, 2, top - y0 - 7, Hex("#7ACF9A"));
                    q.Rect(x0 + bw - 7, y0 + 7, 1, top - y0 - 7, Hex("#1E4A34"));
                    for (var k = 0; k < 4 + h * 2; k++) q.Px(x0 + 6 + r.Next(Mathf.Max(1, bw - 12)), y0 + 9 + r.Next(Mathf.Max(1, top - y0 - 12)), Hex("#B8F0C8"));
                    q.Block(x0 + 2, top, bw - 4, 5, 2, Hex("#6E7268"), Hex("#9EA298"), Hex("#C2C6BC"), Hex("#868A80"));
                    q.Glow(W / 2, y0 + bh / 2, 10f, Green, 0.18f);
                    break;
                }
                case ObstacleArt.Family.LabsRack:
                {
                    q.Block(x0, y0, bw, bh, front + 4, Hex("#1A1E1E"), Hex("#2E3434"), Hex("#4A5252"), Hex("#242A2A"));
                    for (var y = y0 + 3; y < y0 + front + 1; y += 4)
                    {
                        q.Line(x0 + 3, y, x0 + bw - 5, y, Hex("#3A4242"));
                        for (var x = x0 + 4; x < x0 + bw - 6; x += 5) if (r.Next(3) == 0) q.Px(x, y + 1, r.Next(4) == 0 ? Amber : Hex("#5ED0B0"));
                    }

                    q.Rect(x0 + 2, y0 + bh - 4, bw - 6, 1, Hex("#5A6262"));
                    q.Vines(x0 + bw / 2, y0 + front, Mathf.Max(5, bw / 2 - 2), bh - front, Hex("#2C4524"), Hex("#5F8A3A"));
                    break;
                }
                case ObstacleArt.Family.LabsPlanter:
                {
                    // An off-white containment crate burst open by growth: moss on top, vines down the front.
                    q.Block(x0, y0, bw, bh, front, Hex("#8A8E86"), Hex("#B4B8AE"), Hex("#D4D7CE"), Hex("#9A9E94"));
                    q.Rect(x0 + 2, y0 + front - 4, bw - 6, 2, Hex("#4FB8B0"));
                    for (var k = 0; k < 18 * w * h; k++)
                    {
                        var x = x0 + 2 + r.Next(Mathf.Max(1, bw - 6));
                        var y = y0 + front + 2 + r.Next(Mathf.Max(1, bh - front - 4));
                        q.Px(x, y, r.Next(3) == 0 ? Hex("#7FB04A") : Hex("#4A7030"));
                    }

                    q.Vines(x0 + 1, y0, bw - 4, front, Hex("#2C4524"), Hex("#5F8A3A"));
                    q.Px(x0 + bw / 2, y0 + bh - 5, Hex("#E07ACF"));
                    break;
                }
                case ObstacleArt.Family.LabsBench:
                {
                    // A lab bench: white top with flasks and a microscope, a drawer front.
                    q.Block(x0, y0, bw, bh, front, Hex("#7C8078"), Hex("#C8CCC2"), Hex("#E6E9E0"), Hex("#9A9E94"));
                    for (var x = x0 + 4; x < x0 + bw - 8; x += 9) q.Rect(x, y0 + 3, 6, front - 7, Hex("#8A8E86"));
                    for (var x = x0 + 6; x < x0 + bw - 10; x += 11)
                    {
                        var col = r.Next(2) == 0 ? Hex("#4FB8B0") : Hex("#9AD06A");
                        q.Rect(x, y0 + front + 4, 4, 5, Hex("#D8E8E8"));
                        q.Rect(x, y0 + front + 4, 4, 2, col);
                    }

                    q.Rect(x0 + bw - 12, y0 + front + 3, 3, 8, SteelDark);
                    q.Rect(x0 + bw - 14, y0 + front + 3, 7, 2, SteelDark);
                    break;
                }

                // ---------------------------------------------------------------- Cryo Vaults
                case ObstacleArt.Family.CryoPod:
                {
                    // A cryo pod lying in its cradle: frosted window, cyan glow, a figure's silhouette.
                    q.Block(x0, y0, bw, bh, front, Hex("#1C242C"), Hex("#2E3C48"), Hex("#4A5E6C"), Hex("#243038"));
                    var wy0 = y0 + front + 3;
                    var wh = bh - front - 7;
                    q.Rect(x0 + 4, wy0, bw - 10, wh, Hex("#1A4A58"));
                    q.Rect(x0 + bw / 2 - 3, wy0 + 2, 4, Mathf.Max(3, wh - 4), Hex("#2A6070"));
                    q.Frost(x0 + 4, wy0 + wh / 2, bw - 10, wh / 2, Hex("#8FB0BE"), Hex("#E2F3F8"));
                    q.Glow(x0 + bw / 2, wy0 + wh / 2, 10f, Cyan, 0.2f);
                    q.Rect(x0 + 3, y0 + front - 4, 3, 2, Cyan);
                    break;
                }
                case ObstacleArt.Family.CryoContainer:
                {
                    q.Block(x0, y0, bw, bh, front, Hex("#26323C"), Hex("#3C4C5A"), Hex("#5A6E7E"), Hex("#2E3C48"));
                    for (var x = x0 + 4; x < x0 + bw - 4; x += 6) q.Line(x, y0 + 2, x, y0 + front - 3, Hex("#2A3640"));
                    q.Rect(x0 + bw - 12, y0 + 4, 6, 4, Amber);
                    q.Frost(x0, y0 + front, bw - 2, bh - front, Hex("#8FB0BE"), Hex("#E2F3F8"));
                    break;
                }
                case ObstacleArt.Family.CryoTank:
                {
                    var cx = W / 2 - 1;
                    var cy = H / 2 + 1;
                    var rad = Mathf.Min(bw, bh) / 2 - 1;
                    q.Block(x0 + 2, y0, bw - 4, Mathf.Max(8, bh / 3), 5, Hex("#1C242C"), Hex("#2E3C48"), Hex("#4A5E6C"), Hex("#243038"));
                    q.Disc(cx, cy, rad, Hex("#2E3C48"), Hex("#4A5E6C"), Hex("#7A92A2"));
                    q.Ring(cx, cy, rad - 3, Hex("#7FE6F0"));
                    q.Ring(cx, cy, Mathf.Max(2, rad / 2), Hex("#26323C"));
                    q.Frost(cx - rad, cy, rad * 2, rad, Hex("#8FB0BE"), Hex("#E2F3F8"));
                    q.Glow(cx, cy, rad * 0.8f, Cyan, 0.12f);
                    break;
                }
                case ObstacleArt.Family.CryoConsole:
                {
                    q.Block(x0, y0, bw, bh, front + 2, Hex("#1C242C"), Hex("#2A3640"), Hex("#44566A"), Hex("#222C36"));
                    var sy = y0 + front + 4;
                    for (var x = x0 + 3; x + 10 <= x0 + bw - 4; x += 13)
                    {
                        q.Rect(x, sy, 10, bh - front - 9, Hex("#0E2A34"));
                        q.Rect(x + 1, sy + 2, 6, 1, Cyan);
                        q.Rect(x + 1, sy + 4, 3, 1, Hex("#3E9AA8"));
                    }

                    for (var x = x0 + 4; x < x0 + bw - 6; x += 4) q.Px(x, y0 + front - 3, r.Next(3) == 0 ? Amber : Hex("#3E9AA8"));
                    q.Frost(x0, y0 + bh - 6, bw - 2, 6, Hex("#8FB0BE"), Hex("#E2F3F8"));
                    break;
                }
                case ObstacleArt.Family.CryoIceCrate:
                {
                    // A supply crate sealed in a growth of ice: the crate shows through a translucent cap.
                    q.Block(x0, y0, bw, bh, front, Hex("#2E2A24"), Hex("#4E4232"), Hex("#6A5A44"), Hex("#3A3226"));
                    // Planks and steel bands on the exposed front so it reads as a crate, not a mound.
                    for (var y = y0 + 4; y < y0 + front - 2; y += 4) q.Line(x0 + 1, y, x0 + bw - 3, y, Hex("#2A241C"));
                    for (var x = x0 + 5; x < x0 + bw - 3; x += 12) q.Rect(x, y0 + 1, 2, front - 3, Steel);
                    for (var y = y0 + front; y < y0 + bh; y++)
                    for (var x = x0; x < x0 + bw; x++)
                        if ((((x / 3) * 7 + (y / 3) * 3) % 11) < 5) c.Over(x, y, Hex("#BFE4EE"), 0.28f);
                    q.Frost(x0, y0 + front - 4, bw, bh - front + 4, Hex("#8FB0BE"), Hex("#E2F3F8"));
                    for (var k = 0; k < 2 + w; k++) q.Shard(x0 + 4 + r.Next(Mathf.Max(1, bw - 8)), y0 + bh - 6 - r.Next(4), Hex("#9FD0DE"), Hex("#E2F3F8"));
                    break;
                }
            }

            q.Finish();
        }

        private static void StackCrates(PropPainter q, System.Random r, int x0, int y0, int bw, int bh, Color32 dark, Color32 mid, Color32 light, Color32 front, Color32 band)
        {
            // A big crate at the back, a smaller one stacked in front of it: one solid mass with a readable step.
            var backH = bh;
            q.Block(x0, y0, bw, backH, Mathf.RoundToInt(backH * 0.4f), dark, mid, light, front);
            for (var x = x0 + 2; x < x0 + bw - 2; x += 6) q.Line(x, y0 + Mathf.RoundToInt(backH * 0.4f) + 2, x, y0 + backH - 3, Shade(mid, 0.82f));
            var sw = Mathf.Max(10, bw / 2);
            var sx = x0 + r.Next(Mathf.Max(1, bw - sw));
            var sh = Mathf.Max(10, backH / 2);
            q.Block(sx, y0, sw, sh, Mathf.RoundToInt(sh * 0.5f), dark, Shade(mid, 1.08f), Shade(light, 1.05f), Shade(front, 1.05f));
            q.Rect(sx, y0 + Mathf.RoundToInt(sh * 0.25f), sw, 1, band);
            q.Rect(sx + sw / 2 - 1, y0 + 1, 2, Mathf.RoundToInt(sh * 0.5f) - 2, band);
        }
    }
}
