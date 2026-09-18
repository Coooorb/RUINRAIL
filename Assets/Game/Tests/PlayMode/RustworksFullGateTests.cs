using RuinRail.Core;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 118 Rustworks gate: the complete full-run battery (seeded solo expeditions over the real 21-room pool with
    /// every category, both Elites at representative depths, both bosses, events/loot/merchant/economy/extraction
    /// conservation, failure path, performance smoke) re-run on the Rustworks content set. Multiplayer convergence and
    /// party scaling for the biome are covered by MultiplayerGateTests (TASK 108) which are biome-agnostic.
    /// </summary>
    public class RustworksFullGateTests : MetroFullGateTests
    {
        protected override Biome GateBiome => Biome.Rustworks;
        protected override string RoomFolder => "Assets/Game/ScriptableObjects/Rooms/Rustworks";
        protected override string ReportPath => "TestResults/gate_118_rustworks_runs.md";
        protected override string PerfReportPath => "TestResults/gate_118_perf.md";
        protected override string ReportTitle => "# Rustworks full-run gate (TASK 118)";
        protected override string[] ExpectedBossIds => new[] { "boss_the_foundry_titan", "boss_scrap_king" };
        protected override (string eliteId, int depth)[] EliteFixtures => new[] { ("elite_scrap_executioner", 3), ("elite_crusher_unit", 12) };
    }
}
