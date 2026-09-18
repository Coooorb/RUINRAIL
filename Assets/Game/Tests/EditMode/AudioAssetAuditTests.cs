using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Audio;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 140 — the audio contract is complete in data while every clip is truthfully an external blocker.</summary>
    public sealed class AudioAssetAuditTests
    {
        [Test]
        public void Audit_ContractComplete_ContentBlocked_ListsEveryRequiredEvent()
        {
            var report = AudioAssetAudit.WriteReport();
            Assert.IsTrue(report.ContractComplete, string.Join("\n", report.Problems));
            Assert.AreEqual(AudioEventIds.Required.Count, report.Required);
            Assert.AreEqual(report.Required, report.Defined);
            // Final audio has landed: every required event now resolves to a generated clip.
            Assert.AreEqual(report.Required, report.WithClips, "Every required event has a clip.");
            Assert.IsTrue(report.ContentComplete);
            Assert.AreEqual(0, report.Blocked.Count(), "Nothing is blocked once every event has a clip.");
            StringAssert.Contains("COMPLETE", report.ToMarkdown());
            StringAssert.Contains(AudioEventIds.DropLegendary, report.ToMarkdown());
            StringAssert.Contains(AudioEventIds.BlasterVent, report.ToMarkdown());
            Assert.IsTrue(File.Exists(AudioAssetAudit.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), AudioAssetAudit.Audit().ToMarkdown(), "Deterministic.");
            Assert.AreEqual(11, AudioEventIds.Required.Count(r => r.id.StartsWith("weapon.fire.") || r.id == AudioEventIds.BowRelease || r.id == AudioEventIds.KnifeSlash || r.id == AudioEventIds.SpearThrust), "One fire identity per weapon class.");
        }
    }
}
