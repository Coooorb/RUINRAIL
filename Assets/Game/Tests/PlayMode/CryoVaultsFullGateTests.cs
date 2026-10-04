using RuinRail.Core;

namespace RuinRail.Tests
{
    /// <summary>
    /// Cryo Vaults gate: the complete full-run battery (seeded solo expeditions over the real 21-room pool with every
    /// category, both Elites at representative depths, both bosses, events/loot/merchant/economy/extraction conservation,
    /// failure path, performance smoke) re-run on the Cryo content set. Multiplayer convergence, party scaling,
    /// dead/revive/voting are covered by the biome-agnostic MultiplayerGateTests.
    /// </summary>
    public class CryoVaultsFullGateTests : MetroFullGateTests
    {
        protected override Biome GateBiome => Biome.CryoVaults;
        protected override string RoomFolder => "Assets/Game/ScriptableObjects/Rooms/CryoVaults";
        protected override string ReportPath => "TestResults/gate_cryo_vaults_runs.md";
        protected override string PerfReportPath => "TestResults/gate_cryo_vaults_perf.md";
        protected override string ReportTitle => "# Cryo Vaults full-run gate";
        protected override string[] ExpectedBossIds => new[] { "boss_the_warden", "boss_subject_zero" };
        protected override (string eliteId, int depth)[] EliteFixtures => new[] { ("elite_cryo_enforcer", 3), ("elite_vault_stalker", 12) };
    }
}
