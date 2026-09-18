using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>Which way the body faces. Matches BodyFacing8 ordering used by the animation pipeline.</summary>
    public enum Facing8 { S, SE, E, NE, N, NW, W, SW }

    /// <summary>The six visual state roles in FINAL_ART_PRODUCTION_SPEC section 5.1.</summary>
    public enum VisualState { Idle, Move, Attack, Hit, Special, Death }

    /// <summary>
    /// One character family's design profile, transcribed from FINAL_ART_PRODUCTION_SPEC sections 6-9.
    ///
    /// The generator is a single parameterised humanoid/machine builder; everything that makes a Grunt look unlike a
    /// Sniper lives here as data. That is deliberate: 22 hand-written generators would drift apart, and the spec
    /// requires one canonical perspective and consistent body scale across the whole cast (4.1, 24.2).
    /// </summary>
    public sealed class CharacterProfile
    {
        public string Id = string.Empty;
        public string DisplayName = string.Empty;

        /// <summary>Source canvas. Spec 4.2-4.5: player 32x48, normals 28x40..36x52, elites larger, bosses 48-80+.</summary>
        public int CanvasWidth = 32;
        public int CanvasHeight = 48;

        // --- proportions, in pixels, for the S facing (spec 4.2) ---
        public int HeadRadiusX = 5;
        public int HeadRadiusY = 5;
        public int ShoulderHalfWidth = 8;
        public int TorsoHalfWidth = 6;
        public int HipHalfWidth = 5;
        public int TorsoTop = 30;
        public int TorsoBottom = 18;
        public int LegBottom = 6;
        public int ArmThickness = 3;

        // --- materials ---
        public RuinPalette.Ramp Primary = RuinPalette.OliveRamp;      // dominant cloth/body
        public RuinPalette.Ramp Secondary = RuinPalette.SteelRamp;    // armour/plates
        public RuinPalette.Ramp Accent = RuinPalette.RustRamp;        // identification accent
        public RuinPalette.Ramp Skin = RuinPalette.FleshRamp;
        public Color32 Outline = RuinPalette.OutlineCharcoal;
        public Color32 Emissive = RuinPalette.AmberActive;

        // --- silhouette features (each maps to a named trait in the spec's profiles) ---
        /// <summary>Head covering: 0 none, 1 hood/cloth, 2 helmet, 3 visor/optic, 4 respirator.</summary>
        public int HeadGear = 1;
        /// <summary>Backpack / apparatus mass behind the torso. Bomber, Summoner, player utility.</summary>
        public int BackpackSize;
        /// <summary>Large frontal shield (Shield enemy).</summary>
        public bool FrontShield;
        /// <summary>Long weapon profile carried in silhouette (Sniper, Shooter).</summary>
        public int CarriedWeaponLength;
        /// <summary>Forward aggressive lean in pixels (Grunt, Charger).</summary>
        public int ForwardLean;
        /// <summary>Extra shoulder/arm mass (Brute, Crusher).</summary>
        public int BulkBoost;
        /// <summary>Hunched low-profile creature rather than upright humanoid (Swarm, Tunnel Maw).</summary>
        public bool Hunched;
        /// <summary>Elongated blade/claw forearm (Tunnel Stalker).</summary>
        public bool BladeArm;
        /// <summary>Emissive core/lamp on the chest, and its radius.</summary>
        public int CoreRadius;
        /// <summary>Antenna / crown / signal mast above the head (Scrap King, Summoner, Conductor).</summary>
        public int AntennaHeight;
        /// <summary>Asymmetric overgrown limb (Mutated Brute, Subject Omega).</summary>
        public bool OvergrownArm;
        /// <summary>Machine rather than a person: no skin, plated geometry, treads or a base instead of legs.</summary>
        public bool Machine;
        /// <summary>Wide low body instead of an upright one (Tunnel Maw, Aegis Core).</summary>
        public bool WideLowBody;
        /// <summary>Warning stripes on frontal armour (Charger, Railguard, Crusher).</summary>
        public bool WarningStripes;
        /// <summary>Grime/wear pass density. Rustworks and Metro families carry more.</summary>
        public float Grime = 0.05f;
        /// <summary>Deterministic seed so every regeneration is byte-identical.</summary>
        public int Seed;

        /// <summary>Spec 4.3: visual mass communicates archetype. Used for the acceptance check on body scale.</summary>
        public int ExpectedContentHeightMin = 30;
        public int ExpectedContentHeightMax = 46;
    }
}
