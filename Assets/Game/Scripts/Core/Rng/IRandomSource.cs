namespace RuinRail.Core.Rng
{
    /// <summary>
    /// Project-owned random source. Gameplay code must draw from an injected instance of this
    /// (seeded per logical stream) instead of calling UnityEngine.Random directly.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
        int NextInt(int minInclusive, int maxInclusive);

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);

        /// <summary>Uniform float in [0, 1).</summary>
        float NextFloat();
    }
}
