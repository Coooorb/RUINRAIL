using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 149 — the completion manifest is the single list a reviewer reads. These tests protect the two things
    /// automation can actually prove: that it still reconciles with the TASK-148 baseline (production/135), and that
    /// no category of release blocker silently disappears from it.
    /// </summary>
    public sealed class CompletionAssetManifestTests
    {
        [Test]
        public void Manifest_ReconcilesWithTask148Baseline()
        {
            var report = CompletionAssetManifest.Generate();

            Assert.IsNotEmpty(report.Reconciliations);
            foreach (var line in report.Reconciliations.Where(l => !l.Explained))
            {
                Assert.AreEqual(line.Baseline, line.Current,
                    $"{line.Item}: production/135 baseline {line.Baseline}, repository reports {line.Current} (source: {line.Source}). " +
                    "A real content change must update production/135 and be logged, not silently drift.");
            }

            foreach (var line in report.Reconciliations.Where(l => l.Explained))
            {
                Assert.AreNotEqual(line.Baseline, line.Current,
                    $"{line.Item} now matches its baseline, so its TASK-149 delta explanation is stale and must be removed.");
                StringAssert.Contains("production/135", line.ExplainedDelta, "An explained delta must name where the correction is recorded.");
            }

            Assert.IsTrue(report.AllReconciled, "Every count either matches production/135 or carries a verified explanation.");
        }

        [Test]
        public void Manifest_CoversEveryRequiredCategoryAndBlockerClass()
        {
            var report = CompletionAssetManifest.Generate();

            foreach (var category in new[]
                     {
                         "Character source sprites", "Character animation clip roles", "Weapon sprites", "Item icons",
                         "Biome tiles", "Biome props and dressing", "Biome lighting", "World objects and base presentation",
                         "UI skin", "VFX", "SFX", "Music", "Stingers", "Ambience", "Network prefab"
                     })
            {
                CollectionAssert.Contains(report.Categories.ToList(), category, "production/134 requires this manifest category.");
            }

            Assert.IsNotEmpty(report.LiveServiceBlockers, "TASK 180-181 blockers must stay visible.");
            Assert.IsNotEmpty(report.EngineeringBlockers, "TASK 177-179/183 blockers must stay visible.");
            Assert.AreEqual(7, report.HumanGates.Count, "production/132 lists seven mandatory human/external gates.");
            StringAssert.Contains("Windows x64", report.ReleasePlatformScope, "Release scope must name the only proven platform.");
        }

        [Test]
        public void Manifest_IsDeterministicAndCheckedIn()
        {
            var report = CompletionAssetManifest.WriteReport();

            Assert.IsTrue(File.Exists(CompletionAssetManifest.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), CompletionAssetManifest.Generate().ToMarkdown(), "Regeneration must be stable.");
            Assert.AreEqual(report.ToMarkdown(), File.ReadAllText(CompletionAssetManifest.ReportPath));
        }

        [Test]
        public void EveryRoleCarriesAStatusFromTheApprovedVocabulary()
        {
            var report = CompletionAssetManifest.Generate();
            var allowed = new[] { CompletionAssetManifest.Missing, CompletionAssetManifest.Placeholder, CompletionAssetManifest.Integrated };

            foreach (var role in report.Roles)
            {
                Assert.IsNotEmpty(role.RoleId);
                Assert.IsNotEmpty(role.Owner);
                CollectionAssert.Contains(allowed, role.Status, $"{role.RoleId} uses a status outside production/134.");
            }

            // Every role is expected to be integrated now that the art and audio pipelines have run. A role may only
            // claim INTEGRATED because its asset actually resolves, which the manifest derives from the filesystem.
            Assert.AreEqual(report.Roles.Count, report.Count(CompletionAssetManifest.Integrated),
                "Every manifest role should resolve to a real asset.");
        }
    }
}
