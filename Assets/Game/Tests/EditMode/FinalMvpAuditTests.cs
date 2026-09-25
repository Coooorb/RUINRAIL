using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 148 — the final audit is evidence-based: every engineering line passes on the real repository, the external roles are derived from the asset audits, and the status can never be COMPLETE while one is missing.</summary>
    public sealed class FinalMvpAuditTests
    {
        [Test]
        public void FinalAudit_EngineeringPasses_ExternalContentDerived_StatusIsTruthful()
        {
            var report = FinalMvpAudit.WriteReport();
            var fails = report.Lines.Where(l => l.Result == "FAIL").Select(l => l.Requirement + ": " + l.Evidence).ToList();
            CollectionAssert.IsEmpty(fails, "Every engineering/verification requirement must pass on the real repository.");
            Assert.IsTrue(report.Lines.Count >= 30);
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("33 weapons") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("63 rooms") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("Exactly 11 music roles") && l.Result == "PASS"));
            Assert.IsTrue(report.Lines.Any(l => l.Requirement.StartsWith("Post-MVP") && l.Result == "PASS"), "No prohibited scope in runtime code.");
            // External content is derived from the asset audits and the completion manifest, never hard-coded either way.
            var manifest = CompletionAssetManifest.Generate();
            var animation = AnimationAssetAudit.Audit();
            var contentMissing = manifest.Roles.Any(x => x.Status != CompletionAssetManifest.Integrated) || AudioAssetAudit.Audit().Blocked.Any()
                || !MusicAssetAudit.Audit().ContentComplete || animation.Blocked.Any() || animation.WeaponSpritesPresent < animation.WeaponSpritesRequired;
            Assert.AreEqual(contentMissing, report.ExternalBlockers.Count > 0, "The external-content blockers must mirror the manifest and the audio/music audits.");
            Assert.AreEqual(contentMissing ? FinalMvpAudit.Status.BLOCKED_EXTERNAL_ASSET : FinalMvpAudit.Status.COMPLETE, report.FinalStatus, "Never COMPLETE while mandatory external content is missing; never BLOCKED once it is all integrated.");
            Assert.IsTrue(report.NotRun.Any(n => n.Contains("Relay")));
            Assert.IsTrue(report.Lines.Where(l => l.Requirement.Contains("suite green")).All(l => l.Result != "FAIL"), "Suite lines are NOT RUN inside the suite and PASS in the post-harness audit — never fabricated.");
            Assert.IsTrue(File.Exists(FinalMvpAudit.ReportPath));
            StringAssert.Contains($"## FINAL STATUS: **{report.FinalStatus}**", File.ReadAllText(FinalMvpAudit.ReportPath));
        }
    }
}
