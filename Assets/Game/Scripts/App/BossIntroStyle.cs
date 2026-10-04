using RuinRail.Core;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// What makes one boss's introduction its own: the accent its name card and reveal burn in (from the boss's approved
    /// palette, art spec section 9), its approved identity line (combat/46), and how the arena answers the reveal — the
    /// biome's own matter thrown up around it. Presentation data only; nothing here touches the fight.
    /// </summary>
    public readonly struct BossIntroStyle
    {
        /// <summary>The arena's answer to the reveal, in the biome's matter.</summary>
        public enum Reaction
        {
            /// <summary>Fast bright sparks (power, signal, energy).</summary>
            Sparks,
            /// <summary>Glowing embers that rise (forge heat).</summary>
            Embers,
            /// <summary>Slow drifting spores (biomass).</summary>
            Spores,
            /// <summary>Ice glints thrown out and settling (cryo).</summary>
            Frost,
            /// <summary>Grit and chips kicked off the floor (collapsed tunnels).</summary>
            Debris
        }

        public BossIntroStyle(Color accent, Color glow, string identity, Reaction reaction)
        {
            Accent = accent;
            Glow = glow;
            Identity = identity ?? string.Empty;
            ArenaReaction = reaction;
        }

        /// <summary>The card's rules, kicker and title shadow, the ground shockwave.</summary>
        public Color Accent { get; }
        /// <summary>The reveal flash on the boss and the hottest particles.</summary>
        public Color Glow { get; }
        /// <summary>The approved one-line identity under the name (combat/46), upper case.</summary>
        public string Identity { get; }
        public Reaction ArenaReaction { get; }

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.white;

        /// <summary>The style for a boss definition id; an unknown boss falls back to its biome's language.</summary>
        public static BossIntroStyle For(string bossId, Biome biome) => bossId switch
        {
            // Ruined Metro — emergency red / warning yellow signalling; sick emergency green and amber in the tunnels.
            "boss_the_conductor" => new BossIntroStyle(Hex("#E3B53C"), Hex("#F0603E"), "AUTOMATED METRO SECURITY COMMAND ROBOT", Reaction.Sparks),
            "boss_tunnel_maw" => new BossIntroStyle(Hex("#A9C24A"), Hex("#E6A94A"), "LARGE TUNNEL MUTANT", Reaction.Debris),
            // Rustworks — furnace orange and white-hot; rust and ochre with red-amber power.
            "boss_the_foundry_titan" => new BossIntroStyle(Hex("#E8772E"), Hex("#FFE6B0"), "HUGE INDUSTRIAL ROBOT", Reaction.Embers),
            "boss_scrap_king" => new BossIntroStyle(Hex("#D49A3A"), Hex("#E0533A"), "MOBILE ARMORED WASTELAND FIGHTER", Reaction.Sparks),
            // Overgrown Labs — toxic green with a red biological accent; cyan/green shield energy.
            "boss_subject_omega" => new BossIntroStyle(Hex("#9DC75A"), Hex("#D9F2A0"), "LARGE MUTATION AND BIOMASS EXPERIMENT", Reaction.Spores),
            "boss_aegis_core" => new BossIntroStyle(Hex("#5FD3C4"), Hex("#D8FFF6"), "HIGH-TECH SECURITY CORE", Reaction.Sparks),
            // Cryo Vaults — the red security optic on ice; the cryo-port's cyan and frost white.
            "boss_the_warden" => new BossIntroStyle(Hex("#E0533A"), Hex("#DCEFF4"), "AUTONOMOUS VAULT SECURITY MACHINE", Reaction.Frost),
            "boss_subject_zero" => new BossIntroStyle(Hex("#7FE6F0"), Hex("#FFFFFF"), "THE PRESERVED EXPERIMENTAL SUBJECT", Reaction.Frost),
            _ => biome switch
            {
                Biome.Rustworks => new BossIntroStyle(Hex("#E8772E"), Hex("#FFE6B0"), string.Empty, Reaction.Embers),
                Biome.OvergrownLabs => new BossIntroStyle(Hex("#9DC75A"), Hex("#D9F2A0"), string.Empty, Reaction.Spores),
                Biome.CryoVaults => new BossIntroStyle(Hex("#7FE6F0"), Hex("#FFFFFF"), string.Empty, Reaction.Frost),
                _ => new BossIntroStyle(Hex("#E3B53C"), Hex("#F0603E"), string.Empty, Reaction.Sparks)
            }
        };
    }
}
