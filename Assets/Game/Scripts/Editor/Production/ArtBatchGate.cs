using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 153-164 — the acceptance gate each final art batch has to pass.
    ///
    /// Every one of those tasks asks the same question of a different slice of the manifest: are these roles final,
    /// do they import correctly, is their provenance recorded, and is the placeholder gone from the release path?
    /// Asking it once, here, means each batch is judged by identical rules and a reviewer reads one report format.
    ///
    /// It reports readiness. It never approves: visual quality is decided by a human at the TASK 152/165/176 gates.
    /// </summary>
    public static class ArtBatchGate
    {
        public const string ReportFolder = "TestResults/ArtBatches";

        /// <summary>One TASK's batch: which manifest roles it owns and what the art has to achieve.</summary>
        public sealed class Batch
        {
            public int TaskNumber;
            public string Name = string.Empty;
            /// <summary>Manifest categories this batch owns in full.</summary>
            public string[] Categories = Array.Empty<string>();
            /// <summary>Restricts the batch to these role ids when a category is shared between tasks.</summary>
            public Func<CompletionAssetManifest.Role, bool> RoleFilter;
            /// <summary>The readability bar this batch is judged against, from art/106.</summary>
            public string ReadabilityRule = string.Empty;

            public string ReportPath => $"{ReportFolder}/task_{TaskNumber}_{Name.ToLowerInvariant().Replace(' ', '_')}.md";
        }

        public sealed class Report
        {
            public Batch Batch;
            public readonly List<CompletionAssetManifest.Role> Roles = new();
            public readonly List<string> Problems = new();

            public IEnumerable<CompletionAssetManifest.Role> Outstanding =>
                Roles.Where(r => r.Status != CompletionAssetManifest.Integrated);

            public int Delivered => Roles.Count(r => r.Status == CompletionAssetManifest.Integrated);
            public bool Complete => Roles.Count > 0 && Delivered == Roles.Count && Problems.Count == 0;

            public string Status =>
                Problems.Count > 0 ? "FAIL_BLOCKER"
                : Complete ? "PASS"
                : "BLOCKED_EXTERNAL_ASSET";

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"# TASK {Batch.TaskNumber} — {Batch.Name}");
                sb.AppendLine();
                sb.AppendLine($"Status: **{Status}** — {Delivered}/{Roles.Count} roles final.");
                sb.AppendLine();
                if (Batch.ReadabilityRule.Length > 0)
                {
                    sb.AppendLine($"**Readability bar:** {Batch.ReadabilityRule}");
                    sb.AppendLine();
                }

                if (Outstanding.Any())
                {
                    sb.AppendLine("## Outstanding roles");
                    sb.AppendLine();
                    sb.AppendLine("| Role id | Status | Expected source path | Binding point |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var role in Outstanding.OrderBy(r => r.Category, StringComparer.Ordinal).ThenBy(r => r.RoleId, StringComparer.Ordinal))
                        sb.AppendLine($"| {role.RoleId} | {role.Status} | {role.ExpectedSourcePath} | {role.BindingPoint} |");
                    sb.AppendLine();
                }

                foreach (var problem in Problems) sb.AppendLine($"- ERROR {problem}");
                if (Problems.Count > 0) sb.AppendLine();

                sb.AppendLine("Delivery follows `docs/technical/136_ASSET_REPLACEMENT_WORKFLOW.md`. A role becomes INTEGRATED only when the asset is bound at its existing seam, imports cleanly, carries a provenance entry, and its placeholder has left the release path. Visual quality is approved by a human, never here.");
                return sb.ToString();
            }
        }

        /// <summary>The batches TASK 153-164 own, in task order. Together they must cover every art role in the manifest.</summary>
        public static readonly IReadOnlyList<Batch> Batches = new[]
        {
            new Batch
            {
                TaskNumber = 153, Name = "Final player sprite set",
                Categories = new[] { "Character source sprites", "Character animation clip roles" },
                RoleFilter = r => r.RoleId == "player",
                ReadabilityRule = "art/106 section 4: ~32x48 px survivor, head/torso/hands readable at 1x, no baked firearm (the weapon is a separate pivot sprite). Downed, GetUp and Death must stay visually distinct."
            },
            new Batch
            {
                TaskNumber = 154, Name = "Final normal enemy sprite sets",
                Categories = new[] { "Character source sprites", "Character animation clip roles" },
                RoleFilter = r => IsEnemyKind(r, "Enemy"),
                ReadabilityRule = "art/106 section 4: nine distinct silhouettes stating gameplay role before surface detail. Telegraph must be unmistakable against Move and Idle."
            },
            new Batch
            {
                TaskNumber = 155, Name = "Final elite sprite sets",
                Categories = new[] { "Character source sprites", "Character animation clip roles" },
                RoleFilter = r => IsEnemyKind(r, "Elite"),
                ReadabilityRule = "art/106 section 4: Elites escalate the world rather than recolouring a normal enemy; ~48-64+ px."
            },
            new Batch
            {
                TaskNumber = 156, Name = "Final boss sprite sets",
                Categories = new[] { "Character source sprites", "Character animation clip roles" },
                RoleFilter = r => IsEnemyKind(r, "Boss"),
                ReadabilityRule = "art/106 section 4: identifiable at room scale before the attack starts; telegraph built into the design; ~64-128 px."
            },
            new Batch
            {
                TaskNumber = 157, Name = "Final weapon sprite catalog",
                Categories = new[] { "Weapon sprites" },
                ReadabilityRule = "art/106 section 5: 33 distinct silhouettes; a class shares a family but never becomes a palette swap. Authored pointing +X for the 360-degree pivot."
            },
            new Batch
            {
                TaskNumber = 158, Name = "Equipment consumable loot and pickup art",
                Categories = new[] { "Item icons" },
                RoleFilter = r => true,
                ReadabilityRule = "art/106 section 9: icons read inside a rarity frame without the frame overwhelming the silhouette. World pickups and their icons must read as the same object. Requires the ItemDefinition icon seam."
            },
            new Batch
            {
                TaskNumber = 159, Name = "Ruined Metro final tileset and props",
                Categories = new[] { "Biome tiles", "Biome props and dressing", "Biome lighting" },
                RoleFilter = r => r.RoleId.StartsWith("RuinedMetro", StringComparison.Ordinal),
                ReadabilityRule = "art/106 section 7: damp concrete, station tile, rails, cables, failing emergency systems; cool dirty grey-green. Floors stay quiet enough that projectiles, loot and telegraphs survive."
            },
            new Batch
            {
                TaskNumber = 160, Name = "Rustworks final tileset and props",
                Categories = new[] { "Biome tiles", "Biome props and dressing", "Biome lighting" },
                RoleFilter = r => r.RoleId.StartsWith("Rustworks", StringComparison.Ordinal),
                ReadabilityRule = "art/106 section 7: oxidized steel, pipes, furnaces, conveyors, hazard markings; rust orange on dark metal. Higher mechanical density than Metro without obscuring navigation."
            },
            new Batch
            {
                TaskNumber = 161, Name = "Overgrown Labs final tileset and props",
                Categories = new[] { "Biome tiles", "Biome props and dressing", "Biome lighting" },
                RoleFilter = r => r.RoleId.StartsWith("OvergrownLabs", StringComparison.Ordinal),
                ReadabilityRule = "art/106 section 7: aged off-white lab surfaces and glass invaded by desaturated organic growth; restrained cyan/green light."
            },
            new Batch
            {
                TaskNumber = 162, Name = "Safehouse transit and base environment art",
                Categories = new[] { "World objects and base presentation" },
                ReadabilityRule = "art/106 section 8: the Shelter is patched but maintained, warmer and more inhabited than a dungeon; the transit car reads heavy, mechanical and dependable."
            },
            new Batch
            {
                TaskNumber = 163, Name = "Final UI skin icons glyphs and pixel font",
                Categories = new[] { "UI skin" },
                ReadabilityRule = "art/106 section 9: utilitarian shelter/field equipment. Every important state uses colour AND shape/icon/text. Focus states obvious for keyboard and controller. The font needs a recorded licence."
            },
            new Batch
            {
                TaskNumber = 164, Name = "63 room art dressing and environment integration",
                Categories = new[] { "Biome props and dressing" },
                RoleFilter = r => true,
                ReadabilityRule = "art/106 section 2: dressing may never hide an actor, projectile, hazard, door or interactable in any of the 63 rooms."
            }
        };

        private static bool IsEnemyKind(CompletionAssetManifest.Role role, string kind)
        {
            if (role.RoleId == "player") return false;
            var isElite = role.RoleId.StartsWith("elite_", StringComparison.Ordinal);
            var isBoss = role.RoleId.StartsWith("boss_", StringComparison.Ordinal);
            return kind switch
            {
                "Elite" => isElite,
                "Boss" => isBoss,
                _ => !isElite && !isBoss
            };
        }

        [MenuItem("RuinRail/Production/Check All Art Batches")]
        public static void CheckAllMenu()
        {
            foreach (var report in CheckAll()) Debug.Log(report.ToMarkdown());
        }

        public static IReadOnlyList<Report> CheckAll()
        {
            var manifest = CompletionAssetManifest.Generate();
            return Batches.Select(b => Check(b, manifest)).ToList();
        }

        public static Report Check(Batch batch, CompletionAssetManifest.Report manifest = null)
        {
            manifest ??= CompletionAssetManifest.Generate();
            var report = new Report { Batch = batch };

            var roles = manifest.Roles.Where(r => batch.Categories.Contains(r.Category));
            if (batch.RoleFilter != null) roles = roles.Where(batch.RoleFilter);
            report.Roles.AddRange(roles);

            if (report.Roles.Count == 0)
                report.Problems.Add($"TASK {batch.TaskNumber} owns no manifest roles; its batch definition does not match the manifest.");

            ValidateDeliveredRoles(report);
            return report;
        }

        /// <summary>
        /// Whatever the batch has actually delivered must survive the pipeline: correct import, recorded provenance,
        /// and no placeholder still standing in for a role that claims to be final.
        /// </summary>
        private static void ValidateDeliveredRoles(Report report)
        {
            var delivered = report.Roles.Where(r => r.Status == CompletionAssetManifest.Integrated).ToList();
            if (delivered.Count == 0) return;

            var provenance = AssetProvenanceRegistry.Load();
            report.Problems.AddRange(ArtProductionContract.ValidateAttestations(
                delivered.Select(r => (r.RoleId, r.Status)),
                provenance.Select(e => e.ToAttestation())));

            foreach (var role in delivered)
            {
                if (role.SourcePath.Contains(ArtProductionContract.PlaceholderFolder, StringComparison.OrdinalIgnoreCase))
                    report.Problems.Add($"{role.RoleId} claims INTEGRATED but still resolves to placeholder art at {role.SourcePath}.");

                var path = role.ExpectedSourcePath;
                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                {
                    report.Problems.Add($"{role.RoleId} claims INTEGRATED but no texture exists at {path}.");
                    continue;
                }

                // UI art is canvas-space and authored at screen pixels (art/107 section 3).


                var expectedPpu = ArtProductionContract.IsUiSprite(path) ? 1 : ArtProductionContract.RequiredPixelsPerUnit;


                if (!Mathf.Approximately(importer.spritePixelsPerUnit, expectedPpu))
                    report.Problems.Add($"{role.RoleId} at {path}: PPU {importer.spritePixelsPerUnit}, expected {expectedPpu}.");
                if (importer.filterMode != ArtProductionContract.RequiredFilterMode)
                    report.Problems.Add($"{role.RoleId} at {path}: filter {importer.filterMode}, expected Point.");
            }
        }

        public static Report WriteReport(Batch batch)
        {
            var report = Check(batch);
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllText(batch.ReportPath, report.ToMarkdown());
            return report;
        }

        public static IReadOnlyList<Report> WriteAllReports()
        {
            var manifest = CompletionAssetManifest.Generate();
            Directory.CreateDirectory(ReportFolder);
            var reports = Batches.Select(b => Check(b, manifest)).ToList();
            foreach (var report in reports) File.WriteAllText(report.Batch.ReportPath, report.ToMarkdown());
            return reports;
        }
    }
}
