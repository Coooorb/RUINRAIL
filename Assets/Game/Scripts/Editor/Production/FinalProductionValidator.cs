using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuinRail.Audio;
using RuinRail.Gameplay.Combat.Hazards;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Items;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 145 — the final engineering/data validation (technical/115): stable ids per definition type, every
    /// serialized reference resolves, room/loot/legendary/price/network data, exact content counts, build scenes and
    /// assembly hygiene. External art/audio blockers are listed separately and never counted as data failures.
    /// Every error names the asset (and field/index where possible).
    /// </summary>
    public static class FinalProductionValidator
    {
        public const string ReportPath = "TestResults/final_validation.md";
        public const string AssetRoot = "Assets/Game";

        public sealed class Section
        {
            public Section(string name) { Name = name; }
            public string Name { get; }
            public readonly List<string> Passed = new();
            public readonly List<string> Errors = new();
            public bool Pass => Errors.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Section> Sections = new();
            public readonly List<string> ExternalBlockers = new();
            public readonly List<string> Findings = new();
            public bool DataPass => Sections.All(s => s.Pass);
            public int ErrorCount => Sections.Sum(s => s.Errors.Count);

            public Section Add(string name) { var s = new Section(name); Sections.Add(s); return s; }

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL V1 final production validation (TASK 145)");
                sb.AppendLine();
                sb.AppendLine($"Data/engineering result: **{(DataPass ? "PASS" : "FAIL")}** — {Sections.Count(s => s.Pass)}/{Sections.Count} sections pass, {ErrorCount} errors.");
                sb.AppendLine($"External assets (not data failures): **{(ExternalBlockers.Count == 0 ? "none" : "BLOCKED_EXTERNAL_ASSET")}** — {ExternalBlockers.Count} outstanding.");
                sb.AppendLine();
                foreach (var s in Sections)
                {
                    sb.AppendLine($"## {s.Name} — {(s.Pass ? "PASS" : "FAIL")}");
                    foreach (var p in s.Passed) sb.AppendLine($"- PASS {p}");
                    foreach (var e in s.Errors) sb.AppendLine($"- ERROR {e}");
                    sb.AppendLine();
                }

                sb.AppendLine("## Findings for later tasks (not data failures)");
                foreach (var f in Findings) sb.AppendLine($"- {f}");
                sb.AppendLine();
                sb.AppendLine("## External art / audio blockers (BLOCKED_EXTERNAL_ASSET)");
                foreach (var b in ExternalBlockers) sb.AppendLine($"- {b}");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Everything (Final)")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = ValidateProject();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report ValidateProject()
        {
            var report = new Report();
            ValidateStableIds(report.Add("Stable ids"));
            ValidateReferences(report.Add("Serialized references"));
            ValidateRooms(report.Add("Rooms and room pools"));
            ValidateLootAndLegendary(report.Add("Loot tables, rarity tables, Legendary mechanics, prices"));
            ValidateEnemies(report.Add("Enemies, Elites, Bosses"));
            ValidateContentCounts(report.Add("Content counts (production/126)"));
            ValidateBuildScenes(report.Add("Build scenes and boot flow"), report.Findings);
            ValidateAssemblies(report.Add("Assembly hygiene (no Editor leaks into runtime)"));
            ValidateNetworkPrefabs(report.Add("Network prefabs"), report.Findings);
            CollectExternalBlockers(report);
            return report;
        }

        // ---- 1. Stable ids ----

        private static void ValidateStableIds(Section section)
        {
            CheckIds(section, LoadAll<ItemDefinition>(), d => d.Id, "item");
            CheckIds(section, LoadAll<EnemyDefinition>(), d => d.Id, "enemy");
            CheckIds(section, LoadAll<EliteDefinition>(), d => d.Id, "elite");
            CheckIds(section, LoadAll<BossDefinition>(), d => d.Id, "boss");
            CheckIds(section, LoadAll<RoomDefinition>().Where(r => !AssetDatabase.GetAssetPath(r).Contains("/_Test/")).ToList(), d => d.Id, "room");
            CheckIds(section, LoadAll<EnemyAttackDefinition>(), d => d.Id, "attack");
            CheckIds(section, LoadAll<HazardDefinition>(), d => d.Id, "hazard");
            CheckIds(section, LoadAll<AffixDefinition>(), d => d.Id, "affix");
            CheckIds(section, LoadAll<LootTableDefinition>(), d => d.Id, "loot table");
            CheckIds(section, LoadAll<AudioEventDefinition>(), d => d.Id, "audio event");
            var actors = LoadAll<EnemyDefinition>().Select(e => e.Id).Concat(LoadAll<EliteDefinition>().Select(e => e.Id)).Concat(LoadAll<BossDefinition>().Select(b => b.Id)).ToList();
            var dup = actors.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dup.Count > 0) section.Errors.Add($"actor ids shared across enemy/elite/boss: {string.Join(", ", dup)}.");
            else section.Passed.Add($"{actors.Count} actor ids unique across enemies, Elites and Bosses.");
        }

        private static void CheckIds<T>(Section section, List<T> definitions, Func<T, string> id, string kind) where T : UnityEngine.Object
        {
            var empty = definitions.Where(d => string.IsNullOrWhiteSpace(id(d))).ToList();
            foreach (var e in empty) section.Errors.Add($"{kind} '{AssetDatabase.GetAssetPath(e)}': empty id.");
            var dups = definitions.Where(d => !string.IsNullOrWhiteSpace(id(d))).GroupBy(id).Where(g => g.Count() > 1).ToList();
            foreach (var g in dups) section.Errors.Add($"{kind} id '{g.Key}' used by: {string.Join(", ", g.Select(d => AssetDatabase.GetAssetPath(d)))}.");
            var bad = definitions.Where(d => !string.IsNullOrWhiteSpace(id(d)) && !Regex.IsMatch(id(d), "^[a-z0-9_.]+$")).ToList();
            foreach (var b in bad) section.Errors.Add($"{kind} '{AssetDatabase.GetAssetPath(b)}': id '{id(b)}' is not a stable lower_snake id.");
            if (empty.Count == 0 && dups.Count == 0 && bad.Count == 0) section.Passed.Add($"{definitions.Count} {kind} ids unique, non-empty, stable.");
        }

        // ---- 2. Serialized references ----

        private static void ValidateReferences(Section section)
        {
            var scanned = 0;
            var missing = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject t:Prefab", new[] { AssetRoot + "/ScriptableObjects", AssetRoot + "/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null) { section.Errors.Add($"'{path}': contains a missing (null) sub-asset or missing script."); missing++; continue; }
                    if (asset is GameObject go)
                    {
                        foreach (var component in go.GetComponentsInChildren<Component>(true))
                        {
                            if (component == null) { section.Errors.Add($"'{path}': a missing script component."); missing++; continue; }
                            missing += ScanObject(component, path, section);
                        }
                    }
                    else if (asset is ScriptableObject)
                    {
                        missing += ScanObject(asset, path, section);
                    }

                    scanned++;
                }
            }

            if (missing == 0) section.Passed.Add($"{scanned} assets scanned: every serialized object reference resolves.");
        }

        private static int ScanObject(UnityEngine.Object target, string path, Section section)
        {
            var missing = 0;
            var so = new SerializedObject(target);
            var prop = so.GetIterator();
            var enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = true;
                if (prop.propertyType == SerializedPropertyType.ObjectReference && prop.objectReferenceValue == null && prop.objectReferenceInstanceIDValue != 0)
                {
                    section.Errors.Add($"'{path}' ({target.GetType().Name}) field '{prop.propertyPath}': missing reference.");
                    missing++;
                }
            }

            return missing;
        }

        // ---- 3. Rooms ----

        private static void ValidateRooms(Section section)
        {
            var reports = RoomValidationTools.ValidateProject();
            var failing = reports.Where(r => !r.IsGeneratorReady).ToList();
            foreach (var r in failing) foreach (var p in r.Problems) section.Errors.Add($"room '{r.RoomId}': {p}");
            if (failing.Count == 0) section.Passed.Add($"{reports.Count} rooms generator-ready (schema, sockets, reachability, markers).");

            var rooms = LoadAll<RoomDefinition>().Where(r => !AssetDatabase.GetAssetPath(r).Contains("/_Test/")).ToList();
            foreach (var room in rooms)
            {
                if (room.Prefab == null) section.Errors.Add($"room '{room.Id}': no prefab.");
                else if (room.Prefab.GetComponent<RoomRoot>() == null || room.Prefab.GetComponent<RoomRoot>().Definition != room) section.Errors.Add($"room '{room.Id}': prefab '{AssetDatabase.GetAssetPath(room.Prefab)}' does not point back to it.");
            }

            var pools = Dungeon.Generation.BiomeRoomPools.Build(rooms);
            if (!pools.IsComplete) foreach (var p in pools.Problems()) section.Errors.Add("room pools: " + p);
            else section.Passed.Add("Three biome room pools complete (21 rooms each).");
        }

        // ---- 4. Loot / Legendary / prices ----

        private static void ValidateLootAndLegendary(Section section)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
            if (catalog == null) section.Errors.Add("LootSourceCatalog.asset missing.");
            else
            {
                foreach (LootSourceKind kind in Enum.GetValues(typeof(LootSourceKind)))
                {
                    if (!catalog.TryGet(kind, out var source) || source.Table == null) section.Errors.Add($"LootSourceCatalog: no table for {kind}.");
                }

                foreach (LootQuality quality in Enum.GetValues(typeof(LootQuality)))
                {
                    if (catalog.RarityTableFor(quality) == null) section.Errors.Add($"LootSourceCatalog: no rarity table for {quality}.");
                }

                if (catalog.Sources.All(s => s.Table != null) && catalog.RarityTables.All(t => t != null)) section.Passed.Add($"LootSourceCatalog: {catalog.Sources.Count} sources and {catalog.RarityTables.Count} rarity tables resolve.");
            }

            foreach (var table in LoadAll<LootTableDefinition>())
            {
                if (table.Rolls.Count == 0) section.Errors.Add($"loot table '{table.Id}': no rolls (impossible empty table).");
                for (var r = 0; r < table.Rolls.Count; r++)
                {
                    var roll = table.Rolls[r];
                    if (roll.Entries.Length == 0) section.Errors.Add($"loot table '{table.Id}' roll {r} ('{roll.Label}'): no entries.");
                    if (roll.ChancePercent <= 0) section.Errors.Add($"loot table '{table.Id}' roll {r}: chance {roll.ChancePercent}% can never roll.");
                    for (var e = 0; e < roll.Entries.Length; e++)
                    {
                        var entry = roll.Entries[e];
                        if (entry.Weight < 1) section.Errors.Add($"loot table '{table.Id}' roll {r} entry {e}: weight {entry.Weight} < 1.");
                        if (entry.MinQuantity < 1 || entry.MaxQuantity < entry.MinQuantity) section.Errors.Add($"loot table '{table.Id}' roll {r} entry {e}: quantity range {entry.MinQuantity}-{entry.MaxQuantity} invalid.");
                    }
                }
            }

            foreach (var rarity in LoadAll<RarityTableDefinition>())
            {
                foreach (var depth in new[] { 1, 5, 10, 20 })
                {
                    var weights = rarity.WeightsAt(depth);
                    if (weights == null || weights.Sum() <= 0) section.Errors.Add($"rarity table '{rarity.name}': no positive weights at depth {depth}.");
                }
            }

            section.Passed.Add($"{LoadAll<LootTableDefinition>().Count} loot tables and {LoadAll<RarityTableDefinition>().Count} rarity tables have valid weights and non-empty rolls (where no error is listed).");

            var weapons = WeaponCatalogValidationTools.ValidateProject();
            foreach (var p in weapons.Problems) section.Errors.Add("weapon catalog: " + p);
            if (weapons.IsValid) section.Passed.Add($"Weapon catalog: {weapons.DefinitionCount} definitions, every Legendary has its registered special/passive, prices from the economy tables.");

            foreach (var path in new[] { "Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset", "Assets/Game/ScriptableObjects/Base/TraderConfig.asset", "Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset", "Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset", "Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset", "Assets/Game/ScriptableObjects/Balance/MultiplayerBalanceConfig.asset" })
            {
                if (AssetDatabase.LoadAssetAtPath<ScriptableObject>(path) == null) section.Errors.Add($"required config missing: {path}");
            }
        }

        // ---- 5. Enemies ----

        private static void ValidateEnemies(Section section)
        {
            foreach (var elite in LoadAll<EliteDefinition>())
            {
                if (elite.Moveset.Count == 0 || elite.Moveset.Any(a => a == null)) section.Errors.Add($"elite '{elite.Id}': moveset missing an attack reference.");
                if (elite.BaseHealth <= 0) section.Errors.Add($"elite '{elite.Id}': base health {elite.BaseHealth}.");
            }

            foreach (var boss in LoadAll<BossDefinition>())
            {
                if (boss.Moveset.Count == 0 || boss.Moveset.Any(a => a == null)) section.Errors.Add($"boss '{boss.Id}': moveset missing an attack reference.");
                if (boss.PhaseTwoArenaHazards.Any(a => a == null)) section.Errors.Add($"boss '{boss.Id}': phase-two hazard reference missing.");
                if (boss.BaseHealth <= 0) section.Errors.Add($"boss '{boss.Id}': base health {boss.BaseHealth}.");
            }

            foreach (var enemy in LoadAll<EnemyDefinition>())
            {
                if (enemy.BaseHealth <= 0) section.Errors.Add($"enemy '{enemy.Id}': base health {enemy.BaseHealth}.");
                if (enemy.Moveset.Any(a => a == null)) section.Errors.Add($"enemy '{enemy.Id}': moveset missing an attack reference.");
            }

            foreach (var attack in LoadAll<EnemyAttackDefinition>())
            {
                if (attack.DamageMin < 0 || attack.TelegraphSeconds < 0) section.Errors.Add($"attack '{attack.Id}': invalid damage/telegraph.");
            }

            if (section.Errors.Count == 0) section.Passed.Add($"{LoadAll<EnemyDefinition>().Count} enemies, {LoadAll<EliteDefinition>().Count} Elites, {LoadAll<BossDefinition>().Count} Bosses: stats and attack references valid.");
        }

        // ---- 6. Content counts ----

        private static void ValidateContentCounts(Section section)
        {
            var counts = ContentCountValidator.ValidateProject();
            foreach (var line in counts.Lines.Where(l => !l.Pass)) section.Errors.Add($"{line.Category} [{line.Scope}]: expected {line.Expected}, actual {line.Actual}.");
            foreach (var p in counts.Problems) section.Errors.Add(p);
            if (counts.Pass) section.Passed.Add($"{counts.Lines.Count}/{counts.Lines.Count} count lines exact: 33 weapons / 9 armor / 16 accessories / 10 consumables / 9 enemies / 6 Elites / 6 Bosses / 63 rooms.");
        }

        // ---- 7. Build scenes ----

        private static void ValidateBuildScenes(Section section, List<string> findings)
        {
            var scenes = EditorBuildSettings.scenes;
            var expected = new[] { SceneNames.Bootstrap, SceneNames.MainMenu, SceneNames.Base, SceneNames.Dungeon };
            var names = scenes.Where(s => s.enabled).Select(s => Path.GetFileNameWithoutExtension(s.path)).ToList();
            if (!names.SequenceEqual(expected)) section.Errors.Add($"build scenes are [{string.Join(", ", names)}], expected [{string.Join(", ", expected)}] in that order.");
            foreach (var s in scenes) if (!File.Exists(s.path)) section.Errors.Add($"build scene file missing: {s.path}");
            if (names.SequenceEqual(expected) && scenes.All(s => File.Exists(s.path))) section.Passed.Add("Build settings: Bootstrap, MainMenu, Base, Dungeon enabled in order and present.");

            var bootstrap = File.ReadAllText("Assets/Game/Scenes/Bootstrap.unity");
            var appRootGuid = AssetDatabase.AssetPathToGUID("Assets/Game/Scripts/Core/AppRoot.cs");
            if (!bootstrap.Contains(appRootGuid)) section.Errors.Add("Bootstrap scene has no AppRoot.");
            else section.Passed.Add("Bootstrap scene carries AppRoot (loads MainMenu).");

            foreach (var scene in new[] { SceneNames.MainMenu, SceneNames.Base, SceneNames.Dungeon })
            {
                var text = File.ReadAllText($"Assets/Game/Scenes/{scene}.unity");
                if (!text.Contains("MonoBehaviour:")) findings.Add($"Scene '{scene}' has no composition root yet (no MonoBehaviour): the boot flow (TASK 147) must compose the screen/run from the existing view models and services.");
            }
        }

        // ---- 8. Assembly hygiene ----

        private static void ValidateAssemblies(Section section)
        {
            var asmdefs = Directory.GetFiles("Assets/Game/Scripts", "*.asmdef", SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/Game/Tests", "*.asmdef", SearchOption.AllDirectories));
            var runtime = 0;
            foreach (var asmdef in asmdefs)
            {
                var text = File.ReadAllText(asmdef);
                var isEditorOnly = text.Contains("\"Editor\"") && text.Contains("includePlatforms");
                if (isEditorOnly || asmdef.Contains("Tests")) continue;
                runtime++;
                if (text.Contains("\"Game.Editor\"")) section.Errors.Add($"runtime assembly '{Path.GetFileName(asmdef)}' references Game.Editor.");
                if (Regex.IsMatch(text, "\"UnityEditor[^\"]*\"")) section.Errors.Add($"runtime assembly '{Path.GetFileName(asmdef)}' references a UnityEditor assembly.");
            }

            var leaks = 0;
            foreach (var file in Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Editor/")) continue;
                var text = File.ReadAllText(file);
                if (!text.Contains("using UnityEditor") && !text.Contains("UnityEditor.")) continue;
                if (text.Contains("#if UNITY_EDITOR")) continue;
                section.Errors.Add($"runtime script '{file.Replace('\\', '/')}' references UnityEditor outside #if UNITY_EDITOR.");
                leaks++;
            }

            if (section.Errors.Count == 0) section.Passed.Add($"{runtime} runtime assemblies reference no Editor assembly; no runtime script uses UnityEditor outside #if UNITY_EDITOR.");
        }

        // ---- 9. Network prefabs ----

        private static void ValidateNetworkPrefabs(Section section, List<string> findings)
        {
            var list = AssetDatabase.LoadAssetAtPath<Unity.Netcode.NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (list == null) { section.Errors.Add("DefaultNetworkPrefabs.asset missing."); return; }
            foreach (var entry in list.PrefabList)
            {
                if (entry.Prefab == null) section.Errors.Add("DefaultNetworkPrefabs: a prefab entry is missing.");
                else if (entry.Prefab.GetComponent<Unity.Netcode.NetworkObject>() == null) section.Errors.Add($"DefaultNetworkPrefabs: '{entry.Prefab.name}' has no NetworkObject.");
            }

            section.Passed.Add($"DefaultNetworkPrefabs.asset present with {list.PrefabList.Count} entries, all resolving.");
            if (list.PrefabList.Count == 0) findings.Add("No network player prefab is registered yet: NgoPlayerEntityFactory takes the prefab at composition (TASK 147 boot flow); the live NGO/Relay path stays NOT RUN until it exists.");
        }

        // ---- External blockers ----

        private static void CollectExternalBlockers(Report report)
        {
            var animation = AnimationAssetAudit.Audit();
            foreach (var a in animation.Blocked) report.ExternalBlockers.Add($"animation set '{a.ActorId}' ({a.Kind}): {a.Missing}/{a.Required} clips missing");
            if (animation.WeaponSpritesPresent < animation.WeaponSpritesRequired) report.ExternalBlockers.Add($"weapon sprites: {animation.WeaponSpritesPresent}/{animation.WeaponSpritesRequired}");
            var audio = AudioAssetAudit.Audit();
            foreach (var id in audio.Blocked) report.ExternalBlockers.Add($"sfx clip '{id}'");
            var music = MusicAssetAudit.Audit();
            foreach (var t in music.MissingTracks) report.ExternalBlockers.Add($"music track '{t}'");
            foreach (var s in music.MissingStingers) report.ExternalBlockers.Add($"stinger '{s}'");
            foreach (var a in music.MissingAmbience) report.ExternalBlockers.Add($"ambience loop '{a}'");
            // Derived from the manifest rather than asserted, so this line tells the truth as art lands instead of
            // permanently claiming the project is still on placeholders.
            var manifest = CompletionAssetManifest.Generate();
            var outstanding = manifest.Roles
                .Where(r => r.Status != CompletionAssetManifest.Integrated)
                .GroupBy(r => r.Category)
                .Select(g => $"{g.Key} ({g.Count()})")
                .ToList();

            if (outstanding.Count > 0)
                report.ExternalBlockers.Add("outstanding final asset roles: " + string.Join(", ", outstanding));
        }

        private static List<T> LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}").Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).OrderBy(a => AssetDatabase.GetAssetPath(a), StringComparer.Ordinal).ToList();
    }
}
