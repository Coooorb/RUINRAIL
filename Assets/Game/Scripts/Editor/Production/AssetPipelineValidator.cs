using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Audio;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 151 requirement 4 — the gate a final asset batch must pass before it counts as integrated.
    ///
    /// It checks the five failure modes that actually break this project: a role with no id, two roles mapped to the
    /// same file, an asset imported with the wrong pixel settings, a binding point left holding a null reference, and
    /// a placeholder asset claiming to be final content. It reports problems with the path and the role that caused
    /// them, so the fix is obvious; it never judges how the art looks.
    /// </summary>
    public static class AssetPipelineValidator
    {
        public const string ReportPath = "TestResults/asset_pipeline.md";

        public sealed class Report
        {
            public readonly List<string> Passed = new();
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL asset pipeline validation (TASK 151)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Passed.Count} checks passed, {Problems.Count} problems.");
                sb.AppendLine();
                foreach (var p in Passed) sb.AppendLine($"- PASS {p}");
                foreach (var p in Problems) sb.AppendLine($"- ERROR {p}");
                sb.AppendLine();
                sb.AppendLine("Import correctness and reference integrity are proven here. Visual and audio quality are not, and cannot be (art/107 section 5).");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Asset Pipeline")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Validate()
        {
            var report = new Report();
            var manifest = CompletionAssetManifest.Generate();

            ValidateRoleIds(report, manifest);
            ValidateRoleMappings(report, manifest);
            ValidateImportedAssets(report, manifest);
            ValidateBindingReferences(report);
            ValidatePlaceholdersNotMarkedFinal(report, manifest);
            ValidateProvenance(report, manifest);
            return report;
        }

        /// <summary>Missing role ids. A role without a stable id cannot be replaced without touching gameplay code.</summary>
        private static void ValidateRoleIds(Report report, CompletionAssetManifest.Report manifest)
        {
            var blank = manifest.Roles.Where(r => string.IsNullOrWhiteSpace(r.RoleId)).ToList();
            foreach (var role in blank) report.Problems.Add($"{role.Category}: a role has no id, so no asset can be bound to it.");

            var noConvention = manifest.Roles
                .Where(r => r.ExpectedSourcePath.Length == 0)
                .Select(r => $"{r.Category}/{r.RoleId}")
                .ToList();
            foreach (var role in noConvention) report.Problems.Add($"{role}: AssetNamingConvention has no placement, so a final asset has nowhere defined to land.");

            if (blank.Count == 0 && noConvention.Count == 0)
                report.Passed.Add($"All {manifest.Roles.Count} manifest roles carry a stable id and a convention path");
        }

        /// <summary>Duplicate role mappings: two roles competing for one file would silently overwrite each other.</summary>
        private static void ValidateRoleMappings(Report report, CompletionAssetManifest.Report manifest)
        {
            var duplicateIds = manifest.Roles
                .Where(r => !string.IsNullOrWhiteSpace(r.RoleId))
                .GroupBy(r => r.Category + "/" + r.RoleId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();
            foreach (var group in duplicateIds) report.Problems.Add($"Duplicate role id '{group.Key}' appears {group.Count()} times in the manifest.");

            var duplicatePaths = manifest.Roles
                .Where(r => r.ExpectedSourcePath.Length > 0 && !r.ExpectedSourcePath.EndsWith("/", StringComparison.Ordinal))
                .GroupBy(r => r.ExpectedSourcePath, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Select(r => r.Category + "/" + r.RoleId).Distinct(StringComparer.Ordinal).Count() > 1)
                .ToList();
            foreach (var group in duplicatePaths)
                report.Problems.Add($"Path collision at '{group.Key}': claimed by {string.Join(", ", group.Select(r => r.Category + "/" + r.RoleId))}.");

            if (duplicateIds.Count == 0 && duplicatePaths.Count == 0)
                report.Passed.Add("No duplicate role id and no expected-source-path collision");
        }

        /// <summary>Whatever has actually landed at a convention path must import as pixel art (art/107 section 3).</summary>
        private static void ValidateImportedAssets(Report report, CompletionAssetManifest.Report manifest)
        {
            var present = 0;
            foreach (var role in manifest.Roles)
            {
                var path = role.ExpectedSourcePath;
                if (path.Length == 0 || path.EndsWith("/", StringComparison.Ordinal)) continue;
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;

                present++;
                if (importer.textureType != ArtProductionContract.RequiredTextureType)
                    report.Problems.Add($"{role.RoleId} at {path}: texture type {importer.textureType}, expected {ArtProductionContract.RequiredTextureType}.");
                // UI art is canvas-space and authored at screen pixels (art/107 section 3, spec 21).

                var expectedPpu = ArtProductionContract.IsUiSprite(path) ? 1 : ArtProductionContract.RequiredPixelsPerUnit;

                if (!Mathf.Approximately(importer.spritePixelsPerUnit, expectedPpu))
                    report.Problems.Add($"{role.RoleId} at {path}: PPU {importer.spritePixelsPerUnit}, expected {expectedPpu} (art/101).");
                if (importer.filterMode != ArtProductionContract.RequiredFilterMode)
                    report.Problems.Add($"{role.RoleId} at {path}: filter {importer.filterMode}, expected {ArtProductionContract.RequiredFilterMode}; smoothing destroys pixel edges.");
                if (importer.textureCompression != ArtProductionContract.RequiredCompression)
                    report.Problems.Add($"{role.RoleId} at {path}: compression {importer.textureCompression}, expected {ArtProductionContract.RequiredCompression}.");
                if (importer.mipmapEnabled)
                    report.Problems.Add($"{role.RoleId} at {path}: mip maps enabled; they blur a pixel-perfect orthographic camera.");
            }

            report.Passed.Add(present == 0
                ? "No final sprite has landed at a convention path yet; the import gate applies from the first TASK 153 batch"
                : $"{present} sprite(s) at convention paths import at PPU {ArtProductionContract.RequiredPixelsPerUnit}, Point, Uncompressed, no mip maps");
        }

        /// <summary>Broken sprite/clip references in the binding points that already exist. A null here ships as a silent hole.</summary>
        private static void ValidateBindingReferences(Report report)
        {
            var problems = report.Problems.Count;

            foreach (var set in LoadAll<CharacterAnimationSet>())
            {
                var path = AssetDatabase.GetAssetPath(set);
                foreach (var clip in set.Clips.Where(c => c != null))
                {
                    if (clip.Frames == null || clip.Frames.Length == 0) continue;
                    if (clip.Frames.Any(f => f == null))
                        report.Problems.Add($"{path}: clip '{clip.Id}' has a null frame reference.");
                }
            }

            foreach (var tile in LoadAll<Tile>())
            {
                if (tile.sprite == null)
                    report.Problems.Add($"{AssetDatabase.GetAssetPath(tile)}: Tile has no sprite; it will render as nothing.");
            }

            // Damaging floor must never ship as a still tile: each biome's hazard is the animated loop, fully framed.
            foreach (RuinRail.EditorTools.ArtGen.TileFactory.Biome biome in Enum.GetValues(typeof(RuinRail.EditorTools.ArtGen.TileFactory.Biome)))
            {
                var hazardPath = RuinRail.EditorTools.ArtGen.ArtIntegration.HazardTilePath(biome);
                if (AssetDatabase.LoadAssetAtPath<TileBase>(hazardPath) is not RuinRail.Dungeon.Grid.HazardAnimatedTile hazard || !hazard.IsAnimated)
                    report.Problems.Add($"{hazardPath}: the {biome} damaging-floor hazard is not an animated HazardAnimatedTile with every frame bound.");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<AudioEventCatalog>(AudioAssetAudit.CatalogPath);
            if (catalog != null)
            {
                foreach (var definition in catalog.Events.Where(e => e != null))
                {
                    if (definition.Clips != null && definition.Clips.Length > 0 && definition.Clips.Any(c => c == null))
                        report.Problems.Add($"Audio event '{definition.Id}' has a null entry in its clip list.");
                }

                foreach (var duplicate in catalog.DuplicateIds())
                    report.Problems.Add($"Audio event id '{duplicate}' is defined more than once.");
            }

            if (report.Problems.Count == problems)
                report.Passed.Add("No broken sprite, clip or audio reference in any existing binding point");
        }

        /// <summary>
        /// Placeholder-as-final. Dev art may exist, but it may never be the thing a role claims as production content
        /// (art/106 section 13.9): the placeholder must leave the release path when its role is filled.
        /// </summary>
        private static void ValidatePlaceholdersNotMarkedFinal(Report report, CompletionAssetManifest.Report manifest)
        {
            var offenders = manifest.Roles
                .Where(r => ArtProductionContract.StatusesRequiringAttestation.Contains(r.Status))
                .Where(r => r.SourcePath.Contains(ArtProductionContract.PlaceholderFolder, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var role in offenders)
                report.Problems.Add($"{role.RoleId} claims {role.Status} but resolves to placeholder art at {role.SourcePath}.");

            if (offenders.Count == 0)
                report.Passed.Add($"No role claims final status while resolving to {ArtProductionContract.PlaceholderFolder}");
        }

        /// <summary>Requirement 5: a role that claims final status must carry a complete provenance record.</summary>
        private static void ValidateProvenance(Report report, CompletionAssetManifest.Report manifest)
        {
            var entries = AssetProvenanceRegistry.Load();
            var problems = ArtProductionContract.ValidateAttestations(
                manifest.Roles.Select(r => (r.RoleId, r.Status)),
                entries.Select(e => e.ToAttestation()));

            report.Problems.AddRange(problems);

            var duplicates = entries
                .GroupBy(e => e.roleId, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            foreach (var roleId in duplicates)
                report.Problems.Add($"Provenance registry has {entries.Count(e => e.roleId == roleId)} entries for role '{roleId}'.");

            if (problems.Count == 0 && duplicates.Count == 0)
                report.Passed.Add($"Provenance registry is consistent ({entries.Count} entries); no role claims final status without a complete attestation");
        }

        private static List<T> LoadAll<T>() where T : UnityEngine.Object =>
            AssetDatabase.FindAssets($"t:{typeof(T).Name}")
                .Select(g => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null)
                .ToList();
    }
}
