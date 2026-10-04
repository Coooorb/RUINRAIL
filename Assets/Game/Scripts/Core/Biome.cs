namespace RuinRail.Core
{
    /// <summary>
    /// The shipped biomes (dungeon/56_BIOMES.md). Shared by rooms, enemies and loot. Values are stored as ints in room
    /// and enemy assets and in co-op messages: new biomes are only ever appended.
    /// </summary>
    public enum Biome
    {
        RuinedMetro,
        Rustworks,
        OvergrownLabs,
        CryoVaults
    }
}
