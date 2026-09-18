using System;

namespace RuinRail.Core.Rng
{
    /// <summary>
    /// Deterministic, platform-independent PRNG (SplitMix64). Identical seed + identical call sequence
    /// yields identical results on every platform, which the seeded dungeon/loot pipeline relies on.
    /// </summary>
    public sealed class SeededRandom : IRandomSource
    {
        private const ulong Golden = 0x9E3779B97F4A7C15UL;

        private ulong _state;

        public SeededRandom(ulong seed)
        {
            Seed = seed;
            _state = seed;
        }

        public SeededRandom(int seed) : this(unchecked((ulong)(uint)seed))
        {
        }

        public ulong Seed { get; }

        public int NextInt(int minInclusive, int maxInclusive)
        {
            if (maxInclusive < minInclusive)
            {
                throw new ArgumentException($"maxInclusive ({maxInclusive}) must be >= minInclusive ({minInclusive}).");
            }

            var range = (ulong)((long)maxInclusive - minInclusive + 1);
            return (int)(minInclusive + (long)(NextUlong() % range));
        }

        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentException("maxExclusive must be positive.", nameof(maxExclusive));
            }

            return (int)(NextUlong() % (ulong)maxExclusive);
        }

        public float NextFloat()
        {
            return (NextUlong() >> 40) * (1f / (1 << 24));
        }

        private ulong NextUlong()
        {
            _state += Golden;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Deterministically mixes several integer inputs into one 64-bit seed.</summary>
        public static ulong MixSeed(params int[] parts)
        {
            var h = Golden;
            foreach (var part in parts)
            {
                h ^= unchecked((ulong)(uint)part) + Golden + (h << 6) + (h >> 2);
                h = (h ^ (h >> 30)) * 0xBF58476D1CE4E5B9UL;
            }

            return h == 0 ? Golden : h;
        }
    }
}
