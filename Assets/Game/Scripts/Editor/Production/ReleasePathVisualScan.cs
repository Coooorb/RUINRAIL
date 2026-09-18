using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 165 requirement 2 — walk everything that actually ships (the build scenes and the 63 room prefabs) and
    /// report every renderer still pointing at placeholder art or at nothing at all.
    ///
    /// The manifest says what art is owed; this says what the release path is currently rendering. The two can differ:
    /// an asset can exist and still not be wired in, and a placeholder can linger in a scene after its role is filled.
    /// This is the check that catches that, and it is the evidence TASK 184 needs to claim the release path is clean.
    /// </summary>
    public static class ReleasePathVisualScan
    {
        public const string ReportPath = "TestResults/release_path_visuals.md";

        public sealed class Finding
        {
            public string Source = string.Empty;
            public string ObjectPath = string.Empty;
            public string Kind = string.Empty;
            public string Detail = string.Empty;
        }

        public sealed class Report
        {
            public readonly List<Finding> Placeholders = new();
            public readonly List<Finding> Missing = new();
            /// <summary>Placeholder use inside test-only fixtures, which never enter the build's content catalog.</summary>
            public readonly List<Finding> TestFixtures = new();
            public int ScenesScanned;
            public int PrefabsScanned;
            public int RenderersScanned;

            /// <summary>Clean means nothing in the release path renders placeholder art or a null sprite.</summary>
            public bool Clean => Placeholders.Count == 0 && Missing.Count == 0;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL release-path visual scan (TASK 165)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Clean ? "CLEAN" : "PLACEHOLDER ART IN RELEASE PATH")}** — {Placeholders.Count} placeholder reference(s), {Missing.Count} missing reference(s).");
                sb.AppendLine($"Scanned {ScenesScanned} build scene(s), {PrefabsScanned} prefab(s), {RenderersScanned} renderer(s).");
                sb.AppendLine();

                void Table(string title, List<Finding> findings, string note)
                {
                    sb.AppendLine($"## {title} — {findings.Count}");
                    sb.AppendLine();
                    if (findings.Count == 0)
                    {
                        sb.AppendLine("None.");
                        sb.AppendLine();
                        return;
                    }

                    sb.AppendLine(note);
                    sb.AppendLine();
                    sb.AppendLine("| Source | Object | Kind | Detail |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var f in findings.Take(200)) sb.AppendLine($"| {f.Source} | {f.ObjectPath} | {f.Kind} | {f.Detail} |");
                    if (findings.Count > 200) sb.AppendLine($"| … | … | … | {findings.Count - 200} more |");
                    sb.AppendLine();
                }

                Table("Placeholder art still rendered", Placeholders,
                    "These must be replaced before release: art/106 section 13.9 requires placeholder art to leave the release path once its role is filled.");


                Table("Placeholder art in test-only fixtures (does not ship)", TestFixtures,
                    "These live in fixtures excluded from GameContentCatalog, so they never reach a player. Listed for completeness.");
                Table("Missing sprite references", Missing,
                    "A null sprite renders as nothing and fails silently in a build.");

                sb.AppendLine("This scan proves what the release path renders. It does not judge whether the art is good (art/107 section 5).");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Scan Release Path Visuals")]
        public static void ScanMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Scan();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Scan()
        {
            var report = new Report();
            ScanPrefabs(report);
            ScanBuildScenes(report);
            return report;
        }

        private static void ScanPrefabs(Report report)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Game/Prefabs" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                report.PrefabsScanned++;
                Inspect(prefab, path, report);
            }
        }

        /// <summary>
        /// Build scenes are opened additively without loading them into play mode, inspected, then closed, so the scan
        /// leaves the editor exactly as it found it.
        /// </summary>
        private static void ScanBuildScenes(Report report)
        {
            foreach (var entry in EditorBuildSettings.scenes.Where(s => s.enabled))
            {
                if (!File.Exists(entry.path)) continue;

                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Additive);
                report.ScenesScanned++;
                try
                {
                    foreach (var root in scene.GetRootGameObjects()) Inspect(root, entry.path, report);
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void Inspect(GameObject root, string source, Report report)
        {
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                report.RenderersScanned++;
                Record(renderer.sprite, renderer.gameObject, source, "SpriteRenderer", report);
            }

            foreach (var map in root.GetComponentsInChildren<Tilemap>(true))
            {
                report.RenderersScanned++;
                foreach (var position in map.cellBounds.allPositionsWithin)
                {
                    var tile = map.GetTile(position);
                    if (tile == null) continue;

                    var assetPath = AssetDatabase.GetAssetPath(tile);
                    if (!IsPlaceholder(assetPath)) continue;

                    // One finding per tile asset per tilemap, not per painted cell. Test-only fixtures go in their
                    // own bucket: they are excluded from GameContentCatalog and can never reach a player.
                    var bucket = IsTestFixture(source) ? report.TestFixtures : report.Placeholders;
                    if (bucket.Any(f => f.Source == source && f.ObjectPath == HierarchyPath(map.gameObject) && f.Detail == assetPath)) continue;
                    bucket.Add(new Finding
                    {
                        Source = source, ObjectPath = HierarchyPath(map.gameObject), Kind = "Tilemap tile", Detail = assetPath
                    });
                }
            }
        }

        private static void Record(Sprite sprite, GameObject owner, string source, string kind, Report report)
        {
            if (sprite == null)
            {
                report.Missing.Add(new Finding { Source = source, ObjectPath = HierarchyPath(owner), Kind = kind, Detail = "sprite is null" });
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(sprite);
            if (IsPlaceholder(assetPath))
                report.Placeholders.Add(new Finding { Source = source, ObjectPath = HierarchyPath(owner), Kind = kind, Detail = assetPath });
        }

        /// <summary>Fixtures under a _Test folder are excluded from the shipped content catalog.</summary>
        private static bool IsTestFixture(string source) => source.Replace('\\', '/').Contains("/_Test/");

        private static bool IsPlaceholder(string assetPath) =>
            !string.IsNullOrEmpty(assetPath) &&
            assetPath.Replace('\\', '/').Contains(ArtProductionContract.PlaceholderFolder, StringComparison.OrdinalIgnoreCase);

        private static string HierarchyPath(GameObject go)
        {
            var parts = new List<string>();
            for (var t = go.transform; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
