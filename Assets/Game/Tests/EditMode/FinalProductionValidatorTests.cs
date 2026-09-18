using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.EditorTools.Production;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 145 — the final data/engineering validation passes with zero errors; external assets are listed separately, never masked.</summary>
    public sealed class FinalProductionValidatorTests
    {
        [Test]
        public void FinalValidation_AllSectionsPass_ExactCounts_NoEditorLeaks_ExternalBlockersListedSeparately()
        {
            var report = FinalProductionValidator.WriteReport();
            var markdown = report.ToMarkdown();
            Assert.IsTrue(report.DataPass, string.Join("\n", report.Sections.SelectMany(s => s.Errors.Select(e => s.Name + ": " + e))));
            Assert.AreEqual(9, report.Sections.Count);
            CollectionAssert.AreEqual(new[] { "Stable ids", "Serialized references", "Rooms and room pools", "Loot tables, rarity tables, Legendary mechanics, prices", "Enemies, Elites, Bosses", "Content counts (production/126)", "Build scenes and boot flow", "Assembly hygiene (no Editor leaks into runtime)", "Network prefabs" }, report.Sections.Select(s => s.Name));
            StringAssert.Contains("33 weapons / 9 armor / 16 accessories / 10 consumables / 9 enemies / 6 Elites / 6 Bosses / 63 rooms", markdown);
            StringAssert.Contains("every serialized object reference resolves", markdown);
            StringAssert.Contains("no runtime script uses UnityEditor outside #if UNITY_EDITOR", markdown);
            // Art and audio have landed, so the external-blocker list is now expected to be empty. It is derived from

            // the manifest rather than hard-coded, so it reports whatever is genuinely outstanding.

            CollectionAssert.IsEmpty(report.ExternalBlockers, "Every final asset role resolves.");
            StringAssert.Contains("## External art / audio blockers (BLOCKED_EXTERNAL_ASSET)", markdown);
            Assert.IsTrue(report.Findings.Any(f => f.Contains("composition root")), "The empty MainMenu/Base/Dungeon scenes are an actionable finding for the boot-flow task.");
            Assert.IsTrue(File.Exists(FinalProductionValidator.ReportPath));
            Assert.AreEqual(markdown, FinalProductionValidator.ValidateProject().ToMarkdown(), "Deterministic.");
        }
    }
}
