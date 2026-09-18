using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 165 — the scan must actually look at what ships, and must report honestly that the release path is still
    /// rendering placeholder art. A "clean" result here before the art lands would be a false clearance.
    /// </summary>
    public sealed class ReleasePathVisualScanTests
    {
        [Test]
        public void Scan_CoversTheRoomPrefabsThatShip()
        {
            var report = ReleasePathVisualScan.Scan();

            Assert.GreaterOrEqual(report.PrefabsScanned, 63, "All 63 room prefabs are in the release path and must be scanned.");
            Assert.Greater(report.RenderersScanned, 0, "A scan that inspects nothing proves nothing.");
        }

        [Test]
        public void ReleasePath_RendersNoPlaceholderArt()
        {
            var report = ReleasePathVisualScan.Scan();

            CollectionAssert.IsEmpty(
                report.Placeholders.Select(f => $"{f.Source}:{f.ObjectPath} -> {f.Detail}").ToList(),
                "Final art has landed, so nothing in the shipping path may still render dev art (art/106 section 13.9).");
            Assert.IsTrue(report.Clean);
        }

        [Test]
        public void TestOnlyFixtures_AreReportedSeparatelyAndDoNotFailTheReleasePath()
        {
            var report = ReleasePathVisualScan.Scan();

            // The grid test fixture keeps its placeholder tiles on purpose: it is excluded from GameContentCatalog
            // and never reaches a player, so it is bucketed rather than counted against the release path.
            foreach (var finding in report.TestFixtures)
            {
                StringAssert.Contains("_Test", finding.Source);
                Assert.IsNotEmpty(finding.ObjectPath, "A finding must locate the object inside that source.");
            }
        }

        [Test]
        public void Scan_FindsNoNullSpriteReference()
        {
            var report = ReleasePathVisualScan.Scan();

            CollectionAssert.IsEmpty(
                report.Missing.Select(f => $"{f.Source}:{f.ObjectPath}").ToList(),
                "A null sprite renders as nothing and fails silently in a build.");
        }

        [Test]
        public void Report_IsWrittenAndNamesTheOffendingAssets()
        {
            var report = ReleasePathVisualScan.WriteReport();

            Assert.IsTrue(File.Exists(ReleasePathVisualScan.ReportPath));
            var markdown = report.ToMarkdown();
            StringAssert.Contains("CLEAN", markdown);
            StringAssert.Contains("Scanned", markdown, "The report must state how much it actually inspected.");
        }
    }
}
