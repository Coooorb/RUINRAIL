using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 153-164 — the batch gates must between them cover every art role in the manifest, must not overlap in a
    /// way that lets a role be claimed twice, and must block rather than pass while the art is missing.
    /// </summary>
    public sealed class ArtBatchGateTests
    {
        [Test]
        public void EveryBatch_OwnsAtLeastOneManifestRole()
        {
            foreach (var report in ArtBatchGate.CheckAll())
            {
                Assert.IsNotEmpty(report.Roles, $"TASK {report.Batch.TaskNumber} ({report.Batch.Name}) matches no manifest role.");
                CollectionAssert.IsEmpty(report.Problems, $"TASK {report.Batch.TaskNumber} batch definition is inconsistent with the manifest.");
                Assert.IsNotEmpty(report.Batch.ReadabilityRule, $"TASK {report.Batch.TaskNumber} must state the bar its art is judged against.");
            }
        }

        [Test]
        public void CharacterBatches_PartitionThePlayerEnemiesElitesAndBosses()
        {
            var reports = ArtBatchGate.CheckAll().ToDictionary(r => r.Batch.TaskNumber);

            // Each actor contributes two roles: a source sheet and an animation set.
            Assert.AreEqual(2, reports[153].Roles.Count, "One player actor.");
            Assert.AreEqual(9 * 2, reports[154].Roles.Count, "Nine normal enemy archetypes.");
            Assert.AreEqual(6 * 2, reports[155].Roles.Count, "Six Elites.");
            Assert.AreEqual(6 * 2, reports[156].Roles.Count, "Six Bosses.");

            var actorRoles = reports[153].Roles.Concat(reports[154].Roles).Concat(reports[155].Roles).Concat(reports[156].Roles)
                .Select(Key).ToList();
            Assert.AreEqual(22 * 2, actorRoles.Count, "22 animation sets in total.");
            Assert.AreEqual(actorRoles.Count, actorRoles.Distinct().Count(), "No actor may be claimed by two batches.");
        }

        [Test]
        public void ContentBatches_MatchTheApprovedCounts()
        {
            var reports = ArtBatchGate.CheckAll().ToDictionary(r => r.Batch.TaskNumber);

            Assert.AreEqual(33, reports[157].Roles.Count, "33 weapons.");
            Assert.AreEqual(72, reports[158].Roles.Count, "72 item-family icons.");

            foreach (var task in new[] { 159, 160, 161 })
            {
                Assert.AreEqual(5 + 6 + 1, reports[task].Roles.Count,
                    $"TASK {task}: five tile categories, six prop packages and one lighting profile for its biome.");
            }
        }

        /// <summary>Roles are value-like records regenerated per call, so they are compared by identity key, not reference.</summary>
        private static string Key(CompletionAssetManifest.Role role) => role.Category + "/" + role.RoleId;

        [Test]
        public void EveryArtRoleInTheManifest_IsOwnedByAtLeastOneBatch()
        {
            var manifest = CompletionAssetManifest.Generate();
            var owned = ArtBatchGate.CheckAll().SelectMany(r => r.Roles).Select(Key).ToHashSet();

            // Audio, network and animation-audit categories belong to later tasks, not to the art batches.
            var artCategories = new[]
            {
                "Character source sprites", "Character animation clip roles", "Weapon sprites", "Item icons",
                "Biome tiles", "Biome props and dressing", "Biome lighting", "World objects and base presentation", "UI skin"
            };

            var unowned = manifest.Roles
                .Where(r => artCategories.Contains(r.Category))
                .Select(Key)
                .Where(key => !owned.Contains(key))
                .ToList();

            CollectionAssert.IsEmpty(unowned, "Every art role must be some task's responsibility, or it will never be produced.");
        }

        [Test]
        public void EveryBatch_ReportsItsDeliveryStateHonestly()
        {
            // Art has landed, so the gate is now checked for reporting delivery truthfully rather than for being
            // uniformly empty. A batch is PASS exactly when every one of its roles is delivered, never otherwise.
            foreach (var report in ArtBatchGate.CheckAll())
            {
                var label = "TASK " + report.Batch.TaskNumber;
                CollectionAssert.IsEmpty(report.Problems, label + " reported a delivery problem.");
                Assert.AreEqual(report.Delivered == report.Roles.Count, report.Complete, label);
                Assert.AreEqual(report.Complete ? "PASS" : "BLOCKED_EXTERNAL_ASSET", report.Status, label);
            }
        }

        [Test]
        public void OutstandingRoles_TellTheArtistWhereTheAssetGoesAndWhatBindsIt()
        {
            foreach (var report in ArtBatchGate.CheckAll())
            {
                foreach (var role in report.Outstanding)
                {
                    Assert.IsNotEmpty(role.ExpectedSourcePath, $"{role.RoleId} has no delivery path.");
                    Assert.IsNotEmpty(role.BindingPoint, $"{role.RoleId} names no binding point.");
                }
            }
        }

        [Test]
        public void BatchReports_AreWritten()
        {
            var reports = ArtBatchGate.WriteAllReports();

            Assert.AreEqual(12, reports.Count, "TASK 153 through 164.");
            foreach (var report in reports)
            {
                Assert.IsTrue(File.Exists(report.Batch.ReportPath), $"{report.Batch.ReportPath} was not written.");
                StringAssert.Contains(report.Complete ? "PASS" : "BLOCKED_EXTERNAL_ASSET", report.ToMarkdown());
            }
        }
    }
}
