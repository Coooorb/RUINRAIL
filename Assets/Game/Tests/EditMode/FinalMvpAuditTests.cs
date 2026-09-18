using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 148 — the final audit is evidence-based: every engineering line passes on the real repository, the external roles are listed, and the status can never be COMPLETE while they are missing.</summary>
    public sealed class FinalMvpAuditTests
    {
        [Test]
        public void FinalAudit_EngineeringPasses_ExternalContentBlocks_StatusIsTruthful()
        {
            var report = FinalMvpAudit.WriteReport();
            var fails = report.Lines.Where(l => l.Result == "FAIL").Select(l => l.Requirement + ": " + l.Evidence).ToList();
            CollectionAssert.IsEmpty(fails, "Every engineering/verification requirement must pass on the real repository.");
            Assert.IsTrue(report.Lines.Count >= 30);
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("33 weapons") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("63 rooms") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("Exactly 11 music roles") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("Post-MVP") && l.Result == "PASS"), "No prohibited scope in runtime code.");
            Assert.Greater(report.ExternalBlockers.Count, 0, "The repository ships no final art/animation/audio content.");
            Assert.AreEqual(FinalMvpAudit.Status.BLOCKED_EXTERNAL_ASSET, report.FinalStatus, "Never COMPLETE while mandatory external content is missing.");
            Assert.IsTrue(report.NotRun.Any(n => n.Contains("Relay")));
            Assert.IsTrue(report.Lines.Where(l => l.Requirement.Contains("suite green")).All(l => l.Result != "FAIL"), "Suite lines are NOT RUN inside the suite and PASS in the post-harness audit — never fabricated.");
            Assert.IsTrue(File.Exists(FinalMvpAudit.ReportPath));
            StringAssert.Contains("## FINAL STATUS: **BLOCKED_EXTERNAL_ASSET**", File.ReadAllText(FinalMvpAudit.ReportPath));
        }
    }
}
