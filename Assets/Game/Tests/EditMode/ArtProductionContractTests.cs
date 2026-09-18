using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 150 — art/107 is the contract every later art batch is reviewed against. These tests protect the parts a
    /// machine can prove: the approved technical numbers have not drifted, the reference hierarchy is intact, and a
    /// role cannot claim approval without a recorded originality attestation.
    /// </summary>
    public sealed class ArtProductionContractTests
    {
        [Test]
        public void TechnicalContract_StillMatchesTheApprovedArtSpecifications()
        {
            var report = ArtProductionContract.Validate();

            CollectionAssert.IsEmpty(report.Problems, "art/107 section 2 reconciled art/100-104 with art/106 and found no contradiction; a problem here means an approved number moved.");
            Assert.IsTrue(report.Pass);
            Assert.IsNotEmpty(report.Passed);
        }

        [Test]
        public void ImportContract_UsesTheApprovedPixelRules()
        {
            Assert.AreEqual(32, ArtProductionContract.RequiredPixelsPerUnit, "art/101: one 32 px tile is one world unit.");
            Assert.AreEqual(UnityEngine.FilterMode.Point, ArtProductionContract.RequiredFilterMode, "art/106 section 3: preserve hard pixel edges.");
            Assert.AreEqual(UnityEditor.TextureImporterCompression.Uncompressed, ArtProductionContract.RequiredCompression, "Block compression destroys pixel art.");
            Assert.AreEqual(UnityEditor.TextureImporterType.Sprite, ArtProductionContract.RequiredTextureType);
        }

        [Test]
        public void ReferenceHierarchy_MatchesTheFinalArtBible()
        {
            var references = ArtProductionContract.References;

            CollectionAssert.AreEqual(
                new[] { "Soul Knight", "Zero Sievert", "Fallout 4", "ARC Raiders" },
                references.Select(r => r.Game).ToArray(),
                "art/106 section 1 fixes both the set and the order.");

            Assert.AreEqual(ArtProductionContract.ReferenceStrength.Strongest, references[0].Strength, "Soul Knight: sprite construction.");
            Assert.AreEqual(ArtProductionContract.ReferenceStrength.Important, references[1].Strength, "Zero Sievert: gritty pixel-world finish.");
            Assert.AreEqual(ArtProductionContract.ReferenceStrength.Strongest, references[2].Strength, "Fallout 4: atmosphere.");
            Assert.AreEqual(ArtProductionContract.ReferenceStrength.Medium, references[3].Strength, "ARC Raiders: industrial sci-fi accent.");

            foreach (var reference in references)
            {
                Assert.IsNotEmpty(reference.Informs, $"{reference.Game} must state what it informs.");
                Assert.IsNotEmpty(reference.NeverTake, $"{reference.Game} must state what may never be taken from it.");
            }
        }

        [Test]
        public void ApprovedRole_WithoutACompleteAttestation_IsRejected()
        {
            var complete = new ArtProductionContract.OriginalityAttestation
            {
                RoleId = "player", Author = "RUINRAIL art", Provenance = "original", License = "project-owned"
            };

            var problems = ArtProductionContract.ValidateAttestations(
                new[]
                {
                    ("player", "INTEGRATED"),
                    ("grunt", "APPROVED"),
                    ("brute", "MISSING")
                },
                new[] { complete });

            Assert.AreEqual(1, problems.Count, "Only the APPROVED role with no attestation is a problem; MISSING needs none.");
            StringAssert.Contains("grunt", problems[0]);
            StringAssert.Contains("no originality attestation", problems[0]);
        }

        [Test]
        public void AttestationDerivedFromAReferenceGame_IsRejected()
        {
            var derived = new ArtProductionContract.OriginalityAttestation
            {
                RoleId = "grunt", Author = "RUINRAIL art", Provenance = "reference screenshot", License = "project-owned",
                DerivedFromReferenceGame = true
            };

            Assert.IsFalse(derived.IsComplete);
            StringAssert.Contains("derived from a reference game", derived.Problem);

            var problems = ArtProductionContract.ValidateAttestations(new[] { ("grunt", "APPROVED") }, new[] { derived });
            Assert.AreEqual(1, problems.Count);
            StringAssert.Contains("derived from a reference game", problems[0]);
        }

        [Test]
        public void ContractReport_IsWrittenAndDeterministic()
        {
            var report = ArtProductionContract.WriteReport();

            Assert.IsTrue(File.Exists(ArtProductionContract.ReportPath));
            Assert.AreEqual(report.ToMarkdown(), ArtProductionContract.Validate().ToMarkdown());
            StringAssert.Contains("human judgments", report.ToMarkdown(), "The report must say plainly what it cannot check.");
        }
    }
}
