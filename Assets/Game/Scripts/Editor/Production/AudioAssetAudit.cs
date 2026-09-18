using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Audio;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 140 final-gate listing for SFX: every required audio event id (art/105 coverage contract) must have a
    /// catalog definition; definitions without clips are truthfully BLOCKED_EXTERNAL_ASSET (fallback silence at runtime).
    /// Also creates the catalog with one silent definition per required id so no reference is ever missing.
    /// </summary>
    public static class AudioAssetAudit
    {
        public const string AudioFolder = "Assets/Game/ScriptableObjects/Audio";
        public const string EventsFolder = AudioFolder + "/Events";
        public const string CatalogPath = AudioFolder + "/AudioEventCatalog.asset";
        public const string ReportPath = "TestResults/audio_assets.md";

        public sealed class Report
        {
            public readonly List<(string id, AudioBus bus, bool defined, bool hasClips)> Lines = new();
            public readonly List<string> Problems = new();
            public int Required => Lines.Count;
            public int Defined => Lines.Count(l => l.defined);
            public int WithClips => Lines.Count(l => l.hasClips);
            public IEnumerable<string> Blocked => Lines.Where(l => !l.hasClips).Select(l => l.id);
            public bool ContractComplete => Lines.All(l => l.defined) && Problems.Count == 0;
            public bool ContentComplete => ContractComplete && Lines.All(l => l.hasClips);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL V1 audio event audit (art/105 SFX coverage rule)");
                sb.AppendLine();
                sb.AppendLine($"Contract: **{(ContractComplete ? "COMPLETE" : "INCOMPLETE")}** — {Defined}/{Required} required events defined. Content: **{(ContentComplete ? "COMPLETE" : "BLOCKED_EXTERNAL_ASSET")}** — {WithClips}/{Required} events have clips.");
                sb.AppendLine();
                sb.AppendLine("| Event | Bus | Defined | Clips | Status |");
                sb.AppendLine("|---|---|---|---|---|");
                foreach (var l in Lines) sb.AppendLine($"| {l.id} | {l.bus} | {(l.defined ? "yes" : "NO")} | {(l.hasClips ? "yes" : "none")} | {(l.hasClips ? "OK" : l.defined ? "BLOCKED_EXTERNAL_ASSET" : "MISSING_DEFINITION")} |");
                foreach (var p in Problems) sb.AppendLine($"- PROBLEM {p}");
                sb.AppendLine();
                sb.AppendLine("Runtime behaviour without clips: AudioService plays fallback silence and records the event id; hooks stay wired; no exceptions.");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Audit Audio Events")]
        public static void AuditMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Audit();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Audit()
        {
            var report = new Report();
            var catalog = AssetDatabase.LoadAssetAtPath<AudioEventCatalog>(CatalogPath);
            if (catalog == null) report.Problems.Add($"Catalog missing at {CatalogPath}.");
            foreach (var (id, bus, _) in AudioEventIds.Required)
            {
                var defined = catalog != null && catalog.TryGet(id, out var d);
                var hasClips = defined && catalog.TryGet(id, out var dd) && dd.HasClips;
                if (defined && catalog.TryGet(id, out var def) && def.Bus != bus) report.Problems.Add($"{id}: bus {def.Bus}, expected {bus}.");
                report.Lines.Add((id, bus, defined, hasClips));
            }

            if (catalog != null) foreach (var dup in catalog.DuplicateIds()) report.Problems.Add($"Duplicate event id '{dup}'.");
            return report;
        }

        /// <summary>Creates the catalog and one definition per required id (idempotent; existing definitions are kept, so authored clips survive).</summary>
        [MenuItem("RuinRail/Production/Create Audio Event Assets")]
        public static void CreateAssets()
        {
            Directory.CreateDirectory(EventsFolder);
            AssetDatabase.Refresh();
            var definitions = new List<AudioEventDefinition>();
            foreach (var (id, bus, loop) in AudioEventIds.Required)
            {
                var path = $"{EventsFolder}/Sfx_{id.Replace('.', '_')}.asset";
                var definition = AssetDatabase.LoadAssetAtPath<AudioEventDefinition>(path);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<AudioEventDefinition>();
                    definition.Configure(id, bus, loop);
                    AssetDatabase.CreateAsset(definition, path);
                }

                definitions.Add(definition);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AudioEventCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<AudioEventCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Configure(definitions);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
