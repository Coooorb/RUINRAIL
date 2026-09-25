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

        /// <summary>
        /// Verification substitute on a macOS development machine that has no Windows Build Support module: the same
        /// scenes, the same non-development options, built for the host platform so the built-player smoke can run
        /// here. It is not a platform commitment — the release target stays Windows x64 (<see cref="BuildBatch"/>).
        /// </summary>
        public const string MacOutputDirectory = "Builds/MacOS";
        public const string MacAppName = "RUINRAIL.app";
        public const string MacReportPath = "TestResults/build_report_macos.md";

        public static void BuildMacBatch()
        {
            var report = BuildForTarget(BuildTarget.StandaloneOSX, MacOutputDirectory, MacAppName, MacReportPath);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport Build() => BuildForTarget(BuildTarget.StandaloneWindows64, OutputDirectory, ExecutableName, ReportPath);

        private static BuildReport BuildForTarget(BuildTarget target, string outputDirectory, string executableName, string reportPath)
        {
            GameContentCatalogBuilder.Build();
            var scenesInSettings = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (!scenesInSettings.SequenceEqual(ScenePaths)) Debug.LogWarning($"Build settings scene order differs from the approved order; building the approved order: {string.Join(", ", ScenePaths)}");
            foreach (var path in ScenePaths) if (!File.Exists(path)) throw new FileNotFoundException(path);
            Directory.CreateDirectory(outputDirectory);
            var options = new BuildPlayerOptions
            {
                scenes = ScenePaths,
                locationPathName = Path.Combine(outputDirectory, executableName),
                target = target,
                options = BuildOptions.None // release: no development build, no debugging, no script debugging
            };
            var report = BuildPipeline.BuildPlayer(options);
            WriteReport(report, reportPath, target);
            return report;
        }

        public static void WriteReport(BuildReport report) => WriteReport(report, ReportPath, BuildTarget.StandaloneWindows64);

        public static void WriteReport(BuildReport report, string reportPath, BuildTarget target)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Release build report (TASK 147)");
            sb.AppendLine();
            sb.AppendLine(target == BuildTarget.StandaloneWindows64
                ? "- Target: StandaloneWindows64 (development-environment platform; no other platform commitment), options: None (non-development release)."
                : $"- Target: {target} (verification substitute on a macOS development machine without Windows Build Support; the release target remains Windows x64), options: None (non-development).");
            sb.AppendLine($"- Scenes: {string.Join(", ", SceneOrder)}");
            sb.AppendLine($"- Result: **{report.summary.result}** — errors {report.summary.totalErrors}, warnings {report.summary.totalWarnings}, size {report.summary.totalSize / 1024f / 1024f:0.0} MB, time {report.summary.totalTime.TotalSeconds:0} s.");
            sb.AppendLine($"- Output: {report.summary.outputPath}");
            sb.AppendLine();
            var messages = report.steps.SelectMany(s => s.messages.Select(m => (step: s.name, m.type, m.content))).Where(m => m.type == LogType.Error || m.type == LogType.Exception || m.type == LogType.Warning).ToList();
            sb.AppendLine($"## Build messages ({messages.Count} warnings/errors)");
            foreach (var (step, type, content) in messages.Take(200)) sb.AppendLine($"- [{type}] {step}: {content.Replace('\n', ' ').Trim()}");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? "TestResults");
            File.WriteAllText(reportPath, sb.ToString());
        }
    }
}
