using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// A brief, biome-flavoured flourish when the local player first walks into a room (art/100 biome identity): a few
    /// pixel-sized particles, lamps and lines that play for about two seconds and remove themselves. Each biome has
    /// <see cref="VariantCount"/> variants; <see cref="AssignVariants"/> picks one per room from the depth's seed so
    /// neighbouring rooms rarely repeat.
    ///   Ruined Metro — 0 signal fault (blinking signal lamps, electrical spits, a cold flicker); 1 passing train (a
    ///     headlight streak behind a wall, dust shaken from the ceiling); 2 emergency alarm (red beacons, a chase of
    ///     floor strip lights, a red pulse).
    ///   Rustworks — 0 steam vents (floor steam, a spark shower); 1 furnace flare (a warm pulse, embers rising along a
    ///     wall); 2 pressure release (a steam jet from a wall pipe, clanking sparks, a row of gauge lamps).
    ///   Overgrown Labs — 0 creeping veins (bioluminescent growth crawling in from the walls); 1 specimen stir (bubbles
    ///     rising in two tanks, a violet specimen lamp stuttering); 2 quarantine scan (a pale scan line sweeping the room,
    ///     a violet warning lamp).
    /// Presentation only: no collider, no light source, nothing a system reads; overlays stay under 8 % opacity so what
    /// is visible in the room never changes. Placement and variant are seeded by the room, so every peer matches.
    /// </summary>
    public sealed class RoomEntryAtmosphere : MonoBehaviour
    {
        /// <summary>The longest any flourish runs (seconds); everything is gone after it.</summary>
        public const float MaxSeconds = 2.2f;
        /// <summary>Overlay opacity ceiling: a flicker is a hint, never a visibility change.</summary>
        public const float MaxOverlayAlpha = 0.08f;
        private const float Px = 1f / SortingConvention.PixelsPerUnit;

        private static Sprite _pixel;

        private sealed class Particle
        {
            public SpriteRenderer Renderer;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Gravity;
            public float Sway;
            public float Delay;
            public float Life;
            public float Age;
            public Color Color;
            public int StartPixels;
            public int EndPixels;
            /// <summary>Opacity over normalised life (0..1).</summary>
            public System.Func<float, float> Alpha;
        }

        private readonly List<Particle> _particles = new();
        private float _elapsed;

        /// <summary>Distinct variants per biome.</summary>
        public const int VariantCount = 3;

        public Biome Biome { get; private set; }
        public int Variant { get; private set; }
        /// <summary>Particles, lamps and overlays this flourish was built from.</summary>
        public int PieceCount => _particles.Count;
        /// <summary>Pieces currently drawn (started and not yet finished).</summary>
        public int VisibleCount { get; private set; }
        /// <summary>The strongest overlay opacity this flourish uses.</summary>
        public float PeakOverlayAlpha { get; private set; }
        public bool IsFinished => _elapsed >= MaxSeconds;

        /// <summary>Plays one of the biome's variants inside <paramref name="interior"/> (world rect), placed by <paramref name="seed"/>.</summary>
        public static RoomEntryAtmosphere Play(Biome biome, int variant, Rect interior, int seed, Transform parent = null)
        {
            var go = new GameObject($"RoomEntryAtmosphere_{biome}_{variant}");
            if (parent != null) go.transform.SetParent(parent, false);
            var atmosphere = go.AddComponent<RoomEntryAtmosphere>();
            atmosphere.Build(biome, ((variant % VariantCount) + VariantCount) % VariantCount, interior, new System.Random(seed));
            return atmosphere;
        }

        /// <summary>
        /// One variant per room of a depth, deterministic from <paramref name="depthSeed"/> and the room graph (every peer
        /// builds the same graph, so every peer gets the same answer; no random draw is spent). Rooms are visited in id
        /// order; each starts from its seeded preference and takes the first variant no already-assigned neighbour uses,
        /// so adjacent rooms differ whenever a room has fewer than <see cref="VariantCount"/> assigned neighbours.
        /// </summary>
        public static Dictionary<int, int> AssignVariants(IEnumerable<(int id, IReadOnlyList<int> neighbours)> rooms, int depthSeed)
        {
            var assigned = new Dictionary<int, int>();
            var ordered = new List<(int id, IReadOnlyList<int> neighbours)>(rooms);
            ordered.Sort((x, y) => x.id.CompareTo(y.id));
            foreach (var (id, neighbours) in ordered)
            {
                var preferred = (int)(Mix(depthSeed, id) % VariantCount);
                var chosen = preferred;
                for (var k = 0; k < VariantCount; k++)
                {
                    var candidate = (preferred + k) % VariantCount;
                    var taken = false;
                    if (neighbours != null)
                        foreach (var n in neighbours)
                            if (assigned.TryGetValue(n, out var v) && v == candidate) { taken = true; break; }
                    if (!taken) { chosen = candidate; break; }
                }

                assigned[id] = chosen;
            }

            return assigned;
        }

        private static uint Mix(int seed, int id)
        {
            unchecked
            {
                var h = (uint)seed * 0x9E3779B1u ^ (uint)id * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                return h;
            }
        }

        private void Build(Biome biome, int variant, Rect room, System.Random rng)
        {
            Biome = biome;
            Variant = variant;
            var inner = new Rect(room.xMin + 1f, room.yMin + 1f, Mathf.Max(0.5f, room.width - 2f), Mathf.Max(0.5f, room.height - 2f));
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            Vector2 Anywhere() => new(R(inner.xMin, inner.xMax), R(inner.yMin, inner.yMax));
            // Lamps and pipes sit on the side walls: the far wall is under the HUD's room-title band.
            // ...and below the top two tiles, where the minimap and biome chip sit.
            Vector2 SideWall(int side) => new(side < 0 ? room.xMin - 0.3f : room.xMax + 0.3f, R(inner.yMin + 0.5f, Mathf.Max(inner.yMin + 0.6f, inner.yMax - 2f)));
            int Side() => rng.Next(2) == 0 ? -1 : 1;

            switch (biome)
            {
                case Biome.RuinedMetro when variant == 0: // signal fault
                {
                    for (var i = 0; i < 2; i++)
                    {
                        var colour = i == 0 ? new Color(0.95f, 0.30f, 0.25f) : new Color(0.95f, 0.70f, 0.30f);
                        AddLamp(SideWall(i == 0 ? -1 : 1), colour, i * 0.18f, 1.3f, t => Mathf.Repeat(t * 3f, 1f) < 0.5f ? 0.95f : 0.1f);
                    }

                    for (var burst = 0; burst < 2; burst++)
                    {
                        var side = burst == 0 ? -1 : 1;
                        var from = SideWall(side);
                        for (var i = 0; i < (burst == 0 ? 9 : 5); i++)
                            Add(from, 2, new Color(0.75f, 0.92f, 1f), new Vector2(-side * R(0.8f, 2.6f), R(-0.4f, 1.8f)), 7f, 0.25f + burst * 0.7f + R(0f, 0.06f), R(0.25f, 0.45f), t => 1f - t);
                    }

                    AddOverlay(room, new Color(0.80f, 0.90f, 1f), 0.10f, 0.55f, t => Flicker(t, 0.06f, 0.075f, 0.045f));
                    break;
                }
                case Biome.RuinedMetro when variant == 1: // passing train
                {
                    // A headlight streak racing along one side wall (the tunnel behind it), then dust shaken loose.
                    var side = Side();
                    var x = side < 0 ? room.xMin - 0.3f : room.xMax + 0.3f;
                    var dir = rng.Next(2) == 0 ? 1f : -1f;
                    var y0 = dir > 0 ? room.yMin : room.yMax;
                    for (var i = 0; i < 6; i++)
                    {
                        var strength = (1f - i / 6f) * 0.85f; // the lead is brightest, the tail fades
                        AddBar(new Vector2(x, y0 + dir * (0.9f - i * 0.18f)), 3, 5, new Color(1f, 0.95f, 0.75f), new Vector2(0f, dir * 14f), 0.15f, Mathf.Max(0.05f, (room.height - 0.9f) / 14f), t => strength);
                    }
                    for (var i = 0; i < 16; i++)
                        Add(new Vector2(R(inner.xMin, inner.xMax), room.yMax - R(0.2f, 1.2f)), i % 3 == 0 ? 2 : 1, new Color(0.72f, 0.70f, 0.66f), new Vector2(R(-0.1f, 0.1f), R(-0.4f, -0.8f)), 0.6f, 0.35f + R(0f, 0.5f), R(0.9f, 1.3f), t => 0.7f * Mathf.Sin(t * Mathf.PI), sway: 0.15f);
                    AddOverlay(room, new Color(1f, 0.95f, 0.8f), 0.15f, 0.35f, t => 0.05f * Mathf.Sin(t * Mathf.PI));
                    break;
                }
                case Biome.RuinedMetro: // emergency alarm
                {
                    var red = new Color(1f, 0.22f, 0.18f);
                    AddLamp(SideWall(-1), red, 0f, 1.6f, t => 0.1f + 0.85f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)));
                    AddLamp(SideWall(1), red, 0f, 1.6f, t => 0.1f + 0.85f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)));
                    // Floor strip lights along the bottom wall chase toward an exit.
                    var y = inner.yMin; // a tile in from the bottom wall: clear of the weapon HUD
                    var count = Mathf.Clamp(Mathf.FloorToInt(inner.width / 0.8f), 6, 14);
                    var reverse = rng.Next(2) == 0;
                    for (var i = 0; i < count; i++)
                    {
                        var k = reverse ? count - 1 - i : i;
                        AddBar(new Vector2(inner.xMin + k * (inner.width / (count - 1)), y), 5, 2, new Color(1f, 0.7f, 0.25f), Vector2.zero, 0.1f + i * 0.07f, 0.5f, t => 0.95f * (1f - t));
                    }

                    AddOverlay(room, red, 0.05f, 1.2f, t => 0.06f * Mathf.Max(0f, Mathf.Sin(t * Mathf.PI * 3f)));
                    break;
                }
                case Biome.Rustworks when variant == 0: // steam vents
                {
                    for (var v = 0; v < 2; v++)
                    {
                        var vent = Anywhere();
                        for (var i = 0; i < 9; i++)
                            Add(vent + new Vector2(R(-0.1f, 0.1f), 0f), 3, new Color(0.88f, 0.88f, 0.86f), new Vector2(R(-0.15f, 0.15f), R(0.8f, 1.1f)), 0f, v * 0.3f + i * 0.11f, 0.95f, t => 0.5f * Mathf.Sin(t * Mathf.PI), sway: 0.25f, endPixels: 7);
                    }

                    var side = Side();
                    var from = SideWall(side);
                    for (var i = 0; i < 11; i++)
                        Add(from, 2, i % 3 == 0 ? new Color(1f, 0.85f, 0.45f) : new Color(1f, 0.55f, 0.2f), new Vector2(-side * R(0.4f, 1.8f), R(0f, 1.4f)), 9f, 0.15f + i * 0.03f, R(0.35f, 0.55f), t => 1f - t * t);
                    break;
                }
                case Biome.Rustworks when variant == 1: // furnace flare
                {
                    // A furnace behind one wall breathes: a warm pulse, embers lifting off along that wall.
                    var side = Side();
                    var wallX = side < 0 ? inner.xMin : inner.xMax;
                    for (var i = 0; i < 20; i++)
                        Add(new Vector2(wallX - side * R(0f, 1.2f), R(inner.yMin, inner.yMax)), i % 4 == 0 ? 2 : 1, i % 3 == 0 ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.42f, 0.15f), new Vector2(-side * R(0.05f, 0.3f), R(0.5f, 1.0f)), 0f, R(0f, 0.7f), R(0.8f, 1.3f), t => 0.95f * (1f - t) * Mathf.Min(1f, t * 6f), sway: 0.3f);
                    AddLamp(SideWall(side), new Color(1f, 0.5f, 0.15f), 0f, 1.5f, t => 0.3f + 0.6f * Mathf.Sin(t * Mathf.PI));
                    AddOverlay(room, new Color(1f, 0.55f, 0.2f), 0.05f, 1.3f, t => 0.05f * Mathf.Sin(t * Mathf.PI));
                    break;
                }
                case Biome.Rustworks: // pressure release
                {
                    // A pipe in a side wall vents a jet of steam across the floor with a clank of sparks.
                    var side = Side();
                    var pipe = SideWall(side);
                    for (var i = 0; i < 12; i++)
                        Add(pipe + new Vector2(0f, R(-0.08f, 0.08f)), 2, new Color(0.9f, 0.9f, 0.88f), new Vector2(-side * R(2.2f, 3.2f), R(-0.2f, 0.25f)), 0f, 0.1f + i * 0.06f, 0.75f, t => 0.55f * (1f - t), endPixels: 6);
                    for (var i = 0; i < 6; i++)
                        Add(pipe, 1, new Color(1f, 0.8f, 0.4f), new Vector2(-side * R(0.3f, 1.2f), R(0.5f, 1.5f)), 8f, 0.08f, R(0.25f, 0.4f), t => 1f - t);
                    // A row of gauge lamps above the pipe blinks down from green to red.
                    for (var i = 0; i < 3; i++)
                    {
                        var colour = i == 0 ? new Color(0.5f, 1f, 0.45f) : i == 1 ? new Color(1f, 0.8f, 0.3f) : new Color(1f, 0.3f, 0.2f);
                        AddLamp(pipe + new Vector2(0f, 0.45f + i * 0.3f), colour, 0.1f + i * 0.25f, 1.1f - i * 0.25f, t => Mathf.Repeat(t * 2f, 1f) < 0.6f ? 0.9f : 0.2f);
                    }

                    break;
                }
                case Biome.OvergrownLabs when variant == 0: // creeping veins
                {
                    // Bioluminescent growth crawls in from the walls, lighting up as it goes, with glowing knots, then
                    // dies back from the root.
                    var glow = new Color(0.78f, 0.5f, 1f);
                    var tip = new Color(0.5f, 1f, 0.92f);
                    for (var v = 0; v < 4; v++)
                    {
                        var side = v % 2 == 0 ? -1 : 1;
                        var at = new Vector2(side < 0 ? room.xMin : room.xMax, R(inner.yMin, Mathf.Max(inner.yMin + 0.1f, inner.yMax - 2f)));
                        var drift = R(-0.8f, 0.8f);
                        var steps = 26 + rng.Next(12);
                        for (var i = 0; i < steps; i++)
                        {
                            at += new Vector2(-side * 2f * Px, Mathf.Round(rng.Next(3) - 1 + drift) * 2f * Px);
                            var last = i == steps - 1;
                            var start = 0.05f + v * 0.15f + i * 0.03f;
                            var life = 1.95f - start;
                            System.Func<float, float> vein = t => (t < 0.1f ? t / 0.1f : 1f) * (t > 0.75f ? (1f - t) / 0.25f : 1f) * (0.75f + 0.2f * Mathf.Sin(t * 18f));
                            Add(at, last ? 3 : 2, last ? tip : glow, Vector2.zero, 0f, start, life, vein);
                            if (i % 7 == 3) Add(at, 6, glow, Vector2.zero, 0f, start, life, t => vein(t) * 0.22f); // a glowing knot
                        }
                    }

                    AddOverlay(room, glow, 0.3f, 1.4f, t => 0.035f * Mathf.Sin(t * Mathf.PI));
                    break;
                }
                case Biome.OvergrownLabs when variant == 1: // specimen stir
                {
                    // Something moves in the tanks: three tank glows swell while bubbles stream up through them, and a
                    // violet specimen lamp stutters.
                    for (var c = 0; c < 3; c++)
                    {
                        var column = Anywhere();
                        var tank = new Color(0.45f, 1f, 0.9f);
                        AddBar(column + new Vector2(0f, 0.2f), 10, 16, tank, Vector2.zero, c * 0.2f, 1.5f, t => 0.16f * Mathf.Sin(t * Mathf.PI));
                        for (var i = 0; i < 12; i++)
                            Add(column + new Vector2(R(-0.1f, 0.1f), -0.1f), i % 3 == 0 ? 3 : 2, new Color(0.7f, 1f, 0.97f), new Vector2(0f, R(0.7f, 1.1f)), 0f, c * 0.2f + i * 0.1f, R(0.55f, 0.8f), t => 0.9f * Mathf.Sin(t * Mathf.PI), sway: 0.1f);
                    }

                    var stutter = new[] { 0.9f, 0.1f, 0.8f, 0.8f, 0.05f, 0.95f, 0.2f, 0.7f, 0.1f, 0.85f };
                    float Stutter(float t) => stutter[Mathf.Clamp(Mathf.FloorToInt(t * stutter.Length), 0, stutter.Length - 1)];
                    AddLamp(SideWall(Side()), new Color(0.8f, 0.35f, 1f), 0.05f, 1.6f, Stutter);
                    AddOverlay(room, new Color(0.55f, 0.3f, 0.8f), 0.05f, 1.6f, t => 0.04f * Stutter(t));
                    break;
                }
                default: // OvergrownLabs quarantine scan
                {
                    // A pale scan line sweeps the room once, a violet warning lamp blinks while it runs.
                    var up = rng.Next(2) == 0;
                    var widthPx = Mathf.RoundToInt(room.width / Px);
                    var from = new Vector2(room.center.x, up ? room.yMin : room.yMax);
                    var speed = room.height / 1.2f;
                    AddBar(from, widthPx, 1, new Color(0.55f, 1f, 0.9f), new Vector2(0f, up ? speed : -speed), 0.15f, 1.2f, t => 0.65f * Mathf.Sin(t * Mathf.PI));
                    AddBar(from - new Vector2(0f, (up ? 1 : -1) * 3f * Px), widthPx, 4, new Color(0.55f, 1f, 0.9f), new Vector2(0f, up ? speed : -speed), 0.15f, 1.2f, t => 0.18f * Mathf.Sin(t * Mathf.PI));
                    // Where the line passes, a few "detections" flare and fade: small brackets marking something in the room.
                    for (var i = 0; i < 6; i++)
                    {
                        var mark = Anywhere();
                        var reached = 0.15f + Mathf.Abs(mark.y - from.y) / speed;
                        var colour = i % 3 == 0 ? new Color(0.8f, 0.35f, 1f) : new Color(0.55f, 1f, 0.9f);
                        AddBar(mark + new Vector2(-4f * Px, 0f), 2, 6, colour, Vector2.zero, reached, 0.8f, t => 0.9f * (1f - t));
                        AddBar(mark + new Vector2(4f * Px, 0f), 2, 6, colour, Vector2.zero, reached, 0.8f, t => 0.9f * (1f - t));
                    }

                    AddLamp(SideWall(Side()), new Color(0.8f, 0.35f, 1f), 0.1f, 1.4f, t => Mathf.Repeat(t * 4f, 1f) < 0.45f ? 0.9f : 0.1f);
                    break;
                }
            }

            Advance(0f);
        }

        /// <summary>A stepped flicker: on/off/on/off/on at the given strengths.</summary>
        private static float Flicker(float t, float a, float b, float c) =>
            t < 0.18f ? a : t < 0.34f ? 0f : t < 0.55f ? b : t < 0.72f ? 0f : c * (1f - (t - 0.72f) / 0.28f);

        private void Add(Vector2 at, int pixels, Color colour, Vector2 velocity, float gravity, float delay, float life, System.Func<float, float> alpha, float sway = 0f, int endPixels = 0)
        {
            var renderer = NewRenderer("Piece", SortingRole.WorldVfx);
            renderer.transform.localScale = new Vector3(pixels, pixels, 1f);
            _particles.Add(new Particle { Renderer = renderer, Position = at, Velocity = velocity, Gravity = gravity, Delay = delay, Life = life, Color = colour, Alpha = alpha, Sway = sway, StartPixels = pixels, EndPixels = endPixels > 0 ? endPixels : pixels });
        }

        /// <summary>A rectangular piece <paramref name="widthPx"/> × <paramref name="heightPx"/> pixels (streaks, strips, scan lines).</summary>
        private void AddBar(Vector2 at, int widthPx, int heightPx, Color colour, Vector2 velocity, float delay, float life, System.Func<float, float> alpha)
        {
            var renderer = NewRenderer("Bar", SortingRole.WorldVfx);
            renderer.transform.localScale = new Vector3(widthPx, heightPx, 1f);
            _particles.Add(new Particle { Renderer = renderer, Position = at, Velocity = velocity, Delay = delay, Life = life, Color = colour, Alpha = alpha, StartPixels = 0, EndPixels = 0 });
        }

        /// <summary>A small wall lamp: a 3-pixel core over a soft 7-pixel halo, both following the same blink.</summary>
        private void AddLamp(Vector2 at, Color colour, float delay, float life, System.Func<float, float> alpha)
        {
            Add(at, 7, colour, Vector2.zero, 0f, delay, life, t => alpha(t) * 0.22f);
            Add(at, 3, colour, Vector2.zero, 0f, delay, life, alpha);
        }

        private void AddOverlay(Rect room, Color colour, float delay, float life, System.Func<float, float> alpha)
        {
            var renderer = NewRenderer("Overlay", SortingRole.WorldVfx);
            renderer.transform.localScale = new Vector3(room.width * SortingConvention.PixelsPerUnit, room.height * SortingConvention.PixelsPerUnit, 1f);
            System.Func<float, float> capped = t => Mathf.Min(MaxOverlayAlpha, alpha(t));
            for (var t = 0f; t <= 1f; t += 0.02f) PeakOverlayAlpha = Mathf.Max(PeakOverlayAlpha, capped(t));
            _particles.Add(new Particle { Renderer = renderer, Position = room.center, Delay = delay, Life = life, Color = colour, Alpha = capped });
        }

        private SpriteRenderer NewRenderer(string name, SortingRole role)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = Pixel;
            SpriteSorting.Apply(renderer, role);
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>One opaque white pixel at the game's pixels-per-unit: scale N draws an N-pixel square on the grid.</summary>
        internal static Sprite Pixel
        {
            get
            {
                if (_pixel != null) return _pixel;
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "AtmospherePixel" };
                texture.SetPixel(0, 0, Color.white);
                texture.Apply();
                _pixel = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), SortingConvention.PixelsPerUnit);
                return _pixel;
            }
        }

        private void Update()
        {
            Advance(Time.deltaTime);
            if (IsFinished) Destroy(gameObject);
        }

        /// <summary>Steps every piece by <paramref name="deltaTime"/> (public for deterministic proof).</summary>
        public void Advance(float deltaTime)
        {
            _elapsed += deltaTime;
            var visible = 0;
            foreach (var p in _particles)
            {
                if (_elapsed < p.Delay) { p.Renderer.enabled = false; continue; }
                var step = Mathf.Min(deltaTime, _elapsed - p.Delay);
                p.Age += step;
                var t = p.Age / p.Life;
                if (t >= 1f || _elapsed >= MaxSeconds) { p.Renderer.enabled = false; continue; }
                p.Velocity.y -= p.Gravity * step;
                p.Position += p.Velocity * step;
                var sway = p.Sway > 0f ? Mathf.Sin(p.Age * 5f + p.Delay * 11f) * p.Sway * 0.25f : 0f;
                var at = p.Position + new Vector2(sway, 0f);
                // Snapped to the pixel grid, like everything else in the world.
                if (p.EndPixels != p.StartPixels)
                {
                    var size = Mathf.Round(Mathf.Lerp(p.StartPixels, p.EndPixels, t));
                    p.Renderer.transform.localScale = new Vector3(size, size, 1f);
                }

                p.Renderer.transform.position = new Vector3(Mathf.Round(at.x / Px) * Px, Mathf.Round(at.y / Px) * Px, 0f);
                var alpha = Mathf.Clamp01(p.Alpha(t));
                p.Renderer.color = new Color(p.Color.r, p.Color.g, p.Color.b, alpha);
                p.Renderer.enabled = alpha > 0.001f;
                if (p.Renderer.enabled) visible++;
            }

            VisibleCount = visible;
        }
    }
}
