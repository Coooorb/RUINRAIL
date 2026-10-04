using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Core.Rng;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// What the camera shows around the rooms: the biome's surrounding structures, laid out over the layout rect minus
    /// every room, on top of the substrate underlay.
    ///   Ruined Metro — tunnel beds and ballast, rail tracks, derelict train cars, support pillars, collapsed slabs.
    ///   Rustworks — foundry floor plates, pipe runs, storage tanks, presses, furnaces, scrap and beam stacks.
    ///   Overgrown Labs — service decks gone to moss, cable ducts, containment tanks, server rows, broken pods.
    ///   Cryo Vaults — insulated subfloor and frost, coolant lines, freezer units, compressors, storage racks, cryo tanks.
    /// Every room sits in a one-cell moat of its own cast shadow, so its walls stay the crispest edge on screen.
    ///
    /// Presentation only: three tilemaps and a set of sprites on the Ground layer between the substrate (-1000) and the
    /// floor tilemaps (0); no collider, no tile on any room tilemap, nothing inside a room rect, so navigation, rooms,
    /// doors, encounters and the minimap never see it. Darker and quieter than every biome floor. The art kit is painted
    /// once per biome per session; the layout is a pure function of (run seed, depth, biome, room rects), so every peer
    /// builds the same surroundings.
    /// </summary>
    public sealed class DungeonSurroundings : MonoBehaviour
    {
        public const int GroundOrder = -990;
        public const int PatchOrder = -987;
        public const int LinearOrder = -985;
        public const int PropOrder = -980;
        public const int ShadowOrder = -975;
        /// <summary>Cells painted beyond the layout rect (the camera clamps inside it; this only covers edge rounding).</summary>
        public const int Margin = 3;

        public Biome Biome { get; private set; }
        public RectInt Region { get; private set; }
        public int GroundCells { get; private set; }
        public int LinearCells { get; private set; }
        public int Props { get; private set; }
        public int ShadowCells { get; private set; }
        /// <summary>FNV-1a over every tile and prop choice: the determinism fingerprint tests compare across builds.</summary>
        public ulong Fingerprint { get; private set; }
        public readonly List<RectInt> PropRects = new();

        public Tilemap Ground { get; private set; }
        public Tilemap Linear { get; private set; }
        public Tilemap Shadow { get; private set; }

        private enum Use : byte { Free, Room, Moat, Linear, Prop }

        /// <summary>Builds the surroundings for one depth under <paramref name="parent"/> (destroyed with it).</summary>
        public static DungeonSurroundings Create(Transform parent, Biome biome, IReadOnlyList<RectInt> rooms, int runSeed, int depth)
        {
            var go = new GameObject("DungeonSurroundings");
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;
            var surroundings = go.AddComponent<DungeonSurroundings>();
            surroundings.Build(biome, rooms != null ? new List<RectInt>(rooms) : new List<RectInt>(), runSeed, depth);
            return surroundings;
        }

        private Tilemap Layer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var map = go.AddComponent<Tilemap>();
            map.tileAnchor = new Vector3(0.5f, 0.5f, 0f);
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = SortingLayers.Ground;
            renderer.sortingOrder = order;
            renderer.mode = TilemapRenderer.Mode.Chunk;
            return map;
        }

        private ulong _print = 1469598103934665603UL;
        private void Mix(int v) => _print = (_print ^ (uint)v) * 1099511628211UL;

        private void Build(Biome biome, List<RectInt> rooms, int runSeed, int depth)
        {
            Biome = biome;
            if (rooms.Count == 0) return;
            var min = rooms[0].min;
            var max = rooms[0].max;
            foreach (var r in rooms) { min = Vector2Int.Min(min, r.min); max = Vector2Int.Max(max, r.max); }
            // The camera never leaves the layout rect, but a layout smaller than the 20 x 12 view shows past it.
            var padX = Margin + Mathf.Max(0, (21 - (max.x - min.x) + 1) / 2);
            var padY = Margin + Mathf.Max(0, (13 - (max.y - min.y) + 1) / 2);
            Region = new RectInt(min.x - padX, min.y - padY, max.x - min.x + 2 * padX, max.y - min.y + 2 * padY);

            gameObject.AddComponent<UnityEngine.Grid>().cellSize = Vector3.one;
            Ground = Layer("Ground", GroundOrder);
            Linear = Layer("Linear", LinearOrder);
            Shadow = Layer("Shadow", ShadowOrder);

            var w = Region.width;
            var h = Region.height;
            var use = new Use[w * h];
            int I(int x, int y) => (y - Region.yMin) * w + (x - Region.xMin);
            bool In(int x, int y) => x >= Region.xMin && y >= Region.yMin && x < Region.xMax && y < Region.yMax;
            foreach (var r in rooms)
                for (var y = r.yMin; y < r.yMax; y++)
                for (var x = r.xMin; x < r.xMax; x++)
                    if (In(x, y)) use[I(x, y)] = Use.Room;
            bool IsRoom(int x, int y) => In(x, y) && use[I(x, y)] == Use.Room;
            for (var y = Region.yMin; y < Region.yMax; y++)
            for (var x = Region.xMin; x < Region.xMax; x++)
            {
                if (use[I(x, y)] == Use.Room) continue;
                for (var dy = -1; dy <= 1 && use[I(x, y)] == Use.Free; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (IsRoom(x + dx, y + dy)) { use[I(x, y)] = Use.Moat; break; }
            }

            var kit = SurroundingsKit.For(biome);
            var rng = new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)biome, 0x53524E44));
            var noiseSeed = (uint)rng.NextInt(int.MaxValue);

            // ---- ground: the biome's lower level in broad material zones ----
            for (var y = Region.yMin; y < Region.yMax; y++)
            for (var x = Region.xMin; x < Region.xMax; x++)
            {
                if (use[I(x, y)] == Use.Room) continue;
                // One material per biome: per-cell zones read as a checkerboard; the zones are organic patches below.
                const int material = 0;
                var variant = (int)(RoomEnvironmentCanvas.Hash(x, y, noiseSeed + 2u) % (uint)SurroundingsKit.GroundVariants);
                var orientation = (int)(RoomEnvironmentCanvas.Hash(x, y, noiseSeed + 3u) % 8u);
                var cell = new Vector3Int(x, y, 0);
                Ground.SetTile(cell, kit.GroundTile(material, variant));
                Ground.SetTransformMatrix(cell, Orientation(orientation));
                Mix(x); Mix(y); Mix(material * 8 + variant); Mix(orientation);
                GroundCells++;
            }

            // ---- spines: long straight runs (one optional turn) through open ground, autotiled ----
            var free = 0;
            foreach (var u in use) if (u == Use.Free) free++;
            var runs = Mathf.Clamp(free / 170, 3, 14);
            var links = new Dictionary<Vector2Int, int>();
            var spines = new List<List<Vector2Int>>();
            bool Open(int x, int y) => In(x, y) && use[I(x, y)] == Use.Free;
            for (var r = 0; r < runs * 6 && spines.Count < runs; r++)
            {
                var horizontal = rng.NextFloat() < (biome == Biome.RuinedMetro ? 0.7f : 0.5f);
                var sx = Region.xMin + rng.NextInt(w);
                var sy = Region.yMin + rng.NextInt(h);
                if (!Open(sx, sy)) continue;
                var path = new List<Vector2Int>();
                var dir = horizontal ? Vector2Int.right : Vector2Int.up;
                var p = new Vector2Int(sx, sy);
                while (Open(p.x - dir.x, p.y - dir.y)) p -= dir;
                var turnAt = 10 + rng.NextInt(20);
                var turned = false;
                while (Open(p.x, p.y) && path.Count < 90)
                {
                    path.Add(p);
                    if (!turned && path.Count == turnAt && rng.NextFloat() < 0.4f)
                    {
                        var side = rng.NextFloat() < 0.5f ? 1 : -1;
                        var next = new Vector2Int(dir.y * side, dir.x * side);
                        if (Open(p.x + next.x, p.y + next.y)) { dir = next; turned = true; }
                    }

                    p += dir;
                }

                if (path.Count < 8) continue;
                // Rails run in pairs; so do the big pipe trunks: a parallel twin two cells over where there is room.
                if ((biome == Biome.RuinedMetro || biome == Biome.Rustworks) && rng.NextFloat() < 0.55f && !turned)
                {
                    var offset = horizontal ? new Vector2Int(0, 3) : new Vector2Int(3, 0);
                    var twin = new List<Vector2Int>();
                    foreach (var c in path) { if (!Open(c.x + offset.x, c.y + offset.y)) break; twin.Add(c + offset); }
                    if (twin.Count >= 8) Commit(twin);
                }

                Commit(path);
            }

            void Commit(List<Vector2Int> path)
            {
                for (var i = 0; i < path.Count; i++)
                {
                    var c = path[i];
                    var mask = 0;
                    if (i > 0) mask |= Side(path[i - 1] - c);
                    if (i < path.Count - 1) mask |= Side(path[i + 1] - c);
                    links[c] = links.TryGetValue(c, out var m) ? m | mask : mask;
                    use[I(c.x, c.y)] = Use.Linear;
                }

                spines.Add(path);
            }

            foreach (var kv in links)
            {
                var cell = new Vector3Int(kv.Key.x, kv.Key.y, 0);
                Linear.SetTile(cell, kit.LinearTile(kv.Value));
                Mix(kv.Key.x); Mix(kv.Key.y); Mix(kv.Value);
                LinearCells++;
            }

            // ---- structures: aligned to the spines, arranged in yards, a little debris between ----
            var props = new GameObject("Props").transform;
            props.SetParent(transform, false);
            var types = kit.PropTypes;
            var grammar = SurroundingsGrammar.For(biome);

            bool Place(string id, int px, int py, bool vertical, bool onSpine)
            {
                var type = kit.Type(id);
                var pw = vertical ? type.H : type.W;
                var ph = vertical ? type.W : type.H;
                for (var y = py; y < py + ph; y++)
                for (var x = px; x < px + pw; x++)
                {
                    if (!In(x, y)) return false;
                    var u = use[I(x, y)];
                    if (!(u == Use.Free || (onSpine && u == Use.Linear))) return false;
                }

                var variant = rng.NextInt(SurroundingsKit.PropVariants);
                var go = new GameObject(id);
                go.transform.SetParent(props, false);
                go.transform.position = new Vector3(px + pw * 0.5f, py + ph * 0.5f, 0f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = kit.Prop(id, variant, vertical);
                renderer.sortingLayerName = SortingLayers.Ground;
                renderer.sortingOrder = PropOrder;
                renderer.flipX = type.CanFlip && rng.NextFloat() < 0.5f;
                for (var y = py; y < py + ph; y++)
                for (var x = px; x < px + pw; x++)
                    use[I(x, y)] = Use.Prop;
                PropRects.Add(new RectInt(px, py, pw, ph));
                Mix(px); Mix(py); Mix(IndexOf(types, id)); Mix(variant); Mix(vertical ? 1 : 0);
                Props++;
                return true;
            }

            // Along each spine: the biome's line-side structures, parallel to the run, at a steady rhythm.
            foreach (var path in spines)
            {
                for (var i = 2; i < path.Count - 2; i += 3 + rng.NextInt(5))
                {
                    var c = path[i];
                    var horizontalRun = path[i + 1].y == c.y;
                    if (grammar.OnSpine != null && rng.NextFloat() < 0.35f)
                    {
                        // Sits on the run itself (a carriage on its rails).
                        var t = kit.Type(grammar.OnSpine);
                        var straight = true;
                        for (var k = 0; k < t.W && i + k < path.Count; k++) straight &= horizontalRun ? path[i + k].y == c.y : path[i + k].x == c.x;
                        if (straight && i + t.W < path.Count && Place(grammar.OnSpine, horizontalRun ? c.x : c.x - (t.H - 1) / 2, horizontalRun ? c.y - (t.H - 1) / 2 : c.y, !horizontalRun, true))
                        {
                            i += t.W;
                            continue;
                        }
                    }

                    var id = grammar.Beside[rng.NextInt(grammar.Beside.Length)];
                    var type = kit.Type(id);
                    var side = rng.NextFloat() < 0.5f ? 1 : -1;
                    // Long side parallel to the run.
                    var vertical = !horizontalRun && type.CanRotate;
                    var pw = vertical ? type.H : type.W;
                    var ph = vertical ? type.W : type.H;
                    var px = horizontalRun ? c.x : (side > 0 ? c.x + 1 : c.x - pw);
                    var py = horizontalRun ? (side > 0 ? c.y + 1 : c.y - ph) : c.y;
                    Place(id, px, py, vertical, false);
                }
            }

            // Yards: open stretches organised the way the place was used — a pillared hall, a tank farm, a lab bay, rack aisles.
            var openCells = new List<Vector2Int>();
            for (var y = Region.yMin; y < Region.yMax; y++)
            for (var x = Region.xMin; x < Region.xMax; x++)
                if (Open(x, y)) openCells.Add(new Vector2Int(x, y));
            var yards = Mathf.Clamp(free / 240, 1, 10);
            for (var attempt = 0; attempt < 400 && yards > 0 && openCells.Count > 0; attempt++)
            {
                var yw = 8 + rng.NextInt(8);
                var yh = 6 + rng.NextInt(6);
                var at = openCells[rng.NextInt(openCells.Count)];
                var yx = at.x;
                var yy = at.y;
                var clear = 0;
                for (var y = yy; y < yy + yh; y++)
                for (var x = yx; x < yx + yw; x++)
                    if (Open(x, y)) clear++;
                if (clear < yw * yh * 0.85f) continue;
                var row = 0;
                for (var y = yy + grammar.YardMargin; y + grammar.YardStepY <= yy + yh; y += grammar.YardStepY, row++)
                for (var x = yx + grammar.YardMargin + (row % 2) * grammar.YardStagger; x + grammar.YardStepX <= yx + yw; x += grammar.YardStepX)
                    Place(grammar.Yard[(row + (x - yx) / grammar.YardStepX) % grammar.Yard.Length == 0 || grammar.Yard.Length == 1 ? 0 : row % grammar.Yard.Length], x, y, grammar.YardVertical, false);
                yards--;
            }

            // Organic patches over the ground (damp, oil and slag, moss carpets, frost sheets): flat, no footprint.
            var patches = free / 28;
            var patchRoot = new GameObject("Patches").transform;
            patchRoot.SetParent(transform, false);
            for (var attempt = 0; attempt < patches * 3 && patches > 0 && openCells.Count > 0; attempt++)
            {
                var at = openCells[rng.NextInt(openCells.Count)];
                if (!Open(at.x, at.y)) continue;
                var variant = rng.NextInt(SurroundingsKit.PatchVariants);
                var go = new GameObject("patch");
                go.transform.SetParent(patchRoot, false);
                go.transform.position = new Vector3(at.x + 0.5f, at.y + 0.5f, 0f);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = kit.Patch(variant);
                renderer.sortingLayerName = SortingLayers.Ground;
                renderer.sortingOrder = PatchOrder;
                renderer.flipX = rng.NextFloat() < 0.5f;
                renderer.flipY = rng.NextFloat() < 0.5f;
                Mix(at.x); Mix(at.y); Mix(100 + variant);
                patches--;
            }

            // Debris between, sparse.
            var debris = free / 22;
            for (var attempt = 0; attempt < debris * 4 && debris > 0; attempt++)
            {
                var id = grammar.Debris[rng.NextInt(grammar.Debris.Length)];
                if (Place(id, Region.xMin + rng.NextInt(w), Region.yMin + rng.NextInt(h), kit.Type(id).CanRotate && rng.NextFloat() < 0.5f, false)) debris--;
            }

            // ---- every room's cast shadow in its moat ----
            for (var y = Region.yMin; y < Region.yMax; y++)
            for (var x = Region.xMin; x < Region.xMax; x++)
            {
                if (use[I(x, y)] == Use.Room) continue;
                var mask = 0;
                if (IsRoom(x, y + 1)) mask |= 1;
                if (IsRoom(x + 1, y)) mask |= 2;
                if (IsRoom(x, y - 1)) mask |= 4;
                if (IsRoom(x - 1, y)) mask |= 8;
                if (IsRoom(x + 1, y + 1)) mask |= 16;
                if (IsRoom(x - 1, y + 1)) mask |= 32;
                if (IsRoom(x + 1, y - 1)) mask |= 64;
                if (IsRoom(x - 1, y - 1)) mask |= 128;
                if (mask == 0) continue;
                Shadow.SetTile(new Vector3Int(x, y, 0), kit.ShadowTile(mask));
                Mix(mask);
                ShadowCells++;
            }

            Fingerprint = _print;
        }

        private sealed class SurroundingsGrammar
        {
            public string OnSpine;
            public string[] Beside, Yard, Debris;
            public int YardStepX = 4, YardStepY = 4, YardMargin = 1, YardStagger;
            public bool YardVertical;

            public static SurroundingsGrammar For(Biome biome) => biome switch
            {
                Biome.Rustworks => new SurroundingsGrammar
                {
                    Beside = new[] { "press", "beams", "crates", "furnace" }, Yard = new[] { "tank" }, Debris = new[] { "scrap", "crates" },
                    YardStepX = 4, YardStepY = 4
                },
                Biome.OvergrownLabs => new SurroundingsGrammar
                {
                    Beside = new[] { "servers", "partition", "bench" }, Yard = new[] { "containment", "bench" }, Debris = new[] { "overgrowth", "pod" },
                    YardStepX = 3, YardStepY = 3
                },
                Biome.CryoVaults => new SurroundingsGrammar
                {
                    Beside = new[] { "freezer", "compressor", "pallet" }, Yard = new[] { "rack" }, Debris = new[] { "icefall", "pallet" },
                    YardStepX = 5, YardStepY = 2
                },
                _ => new SurroundingsGrammar
                {
                    OnSpine = "traincar", Beside = new[] { "cablebox", "booth", "slab" }, Yard = new[] { "pillar" }, Debris = new[] { "rubble", "slab" },
                    YardStepX = 5, YardStepY = 4, YardStagger = 0
                }
            };
        }

        private static int IndexOf(IReadOnlyList<SurroundingsKit.PropType> types, string id)
        {
            for (var i = 0; i < types.Count; i++) if (types[i].Id == id) return i;
            return -1;
        }

        private static int Side(Vector2Int d) => d == Vector2Int.up ? 1 : d == Vector2Int.right ? 2 : d == Vector2Int.down ? 4 : 8;

        private static readonly Matrix4x4[] Orientations =
        {
            Matrix4x4.identity,
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), Vector3.one),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 180), Vector3.one),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 270), Vector3.one),
            Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1, 1, 1)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 90), new Vector3(-1, 1, 1)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 180), new Vector3(-1, 1, 1)),
            Matrix4x4.TRS(Vector3.zero, Quaternion.Euler(0, 0, 270), new Vector3(-1, 1, 1))
        };

        private static Matrix4x4 Orientation(int i) => Orientations[i & 7];
    }
}
