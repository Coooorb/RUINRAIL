using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Loot;
using UnityEngine;
using static RuinRail.Dungeon.Runtime.RoomEnvironmentCanvas;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>The reward tier a dungeon chest is drawn as (presentation only; loot comes from its source kind).</summary>
    public enum ChestTier
    {
        /// <summary>A Supply Chest in an ordinary room: the plain field container.</summary>
        Supply,
        /// <summary>The chest an Elite leaves: the same container, reinforced and marked.</summary>
        Elite,
        /// <summary>A Loot room's equipment chest: a wide gear case.</summary>
        Equipment,
        /// <summary>A Treasure room's chest: a brass-bound strongbox.</summary>
        Treasure,
        /// <summary>The Boss Cache: a large armoured vault crate with a glowing core.</summary>
        Boss
    }

    /// <summary>
    /// Dungeon chest art, drawn per biome, tier and state (closed / opened / Boss Cache locked).
    ///
    /// All tiers share one construction — a reinforced container seen from the top-down three-quarter view, with
    /// corner brackets, a lid seam, a lock plate and a status lamp, grounded by its own contact shadow — so they read as
    /// one family; size, trim and markings climb with the tier: plain Supply, banded and chevron-marked Elite, wide
    /// gear case, brass-bound strongbox, and the Boss Cache twice the bulk with gold framing and a glowing biome core.
    /// Each biome builds the container its own way rather than recolouring one crate:
    ///   Ruined Metro — painted blue-grey transit maintenance crate with hazard tape and a stencilled number;
    ///   Rustworks — riveted rust-iron tool chest with black-iron straps and rust streaks;
    ///   Overgrown Labs — rounded off-white specimen case with a teal stripe and creeping moss;
    ///   Cryo Vaults — insulated dark cold-storage box with a frosted lid, a cyan seal and an amber label.
    /// The locked Boss Cache wears its biome's lock (clamp bars, chains, a containment field, an ice casing).
    ///
    /// Presentation only: it swaps the sprite through <see cref="WorldObjectVisual.SetSkin"/> under the same art keys
    /// and state machine; collider, prompt, loot and the transform are untouched (scale 1, so the art stays on the
    /// pixel grid). Every sprite is a pure function of (biome, tier, state), identical on every peer, painted once per
    /// session and cached.
    /// </summary>
    public static class ChestArt
    {
        public const int NormalCanvas = 40;
        public const int BossCanvas = 64;

        private static readonly Dictionary<(Biome, ChestTier, string), Sprite> Cache = new();

        /// <summary>The tier a chest of this source kind is drawn as; the Elite reward is a Supply Chest by loot but its own tier by look.</summary>
        public static ChestTier TierOf(LootSourceKind kind, bool eliteReward = false) => kind switch
        {
            LootSourceKind.BossCache => ChestTier.Boss,
            LootSourceKind.TreasureChest => ChestTier.Treasure,
            LootSourceKind.EquipmentChest => ChestTier.Equipment,
            _ => eliteReward ? ChestTier.Elite : ChestTier.Supply
        };

        /// <summary>Draws the chest for its biome and tier from here on, in every state.</summary>
        public static void Apply(SupplyChest chest, Biome biome, ChestTier tier)
        {
            var visual = chest != null ? chest.Visual : null;
            if (visual == null) return;
            if (visual.Renderer != null)
            {
                visual.Renderer.color = Color.white;
                visual.Renderer.transform.localScale = Vector3.one;
            }

            visual.SetSkin(key => For(biome, tier, key));
        }

        /// <summary>The sprite for an art key (closed, opened or cache gate); null for a key that is not a chest state.</summary>
        public static Sprite For(Biome biome, ChestTier tier, string key)
        {
            if (key != WorldObjectArt.SupplyChest && key != WorldObjectArt.SupplyChestOpen && key != WorldObjectArt.BossCacheGate) return null;
            if (Cache.TryGetValue((biome, tier, key), out var cached) && cached != null) return cached;
            var size = tier == ChestTier.Boss ? BossCanvas : NormalCanvas;
            var canvas = new RoomEnvironmentCanvas(size, size);
            var painter = new ChestPainter(canvas, biome, tier);
            var pivotY = painter.Paint(key == WorldObjectArt.SupplyChestOpen, key == WorldObjectArt.BossCacheGate);
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = $"Chest_{biome}_{tier}_{key}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(canvas.Pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, pivotY / (float)size), 32f, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;
            Cache[(biome, tier, key)] = sprite;
            return sprite;
        }

        /// <summary>Visible pixel width of a closed chest of this tier (tests compare the hierarchy).</summary>
        public static int BodyWidth(ChestTier tier) => ChestPainter.Shape(tier).W;
    }

    internal sealed class ChestPainter
    {
        private readonly RoomEnvironmentCanvas _c;
        private readonly Biome _biome;
        private readonly ChestTier _tier;
        private readonly bool[] _body;

        private struct Ramp
        {
            public Color32 Dark, Mid, Light, Edge;
            public Ramp(string dark, string mid, string light, string edge) { Dark = Hex(dark); Mid = Hex(mid); Light = Hex(light); Edge = Hex(edge); }
        }

        private static readonly Ramp Gold = new("#6E521E", "#A8823A", "#D8B25A", "#F2DA8E");
        private static readonly Color32 Outline = Hex("#121518");
        private static readonly Color32 ShadowTone = Hex("#06080A");

        private Ramp _bodyRamp, _trim;
        private Color32 _lamp, _accent, _accent2;

        public ChestPainter(RoomEnvironmentCanvas canvas, Biome biome, ChestTier tier)
        {
            _c = canvas;
            _biome = biome;
            _tier = tier;
            _body = new bool[canvas.Width * canvas.Height];
            switch (biome)
            {
                case Biome.Rustworks:
                    _bodyRamp = new Ramp("#45241A", "#7A4229", "#9E5D38", "#C77D4C");
                    _trim = new Ramp("#1F1C1A", "#3A3633", "#5E5853", "#8E857C");
                    _lamp = Hex("#FF8A3D"); _accent = Hex("#C8932F"); _accent2 = Hex("#4A2A1A");
                    break;
                case Biome.OvergrownLabs:
                    _bodyRamp = new Ramp("#7C8078", "#B7BBB1", "#D8DBD2", "#F0F2EC");
                    _trim = new Ramp("#284448", "#3E8A93", "#62B2BB", "#A6DDE2");
                    _lamp = Hex("#8CFF7A"); _accent = Hex("#3E8A93"); _accent2 = Hex("#3F6A2A");
                    break;
                case Biome.CryoVaults:
                    _bodyRamp = new Ramp("#202A33", "#34424D", "#4C5E6C", "#6B8090");
                    _trim = new Ramp("#4B6573", "#7FA0AE", "#B4D3DE", "#E2F3F8");
                    _lamp = Hex("#7FDFFF"); _accent = Hex("#D9B85A"); _accent2 = Hex("#E6F6FB");
                    break;
                default:
                    _bodyRamp = new Ramp("#2E3840", "#4A5864", "#6A7B88", "#90A1AC");
                    _trim = new Ramp("#40474B", "#767F83", "#A9B1B3", "#D2D7D6");
                    _lamp = Hex("#FFB347"); _accent = Hex("#C9A227"); _accent2 = Hex("#1E2224");
                    break;
            }

            if (tier == ChestTier.Equipment) _bodyRamp = Darker(_bodyRamp, biome == Biome.OvergrownLabs ? 0.8f : 0.85f);
            if (tier is ChestTier.Treasure or ChestTier.Boss) _trim = Gold;
        }

        private static Ramp Darker(Ramp r, float k) => new() { Dark = Shade(r.Dark, k), Mid = Shade(r.Mid, k), Light = Shade(r.Light, k), Edge = Shade(r.Edge, k) };

        /// <summary>Box width, front height and lid depth per tier.</summary>
        public static (int W, int Hf, int Ht) Shape(ChestTier tier) => tier switch
        {
            ChestTier.Elite => (22, 11, 8),
            ChestTier.Equipment => (26, 9, 8),
            ChestTier.Treasure => (26, 11, 9),
            ChestTier.Boss => (36, 15, 12),
            _ => (20, 10, 8)
        };

        // ---------------------------------------------------------------- primitives

        private void Px(int x, int y, Color32 c, bool body = true)
        {
            if (!_c.In(x, y)) return;
            _c.Set(x, y, c);
            if (body) _body[y * _c.Width + x] = true;
        }

        private void Blend(int x, int y, Color32 c, float a) => _c.Over(x, y, c, a);

        private void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                Px(xx, yy, c);
        }

        private bool IsBody(int x, int y) => _c.In(x, y) && _body[y * _c.Width + x];

        private void OutlineBody()
        {
            var add = new List<Vector2Int>();
            for (var y = 0; y < _c.Height; y++)
            for (var x = 0; x < _c.Width; x++)
            {
                if (IsBody(x, y)) continue;
                if (IsBody(x + 1, y) || IsBody(x - 1, y) || IsBody(x, y + 1) || IsBody(x, y - 1)) add.Add(new Vector2Int(x, y));
            }

            foreach (var p in add) Px(p.x, p.y, Outline, false);
        }

        private void Glow(int cx, int cy, float radius, Color32 color, float strength)
        {
            for (var y = (int)(cy - radius); y <= cy + radius; y++)
            for (var x = (int)(cx - radius); x <= cx + radius; x++)
            {
                var d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / radius;
                if (d >= 1f || IsBody(x, y)) continue;
                Blend(x, y, color, strength * Mathf.Floor((1f - d) * 3f + 0.5f) / 3f);
            }
        }

        // ---------------------------------------------------------------- paint

        /// <summary>Paints the chest; returns the pivot height in pixels (the closed box's visual centre).</summary>
        public int Paint(bool opened, bool locked)
        {
            var (w, hf, ht) = Shape(_tier);
            var boss = _tier == ChestTier.Boss;
            var x0 = (_c.Width - w) / 2;
            var yb = boss ? 12 : 9;
            var top = yb + hf + ht;

            // Ground: a soft contact shadow; the Boss Cache also throws its core's light on the floor in front.
            Ellipse(_c.Width / 2, yb, w / 2 + 3, 3, ShadowTone, 0.5f);
            Ellipse(_c.Width / 2, yb - 1, w / 2 + 1, 2, ShadowTone, 0.35f);
            if (boss && !locked && !opened) Ellipse(_c.Width / 2, yb - 3, w / 2 - 2, 3, Color32.Lerp(CoreGlow(), Hex("#FFFFFF"), 0.35f), 0.3f);
            if (boss) Skid(x0, yb, w);

            Front(x0, yb, w, hf);
            LidBand(x0, yb + hf - 3, w, opened);
            if (opened) Interior(x0, yb + hf, w, ht); else Lid(x0, yb + hf, w, ht);
            Brackets(x0, yb, w, hf, ht, opened);
            BiomeFront(x0, yb, w, hf, ht, opened);
            TierFront(x0, yb, w, hf, ht, opened, locked);
            LockPlate(x0 + w / 2, yb + hf - (boss ? 9 : 6), opened, locked);
            if (opened) RaisedLid(x0, top, w, ht);
            if (locked) BiomeLock(x0, yb, w, hf, ht);
            OutlineBody();
            if (boss && !locked && !opened) Glow(_c.Width / 2, yb + 6, 10f, CoreGlow(), 0.3f);
            return yb + (hf + ht) / 2;
        }

        /// <summary>The Boss Cache stands on a heavy skid: a wider dark base plate with lit edge.</summary>
        private void Skid(int x0, int yb, int w)
        {
            for (var y = yb - 2; y < yb + 1; y++)
            for (var x = x0 - 2; x < x0 + w + 2; x++)
                Px(x, y, y == yb ? _trim.Light : y == yb - 1 ? _trim.Mid : _trim.Dark);
            for (var x = x0; x < x0 + w; x += 6) Px(x, yb - 1, _trim.Edge);
        }

        /// <summary>The lid's front band, overhanging the box by a pixel on each side while closed.</summary>
        private void LidBand(int x0, int y0, int w, bool opened)
        {
            var over = opened ? 0 : 1;
            for (var y = y0; y < y0 + 3; y++)
            for (var x = x0 - over; x < x0 + w + over; x++)
            {
                if (Rounded && (x == x0 - over || x == x0 + w - 1 + over) && y == y0) continue;
                var c = y == y0 + 2 ? _bodyRamp.Light : y == y0 ? Shade(_bodyRamp.Dark, 0.8f) : _bodyRamp.Mid;
                if (x >= x0 + w - 1) c = Shade(c, 0.8f);
                Px(x, y, c);
            }
        }

        private Color32 CoreGlow() => _biome switch
        {
            Biome.Rustworks => Hex("#FF9A3C"),
            Biome.OvergrownLabs => Hex("#9CFF8A"),
            Biome.CryoVaults => Hex("#8FE6FF"),
            _ => Hex("#FFC66B")
        };

        private void Ellipse(int cx, int cy, int rx, int ry, Color32 color, float alpha)
        {
            for (var y = cy - ry; y <= cy + ry; y++)
            for (var x = cx - rx; x <= cx + rx; x++)
            {
                var dx = (x - cx) / (float)rx;
                var dy = (y - cy) / (float)ry;
                if (dx * dx + dy * dy > 1f) continue;
                Blend(x, y, color, alpha);
            }
        }

        private bool Rounded => _biome == Biome.OvergrownLabs;

        private void Front(int x0, int yb, int w, int hf)
        {
            for (var y = yb; y < yb + hf - 3; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                if (Rounded && (x == x0 || x == x0 + w - 1) && y == yb) continue;
                var c = _bodyRamp.Mid;
                if (x == x0) c = _bodyRamp.Light;
                if (x >= x0 + w - 2) c = _bodyRamp.Dark;
                if (y == yb) c = Shade(_bodyRamp.Dark, 0.85f);
                if (y == yb + hf - 4) c = Shade(c, 0.85f); // shade under the lid band
                Px(x, y, c);
            }

            // Material texture on the front face.
            for (var y = yb + 1; y < yb + hf - 4; y++)
            for (var x = x0 + 1; x < x0 + w - 2; x++)
            {
                switch (_biome)
                {
                    case Biome.Rustworks when White(x, y, 7u) < 0.12f:
                        Px(x, y, White(x, y, 8u) < 0.5f ? _accent2 : _bodyRamp.Edge); break;
                    case Biome.RuinedMetro when White(x, y, 7u) < 0.05f:
                        Px(x, y, _bodyRamp.Light); break;
                    case Biome.CryoVaults when (x - x0) % 5 == 0:
                        Px(x, y, Shade(_bodyRamp.Mid, 0.9f)); break; // insulated ribs
                }
            }
        }

        private void Lid(int x0, int y0, int w, int ht)
        {
            for (var y = y0; y < y0 + ht; y++)
            for (var x = x0 - 1; x < x0 + w + 1; x++)
            {
                if (Rounded && (x == x0 - 1 || x == x0 + w) && (y == y0 + ht - 1 || y == y0)) continue;
                var c = _bodyRamp.Light;
                if (y == y0) c = _bodyRamp.Edge; // the lit front rim
                if (y >= y0 + ht - 2) c = _bodyRamp.Mid;
                if (x >= x0 + w - 1) c = _bodyRamp.Mid;
                if (x == x0 - 1) c = _bodyRamp.Edge;
                Px(x, y, c);
            }

            if (_tier == ChestTier.Treasure)
            {
                // A domed lid: a brighter crown band through the middle.
                var crown = y0 + ht / 2;
                for (var x = x0 + 1; x < x0 + w - 2; x++) { Px(x, crown, _bodyRamp.Edge); Px(x, crown + 1, Shade(_bodyRamp.Edge, 0.95f)); }
            }
        }

        private void Interior(int x0, int y0, int w, int ht)
        {
            // Looking into the open box: the far inner wall lit, the floor of the box in deep shadow, the near rim bright.
            var deep = Shade(_bodyRamp.Dark, 0.4f);
            for (var y = y0; y < y0 + ht; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var rim = y == y0 || x == x0 || x == x0 + w - 1 || y == y0 + ht - 1;
                var farWall = y >= y0 + ht - 3 && !rim;
                Px(x, y, rim ? (y == y0 ? _bodyRamp.Edge : _bodyRamp.Light) : farWall ? Shade(_bodyRamp.Mid, 0.7f) : deep);
            }

            // What was left behind: loose packing, or the cache's dying core light.
            var seed = (uint)(_biome + 1) * 13u + (uint)_tier;
            for (var i = 0; i < w / 3; i++)
            {
                var x = x0 + 2 + (int)(White(i, 1, seed) * (w - 4));
                var y = y0 + 1 + (int)(White(i, 2, seed) * (ht - 4));
                Px(x, y, Hex("#B59E68"));
                if (White(i, 3, seed) < 0.4f) Px(x + 1, y, Hex("#8A7650"));
            }

            if (_tier == ChestTier.Boss)
                for (var y = y0 + 1; y < y0 + ht - 3; y++)
                for (var x = x0 + 2; x < x0 + w - 2; x++)
                    Blend(x, y, CoreGlow(), 0.18f + 0.1f * ((x + y) % 3 == 0 ? 1 : 0));
        }

        private void RaisedLid(int x0, int top, int w, int ht)
        {
            // The lid hinged back past upright: a short slab showing its inner face, with a shadow gap at the hinge.
            var h = Mathf.Max(4, ht - 2);
            for (var x = x0; x < x0 + w; x++) Px(x, top, Shade(_bodyRamp.Dark, 0.5f));
            for (var y = top + 1; y < top + 1 + h; y++)
            for (var x = x0 - 1; x < x0 + w + 1; x++)
            {
                var edge = y == top + h || x == x0 - 1 || x == x0 + w;
                var c = edge ? (y == top + h ? _trim.Light : _trim.Mid) : y == top + 1 ? Shade(_bodyRamp.Mid, 0.75f) : Shade(_bodyRamp.Light, 0.82f);
                if (!edge && (x - x0) % 7 == 3) c = Shade(c, 0.88f);
                Px(x, y, c);
            }
        }

        private void Brackets(int x0, int yb, int w, int hf, int ht, bool opened)
        {
            if (Rounded) return; // the specimen case is moulded, not bracketed
            var size = _tier == ChestTier.Boss ? 4 : 3;
            void Corner(int cx, int cy, int sx, int sy)
            {
                for (var i = 0; i < size; i++)
                {
                    Px(cx + i * sx, cy, i == 0 ? _trim.Light : _trim.Mid);
                    Px(cx, cy + i * sy, i == 0 ? _trim.Light : _trim.Mid);
                }
            }

            Corner(x0, yb, 1, 1);
            Corner(x0 + w - 1, yb, -1, 1);
            Corner(x0, yb + hf - 1, 1, -1);
            Corner(x0 + w - 1, yb + hf - 1, -1, -1);
            if (!opened)
            {
                Corner(x0, yb + hf + ht - 1, 1, -1);
                Corner(x0 + w - 1, yb + hf + ht - 1, -1, -1);
            }

            if (_biome == Biome.Rustworks || _tier is ChestTier.Treasure or ChestTier.Boss)
            {
                // Rivets along the straps.
                for (var x = x0 + 4; x < x0 + w - 4; x += 4)
                {
                    Px(x, yb + 1, _trim.Edge);
                    Px(x, yb + hf - 2, _trim.Edge);
                }
            }
        }

        private void BiomeFront(int x0, int yb, int w, int hf, int ht, bool opened)
        {
            switch (_biome)
            {
                case Biome.RuinedMetro:
                {
                    // Hazard tape along the lid's front rim, and a stencilled crate number.
                    if (!opened)
                        for (var x = x0 + 1; x < x0 + w - 1; x++)
                            Px(x, yb + hf + 1, ((x + yb) / 2 & 1) == 0 ? _accent : _accent2);
                    Digit(x0 + 3, yb + hf - 5, (int)_tier + 3, Hex("#D8DCD6"));
                    break;
                }
                case Biome.Rustworks:
                {
                    // Rust bleeding down from the seam.
                    for (var x = x0 + 2; x < x0 + w - 2; x++)
                    {
                        if (White(x, 3, 11u) > 0.3f) continue;
                        var len = 2 + (int)(White(x, 4, 11u) * 5f);
                        for (var k = 0; k < len; k++) Px(x, yb + hf - 5 - k, Shade(_accent2, 1.2f));
                    }

                    break;
                }
                case Biome.OvergrownLabs:
                {
                    // Teal stripe, and moss creeping up the lower corner.
                    for (var x = x0 + 1; x < x0 + w - 1; x++) { Px(x, yb + 3, _accent); Px(x, yb + 4, Shade(_accent, 1.2f)); }
                    for (var i = 0; i < 9; i++)
                    {
                        var x = x0 + (int)(White(i, 5, 17u) * 6f);
                        var y = yb + (int)(White(i, 6, 17u) * 4f);
                        Px(x, y, White(i, 7, 17u) < 0.5f ? Hex("#2C4524") : Hex("#5F8A3A"));
                    }

                    break;
                }
                case Biome.CryoVaults:
                {
                    // Frost on the lid, a cyan seal at the seam, an amber cold-chain label.
                    if (!opened)
                        for (var y = yb + hf; y < yb + hf + ht; y++)
                        for (var x = x0; x < x0 + w; x++)
                        {
                            var n = Value(x, y, 3f, 23u) + (y - yb - hf) * 0.08f;
                            if (n > 0.62f) Px(x, y, White(x, y, 24u) < 0.25f ? Hex("#FFFFFF") : _accent2);
                        }

                    for (var x = x0 + 1; x < x0 + w - 1; x++) Px(x, yb + hf - 3, _trim.Light);
                    Rect(x0 + 3, yb + 3, 5, 3, _accent);
                    Px(x0 + 4, yb + 4, Shade(_accent, 0.6f));
                    Px(x0 + 6, yb + 4, Shade(_accent, 0.6f));
                    break;
                }
            }
        }

        private void TierFront(int x0, int yb, int w, int hf, int ht, bool opened, bool locked)
        {
            switch (_tier)
            {
                case ChestTier.Elite:
                {
                    // Two reinforcing bands over the front and the lid, and a gold double chevron.
                    foreach (var bx in new[] { x0 + w / 4, x0 + w - 1 - w / 4 })
                    for (var y = yb; y < yb + hf + (opened ? 0 : ht); y++)
                        Px(bx, y, y == yb + hf ? _trim.Edge : _trim.Mid);
                    var cx = x0 + w / 2;
                    var chevron = Hex("#E8A33A");
                    for (var k = 0; k < 2; k++)
                    for (var i = -4; i <= 4; i++)
                    {
                        var y = yb + 1 + k * 3 + (4 - Mathf.Abs(i)) / 2;
                        Px(cx + i, y, chevron);
                        Px(cx + i, y + 1, Shade(chevron, 0.7f));
                    }

                    if (!opened) for (var x = x0; x < x0 + w; x++) Px(x, yb + hf + ht - 2, Shade(chevron, 0.85f));
                    break;
                }
                case ChestTier.Equipment:
                {
                    // A carry handle on the lid, two latches, and a marking stripe.
                    if (!opened)
                    {
                        var hy = yb + hf + ht - 3;
                        for (var x = x0 + w / 2 - 4; x <= x0 + w / 2 + 4; x++) Px(x, hy + 1, _trim.Light);
                        Px(x0 + w / 2 - 4, hy, _trim.Mid);
                        Px(x0 + w / 2 + 4, hy, _trim.Mid);
                    }

                    foreach (var lx in new[] { x0 + 4, x0 + w - 6 }) Rect(lx, yb + hf - 5, 2, 3, _trim.Light);
                    for (var x = x0 + 1; x < x0 + w - 2; x++) Px(x, yb + 2, _accent);
                    break;
                }
                case ChestTier.Treasure:
                {
                    // Gold bands and studs.
                    foreach (var bx in new[] { x0 + 4, x0 + w - 5 })
                    for (var y = yb; y < yb + hf + (opened ? 0 : ht); y++)
                        Px(bx, y, (y & 1) == 0 ? _trim.Mid : _trim.Light);
                    for (var x = x0 + 2; x < x0 + w - 2; x += 3) Px(x, yb + hf - 2, _trim.Edge);
                    break;
                }
                case ChestTier.Boss:
                {
                    // A heavy gold frame, side handles and a glowing core window.
                    for (var x = x0; x < x0 + w; x++) { Px(x, yb + 1, _trim.Mid); Px(x, yb + hf - 2, _trim.Light); }
                    for (var y = yb; y < yb + hf; y++) { Px(x0 + 1, y, _trim.Light); Px(x0 + w - 2, y, _trim.Mid); }
                    Rect(x0 - 2, yb + 4, 2, 5, _trim.Dark);
                    Rect(x0 + w, yb + 4, 2, 5, _trim.Dark);
                    Px(x0 - 2, yb + 8, _trim.Light);
                    Px(x0 + w + 1, yb + 8, _trim.Light);
                    // Solid gold corner caps.
                    foreach (var cx0 in new[] { x0, x0 + w - 4 })
                    foreach (var cy0 in new[] { yb, yb + hf - 7 })
                    {
                        Rect(cx0, cy0, 4, 4, _trim.Mid);
                        Px(cx0 + 1, cy0 + 2, _trim.Edge);
                    }

                    var wx = x0 + w / 2 - 6;
                    var wy = yb + 2;
                    for (var y = 0; y < 6; y++)
                    for (var x = 0; x < 12; x++)
                    {
                        var frame = x == 0 || y == 0 || x == 11 || y == 5;
                        Color32 c;
                        if (frame) c = _trim.Dark;
                        else if (locked || opened) c = Shade(_bodyRamp.Dark, 0.6f);
                        else
                        {
                            var core = Mathf.Abs(x - 5.5f) < 2.5f && Mathf.Abs(y - 2.5f) < 1.5f;
                            c = core ? Hex("#FFFFFF") : Color32.Lerp(CoreGlow(), Hex("#FFFFFF"), 0.25f);
                            if (x == 1 || y == 1) c = Shade(CoreGlow(), 0.75f);
                        }

                        Px(wx + x, wy + y, c);
                    }

                    if (!opened)
                    {
                        for (var x = x0 + 1; x < x0 + w - 1; x++)
                        {
                            Px(x, yb + hf + 2, _trim.Mid);
                            Px(x, yb + hf + ht - 3, _trim.Mid);
                        }

                        // The lid's emblem plate with the biome gem.
                        var ex = x0 + w / 2 - 4;
                        var ey = yb + hf + 4;
                        Rect(ex, ey, 8, 4, _trim.Mid);
                        for (var x = ex; x < ex + 8; x++) Px(x, ey + 3, _trim.Edge);
                        Rect(ex + 3, ey + 1, 2, 2, locked ? Shade(_bodyRamp.Dark, 0.6f) : CoreGlow());
                    }

                    break;
                }
            }
        }

        private void LockPlate(int cx, int y, bool opened, bool locked)
        {
            var w = _tier == ChestTier.Boss ? 7 : 5;
            var h = _tier == ChestTier.Boss ? 5 : 4;
            var x0 = cx - w / 2;
            if (_tier == ChestTier.Boss) y += 2;
            for (var yy = 0; yy < h; yy++)
            for (var xx = 0; xx < w; xx++)
            {
                var edge = xx == 0 || yy == 0 || xx == w - 1 || yy == h - 1;
                Px(x0 + xx, y + yy, edge ? _trim.Dark : yy == h - 2 ? _trim.Light : _trim.Mid);
            }

            // The status lamp: lit while it holds something, off once looted, a dull warning while locked.
            var lamp = opened ? Hex("#3C4A3E") : locked ? Hex("#9A4A2E") : _lamp;
            Px(cx, y + h - 2, lamp);
            if (w > 5) Px(cx - 1, y + h - 2, lamp);
            if (!opened && !locked) Px(cx, y + h - 1, Color32.Lerp(_lamp, Hex("#FFFFFF"), 0.5f));
            if (_tier == ChestTier.Treasure) Px(cx, y + 1, Hex("#141414")); // keyhole
        }

        private void BiomeLock(int x0, int yb, int w, int hf, int ht)
        {
            switch (_biome)
            {
                case Biome.Rustworks:
                {
                    // Heavy chains crossed over the front.
                    for (var t = 0; t <= w; t++)
                    {
                        var y1 = yb + 1 + t * (hf - 2) / w;
                        var y2 = yb + hf - 2 - t * (hf - 2) / w;
                        var link = (t / 2 & 1) == 0 ? Hex("#8E857C") : Hex("#4A4440");
                        Px(x0 + t, y1, link);
                        Px(x0 + t, y2, link);
                    }

                    Rect(x0 + w / 2 - 2, yb + hf / 2 - 2, 5, 5, Hex("#5E5853"));
                    Px(x0 + w / 2, yb + hf / 2, Hex("#141414"));
                    break;
                }
                case Biome.OvergrownLabs:
                {
                    // A containment field: emitters at the corners and a translucent sheet over the case.
                    for (var y = yb; y < yb + hf + ht; y++)
                    for (var x = x0 - 1; x <= x0 + w; x++)
                        Blend(x, y, Hex("#7CFFC0"), (y + x / 3) % 4 == 0 ? 0.55f : 0.18f);
                    foreach (var ex in new[] { x0 - 2, x0 + w + 1 })
                    {
                        Rect(ex, yb, 1, 3, Hex("#284448"));
                        Px(ex, yb + 3, Hex("#BFFFE0"));
                    }

                    break;
                }
                case Biome.CryoVaults:
                {
                    // An ice casing over the lower front, with icicles from the lid.
                    for (var y = yb; y < yb + hf / 2 + 1; y++)
                    for (var x = x0 - 1; x <= x0 + w; x++)
                    {
                        var shine = (x - y) % 7 == 0;
                        Blend(x, y, shine ? Hex("#FFFFFF") : Hex("#BFE3EE"), shine ? 0.85f : 0.55f);
                    }

                    for (var x = x0 + 2; x < x0 + w - 2; x += 3)
                    {
                        var len = 1 + (x * 7 % 4);
                        for (var k = 0; k < len; k++) Blend(x, yb + hf - 3 - k, Hex("#E6F6FB"), 0.9f);
                    }

                    break;
                }
                default:
                {
                    // Steel clamp bars and a padlock.
                    foreach (var by in new[] { yb + 2, yb + hf - 5 })
                    for (var x = x0 - 1; x <= x0 + w; x++)
                    {
                        Px(x, by, Hex("#3A4044"));
                        Px(x, by + 1, Hex("#8A9396"));
                    }

                    Rect(x0 + w / 2 - 2, yb + hf / 2 - 3, 5, 5, Hex("#C9A227"));
                    Px(x0 + w / 2, yb + hf / 2 - 1, Hex("#141414"));
                    for (var x = x0 + w / 2 - 1; x <= x0 + w / 2 + 1; x++) Px(x, yb + hf / 2 + 2, Hex("#8A9396"));
                    break;
                }
            }
        }

        private static readonly string[] Digits =
        {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111", "101101111001001",
            "111100111001111", "111100111101111", "111001001001001", "111101111101111", "111101111001111"
        };

        private void Digit(int x, int yTop, int d, Color32 color)
        {
            var bits = Digits[Mathf.Abs(d) % 10];
            for (var r = 0; r < 5; r++)
            for (var c = 0; c < 3; c++)
                if (bits[r * 3 + c] == '1') Px(x + c, yTop - r, color);
        }
    }
}
