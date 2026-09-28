using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Inventory
{
    /// <summary>
    /// The one delayed item inspection of the Shelter and the in-game inventory (ui/93, ui/94): after a short deliberate
    /// rest on an item — pointer hover or keyboard / controller focus — a compact card shows that item's name, rarity,
    /// category and its stat lines. When the item can be compared with worn gear, a second card beside it shows the
    /// equipped item of the relevant slot with its own lines — two items side by side, never a list of differences.
    /// The lines are the authoritative tooltip every item window uses (<see cref="ItemTooltip.Build"/> through
    /// <see cref="ItemDetailLayout"/>): no stat is computed here. The prose description is left out when the item has
    /// stat lines, so the card stays short. The cards sit beside the inspected slot (right, else left, else one on each
    /// side), never over it and never over each other, clamped inside the canvas. The host calls <see cref="Track"/>
    /// every frame with what is inspected now; anything else — a new item, nothing, a drag — hides them at once and
    /// restarts the delay.
    /// </summary>
    public sealed class ItemStatPopup : MonoBehaviour
    {
        public const float DelaySeconds = 0.45f;
        public const int Width = 170;
        public const int MaxRows = 16;
        public const int InnerWidth = Width - UiTheme.Pad * 2;
        private const int Gap = 4;
        private const int Margin = 4;
        private const int HeaderHeight = 30;

        private Card _item;
        private Card _equipped;
        private string _key;
        private float _since;

        public bool IsVisible => gameObject.activeSelf;
        public string TitleText => _item.Title.text;
        public string SubtitleText => _item.Subtitle.text;
        public IReadOnlyList<string> RowTexts => _item.RowTexts;
        /// <summary>Where the inspected item's card sits, in the root canvas's space (x right, y up, centre origin).</summary>
        public Rect Bounds => _item.Bounds;
        /// <summary>True while the equipped item's card is shown beside the inspected one.</summary>
        public bool IsComparing => IsVisible && _equipped.Root.gameObject.activeSelf;
        public string ComparedTitleText => IsComparing ? _equipped.Title.text : string.Empty;
        public string ComparedSubtitleText => IsComparing ? _equipped.Subtitle.text : string.Empty;
        /// <summary>The small right-hand tag of each card's title row ("EQUIPPED", "STARTER" or empty).</summary>
        public string TagText => _item.Tag.text;
        public string ComparedTagText => IsComparing ? _equipped.Tag.text : string.Empty;
        public IReadOnlyList<string> ComparedRowTexts => IsComparing ? _equipped.RowTexts : Array.Empty<string>();
        public Rect ComparedBounds => _equipped.Bounds;
        public int Shows { get; private set; }

        /// <summary>Builds the (hidden) cards on the root canvas of <paramref name="anyChild"/>, drawn above that canvas's content.</summary>
        public static ItemStatPopup Create(Transform anyChild)
        {
            var canvas = anyChild.GetComponentInParent<Canvas>().rootCanvas;
            var go = new GameObject("ItemStatPopup", typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            var popup = go.AddComponent<ItemStatPopup>();
            popup._item = new Card(go.transform, "Inspected");
            popup._equipped = new Card(go.transform, "Equipped");
            foreach (var g in go.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false; // inspection never takes the pointer from the slots
            go.SetActive(false);
            return popup;
        }

        /// <summary>
        /// Call every frame with what is inspected now (null key = nothing) and, when it compares, the equipped item's
        /// tooltip. The cards open once the same key has been inspected for <see cref="DelaySeconds"/> (unscaled: a
        /// paused world does not stall it) and close the moment it changes.
        /// </summary>
        public void Track(string key, Func<ItemTooltip> tooltip, RectTransform slot, Func<ItemTooltip> equipped = null)
        {
            if (key != _key)
            {
                _key = key;
                _since = Time.unscaledTime;
                Hide();
            }

            if (key == null || slot == null || IsVisible || Time.unscaledTime - _since < DelaySeconds) return;
            var content = tooltip?.Invoke();
            if (content != null) Show(content, slot, equipped?.Invoke());
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// <summary>Opens at once for <paramref name="slot"/> (the delay is <see cref="Track"/>'s); <paramref name="equipped"/> adds the second card.</summary>
        public void Show(ItemTooltip tooltip, RectTransform slot, ItemTooltip equipped = null)
        {
            var itemHeight = _item.Fill(tooltip, null);
            var equippedHeight = equipped != null ? _equipped.Fill(equipped, "EQUIPPED") : 0;
            _equipped.Root.gameObject.SetActive(equipped != null);
            Place(slot, itemHeight, equippedHeight);
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            Shows++;
        }

        /// <summary>
        /// The pair beside the slot, the inspected card nearest it: right if both fit, else left, else one card on each
        /// side; each card top-aligned with the slot and clamped inside the canvas, on whole pixels.
        /// </summary>
        private void Place(RectTransform slot, int itemHeight, int equippedHeight)
        {
            var area = ((RectTransform)transform.parent).rect;
            var s = SlotBounds(slot);
            var both = equippedHeight > 0;
            var span = both ? Width * 2 + Gap : Width;
            var rightRoom = area.xMax - Margin - (s.xMax + Gap);
            var leftRoom = s.xMin - Gap - (area.xMin + Margin);
            float itemX, equippedX;
            if (rightRoom >= span) { itemX = s.xMax + Gap; equippedX = itemX + Width + Gap; }
            else if (leftRoom >= span) { itemX = s.xMin - Gap - Width; equippedX = itemX - Gap - Width; }
            else if (both && rightRoom >= Width && leftRoom >= Width) { itemX = s.xMax + Gap; equippedX = s.xMin - Gap - Width; }
            else
            {
                // No clean side: the side with more room, clamped (the only case that may cover part of the slot).
                itemX = rightRoom >= leftRoom ? s.xMax + Gap : s.xMin - Gap - span;
                itemX = Mathf.Clamp(itemX, area.xMin + Margin, area.xMax - Margin - span);
                equippedX = itemX + Width + Gap;
            }

            _item.Place(itemX, s.yMax, itemHeight, area);
            if (both) _equipped.Place(equippedX, s.yMax, equippedHeight, area);
        }

        /// <summary>The inspected slot's rect in the same space as <see cref="Bounds"/> (also a test seam).</summary>
        public Rect SlotBounds(RectTransform slot)
        {
            var canvas = (RectTransform)transform.parent;
            var corners = new Vector3[4];
            slot.GetWorldCorners(corners);
            var min = canvas.InverseTransformPoint(corners[0]);
            var max = canvas.InverseTransformPoint(corners[2]);
            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        /// <summary>The panel rows of one tooltip: its description only when it has no stat lines, capped at <see cref="MaxRows"/>.</summary>
        public static List<DetailRow> RowsOf(ItemTooltip tooltip)
        {
            var hasStats = tooltip.BaseStats.Count > 0 || tooltip.Affixes.Count > 0;
            var description = tooltip.Description;
            if (hasStats) tooltip.Description = string.Empty;
            var rows = ItemDetailLayout.Compose(tooltip, Array.Empty<ComparisonLine>(), InnerWidth);
            tooltip.Description = description;
            return rows.Count > MaxRows ? rows.Take(MaxRows - 1).Append(new DetailRow("…", string.Empty, UiTheme.InkMuted)).ToList() : rows;
        }

        private static Color Readable(Color color)
        {
            var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance < 0.45f ? Color.Lerp(color, Color.white, (0.45f - luminance) / 0.45f) : color;
        }

        /// <summary>One item's card: a 1 px edge around an opaque fill, rarity rule, name, rarity line and the stat rows.</summary>
        private sealed class Card
        {
            public readonly RectTransform Root;
            public readonly Text Title;
            public readonly Text Subtitle;
            public readonly Text Tag;
            private readonly Image _rarityRule;
            private readonly List<Text> _rows = new();

            public Rect Bounds { get; private set; }
            public IReadOnlyList<string> RowTexts => _rows.Where(r => r.gameObject.activeSelf).Select(r => r.text).ToList();

            public Card(Transform parent, string name)
            {
                Root = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
                Root.SetParent(parent, false);
                Root.anchorMin = Root.anchorMax = new Vector2(0.5f, 0.5f);
                Root.pivot = new Vector2(0f, 1f);
                Root.GetComponent<Image>().color = UiTheme.PanelEdgeSoft;
                var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
                var fillRect = (RectTransform)fill.transform;
                fillRect.SetParent(Root, false);
                fillRect.anchorMin = Vector2.zero;
                fillRect.anchorMax = Vector2.one;
                fillRect.offsetMin = new Vector2(1, 1);
                fillRect.offsetMax = new Vector2(-1, -1);
                fill.GetComponent<Image>().color = UiTheme.NearBlack;
                _rarityRule = UiBuild.Plate(Root, new UiRect(0, 0, Width, 2), UiTheme.Amber, "RarityRule");
                Title = UiBuild.Label(Root, string.Empty, new UiRect(UiTheme.Pad, 5, InnerWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Title");
                Tag = UiBuild.Label(Root, string.Empty, new UiRect(UiTheme.Pad, 5, InnerWidth, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.InkMuted, false, "Tag");
                Subtitle = UiBuild.Label(Root, string.Empty, new UiRect(UiTheme.Pad, 15, InnerWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Subtitle");
                UiBuild.Plate(Root, new UiRect(UiTheme.Pad, 26, InnerWidth, 1), UiTheme.PanelEdgeSoft, "Rule");
                for (var i = 0; i < MaxRows; i++)
                    _rows.Add(UiBuild.Label(Root, string.Empty, new UiRect(UiTheme.Pad, HeaderHeight + i * (UiText.LineHeight + 1), InnerWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Row" + i));
            }

            /// <summary>Writes the tooltip into the card and returns its height.</summary>
            public int Fill(ItemTooltip tooltip, string tag)
            {
                // Title row: name (x count) left, a short tag right (EQUIPPED on the compared card, else STARTER); rarity · category below.
                var style = RarityStyle.For(tooltip.Rarity);
                Tag.text = tag ?? (tooltip.IsUnsellable ? "STARTER" : string.Empty);
                Tag.color = tag != null ? UiTheme.Amber : UiTheme.InkMuted;
                var titleWidth = Tag.text.Length > 0 ? InnerWidth - UiText.Width(Tag.text) - UiText.Advance : InnerWidth;
                var name = tooltip.Name + (tooltip.Quantity.HasValue && tooltip.Quantity.Value > 1 ? " x" + tooltip.Quantity.Value : string.Empty);
                Title.text = UiText.Fit(name, titleWidth);
                Title.color = Readable(style.Color);
                Subtitle.text = UiText.Fit(style.Label + " · " + tooltip.CategoryText, InnerWidth);
                _rarityRule.color = style.Color;

                var rows = RowsOf(tooltip);
                for (var i = 0; i < _rows.Count; i++)
                {
                    var visible = i < rows.Count;
                    _rows[i].gameObject.SetActive(visible);
                    if (!visible) continue;
                    _rows[i].text = ItemDetailLayout.Render(rows[i], InnerWidth);
                    _rows[i].color = rows[i].Color;
                }

                var height = HeaderHeight + Mathf.Max(1, rows.Count) * (UiText.LineHeight + 1) + UiTheme.Pad;
                Root.sizeDelta = new Vector2(Width, height);
                return height;
            }

            public void Place(float x, float slotTop, int height, Rect area)
            {
                x = Mathf.Round(Mathf.Clamp(x, area.xMin + Margin, area.xMax - Margin - Width));
                var top = Mathf.Round(Mathf.Clamp(slotTop, area.yMin + Margin + height, area.yMax - Margin));
                Root.anchoredPosition = new Vector2(x, top);
                Bounds = new Rect(x, top - height, Width, height);
            }
        }
    }
}
