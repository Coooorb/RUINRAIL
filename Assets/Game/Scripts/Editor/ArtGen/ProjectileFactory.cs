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
            /// <summary>Presentation scale bound into the catalog profile (boss volleys draw at 2x).</summary>
            public int Scale = 1;
            /// <summary>Shot feel bound into the profile: muzzle flash / impact effect kinds, their seconds, the weapon kick.</summary>
            public string MuzzleKind = string.Empty;
            public float MuzzleSeconds;
            public string ImpactKind = string.Empty;
            public float ImpactSeconds;
            public float RecoilPixels;
        }

        /// <summary>
        /// The weight ladder of the player families' shot feel (presentation only): what flashes at the muzzle, what bursts
        /// where the shot lands, how hard the held weapon kicks. Light for rapid fire, heavy for slugs and blasts, a rail
        /// for the sniper, cyan for energy. Legendary variants share their family's stack.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, (string muzzle, float muzzleSeconds, string impact, float impactSeconds, float recoil)> ShotFeel =
            new Dictionary<string, (string, float, string, float, float)>
            {
                ["proj_pistol"] = ("muzzle", 0.07f, "impact", 0.16f, 2f),
                ["proj_smg"] = ("muzzle_light", 0.05f, "impact_light", 0.12f, 1f),
                ["proj_rifle"] = ("muzzle", 0.07f, "impact", 0.16f, 2f),
                ["proj_battle_rifle"] = ("muzzle_heavy", 0.08f, "impact_heavy", 0.2f, 3f),
                ["proj_pellet"] = ("muzzle_heavy", 0.1f, "impact_light", 0.14f, 4f),
                ["proj_sniper"] = ("muzzle_rail", 0.09f, "impact_heavy", 0.22f, 3f),
                ["proj_arrow"] = ("none", 0f, "impact_light", 0.12f, 1f),
                ["proj_energy_bolt"] = ("muzzle_energy", 0.08f, "impact_energy", 0.18f, 2f),
                ["proj_rocket"] = ("muzzle_heavy", 0.1f, "impact", 0.16f, 4f)
            };

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
            ["aegis_ring_while_line"] = "proj_boss_energy",
            ["vault_stalker_frost_fan"] = "proj_enemy_frost",
            ["vault_stalker_shard_burst"] = "proj_enemy_frost",
            ["warden_sweep_fan"] = "proj_boss_frost",
            ["warden_aimed_volley"] = "proj_boss_frost",
            ["warden_emergency_purge"] = "proj_boss_frost",
            ["subject_zero_radial_burst"] = "proj_boss_frost"
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
                Add("proj_pistol" + s, legendary ? 12 : 10, 5, "punchy brass slug: outlined, hot cream core, short warm tracer" + tag);
                Add("proj_smg" + s, legendary ? 13 : 11, 5, "quick bright round: short hot body, long thin tracer, readable in a stream" + tag);
                Add("proj_rifle" + s, legendary ? 15 : 13, 5, "strong steel-cored rifle tracer with a long ochre tail" + tag);
                Add("proj_battle_rifle" + s, legendary ? 18 : 16, 7, "heavy round: thick white-hot core, amber flanks, long tail" + tag);
                Add("proj_pellet" + s, 7, 7, "chunky hot shotgun pellet: white core, amber body, dark rim" + tag, pivotX: 0.5f, frames: 2, frameSeconds: 0.05f);
                Add("proj_sniper" + s, 20, 3, "precise rail: white-hot core line with cyan flanks and a dark rim" + tag, pivotX: 0.95f);
                Add("proj_arrow" + s, 14, 3, "arrow with steel head and fletching, aligned to travel" + tag, pivotX: 0.9f);
                Add("proj_energy_bolt" + s, 12, 7, "cyan plasma bolt: white core, glowing halo, 2-frame flicker" + tag, frames: 2, pivotX: 0.75f);
                Add("proj_rocket" + s, 14, 7, "rocket body with amber nose and a flickering exhaust trail" + tag, frames: 2, pivotX: 0.8f, trail: "proj_rocket_trail" + s, trailBack: 0.4f, frameSeconds: 0.05f);
                Add("proj_rocket_trail" + s, 12, 5, "rocket exhaust flame and smoke" + tag, frames: 2, pivotX: 0.9f, frameSeconds: 0.05f);
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
            Add("proj_enemy_frost", 9, 5, "Cryo Vaults ice splinter: pale cyan with a white tip and a running glint, 2-frame", frames: 2, pivotX: 0.75f);
            Add("proj_boss_frost", 11, 5, "Cryo Vaults boss ice lance: pale cyan and frost white, ice-blue flanks, 2-frame glint", frames: 2, pivotX: 0.75f);
            // Boss volleys draw at twice their pixel size so they read at gameplay distance (the hitbox is unchanged).
            foreach (var spec in list) if (spec.Id.StartsWith("proj_boss_", System.StringComparison.Ordinal)) spec.Scale = 2;
            foreach (var spec in list)
            {
                var family = spec.Id.EndsWith(LegendarySuffix, StringComparison.Ordinal) ? spec.Id.Substring(0, spec.Id.Length - LegendarySuffix.Length) : spec.Id;
                if (!ShotFeel.TryGetValue(family, out var feel)) continue;
                spec.MuzzleKind = feel.muzzle; spec.MuzzleSeconds = feel.muzzleSeconds;
                spec.ImpactKind = feel.impact; spec.ImpactSeconds = feel.impactSeconds;
                spec.RecoilPixels = feel.recoil;
            }

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
        /// <summary>The player's energy / rail cyan: brighter than the hostile teal so a player bolt never reads as a threat.</summary>
        private static readonly Color32 PlayerCyan = RuinPalette.Hex("#7FE6F0");
        private static readonly Color32 Teal = RuinPalette.TerminalGreen;
        private static readonly Color32 IceBlue = RuinPalette.Hex("#79A9C4");
        private static readonly Color32 PaleCyan = RuinPalette.Hex("#A9DDE8");
        private static readonly Color32 FrostWhite = RuinPalette.Hex("#DCEFF4");

        public static PixelCanvas Build(string id, int frame)
        {
            var spec = Find(id) ?? throw new ArgumentException("unknown projectile profile " + id, nameof(id));
            var c = new PixelCanvas(spec.Width, spec.Height);
            var legendary = id.EndsWith(LegendarySuffix, StringComparison.Ordinal);
            var family = legendary ? id.Substring(0, id.Length - LegendarySuffix.Length) : id;
            switch (family)
            {
                case "proj_pistol": Slug(c, 1, 0, 3, Brass, legendary ? Amber : Ochre, legendary); break;
                case "proj_smg": Slug(c, 1, 0, 6, legendary ? Amber : Ochre, Ochre, legendary); break;
                case "proj_rifle": Slug(c, 1, 0, 5, Steel, Ochre, legendary); break;
                case "proj_battle_rifle": Slug(c, 2, 1, 6, legendary ? Amber : Ochre, Ochre, legendary); break;
                case "proj_pellet": Pellet(c, legendary ? Amber : Brass, frame); break;
                case "proj_sniper": PlayerRail(c, legendary ? Amber : PlayerCyan); break;
                case "proj_arrow": Arrow(c, legendary); break;
                case "proj_energy_bolt": Plasma(c, legendary ? Amber : PlayerCyan, frame); break;
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
                case "proj_enemy_frost":
                case "proj_boss_frost": IceShard(c, frame); break;
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

        /// <summary>
        /// A player round, pointing +X: an outlined capsule of <paramref name="halfBody"/> rows each side of the centre
        /// (tapering one row toward the tail when <paramref name="taper"/> is set), a hot core, a white-hot nose, and a
        /// dithered tracer tail of <paramref name="tail"/> pixels behind it. Thick enough to read as dangerous, small
        /// enough to never cover the enemy it flies at.
        /// </summary>
        private static void Slug(PixelCanvas c, int halfBody, int taper, int tail, Color32 body, Color32 tracer, bool legendary)
        {
            var w = c.Width; var mid = c.Height / 2;
            var head = w - 1;
            var start = tail; // the body runs from just after the tail to the nose
            for (var x = 0; x < start; x++)
                c.Set(x, mid, x % 2 == (start % 2) ? tracer : RuinPalette.Darken(tracer, 0.35f));
            for (var x = start; x <= head; x++)
            {
                var fromHead = head - x;
                var h = halfBody;
                if (fromHead == 0) h = Mathf.Max(0, halfBody - 1); // a rounded nose
                if (taper > 0 && x - start < (w - start) / 3) h = Mathf.Max(0, halfBody - taper);
                for (var dy = -h; dy <= h; dy++)
                {
                    var a = Mathf.Abs(dy);
                    var col = a == h && h > 0 ? body : fromHead <= 1 ? HotWhite : Core;
                    if (h == 0) col = fromHead <= 1 ? HotWhite : fromHead <= 3 ? Core : body;
                    c.Set(x, mid + dy, col);
                }

                c.Set(x, mid + h + 1, Outline);
                c.Set(x, mid - h - 1, Outline);
            }

            c.Set(head, mid, HotWhite);
            if (legendary)
            {
                // The Legendary accent: amber flanks behind the nose.
                var ax = Mathf.Max(start, head - 3);
                c.Set(ax, mid + halfBody, Amber); c.Set(ax, mid - halfBody, Amber);
                c.Set(ax - 1, mid + halfBody, Amber); c.Set(ax - 1, mid - halfBody, Amber);
            }
        }

        // A chunky hot pellet: dark rim, warm body, white core; the second frame breathes so a cloud of pellets shimmers.
        private static void Pellet(PixelCanvas c, Color32 body, int frame)
        {
            var mid = c.Width / 2;
            c.Ellipse(mid + 0.5f, mid + 0.5f, 3.4f, 3.4f, Outline);
            c.Ellipse(mid + 0.5f, mid + 0.5f, 2.5f, 2.5f, body);
            c.Ellipse(mid + 0.5f, mid + 0.5f, frame == 0 ? 1.6f : 1.2f, frame == 0 ? 1.6f : 1.2f, Core);
            c.Set(mid, mid, HotWhite);
            c.Set(mid + 1, mid + 1, frame == 0 ? HotWhite : Core);
        }

        // Player plasma: a soft halo round a white core, outlined, flickering between a long and a short body.
        private static void Plasma(PixelCanvas c, Color32 halo, int frame)
        {
            var w = c.Width; var mid = c.Height / 2;
            var len = frame == 0 ? w - 1 : w - 2;
            var cx = len * 0.6f;
            c.Ellipse(cx, mid + 0.5f, len * 0.5f, 3.4f, Outline);
            c.Ellipse(cx, mid + 0.5f, len * 0.5f - 1f, 2.5f, RuinPalette.Darken(halo, 0.25f));
            c.Ellipse(cx + 0.5f, mid + 0.5f, len * 0.5f - 2f, 1.6f, halo);
            for (var x = Mathf.RoundToInt(cx) - 1; x < len - 1; x++) c.Set(x, mid, Core);
            c.Set(len - 2, mid, HotWhite); c.Set(len - 3, mid, HotWhite);
            // A spark trailing the bolt.
            c.Set(0, mid + (frame == 0 ? 1 : -1), halo);
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

        // The player's sniper rail: a white-hot core line over most of its length, coloured flanks, a dark fading tail.
        private static void PlayerRail(PixelCanvas c, Color32 body)
        {
            var w = c.Width; var mid = c.Height / 2;
            for (var x = 0; x < w; x++)
            {
                var t = (float)x / (w - 1);
                c.Set(x, mid, t > 0.4f ? HotWhite : t > 0.15f ? body : RuinPalette.Darken(body, 0.55f));
                if (t > 0.3f && t < 0.97f)
                {
                    var flank = t > 0.6f ? body : RuinPalette.Darken(body, 0.3f);
                    c.Set(x, mid - 1, flank);
                    c.Set(x, mid + 1, flank);
                }
            }

            c.Set(0, mid, Outline);
            c.Set(1, mid, RuinPalette.Darken(body, 0.7f));
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

        /// <summary>
        /// An elongated ice splinter along the travel axis: widest a third of the way back from the tip, tapering to a
        /// point at both ends, ice-blue flanks round a pale-cyan body, a white tip and a glint that runs down the body
        /// on alternate frames. Cold where every other hostile round is warm or toxic, outlined so it holds on frost.
        /// </summary>
        private static void IceShard(PixelCanvas c, int frame)
        {
            var w = c.Width; var h = c.Height; var mid = h / 2;
            var widest = (w - 1) * 0.62f;
            for (var x = 0; x < w; x++)
            {
                var t = x <= widest ? x / widest : (w - 1 - x) / (w - 1 - widest);
                var half = Mathf.Clamp(Mathf.RoundToInt(t * (mid - 1)), 0, mid - 1);
                for (var dy = -half; dy <= half; dy++)
                    c.Set(x, mid + dy, Mathf.Abs(dy) == half && half > 0 ? IceBlue : PaleCyan);
                if (mid - half - 1 >= 0) c.Set(x, mid - half - 1, Outline);
                if (mid + half + 1 < h) c.Set(x, mid + half + 1, Outline);
            }

            c.Set(w - 1, mid, HotWhite);
            c.Set(w - 2, mid, FrostWhite);
            var glint = frame == 0 ? (int)widest : (int)widest - 3;
            if (glint > 0) c.Set(glint, mid, HotWhite);
            c.Set(0, mid, IceBlue);
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
