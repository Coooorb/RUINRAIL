namespace RuinRail.Core.Rng
{
    /// <summary>Logical seeded streams; each is derived independently so consuming one never perturbs another.</summary>
    public enum RngStream
    {
        Dungeon = 1,
        Encounter = 2,
        Loot = 3,
        Assembly = 4,
        /// <summary>Per-depth biome selection (56 / 114): its own stream so room, encounter and loot draws never shift it.</summary>
        Biome = 5
    }

    public static class RngStreams
    {
        /// <summary>Derives the seeded source for one stream of one depth of one expedition (RunSeed + Depth reproduces it).</summary>
        public static SeededRandom Derive(int runSeed, int depth, RngStream stream)
        {
            return new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)stream));
        }
    }
}
