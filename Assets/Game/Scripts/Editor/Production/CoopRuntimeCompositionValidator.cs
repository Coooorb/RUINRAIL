using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.App;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The contract gate for the co-op runtime-composition pass.
    ///
    /// Every rule here guards a way the run could quietly become solo-shaped again. The repository has had the whole
    /// party architecture for a long time — presence, roster, loot authority, revive, voting — and it still behaved as
    /// solo, because exactly one player entity was ever composed. Type existence therefore proves nothing: these rules
    /// check that the real composition root *calls* the party architecture, that local presentation stays local, and
    /// that scaling cannot run ahead of the entities that exist.
    ///
    /// <see cref="Validate"/> takes its inputs so a deliberately broken fixture can be fed in and proven to fail.
    /// </summary>
    public static class CoopRuntimeCompositionValidator
    {
        public const string ReportPath = "TestResults/coop_runtime_composition.md";

        /// <summary>80: maximum 3 players total.</summary>
        public const int MaxPartySize = 3;

        public const string ExpeditionScenePath = "Assets/Game/Scripts/App/ExpeditionScene.cs";
        public const string ExpeditionPartyPath = "Assets/Game/Scripts/App/ExpeditionParty.cs";
        public const string PresencePath = "Assets/Game/Scripts/Multiplayer/PlayerPresence.cs";

        public sealed class Line
        {
            public string Rule = string.Empty;
            public string Subject = string.Empty;
            public string Detail = string.Empty;
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Line> Lines = new();
            public int Failed => Lines.Count(l => !l.Pass);
            public bool Pass => Lines.Count > 0 && Lines.All(l => l.Pass);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL co-op runtime composition contract");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count} rules checked, {Lines.Count - Failed} clean, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Rule | Subject | Detail | Result |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var l in Lines.OrderBy(l => l.Rule, StringComparer.Ordinal).ThenBy(l => l.Subject, StringComparer.Ordinal))
                    sb.AppendLine($"| {l.Rule} | {l.Subject} | {l.Detail} | {(l.Pass ? "PASS" : "FAIL: " + string.Join("; ", l.Problems))} |");
                return sb.ToString();
            }
        }

        /// <summary>
        /// The composition facts the gate checks. They are supplied rather than scraped so a test can feed a broken
        /// fixture — a run that composed one entity while claiming a trio, a remote spawn that built a camera, and so on.
        /// </summary>
        public sealed class CompositionFacts
        {
            /// <summary>Party sizes the run composition can actually produce.</summary>
            public IReadOnlyList<int> ComposablePartySizes = Array.Empty<int>();
            /// <summary>Expected vs composed for a trio start; they must agree or the run must refuse to start.</summary>
            public int TrioExpected;
            public int TrioComposed;
            /// <summary>Whether a start whose composed count is short of the expected count is refused.</summary>
            public bool RefusesUnderfilledStart;
            /// <summary>Cameras, audio listeners, local input readers and HUDs created by composing a remote member.</summary>
            public int RemoteCameras;
            public int RemoteAudioListeners;
            public int RemoteLocalInputReaders;
            public int RemoteHuds;
            /// <summary>Members the party roster receives for a trio run.</summary>
            public int RosterMembersForTrio;
            /// <summary>Loot participants registered for a trio run.</summary>
            public int LootParticipantsForTrio;
            /// <summary>Whether the composed co-op run reports IsCoop and the solo run does not.</summary>
            public bool CoopFlagFromComposedParty;
            public bool SoloFlagStaysSolo;

            // ---- completion pass: host/client runtime fixtures (null = not supplied) ----
            /// <summary>Rooms whose replicated client state differs from the host's after the host resolved them.</summary>
            public int? RoomStateDivergences;
            /// <summary>Party members a client-side party view composes for a duo and a trio start (must equal the run's members).</summary>
            public int? ClientDuoComposed;
            public int? ClientTrioComposed;
            /// <summary>Cameras / listeners / local inputs a client-side party view adds for the other members.</summary>
            public int? ClientRemoteLocalPresentation;
            /// <summary>Replicas a resync (reconnect) re-created a second time, and room records it rewound.</summary>
            public int? ResyncDuplicates;
            /// <summary>Hits a client could apply without the host (must be 0).</summary>
            public int? ClientLocalHitsApplied;
        }

        /// <summary>Source overrides by project path (deliberately broken fixtures); null reads the real files.</summary>
        private static IReadOnlyDictionary<string, string> _overrides;

        private static string Source(string path) =>
            _overrides != null && _overrides.TryGetValue(path, out var text) ? text ?? string.Empty : File.Exists(path) ? File.ReadAllText(path) : string.Empty;

        public const string ExpeditionCoopPath = "Assets/Game/Scripts/App/ExpeditionScene.Coop.cs";
        public const string RigPath = "Assets/Game/Scripts/App/PlayerRigComposer.cs";
        public const string HostWorldPath = "Assets/Game/Scripts/Multiplayer/CoopHostWorld.cs";
        public const string ClientWorldPath = "Assets/Game/Scripts/Multiplayer/CoopClientWorld.cs";
        public const string LinkPath = "Assets/Game/Scripts/Multiplayer/CoopRunLink.cs";
        public const string TransitPath = "Assets/Game/Scripts/Expedition/TransitDecision.cs";
        public const string ProofPath = "Assets/Game/Scripts/App/CoopExpeditionProof.cs";
        public const string GameAppPath = "Assets/Game/Scripts/App/GameApp.cs";

        [MenuItem("RuinRail/Production/Validate Co-op Runtime Composition")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? ".");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        private const string WorldItemPickupPath = "Assets/Game/Scripts/Loot/WorldItemPickup.cs";
        private const string CoinPickupPath = "Assets/Game/Scripts/Loot/CoinPickup.cs";
        private const string NetworkPlayerLifePath = "Assets/Game/Scripts/Multiplayer/NetworkPlayerLife.cs";
        private const string LiveServicePath = "Assets/Game/Scripts/Multiplayer/LiveServiceConfiguration.cs";

        public static Report Validate() => Validate(null);

        public static Report Validate(CompositionFacts facts) => Validate(facts, null);

        /// <summary>
        /// Validates with optional source overrides (project path → text), so a deliberately broken fixture — a client
        /// branch removed, a spawner left authoritative, a registration dropped — can be proven to fail.
        /// </summary>
        public static Report Validate(CompositionFacts facts, IReadOnlyDictionary<string, string> sourceOverrides)
        {
            _overrides = sourceOverrides;
            try
            {
                var report = ValidateCore(facts);
                ValidateCompletion(report, facts);
                return report;
            }
            finally
            {
                _overrides = null;
            }
        }

        private static Report ValidateCore(CompositionFacts facts)
        {
            var report = new Report();

            // ---- 1. co-op is in V1 scope and bounded at 3 ----
            var scope = new Line { Rule = "scope", Subject = "co-op in V1, max 3, no PvP", Detail = $"ExpeditionParty.MaxPartySize={ExpeditionParty.MaxPartySize}, SessionRequest.MaxPartySize={SessionRequest.MaxPartySize}" };
            if (ExpeditionParty.MaxPartySize != MaxPartySize) scope.Problems.Add($"run composition allows {ExpeditionParty.MaxPartySize} players; 80 says 3");
            if (SessionRequest.MaxPartySize != MaxPartySize) scope.Problems.Add($"session allows {SessionRequest.MaxPartySize} players; 80 says 3");
            if (NetworkSessionController.SupportsHostMigration) scope.Problems.Add("host migration is explicitly post-MVP (85)");
            if (NetworkSessionController.SupportsPublicMatchmaking) scope.Problems.Add("public matchmaking is a non-goal");
            if (NetworkSessionController.SupportsDedicatedServer) scope.Problems.Add("dedicated servers are a non-goal");
            report.Lines.Add(scope);

            // ---- 2. the party architecture has a real runtime composition caller ----
            foreach (var (needle, subject, problem) in new[]
                     {
                         ("new PlayerPresenceService", "PlayerPresenceService", "no runtime composition constructs the presence service"),
                         ("new LocalPlayerEntityFactory", "player entity factory", "no runtime composition constructs a player entity factory"),
                         ("new LootAuthorityService", "LootAuthorityService", "no runtime composition constructs the loot authority"),
                         ("new ReconnectGraceService", "ReconnectGraceService", "no runtime composition constructs the reconnect grace")
                     })
            {
                var line = new Line { Rule = "runtime composition", Subject = subject };
                var callers = RuntimeCallersOf(needle);
                line.Detail = callers.Count + " runtime caller(s): " + string.Join(", ", callers.Select(Path.GetFileName));
                if (callers.Count == 0) line.Problems.Add(problem + " (test-only construction does not compose a run)");
                report.Lines.Add(line);
            }

            // ---- 3. the network player prefab exists and is registered ----
            var prefab = new Line { Rule = "network prefab", Subject = "PlayerNetworkEntity" };
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPlayerPrefabAuthoring.PrefabPath);
            prefab.Detail = prefabAsset != null ? NetworkPlayerPrefabAuthoring.PrefabPath : "(missing)";
            if (prefabAsset == null) prefab.Problems.Add("NgoPlayerEntityFactory has nothing to spawn without the prefab");
            else if (!RegisteredNetworkPrefab(prefabAsset)) prefab.Problems.Add("the prefab is not registered in DefaultNetworkPrefabs.asset, so NGO cannot spawn it");
            report.Lines.Add(prefab);

            // ---- 4. the expedition is not hardcoded to one player ----
            var scene = File.Exists(ExpeditionScenePath) ? File.ReadAllText(ExpeditionScenePath) : string.Empty;
            var hardcoded = new Line { Rule = "no solo hardcoding", Subject = "ExpeditionScene" };
            var isCoopFalse = CountOccurrences(scene, "isCoop: false");
            hardcoded.Detail = $"{isCoopFalse} literal `isCoop: false` site(s)";
            if (isCoopFalse > 0) hardcoded.Problems.Add($"{isCoopFalse} co-op flag(s) still hardcoded to false in the real run path");
            if (!scene.Contains("ComposeParty", StringComparison.Ordinal)) hardcoded.Problems.Add("the expedition does not compose a party at all");
            if (!scene.Contains("ScalingPartySize", StringComparison.Ordinal)) hardcoded.Problems.Add("dungeon scaling does not read the composed party size");
            report.Lines.Add(hardcoded);

            // ---- 5. co-op must not stop the shared world ----
            var pause = new Line { Rule = "co-op pause", Subject = "shared world keeps running" };
            var pauseProblems = new List<string>();
            foreach (var path in new[]
                     {
                         "Assets/Game/Scripts/UI/Inventory/InventoryViewModel.cs",
                         "Assets/Game/Scripts/UI/Merchant/MerchantViewModel.cs",
                         "Assets/Game/Scripts/UI/WeaponCache/WeaponCacheViewModel.cs",
                         "Assets/Game/Scripts/UI/RunEnd/RunFailedViewModel.cs"
                     })
            {
                if (!File.Exists(path)) { pauseProblems.Add(Path.GetFileName(path) + " is missing"); continue; }
                var text = File.ReadAllText(path);
                if (!text.Contains("if (!_isCoop) _pause?.Pause();", StringComparison.Ordinal))
                    pauseProblems.Add(Path.GetFileName(path) + " pauses the world without checking the co-op flag");
            }

            pause.Detail = pauseProblems.Count == 0 ? "every run window gates its world pause on the co-op flag" : string.Join("; ", pauseProblems);
            pause.Problems.AddRange(pauseProblems);
            report.Lines.Add(pause);

            // ---- 6. run-level vs local presentation is explicit ----
            var separation = new Line { Rule = "composition separation", Subject = "run level vs local presentation" };
            var partySource = File.Exists(ExpeditionPartyPath) ? File.ReadAllText(ExpeditionPartyPath) : string.Empty;
            if (string.IsNullOrEmpty(partySource)) separation.Problems.Add("no run-level party composition exists");
            foreach (var forbidden in new[] { "AddComponent<Camera>", "AddComponent<AudioListener>", "AddComponent<PlayerInput>", "DungeonHudView.Create", "UiKit.Canvas" })
                if (partySource.Contains(forbidden, StringComparison.Ordinal))
                    separation.Problems.Add($"run-level party composition creates local presentation ({forbidden})");
            separation.Detail = "ExpeditionParty creates no camera, listener, local input or HUD";
            report.Lines.Add(separation);

            // ---- 7. remote replicas are input-isolated by construction ----
            var isolation = new Line { Rule = "remote isolation", Subject = "PlayerEntityBuilder" };
            var builder = File.Exists("Assets/Game/Scripts/Player/PlayerEntityBuilder.cs") ? File.ReadAllText("Assets/Game/Scripts/Player/PlayerEntityBuilder.cs") : string.Empty;
            if (!builder.Contains("reader = NullPlayerInputReader.Instance;", StringComparison.Ordinal))
                isolation.Problems.Add("a non-local player is not built with the null input reader");
            if (builder.Contains("AddComponent<AudioListener>", StringComparison.Ordinal))
                isolation.Problems.Add("the player composition adds an AudioListener, so every remote member would add one");
            isolation.Detail = "remote members get NullPlayerInputReader and no listener";
            report.Lines.Add(isolation);

            // ---- 8. the party roster and voting receive real presence ----
            var roster = new Line { Rule = "party wiring", Subject = "roster / voting / reconnect" };
            if (!File.ReadAllText(PresencePath).Contains("LifeRoster = _roster", StringComparison.Ordinal))
                roster.Problems.Add("a presence-spawned member does not join the party life roster");
            if (!scene.Contains("new PartyExpeditionBinding", StringComparison.Ordinal))
                roster.Problems.Add("transit voting is not bound to the party roster in the real run");
            if (!scene.Contains("Presence.Despawned", StringComparison.Ordinal) && !scene.Contains("OnPartyMemberLeft", StringComparison.Ordinal))
                roster.Problems.Add("the run does not react to a member leaving");
            roster.Detail = "presence -> PartyLifeRoster -> PartyExpeditionBinding -> TransitDecision";
            report.Lines.Add(roster);

            // ---- 9. damage authority follows the real role ----
            var authority = new Line { Rule = "host authority", Subject = "DamageAuthority follows the network role" };
            if (!scene.Contains("DamageAuthority.LocalIsAuthoritative =", StringComparison.Ordinal))
                authority.Problems.Add("the run never sets damage authority from the network role: a client would apply damage locally");
            if (!scene.Contains("IsHostAuthority", StringComparison.Ordinal))
                authority.Problems.Add("damage authority is not derived from the session controller's role");
            authority.Detail = "set at composition from NetworkSessionController.IsHostAuthority, reset on teardown";
            report.Lines.Add(authority);

            // ---- 10. room state is party-aware ----
            var rooms = new Line { Rule = "room party state", Subject = "activation once, discovery shared" };
            var room = File.Exists("Assets/Game/Scripts/Dungeon/Runtime/RoomRuntime.cs") ? File.ReadAllText("Assets/Game/Scripts/Dungeon/Runtime/RoomRuntime.cs") : string.Empty;
            if (!room.Contains("if (!IsAuthoritative) return false;", StringComparison.Ordinal))
                rooms.Problems.Add("a client can advance room state locally");
            if (!room.Contains("_state.State != RoomLifecycleState.Unentered", StringComparison.Ordinal))
                rooms.Problems.Add("a second entering player could activate the room twice");
            if (scene.Contains("player != _rig.Player) return; // local player only", StringComparison.Ordinal))
                rooms.Problems.Add("room discovery is still local-player-only");
            rooms.Detail = "RoomRuntime activates once under host authority; the minimap marks for any member";
            report.Lines.Add(rooms);

            // ---- 11. the loot arbiter is on the runtime pickup path for a co-op party, and only there ----
            var arbiter = new Line { Rule = "loot arbitration", Subject = "co-op pickups resolve through the host" };
            var pickup = File.Exists(WorldItemPickupPath) ? File.ReadAllText(WorldItemPickupPath) : string.Empty;
            var coins = File.Exists(CoinPickupPath) ? File.ReadAllText(CoinPickupPath) : string.Empty;
            if (!pickup.Contains("PickupArbiter.Items?.Invoke", StringComparison.Ordinal))
                arbiter.Problems.Add("a world pickup never consults the host arbiter, so a co-op race is decided locally");
            if (!coins.Contains("PickupArbiter.Coins?.Invoke", StringComparison.Ordinal))
                arbiter.Problems.Add("a coin pile never consults the host arbiter, so the party split cannot happen");
            if (!scene.Contains("InstallLootArbiter", StringComparison.Ordinal))
                arbiter.Problems.Add("the run never installs the arbiter: the composed LootAuthorityService would arbitrate nothing");
            if (!scene.Contains("PickupArbiter.Clear()", StringComparison.Ordinal))
                arbiter.Problems.Add("the run never clears the arbiter: the next run or the menu would inherit it");
            arbiter.Detail = "ExpeditionScene installs it for a co-op party only and clears it on teardown";
            report.Lines.Add(arbiter);

            // ---- 12. life state reaches every peer (84) ----
            var lifeSync = new Line { Rule = "replicated life state", Subject = "Downed / Dead / bleedout" };
            if (!File.Exists(NetworkPlayerLifePath))
                lifeSync.Problems.Add("nothing replicates a player's life state, so a teammate's Downed state is invisible to other peers");
            else if (!File.ReadAllText(NetworkPlayerLifePath).Contains("ApplyReplicatedState", StringComparison.Ordinal))
                lifeSync.Problems.Add("the life replicator does not apply state through PlayerLifeStateComponent");
            var prefabComponents = prefabAsset != null ? prefabAsset.GetComponents<Component>().Select(c => c != null ? c.GetType().Name : "(missing)").ToList() : new List<string>();
            foreach (var required in new[] { "NetworkPlayerObject", "NetworkPlayerMotion", "NetworkHealth", "NetworkPlayerLife", "NetworkPlayerCombat" })
                if (prefabAsset != null && !prefabComponents.Contains(required))
                    lifeSync.Problems.Add($"the network player prefab has no {required}: that state never reaches another peer");
            lifeSync.Detail = prefabAsset != null ? string.Join(", ", prefabComponents.Where(c => c.StartsWith("Network", StringComparison.Ordinal))) : "(no prefab)";
            report.Lines.Add(lifeSync);

            // ---- 13. the shipped process actually composes a NetworkManager ----
            var transport = new Line { Rule = "live session composition", Subject = "NetworkManager + transport + lobby bridge" };
            var live = File.Exists(LiveServicePath) ? File.ReadAllText(LiveServicePath) : string.Empty;
            if (!live.Contains("AddComponent<Unity.Netcode.NetworkManager>", StringComparison.Ordinal))
                transport.Problems.Add("live composition never creates a NetworkManager, so an online session has no transport and falls back to the fake");
            if (!live.Contains("AddNetworkPrefab", StringComparison.Ordinal))
                transport.Problems.Add("the networked player prefab is not registered with the NetworkManager");
            if (RuntimeCallersOf("new SessionPartyBridge").Count == 0)
                transport.Problems.Add("nothing joins accepted connections into the party lobby: a hosted session would still start a solo party");
            transport.Detail = "LiveServiceConfiguration composes the manager; BaseHubScreen bridges connections into PartyLobby";
            report.Lines.Add(transport);

            // ---- 14. supplied composition facts (runtime fixtures) ----
            if (facts != null)
            {
                var sizes = new Line { Rule = "composable party sizes", Subject = "1 / 2 / 3", Detail = string.Join(", ", facts.ComposablePartySizes) };
                foreach (var expected in new[] { 1, 2, 3 })
                    if (!facts.ComposablePartySizes.Contains(expected)) sizes.Problems.Add($"the run cannot compose a {expected}-player expedition");
                if (facts.ComposablePartySizes.Any(n => n > MaxPartySize)) sizes.Problems.Add("a party larger than 3 was composable");
                report.Lines.Add(sizes);

                var scaling = new Line { Rule = "party scaling integrity", Subject = "expected vs composed", Detail = $"trio expected {facts.TrioExpected}, composed {facts.TrioComposed}" };
                if (facts.TrioComposed != facts.TrioExpected) scaling.Problems.Add("a trio-scaled run did not compose three player entities");
                if (!facts.RefusesUnderfilledStart) scaling.Problems.Add("an underfilled start is not refused: the dungeon would scale for players that do not exist");
                report.Lines.Add(scaling);

                var local = new Line { Rule = "local presentation", Subject = "remote member adds nothing local", Detail = $"cameras {facts.RemoteCameras}, listeners {facts.RemoteAudioListeners}, local readers {facts.RemoteLocalInputReaders}, HUDs {facts.RemoteHuds}" };
                if (facts.RemoteCameras != 0) local.Problems.Add("a remote member created a camera");
                if (facts.RemoteAudioListeners != 0) local.Problems.Add("a remote member created an AudioListener");
                if (facts.RemoteLocalInputReaders != 0) local.Problems.Add("a remote member created a local input reader");
                if (facts.RemoteHuds != 0) local.Problems.Add("a remote member created a HUD");
                report.Lines.Add(local);

                var party = new Line { Rule = "party services", Subject = "roster and loot participants", Detail = $"roster {facts.RosterMembersForTrio}, loot {facts.LootParticipantsForTrio}" };
                if (facts.RosterMembersForTrio != MaxPartySize) party.Problems.Add($"the party roster received {facts.RosterMembersForTrio} of 3 members");
                if (facts.LootParticipantsForTrio != MaxPartySize) party.Problems.Add($"the loot authority received {facts.LootParticipantsForTrio} of 3 participants");
                report.Lines.Add(party);

                var flag = new Line { Rule = "co-op flag", Subject = "derived from composed presence", Detail = $"coop={facts.CoopFlagFromComposedParty}, solo stays solo={facts.SoloFlagStaysSolo}" };
                if (!facts.CoopFlagFromComposedParty) flag.Problems.Add("a composed co-op run does not report IsCoop");
                if (!facts.SoloFlagStaysSolo) flag.Problems.Add("a solo run reports co-op: solo pause semantics would regress");
                report.Lines.Add(flag);
            }

            return report;
        }

        /// <summary>
        /// The co-op completion rules (Phase 23): the joined client composes and plays the same expedition. Each rule is
        /// a way the run could quietly go back to "the client owns a character in an empty Shelter".
        /// </summary>
        private static void ValidateCompletion(Report report, CompositionFacts facts)
        {
            var scene = Source(ExpeditionScenePath) + "\n" + Source(ExpeditionCoopPath);
            var rig = Source(RigPath);
            var host = Source(HostWorldPath);
            var client = Source(ClientWorldPath);
            var link = Source(LinkPath);
            var transit = Source(TransitPath);

            Line Rule(string rule, string subject, string detail) { var l = new Line { Rule = rule, Subject = subject, Detail = detail }; report.Lines.Add(l); return l; }
            void Require(Line line, bool ok, string problem) { if (!ok) line.Problems.Add(problem); }

            var composition = Rule("client composition", "joined client builds the expedition", "ExpeditionScene resolves Solo/Host/Client and composes the client branch from the host's start");
            Require(composition, scene.Contains("CoopRunMode.Client", StringComparison.Ordinal) && scene.Contains("BuildWhenClientReady", StringComparison.Ordinal), "the run has no client composition branch: a joined client stays in the Shelter");
            Require(composition, scene.Contains("ComposeClientParty", StringComparison.Ordinal), "the client composes no party view of the host's members");

            var depthSync = Rule("depth sync", "NetworkDungeonSync has a runtime caller", "the host publishes every depth; the client rebuilds from it and verifies the fingerprints");
            Require(depthSync, host.Contains("_bus.PublishDepth(", StringComparison.Ordinal), "nothing publishes the host's depth payload at runtime");
            Require(depthSync, link.Contains("Depth.Publish(", StringComparison.Ordinal), "the session link never writes NetworkDungeonSync");
            Require(depthSync, scene.Contains("TryGetHostDepthPayload", StringComparison.Ordinal) && scene.Contains("hostPayload.Rounds", StringComparison.Ordinal), "the client does not consume the host's seed/depth/round (it would roll its own)");
            Require(depthSync, scene.Contains("ClientDepthDesync", StringComparison.Ordinal) && scene.Contains("DungeonFingerprints.Layout", StringComparison.Ordinal), "the client never verifies its rebuild against the host's fingerprints");

            var attach = Rule("local player attach", "PlayerRig attaches to the owned NetworkObject", "no second, hidden gameplay player");
            Require(attach, rig.Contains("public GameObject Attach(GameObject owned", StringComparison.Ordinal), "PlayerRig cannot compose onto an existing (owned) object");
            Require(attach, scene.Contains("_rig.Attach(", StringComparison.Ordinal), "the run never attaches its player to the owned network object");
            Require(attach, rig.Contains("GetComponent<PlayerMovement>()?.SetInputReader(Reader)", StringComparison.Ordinal), "the attached object's movement keeps its spawn-time reader");

            var enemies = Rule("enemy sync", "EnemyNetSync has a runtime caller", "host registers every actor; client builds replicas; client spawner refuses");
            Require(enemies, host.Contains("AuthoritativeEnemySpawner.Capture(", StringComparison.Ordinal), "nothing captures host actors for replication");
            Require(enemies, client.Contains("new EnemyReplicaRegistry(", StringComparison.Ordinal), "nothing builds client replicas");
            Require(enemies, scene.Contains("new AuthoritativeEnemySpawner(roomSpawner, new CoopClientAuthority())", StringComparison.Ordinal), "a client's rooms could spawn enemies of their own");
            Require(enemies, scene.Contains("BossSpawner = Mode == CoopRunMode.Client ? null", StringComparison.Ordinal), "a client could spawn its own boss");

            var linkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPlayerPrefabAuthoring.LinkPrefabPath);
            var registration = Rule("network registration", "session link prefab", linkPrefab != null ? NetworkPlayerPrefabAuthoring.LinkPrefabPath : "(missing)");
            Require(registration, linkPrefab != null, "the co-op expedition channel prefab does not exist");
            if (linkPrefab != null)
            {
                Require(registration, RegisteredNetworkPrefab(linkPrefab), "the link prefab is not registered in DefaultNetworkPrefabs.asset");
                var components = linkPrefab.GetComponents<Component>();
                Require(registration, components.All(c => c != null), "the link prefab carries a missing script (release builds strip it)");
                Require(registration, linkPrefab.GetComponent<Unity.Netcode.NetworkObject>() != null && linkPrefab.GetComponent<CoopRunLink>() != null && linkPrefab.GetComponent<NetworkDungeonSync>() != null, "the link prefab lacks NetworkObject / CoopRunLink / NetworkDungeonSync");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<GameContentCatalog>("Assets/Game/Resources/GameContentCatalog.asset");
            Require(registration, catalog != null && catalog.CoopRunLink != null, "the content catalog does not carry the link prefab (a built player could never register it)");
            Require(registration, Source(LiveServicePath).Contains("CoopLinkSpawner.Install", StringComparison.Ordinal), "the shipped NetworkManager composition never registers/spawns the link");

            var loot = Rule("client loot authority", "clients never resolve shared loot", "client arbiter refuses locally; only presentation interactions run on the client");
            Require(loot, scene.Contains("PickupArbiter.Items = (_, _) => false", StringComparison.Ordinal), "a client pickup could resolve locally");
            Require(loot, scene.Contains("PlayerInteractor.LocalInteractionFilter =", StringComparison.Ordinal), "a client's Interact could open chests/events locally");
            Require(loot, !client.Contains("TryPickUp(", StringComparison.Ordinal) && !client.Contains("RequestPickup(", StringComparison.Ordinal), "the client world resolves pickups itself");

            var roomClear = Rule("client room authority", "clients never resolve room clear", "client rooms are non-authoritative and take the host's lifecycle");
            Require(roomClear, client.Contains("SetAuthoritative(false)", StringComparison.Ordinal) && scene.Contains("runtime.SetAuthoritative(false)", StringComparison.Ordinal), "a client's rooms stay authoritative");
            Require(roomClear, client.Contains("room.RestoreState(", StringComparison.Ordinal), "the client never applies the host's room state");

            var pauseCoop = Rule("co-op time", "client co-op UI never pauses global time", "no Time.timeScale in the co-op runtime");
            foreach (var (path, text) in new[] { (ExpeditionCoopPath, Source(ExpeditionCoopPath)), (HostWorldPath, host), (ClientWorldPath, client) })
                Require(pauseCoop, !text.Contains("Time.timeScale =", StringComparison.Ordinal), Path.GetFileName(path) + " writes Time.timeScale");

            var descend = Rule("networked depth transition", "every peer descends", "the host's single result runs each peer's own Descend/Return");
            Require(descend, transit.Contains("public bool ResolveFromAuthority(", StringComparison.Ordinal), "a client decision cannot take the host's result");
            Require(descend, scene.Contains("OnClientTransitResolved", StringComparison.Ordinal) && scene.Contains("ResolveFromAuthority(", StringComparison.Ordinal), "the client never applies the host's Transit result: only the host would descend");
            Require(descend, host.Contains("CoopKinds.TransitResolved", StringComparison.Ordinal), "the host never publishes the Transit result");

            var reconnect = Rule("live reconnect", "reconnect restores the current state", "host holds and hands back the character; client recomposes and resyncs");
            Require(reconnect, host.Contains("public void ServeResync(", StringComparison.Ordinal) && host.Contains("SendAllRooms(clientId)", StringComparison.Ordinal), "a reconnecting client would get no current room state");
            Require(reconnect, scene.Contains("RequestResync()", StringComparison.Ordinal), "a (re)composed client never asks for the current state");
            Require(reconnect, scene.Contains("ReconnectLoop", StringComparison.Ordinal) && scene.Contains("TransferOwnership(", StringComparison.Ordinal), "no reconnect path hands the held character back");

            var proof = Rule("runtime proof", "real co-op proof fixture", "built-player multi-process expedition proof");
            Require(proof, File.Exists(ProofPath) || (_overrides != null && _overrides.ContainsKey(ProofPath) && !string.IsNullOrEmpty(_overrides[ProofPath])), "the built-player co-op expedition proof is absent");
            Require(proof, Source(GameAppPath).Contains("CoopExpeditionProof.Begin", StringComparison.Ordinal), "the shipped player cannot run the co-op expedition proof");

            if (facts == null) return;
            var diverge = Rule("room state fixture", "host and client agree", $"divergences={facts.RoomStateDivergences?.ToString() ?? "not supplied"}");
            Require(diverge, facts.RoomStateDivergences == 0, facts.RoomStateDivergences == null ? "no host/client room fixture was supplied" : $"{facts.RoomStateDivergences} room(s) diverged between host and client");
            var partySet = Rule("client party view", "duo/trio match the participant set", $"duo={facts.ClientDuoComposed?.ToString() ?? "-"} trio={facts.ClientTrioComposed?.ToString() ?? "-"}");
            Require(partySet, facts.ClientDuoComposed == 2 && facts.ClientTrioComposed == 3, "a client's party view does not match the run's participants");
            Require(partySet, facts.ClientRemoteLocalPresentation == 0, "a client's view of the other members added camera/listener/input");
            var resync = Rule("resync fixture", "no duplicate, no rewind", $"duplicates={facts.ResyncDuplicates?.ToString() ?? "-"} localHits={facts.ClientLocalHitsApplied?.ToString() ?? "-"}");
            Require(resync, facts.ResyncDuplicates == 0, "a resync duplicated replicas or rewound a room");
            Require(resync, facts.ClientLocalHitsApplied == 0, "a client applied damage without the host");
        }

        /// <summary>Files under Assets/Game/Scripts (runtime + composition roots, never Tests) that construct the needle.</summary>
        private static List<string> RuntimeCallersOf(string needle)
        {
            var root = "Assets/Game/Scripts";
            if (!Directory.Exists(root)) return new List<string>();
            return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Replace('\\', '/').Contains("/Editor/", StringComparison.Ordinal))
                .Where(f => File.ReadAllText(f).Contains(needle, StringComparison.Ordinal))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return 0;
            var count = 0;
            for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)) count++;
            return count;
        }

        private static bool RegisteredNetworkPrefab(GameObject prefab)
        {
            const string path = "Assets/DefaultNetworkPrefabs.asset";
            if (!File.Exists(path) || prefab == null) return false;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            return !string.IsNullOrEmpty(guid) && File.ReadAllText(path).Contains(guid, StringComparison.Ordinal);
        }
    }
}
