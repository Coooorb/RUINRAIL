using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Original RUINRAIL pixel projectile art: one small in-flight sprite (or a 2-frame flicker) per profile, authored
    /// pointing +X, sized to read at 640×360 without covering what it flies at. Every sprite has a near-white core pixel
    /// and a dark outline so it stays legible on the bright metro concrete, the rust floors and the green lab tiles
    /// alike. Player families stay in the warm brass/steel language; Legendary variants add the amber accent and a
    /// longer tracer; hostile rounds are muted red/orange, lab projectiles a restrained sickly green, industrial bolts
    /// amber/red, and the Boss profiles are larger and attack-specific — the established telegraph colour language.
    /// </summary>
    public static class ProjectileFactory
    {
        public sealed class Spec
        {
            public string Id;
            public int Width;
            public int Height;
            public int Frames = 1;
            public float FrameSeconds = 0.06f;
            /// <summary>Pivot x (0..1): tracers pivot near their head so nothing draws ahead of the physics point.</summary>
            public float PivotX = 0.85f;
            /// <summary>Optional exhaust/tail sprite id drawn behind the body.</summary>
            public string TrailId;
            public float TrailBack = 0.3f;
            public string Description = string.Empty;
        }

        /// <summary>Family default profile per ranged weapon class.</summary>
        public static readonly IReadOnlyDictionary<WeaponClass, string> FamilyDefaults = new Dictionary<WeaponClass, string>
        {
            [WeaponClass.Pistol] = "proj_pistol",
            [WeaponClass.Smg] = "proj_smg",
            [WeaponClass.AssaultRifle] = "proj_rifle",
            [WeaponClass.BattleRifle] = "proj_battle_rifle",
            [WeaponClass.Shotgun] = "proj_pellet",
            [WeaponClass.Sniper] = "proj_sniper",
            [WeaponClass.Bow] = "proj_arrow",
            [WeaponClass.Blaster] = "proj_energy_bolt",
            [WeaponClass.RocketLauncher] = "proj_rocket"
        };

        public const string LegendarySuffix = "_legendary";
        public const string PlayerDefault = "proj_rifle";
        public const string EnemyDefault = "proj_enemy_round";

        /// <summary>Per-archetype hostile profiles (normal enemies with a projectile attack).</summary>
        public static readonly IReadOnlyDictionary<string, string> EnemyProfiles = new Dictionary<string, string>
        {
            ["shooter"] = "proj_enemy_round",
            ["summoner"] = "proj_enemy_round",
            ["sniper_enemy"] = "proj_enemy_sniper"
        };

        /// <summary>Per-attack hostile profiles (Elite / Boss projectile attacks, by attack id).</summary>
        public static readonly IReadOnlyDictionary<string, string> AttackProfiles = new Dictionary<string, string>
        {
            ["railguard_burst_cannon"] = "proj_enemy_rail",
            ["railguard_rail_sweep"] = "proj_enemy_rail",
            ["crusher_scrap_barrage"] = "proj_enemy_scrap",
            ["prototype_x7_energy_burst"] = "proj_enemy_energy",
            ["prototype_x7_broad_salvo"] = "proj_enemy_energy",
            ["prototype_x7_radial_pulse"] = "proj_enemy_energy",
            ["scrapking_auto_burst"] = "proj_boss_scrap",
            ["conductor_burst_cannon"] = "proj_boss_arc",
            ["conductor_projectile_sweep"] = "proj_boss_arc",
            ["titan_furnace_blast"] = "proj_boss_furnace",
            ["omega_spore_burst"] = "proj_boss_spore",
            ["aegis_triple_burst"] = "proj_boss_energy",
            ["aegis_radial_ring"] = "proj_boss_energy",
            ["aegis_ring_while_line"] = "proj_boss_energy"
        };

        public static IReadOnlyList<Spec> Specs()
        {
            var list = new List<Spec>();
            void Add(string id, int w, int h, string description, int frames = 1, float pivotX = 0.85f, string trail = null, float trailBack = 0.3f, float frameSeconds = 0.06f) =>
                list.Add(new Spec { Id = id, Width = w, Height = h, Frames = frames, PivotX = pivotX, TrailId = trail, TrailBack = trailBack, FrameSeconds = frameSeconds, Description = description });

            // Player families and their Legendary variants.
            foreach (var legendary in new[] { false, true })
            {
                var s = legendary ? LegendarySuffix : string.Empty;
                var tag = legendary ? " (Legendary: amber accent, longer tracer)" : string.Empty;
                Add("proj_pistol" + s, legendary ? 10 : 8, 3, "small conventional bullet with a short warm tracer" + tag);
                Add("proj_smg" + s, legendary ? 9 : 7, 2, "small fast-looking thin tracer" + tag);
                Add("proj_rifle" + s, legendary ? 12 : 10, 3, "medium rifle tracer" + tag);
                Add("proj_battle_rifle" + s, legendary ? 14 : 12, 3, "heavier, brighter rifle projectile" + tag);
                Add("proj_pellet" + s, 3, 3, "one readable shotgun pellet" + tag, pivotX: 0.5f);
                Add("proj_sniper" + s, legendary ? 20 : 16, 1, "thin high-contrast fast tracer" + tag, pivotX: 0.95f);
                Add("proj_arrow" + s, 14, 3, "arrow with steel head and fletching, aligned to travel" + tag, pivotX: 0.9f);
                Add("proj_energy_bolt" + s, 8, 4, "distinct cyan energy bolt, 2-frame flicker" + tag, frames: 2, pivotX: 0.75f);
                Add("proj_rocket" + s, 12, 5, "rocket body with amber nose and a flickering exhaust trail" + tag, frames: 2, pivotX: 0.8f, trail: "proj_rocket_trail" + s, trailBack: 0.34f, frameSeconds: 0.05f);
                Add("proj_rocket_trail" + s, 8, 3, "rocket exhaust puff" + tag, frames: 2, pivotX: 0.9f, frameSeconds: 0.05f);
            }

            // Hostile.
            Add("proj_enemy_round", 8, 3, "hostile ballistic round: muted red/orange tracer, bright core");
            Add("proj_enemy_sniper", 14, 1, "hostile sniper: thin red tracer", pivotX: 0.95f);
            Add("proj_enemy_rail", 10, 3, "Railguard industrial rail bolt: amber/red");
            Add("proj_enemy_scrap", 5, 5, "Crusher scrap shard: rust chunk", pivotX: 0.6f);
            Add("proj_enemy_energy", 8, 4, "Prototype X-7 lab energy: restrained sickly green, 2-frame flicker", frames: 2, pivotX: 0.75f);
            Add("proj_boss_scrap", 10, 4, "Scrap King heavy round: red/orange with a hot core", pivotX: 0.8f);
            Add("proj_boss_arc", 10, 5, "The Conductor's arc bolt: amber/red electric, 2-frame flicker", frames: 2, pivotX: 0.75f);
            Add("proj_boss_furnace", 7, 7, "Foundry Titan ember: molten chunk with a dark crust, 2-frame glow", frames: 2, pivotX: 0.6f, frameSeconds: 0.08f);
            Add("proj_boss_spore", 6, 6, "Subject Omega spore: toxic green bulb with a pale core, 2-frame pulse", frames: 2, pivotX: 0.6f, frameSeconds: 0.09f);
            Add("proj_boss_energy", 9, 5, "Aegis Core lab energy: teal-green bolt with a white core, 2-frame flicker", frames: 2, pivotX: 0.75f);
            return list;
        }

        public static Spec Find(string id)
        {
            foreach (var spec in Specs()) if (spec.Id == id) return spec;
            return null;
        }

        // ---- drawing ----

        private static readonly Color32 Core = RuinPalette.Hex("#FFF6DA");
        private static readonly Color32 HotWhite = RuinPalette.Hex("#FFFFFF");
        private static readonly Color32 Brass = RuinPalette.Hex("#D9B25C");
        private static readonly Color32 Ochre = RuinPalette.WarningOchre;
        private static readonly Color32 Amber = RuinPalette.AmberActive;
        private static readonly Color32 Steel = RuinPalette.PaleSteel;
        private static readonly Color32 DarkSteel = RuinPalette.DarkSteel;
        private static readonly Color32 Outline = RuinPalette.OutlineCharcoal;
        private static readonly Color32 HostileRed = RuinPalette.EmergencyRed;
        private static readonly Color32 HostileOrange = RuinPalette.OxideOrange;
        private static readonly Color32 HostileDark = RuinPalette.BurntRustDark;
        private static readonly Color32 Toxic = RuinPalette.PaleToxic;
        private static readonly Color32 ToxicDim = RuinPalette.SickGreen;
        private static readonly Color32 Cyan = RuinPalette.ElectricCyan;
        private static readonly Color32 Teal = RuinPalette.TerminalGreen;

        public static PixelCanvas Build(string id, int frame)
        {
            var spec = Find(id) ?? throw new ArgumentException("unknown projectile profile " + id, nameof(id));
            var c = new PixelCanvas(spec.Width, spec.Height);
            var legendary = id.EndsWith(LegendarySuffix, StringComparison.Ordinal);
            var family = legendary ? id.Substring(0, id.Length - LegendarySuffix.Length) : id;
            switch (family)
            {
                case "proj_pistol": Tracer(c, Brass, Ochre, legendary); break;
                case "proj_smg": ThinTracer(c, Brass, legendary); break;
                case "proj_rifle": Tracer(c, Steel, Ochre, legendary); break;
                case "proj_battle_rifle": HeavyTracer(c, Steel, Ochre, legendary); break;
                case "proj_pellet": Pellet(c, legendary ? Amber : Brass); break;
                case "proj_sniper": Rail(c, legendary ? Amber : Cyan, HotWhite); break;
                case "proj_arrow": Arrow(c, legendary); break;
                case "proj_energy_bolt": Bolt(c, legendary ? Amber : Cyan, Core, frame); break;
                case "proj_rocket": Rocket(c, legendary, frame); break;
                case "proj_rocket_trail": Exhaust(c, legendary ? Amber : Ochre, frame); break;
                case "proj_enemy_round": Tracer(c, HostileOrange, HostileRed, false, hostile: true); break;
                case "proj_enemy_sniper": Rail(c, HostileRed, Core); break;
                case "proj_enemy_rail": HeavyTracer(c, Amber, HostileRed, false, hostile: true); break;
                case "proj_enemy_scrap": Shard(c, RuinPalette.Rust, HostileOrange); break;
                case "proj_enemy_energy": Bolt(c, ToxicDim, Toxic, frame); break;
                case "proj_boss_scrap": HeavyTracer(c, HostileOrange, HostileRed, false, hostile: true); break;
                case "proj_boss_arc": Arc(c, frame); break;
                case "proj_boss_furnace": Ember(c, frame); break;
                case "proj_boss_spore": Spore(c, frame); break;
                case "proj_boss_energy": Bolt(c, Teal, Core, frame, wide: true); break;
                default: Tracer(c, Steel, Ochre, false); break;
            }

            return c;
        }

        // A conventional bullet: bright head, body colour, tracer fading toward the tail, dark outline top/bottom.
        private static void Tracer(PixelCanvas c, Color32 body, Color32 tracer, bool legendary, bool hostile = false)
        {
            var w = c.Width; var mid = c.Height / 2;
            var head = w - 1;
            for (var x = 1; x < head; x++) c.Set(x, mid, x >= head - 3 ? body : (x % 2 == 0 ? tracer : RuinPalette.Darken(tracer, 0.35f)));
            c.Set(head, mid, hostile ? Core : HotWhite);
            c.Set(head - 1, mid, Core);
            if (c.Height >= 3)
            {
                for (var x = head - 3; x <= head; x++) { c.Set(x, mid + 1, hostile ? HostileDark : Outline); c.Set(x, mid - 1, hostile ? HostileDark : Outline); }
                if (legendary) { c.Set(head - 2, mid + 1, Amber); c.Set(head - 2, mid - 1, Amber); }
            }
        }

        private static void ThinTracer(PixelCanvas c, Color32 body, bool legendary)
        {
            var w = c.Width;
            for (var x = 0; x < w - 2; x++) c.Set(x, 0, x % 2 == 0 ? Ochre : RuinPalette.Darken(Ochre, 0.4f));
            c.Set(w - 2, 0, body);
            c.Set(w - 1, 0, HotWhite);
            if (c.Height > 1) { c.Set(w - 1, 1, Outline); c.Set(w - 2, 1, legendary ? Amber : Outline); }
        }

        private static void HeavyTracer(PixelCanvas c, Color32 body, Color32 tracer, bool legendary, bool hostile = false)
        {
            var w = c.Width; var mid = c.Height / 2;
            for (var x = 0; x < w - 3; x++) { c.Set(x, mid, tracer); if (x % 2 == 1) c.Set(x, mid + 1, RuinPalette.Darken(tracer, 0.3f)); }
            for (var x = w - 3; x < w; x++) { c.Set(x, mid, body); c.Set(x, mid + 1, RuinPalette.Darken(body, 0.25f)); }
            c.Set(w - 1, mid, hostile ? Core : HotWhite);
            c.Set(w - 2, mid, Core);
            if (mid - 1 >= 0) for (var x = w - 4; x < w; x++) c.Set(x, mid - 1, hostile ? HostileDark : Outline);
            if (legendary) { c.Set(w - 3, mid + 1, Amber); c.Set(w - 4, mid, Amber); }
        }

        private static void Pellet(PixelCanvas c, Color32 body)
        {
            c.Set(1, 1, HotWhite);
            c.Set(0, 1, body); c.Set(2, 1, body); c.Set(1, 0, body); c.Set(1, 2, body);
            c.Set(0, 0, Outline); c.Set(2, 0, Outline); c.Set(0, 2, Outline); c.Set(2, 2, Outline);
        }

        // A one-pixel rail: bright head, colour body, fading tail (sniper).
        private static void Rail(PixelCanvas c, Color32 body, Color32 head)
        {
            var w = c.Width;
            for (var x = 0; x < w; x++)
            {
                var t = (float)x / (w - 1);
                c.Set(x, 0, t > 0.85f ? head : t > 0.35f ? body : RuinPalette.Darken(body, 0.45f));
            }

            // A dark tail end: the rail stays readable over a bright floor as well as a dark one.
            c.Set(0, 0, Outline);
            c.Set(1, 0, RuinPalette.Darken(body, 0.7f));
        }

        private static void Arrow(PixelCanvas c, bool legendary)
        {
            var w = c.Width;
            for (var x = 3; x < w - 3; x++) c.Set(x, 1, x % 2 == 0 ? DarkSteel : RuinPalette.MidSteel);
            c.Set(w - 3, 1, Steel); c.Set(w - 2, 1, Core); c.Set(w - 1, 1, HotWhite);
            c.Set(w - 3, 0, Outline); c.Set(w - 3, 2, Outline);
            var fletch = legendary ? Amber : RuinPalette.Olive;
            c.Set(0, 0, fletch); c.Set(1, 1, fletch); c.Set(0, 2, fletch); c.Set(2, 0, RuinPalette.Darken(fletch, 0.3f)); c.Set(2, 2, RuinPalette.Darken(fletch, 0.3f));
        }

        // Energy bolt: elongated core with a coloured halo, flickering between two shapes.
        private static void Bolt(PixelCanvas c, Color32 halo, Color32 core, int frame, bool wide = false)
        {
            var w = c.Width; var h = c.Height; var mid = h / 2;
            var halfLen = frame == 0 ? w - 2 : w - 3;
            for (var x = 1; x < halfLen; x++) c.Set(x, mid, halo);
            for (var x = 2; x < halfLen; x++) { c.Set(x, mid - 1, RuinPalette.Darken(halo, 0.35f)); if (mid + 1 < h) c.Set(x, mid + 1, RuinPalette.Darken(halo, 0.35f)); }
            for (var x = halfLen - 3; x < halfLen; x++) c.Set(x, mid, core);
            c.Set(halfLen, mid, HotWhite);
            if (wide && mid + 1 < h) { c.Set(halfLen - 2, mid + 1, core); c.Set(halfLen - 2, mid - 1, core); }
            if (frame == 1) { c.Set(1, mid - 1, Outline); if (mid + 1 < h) c.Set(1, mid + 1, Outline); }
            for (var x = halfLen - 2; x <= halfLen; x++) { if (mid - 1 >= 0 && !c.IsOpaque(x, mid - 1)) c.Set(x, mid - 1, Outline); if (mid + 1 < h && !c.IsOpaque(x, mid + 1)) c.Set(x, mid + 1, Outline); }
        }

        private static void Rocket(PixelCanvas c, bool legendary, int frame)
        {
            var w = c.Width; var mid = c.Height / 2;
            // Body: steel tube with a dark underside, rust band, amber nose cone.
            c.Rect(2, mid - 1, w - 5, 3, DarkSteel);
            for (var x = 3; x < w - 3; x++) c.Set(x, mid, Steel);
            c.Set(4, mid + 1, RuinPalette.Rust); c.Set(5, mid + 1, RuinPalette.Rust);
            c.Set(w - 3, mid, legendary ? Amber : Ochre); c.Set(w - 3, mid - 1, Outline); c.Set(w - 3, mid + 1, Outline);
            c.Set(w - 2, mid, HotWhite); c.Set(w - 1, mid, legendary ? Amber : Core);
            // Fins.
            c.Set(2, mid - 2, Outline); c.Set(2, mid + 2, Outline); c.Set(3, mid - 2, DarkSteel); c.Set(3, mid + 2, DarkSteel);
            // Exhaust glow at the tail flickers.
            c.Set(1, mid, frame == 0 ? HotWhite : Ochre); c.Set(0, mid, frame == 0 ? Ochre : RuinPalette.OxideOrange);
            for (var x = 2; x < w - 3; x++) { c.Set(x, mid - 2, c.IsOpaque(x, mid - 2) ? c.Get(x, mid - 2) : Outline); c.Set(x, mid + 2, c.IsOpaque(x, mid + 2) ? c.Get(x, mid + 2) : Outline); }
        }

        private static void Exhaust(PixelCanvas c, Color32 hot, int frame)
        {
            var w = c.Width; var mid = c.Height / 2;
            var len = frame == 0 ? w - 1 : w - 3;
            for (var x = w - len; x < w; x++)
            {
                var t = (float)(x - (w - len)) / Mathf.Max(1, len - 1);
                c.Set(x, mid, t > 0.66f ? HotWhite : t > 0.33f ? hot : RuinPalette.OxideOrange);
            }

            c.Set(w - 2, mid - 1, hot); c.Set(w - 2, mid + 1, hot);
            c.Set(w - len, mid, RuinPalette.MidSteel); // smoke at the very tail
        }

        private static void Shard(PixelCanvas c, Color32 body, Color32 edge)
        {
            c.Set(2, 2, HotWhite);
            c.Set(1, 2, edge); c.Set(3, 2, edge); c.Set(2, 1, body); c.Set(2, 3, body); c.Set(3, 3, body); c.Set(1, 1, body);
            c.Set(4, 2, Core); c.Set(0, 2, RuinPalette.BurntRustDark);
            c.Set(1, 0, Outline); c.Set(3, 0, Outline); c.Set(0, 1, Outline); c.Set(4, 1, Outline); c.Set(0, 3, Outline); c.Set(4, 3, Outline); c.Set(1, 4, Outline); c.Set(3, 4, Outline);
        }

        // Conductor: a jagged amber/red arc with a white core, alternating zigzag each frame.
        private static void Arc(PixelCanvas c, int frame)
        {
            var w = c.Width; var mid = c.Height / 2;
            for (var x = 0; x < w; x++)
            {
                var y = mid + ((x + frame) % 3 == 0 ? 1 : (x + frame) % 3 == 1 ? -1 : 0);
                c.Set(x, y, x >= w - 3 ? Core : x % 2 == 0 ? Amber : HostileRed);
                c.Set(x, mid, x >= w - 2 ? HotWhite : RuinPalette.Darken(Amber, 0.3f));
            }

            c.Set(w - 1, mid - 1, HostileDark); c.Set(w - 1, mid + 1, HostileDark);
            c.Set(0, mid - 1, HostileDark); c.Set(0, mid + 1, HostileDark);
        }

        // Titan: a molten ember chunk — dark crust with glowing cracks and a hot core that breathes.
        private static void Ember(PixelCanvas c, int frame)
        {
            var mid = c.Width / 2;
            c.Ellipse(mid, mid, 3f, 3f, HostileDark);
            c.Ellipse(mid, mid, 2f, 2f, HostileRed);
            c.Ellipse(mid, mid, frame == 0 ? 1.2f : 0.8f, frame == 0 ? 1.2f : 0.8f, Ochre);
            c.Set(mid, mid, HotWhite);
            c.Set(mid + 2, mid, frame == 0 ? Core : Ochre); c.Set(mid - 1, mid + 2, Ochre);
            c.Set(mid, mid + 3, Outline); c.Set(mid, mid - 3, Outline); c.Set(mid + 3, mid, Outline); c.Set(mid - 3, mid, Outline);
        }

        // Omega: a toxic spore bulb — sick green skin, pale toxic veins, a pale core that pulses.
        private static void Spore(PixelCanvas c, int frame)
        {
            var mid = c.Width / 2;
            c.Ellipse(mid, mid, 2.5f, 2.5f, RuinPalette.DeepOlive);
            c.Ellipse(mid, mid, 1.8f, 1.8f, ToxicDim);
            c.Set(mid, mid, frame == 0 ? Core : Toxic);
            c.Set(mid + 1, mid, Toxic); c.Set(mid, mid + 1, Toxic); c.Set(mid - 1, mid - 1, frame == 0 ? Toxic : ToxicDim);
            c.Set(mid + 2, mid + 1, Outline); c.Set(mid - 2, mid - 1, Outline);
            c.Set(mid, mid - 2, Outline); c.Set(mid - 2, mid + 1, Outline);
        }
    }
}
