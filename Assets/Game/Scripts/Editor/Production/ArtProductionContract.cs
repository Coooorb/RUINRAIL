using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.Dungeon.Grid;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 150 — the machine-checkable half of art/107_ART_PRODUCTION_CONTRACT.md.
    ///
    /// Every final art batch from TASK 153 onward is reviewed against the same rules. Two of those rules can be
    /// proven by a machine: that a sprite imports the way art/101 and art/106 section 3 require, and that no role
    /// claims approval without a recorded originality attestation. The rest — readability, feel, whether the art is
    /// actually original — is human judgment, and nothing here pretends otherwise.
    ///
    /// The approved numbers are read from their existing owners (GridConstants, AnimationRules); this type adds no
    /// second copy of a value that already lives somewhere else.
    /// </summary>
    public static class ArtProductionContract
    {
        public const string ReportPath = "TestResults/art_production_contract.md";

        /// <summary>Folders whose sprites ship in the release path. Final art batches land here.</summary>
        public static readonly string[] ArtFolders = { "Assets/Game/Art" };

        /// <summary>Dev art that is explicitly allowed to exist until the role it stands in for is replaced (art/106 section 13.9).</summary>
        public const string PlaceholderFolder = "Assets/Game/Art/Tiles/_Placeholder";

        // ---- Import contract (art/107 section 3) ----

        public const int RequiredPixelsPerUnit = GridConstants.PixelsPerUnit;
        public const FilterMode RequiredFilterMode = FilterMode.Point;
        public const TextureImporterCompression RequiredCompression = TextureImporterCompression.Uncompressed;
        public const TextureImporterType RequiredTextureType = TextureImporterType.Sprite;

        // ---- Reference hierarchy (art/106 section 1, recorded so a batch review cannot drift from it) ----

        public enum ReferenceStrength { Medium, Important, Strongest }

        public sealed class ReferenceBoundary
        {
            public string Game;
            public ReferenceStrength Strength;
            public string Informs;
            public string NeverTake;
        }

        /// <summary>art/106 section 1 in order. References inform qualities only; assets are never taken.</summary>
        public static readonly IReadOnlyList<ReferenceBoundary> References = new[]
        {
            new ReferenceBoundary
            {
                Game = "Soul Knight", Strength = ReferenceStrength.Strongest,
                Informs = "sprite construction and geometry: compact forms, simplified anatomy, strong silhouettes, action readability at gameplay scale",
                NeverTake = "characters, costumes, weapons, tiles, effects, UI, proprietary shapes"
            },
            new ReferenceBoundary
            {
                Game = "Zero Sievert", Strength = ReferenceStrength.Important,
                Informs = "gritty pixel-world finish: worn, survival-oriented, utilitarian environments and a grounded extraction feeling",
                NeverTake = "geometry, palettes, props, layouts, lore, silhouettes"
            },
            new ReferenceBoundary
            {
                Game = "Fallout 4", Strength = ReferenceStrength.Strongest,
                Informs = "post-apocalyptic atmosphere and material language: decay, salvaged retro-futurist machinery, oxidized metal, abandoned infrastructure, dust, old signage",
                NeverTake = "recognizable assets, brands, UI, factions, props, iconography"
            },
            new ReferenceBoundary
            {
                Game = "ARC Raiders", Strength = ReferenceStrength.Medium,
                Informs = "restrained industrial sci-fi accents: strong machine silhouettes, clear luminous technology cues, a slightly cleaner high-tech layer",
                NeverTake = "recognizable assets or designs; glossy generic sci-fi"
            }
        };

        // ---- Originality attestation (art/107 section 6) ----

        /// <summary>
        /// What a batch must record before any of its roles may leave CANDIDATE. This proves an attestation was made;
        /// it cannot verify that the claim is true. Confirming originality is a human step at the TASK 152/165/176 gate.
        /// </summary>
        public sealed class OriginalityAttestation
        {
            public string RoleId = string.Empty;
            public string Author = string.Empty;
            public string Provenance = string.Empty;
            public string License = string.Empty;
            public bool DerivedFromReferenceGame;

            public bool IsComplete =>
                RoleId.Length > 0 && Author.Length > 0 && Provenance.Length > 0 && License.Length > 0 && !DerivedFromReferenceGame;

            public string Problem =>
                RoleId.Length == 0 ? "no role id"
                : Author.Length == 0 ? "no author recorded"
                : Provenance.Length == 0 ? "no provenance recorded"
                : License.Length == 0 ? "no licence recorded"
                : DerivedFromReferenceGame ? "declared as derived from a reference game (art/107 section 6 forbids shipping it)"
                : string.Empty;
        }

        /// <summary>Statuses that assert the role is production content, so they require a complete attestation.</summary>
        public static readonly string[] StatusesRequiringAttestation = { "APPROVED", "INTEGRATED" };

        // ---- Report ----

        public sealed class Report
        {
            public readonly List<string> Passed = new();
            public readonly List<string> Problems = new();
            public int SpritesChecked;
            public int PlaceholdersSkipped;
            public bool Pass => Problems.Count == 0;

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL art production contract check (art/107)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Passed.Count} checks passed, {Problems.Count} problems.");
                sb.AppendLine($"Sprites checked: {SpritesChecked} ({PlaceholdersSkipped} placeholder textures skipped; they are dev art until their role is replaced).");
                sb.AppendLine();
                foreach (var p in Passed) sb.AppendLine($"- PASS {p}");
                foreach (var p in Problems) sb.AppendLine($"- ERROR {p}");
                sb.AppendLine();
                sb.AppendLine("Readability, feel and actual originality are human judgments and are not checked here (art/107 section 5).");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Art Production Contract")]
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
            ValidateTechnicalContract(report);
            ValidateImportSettings(report);
            ValidateReferenceHierarchy(report);
            return report;
        }

        /// <summary>The approved numbers art/107 section 2 reconciled. If one of these ever moves, a batch review is reading a stale contract.</summary>
        public static void ValidateTechnicalContract(Report report)
        {
            void Check(string what, bool ok, string evidence)
            {
                if (ok) report.Passed.Add($"{what}: {evidence}.");
                else report.Problems.Add($"{what}: {evidence}.");
            }

            Check("Tile grid (art/101)", GridConstants.TileSizePixels == 32, $"{GridConstants.TileSizePixels}x{GridConstants.TileSizePixels} px, expected 32x32");
            Check("World PPU (art/101)", GridConstants.PixelsPerUnit == 32, $"{GridConstants.PixelsPerUnit}, expected 32");
            Check("One tile is one world unit (art/101)", Mathf.Approximately(GridConstants.TileWorldSize, 1f), $"{GridConstants.TileWorldSize}, expected 1");
            Check("Reference resolution (art/101)",
                GridConstants.ReferenceResolutionWidth == 640 && GridConstants.ReferenceResolutionHeight == 360,
                $"{GridConstants.ReferenceResolutionWidth}x{GridConstants.ReferenceResolutionHeight}, expected 640x360");
            Check("Animation rate (art/103)",
                AnimationRules.MinFps == 8 && AnimationRules.MaxFps == 12,
                $"{AnimationRules.MinFps}-{AnimationRules.MaxFps} fps, expected 8-12");
            Check("8-directional body facing (art/102, art/103)", AnimationRules.AllFacings.Length == 8, $"{AnimationRules.AllFacings.Length} facings, expected 8");
            Check("Player clip keys (art/103)", AnimationRules.PlayerClipKeys.Length == 6, string.Join("/", AnimationRules.PlayerClipKeys));
            Check("Enemy clip keys include Telegraph (art/103)", AnimationRules.EnemyClipKeys.Contains("Telegraph"), string.Join("/", AnimationRules.EnemyClipKeys));
        }

        /// <summary>art/107 section 3: every release-path sprite imports as pixel art at PPU 32. Placeholder dev art is exempt until replaced.</summary>
        public static void ValidateImportSettings(Report report)
        {
            var checkedAny = false;
            foreach (var path in SpriteTexturePaths())
            {
                if (path.StartsWith(PlaceholderFolder, StringComparison.Ordinal))
                {
                    report.PlaceholdersSkipped++;
                    continue;
                }

                if (AssetImporter.GetAtPath(path) is not TextureImporter importer) continue;
                report.SpritesChecked++;
                checkedAny = true;

                if (IsHardwareCursor(path))
                {
                    // A hardware cursor is not drawn by any camera or canvas: it must import as a readable Cursor texture,
                    // still point filtered, uncompressed and without mip maps (the pixel-art rules below still apply).
                    if (importer.textureType != TextureImporterType.Cursor)
                        report.Problems.Add($"{path}: texture type {importer.textureType}, expected Cursor (hardware cursor)");
                    if (!importer.isReadable)
                        report.Problems.Add($"{path}: hardware cursor textures must be readable for Cursor.SetCursor");
                }
                else if (importer.textureType != RequiredTextureType)
                    report.Problems.Add($"{path}: texture type {importer.textureType}, expected {RequiredTextureType}");

                // Spec 21 sets PPU 32 "unless UI/source role explicitly requires another established value". UI art
                // is authored in screen pixels and drawn by the canvas, not the world camera, so PPU 1 is its
                // established value — forcing 32 there would scale every panel to a third of its authored size.
                var expectedPpu = IsUiSprite(path) ? 1 : RequiredPixelsPerUnit;
                if (!IsHardwareCursor(path) && !Mathf.Approximately(importer.spritePixelsPerUnit, expectedPpu))
                    report.Problems.Add($"{path}: PPU {importer.spritePixelsPerUnit}, expected {expectedPpu} (art/101, art/107 section 3)");
                if (importer.filterMode != RequiredFilterMode)
                    report.Problems.Add($"{path}: filter {importer.filterMode}, expected {RequiredFilterMode} (art/106 section 3: hard pixel edges)");
                if (importer.textureCompression != RequiredCompression)
                    report.Problems.Add($"{path}: compression {importer.textureCompression}, expected {RequiredCompression} (block compression destroys pixel art)");
                if (importer.mipmapEnabled)
                    report.Problems.Add($"{path}: mip maps enabled; they blur a pixel-perfect orthographic camera");
                if (!importer.alphaIsTransparency)
                    report.Problems.Add($"{path}: alphaIsTransparency off; sprite borders will fringe");
            }

            if (checkedAny) report.Passed.Add($"{report.SpritesChecked} release-path sprite texture(s) import at PPU {RequiredPixelsPerUnit}, {RequiredFilterMode}, {RequiredCompression}, no mip maps");
            else report.Passed.Add($"No release-path sprite textures exist yet ({report.PlaceholdersSkipped} placeholder textures skipped); the import contract applies from the first TASK 153 batch");
        }

        /// <summary>art/106 section 1: the hierarchy a batch review is measured against must stay intact and complete.</summary>
        public static void ValidateReferenceHierarchy(Report report)
        {
            var expected = new[] { "Soul Knight", "Zero Sievert", "Fallout 4", "ARC Raiders" };
            var games = References.Select(r => r.Game).ToArray();

            if (!expected.SequenceEqual(games))
                report.Problems.Add($"Reference hierarchy is {string.Join(", ", games)}, expected {string.Join(", ", expected)} (art/106 section 1)");
            else if (References.Any(r => r.Informs.Length == 0 || r.NeverTake.Length == 0))
                report.Problems.Add("Every reference must state both what it informs and what may never be taken from it (art/107 section 6)");
            else
                report.Passed.Add("Reference hierarchy matches art/106 section 1, and each entry states its boundary");
        }

        /// <summary>
        /// art/107 section 6: a role may claim APPROVED or INTEGRATED only with a complete originality attestation.
        /// Used by the TASK 153+ batch reviews; it proves the record exists, never that the claim is true.
        /// </summary>
        public static IReadOnlyList<string> ValidateAttestations(
            IEnumerable<(string RoleId, string Status)> roles,
            IEnumerable<OriginalityAttestation> attestations)
        {
            var byRole = attestations
                .Where(a => a != null && a.RoleId.Length > 0)
                .GroupBy(a => a.RoleId)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

            var problems = new List<string>();
            foreach (var (roleId, status) in roles)
            {
                if (!StatusesRequiringAttestation.Contains(status)) continue;

                if (!byRole.TryGetValue(roleId, out var attestation))
                    problems.Add($"{roleId} claims {status} with no originality attestation (art/107 section 6).");
                else if (!attestation.IsComplete)
                    problems.Add($"{roleId} claims {status} but its attestation is incomplete: {attestation.Problem}.");
            }

            return problems;
        }

        /// <summary>UI art is canvas-space and authored at screen pixels; world art is not.</summary>
        public static bool IsUiSprite(string path) => path.Replace('\\', '/').Contains("/Art/UI/");

        /// <summary>The generated hardware cursors (Art/UI/cursor_*.png): Cursor textures, never sprites.</summary>
        public static bool IsHardwareCursor(string path) => IsUiSprite(path) && System.IO.Path.GetFileName(path).StartsWith("cursor_", StringComparison.Ordinal);

        private static IEnumerable<string> SpriteTexturePaths() =>
            ArtFolders
                .Where(AssetDatabase.IsValidFolder)
                .SelectMany(folder => AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(p => p, StringComparer.Ordinal);
    }
}
