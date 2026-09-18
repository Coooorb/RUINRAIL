using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 152 — Review Gate A. The gate's job is to be un-passable by automation: it must name the slice in terms
    /// of real manifest roles, refuse to claim readiness while those roles are placeholders, and never record its own
    /// approval.
    /// </summary>
    public sealed class VisualSliceGateTests
    {
        [Test]
        public void SliceRoles_AllExistInTheManifest()
        {
            var report = VisualSliceGate.Check();

            CollectionAssert.IsEmpty(report.Problems, "Every role named by the slice must exist in the completion manifest.");
            Assert.IsNotEmpty(report.Roles);
        }

        [Test]
        public void SliceCovers_EverySubjectNamedByTheTask()
        {
            var subjects = VisualSliceGate.Subjects.Select(s => s.Name).ToList();

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Player", "P9 Ranger", "Grunt", "Ruined Metro combat room kit",
                    "Loot and pickup presentation", "HUD and inventory panel treatment", "Muzzle and impact feedback"
                },
                subjects,
                "TASK 152 requirement 1 fixes the slice contents.");

            foreach (var subject in VisualSliceGate.Subjects)
                Assert.IsNotEmpty(subject.RoleIds, $"{subject.Name} must name the manifest roles it depends on.");
        }

        [Test]
        public void SliceRolesAreFinal_SoTheGateAwaitsOnlyHumanReview()
        {
            var report = VisualSliceGate.Check();

            CollectionAssert.IsEmpty(report.Outstanding.Select(o => o.RoleId).ToList(),
                "Every slice role now carries final art.");
            Assert.IsTrue(report.AssetsReady);

            // Assets being ready is not approval. The gate must still report that no human has signed it off.
            Assert.IsFalse(report.ApprovalRecorded);
            Assert.AreEqual("WAITING_HUMAN_REVIEW", report.Status);
        }

        [Test]
        public void Gate_NeverSelfApproves()
        {
            Assert.IsFalse(VisualSliceGate.IsApprovalRecorded(),
                "No human verdict has been recorded, so the gate must report that it is not approved.");

            var report = VisualSliceGate.Check();
            Assert.IsFalse(report.ApprovalRecorded);
            Assert.AreNotEqual("PASS", report.Status, "Assets alone never make the gate pass.");
        }

        [Test]
        public void ApprovalRecord_OnlyCountsAnExplicitHumanVerdict()
        {
            var path = VisualSliceGate.ApprovalRecordPath;
            var original = File.Exists(path) ? File.ReadAllText(path) : null;

            try
            {
                File.WriteAllText(path, "# Slice\n\nThis slice looks APPROVED to me, maybe.\n");
                Assert.IsFalse(VisualSliceGate.IsApprovalRecorded(), "Prose containing the word is not a verdict.");

                File.WriteAllText(path, "# Slice\n\nVerdict: REJECTED\n");
                Assert.IsFalse(VisualSliceGate.IsApprovalRecorded());

                File.WriteAllText(path, "# Slice\n\nVerdict: APPROVED\n");
                Assert.IsTrue(VisualSliceGate.IsApprovalRecorded(), "An explicit verdict line is the only thing that counts.");
            }
            finally
            {
                if (original != null) File.WriteAllText(path, original);
                else if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void CapturePlan_IsStandardizedAtReferenceResolution()
        {
            Assert.AreEqual(640, VisualSliceGate.CaptureWidth);
            Assert.AreEqual(360, VisualSliceGate.CaptureHeight);

            var ids = VisualSliceGate.CaptureBeats.Select(b => b.Id).ToList();
            CollectionAssert.AreEqual(
                new[] { "01_idle", "02_movement", "03_combat", "04_loot", "05_room_readability" },
                ids,
                "Requirement 4: idle, movement, combat, loot and room readability, in a fixed order so runs are comparable.");

            foreach (var (id, shows) in VisualSliceGate.CaptureBeats)
                Assert.IsNotEmpty(shows, $"{id} must state what the reviewer is judging.");
        }

        [Test]
        public void Report_IsWrittenAndNamesTheOutstandingRoles()
        {
            var report = VisualSliceGate.WriteReport();

            Assert.IsTrue(File.Exists(VisualSliceGate.ReportPath));
            var markdown = report.ToMarkdown();
            StringAssert.Contains("WAITING_HUMAN_REVIEW", markdown);
            StringAssert.Contains("Capture plan", markdown, "The report must state what a reviewer is to look at.");
            StringAssert.Contains(VisualSliceGate.ApprovalRecordPath, markdown);
        }
    }
}
