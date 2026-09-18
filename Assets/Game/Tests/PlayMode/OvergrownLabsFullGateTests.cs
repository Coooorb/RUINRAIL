using RuinRail.Core;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 128 Overgrown Labs gate: the complete full-run battery (seeded solo expeditions over the real 21-room pool
    /// with every category, both Elites at representative depths, both bosses, events/loot/merchant/economy/extraction
    /// conservation, failure path, performance smoke) re-run on the Labs content set. Multiplayer convergence, party
    /// scaling, dead/revive/voting are covered by the biome-agnostic MultiplayerGateTests (TASK 108).
    /// </summary>
    public class OvergrownLabsFullGateTests : MetroFullGateTests
    {
        protected override Biome GateBiome => Biome.OvergrownLabs;
        protected override string RoomFolder => "Assets/Game/ScriptableObjects/Rooms/OvergrownLabs";
        protected override string ReportPath => "TestResults/gate_128_labs_runs.md";
        protected override string PerfReportPath => "TestResults/gate_128_perf.md";
        protected override string ReportTitle => "# Overgrown Labs full-run gate (TASK 128)";
        protected override string[] ExpectedBossIds => new[] { "boss_subject_omega", "boss_aegis_core" };
        protected override (string eliteId, int depth)[] EliteFixtures => new[] { ("elite_mutated_brute", 3), ("elite_prototype_x7", 12) };
    }
}
