using RuinRail.Core;
using UnityEngine;

namespace RuinRail.Presentation
{
    /// <summary>
    /// The shared enemy palette language of each biome: one multiply tint on the body sprite of every normal enemy and
    /// Elite spawned there, so the same designs read as belonging to the Ruined Metro (cold steel blue), the Rustworks
    /// (rust and ember), the Overgrown Labs (bio green) or the Cryo Vaults (pale frost cyan). It shifts hue only — sprite, silhouette, animation, hitbox and
    /// stats are untouched, and every type keeps its own shapes and value pattern. Elites take the tint at half strength,
    /// so their authored colours and contrast stay stronger than a normal enemy's; bosses keep their own identity. The
    /// tint is the body's resting colour: the hit flash replaces it for its few frames and returns to it, and telegraphs,
    /// status and impact effects are separate renderers the tint never touches.
    /// </summary>
    public static class EnemyBiomeTint
    {
        public static readonly Color RuinedMetro = new(0.76f, 0.88f, 1f);
        public static readonly Color Rustworks = new(1f, 0.80f, 0.62f);
        public static readonly Color OvergrownLabs = new(0.78f, 1f, 0.70f);
        public static readonly Color CryoVaults = new(0.62f, 0.93f, 1f);

        /// <summary>How much of the biome tint an Elite takes (1 = the full normal-enemy tint).</summary>
        public const float EliteStrength = 0.5f;

        public static Color For(Biome biome, bool elite)
        {
            var tint = biome switch
            {
                Biome.RuinedMetro => RuinedMetro,
                Biome.Rustworks => Rustworks,
                Biome.OvergrownLabs => OvergrownLabs,
                Biome.CryoVaults => CryoVaults,
                _ => Color.white
            };
            return elite ? Color.Lerp(Color.white, tint, EliteStrength) : tint;
        }

        /// <summary>Gives a body renderer its biome colour; call before the hit flash caches the resting colour.</summary>
        public static void Apply(SpriteRenderer body, Biome biome, bool elite)
        {
            if (body != null) body.color = For(biome, elite);
        }
    }
}
