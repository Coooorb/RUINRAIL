using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Grid;
using RuinRail.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 137 production validation (art/101, art/102): the approved sorting layers exist in the approved order, the
    /// camera config is 640×360 at PPU 32, every baked room prefab renders its layers on the approved sorting layers,
    /// and each of the three biomes has a readable lighting profile. Deterministic markdown for the final gate.
    /// </summary>
    public static class PresentationValidator
    {
        public const string CameraConfigPath = "Assets/Game/ScriptableObjects/Presentation/CameraRigConfig.asset";
        public const string FeedbackConfigPath = "Assets/Game/ScriptableObjects/Presentation/FeedbackConfig.asset";
        public const string LightingFolder = "Assets/Game/ScriptableObjects/Presentation";
        public const string RoomPrefabFolder = "Assets/Game/Prefabs/Rooms";
        public const string ReportPath = "TestResults/presentation.md";

        public sealed class Report
        {
            public readonly List<string> Passed = new();
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL V1 presentation validation (art/101, art/102)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Passed.Count} checks passed, {Problems.Count} problems.");
                sb.AppendLine();
                foreach (var p in Passed) sb.AppendLine($"- PASS {p}");
                foreach (var p in Problems) sb.AppendLine($"- FAIL {p}");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Presentation")]
        public static void ValidateMenu()
        {
            var report = WriteReport();
            Debug.Log(report.ToMarkdown());
        }

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
            ValidateSortingLayers(report);
            ValidateCamera(report);
            ValidateRoomPrefabs(report);
            ValidateLighting(report);
            ValidateFeedback(report);
            return report;
        }

        public static void ValidateSortingLayers(Report report)
        {
            var project = SortingLayer.layers.Select(l => l.name).ToList();
            var expected = SortingLayers.Ordered;
            var missing = expected.Where(e => !project.Contains(e)).ToList();
            if (missing.Count > 0) report.Problems.Add($"Sorting layers missing from TagManager: {string.Join(", ", missing)}.");
            else report.Passed.Add($"All {expected.Count} approved sorting layers exist.");

            var projected = project.Where(expected.Contains).ToList();
            if (projected.SequenceEqual(expected)) report.Passed.Add("Approved sorting layers are in the approved bottom-to-top order.");
            else report.Problems.Add($"Sorting layer order differs from art/102: {string.Join(" < ", projected)}.");

            foreach (var role in (SortingRole[])Enum.GetValues(typeof(SortingRole)))
            {
                if (!project.Contains(SortingConvention.LayerOf(role))) report.Problems.Add($"Role {role} maps to an undefined layer '{SortingConvention.LayerOf(role)}'.");
            }
        }

        public static void ValidateCamera(Report report)
        {
            var config = AssetDatabase.LoadAssetAtPath<CameraRigConfig>(CameraConfigPath);
            if (config == null) { report.Problems.Add($"Camera config missing at {CameraConfigPath}."); return; }
            if (config.IsApproved) report.Passed.Add($"Camera config: {config.ReferenceWidth}×{config.ReferenceHeight} @ PPU {config.PixelsPerUnit}, orthographic size {config.OrthographicSize}.");
            else report.Problems.Add($"Camera config is {config.ReferenceWidth}×{config.ReferenceHeight} @ PPU {config.PixelsPerUnit}; approved is 640×360 @ 32.");
            if (config.AimOffsetMaxTiles > 2f) report.Problems.Add("Aim offset exceeds 2 tiles: art/102 asks for a subtle offset.");
            if (config.FollowSharpness <= 0f) report.Problems.Add("Follow sharpness must be positive.");
        }

        public static void ValidateRoomPrefabs(Report report)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { RoomPrefabFolder });
            var checkedCount = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/_Test/")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (var tilemap in prefab.GetComponentsInChildren<Tilemap>(true))
                {
                    if (!RoomTilemapLayers.TryParse(tilemap.gameObject.name, out var layer)) continue;
                    var renderer = tilemap.GetComponent<TilemapRenderer>();
                    if (renderer == null) continue;
                    var expectedLayer = RoomTilemapLayers.SortingLayerNameOf(layer);
                    var expectedOrder = RoomTilemapLayers.SortingOrderOf(layer);
                    if (renderer.sortingLayerName != expectedLayer || renderer.sortingOrder != expectedOrder)
                    {
                        report.Problems.Add($"{Path.GetFileName(path)}/{tilemap.gameObject.name}: on '{renderer.sortingLayerName}' order {renderer.sortingOrder}, expected '{expectedLayer}' order {expectedOrder}.");
                    }
                }

                checkedCount++;
            }

            if (checkedCount == 0) report.Problems.Add("No room prefabs found to validate.");
            else report.Passed.Add($"{checkedCount} room prefabs render on the approved sorting layers.");
        }

        public static void ValidateLighting(Report report)
        {
            var profiles = AssetDatabase.FindAssets("t:BiomeLightingProfile", new[] { LightingFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<BiomeLightingProfile>(AssetDatabase.GUIDToAssetPath(g))).Where(p => p != null).ToList();
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var profile = profiles.FirstOrDefault(p => p.Biome == biome);
                if (profile == null) { report.Problems.Add($"No lighting profile for {biome}."); continue; }
                if (profile.IsReadable) report.Passed.Add($"{biome} lighting: global {profile.GlobalLightIntensity:0.##} × {ColorUtility.ToHtmlStringRGB(profile.GlobalLightColor)}, post-processing {(profile.PostProcessing ? "on" : "off")}.");
                else report.Problems.Add($"{biome} lighting profile fails the readability contract (global intensity ≥ 1, no channel < 0.75, no post-processing).");
            }

            if (profiles.Count != Enum.GetValues(typeof(Biome)).Length) report.Problems.Add($"Expected exactly {Enum.GetValues(typeof(Biome)).Length} lighting profiles, found {profiles.Count}.");
        }

        public static void ValidateFeedback(Report report)
        {
            var config = AssetDatabase.LoadAssetAtPath<RuinRail.Presentation.Vfx.FeedbackConfig>(FeedbackConfigPath);
            if (config == null) { report.Problems.Add($"Feedback config missing at {FeedbackConfigPath}."); return; }
            if (config.IsOrdered) report.Passed.Add($"Feedback config: shake small gun {config.SmallGunShakePixels}px < shotgun {config.ShotgunShakePixels}px < explosion {config.ExplosionShakePixels}px ≤ boss slam {config.BossSlamShakePixels}px; explosion {config.ExplosionSeconds}s.");
            else report.Problems.Add("Feedback config shake amounts are not ordered small gun < shotgun < explosion ≤ boss slam (art/104).");
            if (config.ExplosionSeconds > 1f) report.Problems.Add("Explosion effects linger longer than a second and would hide hazards/projectiles (art/104).");
        }

        /// <summary>Creates the approved camera config and the three biome lighting profiles when missing (idempotent).</summary>
        [MenuItem("RuinRail/Production/Create Presentation Assets")]
        public static void CreateAssets()
        {
            Directory.CreateDirectory(LightingFolder);
            if (AssetDatabase.LoadAssetAtPath<CameraRigConfig>(CameraConfigPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<CameraRigConfig>(), CameraConfigPath);
            }

            if (AssetDatabase.LoadAssetAtPath<RuinRail.Presentation.Vfx.FeedbackConfig>(FeedbackConfigPath) == null)
            {
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<RuinRail.Presentation.Vfx.FeedbackConfig>(), FeedbackConfigPath);
            }

            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var path = $"{LightingFolder}/Lighting_{biome}.asset";
                if (AssetDatabase.LoadAssetAtPath<BiomeLightingProfile>(path) != null) continue;
                var profile = ScriptableObject.CreateInstance<BiomeLightingProfile>();
                var so = new SerializedObject(profile);
                so.FindProperty("_biome").enumValueIndex = (int)biome;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(profile, path);
            }

            AssetDatabase.SaveAssets();
        }
    }
}
