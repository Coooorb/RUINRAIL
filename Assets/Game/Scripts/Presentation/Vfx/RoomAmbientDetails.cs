using System;
using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    /// <summary>
    /// Small, localized life in the room the local player is standing in, after the entry flourish
    /// (<see cref="RoomEntryAtmosphere"/>) is over: <see cref="EmittersPerRoom"/> details chosen from the biome's set and
    /// placed by the room seed, each doing something small now and then.
    ///   Ruined Metro — electrical sparks off a wall, a weak signal lamp, a flickering cable run, dust sifting down.
    ///   Rustworks — a steam release from the floor, embers lifting off a wall, a machinery pulse lamp, a twitching gauge.
    ///   Overgrown Labs — tank bubbles over a faint tank glow, a growth vein pulsing violet, drips falling, drifting motes,
    ///     a stuttering specimen lamp.
    ///   Cryo Vaults — condensation puffs off the floor, frost crystals glinting as they settle, a temperature/status
    ///     readout on a wall, a cold condensation drip, and a small machine indicator shuttling along a wall.
    /// Every piece is a few pixels on the ground-details layer (above floor decals, below hazard footprints and every
    /// character, loot, telegraph and effect), with no collider, no light and no overlay; while the room's fight runs the
    /// details slow down. Only the current room's details exist: the owner creates them on entry and destroys them on
    /// leaving or on depth teardown, and a hard cap keeps the live pieces bounded.
    /// </summary>
    public sealed class RoomAmbientDetails : MonoBehaviour
    {
        public const int EmittersPerRoom = 3;
        /// <summary>Upper bound on pieces alive at once (all emitters together).</summary>
        public const int MaxLivePieces = 40;
        /// <summary>Order inside the ground-details layer: above decals (0), below hazard footprints (10).</summary>
        public const int GroundOrder = 5;
        private const float Px = 1f / SortingConvention.PixelsPerUnit;

        public enum Kind
        {
            MetroSparks, MetroSignalLamp, MetroCableFlicker, MetroDust,
            RustSteam, RustEmbers, RustMachineryPulse, RustGauge,
            LabsTankBubbles, LabsGrowthPulse, LabsDrip, LabsMotes, LabsSpecimenFlicker,
            CryoCondensation, CryoFrostGlint, CryoStatusReadout, CryoDrip, CryoMachineShuttle
        }

        private static readonly Kind[] Metro = { Kind.MetroSparks, Kind.MetroSignalLamp, Kind.MetroCableFlicker, Kind.MetroDust };
        private static readonly Kind[] Rust = { Kind.RustSteam, Kind.RustEmbers, Kind.RustMachineryPulse, Kind.RustGauge };
        private static readonly Kind[] Labs = { Kind.LabsTankBubbles, Kind.LabsGrowthPulse, Kind.LabsDrip, Kind.LabsMotes, Kind.LabsSpecimenFlicker };
        private static readonly Kind[] Cryo = { Kind.CryoCondensation, Kind.CryoFrostGlint, Kind.CryoStatusReadout, Kind.CryoDrip, Kind.CryoMachineShuttle };

        private sealed class Emitter
        {
            public Kind Kind;
            public Vector2 At;
            public int Side;
            public float Next;
            public System.Random Rng;
        }

        private sealed class Piece
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
            public Func<float, float> Alpha;
            public bool Live;
        }

        private readonly List<Emitter> _emitters = new();
        private readonly List<Piece> _pieces = new();
        private Func<bool> _busy;
        private Rect _room;

        public Biome Biome { get; private set; }
        public IReadOnlyList<Kind> Kinds => _emitters.ConvertAll(e => e.Kind);
        public int LivePieces { get; private set; }
        public int PiecesSpawned { get; private set; }
        public int PeakLivePieces { get; private set; }

        /// <summary>The kinds a room with this seed gets (same answer on every peer).</summary>
        public static Kind[] KindsFor(Biome biome, int seed)
        {
            var pool = new List<Kind>(biome switch { Biome.RuinedMetro => Metro, Biome.Rustworks => Rust, Biome.CryoVaults => Cryo, _ => Labs });
            var rng = new System.Random(seed ^ 0x5A17);
            var chosen = new Kind[Math.Min(EmittersPerRoom, pool.Count)];
            for (var i = 0; i < chosen.Length; i++)
            {
                var pick = rng.Next(pool.Count);
                chosen[i] = pool[pick];
                pool.RemoveAt(pick);
            }

            return chosen;
        }

        /// <summary>Starts the room's details inside <paramref name="interior"/>; <paramref name="busy"/> (optional) slows them while a fight runs.</summary>
        public static RoomAmbientDetails Create(Biome biome, Rect interior, int seed, Transform parent = null, Func<bool> busy = null)
        {
            var go = new GameObject("RoomAmbientDetails_" + biome);
            if (parent != null) go.transform.SetParent(parent, false);
            var details = go.AddComponent<RoomAmbientDetails>();
            details.Build(biome, interior, seed, busy);
            return details;
        }

        private void Build(Biome biome, Rect room, int seed, Func<bool> busy)
        {
            Biome = biome;
            _room = room;
            _busy = busy;
            var rng = new System.Random(seed ^ 0x2B3D);
            var inner = new Rect(room.xMin + 1f, room.yMin + 1f, Mathf.Max(0.5f, room.width - 2f), Mathf.Max(0.5f, room.height - 2f));
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            // Wall details sit on the floor tile against a side wall, clear of the top HUD band (minimap, room title).
            var topClear = Mathf.Max(inner.yMin + 0.6f, inner.yMax - 2f);
            var usedSides = 0;
            foreach (var kind in KindsFor(biome, seed))
            {
                var side = (usedSides++ % 2 == 0) == (rng.Next(2) == 0) ? -1 : 1;
                var wall = new Vector2(side < 0 ? room.xMin + 0.3f : room.xMax - 0.3f, R(inner.yMin + 0.5f, topClear));
                var floor = new Vector2(side < 0 ? R(inner.xMin, inner.center.x - 1f) : R(inner.center.x + 1f, inner.xMax), R(inner.yMin, topClear));
                var onWall = kind is Kind.MetroSparks or Kind.MetroSignalLamp or Kind.MetroCableFlicker or Kind.RustEmbers
                    or Kind.RustMachineryPulse or Kind.RustGauge or Kind.LabsGrowthPulse or Kind.LabsSpecimenFlicker or Kind.LabsDrip
                    or Kind.CryoStatusReadout or Kind.CryoDrip or Kind.CryoMachineShuttle;
                _emitters.Add(new Emitter { Kind = kind, At = onWall ? wall : floor, Side = side, Rng = new System.Random(rng.Next()), Next = R(0.3f, 2.5f) });
            }
        }

        private void Update() => Advance(Time.deltaTime);

        /// <summary>Steps the emitters and pieces (public for deterministic proof).</summary>
        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            // While the room's fight runs, the details slow to a third: nothing competes with enemies and telegraphs.
            var pace = _busy != null && _busy() ? 1f / 3f : 1f;
            foreach (var e in _emitters)
            {
                e.Next -= deltaTime * pace;
                if (e.Next > 0f) continue;
                e.Next = Fire(e);
            }

            var live = 0;
            foreach (var p in _pieces)
            {
                if (!p.Live) continue;
                if (p.Delay > 0f) { p.Delay -= deltaTime; p.Renderer.enabled = false; live++; continue; }
                p.Age += deltaTime;
                var t = p.Age / p.Life;
                if (t >= 1f) { p.Live = false; p.Renderer.enabled = false; continue; }
                p.Velocity.y -= p.Gravity * deltaTime;
                p.Position += p.Velocity * deltaTime;
                var at = p.Position + new Vector2(p.Sway > 0f ? Mathf.Sin(p.Age * 4f) * p.Sway * 0.25f : 0f, 0f);
                p.Renderer.transform.position = new Vector3(Mathf.Round(at.x / Px) * Px, Mathf.Round(at.y / Px) * Px, 0f);
                var alpha = Mathf.Clamp01(p.Alpha(t));
                p.Renderer.color = new Color(p.Color.r, p.Color.g, p.Color.b, alpha);
                p.Renderer.enabled = alpha > 0.001f;
                live++;
            }

            LivePieces = live;
            PeakLivePieces = Mathf.Max(PeakLivePieces, live);
        }

        /// <summary>One small action of the emitter; returns the seconds until its next one.</summary>
        private float Fire(Emitter e)
        {
            var rng = e.Rng;
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var inward = -e.Side;
            switch (e.Kind)
            {
                case Kind.MetroSparks:
                    for (var i = 0; i < 3 + rng.Next(3); i++)
                        Spawn(e.At, 1, 1, new Color(0.75f, 0.92f, 1f), new Vector2(inward * R(0.4f, 1.6f), R(-0.2f, 1.2f)), 6f, 0f, i * 0.02f, R(0.2f, 0.35f), t => 1f - t);
                    return R(4f, 8f);
                case Kind.MetroSignalLamp:
                    // A weak lamp that blinks twice and rests.
                    Spawn(e.At, 2, 2, new Color(0.95f, 0.35f, 0.25f), Vector2.zero, 0f, 0f, 0f, 1.2f, t => Mathf.Repeat(t * 2f, 1f) < 0.4f ? 0.55f : 0.08f);
                    return R(2.2f, 3.5f);
                case Kind.MetroCableFlicker:
                {
                    var stutter = new[] { 0.6f, 0f, 0.45f, 0.5f, 0f, 0.35f };
                    Spawn(e.At, 1, 7, new Color(0.8f, 0.9f, 1f), Vector2.zero, 0f, 0f, 0f, 0.45f, t => stutter[Mathf.Clamp(Mathf.FloorToInt(t * stutter.Length), 0, stutter.Length - 1)]);
                    return R(5f, 9f);
                }
                case Kind.MetroDust:
                    Spawn(e.At + new Vector2(R(-0.8f, 0.8f), R(0.5f, 1.2f)), 2, 2, new Color(0.74f, 0.72f, 0.68f), new Vector2(R(-0.05f, 0.05f), -R(0.18f, 0.3f)), 0f, 0.2f, 0f, R(2f, 2.8f), t => 0.55f * Mathf.Sin(t * Mathf.PI));
                    return R(1.2f, 2.4f);
                case Kind.RustSteam:
                    for (var i = 0; i < 4; i++)
                        Spawn(e.At, 3, 3, new Color(0.88f, 0.88f, 0.86f), new Vector2(R(-0.1f, 0.1f), R(0.6f, 0.85f)), 0f, 0.2f, i * 0.12f, 0.8f, t => 0.35f * Mathf.Sin(t * Mathf.PI));
                    return R(5f, 9f);
                case Kind.RustEmbers:
                    Spawn(e.At + new Vector2(inward * R(0f, 0.6f), R(-0.4f, 0.4f)), 1, 1, rng.Next(3) == 0 ? new Color(1f, 0.8f, 0.35f) : new Color(1f, 0.45f, 0.15f), new Vector2(inward * R(0f, 0.15f), R(0.35f, 0.6f)), 0f, 0.3f, 0f, R(1f, 1.5f), t => 0.85f * (1f - t) * Mathf.Min(1f, t * 6f));
                    return R(0.9f, 2f);
                case Kind.RustMachineryPulse:
                    Spawn(e.At, 2, 2, new Color(1f, 0.6f, 0.2f), Vector2.zero, 0f, 0f, 0f, 1.6f, t => 0.12f + 0.4f * Mathf.Sin(t * Mathf.PI));
                    return R(2.6f, 3.4f);
                case Kind.RustGauge:
                {
                    // Two tiny gauge lamps; now and then the pair twitches from green to amber.
                    var green = new Color(0.5f, 1f, 0.45f);
                    var amber = new Color(1f, 0.75f, 0.3f);
                    Spawn(e.At, 1, 1, green, Vector2.zero, 0f, 0f, 0f, 1.4f, t => t < 0.5f ? 0.6f : 0.15f);
                    Spawn(e.At + new Vector2(0f, 3f * Px), 1, 1, amber, Vector2.zero, 0f, 0f, 0f, 1.4f, t => t < 0.5f ? 0.1f : 0.65f);
                    return R(3.5f, 6.5f);
                }
                case Kind.LabsTankBubbles:
                    // A faint local tank glow now and then, and one bubble at a time rising through it.
                    if (rng.Next(4) == 0) Spawn(e.At + new Vector2(0f, 0.25f), 8, 12, new Color(0.45f, 1f, 0.9f), Vector2.zero, 0f, 0f, 0f, 1.8f, t => 0.07f * Mathf.Sin(t * Mathf.PI));
                    Spawn(e.At + new Vector2(R(-0.08f, 0.08f), -0.05f), 2, 2, new Color(0.7f, 1f, 0.97f), new Vector2(0f, R(0.5f, 0.8f)), 0f, 0.1f, 0f, R(0.7f, 0.9f), t => 0.7f * Mathf.Sin(t * Mathf.PI));
                    return R(0.5f, 1.1f);
                case Kind.LabsGrowthPulse:
                {
                    // A short vein on the wall-side floor glows violet from root to tip, then fades.
                    var at = e.At;
                    var steps = 6 + rng.Next(5);
                    for (var i = 0; i < steps; i++)
                    {
                        at += new Vector2(inward * 2f * Px, (rng.Next(3) - 1) * 2f * Px);
                        var tip = i == steps - 1;
                        Spawn(at, 2, 2, tip ? new Color(0.5f, 1f, 0.92f) : new Color(0.78f, 0.5f, 1f), Vector2.zero, 0f, 0f, i * 0.05f, 1.2f, t => 0.7f * Mathf.Sin(t * Mathf.PI));
                    }

                    return R(4f, 7f);
                }
                case Kind.LabsDrip:
                {
                    // A drip forms, falls a little under a tile and splashes into two specks.
                    var from = e.At + new Vector2(inward * 0.2f, 0.9f);
                    var colour = new Color(0.6f, 1f, 0.9f);
                    Spawn(from, 1, 2, colour, Vector2.zero, 0f, 0f, 0f, 0.35f, t => 0.6f * t);
                    Spawn(from, 1, 2, colour, new Vector2(0f, -0.5f), 5f, 0f, 0.35f, 0.42f, _ => 0.75f);
                    var landing = from + new Vector2(0f, -0.65f);
                    Spawn(landing, 1, 1, colour, new Vector2(-0.6f, 0.5f), 4f, 0f, 0.77f, 0.2f, t => 0.7f * (1f - t));
                    Spawn(landing, 1, 1, colour, new Vector2(0.6f, 0.5f), 4f, 0f, 0.77f, 0.2f, t => 0.7f * (1f - t));
                    return R(3f, 6f);
                }
                case Kind.LabsMotes:
                    Spawn(e.At + new Vector2(R(-1f, 1f), R(-0.8f, 0.8f)), 2, 2, rng.Next(2) == 0 ? new Color(0.78f, 0.55f, 1f) : new Color(0.55f, 1f, 0.9f), new Vector2(R(-0.08f, 0.08f), R(0.08f, 0.18f)), 0f, 0.3f, 0f, R(2.2f, 3f), t => 0.55f * Mathf.Sin(t * Mathf.PI));
                    return R(0.9f, 1.8f);
                case Kind.CryoCondensation:
                    // Cold air meeting the floor: two or three low vapour puffs that swell and thin out as they rise.
                    for (var i = 0; i < 2 + rng.Next(2); i++)
                        Spawn(e.At + new Vector2(R(-0.2f, 0.2f), 0f), 3, 2, new Color(0.86f, 0.94f, 0.96f), new Vector2(R(-0.12f, 0.12f), R(0.25f, 0.4f)), 0f, 0.3f, i * 0.2f, R(1.2f, 1.6f), t => 0.3f * Mathf.Sin(t * Mathf.PI));
                    return R(3f, 5.5f);
                case Kind.CryoFrostGlint:
                    // A frost crystal drifts down and glints as it catches the light.
                    Spawn(e.At + new Vector2(R(-0.9f, 0.9f), R(0.4f, 1.1f)), 1, 1, Color.white, new Vector2(R(-0.05f, 0.05f), -R(0.12f, 0.22f)), 0f, 0.2f, 0f, R(1.8f, 2.4f), t => Mathf.Repeat(t * 3f, 1f) < 0.3f ? 0.9f : 0.3f * Mathf.Sin(t * Mathf.PI));
                    return R(0.8f, 1.6f);
                case Kind.CryoStatusReadout:
                {
                    // A wall panel's temperature readout: a cyan status lamp, a three-segment bar that steps up, and now
                    // and then an amber warning blink.
                    var cyan = new Color(0.66f, 0.9f, 0.95f);
                    var warn = rng.Next(4) == 0;
                    Spawn(e.At, 2, 2, warn ? new Color(1f, 0.66f, 0.25f) : cyan, Vector2.zero, 0f, 0f, 0f, 1.6f, t => warn ? (Mathf.Repeat(t * 3f, 1f) < 0.5f ? 0.8f : 0.1f) : 0.55f);
                    for (var i = 0; i < 3; i++)
                        Spawn(e.At + new Vector2(0f, (4 + i * 2) * Px), 3, 1, cyan, Vector2.zero, 0f, 0f, i * 0.3f, 1.6f - i * 0.3f, t => 0.5f * (t > 0.8f ? (1f - t) / 0.2f : 1f));
                    return R(2.6f, 4.2f);
                }
                case Kind.CryoDrip:
                {
                    // Condensation beads on a frozen pipe, falls and breaks into two cold specks.
                    var from = e.At + new Vector2(inward * 0.2f, 0.9f);
                    var colour = new Color(0.62f, 0.85f, 0.95f);
                    Spawn(from, 1, 2, colour, Vector2.zero, 0f, 0f, 0f, 0.5f, t => 0.6f * t);
                    Spawn(from, 1, 2, colour, new Vector2(0f, -0.5f), 5f, 0f, 0.5f, 0.42f, _ => 0.75f);
                    var landing = from + new Vector2(0f, -0.65f);
                    Spawn(landing, 1, 1, Color.white, new Vector2(-0.5f, 0.4f), 4f, 0f, 0.92f, 0.2f, t => 0.7f * (1f - t));
                    Spawn(landing, 1, 1, Color.white, new Vector2(0.5f, 0.4f), 4f, 0f, 0.92f, 0.2f, t => 0.7f * (1f - t));
                    return R(3.5f, 6.5f);
                }
                case Kind.CryoMachineShuttle:
                {
                    // An automated system still running: an indicator light shuttles along a wall rail and back.
                    var dir = rng.Next(2) == 0 ? 1f : -1f;
                    Spawn(e.At, 2, 1, new Color(0.66f, 0.9f, 0.95f), new Vector2(0f, dir * 0.6f), 0f, 0f, 0f, 1.2f, t => 0.6f * Mathf.Sin(t * Mathf.PI));
                    Spawn(e.At + new Vector2(0f, dir * 0.72f), 2, 1, new Color(0.66f, 0.9f, 0.95f), new Vector2(0f, -dir * 0.6f), 0f, 0f, 1.4f, 1.2f, t => 0.6f * Mathf.Sin(t * Mathf.PI));
                    return R(4f, 7f);
                }
                default: // LabsSpecimenFlicker
                {
                    var stutter = new[] { 0.7f, 0.05f, 0.55f, 0.6f, 0.05f, 0.7f, 0.1f };
                    Spawn(e.At, 2, 2, new Color(0.8f, 0.35f, 1f), Vector2.zero, 0f, 0f, 0f, 0.7f, t => stutter[Mathf.Clamp(Mathf.FloorToInt(t * stutter.Length), 0, stutter.Length - 1)]);
                    return R(5f, 9f);
                }
            }
        }

        private void Spawn(Vector2 at, int widthPx, int heightPx, Color colour, Vector2 velocity, float gravity, float sway, float delay, float life, Func<float, float> alpha)
        {
            // Pieces never leave the room; the cap keeps a long stay bounded (a burst over the cap is simply skipped).
            if (!_room.Contains(at)) at = new Vector2(Mathf.Clamp(at.x, _room.xMin, _room.xMax), Mathf.Clamp(at.y, _room.yMin, _room.yMax));
            var piece = _pieces.Find(p => !p.Live);
            if (piece == null)
            {
                if (_pieces.Count >= MaxLivePieces) return;
                var go = new GameObject("AmbientPiece");
                go.transform.SetParent(transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = RoomEntryAtmosphere.Pixel;
                SpriteSorting.Apply(renderer, SortingRole.FloorDetail);
                renderer.sortingOrder = GroundOrder;
                renderer.enabled = false;
                piece = new Piece { Renderer = renderer };
                _pieces.Add(piece);
            }

            piece.Renderer.transform.localScale = new Vector3(widthPx, heightPx, 1f);
            piece.Position = at;
            piece.Velocity = velocity;
            piece.Gravity = gravity;
            piece.Sway = sway;
            piece.Delay = delay;
            piece.Life = Mathf.Max(0.05f, life);
            piece.Age = 0f;
            piece.Color = colour;
            piece.Alpha = alpha;
            piece.Live = true;
            PiecesSpawned++;
        }
    }
}
