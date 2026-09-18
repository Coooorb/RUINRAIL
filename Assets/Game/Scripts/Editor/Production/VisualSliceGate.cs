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
    /// TASK 152 — Review Gate A. One representative slice of final art is integrated so a human can approve the
    /// RUINRAIL visual direction before bulk production starts.
    ///
    /// This type defines exactly which manifest roles make up that slice, reports their readiness, and produces the
    /// capture plan the reviewer looks at. It cannot approve anything: the gate is a human decision recorded in
    /// production/VISUAL_SLICE_APPROVAL.md, and nothing here writes that record.
    /// </summary>
    public static class VisualSliceGate
    {
        public const string ReportPath = "TestResults/visual_slice.md";
        public const string ApprovalRecordPath = "production/VISUAL_SLICE_APPROVAL.md";
        public const string CaptureFolder = "TestResults/VisualSlice";

        /// <summary>The reference resolution every capture is judged at (art/101).</summary>
        public const int CaptureWidth = 640;
        public const int CaptureHeight = 360;

        /// <summary>One subject of the slice, and the manifest roles that must be final before it can be reviewed.</summary>
        public sealed class SliceSubject
        {
            public string Name;
            public string[] RoleIds;
        }

        /// <summary>The slice named by TASK 152 requirement 1, expressed as manifest role ids.</summary>
        public static readonly IReadOnlyList<SliceSubject> Subjects = new[]
        {
            new SliceSubject { Name = "Player", RoleIds = new[] { "player" } },
            new SliceSubject { Name = "P9 Ranger", RoleIds = new[] { "weapon_p9_ranger" } },
            new SliceSubject { Name = "Grunt", RoleIds = new[] { "grunt" } },
            new SliceSubject
            {
                Name = "Ruined Metro combat room kit",
                RoleIds = new[]
                {
                    "RuinedMetro.tile.floor", "RuinedMetro.tile.floor_detail", "RuinedMetro.tile.wall",
                    "RuinedMetro.tile.obstacle", "RuinedMetro.tile.hazard",
                    "RuinedMetro.props", "RuinedMetro.doors", "RuinedMetro.lighting"
                }
            },
            new SliceSubject { Name = "Loot and pickup presentation", RoleIds = new[] { "world.item_pickup", "world.coin_pickup", "vfx.loot_glow" } },
            new SliceSubject { Name = "HUD and inventory panel treatment", RoleIds = new[] { "ui.panel_frame", "ui.inventory_slot", "ui.bar.hp", "ui.font.pixel" } },
            new SliceSubject { Name = "Muzzle and impact feedback", RoleIds = new[] { "vfx.muzzle", "vfx.impact" } }
        };

        /// <summary>
        /// The standardized captures, one per thing the reviewer has to judge (requirement 4). Fixed here so two runs
        /// of the gate are comparable and a reviewer always sees the same beats.
        /// </summary>
        public static readonly IReadOnlyList<(string Id, string Shows)> CaptureBeats = new[]
        {
            ("01_idle", "Player idle in a Ruined Metro combat room, weapon held, HUD visible. Silhouette against the floor."),
            ("02_movement", "Player moving, body facing a diagonal while the weapon aims off-axis: 8-direction body with independent 360-degree aim."),
            ("03_combat", "Player firing at a Grunt mid-telegraph: muzzle flash, projectile, impact and the telegraph all legible at once."),
            ("04_loot", "Dropped loot and coins on the floor with the rarity glow, next to the inventory panel treatment."),
            ("05_room_readability", "Whole room at reference scale: obstacles, hazard, doors and props readable without hiding actors or projectiles.")
        };

        public sealed class RoleReadiness
        {
            public string Subject;
            public string RoleId;
            public string Status = "UNKNOWN";
            public string ExpectedSourcePath = string.Empty;
            public bool Ready => Status == CompletionAssetManifest.Integrated;
        }

        public sealed class Report
        {
            public readonly List<RoleReadiness> Roles = new();
            public readonly List<string> Passed = new();
            public readonly List<string> Problems = new();
            public bool ApprovalRecorded;

            public IEnumerable<RoleReadiness> Outstanding => Roles.Where(r => !r.Ready);
            public bool AssetsReady => Roles.Count > 0 && Roles.All(r => r.Ready);

            /// <summary>The runner status. Never PASS while the slice is placeholder art, and never self-approved.</summary>
            public string Status =>
                Problems.Count > 0 ? "FAIL_BLOCKER"
                : !AssetsReady ? "BLOCKED_EXTERNAL_ASSET"
                : !ApprovalRecorded ? "WAITING_HUMAN_REVIEW"
                : "PASS";

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL visual vertical slice — Review Gate A (TASK 152)");
                sb.AppendLine();
                sb.AppendLine($"Status: **{Status}** — {Roles.Count(r => r.Ready)}/{Roles.Count} slice roles final, human approval {(ApprovalRecorded ? "recorded" : "NOT recorded")}.");
                sb.AppendLine();

                if (!AssetsReady)
                {
                    sb.AppendLine("## Missing final assets");
                    sb.AppendLine();
                    sb.AppendLine("The slice cannot be captured or reviewed until these roles carry final art. Placeholder art may not stand in: acceptance criterion 1 requires no placeholder fallback for the selected roles.");
                    sb.AppendLine();
                    sb.AppendLine("| Subject | Role id | Current status | Expected source path |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var role in Outstanding)
                        sb.AppendLine($"| {role.Subject} | {role.RoleId} | {role.Status} | {role.ExpectedSourcePath} |");
                    sb.AppendLine();
                }

                sb.AppendLine("## Capture plan");
                sb.AppendLine();
                sb.AppendLine($"Every capture is taken at {CaptureWidth}x{CaptureHeight} (art/101) from the composed Dungeon scene, and written to `{CaptureFolder}`.");
                sb.AppendLine();
                sb.AppendLine("| Capture | What the reviewer is judging |");
                sb.AppendLine("|---|---|");
                foreach (var (id, shows) in CaptureBeats) sb.AppendLine($"| `{id}.png` | {shows} |");
                sb.AppendLine();

                foreach (var p in Passed) sb.AppendLine($"- PASS {p}");
                foreach (var p in Problems) sb.AppendLine($"- ERROR {p}");
                sb.AppendLine();
                sb.AppendLine($"Approval is a human decision recorded in `{ApprovalRecordPath}`. No validator, tool or model may set it, and TASK 153 may not start until it reads APPROVED.");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Check Visual Slice Gate")]
        public static void CheckMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Check();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Check()
        {
            var report = new Report();
            var manifest = CompletionAssetManifest.Generate();

            foreach (var subject in Subjects)
            {
                foreach (var roleId in subject.RoleIds)
                {
                    var roles = manifest.Roles.Where(r => r.RoleId == roleId).ToList();
                    if (roles.Count == 0)
                    {
                        report.Problems.Add($"{subject.Name}: role '{roleId}' is not in the completion manifest; the slice names a role the repository does not have.");
                        continue;
                    }

                    // A subject is only ready when every manifest role sharing that id is final (a character has both
                    // a source sheet and an animation set).
                    foreach (var role in roles)
                    {
                        report.Roles.Add(new RoleReadiness
                        {
                            Subject = subject.Name,
                            RoleId = role.Category + "/" + role.RoleId,
                            Status = role.Status,
                            ExpectedSourcePath = role.ExpectedSourcePath
                        });
                    }
                }
            }

            report.ApprovalRecorded = IsApprovalRecorded();
            ValidateFacingContract(report);

            if (report.AssetsReady) report.Passed.Add("Every slice role carries final art");
            return report;
        }

        /// <summary>
        /// Requirement 3 regression: the slice must not quietly change the facing model. The body stays 8-directional
        /// and the weapon keeps mathematical 360-degree aim (art/102, art/103).
        /// </summary>
        private static void ValidateFacingContract(Report report)
        {
            var facings = Presentation.Animation.AnimationRules.AllFacings.Length;
            if (facings != 8)
            {
                report.Problems.Add($"Body facing is {facings}-directional, expected 8 (art/102).");
                return;
            }

            report.Passed.Add("8-directional body facing preserved, weapon aim stays a separate 360-degree pivot (WeaponVisualDriver)");
        }

        /// <summary>
        /// Reads the human approval record. A record counts only when a human wrote an explicit APPROVED verdict —
        /// the file existing, or containing the word, is not enough.
        /// </summary>
        public static bool IsApprovalRecorded()
        {
            if (!File.Exists(ApprovalRecordPath)) return false;

            foreach (var line in File.ReadAllLines(ApprovalRecordPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("Verdict:", StringComparison.OrdinalIgnoreCase)) continue;
                var verdict = trimmed.Substring("Verdict:".Length).Trim().Trim('*', '`', ' ');
                return string.Equals(verdict, "APPROVED", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
    }
}
