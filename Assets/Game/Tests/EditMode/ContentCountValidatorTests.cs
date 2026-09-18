using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.EditorTools.Production;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 130 — the production content validator proves every fixed V1 count/ID (production/126) and writes the deterministic report.</summary>
    public sealed class ContentCountValidatorTests
    {
        [Test]
        public void Project_MatchesEveryApprovedContentCount_AndWritesTheReport()
        {
            var report = ContentCountValidator.ValidateProject();
            var path = ContentCountValidator.WriteReport(report);
            Assert.IsTrue(File.Exists(path));
            Assert.IsTrue(report.Pass, "\n" + report.ToMarkdown());

            int Actual(string category, string scope) => report.Lines.Single(l => l.Category == category && l.Scope == scope).Actual;
            Assert.AreEqual(3, Actual("Biomes", "all"));
            Assert.AreEqual(63, Actual("Room prefabs", "all biomes"));
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                Assert.AreEqual(21, Actual("Room prefabs", biome.ToString()));
                Assert.AreEqual(2, Actual("Rooms: Start", biome.ToString()));
                Assert.AreEqual(5, Actual("Rooms: Combat Small", biome.ToString()));
                Assert.AreEqual(4, Actual("Rooms: Combat Medium", biome.ToString()));
                Assert.AreEqual(2, Actual("Rooms: Combat Large", biome.ToString()));
                Assert.AreEqual(1, Actual("Rooms: Merchant", biome.ToString()));
                Assert.AreEqual(2, Actual("Rooms: Event", biome.ToString()));
                Assert.AreEqual(1, Actual("Rooms: Loot", biome.ToString()));
                Assert.AreEqual(1, Actual("Rooms: Treasure", biome.ToString()));
                Assert.AreEqual(1, Actual("Rooms: MedicalRecovery", biome.ToString()));
                Assert.AreEqual(2, Actual("Rooms: Boss", biome.ToString()));
                Assert.AreEqual(2, Actual("Elites", biome.ToString()));
                Assert.AreEqual(2, Actual("Bosses", biome.ToString()));
            }

            Assert.AreEqual(33, Actual("Weapons", "all"));
            Assert.AreEqual(11, Actual("Weapon classes", "with authored weapons"));
            Assert.AreEqual(22, Actual("Weapons", "regular"));
            Assert.AreEqual(11, Actual("Weapons", "Legendary-only"));
            Assert.AreEqual(9, Actual("Armor families", "all"));
            Assert.AreEqual(16, Actual("Accessory families", "all"));
            Assert.AreEqual(10, Actual("Consumables", "all"));
            Assert.AreEqual(9, Actual("Normal enemy archetypes", "all"));
            Assert.AreEqual(6, Actual("Elites", "all"));
            Assert.AreEqual(6, Actual("Bosses", "all"));
            Assert.AreEqual(6, Actual("Dungeon event kinds", "all"));
            Assert.IsEmpty(report.Problems);
        }

        [Test]
        public void Report_IsDeterministic_AndListsEveryLineWithPassFail()
        {
            var a = ContentCountValidator.ValidateProject().ToMarkdown();
            var b = ContentCountValidator.ValidateProject().ToMarkdown();
            Assert.AreEqual(a, b, "Same project, byte-identical report (stable ordering, no timestamps).");
            StringAssert.Contains("Result: **PASS**", a);
            StringAssert.Contains("| Room prefabs | all biomes | 63 | 63 | PASS |", a);
            StringAssert.Contains("| Weapons | all | 33 | 33 | PASS |", a);
            StringAssert.Contains("| Elites | Rustworks | 2 | 2 | PASS |", a);
        }

        [Test]
        public void MissingExtraOrDuplicateContent_FailsTheValidator()
        {
            // The report model itself: any mismatch or problem flips the verdict.
            var exact = new ContentCountReport(new[] { new ContentCountLine("Bosses", "all", 6, 6) }, new string[0]);
            Assert.IsTrue(exact.Pass);
            var missing = new ContentCountReport(new[] { new ContentCountLine("Bosses", "all", 6, 5) }, new string[0]);
            Assert.IsFalse(missing.Pass);
            StringAssert.Contains("| Bosses | all | 6 | 5 | FAIL |", missing.ToMarkdown());
            var extra = new ContentCountReport(new[] { new ContentCountLine("Bosses", "all", 6, 7) }, new string[0]);
            Assert.IsFalse(extra.Pass, "Post-MVP content accidentally counted fails as well.");
            var duplicate = new ContentCountReport(new[] { new ContentCountLine("Bosses", "all", 6, 6) }, new[] { "duplicate boss id 'boss_x' (2 definitions)." });
            Assert.IsFalse(duplicate.Pass);
            StringAssert.Contains("## Problems", duplicate.ToMarkdown());

            // And the project scan really is the source of those lines: constants match production/126 exactly.
            Assert.AreEqual(63, ContentCountValidator.RoomsTotal);
            Assert.AreEqual(21, ContentCountValidator.RoomsPerBiome);
            Assert.AreEqual(33, ContentCountValidator.Weapons);
            Assert.AreEqual(11, ContentCountValidator.WeaponClasses);
            Assert.AreEqual(9, ContentCountValidator.ArmorFamilies);
            Assert.AreEqual(16, ContentCountValidator.AccessoryFamilies);
            Assert.AreEqual(10, ContentCountValidator.Consumables);
            Assert.AreEqual(9, ContentCountValidator.NormalEnemies);
            Assert.AreEqual(6, ContentCountValidator.Elites);
            Assert.AreEqual(6, ContentCountValidator.Bosses);
            Assert.AreEqual(6, ContentCountValidator.DungeonEvents);
            Assert.AreEqual(21, ContentCountValidator.RoomDistribution.Sum(d => d.count));
        }
    }
}
