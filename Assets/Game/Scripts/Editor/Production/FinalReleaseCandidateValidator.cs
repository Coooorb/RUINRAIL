using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RuinRail.App;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The final release-candidate gate. It aggregates the pass validators (it never re-implements them) and adds the
    /// release contracts nothing else owned: the approved scenes in order, the save version and its unbroken migration
    /// chain, the deepest-depth field, the starter kit, network registration (player entity + the one session link the
    /// enemies replicate through, by unique definition id), a single AudioListener composition site, the pixel UI font,
    /// every shipping screen present and composed, an input action set whose every action has a rebind label, both
    /// schemes' glyphs and a rebind path (or is documented as fixed), the frozen release snapshot against the committed
    /// baseline, a release-smoke manifest that maps every critical path to a deterministic scenario, and superseded
    /// current-status documents carrying their banner.
    ///
    /// <see cref="Validate(Facts)"/> takes its inputs so a test can feed a deliberately broken fixture and prove each
    /// rule fails.
    /// </summary>
    public static class FinalReleaseCandidateValidator
    {
        public const string ReportPath = "TestResults/final_release_candidate.md";
        public const string BaselinePath = "production/FINAL_RELEASE_FROZEN_BASELINE.csv";
        public const string SmokeManifestPath = "scripts/release_smoke_manifest.csv";
        public const string CurrentStatusPath = "production/FINAL_RELEASE_CANDIDATE_AUDIT.md";
        public const string SupersededMarker = "SUPERSEDED";

        /// <summary>Deterministic release-smoke scenarios that must exist in the manifest.</summary>
        public static readonly string[] RequiredScenarios = { "fresh", "returning", "death", "cache", "duo", "trio" };

        /// <summary>Critical shipping paths every one of which must map to at least one deterministic scenario.</summary>
        public static readonly string[] CriticalPaths =
        {
            "boot and main menu", "fresh profile creation", "shelter stations", "solo expedition D1", "combat", "loot and ammo",
            "inventory", "merchant", "non-combat events", "weapon cache", "boss", "transit descend", "next depth", "return and extraction",
            "save and reload", "death and run lost", "leave to main menu", "settings", "controller navigation", "returning profile",
            "attribute effects", "storage", "shelter trader", "co-op duo full loop", "co-op revive", "co-op transit vote",
            "co-op reconnect", "co-op trio composition", "defibrillator"
        };

        /// <summary>Shipping screens / screen models that must exist and be composed by runtime code.</summary>
        public static readonly string[] RequiredScreens =
        {
            "MainMenuScreen", "ShelterOnboardingViewModel", "BaseHubScreen", "CharacterPanelViewModel", "StoragePanelViewModel",
            "TraderPanelViewModel", "TerminalViewModel", "SettingsPanel", "CodexPanel", "DungeonHudView", "InventoryView",
            "MerchantView", "WeaponCacheView", "PauseMenuScreen", "RunFailedScreen", "ExpeditionSummaryViewModel", "TransitVoteViewModel"
        };

        /// <summary>
        /// Actions deliberately without a rebindable binding, documented in ui/90 (CONTROLS): Pause, and Aim — the mouse
        /// pointer position / the right-stick axis, which the rebinder by rule does not offer (buttons and composite
        /// parts only).
        /// </summary>
        public static readonly string[] DocumentedFixedActions = { "Pause", "Aim" };

        /// <summary>Current-status documents a later pass superseded: each must carry the banner pointing to the current audit.</summary>
        public static readonly string[] SupersededCurrentDocs =
        {
            "production/131_CURRENT_PRODUCTION_STATE_AFTER_TASK_148.md", "production/AUDIO_EVENT_AUDIT.md", "production/MUSIC_ASSET_AUDIT.md",
            "production/ANIMATION_ASSET_AUDIT.md", "production/FULL_GAME_COMPREHENSIVE_REVIEW.md", "production/FINAL_RELEASE_COMPLETION_REPORT.md",
            "production/FINAL_SHIPPABLE_V1_REPORT.md", "production/RELEASE_CANDIDATE_REPORT.md", "production/RELEASE_VALIDATION_REPORT.md",
            "production/MULTIPLAYER_GATE_REPORT.md", "production/COOP_RUNTIME_COMPOSITION_REPORT.md"
        };

        public sealed class Facts
        {
            public string[] ApprovedScenes = Array.Empty<string>();
            public string[] BuildSettingsScenes = Array.Empty<string>();
            public string[] MissingSceneFiles = Array.Empty<string>();
            public Dictionary<string, bool> SubValidators = new();
            public int SaveVersion;
            public int OldestSupportedSaveVersion;
            public bool MigrationChainUnbroken;
            public bool DeepestDepthField;
            public string[] StarterKit = Array.Empty<string>();
            public string[] StarterKitUnresolved = Array.Empty<string>();
            public string[] RegisteredNetworkPrefabs = Array.Empty<string>();
            public bool CatalogPlayerEntity;
            public bool CatalogSessionLink;
            public string[] ReplicatedDefinitionIds = Array.Empty<string>();
            public int AudioListenerSites;
            public string UiFontName = string.Empty;
            public Dictionary<string, bool> Screens = new();
            public string[] InputActions = Array.Empty<string>();
            public string[] ActionsWithoutLabel = Array.Empty<string>();
            public string[] ActionsWithoutKeyboardGlyph = Array.Empty<string>();
            public string[] ActionsWithoutGamepadGlyph = Array.Empty<string>();
            public string[] ActionsWithoutRebind = Array.Empty<string>();
            public Dictionary<string, string> Snapshot = new(StringComparer.Ordinal);
            public Dictionary<string, string> Baseline;
            public List<(string Path, string Scenario)> SmokeManifest = new();
            public Dictionary<string, bool> SupersededBanners = new();
            public bool CurrentStatusDocument;

            // ---- final release cleanup: co-op reactive passives + the rebinding contract ----
            public string[] ImpactMechanics = Array.Empty<string>();
            public string[] ImpactMechanicsWithoutPassive = Array.Empty<string>();
            public string[] ImpactMechanicsOnMemberRig = Array.Empty<string>();
            public bool RemoteMirrorComposesImpactPassives;
            public bool PassiveWorldActionsComposed;
            public bool RemoteMirrorDerivesFromMirroredEquipment;
            public bool RemoteMirrorRefreshesOnSnapshot;
            public bool ClientRigExcludesImpactPassives;
            public bool RigTicksPassivesAndRaisesDamage;
            public string[] ClientToHostPassiveKinds = Array.Empty<string>();
            public string[] MissingRemoteConsequenceProofs = Array.Empty<string>();
            public bool InputDocBlanketRebindClaim;
            public bool InputDocStatesAimIsFixedAxis;
            /// <summary>"scheme:action" bindings that are fixed in the rebinder (no rebindable entry in that scheme).</summary>
            public string[] FixedBindings = Array.Empty<string>();
        }

        /// <summary>The fixed (non-rebindable) scheme bindings technical/116 documents: Pause, Aim, and the gamepad Move stick.</summary>
        public static readonly string[] DocumentedFixedBindings = { "Keyboard&Mouse:Aim", "Keyboard&Mouse:Pause", "Gamepad:Move", "Gamepad:Aim", "Gamepad:Pause" };

        public const string InputDocPath = "technical/116_INPUT_SYSTEM.md";
        public const string ReactivePassiveTestPath = "Assets/Game/Tests/PlayMode/CoopReactivePassiveTests.cs";
        public const string CoopProofPath = "Assets/Game/Scripts/App/CoopExpeditionProof.cs";

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
            public bool Fails(string rule) => Lines.Any(l => l.Rule == rule && !l.Pass);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL final release-candidate contract");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count} rules checked, {Lines.Count - Failed} clean, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Rule | Subject | Detail | Result |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var l in Lines)
                    sb.AppendLine($"| {l.Rule} | {l.Subject} | {l.Detail.Replace("|", "/")} | {(l.Pass ? "PASS" : "FAIL: " + string.Join("; ", l.Problems).Replace("|", "/"))} |");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Final Release Candidate")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? ".");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        /// <summary>Batch entry: writes the committed frozen baseline from the current tree (run once, deliberately).</summary>
        public static void WriteBaselineBatch()
        {
            File.WriteAllText(BaselinePath, FinalReleaseSnapshot.ToCsv(FinalReleaseSnapshot.Collect()));
            Debug.Log("[RELEASE] frozen baseline written → " + BaselinePath);
        }

        public static Report Validate() => Validate(Collect());

        // ------------------------------------------------------------------ facts from the real project

        public static Facts Collect()
        {
            var f = new Facts
            {
                ApprovedScenes = ReleaseBuildTool.ScenePaths,
                BuildSettingsScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray()
            };
            f.MissingSceneFiles = f.ApprovedScenes.Where(p => !File.Exists(p)).ToArray();

            f.SubValidators["FinalProductionValidator"] = FinalProductionValidator.ValidateProject().DataPass;
            f.SubValidators["ContentCountValidator"] = ContentCountValidator.ValidateProject().Pass;
            f.SubValidators["StatConsumerIntegrityValidator"] = StatConsumerIntegrityValidator.Validate().Pass;
            f.SubValidators["RunVarietyDepthRetentionValidator"] = RunVarietyDepthRetentionValidator.Validate().Pass;
            f.SubValidators["PresentationAudioUxQolValidator"] = PresentationAudioUxQolValidator.Validate().Pass;
            f.SubValidators["CoopRuntimeCompositionValidator"] = CoopRuntimeCompositionValidator.Validate().Pass;

            var pipeline = SaveMigrationPipeline.Default;
            f.SaveVersion = SaveSlot.CurrentVersion;
            f.OldestSupportedSaveVersion = pipeline.OldestSupportedVersion;
            f.MigrationChainUnbroken = Enumerable.Range(pipeline.OldestSupportedVersion, SaveSlot.CurrentVersion - pipeline.OldestSupportedVersion + 1).All(pipeline.Supports);
            f.DeepestDepthField = typeof(PlayerProfile).GetField("DeepestDepthReached", BindingFlags.Public | BindingFlags.Instance) != null;

            var catalog = GameContentCatalog.Load();
            var kit = StarterKitService.CreateKit();
            f.StarterKit = kit.Select(k => $"{k.item.DefinitionId} x{k.item.Quantity}{(k.slot.HasValue ? " @" + k.slot.Value : "")}").ToArray();
            f.StarterKitUnresolved = kit.Where(k => catalog == null || catalog.Items.All(i => i == null || i.Id != k.item.DefinitionId)).Select(k => k.item.DefinitionId).ToArray();

            var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>(NetworkPlayerPrefabAuthoring.PrefabListPath);
            f.RegisteredNetworkPrefabs = list != null ? list.PrefabList.Where(p => p?.Prefab != null).Select(p => AssetDatabase.GetAssetPath(p.Prefab)).ToArray() : Array.Empty<string>();
            f.CatalogPlayerEntity = catalog != null && catalog.NetworkPlayerEntity != null;
            f.CatalogSessionLink = catalog != null && catalog.CoopRunLink != null;
            if (catalog != null)
                f.ReplicatedDefinitionIds = catalog.Enemies.Where(e => e != null).Select(e => e.Id)
                    .Concat(catalog.Elites.Where(e => e != null).Select(e => e.Id))
                    .Concat(catalog.Bosses.Where(b => b != null).Select(b => b.Id)).ToArray();

            f.AudioListenerSites = RuntimeSources().Count(s => s.Value.Contains("AddComponent<AudioListener>"));
            var font = RuinRail.UI.Theme.UiFont.Font();
            f.UiFontName = font != null ? font.name : string.Empty;

            var sources = RuntimeSources();
            foreach (var screen in RequiredScreens)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().SelectMany(SafeTypes).FirstOrDefault(t => t.Name == screen && (t.Namespace ?? "").StartsWith("RuinRail"));
                // Composed = an instantiation site exists in runtime code (AddComponent / new / a static Build, often in
                // the screen's own file) AND a shipping file other than its own reaches it. Proof and smoke harness files
                // do not count as the release path.
                var token = new System.Text.RegularExpressions.Regex(@"\b" + screen + @"\b");
                // Static builders (SettingsPanel.Build / CodexPanel.Build) count too: they are how those panels are composed.
                var instantiated = sources.Values.Any(text => text.Contains("AddComponent<" + screen + ">") || text.Contains("new " + screen + "(") || text.Contains(screen + ".Build("));
                var reached = sources.Any(s => !s.Key.EndsWith("/" + screen + ".cs") && !IsHarness(s.Key) && token.IsMatch(s.Value));
                var composed = type != null && instantiated && reached;
                f.Screens[screen] = composed;
            }

            var reader = new RuinRail.Core.Input.PlayerInputReader();
            try
            {
                var map = reader.Asset.FindActionMap("Player");
                f.InputActions = map != null ? map.actions.Select(a => a.name).ToArray() : Array.Empty<string>();
                var labels = typeof(RuinRail.Core.Input.InputRebinder).GetField("ActionLabels", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as Dictionary<string, string>;
                f.ActionsWithoutLabel = f.InputActions.Where(a => labels == null || !labels.ContainsKey(a)).ToArray();
                var kb = new RuinRail.UI.Onboarding.SchemeGlyphs(RuinRail.UI.Onboarding.InputScheme.KeyboardMouse);
                var gp = new RuinRail.UI.Onboarding.SchemeGlyphs(RuinRail.UI.Onboarding.InputScheme.Gamepad);
                f.ActionsWithoutKeyboardGlyph = f.InputActions.Where(a => string.IsNullOrWhiteSpace(kb.For(a)) || kb.For(a) == a).ToArray();
                f.ActionsWithoutGamepadGlyph = f.InputActions.Where(a => string.IsNullOrWhiteSpace(gp.For(a)) || gp.For(a) == a).ToArray();
                using var rebinder = new RuinRail.Core.Input.InputRebinder(reader.Asset);
                f.ActionsWithoutRebind = f.InputActions.Where(a => !rebinder.Entries.Any(e => e.ActionName == a && e.IsRebindable)).ToArray();
            }
            finally
            {
                reader.Dispose();
            }

            foreach (var row in FinalReleaseSnapshot.Collect()) f.Snapshot[row.Key] = row.Value;
            f.Baseline = File.Exists(BaselinePath) ? FinalReleaseSnapshot.ParseCsv(File.ReadAllText(BaselinePath)) : null;

            if (File.Exists(SmokeManifestPath))
            {
                foreach (var line in File.ReadAllLines(SmokeManifestPath).Skip(1))
                {
                    var cells = line.Split(',');
                    if (cells.Length >= 2 && !string.IsNullOrWhiteSpace(cells[0])) f.SmokeManifest.Add((cells[0].Trim(), cells[1].Trim()));
                }
            }

            foreach (var doc in SupersededCurrentDocs)
                f.SupersededBanners[doc] = File.Exists(doc) && File.ReadLines(doc).Take(8).Any(l => l.Contains(SupersededMarker) && l.Contains(CurrentStatusPath));
            f.CurrentStatusDocument = File.Exists(CurrentStatusPath);

            // Reactive passives on a remote member's body: composed by the host from the mirrored equipment, never run
            // by the member's own rig, never triggerable by a member message.
            f.ImpactMechanics = RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IncomingImpactMechanics.ToArray();
            f.ImpactMechanicsWithoutPassive = f.ImpactMechanics.Where(m => RuinRail.Gameplay.Items.Passives.EquipmentPassiveFactory.Create(m) == null).ToArray();
            f.ImpactMechanicsOnMemberRig = f.ImpactMechanics.Where(RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IsMemberRigMechanic).ToArray();
            var coop = Read("Assets/Game/Scripts/App/ExpeditionScene.Coop.cs");
            var scene = Read("Assets/Game/Scripts/App/ExpeditionScene.cs");
            var rig = Read("Assets/Game/Scripts/App/PlayerRigComposer.cs");
            f.RemoteMirrorComposesImpactPassives = coop.Contains("EquipmentPassiveRegistrar.IsHostResolvedMechanic") && RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IncomingImpactMechanics.All(RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IsHostResolvedMechanic) && coop.Contains("GetComponent<PlayerImpactReceiver>()?.SetEvents(Events)")
                && coop.Contains("_health.Damaged += Events.RaiseDamageTaken") && coop.Contains("EquipmentPassiveTicker.On(entity)");
            f.RemoteMirrorDerivesFromMirroredEquipment = coop.Contains("Passives = new EquipmentPassiveRegistrar(Inventory,");
            // Emergency Vent / Discharge / Arc Stagger / Room Sweep act only through PassiveWorldActions: both passive
            // contexts (the rig and the host's member copy) must be given the composed world, not the None fallback.
            f.PassiveWorldActionsComposed = rig.Contains("new PlayerPassiveWorld(Player, GroundPickups)") && rig.Contains("PassiveWorld.Actions), PassiveAdmit)")
                && coop.Contains("World = new PlayerPassiveWorld(entity, groundPickups)") && coop.Contains("World.Actions),") && coop.Contains("_roomRelay.SetEvents(Events)")
                && scene.Contains("GroundPickups = () => _services.GroundLoot.Tracked");
            f.RemoteMirrorRefreshesOnSnapshot = coop.Contains("Passives?.Refresh()");
            f.ClientRigExcludesImpactPassives = scene.Contains("PassiveAdmit = Mode == CoopRunMode.Client ? RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IsMemberRigMechanic");
            f.RigTicksPassivesAndRaisesDamage = rig.Contains("EquipmentPassiveTicker.On(Player)") && rig.Contains("Damaged += CombatEvents.RaiseDamageTaken");
            f.ClientToHostPassiveKinds = typeof(RuinRail.Networking.CoopKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(fi => fi.IsLiteral && fi.FieldType == typeof(string)).Select(fi => (string)fi.GetRawConstantValue())
                .Where(RuinRail.Networking.CoopKinds.IsClientToHost)
                .Where(k => k.Contains("passive") || k.Contains("negate") || k.Contains("stagger") || k.Contains("knockback")).ToArray();
            var tests = Read(ReactivePassiveTestPath);
            var proof = Read(CoopProofPath);
            var missing = new List<string>();
            foreach (var (mechanic, armor, proofLine) in new[] { ("anchored", "armor_riot_armor", "Anchored on a remote client"), ("shock_absorber", "armor_blast_suit", "Shock Absorber on a remote client"), ("exo_lock", "armor_reinforced_exo_rig", "Exo Lock on a remote client") })
            {
                if (!tests.Contains("RemoteMember") || !tests.Contains(armor)) missing.Add(mechanic + ": no PlayMode remote-member consequence test");
                if (!proof.Contains(proofLine)) missing.Add(mechanic + ": no built-player host/client proof step");
            }

            f.MissingRemoteConsequenceProofs = missing.ToArray();

            // The rebinding contract: technical/116 vs the rebinder's real per-scheme data.
            var inputDoc = Read(InputDocPath);
            f.InputDocBlanketRebindClaim = inputDoc.IndexOf("All gameplay bindings must support rebinding", StringComparison.OrdinalIgnoreCase) >= 0;
            f.InputDocStatesAimIsFixedAxis = inputDoc.Contains("Aim is positional/analog input, not a key binding") && inputDoc.Contains("right-stick axis") && inputDoc.Contains("mouse pointer position");
            var fixedReader = new RuinRail.Core.Input.PlayerInputReader();
            try
            {
                using var fixedRebinder = new RuinRail.Core.Input.InputRebinder(fixedReader.Asset);
                f.FixedBindings = fixedRebinder.Entries.GroupBy(e => e.Scheme + ":" + e.ActionName).Where(g => g.All(e => !e.IsRebindable)).Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
            }
            finally
            {
                fixedReader.Dispose();
            }

            return f;
        }

        /// <summary>Built-player proof/smoke harness sources: they exercise screens, they are not what composes them.</summary>
        private static bool IsHarness(string path) => path.Contains("/SmokeRunner") || path.Contains("/CoopExpeditionProof") || path.Contains("/CoopPeerRunner") || path.Contains("/PlayerProfileRunner") || path.Contains("/ProofInputReader");

        private static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
        }

        /// <summary>Runtime C# sources (no Editor code, no tests), path → text.</summary>
        private static Dictionary<string, string> RuntimeSources() =>
            Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/Editor/"))
                .ToDictionary(p => p, File.ReadAllText);

        // ------------------------------------------------------------------ rules

        public static Report Validate(Facts f)
        {
            var report = new Report();
            Line Add(string rule, string subject, string detail)
            {
                var line = new Line { Rule = rule, Subject = subject, Detail = detail ?? string.Empty };
                report.Lines.Add(line);
                return line;
            }

            // 1. scenes
            var scenes = Add("scenes", "approved order in the build", string.Join(", ", f.BuildSettingsScenes.Select(Path.GetFileNameWithoutExtension)));
            if (f.ApprovedScenes.Length != 4) scenes.Problems.Add($"expected 4 approved scenes, found {f.ApprovedScenes.Length}");
            if (!f.BuildSettingsScenes.SequenceEqual(f.ApprovedScenes)) scenes.Problems.Add("build settings differ from the approved scene order: " + string.Join(", ", f.ApprovedScenes));
            if (f.MissingSceneFiles.Length > 0) scenes.Problems.Add("missing scene files: " + string.Join(", ", f.MissingSceneFiles));
            if (f.BuildSettingsScenes.Any(s => s.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0)) scenes.Problems.Add("a test scene is in the build");

            // 2. the pass validators, aggregated
            foreach (var pair in f.SubValidators.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var sub = Add("pass validator", pair.Key, pair.Value ? "PASS" : "FAIL");
                if (!pair.Value) sub.Problems.Add(pair.Key + " fails (see its own report)");
            }

            foreach (var required in new[] { "FinalProductionValidator", "ContentCountValidator", "StatConsumerIntegrityValidator", "RunVarietyDepthRetentionValidator", "PresentationAudioUxQolValidator", "CoopRuntimeCompositionValidator" })
                if (!f.SubValidators.ContainsKey(required)) Add("pass validator", required, "not run").Problems.Add("the aggregate did not run " + required);

            // 3. save
            var save = Add("save", "version + migration chain + deepest-depth field", $"v{f.SaveVersion}, oldest supported v{f.OldestSupportedSaveVersion}");
            if (f.SaveVersion < 2) save.Problems.Add($"save version {f.SaveVersion} is below the shipped schema 2");
            if (!f.MigrationChainUnbroken) save.Problems.Add("a supported save version has no unbroken migration chain to the current version");
            if (f.OldestSupportedSaveVersion > 1) save.Problems.Add("v1 saves are no longer supported");
            if (!f.DeepestDepthField) save.Problems.Add("PlayerProfile.DeepestDepthReached is missing");

            // 4. starter kit
            var kit = Add("starter kit", "P9 / Field Knife / Scrap Vest / Bandage / 60 light", string.Join("; ", f.StarterKit));
            var expectedKit = new[]
            {
                StarterKitService.PistolId + " x1 @PrimaryWeapon", StarterKitService.KnifeId + " x1 @SecondaryWeapon", StarterKitService.VestId + " x1 @Armor",
                StarterKitService.BandageId + " x" + StarterKitService.BandageCount + " @ActiveConsumable", StarterKitService.LightAmmoId + " x" + StarterKitService.LightAmmoCount
            };
            if (!expectedKit.OrderBy(s => s).SequenceEqual(f.StarterKit.OrderBy(s => s))) kit.Problems.Add("the kit differs from the approved starter kit: " + string.Join("; ", expectedKit));
            if (f.StarterKitUnresolved.Length > 0) kit.Problems.Add("kit items not in the catalog: " + string.Join(", ", f.StarterKitUnresolved));

            // 5. network registration
            var net = Add("network registration", "player entity + session link", string.Join(", ", f.RegisteredNetworkPrefabs.Select(Path.GetFileName)));
            if (!f.RegisteredNetworkPrefabs.Contains(NetworkPlayerPrefabAuthoring.PrefabPath)) net.Problems.Add("the player network entity is not in the NGO prefab list");
            if (!f.RegisteredNetworkPrefabs.Contains(NetworkPlayerPrefabAuthoring.LinkPrefabPath)) net.Problems.Add("the co-op session link is not in the NGO prefab list");
            if (!f.CatalogPlayerEntity || !f.CatalogSessionLink) net.Problems.Add("the content catalog does not reference both network prefabs");
            var enemies = Add("enemy network registration", "replicated by unique definition id over the session link", f.ReplicatedDefinitionIds.Length + " definitions");
            if (f.ReplicatedDefinitionIds.Length == 0) enemies.Problems.Add("no enemy/elite/boss definitions to replicate");
            if (f.ReplicatedDefinitionIds.Any(string.IsNullOrWhiteSpace)) enemies.Problems.Add("a definition has no id (it could not be replicated)");
            var duplicate = f.ReplicatedDefinitionIds.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicate.Count > 0) enemies.Problems.Add("duplicate replication ids: " + string.Join(", ", duplicate));
            if (!f.RegisteredNetworkPrefabs.Contains(NetworkPlayerPrefabAuthoring.LinkPrefabPath)) enemies.Problems.Add("the session link the enemies replicate through is not registered");

            // 6. one listener, 7. the UI face
            var listener = Add("audio listener", "one composition site", f.AudioListenerSites + " site(s)");
            if (f.AudioListenerSites != 1) listener.Problems.Add($"expected exactly one AddComponent<AudioListener> in runtime code, found {f.AudioListenerSites}");
            var font = Add("ui font", "the authored pixel face", f.UiFontName);
            if (f.UiFontName != "ruinrail_pixel") font.Problems.Add($"the UI face is '{f.UiFontName}', not the authored ruinrail_pixel (placeholder/fallback font)");

            // 8. screens
            foreach (var screen in RequiredScreens)
            {
                var composed = f.Screens.TryGetValue(screen, out var ok) && ok;
                var line = Add("ui screen", screen, composed ? "composed by runtime code" : "missing");
                if (!composed) line.Problems.Add("the screen type is missing or nothing on the release path composes it");
            }

            // 9. input / settings actions
            var actions = Add("input actions", "labels, glyphs, rebind paths", $"{f.InputActions.Length} actions: {string.Join(", ", f.InputActions)}");
            foreach (var a in PresentationAudioUxQolValidator.RequiredInputActions) if (!f.InputActions.Contains(a)) actions.Problems.Add("missing action " + a);
            if (f.InputActions.Length != PresentationAudioUxQolValidator.RequiredInputActions.Count()) actions.Problems.Add($"expected {PresentationAudioUxQolValidator.RequiredInputActions.Count()} actions, found {f.InputActions.Length}");
            if (f.ActionsWithoutLabel.Length > 0) actions.Problems.Add("no rebind label: " + string.Join(", ", f.ActionsWithoutLabel));
            if (f.ActionsWithoutKeyboardGlyph.Length > 0) actions.Problems.Add("no keyboard glyph: " + string.Join(", ", f.ActionsWithoutKeyboardGlyph));
            if (f.ActionsWithoutGamepadGlyph.Length > 0) actions.Problems.Add("no gamepad glyph: " + string.Join(", ", f.ActionsWithoutGamepadGlyph));
            var undocumented = f.ActionsWithoutRebind.Except(DocumentedFixedActions).ToList();
            if (undocumented.Count > 0) actions.Problems.Add("no rebind path and not documented as fixed: " + string.Join(", ", undocumented));

            // 10. frozen release snapshot vs the committed baseline
            if (f.Baseline == null)
            {
                Add("frozen snapshot", BaselinePath, "missing").Problems.Add("the committed release baseline is missing");
            }
            else
            {
                var drift = Add("frozen snapshot", "every baseline value unchanged", $"{f.Baseline.Count} baseline values, {f.Snapshot.Count} current");
                foreach (var pair in f.Baseline.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!f.Snapshot.TryGetValue(pair.Key, out var now)) drift.Problems.Add($"{pair.Key}: no longer reported");
                    else if (now != pair.Value) drift.Problems.Add($"{pair.Key}: '{pair.Value}' → '{now}'");
                }

                foreach (var key in f.Snapshot.Keys.Where(k => !f.Baseline.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
                    drift.Problems.Add($"{key}: not in the baseline (a new frozen value must be added deliberately)");
                // Named guards for the values the owner froze explicitly, so their failure is unmistakable.
                foreach (var (name, prefix) in new[] { ("D1 ammo", "loot.supply_chest.light_ammo"), ("Field Knife", "weapon.field_knife"), ("blasters", "blaster.heat."), ("depth scaling", "depth.scaling."), ("co-op scaling", "coop.scaling") })
                {
                    var guard = Add("frozen " + name, prefix, "baseline comparison");
                    foreach (var pair in f.Baseline.Where(p => p.Key.StartsWith(prefix, StringComparison.Ordinal)))
                        if (!f.Snapshot.TryGetValue(pair.Key, out var now) || now != pair.Value) guard.Problems.Add($"{pair.Key} drifted: '{pair.Value}' → '{now}'");
                    if (!f.Baseline.Keys.Any(k => k.StartsWith(prefix, StringComparison.Ordinal))) guard.Problems.Add("no baseline value for " + prefix);
                }
            }

            // 11. release smoke manifest
            var manifest = Add("release smoke manifest", SmokeManifestPath, $"{f.SmokeManifest.Count} critical-path rows");
            if (f.SmokeManifest.Count == 0) manifest.Problems.Add("the release smoke manifest is missing or empty");
            foreach (var scenario in RequiredScenarios)
                if (f.SmokeManifest.All(r => r.Scenario != scenario)) manifest.Problems.Add("no row runs the required scenario " + scenario);
            foreach (var path in CriticalPaths)
                if (f.SmokeManifest.All(r => !string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase))) manifest.Problems.Add("critical path without a scenario: " + path);

            // 12. current-status documents
            var status = Add("current status", CurrentStatusPath, f.CurrentStatusDocument ? "present" : "missing");
            if (!f.CurrentStatusDocument) status.Problems.Add("the current release-candidate audit document is missing");
            foreach (var doc in SupersededCurrentDocs)
            {
                var banner = f.SupersededBanners.TryGetValue(doc, out var has) && has;
                var line = Add("superseded status", doc, banner ? "banner present" : "no banner");
                if (!banner) line.Problems.Add($"a superseded current-status document must open with a '{SupersededMarker}' banner pointing to {CurrentStatusPath}");
            }

            // 13. co-op reactive passives (final release cleanup)
            var mechanics = Add("reactive passives", "remote member composition", string.Join(", ", f.ImpactMechanics));
            if (f.ImpactMechanics.Length != 3 || !new[] { "anchored", "shock_absorber", "exo_lock" }.All(f.ImpactMechanics.Contains)) mechanics.Problems.Add("the incoming-impact set must be exactly anchored, shock_absorber, exo_lock");
            if (f.ImpactMechanicsWithoutPassive.Length > 0) mechanics.Problems.Add("mechanic without a passive: " + string.Join(", ", f.ImpactMechanicsWithoutPassive));
            if (!f.RemoteMirrorComposesImpactPassives) mechanics.Problems.Add("the host's copy of a remote member does not compose the incoming-impact passives (event hub on its receiver, damage signal, ticker)");
            if (!f.RigTicksPassivesAndRaisesDamage) mechanics.Problems.Add("the player rig does not tick its passives or raise DamageTaken from its health");
            if (!f.PassiveWorldActionsComposed) mechanics.Problems.Add("PassiveWorldActions is not composed into the rig's and the member copy's passive contexts (Emergency Vent / Discharge / Arc Stagger / Room Sweep would do nothing)");
            var derive = Add("reactive passives", "host derives the member's equipped passive", f.RemoteMirrorDerivesFromMirroredEquipment ? "from the mirrored inventory" : "missing");
            if (!f.RemoteMirrorDerivesFromMirroredEquipment) derive.Problems.Add("the host cannot derive the remote member's equipped reactive passive (registrar not built on the mirrored inventory)");
            if (!f.RemoteMirrorRefreshesOnSnapshot) derive.Problems.Add("an equipment snapshot does not refresh the host-side passives (stale passive after a swap)");
            var authority = Add("reactive passives", "host-authoritative, applied once", $"client rig excludes: {f.ClientRigExcludesImpactPassives}");
            if (!f.ClientRigExcludesImpactPassives) authority.Problems.Add("a co-op client's own rig may run the incoming-impact passives (they would apply twice / client-side)");
            if (f.ImpactMechanicsOnMemberRig.Length > 0) authority.Problems.Add("mechanic admitted on both the host copy and the member rig: " + string.Join(", ", f.ImpactMechanicsOnMemberRig));
            if (f.ClientToHostPassiveKinds.Length > 0) authority.Problems.Add("a client may send a passive/impact trigger: " + string.Join(", ", f.ClientToHostPassiveKinds));
            var proofs = Add("reactive passives", "remote-client consequence proofs", f.MissingRemoteConsequenceProofs.Length == 0 ? "Anchored, Shock Absorber, Exo Lock" : "missing");
            foreach (var m in f.MissingRemoteConsequenceProofs) proofs.Problems.Add(m);

            // 14. the rebinding contract (technical/116 vs the rebinder)
            var rebind = Add("rebinding contract", InputDocPath, "fixed: " + string.Join(", ", f.FixedBindings));
            if (f.InputDocBlanketRebindClaim) rebind.Problems.Add("the controls doc again claims every gameplay binding is rebindable (pointer/axis Aim is not a key binding)");
            if (!f.InputDocStatesAimIsFixedAxis) rebind.Problems.Add("the controls doc does not state that Aim is fixed positional/analog input (mouse pointer / right-stick axis)");
            var undocumentedFixed = f.FixedBindings.Except(DocumentedFixedBindings).ToList();
            var claimedFixed = DocumentedFixedBindings.Except(f.FixedBindings).ToList();
            if (undocumentedFixed.Count > 0) rebind.Problems.Add("fixed in the rebinder but not documented: " + string.Join(", ", undocumentedFixed));
            if (claimedFixed.Count > 0) rebind.Problems.Add("documented as fixed but rebindable: " + string.Join(", ", claimedFixed));

            return report;
        }
    }
}
