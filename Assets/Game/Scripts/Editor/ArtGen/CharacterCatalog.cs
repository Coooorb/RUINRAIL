using System.Collections.Generic;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// The 22 character design profiles, transcribed from FINAL_ART_PRODUCTION_SPEC sections 6-9.
    ///
    /// Ids match the runtime actor ids (EnemyDefinition/EliteDefinition/BossDefinition), because the animation
    /// pipeline binds a CharacterAnimationSet by ActorId. Every palette choice below is quoted from the spec's
    /// per-archetype palette list rather than invented.
    /// </summary>
    public static class CharacterCatalog
    {
        // ---- 6. PLAYER ----
        public static CharacterProfile Player() => new()
        {
            Id = "player", DisplayName = "Player",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 5, HeadRadiusY = 5,
            ShoulderHalfWidth = 8, TorsoHalfWidth = 6, HipHalfWidth = 5,
            TorsoTop = 31, TorsoBottom = 18, LegBottom = 6,
            // Dominant charcoal/olive cloth, secondary worn steel plate, one amber recognition accent (spec 6).
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#3A4436")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#4A5254")),
            Accent = RuinPalette.RampOf(RuinPalette.Hex("#A9702F")),
            Skin = RuinPalette.FleshRamp,
            Emissive = RuinPalette.AmberActive,
            HeadGear = 4, BackpackSize = 9, Grime = 0.05f, Seed = 101,
            ExpectedContentHeightMin = 34, ExpectedContentHeightMax = 46
        };

        // ---- 7. NORMAL ENEMIES ----
        public static CharacterProfile Grunt() => new()
        {
            Id = "grunt", DisplayName = "Grunt",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 5, HeadRadiusY = 5,
            ShoulderHalfWidth = 9, TorsoHalfWidth = 7, HipHalfWidth = 5,
            TorsoTop = 30, TorsoBottom = 17, LegBottom = 6,
            // dark brown / dirty olive / steel, rusty orange identification accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#463A2C")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#4B5556")),
            Accent = RuinPalette.RampOf(RuinPalette.OxideOrange),
            HeadGear = 1, ForwardLean = 2, BulkBoost = 1, Grime = 0.09f, Seed = 201,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile Shooter() => new()
        {
            Id = "shooter", DisplayName = "Shooter",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 4, HeadRadiusY = 5,
            ShoulderHalfWidth = 7, TorsoHalfWidth = 5, HipHalfWidth = 4,
            TorsoTop = 31, TorsoBottom = 18, LegBottom = 6,
            // desaturated blue-gray / dirty tan / steel, small amber ranged-tech accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#4C5A63")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#8A7B5E")),
            Accent = RuinPalette.RampOf(RuinPalette.AmberActive),
            HeadGear = 3, CarriedWeaponLength = 8, Grime = 0.05f, Seed = 202,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile Swarm() => new()
        {
            Id = "swarm", DisplayName = "Swarm",
            // Spec 7.3: clearly smaller, low centre of gravity, never confused with the player.
            CanvasWidth = 24, CanvasHeight = 24,
            HeadRadiusX = 4, HeadRadiusY = 3,
            ShoulderHalfWidth = 6, TorsoHalfWidth = 5, HipHalfWidth = 4,
            TorsoTop = 13, TorsoBottom = 7, LegBottom = 3, ArmThickness = 2,
            // dark sick green / gray-brown / pale contamination accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#3C4A32")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#5A5348")),
            Accent = RuinPalette.RampOf(RuinPalette.PaleToxic),
            Skin = RuinPalette.SickFleshRamp,
            Emissive = RuinPalette.PaleToxic,
            HeadGear = 0, Hunched = true, Grime = 0.08f, Seed = 203,
            ExpectedContentHeightMin = 12, ExpectedContentHeightMax = 22
        };

        public static CharacterProfile Charger() => new()
        {
            Id = "charger", DisplayName = "Charger",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 4, HeadRadiusY = 4,
            ShoulderHalfWidth = 10, TorsoHalfWidth = 7, HipHalfWidth = 5,
            TorsoTop = 29, TorsoBottom = 17, LegBottom = 6,
            // steel / dark red-brown / warning ochre
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#5A3A30")),
            Secondary = RuinPalette.RampOf(RuinPalette.MidSteel),
            Accent = RuinPalette.RampOf(RuinPalette.WarningOchre),
            HeadGear = 2, ForwardLean = 4, BulkBoost = 2, WarningStripes = true, Grime = 0.07f, Seed = 204,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile Brute() => new()
        {
            Id = "brute", DisplayName = "Brute",
            // Spec 4.3 / 7.5: significantly larger than the player.
            CanvasWidth = 48, CanvasHeight = 64,
            HeadRadiusX = 6, HeadRadiusY = 5,
            ShoulderHalfWidth = 14, TorsoHalfWidth = 11, HipHalfWidth = 8,
            TorsoTop = 42, TorsoBottom = 22, LegBottom = 7, ArmThickness = 5,
            // deep steel / concrete gray / rust / warning yellow details
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#3E4749")),
            Secondary = RuinPalette.RampOf(RuinPalette.Concrete),
            Accent = RuinPalette.RampOf(RuinPalette.Rust),
            HeadGear = 2, BulkBoost = 3, WarningStripes = true, Grime = 0.1f, Seed = 205,
            ExpectedContentHeightMin = 44, ExpectedContentHeightMax = 62
        };

        public static CharacterProfile Bomber() => new()
        {
            Id = "bomber", DisplayName = "Bomber",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 5, HeadRadiusY = 5,
            ShoulderHalfWidth = 8, TorsoHalfWidth = 6, HipHalfWidth = 5,
            TorsoTop = 30, TorsoBottom = 18, LegBottom = 6,
            // dirty tan / olive / oxide orange / emergency-red explosive markings
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#7A6A4C")),
            Secondary = RuinPalette.RampOf(RuinPalette.Olive),
            Accent = RuinPalette.RampOf(RuinPalette.EmergencyRed),
            Emissive = RuinPalette.EmergencyRed,
            HeadGear = 1, BackpackSize = 13, CoreRadius = 2, Grime = 0.06f, Seed = 206,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile ShieldEnemy() => new()
        {
            Id = "shield_enemy", DisplayName = "Shield",
            CanvasWidth = 36, CanvasHeight = 48,
            HeadRadiusX = 4, HeadRadiusY = 4,
            ShoulderHalfWidth = 8, TorsoHalfWidth = 6, HipHalfWidth = 5,
            TorsoTop = 30, TorsoBottom = 17, LegBottom = 6,
            // dark steel / worn paint / warning stripe remnants
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#3A4144")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#56605F")),
            Accent = RuinPalette.RampOf(RuinPalette.DirtyYellow),
            HeadGear = 2, FrontShield = true, Grime = 0.08f, Seed = 207,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile SniperEnemy() => new()
        {
            Id = "sniper_enemy", DisplayName = "Sniper",
            CanvasWidth = 36, CanvasHeight = 48,
            HeadRadiusX = 4, HeadRadiusY = 4,
            ShoulderHalfWidth = 6, TorsoHalfWidth = 5, HipHalfWidth = 4,
            TorsoTop = 32, TorsoBottom = 19, LegBottom = 6, ArmThickness = 2,
            // desaturated gray-green / dark cloth / cold blue optic accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#414A42")),
            Secondary = RuinPalette.RampOf(RuinPalette.Charcoal),
            Accent = RuinPalette.RampOf(RuinPalette.ColdBlue),
            Emissive = RuinPalette.ColdBlue,
            HeadGear = 3, CarriedWeaponLength = 13, Grime = 0.04f, Seed = 208,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 46
        };

        public static CharacterProfile Summoner() => new()
        {
            Id = "summoner", DisplayName = "Summoner",
            CanvasWidth = 32, CanvasHeight = 48,
            HeadRadiusX = 5, HeadRadiusY = 5,
            ShoulderHalfWidth = 7, TorsoHalfWidth = 6, HipHalfWidth = 5,
            TorsoTop = 31, TorsoBottom = 18, LegBottom = 6,
            // dirty lab/industrial neutrals, toxic green / terminal cyan emissive accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#565B52")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#6B6E64")),
            Accent = RuinPalette.RampOf(RuinPalette.TerminalGreen),
            Emissive = RuinPalette.TerminalGreen,
            HeadGear = 1, BackpackSize = 12, AntennaHeight = 6, CoreRadius = 2, Grime = 0.05f, Seed = 209,
            ExpectedContentHeightMin = 32, ExpectedContentHeightMax = 47
        };

        // ---- 8. ELITES (1.15x-1.45x normal mass, spec 4.4) ----
        public static CharacterProfile TunnelStalker() => new()
        {
            Id = "elite_tunnel_stalker", DisplayName = "Tunnel Stalker",
            CanvasWidth = 40, CanvasHeight = 56,
            HeadRadiusX = 4, HeadRadiusY = 4,
            ShoulderHalfWidth = 8, TorsoHalfWidth = 6, HipHalfWidth = 5,
            TorsoTop = 34, TorsoBottom = 19, LegBottom = 6,
            // charcoal / oxidized steel / muted bone-gray, dim red sensor accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#23292B")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#6E6A60")),
            Accent = RuinPalette.RampOf(RuinPalette.Hex("#8E8375")),
            Emissive = RuinPalette.EmergencyRed,
            Outline = RuinPalette.OutlineCharcoal,
            HeadGear = 3, Hunched = true, BladeArm = true, ForwardLean = 3, Grime = 0.07f, Seed = 301,
            ExpectedContentHeightMin = 34, ExpectedContentHeightMax = 54
        };

        public static CharacterProfile Railguard() => new()
        {
            Id = "elite_railguard", DisplayName = "Railguard",
            CanvasWidth = 44, CanvasHeight = 56,
            HeadRadiusX = 5, HeadRadiusY = 4,
            ShoulderHalfWidth = 13, TorsoHalfWidth = 10, HipHalfWidth = 8,
            TorsoTop = 36, TorsoBottom = 20, LegBottom = 7, ArmThickness = 4,
            // steel / faded transit blue-green / warning yellow / red indicator
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#37504F")),
            Secondary = RuinPalette.RampOf(RuinPalette.MidSteel),
            Accent = RuinPalette.RampOf(RuinPalette.WarningOchre),
            Emissive = RuinPalette.EmergencyRed,
            HeadGear = 2, Machine = true, BulkBoost = 2, WarningStripes = true, CoreRadius = 2, CarriedWeaponLength = 9,
            Grime = 0.07f, Seed = 302,
            ExpectedContentHeightMin = 36, ExpectedContentHeightMax = 54
        };

        public static CharacterProfile ScrapExecutioner() => new()
        {
            Id = "elite_scrap_executioner", DisplayName = "Scrap Executioner",
            CanvasWidth = 44, CanvasHeight = 56,
            HeadRadiusX = 5, HeadRadiusY = 4,
            ShoulderHalfWidth = 12, TorsoHalfWidth = 9, HipHalfWidth = 7,
            TorsoTop = 36, TorsoBottom = 20, LegBottom = 7, ArmThickness = 5,
            // soot black / rust / furnace orange / dirty steel
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#241F1C")),
            Secondary = RuinPalette.RampOf(RuinPalette.Rust),
            Accent = RuinPalette.RampOf(RuinPalette.OxideOrange),
            Emissive = RuinPalette.OxideOrange,
            Outline = RuinPalette.OutlineRustBlack,
            HeadGear = 4, BulkBoost = 2, OvergrownArm = true, Grime = 0.11f, Seed = 303,
            ExpectedContentHeightMin = 36, ExpectedContentHeightMax = 54
        };

        public static CharacterProfile CrusherUnit() => new()
        {
            Id = "elite_crusher_unit", DisplayName = "Crusher Unit",
            CanvasWidth = 48, CanvasHeight = 56,
            HeadRadiusX = 4, HeadRadiusY = 3,
            ShoulderHalfWidth = 15, TorsoHalfWidth = 11, HipHalfWidth = 9,
            TorsoTop = 34, TorsoBottom = 19, LegBottom = 7, ArmThickness = 6,
            // dark steel / worn yellow paint / oxide red / hot orange active elements
            Primary = RuinPalette.RampOf(RuinPalette.DarkSteel),
            Secondary = RuinPalette.RampOf(RuinPalette.DirtyYellow),
            Accent = RuinPalette.RampOf(RuinPalette.Hex("#8E3A2A")),
            Emissive = RuinPalette.OxideOrange,
            HeadGear = 2, Machine = true, BulkBoost = 3, WarningStripes = true, CoreRadius = 2, Grime = 0.1f, Seed = 304,
            ExpectedContentHeightMin = 34, ExpectedContentHeightMax = 54
        };

        public static CharacterProfile MutatedBrute() => new()
        {
            Id = "elite_mutated_brute", DisplayName = "Mutated Brute",
            CanvasWidth = 48, CanvasHeight = 60,
            HeadRadiusX = 5, HeadRadiusY = 5,
            ShoulderHalfWidth = 13, TorsoHalfWidth = 10, HipHalfWidth = 8,
            TorsoTop = 40, TorsoBottom = 21, LegBottom = 7, ArmThickness = 5,
            // gray flesh/cloth / deep olive / toxic yellow-green / lab-white remnants
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#6B6A5E")),
            Secondary = RuinPalette.RampOf(RuinPalette.DeepOlive),
            Accent = RuinPalette.RampOf(RuinPalette.PaleToxic),
            Skin = RuinPalette.SickFleshRamp,
            Emissive = RuinPalette.PaleToxic,
            Outline = RuinPalette.OutlineGreenBlack,
            HeadGear = 0, BulkBoost = 2, OvergrownArm = true, CoreRadius = 2, Grime = 0.09f, Seed = 305,
            ExpectedContentHeightMin = 40, ExpectedContentHeightMax = 58
        };

        public static CharacterProfile PrototypeX7() => new()
        {
            Id = "elite_prototype_x7", DisplayName = "Prototype X-7",
            CanvasWidth = 40, CanvasHeight = 56,
            HeadRadiusX = 4, HeadRadiusY = 4,
            ShoulderHalfWidth = 10, TorsoHalfWidth = 7, HipHalfWidth = 6,
            TorsoTop = 36, TorsoBottom = 20, LegBottom = 7, ArmThickness = 3,
            // pale ceramic/steel / charcoal joints / cyan-green energy / restrained warning orange
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#A8ADA6")),
            Secondary = RuinPalette.RampOf(RuinPalette.Charcoal),
            Accent = RuinPalette.RampOf(RuinPalette.WarningOchre),
            Emissive = RuinPalette.ElectricCyan,
            HeadGear = 3, Machine = true, CoreRadius = 3, Grime = 0.03f, Seed = 306,
            ExpectedContentHeightMin = 36, ExpectedContentHeightMax = 54
        };

        // ---- 9. BOSSES (spec 4.5: 48x48 minimum, often 64-80+) ----
        public static CharacterProfile TheConductor() => new()
        {
            Id = "boss_the_conductor", DisplayName = "The Conductor",
            CanvasWidth = 64, CanvasHeight = 80,
            HeadRadiusX = 6, HeadRadiusY = 6,
            ShoulderHalfWidth = 17, TorsoHalfWidth = 13, HipHalfWidth = 11,
            TorsoTop = 56, TorsoBottom = 26, LegBottom = 8, ArmThickness = 5,
            // commanding vertical silhouette, emergency-red and warning-yellow signalling
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#2E3A3E")),
            Secondary = RuinPalette.RampOf(RuinPalette.MidSteel),
            Accent = RuinPalette.RampOf(RuinPalette.WarningOchre),
            Emissive = RuinPalette.EmergencyRed,
            HeadGear = 2, BulkBoost = 2, AntennaHeight = 7, CoreRadius = 3, CarriedWeaponLength = 13,
            WarningStripes = true, Grime = 0.07f, Seed = 401,
            ExpectedContentHeightMin = 52, ExpectedContentHeightMax = 78
        };

        public static CharacterProfile TunnelMaw() => new()
        {
            Id = "boss_tunnel_maw", DisplayName = "Tunnel Maw",
            // Spec 9.2: wide low silhouette, mouth/intake as the central read.
            CanvasWidth = 80, CanvasHeight = 64,
            HeadRadiusX = 9, HeadRadiusY = 6,
            ShoulderHalfWidth = 24, TorsoHalfWidth = 20, HipHalfWidth = 18,
            TorsoTop = 36, TorsoBottom = 16, LegBottom = 7, ArmThickness = 5,
            // tunnel charcoal / concrete gray / rust / sick emergency green-amber
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#2A2E2C")),
            Secondary = RuinPalette.RampOf(RuinPalette.Concrete),
            Accent = RuinPalette.RampOf(RuinPalette.Rust),
            Emissive = RuinPalette.Hex("#9BB03F"),
            HeadGear = 0, WideLowBody = true, Machine = true, CoreRadius = 5, Grime = 0.12f, Seed = 402,
            ExpectedContentHeightMin = 30, ExpectedContentHeightMax = 62
        };

        public static CharacterProfile FoundryTitan() => new()
        {
            Id = "boss_the_foundry_titan", DisplayName = "Foundry Titan",
            CanvasWidth = 72, CanvasHeight = 80,
            HeadRadiusX = 6, HeadRadiusY = 5,
            ShoulderHalfWidth = 22, TorsoHalfWidth = 17, HipHalfWidth = 14,
            TorsoTop = 54, TorsoBottom = 24, LegBottom = 8, ArmThickness = 7,
            // blackened steel / rust / orange, white-hot only in the active core
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#26292B")),
            Secondary = RuinPalette.RampOf(RuinPalette.Rust),
            Accent = RuinPalette.RampOf(RuinPalette.OxideOrange),
            Emissive = RuinPalette.Hex("#F0A33C"),
            Outline = RuinPalette.OutlineRustBlack,
            HeadGear = 2, Machine = true, BulkBoost = 4, CoreRadius = 5, WarningStripes = true, Grime = 0.12f, Seed = 403,
            ExpectedContentHeightMin = 52, ExpectedContentHeightMax = 78
        };

        public static CharacterProfile ScrapKing() => new()
        {
            Id = "boss_scrap_king", DisplayName = "Scrap King",
            CanvasWidth = 64, CanvasHeight = 80,
            HeadRadiusX = 6, HeadRadiusY = 5,
            ShoulderHalfWidth = 19, TorsoHalfWidth = 15, HipHalfWidth = 12,
            TorsoTop = 54, TorsoBottom = 25, LegBottom = 8, ArmThickness = 6,
            // mismatched scrap neutrals unified by rust/ochre, controlled red-amber power accents
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#4A4339")),
            Secondary = RuinPalette.RampOf(RuinPalette.Rust),
            Accent = RuinPalette.RampOf(RuinPalette.WarningOchre),
            Emissive = RuinPalette.AmberActive,
            HeadGear = 2, BulkBoost = 3, AntennaHeight = 8, CoreRadius = 3, OvergrownArm = true, Grime = 0.14f, Seed = 404,
            ExpectedContentHeightMin = 52, ExpectedContentHeightMax = 78
        };

        public static CharacterProfile AegisCore() => new()
        {
            Id = "boss_aegis_core", DisplayName = "Aegis Core",
            CanvasWidth = 64, CanvasHeight = 64,
            HeadRadiusX = 7, HeadRadiusY = 6,
            ShoulderHalfWidth = 19, TorsoHalfWidth = 16, HipHalfWidth = 15,
            TorsoTop = 44, TorsoBottom = 18, LegBottom = 7, ArmThickness = 5,
            // pale lab gray / charcoal / cyan-green shield energy / dark vegetation intrusion
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#9BA29C")),
            Secondary = RuinPalette.RampOf(RuinPalette.Charcoal),
            Accent = RuinPalette.RampOf(RuinPalette.DeepOlive),
            Emissive = RuinPalette.ElectricCyan,
            Outline = RuinPalette.OutlineGreenBlack,
            HeadGear = 3, Machine = true, WideLowBody = true, CoreRadius = 6, Grime = 0.05f, Seed = 405,
            ExpectedContentHeightMin = 34, ExpectedContentHeightMax = 62
        };

        public static CharacterProfile SubjectOmega() => new()
        {
            Id = "boss_subject_omega", DisplayName = "Subject Omega",
            CanvasWidth = 64, CanvasHeight = 80,
            HeadRadiusX = 6, HeadRadiusY = 6,
            ShoulderHalfWidth = 18, TorsoHalfWidth = 14, HipHalfWidth = 12,
            TorsoTop = 54, TorsoBottom = 25, LegBottom = 8, ArmThickness = 6,
            // desaturated flesh-gray / lab white / dark olive / toxic green, limited red accent
            Primary = RuinPalette.RampOf(RuinPalette.Hex("#7C7B6E")),
            Secondary = RuinPalette.RampOf(RuinPalette.Hex("#B6BAB1")),
            Accent = RuinPalette.RampOf(RuinPalette.DeepOlive),
            Skin = RuinPalette.SickFleshRamp,
            Emissive = RuinPalette.PaleToxic,
            Outline = RuinPalette.OutlineGreenBlack,
            HeadGear = 0, BulkBoost = 3, OvergrownArm = true, CoreRadius = 4, Grime = 0.08f, Seed = 406,
            ExpectedContentHeightMin = 52, ExpectedContentHeightMax = 78
        };

        /// <summary>All 22 families in manifest order.</summary>
        public static IReadOnlyList<CharacterProfile> All() => new List<CharacterProfile>
        {
            Player(),
            Grunt(), Shooter(), Swarm(), Charger(), Brute(), Bomber(), ShieldEnemy(), SniperEnemy(), Summoner(),
            TunnelStalker(), Railguard(), ScrapExecutioner(), CrusherUnit(), MutatedBrute(), PrototypeX7(),
            TheConductor(), TunnelMaw(), FoundryTitan(), ScrapKing(), AegisCore(), SubjectOmega()
        };

        public static CharacterProfile ById(string id)
        {
            foreach (var p in All()) if (p.Id == id) return p;
            return null;
        }
    }
}
