using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The inventory overlay draws with the project pixel face through the shared <see cref="UiFont"/> seam, the face
    /// carries every glyph the tooltips use, and every box is sized in whole lines and truncates — so the longest
    /// item names and tooltips stay inside their own column instead of running into the backpack grid or the actions.
    /// </summary>
    public sealed class InventoryFontRegressionTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void InventoryView_DrawsEveryLabelWithThePixelFace_AtAuthoredSize()
        {
            var view = InventoryView.Create(new InventoryViewModel());
            _created.Add(view.gameObject);
            Assert.IsTrue(UiFont.UsingPixelFont);
            var texts = view.GetComponentsInChildren<Text>(true);
            Assert.GreaterOrEqual(texts.Length, 5 + InventoryViewModel.BackpackSlots + 3);
            foreach (var text in texts)
            {
                Assert.AreSame(UiFont.Font(), text.font, text.name);
                Assert.AreNotEqual("LegacyRuntime", text.font.name, text.name);
                Assert.AreEqual(UiText.GlyphHeight, text.fontSize, text.name);
                Assert.AreEqual(1f, text.lineSpacing, text.name);
                Assert.AreEqual(VerticalWrapMode.Truncate, text.verticalOverflow, $"{text.name}: never spills below its box");
                var rect = (RectTransform)text.transform;
                Assert.AreEqual(0, Mathf.RoundToInt(rect.sizeDelta.y) % UiText.LineHeight, $"{text.name}: box height {rect.sizeDelta.y} is whole lines");
            }
        }

        [Test]
        public void PixelFace_CarriesEveryGlyphTheTooltipsUse()
        {
            var font = UiFont.Font();
            foreach (var c in "–—…·/%:[]()x0123456789")
            {
                Assert.IsTrue(font.HasCharacter(c), $"missing glyph for '{c}' (U+{(int)c:X4})");
            }
        }

        [Test]
        public void LongestItemNames_FitTheirColumns_AndNoInventoryBoxOverlapsAnother()
        {
            var view = InventoryView.Create(new InventoryViewModel());
            _created.Add(view.gameObject);
            var window = view.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "Panel");
            var windowRect = WorldBox(window);
            // Every text box of the window (all panels, every nesting level) lies inside the window and no two overlap.
            var boxes = view.GetComponentsInChildren<Text>(true).Select(t => (t.name, rect: WorldBox((RectTransform)t.transform))).ToList();
            foreach (var (name, rect) in boxes)
            {
                Assert.IsTrue(windowRect.Contains(rect.min) && windowRect.Contains(rect.max), $"{name} {rect} inside the window {windowRect}");
            }

            for (var i = 0; i < boxes.Count; i++)
            for (var j = i + 1; j < boxes.Count; j++)
            {
                Assert.IsFalse(boxes[i].rect.Overlaps(boxes[j].rect), $"{boxes[i].name} {boxes[i].rect} overlaps {boxes[j].name} {boxes[j].rect}");
            }

            // The longest approved item name still fits the equipment row's name column on one line (or is ellipsised by
            // the front-end rule), never clipped mid-glyph; the details title has the full width of the details panel.
            var catalog = GameContentCatalog.Load();
            var longest = catalog.Items.Where(i => i != null).OrderByDescending(i => i.DisplayName.Length).First();
            var nameWidth = InventoryView.EquipmentPanel.Width - (UiTheme.Pad + InventoryView.SlotSize + 6) - UiTheme.Pad;
            var fitted = UiText.Fit(longest.DisplayName, nameWidth);
            Assert.LessOrEqual(UiText.Width(fitted), nameWidth, $"'{fitted}' fits the {nameWidth} px name column");
            Assert.IsTrue(fitted == longest.DisplayName || fitted.EndsWith("…"), "either whole or ellipsised, never clipped mid-glyph");
            Assert.LessOrEqual(UiText.Width(longest.DisplayName), InventoryView.DetailsPanel.Width - UiTheme.Pad * 2, "the details title shows the whole name");
        }

        /// <summary>World-space rect of a RectTransform (canvas units; the canvas is unscaled in a fresh test scene).</summary>
        private static Rect WorldBox(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = new Vector2(Mathf.Min(corners[0].x, corners[2].x), Mathf.Min(corners[0].y, corners[2].y));
            var max = new Vector2(Mathf.Max(corners[0].x, corners[2].x), Mathf.Max(corners[0].y, corners[2].y));
            // Shrink by a hair so boxes that share an edge on the pixel grid do not count as overlapping.
            return Rect.MinMaxRect(min.x + 0.01f, min.y + 0.01f, max.x - 0.01f, max.y - 0.01f);
        }
    }
}
