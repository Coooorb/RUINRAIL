using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The 13 VFX and telegraph roles from FINAL_ART_PRODUCTION_SPEC section 19.
    ///
    /// The governing rule is that effects communicate mechanics before spectacle: hard pixel edges, few frames, and
    /// nothing that covers a telegraph. Telegraph markers in particular are drawn edge-heavy with a hollow centre,
    /// because the spec is explicit that the edge matters more than the fill (19.9) — a solid fill would hide the
    /// enemy the player needs to watch.
    ///
    /// The runtime scales one pool sprite to the real mechanical shape, so these are authored as unit markers and
    /// must stay geometrically honest about the area they represent (24.7).
    /// </summary>
    public static class VfxFactory
    {
        /// <summary>Role ids exactly as CombatFeedback and TelegraphIndicator spawn them.</summary>
        public static readonly IReadOnlyList<string> Roles = new[]
        {
            "muzzle", "impact", "explosion", "melee", "stagger", "heal", "status", "loot_glow",
            "telegraph_stationary", "telegraph_dash", "telegraph_projectile", "telegraph_zone", "telegraph_slam"
        };

        /// <summary>
        /// Consumable world effects (items/31): the thrown grenade, the Shock burst, and the two lasting areas. The
        /// areas are drawn at native density for their authored radius (radius × 64 px at 32 px/tile), every frame at
        /// the full radius with a readable rim — the gameplay area is full from the first frame to the last.
        /// </summary>
        public static readonly IReadOnlyList<string> ConsumableRoles = new[] { "grenade", "shock", "smoke_cloud", "fire_zone" };

        /// <summary>
        /// Shot feel: the muzzle flashes and impact bursts a weapon's projectile profile names, drawn at native pixel size
        /// (never resampled on the pixel-perfect camera), authored pointing +X from the centre — a muzzle flash along the
        /// shot, an impact facing back toward the shooter. Light (SMG, bow), standard (pistol, rifle), heavy (battle
        /// rifle, shotgun, rocket), rail (sniper) and energy (blaster): one weight ladder in the warm brass/amber language,
        /// energy in cyan. Short and front-loaded: the hot frame first, then smoke that is mostly air.
        /// </summary>
        public static readonly IReadOnlyList<string> ShotFeelRoles = new[]
        {
            "muzzle_light", "muzzle_heavy", "muzzle_rail", "muzzle_energy", "impact_light", "impact_heavy", "impact_energy"
        };

        /// <summary>
        /// Dash feel: the push-off burst left at the dash's start, authored pointing +X = away from the dash direction
        /// (the runtime rotates it there). Cold speed streaks and a kick of floor dust, front-loaded and mostly air by the
        /// second frame so nothing under it is hidden.
        /// </summary>
        public static readonly IReadOnlyList<string> DashRoles = new[] { "dash_burst" };

        /// <summary>
        /// A radius-true blast sheet: the explosion drawn at native pixels for one exact gameplay radius (in pixels at
        /// 32 px/tile), so the runtime never stretches the art to show the real area. One per distinct rocket radius.
        /// </summary>
        public static string BlastRole(int radiusPixels) => "explosion_r" + radiusPixels;

        private static bool IsBlast(string role, out int radius)
        {
            radius = 0;
            return role.StartsWith("explosion_r", System.StringComparison.Ordinal) && int.TryParse(role.Substring("explosion_r".Length), out radius) && radius > 0;
        }

        public static int FrameCount(string role) => IsBlast(role, out _) ? 6 : role switch
        {
            "muzzle" => 3,
            "impact" => 4,
            "muzzle_light" => 2,
            "muzzle_heavy" => 4,
            "muzzle_rail" => 3,
            "muzzle_energy" => 3,
            "impact_light" => 3,
            "impact_heavy" => 5,
            "impact_energy" => 4,
            "dash_burst" => 4,
            "explosion" => 7,
            "melee" => 4,
            "stagger" => 3,
            "heal" => 4,
            "status" => 3,
            "loot_glow" => 4,
            "grenade" => 2,
            "shock" => 5,
            "smoke_cloud" => 6,
            "fire_zone" => 5,
            _ => 2   // telegraphs pulse rather than play out
        };

        public static int SizeOf(string role) => IsBlast(role, out var blast) ? blast * 2 + 2 : role switch
        {
            "explosion" => 32,
            "muzzle" or "impact" or "muzzle_energy" or "impact_energy" => 24,
            "muzzle_heavy" or "muzzle_rail" or "impact_heavy" or "dash_burst" => 32,
            "melee" => 24,
            "loot_glow" => 24,
            "grenade" => 12,
            "shock" => 160,        // Shock Grenade: 2.5-tile radius
            "smoke_cloud" => 256,  // Smoke Grenade: 4-tile radius
            "fire_zone" => 160,    // Incendiary burn area: 2.5-tile radius
            _ when role.StartsWith("telegraph") => 32,
            _ => 16
        };

        public static PixelCanvas Build(string role, int frame)
        {
            var size = SizeOf(role);
            var c = new PixelCanvas(size, size);
            var mid = size / 2;
            var frames = FrameCount(role);
            var t = frames <= 1 ? 0f : (float)frame / (frames - 1);

            if (IsBlast(role, out var blastRadius)) { RadiusBlast(c, mid, blastRadius, frame); return c; }
            switch (role)
            {
                case "muzzle": ShotMuzzle(c, mid, frame, 9, 3.5f, Warm); break;
                case "impact": ShotImpact(c, mid, frame, frames, 9, Warm); break;
                case "muzzle_light": ShotMuzzle(c, mid, frame + 1, 6, 2.2f, Warm); break;
                case "muzzle_heavy": ShotMuzzle(c, mid, frame, 14, 5.5f, Warm, jets: true); break;
                case "muzzle_rail": RailMuzzle(c, mid, frame); break;
                case "muzzle_energy": EnergyMuzzle(c, mid, frame); break;
                case "impact_light": ShotImpact(c, mid, frame + 1, frames + 1, 6, Warm); break;
                case "impact_heavy": ShotImpact(c, mid, frame, frames, 13, Warm, debris: true); break;
                case "impact_energy": EnergyImpact(c, mid, frame, frames); break;
                case "dash_burst": DashBurst(c, mid, frame, frames); break;
                case "explosion": Explosion(c, mid, t); break;
                case "melee": MeleeArc(c, size, mid, t); break;
                case "stagger": Stagger(c, mid, t); break;
                case "heal": Heal(c, size, mid, t); break;
                case "status": Status(c, mid, t); break;
                case "loot_glow": LootGlow(c, mid, t); break;
                case "grenade": Grenade(c, mid, frame); break;
                case "shock": ShockBurst(c, mid, t); break;
                case "smoke_cloud": SmokeCloud(c, mid, frame, frames); break;
                case "fire_zone": FireZone(c, mid, frame, frames); break;
                case "telegraph_zone": TelegraphBox(c, size, frame); break;
                case "telegraph_dash": TelegraphDash(c, size, mid, frame); break;
                case "telegraph_projectile": TelegraphLine(c, size, mid, frame); break;
                case "telegraph_slam": TelegraphRing(c, size, mid, frame); break;
                default: TelegraphRing(c, size, mid, frame); break;
            }

            return c;
        }

        // ---- consumables (items/31) ----

        private static float Hash(int a, int b) => Mathf.Abs(Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.547f) % 1f;

        // The thrown grenade: a small dark canister with an ochre band; two frames read as a tumble in flight.
        private static void Grenade(PixelCanvas c, int mid, int frame)
        {
            var body = RuinPalette.DarkSteel;
            if (frame == 0)
            {
                c.Ellipse(mid, mid, 3.5f, 3f, RuinPalette.OutlineCharcoal);
                c.Ellipse(mid, mid, 2.6f, 2.1f, body);
                c.Rect(mid - 1, mid - 3, 2, 6, RuinPalette.WarningOchre);
                c.Set(mid - 2, mid + 1, RuinPalette.PaleSteel);
            }
            else
            {
                c.Ellipse(mid, mid, 3f, 3.5f, RuinPalette.OutlineCharcoal);
                c.Ellipse(mid, mid, 2.1f, 2.6f, body);
                c.Rect(mid - 3, mid - 1, 6, 2, RuinPalette.WarningOchre);
                c.Set(mid + 1, mid + 2, RuinPalette.PaleSteel);
            }
        }

        // Shock Grenade: a cold electric ring that reaches the real radius at once, jagged arcs inside, then breaks up.
        private static void ShockBurst(PixelCanvas c, int mid, float t)
        {
            var r = mid - 1f;
            var ring = t < 0.5f ? RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.35f) : RuinPalette.ColdBlue;
            for (var a = 0; a < 720; a++)
            {
                var ang = a * Mathf.PI / 360f;
                for (var w = 0; w < (t < 0.5f ? 3 : 2); w++)
                    c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * (r - w)), mid + Mathf.RoundToInt(Mathf.Sin(ang) * (r - w)), ring);
            }

            var arcs = t < 0.3f ? 14 : t < 0.7f ? 10 : 6;
            for (var i = 0; i < arcs; i++)
            {
                var ang = (i + Hash(i, 7) * 0.6f) * Mathf.PI * 2f / arcs + t;
                int px = mid, py = mid;
                var steps = 8;
                for (var s = 1; s <= steps; s++)
                {
                    var rr = r * s / steps * (0.35f + 0.65f * Mathf.Min(1f, 0.4f + t));
                    var jitter = (Hash(i, s + Mathf.RoundToInt(t * 10f)) - 0.5f) * 0.5f;
                    var nx = mid + Mathf.RoundToInt(Mathf.Cos(ang + jitter) * rr);
                    var ny = mid + Mathf.RoundToInt(Mathf.Sin(ang + jitter) * rr);
                    c.Line(px, py, nx, ny, s < steps / 2 ? RuinPalette.Lighten(RuinPalette.ElectricCyan, 0.5f) : RuinPalette.ElectricCyan);
                    px = nx; py = ny;
                }
            }

            if (t < 0.25f) c.Ellipse(mid, mid, 6f, 6f, RuinPalette.Hex("#E6FBFF"));
            if (t > 0.6f)
                for (var y = 0; y < c.Height; y++)
                for (var x = 0; x < c.Width; x++)
                    if (c.IsOpaque(x, y) && (x * 7 + y * 3) % 5 < (t > 0.85f ? 3 : 1)) c.Erase(x, y);
        }

        // Smoke Grenade: overlapping shaded puffs clipped to the exact radius, a 50 % dither so actors inside stay
        // readable, and a denser rim that marks the area's real edge. The puffs drift frame to frame; the last frame
        // thins (the cloud is about to lift) but keeps the full rim.
        private static void SmokeCloud(PixelCanvas c, int mid, int frame, int frames)
        {
            var r = mid - 0.5f;
            var shadow = RuinPalette.ConcreteShadow;
            var body = RuinPalette.Concrete;
            var light = RuinPalette.ConcreteLight;
            var puffs = new PixelCanvas(c.Width, c.Height);
            for (var i = 0; i < 46; i++)
            {
                var ang = Hash(i, 1) * Mathf.PI * 2f + frame * 0.09f * (i % 2 == 0 ? 1f : -1f);
                var dist = Mathf.Sqrt(Hash(i, 2)) * (r - 14f);
                var pr = 14f + Hash(i, 3) * 18f;
                var cx = mid + Mathf.Cos(ang) * dist;
                var cy = mid + Mathf.Sin(ang) * dist;
                puffs.Ellipse(cx, cy - 2f, pr, pr * 0.85f, shadow);
                puffs.Ellipse(cx, cy, pr * 0.92f, pr * 0.78f, body);
                puffs.Ellipse(cx - pr * 0.25f, cy + pr * 0.25f, pr * 0.45f, pr * 0.35f, light);
            }

            var thin = frame == frames - 1;
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var dx = x + 0.5f - mid;
                var dy = y + 0.5f - mid;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                if (d > r - 2.5f) { c.Set(x, y, RuinPalette.Darken(body, 0.15f)); continue; }        // the real edge
                if (d > r - 6f) { if ((x + y) % 2 == 0) c.Set(x, y, body); continue; }
                var col = puffs.IsOpaque(x, y) ? puffs.Get(x, y) : body;
                var keep = thin ? (x + y * 2) % 4 == 0 : (x + y) % 2 == 0;
                if (keep) c.Set(x, y, col);
            }
        }

        // Incendiary burn area: a scorched rim at the real radius, sparse char inside, flame tongues that flicker
        // frame to frame; the last frame is embers (the burn is ending) with the rim kept.
        private static void FireZone(PixelCanvas c, int mid, int frame, int frames)
        {
            var r = mid - 0.5f;
            var embers = frame == frames - 1;
            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var dx = x + 0.5f - mid;
                var dy = y + 0.5f - mid;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                if (d > r - 2f) { c.Set(x, y, RuinPalette.Rust); continue; }
                if (d > r - 3f) { c.Set(x, y, RuinPalette.BurntRustDark); continue; }
                if ((x * 3 + y * 5) % 11 == 0) c.Set(x, y, RuinPalette.BurntRustDark);
            }

            var count = embers ? 34 : 38;
            for (var i = 0; i < count; i++)
            {
                var ang = Hash(i, 11) * Mathf.PI * 2f;
                var dist = Mathf.Sqrt(Hash(i, 12)) * (r - 10f);
                var fx = mid + Mathf.RoundToInt(Mathf.Cos(ang) * dist);
                var fy = mid + Mathf.RoundToInt(Mathf.Sin(ang) * dist);
                if (embers)
                {
                    c.Set(fx, fy, RuinPalette.OxideOrange);
                    if (Hash(i, 13) > 0.5f) c.Set(fx + 1, fy, RuinPalette.WarningOchre);
                    continue;
                }

                var height = 9 + Mathf.RoundToInt(Hash(i, 20 + frame) * 8f);
                var half = 3 + Mathf.RoundToInt(Hash(i, 14) * 2f);
                for (var h = 0; h < height; h++)
                {
                    var w = Mathf.Max(0, Mathf.RoundToInt(half * (1f - (float)h / height)));
                    var sway = Mathf.RoundToInt(Mathf.Sin((h + frame * 2 + i) * 0.9f) * (h > height / 2 ? 1f : 0f));
                    var col = h < height * 0.4f ? RuinPalette.OxideOrange : h < height * 0.8f ? RuinPalette.AmberActive : RuinPalette.Hex("#FFF3C8");
                    for (var xx = -w; xx <= w; xx++) c.Set(fx + xx + sway, fy + h, Mathf.Abs(xx) == w && w > 0 ? RuinPalette.Rust : col);
                }
            }
        }

        // ---- shot feel ----

        private readonly struct ShotRamp
        {
            public ShotRamp(Color32 white, Color32 core, Color32 hot, Color32 edge, Color32 smoke)
            {
                White = white; Core = core; Hot = hot; Edge = edge; Smoke = smoke;
            }

            public Color32 White { get; }
            public Color32 Core { get; }
            public Color32 Hot { get; }
            public Color32 Edge { get; }
            public Color32 Smoke { get; }
        }

        private static readonly ShotRamp Warm = new(RuinPalette.Hex("#FFFFFF"), RuinPalette.Hex("#FFF3C8"), RuinPalette.AmberActive, RuinPalette.OxideOrange, RuinPalette.Hex("#8C8A80"));
        private static readonly ShotRamp Cold = new(RuinPalette.Hex("#FFFFFF"), RuinPalette.Hex("#DDFBFF"), RuinPalette.Hex("#7FE6F0"), RuinPalette.ElectricCyan, RuinPalette.Hex("#5E8A92"));

        /// <summary>A forward cone along +X from the centre: edge, hot body, cream core, white centre line.</summary>
        private static void Cone(PixelCanvas c, int mid, int length, float half, ShotRamp ramp, int back = 2)
        {
            for (var d = -back; d <= length; d++)
            {
                var k = d < 0 ? 1f + d / (float)(back + 1) : 1f - d / (float)(length + 1);
                var h = half * Mathf.Sqrt(Mathf.Max(0f, k));
                var x = mid + d;
                for (var y = -Mathf.CeilToInt(h); y <= Mathf.CeilToInt(h); y++)
                {
                    var a = Mathf.Abs(y);
                    if (a > h + 0.35f) continue;
                    c.Set(x, mid + y, a > h * 0.62f ? ramp.Edge : a > h * 0.3f ? ramp.Hot : ramp.Core);
                }

                if (d >= -1 && d < length * 0.7f) c.Set(x, mid, ramp.White);
            }
        }

        /// <summary>A loose, mostly-air smoke puff (dithered) so it never hides what is behind it.</summary>
        private static void Puff(PixelCanvas c, float cx, float cy, float r, Color32 col, int phase)
        {
            for (var y = Mathf.FloorToInt(cy - r); y <= Mathf.CeilToInt(cy + r); y++)
            for (var x = Mathf.FloorToInt(cx - r); x <= Mathf.CeilToInt(cx + r); x++)
            {
                var dx = (x + 0.5f - cx) / r; var dy = (y + 0.5f - cy) / r;
                var dd = dx * dx + dy * dy;
                if (dd > 1f) continue;
                // Denser at the heart, thinning to the rim: a soft clump, never a hollow ring.
                if (((x + y + phase) & 1) == 0 && Hash(x, y + phase) > 0.2f + dd * 0.55f) c.Set(x, y, col);
            }
        }

        /// <summary>
        /// Muzzle flash: frame 0 the full cone with side spikes (heavy guns add side jets), frame 1 a shorter hot cone,
        /// later frames a small ember and smoke that drifts forward.
        /// </summary>
        private static void ShotMuzzle(PixelCanvas c, int mid, int frame, int length, float half, ShotRamp ramp, bool jets = false)
        {
            switch (frame)
            {
                case 0:
                    Cone(c, mid, length, half, ramp);
                    var spike = Mathf.Max(2, Mathf.RoundToInt(half * 0.9f));
                    c.Line(mid + 1, mid + Mathf.CeilToInt(half * 0.5f), mid + 1 + spike, mid + Mathf.CeilToInt(half * 0.5f) + spike, ramp.Hot);
                    c.Line(mid + 1, mid - Mathf.CeilToInt(half * 0.5f), mid + 1 + spike, mid - Mathf.CeilToInt(half * 0.5f) - spike, ramp.Hot);
                    if (jets)
                    {
                        c.Line(mid, mid + 2, mid - 1, mid + Mathf.RoundToInt(half) + 2, ramp.Edge);
                        c.Line(mid, mid - 2, mid - 1, mid - Mathf.RoundToInt(half) - 2, ramp.Edge);
                        c.Line(mid + 1, mid + 2, mid, mid + Mathf.RoundToInt(half) + 1, ramp.Hot);
                        c.Line(mid + 1, mid - 2, mid, mid - Mathf.RoundToInt(half) - 1, ramp.Hot);
                    }
                    break;
                case 1:
                    Cone(c, mid, Mathf.Max(3, length * 3 / 5), Mathf.Max(1.5f, half * 0.7f), ramp, 1);
                    if (jets) { c.Set(mid, mid + Mathf.RoundToInt(half) + 1, ramp.Edge); c.Set(mid, mid - Mathf.RoundToInt(half) - 1, ramp.Edge); }
                    Puff(c, mid + length * 0.55f, mid, Mathf.Max(2f, half * 0.8f), ramp.Smoke, frame);
                    break;
                case 2:
                    c.Set(mid + 1, mid, ramp.Core); c.Set(mid + 2, mid, ramp.Hot);
                    Puff(c, mid + length * 0.65f, mid, Mathf.Max(2.5f, half), ramp.Smoke, frame);
                    break;
                default:
                    Puff(c, mid + length * 0.75f, mid + 1, Mathf.Max(3f, half * 1.2f), RuinPalette.Darken(ramp.Smoke, 0.15f), frame);
                    break;
            }
        }

        /// <summary>Sniper: a long white-hot rail with cyan edges and perpendicular vent flares.</summary>
        private static void RailMuzzle(PixelCanvas c, int mid, int frame)
        {
            var len = frame == 0 ? 15 : frame == 1 ? 10 : 0;
            if (len > 0)
            {
                for (var d = -1; d <= len; d++)
                {
                    c.Set(mid + d, mid, d < len - 2 ? Cold.White : Cold.Hot);
                    if (d < len - 3) { c.Set(mid + d, mid + 1, Cold.Hot); c.Set(mid + d, mid - 1, Cold.Hot); }
                    if (frame == 0 && d < len / 2) { c.Set(mid + d, mid + 2, Cold.Edge); c.Set(mid + d, mid - 2, Cold.Edge); }
                }

                var vent = frame == 0 ? 5 : 3;
                for (var side = -1; side <= 1; side += 2)
                {
                    c.Line(mid + 2, mid + side * 2, mid + 2, mid + side * (2 + vent), Warm.Hot);
                    c.Set(mid + 2, mid + side * (2 + vent), Warm.Core);
                }

                c.Ellipse(mid, mid, 2f, 2f, Warm.Core);
                c.Set(mid, mid, Cold.White);
            }
            else Puff(c, mid + 6, mid, 3f, Cold.Smoke, frame);
        }

        /// <summary>Blaster: a cyan ring snapping out round a white core with a short forward spike.</summary>
        private static void EnergyMuzzle(PixelCanvas c, int mid, int frame)
        {
            var r = 3.5f + frame * 2f;
            for (var a = 0; a < 64; a++)
            {
                var ang = a * Mathf.PI / 32f;
                var x = mid + 1 + Mathf.RoundToInt(Mathf.Cos(ang) * r);
                var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * r * 0.85f);
                if (frame < 2 || (a & 1) == 0) c.Set(x, y, frame == 0 ? Cold.Hot : Cold.Edge);
            }

            if (frame == 0)
            {
                Cone(c, mid, 7, 2.2f, Cold, 1);
                c.Ellipse(mid + 1, mid, 2.2f, 2.2f, Cold.Core);
                c.Set(mid + 1, mid, Cold.White);
            }
            else if (frame == 1)
            {
                c.Ellipse(mid + 1, mid, 1.4f, 1.4f, Cold.Hot);
                c.Set(mid + 3, mid, Cold.Core);
            }
        }

        /// <summary>
        /// Impact burst, facing back along +X toward the shooter: a white-hot flash, then sparks thrown back in a fan,
        /// then embers and a dust ring that is mostly air. Heavy hits add dark debris chips.
        /// </summary>
        private static void ShotImpact(PixelCanvas c, int mid, int frame, int frames, int reach, ShotRamp ramp, bool debris = false)
        {
            var t = frames <= 1 ? 0f : frame / (float)(frames - 1);
            var flash = reach * 0.42f;
            if (frame == 0)
            {
                c.Ellipse(mid, mid, flash + 1f, flash + 1f, ramp.Edge);
                c.Ellipse(mid, mid, flash, flash, ramp.Hot);
                c.Ellipse(mid, mid, flash * 0.6f, flash * 0.6f, ramp.Core);
                c.Ellipse(mid, mid, Mathf.Max(1f, flash * 0.3f), Mathf.Max(1f, flash * 0.3f), ramp.White);
                // A cross glint: reads as a hit even at a glance.
                c.Line(mid - Mathf.RoundToInt(flash + 2), mid, mid + Mathf.RoundToInt(flash + 2), mid, ramp.Core);
                c.Line(mid, mid - Mathf.RoundToInt(flash + 2), mid, mid + Mathf.RoundToInt(flash + 2), ramp.Core);
                return;
            }

            var sparks = debris ? 9 : 7;
            for (var i = 0; i < sparks; i++)
            {
                // Mostly back toward the shooter (+X), a few to the sides.
                var spread = (Hash(i, 3) - 0.5f) * (i % 3 == 0 ? 3.4f : 2.1f);
                var len = reach * (0.55f + Hash(i, 5) * 0.45f);
                var r0 = Mathf.Lerp(1.5f, len * 0.75f, t);
                var r1 = Mathf.Lerp(len * 0.55f, len, t);
                var cos = Mathf.Cos(spread); var sin = Mathf.Sin(spread);
                var x0 = mid + Mathf.RoundToInt(cos * r0); var y0 = mid + Mathf.RoundToInt(sin * r0);
                var x1 = mid + Mathf.RoundToInt(cos * r1); var y1 = mid + Mathf.RoundToInt(sin * r1);
                if (t < 0.7f) c.Line(x0, y0, x1, y1, t < 0.4f ? ramp.Hot : ramp.Edge);
                c.Set(x1, y1, t < 0.5f ? ramp.Core : ramp.Hot);
            }

            if (t < 0.45f)
            {
                c.Ellipse(mid, mid, flash * 0.7f, flash * 0.7f, ramp.Hot);
                c.Ellipse(mid, mid, flash * 0.4f, flash * 0.4f, ramp.Core);
            }
            else if (t < 0.8f) c.Set(mid, mid, ramp.Hot);

            if (debris && t > 0.2f)
                for (var i = 0; i < 5; i++)
                {
                    var ang = (Hash(i, 11) - 0.5f) * 3f;
                    var r = Mathf.Lerp(3f, reach * 0.9f, t) * (0.6f + Hash(i, 13) * 0.4f);
                    var x = mid + Mathf.RoundToInt(Mathf.Cos(ang) * r); var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                    c.Set(x, y, RuinPalette.DarkSteel); c.Set(x + 1, y, RuinPalette.MidSteel);
                }

            if (t > 0.5f) Puff(c, mid + 1, mid, Mathf.Lerp(flash, reach * 0.8f, t), ramp.Smoke, frame);
        }

        /// <summary>
        /// Dash push-off, facing +X = behind the dash: frame 0 a cold crescent where the foot kicked and four speed
        /// streaks, then the crescent opens and thins while the streaks run back and break up, and a kick of floor dust
        /// drifts out behind; the last frame is dust only. Cold like the energy shots so it never reads as a hit.
        /// </summary>
        private static void DashBurst(PixelCanvas c, int mid, int frame, int frames)
        {
            var t = frames <= 1 ? 0f : frame / (float)(frames - 1);
            var dust = RuinPalette.DustBeige; var dustShade = RuinPalette.Darken(RuinPalette.DustBeige, 0.3f);

            if (frame < 3)
            {
                // The crescent opens toward the back: a kick plane across the dash line.
                var r = Mathf.Lerp(5f, 10f, t);
                var col = frame == 0 ? Cold.Core : frame == 1 ? Cold.Hot : Cold.Edge;
                for (var a = -65; a <= 65; a += 3)
                {
                    var ang = a * Mathf.Deg2Rad;
                    var x = mid - 3 + Mathf.RoundToInt(Mathf.Cos(ang) * r * 0.55f);
                    var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                    if (frame < 2) c.Set(x + 1, y, Cold.Edge); // a cyan rim behind the bright face
                    c.Set(x, y, col);
                    if (frame == 0 && Mathf.Abs(a) < 40) c.Set(x - 1, y, Cold.White);
                }

                // The launch frame carries the punch: a white-hot kick flash where the foot pushed off.
                if (frame == 0)
                {
                    c.Ellipse(mid - 2, mid, 2.5f, 4.5f, Cold.Hot);
                    c.Ellipse(mid - 2, mid, 1.5f, 3f, Cold.Core);
                    c.Ellipse(mid - 2, mid, 0.8f, 1.6f, Cold.White);
                }

                // Speed streaks: staggered lengths so they read as motion, never as a solid block.
                int[] rows = { -5, -2, 1, 4 };
                for (var i = 0; i < rows.Length; i++)
                {
                    var len = 7 + Mathf.RoundToInt(Hash(i, 21) * 6f);
                    var start = mid - 2 + Mathf.RoundToInt(t * 9f) + (i % 2);
                    var from = frame == 2 ? start + len / 2 : start;
                    for (var x = from; x < start + len; x++)
                    {
                        if (frame == 2 && ((x + i) & 1) == 1) continue;
                        c.Set(x, mid + rows[i], x < start + 2 && frame == 0 ? Cold.White : frame == 0 ? Cold.Core : Cold.Edge);
                    }
                }
            }

            if (frame >= 1)
            {
                // Floor dust kicked out behind, spreading and thinning.
                var push = Mathf.Lerp(3f, 9f, t);
                var size = Mathf.Lerp(2.5f, 4.5f, t);
                Puff(c, mid + push, mid - 3f, size, frame == 3 ? dustShade : dust, frame);
                Puff(c, mid + push + 1f, mid + 3f, size * 0.85f, frame == 3 ? dustShade : dust, frame + 1);
                if (frame < 3) Puff(c, mid + push - 1f, mid, size * 0.6f, dustShade, frame + 2);
                for (var i = 0; i < 4; i++)
                {
                    var ang = (Hash(i, 31) - 0.5f) * 2.2f;
                    var rr = Mathf.Lerp(4f, 13f, t) * (0.7f + Hash(i, 33) * 0.3f);
                    c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * rr), mid + Mathf.RoundToInt(Mathf.Sin(ang) * rr), t < 0.6f ? RuinPalette.ConcreteLight : dustShade);
                }
            }
        }

        /// <summary>
        /// The radius-true blast, native pixels: a crisp rim on the exact gameplay radius from the first frame (the area is
        /// live at once), a white-hot fireball that swells to most of it, then a broken ring of fire and smoke that is
        /// mostly air by the last frames, so hazards and telegraphs under it read again quickly.
        /// </summary>
        private static void RadiusBlast(PixelCanvas c, int mid, int radius, int frame)
        {
            var R = (float)radius;
            var white = Warm.White; var core = Warm.Core; var hot = Warm.Hot; var edge = Warm.Edge;
            var dark = RuinPalette.BurntRustDark; var smoke = Warm.Smoke; var soot = RuinPalette.Darken(Warm.Smoke, 0.35f);
            var cx = mid + 0.5f; var cy = mid + 0.5f;
            float Noise(float angle, int seed) => Hash(Mathf.FloorToInt((angle + Mathf.PI) * 6f), seed);

            void Rim(int every, Color32 outer, Color32 inner)
            {
                for (var a = 0; a < 720; a++)
                {
                    if (every > 1 && (a / 6) % every != 0) continue;
                    var ang = a * Mathf.PI / 360f;
                    c.Set(Mathf.FloorToInt(cx + Mathf.Cos(ang) * (R - 0.5f)), Mathf.FloorToInt(cy + Mathf.Sin(ang) * (R - 0.5f)), outer);
                    if (inner.a > 0) c.Set(Mathf.FloorToInt(cx + Mathf.Cos(ang) * (R - 1.5f)), Mathf.FloorToInt(cy + Mathf.Sin(ang) * (R - 1.5f)), inner);
                }
            }

            for (var y = 0; y < c.Height; y++)
            for (var x = 0; x < c.Width; x++)
            {
                var dx = x + 0.5f - cx; var dy = y + 0.5f - cy;
                var d = Mathf.Sqrt(dx * dx + dy * dy) / R;
                if (d > 1f) continue;
                var ang = Mathf.Atan2(dy, dx);
                var n = Noise(ang, frame);
                var dither = Hash(x, y + frame * 31);
                switch (frame)
                {
                    case 0:
                        if (d < 0.18f) c.Set(x, y, white);
                        else if (d < 0.32f) c.Set(x, y, core);
                        else if (d < 0.42f + n * 0.05f) c.Set(x, y, hot);
                        else if (d < 0.48f + n * 0.06f) c.Set(x, y, edge);
                        break;
                    case 1:
                        if (d < 0.14f) c.Set(x, y, white);
                        else if (d < 0.38f) c.Set(x, y, core);
                        else if (d < 0.6f + n * 0.06f) c.Set(x, y, hot);
                        else if (d < 0.72f + n * 0.08f) c.Set(x, y, edge);
                        else if (d < 0.76f + n * 0.08f) c.Set(x, y, dark);
                        break;
                    case 2:
                        if (d < 0.22f) c.Set(x, y, core);
                        else if (d < 0.5f + n * 0.08f) c.Set(x, y, dither < 0.2f ? edge : hot);
                        else if (d < 0.8f + n * 0.1f) c.Set(x, y, dither < 0.3f ? dark : edge);
                        else if (d < 0.86f + n * 0.1f && dither < 0.6f) c.Set(x, y, dark);
                        break;
                    case 3:
                        // The fire breaks into a ring; the heart clears to thin smoke.
                        if (d > 0.58f + n * 0.08f && d < 0.92f && dither < 0.5f) c.Set(x, y, dither < 0.18f ? hot : dither < 0.34f ? edge : dark);
                        else if (d < 0.58f && ((x + y) & 1) == 0 && dither < 0.4f) c.Set(x, y, smoke);
                        break;
                    case 4:
                        if (d > 0.62f && ((x + y) & 1) == 0 && dither < 0.32f) c.Set(x, y, d > 0.85f ? soot : smoke);
                        else if (dither > 0.985f) c.Set(x, y, hot); // embers
                        break;
                    default:
                        if (d > 0.7f && ((x + y) & 1) == 0 && dither < 0.12f) c.Set(x, y, soot);
                        else if (dither > 0.993f) c.Set(x, y, edge);
                        break;
                }
            }

            // Sparks thrown to the edge of the area on the first frames.
            if (frame <= 1)
                for (var i = 0; i < 12; i++)
                {
                    var ang = (i + Hash(i, 41) * 0.6f) * Mathf.PI * 2f / 12f;
                    var r0 = R * (frame == 0 ? 0.5f : 0.74f); var r1 = R * (frame == 0 ? 0.78f : 0.94f);
                    c.Line(Mathf.FloorToInt(cx + Mathf.Cos(ang) * r0), Mathf.FloorToInt(cy + Mathf.Sin(ang) * r0),
                        Mathf.FloorToInt(cx + Mathf.Cos(ang) * r1), Mathf.FloorToInt(cy + Mathf.Sin(ang) * r1), frame == 0 ? core : hot);
                }

            // The true radius as a broken ring of sparks (cream/amber, never the telegraphs' solid orange outline), thinning as it clears.
            switch (frame)
            {
                case 0: Rim(2, core, hot); break;
                case 1: Rim(2, hot, new Color32(0, 0, 0, 0)); break;
                case 2: Rim(3, hot, new Color32(0, 0, 0, 0)); break;
                case 3: Rim(4, edge, new Color32(0, 0, 0, 0)); break;
            }
        }

        /// <summary>Blaster hit: a white flash, a cyan shock ring and plasma droplets splashing back.</summary>
        private static void EnergyImpact(PixelCanvas c, int mid, int frame, int frames)
        {
            var t = frame / (float)(frames - 1);
            if (frame == 0)
            {
                c.Ellipse(mid, mid, 4.5f, 4.5f, Cold.Edge);
                c.Ellipse(mid, mid, 3.5f, 3.5f, Cold.Hot);
                c.Ellipse(mid, mid, 2f, 2f, Cold.White);
                return;
            }

            var r = Mathf.Lerp(4f, 10f, t);
            for (var a = 0; a < 72; a++)
            {
                var ang = a * Mathf.PI / 36f;
                if (t > 0.6f && (a & 1) == 1) continue;
                c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * r), mid + Mathf.RoundToInt(Mathf.Sin(ang) * r), t < 0.5f ? Cold.Hot : Cold.Edge);
            }

            for (var i = 0; i < 6; i++)
            {
                var ang = (Hash(i, 17) - 0.5f) * 2.4f;
                var d = Mathf.Lerp(3f, 9f, t) * (0.7f + Hash(i, 19) * 0.3f);
                var x = mid + Mathf.RoundToInt(Mathf.Cos(ang) * d); var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * d);
                c.Set(x, y, Cold.Core);
                if (t < 0.6f) c.Set(x + 1, y, Cold.Hot);
            }

            if (t < 0.5f) c.Ellipse(mid, mid, 2f, 2f, Cold.Hot);
        }

        // 19.3 explosion: hot centre -> orange -> smoke, clear radius read, 7 frames
        private static void Explosion(PixelCanvas c, int mid, float t)
        {
            var r = Mathf.Lerp(3f, mid - 1f, Mathf.Sqrt(t));
            if (t < 0.55f)
            {
                c.Ellipse(mid, mid, r, r * 0.92f, RuinPalette.OxideOrange);
                c.Ellipse(mid, mid, r * 0.66f, r * 0.6f, RuinPalette.AmberActive);
                c.Ellipse(mid, mid, r * 0.33f, r * 0.3f, RuinPalette.Hex("#FFF3C8"));
            }
            else
            {
                // Dissipating smoke ring: hollow, so it cannot hide a hazard underneath for long.
                var smoke = RuinPalette.Darken(RuinPalette.Concrete, 0.18f);
                c.Ellipse(mid, mid, r, r * 0.9f, smoke);
                c.Ellipse(mid, mid, r * 0.68f, r * 0.6f, RuinPalette.Darken(RuinPalette.OxideOrange, 0.5f));
                for (var y = 0; y < c.Height; y++)
                for (var x = 0; x < c.Width; x++)
                    if (c.IsOpaque(x, y) && ((x + y) % 3 == 0)) c.Erase(x, y);
            }
        }

        // 19.4 melee arc: directional, does not imply a larger hitbox than the mechanics
        private static void MeleeArc(PixelCanvas c, int size, int mid, float t)
        {
            var r = size / 2f - 2f;
            var sweep = Mathf.Lerp(-0.9f, 0.9f, t);
            var col = RuinPalette.Hex("#E6F0FF");
            for (var i = -14; i <= 14; i++)
            {
                var a = sweep + i * 0.035f;
                var falloff = 1f - Mathf.Abs(i) / 16f;
                var rr = r * (0.72f + 0.28f * falloff);
                var x = mid + Mathf.RoundToInt(Mathf.Cos(a) * rr);
                var y = mid + Mathf.RoundToInt(Mathf.Sin(a) * rr);
                c.Set(x, y, falloff > 0.6f ? col : RuinPalette.Darken(col, 0.35f));
                if (falloff > 0.75f) c.Set(x - 1, y, col);
            }
        }

        // 19.5 stagger: brief shock motif above the enemy, not comedy stars
        private static void Stagger(PixelCanvas c, int mid, float t)
        {
            var spread = Mathf.RoundToInt(Mathf.Lerp(2f, 5f, t));
            var col = RuinPalette.WarningOchre;
            foreach (var dx in new[] { -1, 1 })
            {
                c.Line(mid + dx * spread, mid + 2, mid + dx * (spread - 1), mid - 1, col);
                c.Line(mid + dx * (spread - 1), mid - 1, mid + dx * spread, mid - 3, col);
            }
            c.Line(mid, mid + 4, mid, mid + 1, RuinPalette.Lighten(col, 0.35f));
        }

        // 19.6 heal: green/white upward medical energy, no fantasy sparkle
        private static void Heal(PixelCanvas c, int size, int mid, float t)
        {
            var rise = Mathf.RoundToInt(Mathf.Lerp(0f, size - 6f, t));
            var col = RuinPalette.TerminalGreen;
            c.Rect(mid - 1, 2 + rise, 3, 5, col);
            c.Rect(mid - 3, 4 + rise, 7, 1, col);
            if (t < 0.7f)
            {
                c.Set(mid - 4, 1 + rise, RuinPalette.Lighten(col, 0.4f));
                c.Set(mid + 4, 3 + rise, RuinPalette.Lighten(col, 0.4f));
            }
        }

        // 19.7 status: a small persistent marker, minimal screen noise
        private static void Status(PixelCanvas c, int mid, float t)
        {
            var r = 3 + Mathf.RoundToInt(t);
            c.Ellipse(mid, mid, r, r, RuinPalette.Darken(RuinPalette.ColdBlue, 0.4f));
            c.Ellipse(mid, mid, r - 1, r - 1, RuinPalette.ColdBlue);
            c.Set(mid, mid, RuinPalette.Lighten(RuinPalette.ColdBlue, 0.5f));
        }

        // 19.8 loot glow: rarity-aware halo that leaves the ground item visible
        private static void LootGlow(PixelCanvas c, int mid, float t)
        {
            var r = mid - 3 + Mathf.RoundToInt(Mathf.Sin(t * Mathf.PI) * 2f);
            var col = RuinPalette.WarningOchre;
            // Hollow halo plus rising motes. A filled glow would hide the item it is advertising.
            for (var a = 0; a < 20; a++)
            {
                var ang = a * Mathf.PI * 2f / 20f;
                c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * r), mid + Mathf.RoundToInt(Mathf.Sin(ang) * r), col);
            }
            var lift = Mathf.RoundToInt(t * 5f);
            c.Set(mid - 4, mid - 2 + lift, RuinPalette.Lighten(col, 0.4f));
            c.Set(mid + 3, mid - 4 + lift, RuinPalette.Lighten(col, 0.4f));
        }

        // 19.9 telegraph zone: high-contrast edge, transparent centre
        private static void TelegraphBox(PixelCanvas c, int size, int frame)
        {
            var col = frame == 0 ? RuinPalette.EmergencyRed : RuinPalette.Lighten(RuinPalette.EmergencyRed, 0.35f);
            c.RectOutline(0, 0, size, size, col);
            c.RectOutline(1, 1, size - 2, size - 2, RuinPalette.Darken(col, 0.45f));
            // Corner ticks make the extent readable even when the edge crosses busy floor art.
            foreach (var (x, y) in new[] { (0, 0), (size - 4, 0), (0, size - 4), (size - 4, size - 4) })
            {
                c.Rect(x, y, 4, 2, col);
                c.Rect(x, y, 2, 4, col);
            }
        }

        // 19.10 telegraph dash/charge: a directional corridor with an obvious source-to-target path
        private static void TelegraphDash(PixelCanvas c, int size, int mid, int frame)
        {
            var col = frame == 0 ? RuinPalette.EmergencyRed : RuinPalette.Lighten(RuinPalette.EmergencyRed, 0.35f);
            c.Line(0, 2, size - 1, 2, col);
            c.Line(0, size - 3, size - 1, size - 3, col);
            for (var x = 2; x < size - 6; x += 7)
            {
                c.Line(x, mid - 4, x + 4, mid, col);
                c.Line(x + 4, mid, x, mid + 4, col);
            }
        }

        // 19.11 telegraph projectile/aim: a thin high-contrast line that survives every biome
        private static void TelegraphLine(PixelCanvas c, int size, int mid, int frame)
        {
            var col = frame == 0 ? RuinPalette.EmergencyRed : RuinPalette.Lighten(RuinPalette.EmergencyRed, 0.4f);
            // Dark backing beneath the bright line so it reads on both pale lab floors and dark metal.
            c.Line(0, mid - 1, size - 1, mid - 1, RuinPalette.NearBlack);
            c.Line(0, mid + 1, size - 1, mid + 1, RuinPalette.NearBlack);
            c.Line(0, mid, size - 1, mid, col);
            c.Rect(size - 5, mid - 3, 4, 7, col);
        }

        // 19.12 slam: radius ring plus ground warning with clear timing stages
        private static void TelegraphRing(PixelCanvas c, int size, int mid, int frame)
        {
            var col = frame == 0 ? RuinPalette.EmergencyRed : RuinPalette.Lighten(RuinPalette.EmergencyRed, 0.35f);
            var r = mid - 1;
            for (var a = 0; a < 64; a++)
            {
                var ang = a * Mathf.PI * 2f / 64f;
                var x = mid + Mathf.RoundToInt(Mathf.Cos(ang) * r);
                var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                c.Set(x, y, col);
                c.Set(mid + Mathf.RoundToInt(Mathf.Cos(ang) * (r - 1)), mid + Mathf.RoundToInt(Mathf.Sin(ang) * (r - 1)),
                    RuinPalette.Darken(col, 0.5f));
            }
            // Inner tick ring showing the wind-up stage, without filling the centre.
            if (frame > 0)
                for (var a = 0; a < 8; a++)
                {
                    var ang = a * Mathf.PI / 4f;
                    c.Line(mid + Mathf.RoundToInt(Mathf.Cos(ang) * (r - 5)), mid + Mathf.RoundToInt(Mathf.Sin(ang) * (r - 5)),
                        mid + Mathf.RoundToInt(Mathf.Cos(ang) * (r - 3)), mid + Mathf.RoundToInt(Mathf.Sin(ang) * (r - 3)), col);
                }
        }
    }
}
