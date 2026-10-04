using System;
using System.Collections.Generic;
using RuinRail.Core;
using UnityEngine;
using UnityEngine.Tilemaps;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// One biome's art for the dungeon surroundings, painted at runtime and cached for the session: three ground
    /// materials in <see cref="GroundVariants"/> variants, the biome's linear structure autotiled by its four
    /// connections, the room-shadow tiles, and parametric props drawn at whatever footprint they are placed with.
    /// Everything sits a step below the room floors in value and saturation (the surroundings are the lower, unlit
    /// level the rooms were built into), with only a few dim emissive notes.
    /// </summary>
    internal sealed class SurroundingsKit
    {
        public const int GroundVariants = 4;
        public const int PropVariants = 2;
        public const int PatchVariants = 3;
        private readonly Sprite[] _patches = new Sprite[PatchVariants];
        private const int T = 32;

        public readonly struct PropType
        {
            public PropType(string id, int w, int h, bool canRotate, bool canFlip) { Id = id; W = w; H = h; CanRotate = canRotate; CanFlip = canFlip; }
            public string Id { get; }
            public int W { get; }
            public int H { get; }
            public bool CanRotate { get; }
            public bool CanFlip { get; }
        }

        private static readonly Dictionary<Biome, SurroundingsKit> Kits = new();
        private readonly Biome _biome;
        private readonly Tile[,] _ground = new Tile[3, GroundVariants];
        private readonly Dictionary<int, Tile> _linear = new();
        private readonly Dictionary<int, Tile> _shadow = new();
        private readonly Dictionary<(string, int, bool), Sprite> _props = new();
        private readonly Pal _p;

        public IReadOnlyList<PropType> PropTypes { get; }

        public PropType Type(string id)
        {
            foreach (var t in PropTypes) if (t.Id == id) return t;
            throw new ArgumentException("no surroundings prop " + id + " in " + _biome);
        }

        public static SurroundingsKit For(Biome biome)
        {
            if (!Kits.TryGetValue(biome, out var kit)) Kits[biome] = kit = new SurroundingsKit(biome);
            return kit;
        }

        private sealed class Pal
        {
            public Color32[] Ground0, Ground1, Ground2; // dark, mid, light per material
            public Color32 Outline, Shadow;
            public Color32 BodyDark, Body, BodyLight, Accent, AccentDim, Glow, Metal, MetalLight;
        }

        private static Color32[] R(string a, string b, string c) => new[] { Hex(a), Hex(b), Hex(c) };

        private SurroundingsKit(Biome biome)
        {
            _biome = biome;
            switch (biome)
            {
                case Biome.Rustworks:
                    _p = new Pal
                    {
                        Ground0 = R("#17130F", "#1E1915", "#26201B"), Ground1 = R("#17130F", "#1E1915", "#26201B"), Ground2 = R("#17130F", "#1E1915", "#26201B"),
                        Outline = Hex("#0A0807"), Shadow = Hex("#050403"), BodyDark = Hex("#231E1A"), Body = Hex("#332B25"), BodyLight = Hex("#463B32"),
                        Accent = Hex("#6E4A1E"), AccentDim = Hex("#4A2E16"), Glow = Hex("#A84418"), Metal = Hex("#3A332E"), MetalLight = Hex("#544A42")
                    };
                    PropTypes = new[] { new PropType("tank", 3, 3, false, true), new PropType("press", 3, 2, true, true), new PropType("furnace", 3, 2, false, true),
                        new PropType("scrap", 2, 2, false, true), new PropType("beams", 3, 1, true, true), new PropType("crates", 2, 1, true, true) };
                    break;
                case Biome.OvergrownLabs:
                    _p = new Pal
                    {
                        Ground0 = R("#141B1A", "#1A2221", "#212A28"), Ground1 = R("#141B1A", "#1A2221", "#212A28"), Ground2 = R("#141B1A", "#1A2221", "#212A28"),
                        Outline = Hex("#090D0C"), Shadow = Hex("#040606"), BodyDark = Hex("#202826"), Body = Hex("#2C3533"), BodyLight = Hex("#3C4744"),
                        Accent = Hex("#2E5258"), AccentDim = Hex("#22393D"), Glow = Hex("#4E8E36"), Metal = Hex("#33403E"), MetalLight = Hex("#4A5A57")
                    };
                    PropTypes = new[] { new PropType("containment", 2, 2, false, true), new PropType("servers", 3, 1, true, true), new PropType("pod", 2, 2, false, true),
                        new PropType("overgrowth", 2, 2, false, true), new PropType("bench", 2, 1, true, true), new PropType("partition", 3, 1, true, true) };
                    break;
                case Biome.CryoVaults:
                    _p = new Pal
                    {
                        Ground0 = R("#10161D", "#151C24", "#1B232C"), Ground1 = R("#10161D", "#151C24", "#1B232C"), Ground2 = R("#10161D", "#151C24", "#1B232C"),
                        Outline = Hex("#070A0D"), Shadow = Hex("#030507"), BodyDark = Hex("#1C252D"), Body = Hex("#27323C"), BodyLight = Hex("#36444F"),
                        Accent = Hex("#2E5E6E"), AccentDim = Hex("#22404C"), Glow = Hex("#3E8EA8"), Metal = Hex("#34424C"), MetalLight = Hex("#566A76")
                    };
                    PropTypes = new[] { new PropType("freezer", 3, 2, true, true), new PropType("compressor", 2, 2, false, true), new PropType("rack", 4, 1, true, true),
                        new PropType("cryotank", 2, 2, false, true), new PropType("pallet", 2, 1, true, true), new PropType("icefall", 2, 2, false, true) };
                    break;
                default:
                    _p = new Pal
                    {
                        Ground0 = R("#15191B", "#1B2022", "#22282A"), Ground1 = R("#15191B", "#1B2022", "#22282A"), Ground2 = R("#15191B", "#1B2022", "#22282A"),
                        Outline = Hex("#090B0C"), Shadow = Hex("#040506"), BodyDark = Hex("#1F2427"), Body = Hex("#2B3135"), BodyLight = Hex("#3B4247"),
                        Accent = Hex("#5A4A22"), AccentDim = Hex("#3E3418"), Glow = Hex("#8A3A26"), Metal = Hex("#3C4347"), MetalLight = Hex("#5A6266")
                    };
                    PropTypes = new[] { new PropType("traincar", 6, 2, true, true), new PropType("pillar", 2, 2, false, true), new PropType("slab", 3, 2, true, true),
                        new PropType("rubble", 2, 2, false, true), new PropType("booth", 2, 2, false, true), new PropType("cablebox", 2, 1, true, true) };
                    break;
            }
        }

        // ================================================================ tiles

        private static Tile MakeTile(RoomEnvironmentCanvas c, string name)
        {
            var texture = new Texture2D(T, T, TextureFormat.RGBA32, false, false) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(c.Pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, T, T), new Vector2(0.5f, 0.5f), T, 0, SpriteMeshType.FullRect);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            tile.sprite = sprite;
            tile.flags = TileFlags.None;
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.DontSave;
            return tile;
        }

        private Color32[] Material(int m) => m == 0 ? _p.Ground0 : m == 1 ? _p.Ground1 : _p.Ground2;

        public Tile GroundTile(int material, int variant)
        {
            material = Mathf.Clamp(material, 0, 2);
            variant = Mathf.Clamp(variant, 0, GroundVariants - 1);
            if (_ground[material, variant] != null) return _ground[material, variant];
            var c = new RoomEnvironmentCanvas(T, T);
            var ramp = Material(material);
            var seed = (uint)((int)_biome * 977 + material * 131 + variant * 17 + 5);
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var n = Value(x, y, 6f, seed) * 0.7f + Value(x, y, 2.5f, seed + 1u) * 0.3f;
                var col = n < 0.36f ? ramp[0] : n > 0.68f ? ramp[2] : ramp[1];
                c.Set(x, y, col);
            }

            switch (_biome)
            {
                case Biome.RuinedMetro:
                    if (material == 0) Speckle(c, seed, 0.1f, ramp[2], ramp[0]);        // ballast
                    else if (material == 1) Seams(c, 16, ramp[0], 0.5f);               // old concrete bed slabs
                    else Speckle(c, seed, 0.05f, Hex("#1A2428"), ramp[0]);             // damp
                    break;
                case Biome.Rustworks:
                    if (material == 0) { Seams(c, 32, ramp[0], 1f); Rivets(c, ramp[2]); } // foundry floor plate
                    else if (material == 1) Speckle(c, seed, 0.06f, Hex("#0D0A08"), ramp[0]); // oil-soaked
                    else Speckle(c, seed, 0.08f, Hex("#2E2218"), ramp[0]);             // slag grit
                    break;
                case Biome.OvergrownLabs:
                    if (material == 0) Seams(c, 16, ramp[0], 1f);                     // service deck tiles
                    else if (material == 1) Moss(c, seed);                             // moss
                    else Seams(c, 16, ramp[0], 0.6f);
                    break;
                default:
                    if (material == 0) { Seams(c, 32, ramp[0], 1f); Seams(c, 8, ramp[1], 0.25f); } // insulated subfloor
                    else if (material == 1) Frost(c, seed);                            // frost
                    else Seams(c, 32, ramp[0], 0.8f);
                    break;
            }

            return _ground[material, variant] = MakeTile(c, $"Surround_{_biome}_ground_{material}_{variant}");
        }

        private static void Speckle(RoomEnvironmentCanvas c, uint seed, float share, Color32 light, Color32 dark)
        {
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var w = White(x, y, seed + 9u);
                if (w < share * 0.5f) c.Set(x, y, light);
                else if (w < share) c.Set(x, y, dark);
            }
        }

        private static void Seams(RoomEnvironmentCanvas c, int every, Color32 dark, float keep)
        {
            for (var i = 0; i < T; i++)
            for (var k = 0; k < T; k += every)
            {
                if (White(i, k, 77u) > keep) continue;
                c.Set(i, k, dark);
                c.Set(k, i, dark);
            }
        }

        private static void Rivets(RoomEnvironmentCanvas c, Color32 light)
        {
            foreach (var (x, y) in new[] { (3, 3), (28, 3), (3, 28), (28, 28) }) c.Set(x, y, light);
        }

        private static void Puddle(RoomEnvironmentCanvas c, uint seed, Color32 fill, Color32 sheen)
        {
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var d = Mathf.Sqrt((x - 16f) * (x - 16f) + (y - 15f) * (y - 15f)) / 13f + (Value(x, y, 4f, seed + 3u) - 0.5f) * 0.5f;
                if (d > 1f) continue;
                c.Set(x, y, fill);
                if (d < 0.7f && White(x, y, seed + 4u) < 0.04f) c.Set(x, y, sheen);
            }
        }

        private void Moss(RoomEnvironmentCanvas c, uint seed)
        {
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var n = Value(x, y, 3f, seed + 5u);
                if (n > 0.6f) c.Set(x, y, n > 0.8f ? Hex("#24361F") : Hex("#1C2A19"));
            }
        }

        private void Frost(RoomEnvironmentCanvas c, uint seed)
        {
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var n = Value(x, y, 3f, seed + 6u);
                if (n > 0.62f) c.Set(x, y, n > 0.82f ? Hex("#2C3C48") : Hex("#202C36"));
            }
        }

        // ---------------------------------------------------------------- linear structures

        /// <summary>The biome's linear structure for a connection mask (1 N, 2 E, 4 S, 8 W).</summary>
        public Tile LinearTile(int mask)
        {
            if (_linear.TryGetValue(mask, out var tile)) return tile;
            var c = new RoomEnvironmentCanvas(T, T);
            var n = (mask & 1) != 0;
            var e = (mask & 2) != 0;
            var s = (mask & 4) != 0;
            var w = (mask & 8) != 0;
            var corner = (n || s) && (e || w) && !(n && s) && !(e && w);
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                // Distance across the run (d) and along it (a), for the straight half-runs or the corner arc.
                var best = 99f;
                var along = 0f;
                if (corner)
                {
                    var cx = e ? T : 0f;
                    var cy = n ? T : 0f;
                    var r = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    best = r - 16f;
                    along = Mathf.Atan2(y + 0.5f - cy, x + 0.5f - cx) * 16f;
                }
                else
                {
                    void Try(bool on, float d, float a, bool covers) { if (on && covers && Mathf.Abs(d) < Mathf.Abs(best)) { best = d; along = a; } }
                    Try(e, y + 0.5f - 16f, x, x >= 12);
                    Try(w, y + 0.5f - 16f, x, x <= 19);
                    Try(n, x + 0.5f - 16f, y, y >= 12);
                    Try(s, x + 0.5f - 16f, y, y <= 19);
                    if (mask == 0) { best = y + 0.5f - 16f; along = x; }
                }

                var col = LinearPixel(best, along);
                if (col.HasValue) c.Over(x, y, col.Value, col.Value.a / 255f);
            }

            return _linear[mask] = MakeTile(c, $"Surround_{_biome}_linear_{mask}");
        }

        private Color32? LinearPixel(float d, float a)
        {
            var ad = Mathf.Abs(d);
            var ai = Mathf.FloorToInt(a + 1000f);
            switch (_biome)
            {
                case Biome.RuinedMetro:
                {
                    // Rail track: dark bed, wooden sleepers, two steel rails.
                    if (ad > 12f) return null;
                    if (ad >= 7f && ad < 9f) return d < 0 ? Hex("#6A7377") : Hex("#4A5256");
                    if (ad >= 9f && ad < 10f) return Hex("#101315");
                    if (ai % 8 < 3 && ad < 11f) return ai % 8 == 0 ? Hex("#3A3029") : Hex("#2C241F");
                    return ad > 11f ? Hex("#0E1113") : Hex("#191D1F");
                }
                case Biome.Rustworks:
                {
                    // Two parallel pipes with flanges.
                    foreach (var off in new[] { -6f, 6f })
                    {
                        var p = d - off;
                        if (Mathf.Abs(p) > 4.5f) continue;
                        if (ai % 16 < 2) return Mathf.Abs(p) < 4.5f ? Hex("#5A4E44") : (Color32?)null;
                        return p < -2f ? Hex("#6A5446") : p < 1.5f ? Hex("#463A31") : Hex("#2A221D");
                    }

                    return ad < 11f && ai % 16 == 8 && ad < 2f ? Hex("#211B17") : (Color32?)null;
                }
                case Biome.OvergrownLabs:
                {
                    // A ribbed cable duct, with moss caught along one edge.
                    if (ad > 7f) return null;
                    if (ad > 6f) return Hex("#0B100F");
                    if (ai % 6 == 0) return Hex("#1E3438");
                    if (d < -3f) return Hex("#3A646B");
                    if (d > 4f && (ai * 7) % 5 == 0) return Hex("#2E4626");
                    return d > 3f ? Hex("#1F363A") : Hex("#2A4A50");
                }
                default:
                {
                    // A frosted coolant line on brackets.
                    if (ad > 6f) return null;
                    if (ai % 20 < 3) return ad < 6f ? Hex("#2A363E") : (Color32?)null;
                    if (ad > 5f) return Hex("#0C1217");
                    if (d < -2.5f) return (ai * 3) % 7 < 3 ? Hex("#A4BECA") : Hex("#6E8A98");
                    return d < 2f ? Hex("#435662") : Hex("#2A3842");
                }
            }
        }

        // ---------------------------------------------------------------- room shadows

        /// <summary>The shadow a room throws onto the surroundings next to it (bits: 1 N, 2 E, 4 S, 8 W, 16 NE, 32 NW, 64 SE, 128 SW).</summary>
        public Tile ShadowTile(int mask)
        {
            if (_shadow.TryGetValue(mask, out var tile)) return tile;
            var c = new RoomEnvironmentCanvas(T, T);
            for (var y = 0; y < T; y++)
            for (var x = 0; x < T; x++)
            {
                var d = 99f;
                if ((mask & 1) != 0) d = Mathf.Min(d, T - 1 - y);
                if ((mask & 2) != 0) d = Mathf.Min(d, T - 1 - x);
                if ((mask & 4) != 0) d = Mathf.Min(d, y);
                if ((mask & 8) != 0) d = Mathf.Min(d, x);
                if ((mask & 16) != 0) d = Mathf.Min(d, Mathf.Max(T - 1 - x, T - 1 - y));
                if ((mask & 32) != 0) d = Mathf.Min(d, Mathf.Max(x, T - 1 - y));
                if ((mask & 64) != 0) d = Mathf.Min(d, Mathf.Max(T - 1 - x, y));
                if ((mask & 128) != 0) d = Mathf.Min(d, Mathf.Max(x, y));
                if (d > 20f) continue;
                // Stepped bands: a hard contact line at the wall, then a deep shadow fading over ~20 px.
                var a = d < 1f ? 0.85f : d < 5f ? 0.6f : d < 11f ? 0.42f : 0.22f;
                c.Over(x, y, _p.Shadow, a);
            }

            return _shadow[mask] = MakeTile(c, $"Surround_{_biome}_shadow_{mask}");
        }

        // ================================================================ patches

        /// <summary>An organic flat patch (4 x 3 cells) of the biome's ground condition, soft-edged, no outline or shadow.</summary>
        public Sprite Patch(int variant)
        {
            variant = Mathf.Clamp(variant, 0, PatchVariants - 1);
            if (_patches[variant] != null) return _patches[variant];
            const int w = 4 * T, h = 3 * T;
            var c = new RoomEnvironmentCanvas(w, h);
            var seed = (uint)((int)_biome * 313 + variant * 71 + 9);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = (x - w * 0.5f) / (w * 0.46f);
                var dy = (y - h * 0.5f) / (h * 0.44f);
                var d = Mathf.Sqrt(dx * dx + dy * dy) + (Value(x, y, 14f, seed) - 0.5f) * 0.7f + (Value(x, y, 4f, seed + 1u) - 0.5f) * 0.25f;
                if (d > 1f) continue;
                var inner = d < 0.6f;
                switch (_biome)
                {
                    case Biome.Rustworks:
                        c.Over(x, y, inner ? Hex("#0C0907") : Hex("#120E0B"), inner ? 0.85f : 0.6f);
                        if (inner && White(x, y, seed) < 0.02f) c.Over(x, y, Hex("#3A3A40"), 0.7f);
                        if (!inner && White(x / 2, y / 2, seed + 2u) < 0.05f) c.Over(x, y, Hex("#3A2414"), 0.8f);
                        break;
                    case Biome.OvergrownLabs:
                        var leaf = Value(x, y, 3f, seed + 3u);
                        c.Over(x, y, leaf > 0.6f ? Hex("#22341C") : Hex("#18261A"), inner ? 0.9f : 0.65f);
                        if (leaf > 0.78f && White(x, y, seed) < 0.3f) c.Over(x, y, Hex("#30482A"), 0.9f);
                        break;
                    case Biome.CryoVaults:
                        c.Over(x, y, inner ? Hex("#24323E") : Hex("#1C2731"), inner ? 0.85f : 0.6f);
                        if (White(x, y, seed) < (inner ? 0.05f : 0.02f)) c.Over(x, y, Hex("#4A6272"), 0.9f);
                        break;
                    default:
                        c.Over(x, y, inner ? Hex("#121A1D") : Hex("#161D20"), inner ? 0.85f : 0.55f);
                        if (inner && White(x, y, seed) < 0.015f) c.Over(x, y, Hex("#3A4A50"), 0.8f);
                        break;
                }
            }

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = $"Surround_{_biome}_patch_{variant}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(c.Pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), T, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            return _patches[variant] = sprite;
        }

        // ================================================================ props

        public Sprite Prop(string id, int variant, bool vertical)
        {
            if (_props.TryGetValue((id, variant, vertical), out var sprite)) return sprite;
            PropType type = default;
            foreach (var t in PropTypes) if (t.Id == id) type = t;
            var w = (vertical ? type.H : type.W) * T;
            var h = (vertical ? type.W : type.H) * T;
            var c = new RoomEnvironmentCanvas(w, h);
            var painter = new PropPainter(c, _p.Outline, _p.Shadow, (uint)(variant * 131 + id.Length * 17 + (int)_biome * 7));
            PaintProp(painter, id, variant, w, h);
            painter.Finish();
            // The surroundings are the unlit lower level: every prop sits a step below the rooms in value.
            for (var i = 0; i < c.Pixels.Length; i++) { var px = c.Pixels[i]; c.Pixels[i] = new Color32((byte)(px.r * 0.82f), (byte)(px.g * 0.82f), (byte)(px.b * 0.82f), px.a); }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false, false) { name = $"Surround_{_biome}_{id}_{variant}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels32(c.Pixels);
            texture.Apply(false, true);
            sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), T, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            return _props[(id, variant, vertical)] = sprite;
        }

        private void PaintProp(PropPainter q, string id, int v, int w, int h)
        {
            var p = _p;
            switch (id)
            {
                // ---------------- Ruined Metro
                case "traincar":
                {
                    // A derelict carriage: ribbed roof, a faded livery band and dark windows on its long side.
                    var horizontal = w > h;
                    var front = horizontal ? 10 : 8;
                    q.Block(3, 4, w - 6, h - 8, front, Hex("#2E3438"), Hex("#3C4348"), Hex("#4A5257"), Hex("#22282B"));
                    var top0 = 4 + front;
                    if (horizontal)
                    {
                        for (var x = 8; x < w - 8; x += 6) q.Line(x, top0 + 1, x, h - 6, Hex("#2A3034"));
                        for (var x = 7; x < w - 7; x++) { q.Px(x, 6, Hex("#5A2A22")); q.Px(x, 7, Hex("#4A2420")); }
                        for (var x = 10; x < w - 12; x += 14)
                        {
                            q.Rect(x, 9, 8, 4, Hex("#101518"));
                            q.Px(x + 1, 12, Hex("#4E6670"));
                        }

                        if (v == 1) q.Crush(w - 22, 4, 18, h - 8); // the crushed end
                    }
                    else
                    {
                        for (var y = top0 + 4; y < h - 8; y += 6) q.Line(5, y, w - 6, y, Hex("#2A3034"));
                        for (var y = top0; y < h - 6; y++) { q.Px(4, y, Hex("#5A2A22")); q.Px(w - 5, y, Hex("#4A2420")); }
                        q.Rect(10, 6, w - 20, 4, Hex("#101518"));
                        if (v == 1) q.Crush(4, h - 24, w - 8, 18);
                    }

                    q.Dirt(0.03f, Hex("#24292C"));
                    break;
                }
                case "pillar":
                    q.Block(10, 8, w - 20, h - 16, 12, Hex("#2A2F33"), Hex("#363C40"), Hex("#454C51"), Hex("#1E2225"));
                    q.Rect(14, 24, w - 28, 3, Hex("#4A3E1E")); // faded band
                    q.Chips(6, Hex("#1A1E20"), Hex("#4F575C"));
                    q.Rubble(4, 4, w - 8, 10, 5, p.BodyDark, p.Body, p.BodyLight);
                    break;
                case "slab":
                {
                    // A collapsed platform section lying flat: jagged broken edge, exposed rebar, its yellow edge line.
                    var horizontal = w > h;
                    q.Jagged(4, 6, w - 8, h - 12, 3, Hex("#262B2E"), Hex("#30363A"), Hex("#3C4347"), Hex("#1C2022"));
                    if (horizontal) for (var x = 8; x < w - 8; x++) { if ((x / 3 & 1) == 0) q.Px(x, h - 9, Hex("#4E4220")); }
                    else for (var y = 10; y < h - 8; y++) { if ((y / 3 & 1) == 0) q.Px(w - 9, y, Hex("#4E4220")); }
                    for (var k = 0; k < 4; k++)
                    {
                        var x = 6 + (int)(White(k, 7, 21u) * (w - 12));
                        var y = 6 + (int)(White(k, 8, 21u) * 6);
                        q.Line(x, y, x + 3, y - 4, Hex("#4A2E22"));
                    }

                    q.Crack(w / 2, h / 2, 20, Hex("#15191B"));
                    q.Rubble(2, 2, w - 4, 8, 5, p.BodyDark, p.Body, p.BodyLight);
                    break;
                }
                case "rubble":
                    q.Rubble(4, 4, w - 8, h - 8, 16, p.BodyDark, p.Body, p.BodyLight);
                    q.Rubble(10, 10, w - 20, h - 20, 6, Hex("#3A2E26"), Hex("#4A3A2E"), Hex("#5A4836"));
                    break;
                case "booth":
                    q.Block(8, 6, w - 16, h - 14, 14, Hex("#252A2D"), Hex("#30363A"), Hex("#3E4549"), Hex("#1A1E21"));
                    q.Rect(14, 10, w - 28, 6, Hex("#0F1416"));
                    q.Px(16, 14, Hex("#7A3A26"));
                    q.Px(17, 14, Hex("#5A2A1E"));
                    q.Glow(17, 14, 6f, Hex("#8A3A26"), 0.18f);
                    break;
                case "cablebox":
                    q.Block(6, 6, w - 12, h - 12, 8, Hex("#24292C"), Hex("#2F353A"), Hex("#3C4348"), Hex("#191D20"));
                    q.Rect(10, 16, w - 20, 2, Hex("#4A3E1E"));
                    q.Cable(0, h / 2, 6, h / 2 + 3, Hex("#0E1012"));
                    q.Cable(w - 6, h / 2 + 2, w, h / 2 - 2, Hex("#0E1012"));
                    break;

                // ---------------- Rustworks
                case "tank":
                {
                    var cx = w / 2; var cy = h / 2 + 2; var r = Mathf.Min(w, h) / 2 - 6;
                    q.Disc(cx, cy - 4, r, Hex("#1E1916"), Hex("#1E1916"), Hex("#1E1916")); // side wall below the lid
                    q.Disc(cx, cy, r, Hex("#2E2622"), Hex("#3E342D"), Hex("#4E423A"));
                    q.Ring(cx, cy, r - 3, Hex("#26201C"));
                    for (var k = 0; k < 16; k++) { var a = k * Mathf.PI / 8f; q.Px(cx + Mathf.RoundToInt(Mathf.Cos(a) * (r - 1)), cy + Mathf.RoundToInt(Mathf.Sin(a) * (r - 1)), Hex("#5E5248")); }
                    q.Rect(cx - 3, cy - 3, 6, 6, Hex("#3A322C"));
                    q.Px(cx, cy, Hex("#6A5E52"));
                    if (v == 1) for (var y = cy - r; y < cy + 2; y++) q.Px(cx + r - 2, y, Hex("#4A2A1A")); // rust run
                    q.Line(cx + r - 2, cy - 4 - r + 3, cx + r - 2, cy + r - 6, Hex("#5A5048")); // ladder rail
                    break;
                }
                case "press":
                {
                    var front = 10;
                    q.Block(4, 4, w - 8, h - 8, front, Hex("#2A2420"), Hex("#38302A"), Hex("#4A4038"), Hex("#1E1916"));
                    var horizontal = w > h;
                    for (var i = 0; i < 2; i++)
                    {
                        var px = horizontal ? w / 3 * (i + 1) - 4 : w / 2 - 4;
                        var py = horizontal ? 4 + front + 6 : h / 3 * (i + 1) - 2;
                        q.Rect(px, py, 8, 8, Hex("#2A2420"));
                        q.Rect(px + 2, py + 2, 4, 4, Hex("#5A4E44"));
                    }

                    for (var x = 6; x < w - 6; x++) q.Px(x, 5, ((x / 3) & 1) == 0 ? Hex("#6E5A1E") : Hex("#1A1612"));
                    q.Px(w - 10, 9, Hex("#B05A1E"));
                    q.Glow(w - 10, 9, 5f, Hex("#B05A1E"), 0.16f);
                    break;
                }
                case "furnace":
                {
                    q.Block(4, 4, w - 8, h - 8, 14, Hex("#2A1C16"), Hex("#3A2820"), Hex("#4A342A"), Hex("#1E1410"));
                    for (var y = 6; y < 18; y += 4)
                    for (var x = 6; x < w - 6; x++)
                        if (((x + (y / 4) * 4) % 8) == 0) q.Px(x, y, Hex("#24170F"));
                    // The dim mouth of the furnace, still holding heat.
                    q.Rect(w / 2 - 9, 6, 18, 7, Hex("#140C08"));
                    for (var x = w / 2 - 8; x < w / 2 + 8; x++) { q.Px(x, 7, Hex("#7A2E12")); if (White(x, 1, 3u) < 0.5f) q.Px(x, 8, Hex("#A8441A")); }
                    q.Glow(w / 2, 6, 14f, Hex("#A84418"), 0.2f);
                    q.Rect(w / 2 - 4, h - 14, 8, 8, Hex("#2A221D")); // flue
                    q.Rect(w / 2 - 2, h - 12, 4, 4, Hex("#0E0A08"));
                    break;
                }
                case "scrap":
                    q.Rubble(4, 4, w - 8, h - 8, 14, Hex("#2A2420"), Hex("#3E342D"), Hex("#584A3E"));
                    q.Rubble(8, 8, w - 16, h - 16, 6, Hex("#3A2216"), Hex("#5A3420"), Hex("#6E4228"));
                    q.Line(8, 10, w - 10, h - 12, Hex("#4A4038"));
                    break;
                case "beams":
                {
                    var horizontal = w > h;
                    for (var i = 0; i < 3; i++)
                    {
                        if (horizontal) q.Block(4, 4 + i * 8, w - 8, 7, 3, Hex("#2A2420"), Hex("#3A322C"), Hex("#4A4038"), Hex("#1E1916"));
                        else q.Block(4 + i * 8, 4, 7, h - 8, 3, Hex("#2A2420"), Hex("#3A322C"), Hex("#4A4038"), Hex("#1E1916"));
                    }

                    break;
                }
                case "crates":
                    q.Block(4, 4, w / 2 - 6, h - 10, 6, Hex("#2A2218"), Hex("#3A2E20"), Hex("#4A3A28"), Hex("#1E1810"));
                    q.Block(w / 2 + 2, 6, w / 2 - 6, h - 12, 6, Hex("#26201A"), Hex("#342C24"), Hex("#443A30"), Hex("#1A1612"));
                    break;

                // ---------------- Overgrown Labs
                case "containment":
                {
                    var cx = w / 2; var cy = h / 2 + 2; var r = Mathf.Min(w, h) / 2 - 7;
                    q.Disc(cx, cy - 3, r + 2, Hex("#1A2220"), Hex("#1A2220"), Hex("#1A2220"));
                    q.Disc(cx, cy, r + 2, Hex("#2E3836"), Hex("#3A4644"), Hex("#4A5856"));
                    q.Disc(cx, cy, r - 1, Hex("#2A5222"), Hex("#3A6E2E"), v == 1 ? Hex("#2A3A2A") : Hex("#5A9A3E"));
                    if (v == 0) { q.Glow(cx, cy, r + 6f, Hex("#4E8E36"), 0.18f); q.Px(cx - 3, cy + 3, Hex("#8AC25A")); q.Px(cx - 2, cy + 4, Hex("#8AC25A")); }
                    else q.Crack(cx - 2, cy, 10, Hex("#0E1410"));
                    q.Vines(4, 4, w - 8, h / 3, Hex("#22351E"), Hex("#3A5A2A"));
                    break;
                }
                case "servers":
                {
                    var horizontal = w > h;
                    var n = horizontal ? 3 : 3;
                    for (var i = 0; i < n; i++)
                    {
                        if (horizontal) q.Block(4 + i * (w - 8) / 3, 5, (w - 8) / 3 - 2, h - 10, 8, Hex("#1E2526"), Hex("#283132"), Hex("#343F40"), Hex("#151B1C"));
                        else q.Block(5, 4 + i * (h - 8) / 3, w - 10, (h - 8) / 3 - 2, 6, Hex("#1E2526"), Hex("#283132"), Hex("#343F40"), Hex("#151B1C"));
                    }

                    for (var k = 0; k < 6; k++)
                    {
                        var x = 8 + (int)(White(k, 1, 9u) * (w - 16));
                        var y = 7 + (int)(White(k, 2, 9u) * 4);
                        q.Px(x, y, k % 3 == 0 ? Hex("#4E8E36") : Hex("#2E6E78"));
                    }

                    break;
                }
                case "pod":
                {
                    var cx = w / 2; var cy = h / 2;
                    q.Disc(cx, cy, 20, Hex("#262E2C"), Hex("#323C3A"), Hex("#3E4A48"));
                    q.Disc(cx, cy, 15, Hex("#121A18"), Hex("#16201E"), Hex("#1C2826"));
                    for (var k = 0; k < 9; k++) q.Shard(cx - 18 + (int)(White(k, 3, 11u) * 36), cy - 18 + (int)(White(k, 4, 11u) * 36), Hex("#4A6A6E"), Hex("#6E9AA0"));
                    q.Puddle(cx + 4, cy - 10, 10, 4, Hex("#24401E"));
                    break;
                }
                case "overgrowth":
                    q.Rubble(4, 4, w - 8, h - 8, 10, Hex("#262E2C"), Hex("#343E3C"), Hex("#44504E"));
                    q.Vines(2, 2, w - 4, h - 4, Hex("#22351E"), Hex("#3A5A2A"));
                    q.Vines(6, 6, w - 12, h - 12, Hex("#1C2C18"), Hex("#2E4626"));
                    break;
                case "bench":
                    q.Block(4, 6, w - 8, h - 12, 6, Hex("#2A3232"), Hex("#38423F"), Hex("#48534F"), Hex("#1E2423"));
                    q.Rect(10, h - 10, 6, 3, Hex("#22393D"));
                    q.Px(w - 12, h - 9, Hex("#4E8E36"));
                    break;
                case "partition":
                {
                    var horizontal = w > h;
                    if (horizontal) { q.Block(4, 10, w - 8, 10, 5, Hex("#28302F"), Hex("#34403E"), Hex("#44524F"), Hex("#1C2322")); q.Rect(8, 16, w - 16, 2, Hex("#3E5A5E")); }
                    else { q.Block(10, 4, 10, h - 8, 5, Hex("#28302F"), Hex("#34403E"), Hex("#44524F"), Hex("#1C2322")); q.Rect(14, 10, 2, h - 20, Hex("#3E5A5E")); }
                    for (var k = 0; k < 6; k++) q.Shard(6 + (int)(White(k, 5, 13u) * (w - 12)), 4 + (int)(White(k, 6, 13u) * 6), Hex("#4A6A6E"), Hex("#6E9AA0"));
                    break;
                }

                // ---------------- Cryo Vaults
                case "freezer":
                {
                    var horizontal = w > h;
                    q.Block(4, 4, w - 8, h - 8, horizontal ? 14 : 10, Hex("#1E2830"), Hex("#2A3640"), Hex("#384652"), Hex("#151D24"));
                    q.Frost(4, h / 2, w - 8, h / 2 - 4, Hex("#6E8A98"), Hex("#A4BECA"));
                    q.Rect(8, 7, 8, 5, Hex("#22404C"));
                    q.Px(10, 9, Hex("#4FA6BE"));
                    q.Glow(10, 9, 5f, Hex("#3E8EA8"), 0.16f);
                    q.Rect(w - 16, 8, 2, 6, Hex("#566A76")); // door handle
                    break;
                }
                case "compressor":
                {
                    var cx = w / 2; var cy = h / 2 + 2;
                    q.Block(4, 4, w - 8, h - 8, 10, Hex("#1E2830"), Hex("#2A3640"), Hex("#384652"), Hex("#151D24"));
                    q.Disc(cx, cy + 2, 13, Hex("#121A20"), Hex("#16202A"), Hex("#1C2832"));
                    for (var k = 0; k < 6; k++) { var a = k * Mathf.PI / 3f + 0.3f; q.Line(cx, cy + 2, cx + Mathf.RoundToInt(Mathf.Cos(a) * 11f), cy + 2 + Mathf.RoundToInt(Mathf.Sin(a) * 11f), Hex("#34424C")); }
                    q.Ring(cx, cy + 2, 13, Hex("#566A76"));
                    q.Frost(4, h - 14, w - 8, 10, Hex("#6E8A98"), Hex("#A4BECA"));
                    break;
                }
                case "rack":
                {
                    var horizontal = w > h;
                    if (horizontal)
                    {
                        q.Block(4, 6, w - 8, h - 12, 4, Hex("#1A232A"), Hex("#26313A"), Hex("#34424C"), Hex("#121920"));
                        for (var x = 8; x < w - 16; x += 14) q.Block(x, 9, 10, 10, 4, Hex("#3A3224"), Hex("#4E4430"), Hex("#5E5238"), Hex("#2A2418"));
                    }
                    else
                    {
                        q.Block(6, 4, w - 12, h - 8, 4, Hex("#1A232A"), Hex("#26313A"), Hex("#34424C"), Hex("#121920"));
                        for (var y = 8; y < h - 16; y += 14) q.Block(9, y, 10, 10, 4, Hex("#3A3224"), Hex("#4E4430"), Hex("#5E5238"), Hex("#2A2418"));
                    }

                    q.Frost(4, h - 10, w - 8, 6, Hex("#6E8A98"), Hex("#A4BECA"));
                    break;
                }
                case "cryotank":
                {
                    var cx = w / 2; var cy = h / 2 + 2;
                    q.Disc(cx, cy - 4, 20, Hex("#141C22"), Hex("#141C22"), Hex("#141C22"));
                    q.Disc(cx, cy, 20, Hex("#26323C"), Hex("#34424E"), Hex("#46586A"));
                    q.Ring(cx, cy, 16, Hex("#1C262E"));
                    q.Disc(cx, cy, 7, Hex("#22404C"), Hex("#2E5E6E"), Hex("#3E7E92"));
                    q.Glow(cx, cy, 12f, Hex("#3E8EA8"), 0.16f);
                    q.Frost(cx - 18, cy + 4, 36, 14, Hex("#6E8A98"), Hex("#A4BECA"));
                    break;
                }
                case "pallet":
                    q.Block(4, 6, w - 8, h - 12, 3, Hex("#2E2A22"), Hex("#3E382C"), Hex("#4E4636"), Hex("#221E18"));
                    q.Block(10, 9, w - 20, h - 18, 6, Hex("#2A3640"), Hex("#384652"), Hex("#4A5C6A"), Hex("#1E2830"));
                    q.Frost(8, h - 14, w - 16, 6, Hex("#6E8A98"), Hex("#A4BECA"));
                    break;
                case "icefall":
                    q.Rubble(4, 4, w - 8, h - 8, 12, Hex("#2A3A46"), Hex("#3E5664"), Hex("#6E8A98"));
                    q.Frost(2, 2, w - 4, h - 4, Hex("#4A6270"), Hex("#8FA9B5"));
                    break;
            }
        }
    }

    /// <summary>Drawing primitives for surrounding props: grounded blocks, discs, rubble, frost, vines, glow.</summary>
    internal sealed class PropPainter
    {
        private readonly RoomEnvironmentCanvas _c;
        private readonly Color32 _outline;
        private readonly Color32 _shadow;
        private readonly bool[] _solid;
        private uint _seed;

        public PropPainter(RoomEnvironmentCanvas c, Color32 outline, Color32 shadow, uint seed)
        {
            _c = c;
            _outline = outline;
            _shadow = shadow;
            _seed = seed;
            _solid = new bool[c.Width * c.Height];
        }

        private uint Next() => _seed = _seed * 1664525u + 1013904223u;
        private float Rand() => (Next() >> 8) / 16777216f;

        public void Px(int x, int y, Color32 col)
        {
            if (!_c.In(x, y)) return;
            _c.Set(x, y, col);
            _solid[y * _c.Width + x] = true;
        }

        public void Rect(int x, int y, int w, int h, Color32 col)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Px(xx, yy, col);
        }

        public void Line(int x0, int y0, int x1, int y1, Color32 col)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            for (var guard = 0; guard < 2048; guard++)
            {
                Px(x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                var e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>A grounded box in the three-quarter view: front face of <paramref name="front"/> px, lit top, cast shadow.</summary>
        public void Block(int x0, int y0, int w, int h, int front, Color32 dark, Color32 mid, Color32 light, Color32 frontColor)
        {
            for (var y = y0 - 3; y < y0 + h - 3; y++)
            for (var x = x0 + 2; x < x0 + w + 3; x++)
                _c.Over(x, y, _shadow, 0.45f);
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                Color32 col;
                if (y < y0 + front) col = y == y0 ? Shade(frontColor, 0.8f) : x >= x0 + w - 2 ? Shade(frontColor, 0.85f) : frontColor;
                else if (y == y0 + front) col = light; // lit front rim of the top
                else col = x >= x0 + w - 2 ? dark : y >= y0 + h - 2 ? dark : mid;
                Px(x, y, col);
            }
        }

        /// <summary>A flat broken slab: a low block whose outline is chewed by noise.</summary>
        public void Jagged(int x0, int y0, int w, int h, int front, Color32 dark, Color32 mid, Color32 light, Color32 frontColor)
        {
            bool Inside(int x, int y)
            {
                var ex = Mathf.Min(x - x0, x0 + w - 1 - x);
                var ey = Mathf.Min(y - y0, y0 + h - 1 - y);
                return ex >= 0 && ey >= 0 && Mathf.Min(ex, ey) >= (int)(Value(x, y, 3f, _seed) * 4f);
            }

            for (var y = y0 - 2; y < y0 + h; y++)
            for (var x = x0; x < x0 + w + 2; x++)
                if (Inside(x - 2, y + 2)) _c.Over(x, y, _shadow, 0.4f);
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                if (!Inside(x, y)) continue;
                var below = !Inside(x, y - front);
                Px(x, y, below ? frontColor : !Inside(x, y + 1) ? light : x >= x0 + w - 3 ? dark : mid);
            }
        }

        public void Disc(int cx, int cy, int r, Color32 dark, Color32 mid, Color32 light)
        {
            for (var y = cy - r - 3; y <= cy + r; y++)
            for (var x = cx - r; x <= cx + r + 3; x++)
            {
                var sx = x - cx - 2;
                var sy = y - cy + 3;
                if (sx * sx + sy * sy <= r * r) _c.Over(x, y, _shadow, 0.35f);
            }

            for (var y = cy - r; y <= cy + r; y++)
            for (var x = cx - r; x <= cx + r; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy > r * r) continue;
                var lit = -dx * 0.6f + dy * 0.8f;
                Px(x, y, lit > r * 0.45f ? light : lit < -r * 0.35f ? dark : mid);
            }
        }

        public void Ring(int cx, int cy, int r, Color32 col)
        {
            for (var k = 0; k < r * 8; k++)
            {
                var a = k * Mathf.PI * 2f / (r * 8);
                Px(cx + Mathf.RoundToInt(Mathf.Cos(a) * r), cy + Mathf.RoundToInt(Mathf.Sin(a) * r), col);
            }
        }

        public void Rubble(int x0, int y0, int w, int h, int count, Color32 dark, Color32 mid, Color32 light)
        {
            for (var i = 0; i < count; i++)
            {
                var cw = 3 + (int)(Rand() * 7);
                var ch = 2 + (int)(Rand() * 5);
                var x = x0 + (int)(Rand() * Mathf.Max(1, w - cw));
                var y = y0 + (int)(Rand() * Mathf.Max(1, h - ch));
                for (var yy = y - 1; yy < y + ch - 1; yy++)
                for (var xx = x + 1; xx < x + cw + 1; xx++)
                    _c.Over(xx, yy, _shadow, 0.35f);
                for (var yy = y; yy < y + ch; yy++)
                for (var xx = x; xx < x + cw; xx++)
                {
                    var corner = (xx == x || xx == x + cw - 1) && (yy == y || yy == y + ch - 1);
                    if (corner) continue;
                    Px(xx, yy, yy == y + ch - 1 ? light : yy == y ? dark : mid);
                }
            }
        }

        public void Crack(int x, int y, int length, Color32 col)
        {
            float fx = x, fy = y;
            var a = Rand() * Mathf.PI * 2f;
            for (var i = 0; i < length; i++)
            {
                fx += Mathf.Cos(a);
                fy += Mathf.Sin(a);
                a += (Rand() - 0.5f) * 0.8f;
                var px = Mathf.RoundToInt(fx);
                var py = Mathf.RoundToInt(fy);
                if (_c.In(px, py) && _solid[py * _c.Width + px]) _c.Set(px, py, col);
            }
        }

        /// <summary>A crushed, torn section: dark gaps and bent plate.</summary>
        public void Crush(int x0, int y0, int w, int h)
        {
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
                if (_c.In(x, y) && _solid[y * _c.Width + x] && White(x / 2, y / 2, _seed) < 0.3f) _c.Set(x, y, Shade(_c.Get(x, y), 0.45f));
            for (var i = 0; i < 3; i++) Crack(x0 + (int)(Rand() * w), y0 + (int)(Rand() * h), 12, Shade(_outline, 1.5f));
        }

        public void Chips(int count, Color32 dark, Color32 light)
        {
            for (var i = 0; i < count; i++)
            {
                var x = (int)(Rand() * _c.Width);
                var y = (int)(Rand() * _c.Height);
                if (!_c.In(x, y) || !_solid[y * _c.Width + x]) continue;
                _c.Set(x, y, dark);
                if (_c.In(x - 1, y + 1)) _c.Set(x - 1, y + 1, light);
            }
        }

        public void Dirt(float share, Color32 col)
        {
            for (var y = 0; y < _c.Height; y++)
            for (var x = 0; x < _c.Width; x++)
                if (_solid[y * _c.Width + x] && Value(x, y, 5f, _seed) < share * 3f && White(x, y, _seed) < 0.5f) _c.Set(x, y, col);
        }

        public void Cable(int x0, int y0, int x1, int y1, Color32 col) => Line(x0, y0, x1, y1, col);

        public void Shard(int x, int y, Color32 mid, Color32 light)
        {
            Px(x, y, mid);
            Px(x + 1, y, mid);
            Px(x, y + 1, light);
        }

        public void Puddle(int cx, int cy, int rx, int ry, Color32 col)
        {
            for (var y = cy - ry; y <= cy + ry; y++)
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                var dx = (x - cx) / (float)rx;
                var dy = (y - cy) / (float)ry;
                if (dx * dx + dy * dy <= 1f) _c.Over(x, y, col, 0.8f);
            }
        }

        public void Vines(int x0, int y0, int w, int h, Color32 dark, Color32 leaf)
        {
            for (var v = 0; v < 4; v++)
            {
                float x = x0 + Rand() * w, y = y0 + Rand() * h;
                var a = Rand() * Mathf.PI * 2f;
                for (var i = 0; i < 18; i++)
                {
                    x += Mathf.Cos(a);
                    y += Mathf.Sin(a);
                    a += (Rand() - 0.5f) * 0.9f;
                    var px = Mathf.RoundToInt(x);
                    var py = Mathf.RoundToInt(y);
                    if (px < x0 || py < y0 || px >= x0 + w || py >= y0 + h) break;
                    Px(px, py, dark);
                    if (i % 4 == 2) { Px(px + 1, py, leaf); Px(px, py + 1, leaf); }
                }
            }
        }

        public void Frost(int x0, int y0, int w, int h, Color32 mid, Color32 light)
        {
            for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                if (!_c.In(x, y) || !_solid[y * _c.Width + x]) continue;
                var n = Value(x, y, 3f, _seed + 7u) + (y - y0) / (float)Mathf.Max(1, h) * 0.3f;
                if (n > 0.72f) _c.Set(x, y, White(x, y, _seed) < 0.25f ? light : mid);
            }
        }

        public void Glow(int cx, int cy, float radius, Color32 col, float strength)
        {
            for (var y = (int)(cy - radius); y <= cy + radius; y++)
            for (var x = (int)(cx - radius); x <= cx + radius; x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1f) continue;
                _c.Over(x, y, col, strength * Mathf.Floor((1f - d) * 3f + 0.5f) / 3f);
            }
        }

        /// <summary>Outlines every solid pixel against the ground.</summary>
        public void Finish()
        {
            var add = new List<int>();
            for (var y = 0; y < _c.Height; y++)
            for (var x = 0; x < _c.Width; x++)
            {
                if (_solid[y * _c.Width + x]) continue;
                bool S(int xx, int yy) => _c.In(xx, yy) && _solid[yy * _c.Width + xx];
                if (S(x + 1, y) || S(x - 1, y) || S(x, y + 1) || S(x, y - 1)) add.Add(y * _c.Width + x);
            }

            foreach (var i in add) _c.Pixels[i] = _outline;
        }
    }
}
