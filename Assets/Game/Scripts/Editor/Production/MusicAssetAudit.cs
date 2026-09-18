using System;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Audio;
using RuinRail.Core;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>TASK 141 final-gate listing: exactly 11 track roles, 6 stinger roles and 3 ambience loops; every role without a clip is BLOCKED_EXTERNAL_ASSET.</summary>
    public static class MusicAssetAudit
    {
        public const string CatalogPath = "Assets/Game/ScriptableObjects/Audio/MusicCatalog.asset";
        public const string ReportPath = "TestResults/music_assets.md";

        public sealed class Report
        {
            public bool CatalogExists;
            public bool ExactSlots;
            public int TracksPresent, StingersPresent, AmbiencePresent;
            public string[] MissingTracks = Array.Empty<string>(), MissingStingers = Array.Empty<string>(), MissingAmbience = Array.Empty<string>();
            public bool RoutingComplete => CatalogExists && ExactSlots;
            public bool ContentComplete => RoutingComplete && MissingTracks.Length == 0 && MissingStingers.Length == 0 && MissingAmbience.Length == 0;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL V1 music / stinger / ambience audit (art/105)");
                sb.AppendLine();
                sb.AppendLine($"Routing/data: **{(RoutingComplete ? "COMPLETE" : "INCOMPLETE")}** — {MusicStateResolver.TrackCount} track roles, {MusicStateResolver.StingerCount} stinger roles, {Enum.GetValues(typeof(Biome)).Length} ambience roles{(ExactSlots ? " (exact)" : " (SLOT COUNT WRONG)")}.");
                sb.AppendLine($"Content: **{(ContentComplete ? "COMPLETE" : "BLOCKED_EXTERNAL_ASSET")}** — tracks {TracksPresent}/{MusicStateResolver.TrackCount}, stingers {StingersPresent}/{MusicStateResolver.StingerCount}, ambience {AmbiencePresent}/{Enum.GetValues(typeof(Biome)).Length}.");
                sb.AppendLine();
                sb.AppendLine("| Role | Kind | Status |");
                sb.AppendLine("|---|---|---|");
                foreach (MusicRole role in Enum.GetValues(typeof(MusicRole))) sb.AppendLine($"| {MusicStateResolver.DisplayName(role)} | Track | {(MissingTracks.Contains(role.ToString()) ? "BLOCKED_EXTERNAL_ASSET" : "OK")} |");
                foreach (StingerRole role in Enum.GetValues(typeof(StingerRole))) sb.AppendLine($"| {role} | Stinger | {(MissingStingers.Contains(role.ToString()) ? "BLOCKED_EXTERNAL_ASSET" : "OK")} |");
                foreach (Biome biome in Enum.GetValues(typeof(Biome))) sb.AppendLine($"| {biome} ambience ({MusicStateResolver.AmbienceDescription(biome)}) | Ambience | {(MissingAmbience.Contains(biome.ToString()) ? "BLOCKED_EXTERNAL_ASSET" : "OK")} |");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Audit Music Assets")]
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
            var catalog = AssetDatabase.LoadAssetAtPath<MusicCatalog>(CatalogPath);
            var report = new Report { CatalogExists = catalog != null };
            if (catalog == null) return report;
            report.ExactSlots = catalog.HasExactSlots;
            report.MissingTracks = catalog.MissingTracks.Select(r => r.ToString()).ToArray();
            report.MissingStingers = catalog.MissingStingers.Select(r => r.ToString()).ToArray();
            report.MissingAmbience = catalog.MissingAmbience.Select(b => b.ToString()).ToArray();
            report.TracksPresent = MusicStateResolver.TrackCount - report.MissingTracks.Length;
            report.StingersPresent = MusicStateResolver.StingerCount - report.MissingStingers.Length;
            report.AmbiencePresent = Enum.GetValues(typeof(Biome)).Length - report.MissingAmbience.Length;
            return report;
        }

        [MenuItem("RuinRail/Production/Create Music Catalog")]
        public static void CreateAssets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath) ?? "Assets");
            var catalog = AssetDatabase.LoadAssetAtPath<MusicCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MusicCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.EnsureSlots();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
        }
    }
}
