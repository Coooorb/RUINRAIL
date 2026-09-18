using System.IO;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 141 — routing/data complete with exact slot counts; every track, stinger and ambience loop is a truthful external blocker.</summary>
    public sealed class MusicAssetAuditTests
    {
        [Test]
        public void Audit_RoutingComplete_ContentBlocked_ExactCounts()
        {
            var report = MusicAssetAudit.WriteReport();
            Assert.IsTrue(report.CatalogExists);
            Assert.IsTrue(report.ExactSlots);
            Assert.IsTrue(report.RoutingComplete);
            Assert.IsTrue(report.ContentComplete, "All 11 tracks, 6 stingers and 3 ambience loops are bound.");
            Assert.AreEqual(0, report.MissingTracks.Length);
            Assert.AreEqual(0, report.MissingStingers.Length);
            Assert.AreEqual(0, report.MissingAmbience.Length);
            var md = report.ToMarkdown();
            StringAssert.Contains("Ruined Metro — Boss", md);
            StringAssert.Contains("LegendaryDrop", md);
            StringAssert.Contains("tracks 11/11, stingers 6/6, ambience 3/3", md);
            Assert.IsTrue(File.Exists(MusicAssetAudit.ReportPath));
            Assert.AreEqual(md, MusicAssetAudit.Audit().ToMarkdown());
        }
    }
}
