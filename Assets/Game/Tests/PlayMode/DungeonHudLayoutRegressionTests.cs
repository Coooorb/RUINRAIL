using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.UI.Hud;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// Regression for the lower-left HUD overlap: the HP bar, the HP text and the Dash text each own a band of the
    /// HP panel at one line pitch, every HUD line is drawn in the project pixel face at its authored size, no two
    /// HUD lines share pixels, nothing leaves the 640x360 frame, and the layout is a fixed function of the reference
    /// resolution (corner anchors + integer offsets) rather than of whatever the window happens to be.
    /// </summary>
    public sealed class DungeonHudLayoutRegressionTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private DungeonHudView View()
        {
            var view = DungeonHudView.Create(new DungeonHudViewModel());
            _created.Add(view.gameObject);
            return view;
        }

        /// <summary>Reference-pixel rect (origin bottom-left) by walking the corner anchors up to the canvas.</summary>
        private static Rect ReferenceRect(RectTransform rect, DungeonHudView view)
        {
            var reference = new Vector2(DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight);
            var origin = Vector2.zero;
            var current = rect;
            while (current != null && current != (RectTransform)view.transform)
            {
                var parent = current.parent as RectTransform;
                var parentSize = parent == (RectTransform)view.transform || parent == null ? reference : parent.sizeDelta;
                Assert.AreEqual(current.anchorMin, current.anchorMax, $"{current.name}: corner-anchored (stable at any window size)");
                Assert.AreEqual(Mathf.Round(current.anchoredPosition.x), current.anchoredPosition.x, $"{current.name}: integer x offset");
                Assert.AreEqual(Mathf.Round(current.anchoredPosition.y), current.anchoredPosition.y, $"{current.name}: integer y offset");
                origin += Vector2.Scale(current.anchorMin, parentSize) + current.anchoredPosition - Vector2.Scale(current.pivot, current.sizeDelta);
                current = parent;
            }

            return new Rect(origin, rect.sizeDelta);
        }

        [Test]
        public void BottomLeft_DashIconSlot_ThenBarOverHpText_OccupySeparateBands()
        {
            var view = View();
            var dashPanel = ReferenceRect(view.DashPanel, view);
            var bar = ReferenceRect((RectTransform)view.HpPanel.Find("HpBarBack"), view);
            var texts = view.HpPanel.GetComponentsInChildren<Text>(true);
            Assert.AreEqual(1, texts.Length, "one HP text — the dash indicator is an icon slot, not a text line");
            var hp = ReferenceRect((RectTransform)texts.Single(t => t.name == "HpText").transform, view);
            Assert.IsEmpty(view.DashPanel.GetComponentsInChildren<Text>(true), "no DASH READY / DASH n% text anywhere in the dash slot");
            Assert.IsNotNull(view.DashIcon, "the dash icon view exists");
            Assert.AreEqual(HudDashIconView.Size, Mathf.RoundToInt(dashPanel.width));

            Assert.IsFalse(bar.Overlaps(hp), $"bar {bar} vs HP {hp}");
            Assert.IsFalse(dashPanel.Overlaps(bar) || dashPanel.Overlaps(hp), $"dash slot {dashPanel} vs HP block {bar} / {hp}");
            Assert.Greater(bar.yMin, hp.yMax - 0.01f, "bar above HP text");
            Assert.GreaterOrEqual(hp.height, UiText.LineHeight, "a full line box for the HP text");
            Assert.AreEqual(DungeonHudView.Margin, Mathf.RoundToInt(dashPanel.yMin), "the dash slot sits on the bottom margin");
            Assert.AreEqual(DungeonHudView.Margin, Mathf.RoundToInt(dashPanel.xMin), "and on the left margin");
            Assert.AreEqual(DungeonHudView.Margin, Mathf.RoundToInt(hp.yMin), "the HP text sits on the bottom margin beside it");
            Assert.Greater(hp.xMin, dashPanel.xMax, "the HP block starts right of the dash slot");
        }

        [Test]
        public void EveryHudLine_UsesThePixelFace_AtItsAuthoredSize_WithUnitLineSpacing()
        {
            var view = View();
            Assert.IsTrue(UiFont.UsingPixelFont, "the release path draws with the project pixel font, not LegacyRuntime.ttf");
            foreach (var text in view.GetComponentsInChildren<Text>(true))
            {
                Assert.AreSame(UiFont.Font(), text.font, text.name);
                Assert.AreEqual(UiText.GlyphHeight, text.fontSize, text.name);
                Assert.AreEqual(1f, text.lineSpacing, text.name);
                Assert.AreEqual(VerticalWrapMode.Truncate, text.verticalOverflow, $"{text.name}: a line never spills into the band below");
            }
        }

        [Test]
        public void NoTwoHudLines_SharePixels_AndAllStayInsideTheFrame()
        {
            var view = View();
            var frame = new Rect(0f, 0f, DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight);
            var lines = view.GetComponentsInChildren<Text>(true)
                .Select(t => (t.name, rect: ReferenceRect((RectTransform)t.transform, view)))
                .ToList();
            Assert.GreaterOrEqual(lines.Count, 9, "HP, weapon names/resources/specials, slot numbers, consumable chip, biome, depth chip, coins, room title, party lines");
            foreach (var (name, rect) in lines)
            {
                Assert.IsTrue(frame.Contains(rect.min) && frame.Contains(rect.max), $"{name} {rect} inside 640x360");
            }

            for (var i = 0; i < lines.Count; i++)
            for (var j = i + 1; j < lines.Count; j++)
            {
                Assert.IsFalse(lines[i].rect.Overlaps(lines[j].rect), $"{lines[i].name} {lines[i].rect} overlaps {lines[j].name} {lines[j].rect}");
            }

            var names = lines.Select(l => l.name).ToList();
            Assert.AreEqual(names.Count, names.Distinct().Count(), "every HUD line is built once");
        }

        [Test]
        public void HpString_FitsItsBand_AtItsLongestApprovedForm_AndWeaponResourceLinesFitTheirColumn()
        {
            var view = View();
            var hp = view.HpPanel.GetComponentsInChildren<Text>(true).Single(t => t.name == "HpText");
            // The longest strings the view model produces: three-digit HP with the shield tag; a three-digit magazine and reserve.
            var longestHp = "HP 999 / 999  [SHIELD]";
            Assert.LessOrEqual(UiText.Width(longestHp), ((RectTransform)hp.transform).sizeDelta.x, "HP line fits its band width");
            Assert.AreEqual(HorizontalWrapMode.Overflow, hp.horizontalOverflow, "single-line: never wraps into the band below");
            Assert.LessOrEqual(UiText.Width("999 / 999"), HudWeaponSlotView.TextWidth, "the ammo readout fits the weapon text column");
            Assert.LessOrEqual(UiText.Width("OVERHEATED"), HudWeaponSlotView.TextWidth, "the heat readout fits the weapon text column");
        }

        [Test]
        public void BottomBands_AreIconSlots_NotTextLines_AndNeverOverlap()
        {
            var view = View();
            Assert.IsNotNull(view.PrimarySlot);
            Assert.IsNotNull(view.SecondarySlot);
            Assert.IsNotNull(view.ConsumableSlot);
            Assert.AreEqual("1", view.PrimarySlot.SlotNumber);
            Assert.AreEqual("2", view.SecondarySlot.SlotNumber);
            Assert.AreEqual(HudWeaponSlotView.SlotSize, Mathf.RoundToInt(view.PrimarySlot.SlotRect.sizeDelta.x), "a 40 px icon slot per weapon");
            Assert.AreEqual(HudConsumableSlotView.Size, Mathf.RoundToInt(ReferenceRect(view.ConsumablePanel, view).width));
            var panels = new[] { view.DashPanel, view.HpPanel, view.WeaponsPanel, view.ConsumablePanel, view.TopLeftPanel, view.TopRightPanel, view.PartyPanel, view.BossPanel, view.RoomTitlePanel }
                .Select(p => (p.name, rect: ReferenceRect(p, view))).ToList();
            var frame = new Rect(0f, 0f, DungeonHudView.ReferenceWidth, DungeonHudView.ReferenceHeight);
            foreach (var (name, rect) in panels)
                Assert.IsTrue(frame.Contains(rect.min) && frame.Contains(rect.max), $"{name} {rect} inside 640x360");
            for (var i = 0; i < panels.Count; i++)
            for (var j = i + 1; j < panels.Count; j++)
                Assert.IsFalse(panels[i].rect.Overlaps(panels[j].rect), $"{panels[i].name} {panels[i].rect} overlaps {panels[j].name} {panels[j].rect}");
            var empty = view.ConsumableSlot;
            Assert.IsTrue(empty.IsEmpty && string.IsNullOrEmpty(empty.CountText), "nothing equipped: neutral empty slot, no text");
            Assert.AreEqual(string.Empty, view.PrimaryText, "an empty weapon slot shows no resource text");
        }
    }
}
