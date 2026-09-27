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

        public static int FrameCount(string role) => role switch
        {
            "muzzle" => 3,
            "impact" => 4,
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

        public static int SizeOf(string role) => role switch
        {
            "explosion" => 32,
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

            switch (role)
            {
                case "muzzle": Muzzle(c, mid, t); break;
                case "impact": Impact(c, mid, t); break;
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

        // 19.1 muzzle flash: warm white core, orange edge, 3 frames
        private static void Muzzle(PixelCanvas c, int mid, float t)
        {
            var reach = Mathf.RoundToInt(Mathf.Lerp(6f, 2f, t));
            var core = RuinPalette.Hex("#FFF3C8");
            var edge = RuinPalette.OxideOrange;

            // A forward star rather than a ball: the flash reads directionally.
            c.Line(mid, mid, mid + reach, mid, edge, 3);
            c.Line(mid, mid, mid + reach - 1, mid, core, 1);
            c.Line(mid, mid - reach / 2, mid, mid + reach / 2, edge);
            if (t < 0.6f)
            {
                c.Line(mid + 1, mid - 2, mid + reach - 1, mid - 3, edge);
                c.Line(mid + 1, mid + 2, mid + reach - 1, mid + 3, edge);
                c.Ellipse(mid, mid, 2, 2, core);
            }
        }

        // 19.2 projectile impact: compact, 4 frames
        private static void Impact(PixelCanvas c, int mid, float t)
        {
            var r = Mathf.RoundToInt(Mathf.Lerp(2f, 6f, t));
            var col = t < 0.5f ? RuinPalette.Hex("#FFE9B0") : RuinPalette.WarningOchre;
            for (var a = 0; a < 8; a++)
            {
                var ang = a * Mathf.PI / 4f;
                var x = mid + Mathf.RoundToInt(Mathf.Cos(ang) * r);
                var y = mid + Mathf.RoundToInt(Mathf.Sin(ang) * r);
                c.Set(x, y, col);
                if (t < 0.5f) c.Set(x - Mathf.RoundToInt(Mathf.Cos(ang)), y - Mathf.RoundToInt(Mathf.Sin(ang)), col);
            }
            if (t < 0.35f) c.Ellipse(mid, mid, 2, 2, RuinPalette.Hex("#FFF6DA"));
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
