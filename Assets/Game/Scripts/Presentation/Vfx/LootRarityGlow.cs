using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// Ground presentation of a dropped item's rarity (art/104 loot readability): Common stays quiet; each rarer tier gets
    /// a slightly larger, stronger pixel glow on the floor under the item, Epic adds a short soft shimmer column and
    /// Legendary a taller one with a rising glint now and then. The colour is the item's established rarity colour
    /// (the UI's rarity style, handed in by the composition), the tier is the item's own authoritative
    /// <see cref="ItemInstance.Rarity"/>, and pickups without a rarity meaning (ammo; coins are a different pickup) show
    /// nothing. It is a child of the pickup, so it follows it and dies with it; it re-reads the item every frame and hides
    /// the moment the item is taken. It draws on the ground-details layer just under hazard footprints, so hazards,
    /// telegraphs, characters and the item itself always draw over it. No collider, no light.
    /// </summary>
    public sealed class LootRarityGlow : MonoBehaviour
    {
        /// <summary>Order inside the ground-details layer: above decals and room details, below hazard footprints (10).</summary>
        public const int GroundOrder = 8;
        /// <summary>The tallest shimmer column (pixels): a short column at the item, never a screen-height beam.</summary>
        public const int MaxBeamPixels = 28;
        private const float Px = 1f / SortingConvention.PixelsPerUnit;
        private const float FeetOffset = -0.18f;

        private static readonly Dictionary<(int, int), Sprite> Ellipses = new();
        private static readonly Dictionary<int, Sprite> Columns = new();

        private WorldItemPickup _pickup;
        private Func<Rarity, Color> _colorOf;
        private SpriteRenderer _glow;
        private SpriteRenderer _beam;
        private SpriteRenderer _glint;
        private Rarity? _shown;
        private float _time;
        private float _glintAge = -1f;
        private float _nextGlint;

        /// <summary>The tier currently shown (null: nothing, e.g. Common, ammo or taken).</summary>
        public Rarity? ShownRarity => _shown;
        public bool IsGlowVisible => _glow != null && _glow.enabled;
        public bool IsBeamVisible => _beam != null && _beam.enabled;
        public SpriteRenderer Glow => _glow;
        public SpriteRenderer Beam => _beam;
        public Color TierColor { get; private set; }

        /// <summary>Gives <paramref name="pickup"/> its rarity presentation (once; a second call returns the existing one).</summary>
        public static LootRarityGlow Attach(WorldItemPickup pickup, Func<Rarity, Color> colorOf)
        {
            if (pickup == null || colorOf == null) return null;
            var existing = pickup.GetComponentInChildren<LootRarityGlow>(true);
            if (existing != null) return existing;
            var go = new GameObject("LootRarityGlow");
            go.transform.SetParent(pickup.transform, false);
            var glow = go.AddComponent<LootRarityGlow>();
            glow._pickup = pickup;
            glow._colorOf = colorOf;
            glow._glow = glow.NewRenderer("Glow");
            glow._beam = glow.NewRenderer("Shimmer");
            glow._glint = glow.NewRenderer("Glint");
            glow._glint.sprite = RoomEntryAtmosphere.Pixel;
            pickup.PickedUp += glow.OnPickedUp;
            glow.Refresh();
            return glow;
        }

        /// <summary>The tier an item shows: none for Common and for pickups whose category has no rarity meaning (ammo).</summary>
        public static Rarity? TierOf(WorldItemPickup pickup)
        {
            if (pickup == null || pickup.IsConsumed || pickup.Item == null) return null;
            if (pickup.Category == ItemCategory.Ammo) return null;
            return pickup.Item.Rarity == Rarity.Common ? null : pickup.Item.Rarity;
        }

        private SpriteRenderer NewRenderer(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            SpriteSorting.Apply(renderer, SortingRole.FloorDetail);
            renderer.sortingOrder = GroundOrder;
            renderer.enabled = false;
            return renderer;
        }

        private void OnPickedUp(WorldItemPickup _) => Hide();

        private void OnDestroy()
        {
            if (_pickup != null) _pickup.PickedUp -= OnPickedUp;
        }

        private void Hide()
        {
            _shown = null;
            if (_glow != null) _glow.enabled = false;
            if (_beam != null) _beam.enabled = false;
            if (_glint != null) _glint.enabled = false;
        }

        /// <summary>Re-reads the item and rebuilds the tier if it changed (public for deterministic proof).</summary>
        public void Refresh()
        {
            var tier = TierOf(_pickup);
            if (tier == null) { Hide(); return; }
            if (_shown == tier) return;
            _shown = tier;
            var rank = TierRank(tier.Value);
            TierColor = _colorOf(tier.Value);
            // Glow footprint and strength grow with the tier; all sizes are whole pixels.
            var (w, h) = rank switch { 1 => (14, 5), 2 => (18, 7), 3 => (22, 8), _ => (26, 9) };
            _glow.sprite = Ellipse(w, h);
            _glow.transform.localPosition = new Vector3(0f, FeetOffset, 0f);
            _glow.enabled = true;
            // The item sprite covers the column's foot; what clears the item is the short shimmer.
            var beamHeight = rank switch { 3 => 20, >= 4 => MaxBeamPixels, _ => 0 };
            _beam.enabled = beamHeight > 0;
            if (beamHeight > 0)
            {
                _beam.sprite = Column(beamHeight);
                _beam.transform.localScale = new Vector3(rank >= 4 ? 3f : 2f, 1f, 1f);
                _beam.transform.localPosition = new Vector3(0f, FeetOffset + beamHeight * Px * 0.5f, 0f);
            }

            _glint.enabled = false;
            _glintAge = -1f;
            _nextGlint = 1.2f;
            Apply();
        }

        private static int TierRank(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => 1,
            Rarity.Rare => 2,
            Rarity.Epic => 3,
            _ => 4
        };

        private void Update()
        {
            Refresh();
            if (_shown == null) return;
            _time += Time.deltaTime;
            if (TierRank(_shown.Value) >= 4) TickGlint(Time.deltaTime);
            Apply();
        }

        private void Apply()
        {
            if (_shown == null) return;
            var rank = TierRank(_shown.Value);
            // A slow breath; stronger tiers breathe a little deeper. Never a flash.
            var breath = 0.5f + 0.5f * Mathf.Sin(_time * 2.2f);
            var glowAlpha = (rank switch { 1 => 0.26f, 2 => 0.30f, 3 => 0.36f, _ => 0.42f }) * (0.8f + 0.2f * breath * (rank >= 3 ? 1.5f : 1f));
            _glow.color = new Color(TierColor.r, TierColor.g, TierColor.b, Mathf.Clamp01(glowAlpha));
            if (_beam.enabled)
            {
                var beamAlpha = (rank >= 4 ? 0.42f : 0.32f) * (0.7f + 0.3f * breath);
                _beam.color = new Color(TierColor.r, TierColor.g, TierColor.b, beamAlpha);
            }
        }

        /// <summary>Legendary: a single bright pixel rises up the column now and then.</summary>
        private void TickGlint(float deltaTime)
        {
            if (_glintAge < 0f)
            {
                _nextGlint -= deltaTime;
                if (_nextGlint > 0f) return;
                _glintAge = 0f;
            }

            _glintAge += deltaTime;
            const float life = 0.8f;
            var t = _glintAge / life;
            if (t >= 1f) { _glint.enabled = false; _glintAge = -1f; _nextGlint = 1.6f; return; }
            var y = FeetOffset + t * MaxBeamPixels * Px;
            _glint.transform.localPosition = new Vector3(0f, Mathf.Round(y / Px) * Px, 0f);
            var c = Color.Lerp(TierColor, Color.white, 0.5f);
            _glint.color = new Color(c.r, c.g, c.b, 0.85f * Mathf.Sin(t * Mathf.PI));
            _glint.enabled = true;
        }

        /// <summary>A stepped pixel ellipse (core, mid, rim) of <paramref name="w"/> × <paramref name="h"/> pixels, white; tinted by the renderer.</summary>
        private static Sprite Ellipse(int w, int h)
        {
            if (Ellipses.TryGetValue((w, h), out var cached) && cached != null) return cached;
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = $"LootGlow_{w}x{h}" };
            var cx = (w - 1) / 2f;
            var cy = (h - 1) / 2f;
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var dx = (x - cx) / (w / 2f);
                var dy = (y - cy) / (h / 2f);
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                var a = d <= 0.45f ? 1f : d <= 0.75f ? 0.6f : d <= 1f ? 0.28f : 0f;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }

            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), SortingConvention.PixelsPerUnit);
            Ellipses[(w, h)] = sprite;
            return sprite;
        }

        /// <summary>A one-pixel-wide column fading from full at the foot to nothing at the top.</summary>
        private static Sprite Column(int height)
        {
            if (Columns.TryGetValue(height, out var cached) && cached != null) return cached;
            var texture = new Texture2D(1, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = $"LootShimmer_{height}" };
            for (var y = 0; y < height; y++) texture.SetPixel(0, y, new Color(1f, 1f, 1f, Mathf.Round((1f - y / (float)height) * 4f) / 4f));
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, height), new Vector2(0.5f, 0.5f), SortingConvention.PixelsPerUnit);
            Columns[height] = sprite;
            return sprite;
        }
    }
}
