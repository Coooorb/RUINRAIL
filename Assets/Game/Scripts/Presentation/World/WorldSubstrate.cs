using System;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.World
{
    /// <summary>The substrate art one biome is drawn with: a tileable underlay sprite and the tint it renders at.</summary>
    public readonly struct SubstrateSkin
    {
        public SubstrateSkin(Sprite tile, Color tint)
        {
            Tile = tile;
            Tint = tint;
        }

        public Sprite Tile { get; }
        public Color Tint { get; }
        public bool IsComplete => Tile != null;
    }

    /// <summary>
    /// The dark environmental underlay the dungeon sits in.
    ///
    /// Without it a depth is a handful of lit rooms floating in the camera's solid black clear colour: at the 640x360
    /// reference the viewport is 20 x 11.25 tiles and 40 of the 63 shipping rooms are 16 x 12, so a small room always
    /// leaves four tiles of raw void on screen, and the gaps between placed rooms are void at every size.
    ///
    /// This is presentation only, and deliberately so:
    /// <list type="bullet">
    /// <item>it renders on the existing <see cref="SortingLayers.Ground"/> layer at a large negative order, below the
    /// floor tilemaps (which sit at order 0), so art/102's twelve-layer contract is untouched — no new sorting layer;</item>
    /// <item>it has no collider, no tile occupancy and no room membership, so pathing, room sealing, encounter bounds,
    /// door sockets and the minimap cannot see it;</item>
    /// <item>it is quieter than every biome floor (see <see cref="Palette"/>) and carries only low-frequency texture,
    /// so it reads as the ground the station was cut out of rather than as walkable floor.</item>
    /// </list>
    ///
    /// Coverage is a pure function of the layout bounds: the layout rect grown by <see cref="MarginTiles"/>, which is
    /// wider than the camera's half extent at the layout edge, so the clamped camera can never see past it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldSubstrate : MonoBehaviour
    {
        /// <summary>Below every floor tilemap on the Ground layer (floors sit at <c>BaseOrderOf(Floor)</c> = 0).</summary>
        public const int SortingOrder = -1000;

        /// <summary>
        /// World tiles of substrate beyond the layout rect. The camera clamps to the layout bounds and its half extent
        /// is 10 x 5.625 tiles, so 24 covers the widest possible overhang with room to spare.
        /// </summary>
        public const float MarginTiles = 24f;

        /// <summary>Edge length in pixels of the generated tile (2 x 2 world tiles at 32 PPU).</summary>
        public const int TilePixels = 64;

        /// <summary>Authored substrate art, registered by the composition root. Null (the default) uses <see cref="Generate"/>.</summary>
        public static Func<Biome, SubstrateSkin> SkinResolver { get; set; }

        private static readonly Sprite[] GeneratedTiles = new Sprite[3];

        private SpriteRenderer _renderer;

        public Biome Biome { get; private set; }

        /// <summary>The world rect the substrate covers. Deterministic for a given layout rect.</summary>
        public Rect Coverage { get; private set; }

        public SpriteRenderer Renderer => _renderer;
        public Color Tint => _renderer != null ? _renderer.color : Color.clear;
        /// <summary>True when the underlay is authored art rather than the generated fallback.</summary>
        public bool UsesAuthoredArt { get; private set; }

        /// <summary>The layout rect grown by <see cref="MarginTiles"/> on every side.</summary>
        public static Rect CoverageFor(Rect layoutBounds) => Rect.MinMaxRect(
            layoutBounds.xMin - MarginTiles, layoutBounds.yMin - MarginTiles,
            layoutBounds.xMax + MarginTiles, layoutBounds.yMax + MarginTiles);

        /// <summary>
        /// Builds the underlay for a depth under <paramref name="parent"/> (the dungeon root, so it is destroyed with
        /// the depth). Never adds a collider and never parents anything the room runtime walks.
        /// </summary>
        public static WorldSubstrate Create(Transform parent, Biome biome, Rect layoutBounds)
        {
            var go = new GameObject("WorldSubstrate");
            if (parent != null) go.transform.SetParent(parent, false);
            var substrate = go.AddComponent<WorldSubstrate>();
            substrate.Build(biome, layoutBounds);
            return substrate;
        }

        private void Build(Biome biome, Rect layoutBounds)
        {
            Biome = biome;
            Coverage = CoverageFor(layoutBounds);
            var skin = SkinResolver != null ? SkinResolver(biome) : default;
            UsesAuthoredArt = skin.IsComplete;
            var sprite = UsesAuthoredArt ? skin.Tile : Generate(biome);
            _renderer = gameObject.AddComponent<SpriteRenderer>();
            _renderer.sprite = sprite;
            _renderer.color = UsesAuthoredArt ? skin.Tint : Color.white;
            _renderer.drawMode = SpriteDrawMode.Tiled;
            _renderer.tileMode = SpriteTileMode.Continuous;
            _renderer.size = Coverage.size;
            _renderer.sortingLayerName = SortingLayers.Ground;
            _renderer.sortingOrder = SortingOrder;
            transform.position = new Vector3(Coverage.center.x, Coverage.center.y, 0f);
        }

        // ---------------- generated underlay ----------------

        /// <summary>
        /// The biome's substrate ramp: base, the darker and lighter notes of its texture, and the camera clear colour
        /// used beyond the substrate. Every value is deliberately darker than that biome's floor base
        /// (Metro #4E5153, Rustworks #393E41, Labs #7C837C) so the underlay can never be mistaken for floor.
        /// </summary>
        public static (Color Base, Color Dark, Color Light) Palette(Biome biome) => biome switch
        {
            // Deep tunnel bed: cold concrete and ballast under the platform.
            Biome.RuinedMetro => (Hex("#1D2124"), Hex("#161A1D"), Hex("#242A2D")),
            // Industrial pit: soot and dead machinery below the works.
            Biome.Rustworks => (Hex("#1F1A17"), Hex("#171310"), Hex("#27201B")),
            // Service underlayer: structural deck with organic shadow.
            _ => (Hex("#141C1B"), Hex("#0F1615"), Hex("#1B2422"))
        };

        /// <summary>The camera clear colour for a biome: the substrate's own darkest note, never raw black.</summary>
        public static Color ClearColorFor(Biome biome)
        {
            var (_, dark, _) = Palette(biome);
            return new Color(dark.r, dark.g, dark.b, 1f);
        }

        /// <summary>
        /// The generated tileable underlay for a biome, cached per biome. Deterministic: the seed is the biome, the
        /// texture wraps, and the texture never carries high-frequency noise — the speckle is sparse and the banding
        /// is a single low-contrast step, so a large expanse stays quiet at 640x360.
        /// </summary>
        public static Sprite Generate(Biome biome)
        {
            var index = (int)biome;
            if (index < 0 || index >= GeneratedTiles.Length) index = 0;
            if (GeneratedTiles[index] != null) return GeneratedTiles[index];

            var (baseColor, dark, light) = Palette(biome);
            var texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, false)
            {
                name = "WorldSubstrate_" + biome,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.HideAndDontSave
            };

            var rng = new System.Random(unchecked(0x5D8B3 + index * 7919));
            var pixels = new Color32[TilePixels * TilePixels];
            for (var y = 0; y < TilePixels; y++)
            for (var x = 0; x < TilePixels; x++)
            {
                var c = baseColor;
                // One low-contrast structural rhythm per biome, on the 16 px grid so it tiles seamlessly.
                switch (biome)
                {
                    case Biome.RuinedMetro:
                        if (y % 16 == 0) c = dark;                         // sleeper bed banding
                        else if (y % 16 == 8 && x % 32 < 16) c = light;    // ballast run
                        break;
                    case Biome.Rustworks:
                        if (x % 32 == 0 || y % 32 == 0) c = dark;          // plate seams
                        else if ((x / 8 + y / 8) % 5 == 0) c = light;      // soot-worn plate
                        break;
                    default:
                        if (x % 16 == 0 && y % 4 != 0) c = dark;           // deck ribs
                        else if ((x / 16 + y / 16) % 3 == 0) c = light;    // service panel
                        break;
                }

                // Sparse speckle: ~4% of pixels step one value, which reads as grain rather than noise.
                var roll = rng.Next(100);
                if (roll < 2) c = dark;
                else if (roll < 4) c = light;
                pixels[y * TilePixels + x] = c;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            // FullRect: SpriteRenderer.Tiled requires it, and a tight mesh would silently fall back to Simple.
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, TilePixels, TilePixels), new Vector2(0.5f, 0.5f),
                SortingConvention.PixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            GeneratedTiles[index] = sprite;
            return sprite;
        }

        private static Color Hex(string hex)
        {
            var h = hex.TrimStart('#');
            var r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
            var g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
            var b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            var a = h.Length >= 8 ? Convert.ToInt32(h.Substring(6, 2), 16) / 255f : 1f;
            return new Color(r, g, b, a);
        }

        /// <summary>Relative luminance (WCAG), used by the validator to prove the substrate is quieter than the floor.</summary>
        public static float Luminance(Color c)
        {
            static float Channel(float v) => v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
        }
    }
}
