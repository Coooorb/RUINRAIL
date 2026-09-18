using System;
using RuinRail.Core;
using RuinRail.Core.Rng;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>
    /// Per-depth biome selection (dungeon/56_BIOMES "Selection"): random, direct repeats allowed but weighted down.
    /// Approved example weights after Rustworks — Metro 40 / Labs 40 / Rustworks 20 — generalised as
    /// "each other biome 40, the previous biome 20" (tunable). The first depth has no previous biome and draws
    /// uniformly among the three (no repeat weighting is inferred where none exists). Every draw comes from the
    /// dedicated Biome RNG stream (114) keyed by RunSeed + depth, so the host's sequence is reproducible by any peer
    /// and independent of every dungeon/encounter/loot draw.
    /// </summary>
    public static class BiomeSelector
    {
        public const int OtherBiomeWeight = 40;
        public const int RepeatBiomeWeight = 20;

        /// <summary>Exactly the three V1 biomes, in enum order.</summary>
        public static readonly Biome[] All = { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs };

        public static Biome SelectNext(Biome previous, IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var total = 0;
            foreach (var biome in All) total += WeightFor(biome, previous);
            var pick = random.NextInt(total);
            foreach (var biome in All)
            {
                pick -= WeightFor(biome, previous);
                if (pick < 0) return biome;
            }

            return All[^1];
        }

        /// <summary>Depth 1: uniform among the three biomes from the Biome stream of depth 1.</summary>
        public static Biome SelectFirst(IRandomSource random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return All[random.NextInt(All.Length)];
        }

        public static Biome SelectFirst(int runSeed) => SelectFirst(StreamFor(runSeed, 1));

        /// <summary>Deterministic draw for the biome of depth N+1 from the Biome stream of that depth.</summary>
        public static Biome SelectNext(Biome previous, int runSeed, int nextDepth)
        {
            return SelectNext(previous, StreamFor(runSeed, nextDepth));
        }

        /// <summary>The whole biome sequence of a run up to <paramref name="maxDepth"/> (same seed → same sequence).</summary>
        public static Biome[] Sequence(int runSeed, int maxDepth)
        {
            var result = new Biome[Math.Max(0, maxDepth)];
            for (var depth = 1; depth <= maxDepth; depth++)
            {
                result[depth - 1] = depth == 1 ? SelectFirst(runSeed) : SelectNext(result[depth - 2], runSeed, depth);
            }

            return result;
        }

        public static IRandomSource StreamFor(int runSeed, int depth) => RngStreams.Derive(runSeed, depth, RngStream.Biome);

        public static int WeightFor(Biome candidate, Biome previous) => candidate == previous ? RepeatBiomeWeight : OtherBiomeWeight;
    }
}
