using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.UI.Base;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// UI polish pass, section A5/A6 — the front-end layout is arithmetic, and overlap is a property that arithmetic
    /// can rule out.
    ///
    /// The bug these tests exist for was a profile block dropped at a fixed coordinate with a fixed width: a long
    /// display name, or a level that reached three digits, simply drew through whatever was beside it. Moving the
    /// label until one screenshot looked right would have left the bug in place for the next name. So the header now
    /// measures both blocks and reserves their space, and these tests drive it with the extremes.
    /// </summary>
    public sealed class UiLayoutTests
    {
        /// <summary>The longest name the display-name rules allow, which is the worst case the header must survive.</summary>
        private static readonly string MaxName = new('W', TextFit.MaxDisplayNameChars);

        private static IEnumerable<(string Name, UiRect Bounds)> HeaderBoxes(ScreenLayout.HeaderLayout header) =>
            new[]
            {
                ("title", header.Title.Bounds),
                ("subtitle", header.Subtitle.Bounds),
                ("profileName", header.ProfileName.Bounds),
                ("profileStats", header.ProfileStats.Bounds)
            };

        [Test]
        public void Header_NeverOverlaps_AcrossTheWholeRangeOfProfileValues()
        {
            var names = new[] { "", "A", "Smoke Runner", MaxName };
            var levels = new[] { 1, 9, 42, 999 };
            var coins = new[] { 0, 7, 1450, 999999 };

            foreach (var name in names)
            foreach (var level in levels)
            foreach (var coin in coins)
            {
                var header = ScreenLayout.BuildHeader("RUINRAIL", "THE SHELTER", name, level, coin);
                var problems = ScreenLayout.Overlaps(HeaderBoxes(header));
                CollectionAssert.IsEmpty(problems, $"name='{name}' level={level} coins={coin}");
            }
        }

        [Test]
        public void Header_KeepsEveryTextInsideItsOwnBlockAndInsideTheHeaderBand()
        {
            var header = ScreenLayout.BuildHeader("RUINRAIL", "THE SHELTER", MaxName, 999, 999999);

            Assert.IsTrue(header.Title.Bounds.Within(header.IdentityBlock), "The wordmark stays in the identity block.");
            Assert.IsTrue(header.Subtitle.Bounds.Within(header.IdentityBlock), "The subtitle stays in the identity block.");
            Assert.IsTrue(header.ProfileName.Bounds.Within(header.ProfileBlock), "The name stays in the profile block.");
            Assert.IsTrue(header.ProfileStats.Bounds.Within(header.ProfileBlock), "The stats stay in the profile block.");

            foreach (var (name, bounds) in HeaderBoxes(header))
                Assert.IsTrue(bounds.Within(ScreenLayout.Header), $"{name} {bounds} escapes the header band {ScreenLayout.Header}.");

            Assert.Less(header.IdentityBlock.Right, header.ProfileBlock.X,
                "The identity block and the profile block must be separated, not merely non-overlapping.");
        }

        [Test]
        public void Header_TruncatesRatherThanRunningPastItsReservation()
        {
            var header = ScreenLayout.BuildHeader("RUINRAIL", "THE SHELTER", MaxName, 999, 999999);

            foreach (var text in header.Texts)
            {
                var drawn = UiText.Width(text.Text, text.Scale);
                Assert.LessOrEqual(drawn, text.Bounds.Width,
                    $"'{text.Text}' measures {drawn} px in a {text.Bounds.Width} px box; the layout must truncate, never overflow.");
            }
        }

        [Test]
        public void Header_ClampsAnOverlongDisplayNameToTheDisplayNameBudget()
        {
            var header = ScreenLayout.BuildHeader("RUINRAIL", "THE SHELTER", new string('Q', 200), 12, 100);

            Assert.LessOrEqual(header.ProfileName.Text.Length, TextFit.MaxDisplayNameChars,
                "A name longer than the rules allow is clamped, not drawn.");
            CollectionAssert.IsEmpty(ScreenLayout.Overlaps(HeaderBoxes(header)));
        }

        // ---------------- primary navigation ----------------

        private static IReadOnlyList<(string Id, string Label)> StationTabs() =>
            BaseHubViewModel.Stations.Select(s => ("station." + s, BaseHubViewModel.Label(s))).ToList();

        [Test]
        public void Tabs_FitTheBar_WithoutOverlappingEachOtherOrTheExitAction()
        {
            var slots = ScreenLayout.BuildTabs(StationTabs(), ("station.close", "LEAVE"));

            Assert.AreEqual(BaseHubViewModel.Stations.Length + 1, slots.Count, "Every station plus LEAVE gets a slot.");
            CollectionAssert.IsEmpty(ScreenLayout.Overlaps(slots.Select(s => (s.Id, s.Bounds))));

            foreach (var slot in slots)
                Assert.IsTrue(slot.Bounds.Within(ScreenLayout.TabBar), $"{slot.Id} {slot.Bounds} escapes the tab bar.");
        }

        [Test]
        public void Tabs_AreWideEnoughForTheirOwnLabels_SoNoLabelIsCrammed()
        {
            foreach (var slot in ScreenLayout.BuildTabs(StationTabs(), ("station.close", "LEAVE")))
            {
                var label = UiText.Width(slot.Label);
                Assert.LessOrEqual(label, slot.Bounds.Width,
                    $"'{slot.Label}' needs {label} px and the {slot.Id} tab is {slot.Bounds.Width} px wide.");
            }
        }

        [Test]
        public void Tabs_PlaceLeaveApartFromTheContentSections()
        {
            var slots = ScreenLayout.BuildTabs(StationTabs(), ("station.close", "LEAVE"));
            var exit = slots.Single(s => s.IsExit);
            var lastContent = slots.Where(s => !s.IsExit).OrderBy(s => s.Bounds.Right).Last();

            Assert.AreEqual("station.close", exit.Id);
            Assert.Greater(exit.Bounds.X - lastContent.Bounds.Right, ScreenLayout.TabGap,
                "LEAVE is an action, not the eighth content tab, and is spaced apart from them.");
            Assert.AreEqual(ScreenLayout.Width - UiTheme.ScreenMargin, exit.Bounds.Right,
                "LEAVE is pinned to the right margin.");
        }

        // ---------------- content zones ----------------

        [Test]
        public void ContentColumns_TileTheContentBandWithoutOverlapOrEscape()
        {
            var columns = new[]
            {
                ("left", ScreenLayout.LeftColumn),
                ("main", ScreenLayout.MainColumn),
                ("right", ScreenLayout.RightColumn)
            };

            CollectionAssert.IsEmpty(ScreenLayout.Overlaps(columns));
            foreach (var (name, bounds) in columns)
            {
                Assert.IsTrue(bounds.Within(ScreenLayout.Content), $"The {name} column escapes the content band.");
                Assert.Greater(bounds.Width, 100, $"The {name} column is too narrow to hold a readable row.");
            }
        }

        [Test]
        public void ScreenBands_CoverTheReferenceResolutionExactlyAndInOrder()
        {
            Assert.AreEqual(0, ScreenLayout.Header.Y);
            Assert.AreEqual(ScreenLayout.Header.Bottom, ScreenLayout.TabBar.Y);
            Assert.AreEqual(ScreenLayout.TabBar.Bottom, ScreenLayout.Content.Y);
            Assert.AreEqual(ScreenLayout.Content.Bottom, ScreenLayout.Footer.Y);
            Assert.AreEqual(ScreenLayout.Height, ScreenLayout.Footer.Bottom, "The bands add up to 360 px exactly.");
            Assert.AreEqual(640, ScreenLayout.Width);
            Assert.AreEqual(360, ScreenLayout.Height);
        }

        [Test]
        public void Rows_NeverRunPastTheBottomOfTheirColumn()
        {
            var column = ScreenLayout.MainColumn;
            const int rowHeight = 14;
            const int gap = 2;

            var capacity = ScreenLayout.RowCapacity(column, rowHeight, gap);
            var rows = ScreenLayout.Rows(column, rowHeight, gap, capacity + 5);

            Assert.AreEqual(capacity, rows.Count, "RowCapacity and Rows must agree on how many rows fit.");
            CollectionAssert.IsEmpty(ScreenLayout.Overlaps(rows.Select((r, i) => ($"row{i}", r))));
            foreach (var row in rows) Assert.IsTrue(row.Within(column));
        }

        // ---------------- footer and prompts ----------------

        [Test]
        public void Footer_NamesEveryInputDeviceAndStillFitsTheScreen()
        {
            var footer = new UiPrompts().Footer();

            StringAssert.Contains("Mouse", footer, "The hint must mention the pointer now that the pointer works.");
            StringAssert.Contains("Enter", footer);
            StringAssert.Contains("A:", footer, "Controller glyphs appear beside the keyboard ones.");
            StringAssert.Contains("Esc", footer);
            StringAssert.Contains("Arrows", footer);
            StringAssert.Contains("D-Pad", footer);

            Assert.LessOrEqual(UiText.Width(footer), ScreenLayout.Width - UiTheme.ScreenMargin * 2,
                "The control hint fits on one line at the reference resolution.");
        }

        [Test]
        public void EveryStationDescription_FitsThePanelHeaderWithoutTruncation()
        {
            // The panel header reserves the main column minus its padding and the station icon beside the text.
            const int iconAndPadding = UiTheme.Pad * 2 + 20;
            var available = ScreenLayout.MainColumn.Width - iconAndPadding;

            foreach (BaseStation station in System.Enum.GetValues(typeof(BaseStation)))
            {
                var description = StationPresentation.DescriptionOf(station);
                Assert.IsNotEmpty(description, $"{station} must say what it is for.");
                Assert.LessOrEqual(UiText.Width(description), available,
                    $"{station}: \"{description}\" needs {UiText.Width(description)} px of a {available} px header and " +
                    "would be shown with an ellipsis. A purpose line that gets cut off is worse than a shorter one.");
            }
        }

        [Test]
        public void TextMetrics_MatchTheAuthoredPixelFace()
        {
            Assert.AreEqual(6, UiText.Advance, "Glyph advance at scale 1.");
            Assert.AreEqual(7, UiText.GlyphHeight);
            Assert.AreEqual(9, UiText.LineHeight);
            Assert.AreEqual(12, UiText.Width("AB"), "Two glyphs at scale 1.");
            Assert.AreEqual(24, UiText.Width("AB", 2), "Scale is an integer multiple of the face.");
            Assert.AreEqual(UiText.GlyphHeight + UiText.LineHeight, UiText.Height(2), "Two lines are one line box plus one advance.");
            Assert.AreEqual("ab…", UiText.Fit("abcdef", 18), "Fitting truncates with an ellipsis rather than clipping.");
            Assert.AreEqual("abc", UiText.Fit("abc", 18), "A string that fits is left alone.");
        }
    }
}
