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
            _ => 2   // telegraphs pulse rather than play out
        };

        public static int SizeOf(string role) => role switch
        {
            "explosion" => 32,
            "melee" => 24,
            "loot_glow" => 24,
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
                case "telegraph_zone": TelegraphBox(c, size, frame); break;
                case "telegraph_dash": TelegraphDash(c, size, mid, frame); break;
                case "telegraph_projectile": TelegraphLine(c, size, mid, frame); break;
                case "telegraph_slam": TelegraphRing(c, size, mid, frame); break;
                default: TelegraphRing(c, size, mid, frame); break;
            }

            return c;
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
