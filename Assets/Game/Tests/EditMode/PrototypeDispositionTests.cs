using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 179 — every PROTOTYPE marker had to be dispositioned. What automation can prove is that none was quietly
    /// dropped: release-owned code carries no undecided marker except the one the register explicitly records as
    /// BLOCKED_REVIEW, and that one still says so in the code.
    /// </summary>
    public sealed class PrototypeDispositionTests
    {
        private const string Register = "production/137_PROTOTYPE_VALUE_DISPOSITIONS.md";
        private static readonly string[] ReleaseRoots = { "Assets/Game/Scripts", "Assets/Game/ScriptableObjects" };

        /// <summary>Release-owned authored sources: tests and editor tooling ship no gameplay tunable.</summary>
        private static IEnumerable<string> ReleaseSources() =>
            ReleaseRoots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                .Select(f => f.Replace('\\', '/'))
                .Where(f => f.EndsWith(".cs") || f.EndsWith(".asset"))
                .Where(f => !f.Contains("/Tests/") && !f.Contains("/Editor/"));

        private static List<(string File, int Line, string Text)> Markers()
        {
            var found = new List<(string, int, string)>();
            foreach (var file in ReleaseSources())
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (lines[i].Contains("PROTOTYPE")) found.Add((file, i + 1, lines[i].Trim()));
            }

            return found;
        }
        [Test]
        public void NoUndispositionedMarkerRemains()
        {
            var markers = Markers();

            // The biome lighting tints were the last BLOCKED_REVIEW. They could not be decided while the biome art
            // did not exist; authoring that art resolved them, so no undecided value now ships.
            Assert.AreEqual(0, markers.Count,
                "Every PROTOTYPE marker is dispositioned. A surviving one is an undecided value about to ship:\n  " +
                string.Join("\n  ", markers.Select(m => $"{m.File}:{m.Line} {m.Text}")));
        }

        [Test]
        public void DispositionRegisterExists_AndRecordsEveryCategory()
        {
            Assert.IsTrue(File.Exists(Register), $"{Register} is the sign-off record for all 35 markers.");
            var text = File.ReadAllText(Register);

            foreach (var category in new[]
                     {
                         "Camera", "Feedback and recoil", "Blaster heat audio", "Music and ambience",
                         "Network interpolation", "Pickup feel", "Downed and revive", "Depth scaling", "Economy",
                         "Dungeon events", "Merchant and trader", "Stagger", "Grenade", "Tutorial", "Lighting", "Font metrics"
                     })
            {
                StringAssert.Contains(category, text, $"production/135 requires the {category} category to be dispositioned.");
            }

            StringAssert.Contains("**Values changed: 0.**", text,
                "No evidence existed to tune anything: TASK 182 and 183 have not run. Inventing numbers to delete the word PROTOTYPE is what production/135 forbids.");
        }

        [Test]
        public void EveryDispositionedMarker_KeptItsTunableMetadata()
        {
            // production/135 requires the surrounding comment and config metadata to survive the marker swap.
            var converted = ReleaseSources()
                .Where(f => File.ReadAllText(f).Contains("V1 FINAL (TASK 179)"))
                .ToList();

            Assert.AreEqual(21, converted.Count, "All 21 files holding dispositioned markers carry the decision.");

            foreach (var file in converted)
            {
                foreach (var line in File.ReadAllLines(file).Where(l => l.Contains("V1 FINAL (TASK 179)")))
                {
                    var remainder = line.Replace("V1 FINAL (TASK 179)", string.Empty).Trim(' ', '/', '<', '>', '"', '[', ']', '(', ')', '.', ':', ';', '*');
                    Assert.Greater(remainder.Length, 12,
                        $"{file}: a disposition replaced the explanation instead of the marker — '{line.Trim()}'");
                }
            }
        }
    }
}
