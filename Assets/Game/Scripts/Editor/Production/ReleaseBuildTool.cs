using System;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 147 — clean, non-development release build for the development platform (Windows x64; no other platform is
    /// committed by the project) with the approved scene order, followed by a report of size, warnings and errors.
    /// </summary>
    public static class ReleaseBuildTool
    {
        public const string OutputDirectory = "Builds/Windows64";
        public const string ExecutableName = "RUINRAIL.exe";
        public const string ReportPath = "TestResults/build_report.md";

        public static readonly string[] SceneOrder = { SceneNames.Bootstrap, SceneNames.MainMenu, SceneNames.Base, SceneNames.Dungeon };

        public static string[] ScenePaths => SceneOrder.Select(s => $"Assets/Game/Scenes/{s}.unity").ToArray();

        [MenuItem("RuinRail/Production/Build Release (Windows x64)")]
        public static void BuildMenu() => Build();

        /// <summary>Batchmode entry: exits with 0 on a successful build, 1 otherwise.</summary>
        public static void BuildBatch()
        {
            var report = Build();
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport Build()
        {
            GameContentCatalogBuilder.Build();
            var scenesInSettings = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenesInSettings.SequenceEqual(ScenePaths)) Debug.LogWarning($"Build settings scene order differs from the approved order; building the approved order: {string.Join(", ", ScenePaths)}");
            foreach (var path in ScenePaths) if (!File.Exists(path)) throw new FileNotFoundException(path);
            Directory.CreateDirectory(OutputDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = ScenePaths,
                locationPathName = Path.Combine(OutputDirectory, ExecutableName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None // release: no development build, no debugging, no script debugging
            };
            var report = BuildPipeline.BuildPlayer(options);
            WriteReport(report);
            return report;
        }

        public static void WriteReport(BuildReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Release build report (TASK 147)");
            sb.AppendLine();
            sb.AppendLine($"- Target: StandaloneWindows64 (development-environment platform; no other platform commitment), options: None (non-development release).");
            sb.AppendLine($"- Scenes: {string.Join(", ", SceneOrder)}");
            sb.AppendLine($"- Result: **{report.summary.result}** — errors {report.summary.totalErrors}, warnings {report.summary.totalWarnings}, size {report.summary.totalSize / 1024f / 1024f:0.0} MB, time {report.summary.totalTime.TotalSeconds:0} s.");
            sb.AppendLine($"- Output: {report.summary.outputPath}");
            sb.AppendLine();
            var messages = report.steps.SelectMany(s => s.messages.Select(m => (step: s.name, m.type, m.content))).Where(m => m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning).ToList();
            sb.AppendLine($"## Build messages ({messages.Count} warnings/errors)");
            foreach (var (step, type, content) in messages.Take(200)) sb.AppendLine($"- [{type}] {step}: {content.Replace('\n', ' ').Trim()}");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, sb.ToString());
        }
    }
}
