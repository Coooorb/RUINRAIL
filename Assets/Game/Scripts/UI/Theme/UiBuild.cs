using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// The uGUI drawing primitives of the 640×360 reference screens: rects authored top-left/y-down in reference
    /// pixels, flat plates, 1 px borders drawn as four plates (a stretched sprite border drifts by a pixel), panels,
    /// pixel-font labels at integer scales, the eight focus bracket strokes, and sliced UI sprites from the skin.
    /// <c>UiKit</c> (the App toolkit) forwards here; the in-run inventory view builds through this directly because the
    /// UI assembly cannot see the App assembly.
    /// </summary>
    public static class UiBuild
    {
        private static Sprite _solid;

        /// <summary>
        /// A 1×1 opaque white sprite, created once per process.
        ///
        /// It is a drawing primitive, not art: uGUI's <see cref="Image"/> ignores <see cref="Image.type"/> entirely
        /// when it has no sprite (it falls back to a plain quad), so a filled/masked overlay such as the dash cooldown
        /// wipe silently draws as a full rectangle with its fill amount discarded. Handing it this sprite is what
        /// makes <see cref="Image.Type.Filled"/> behave at all. Nothing about the look comes from here — the colour
        /// and the fill are the caller's.
        /// </summary>
        public static Sprite Solid()
        {
            if (_solid != null) return _solid;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "UiSolid",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            _solid = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            _solid.name = "UiSolid";
            _solid.hideFlags = HideFlags.HideAndDontSave;
            return _solid;
        }

        /// <summary>An <see cref="Image.Type.Filled"/> overlay that actually honours its fill amount.</summary>
        public static Image Fillable(Transform parent, UiRect bounds, Color color, Image.FillMethod method, int origin, string name = "Fill")
        {
            var rect = NewRect(parent, name, bounds);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = Solid();
            image.type = Image.Type.Filled;
            image.fillMethod = method;
            image.fillOrigin = origin;
            image.fillAmount = 0f;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static RectTransform NewRect(Transform parent, string name, UiRect bounds)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(bounds.X, -bounds.Y);
            rect.sizeDelta = new Vector2(bounds.Width, bounds.Height);
            return rect;
        }

        /// <summary>A flat colour plate. The workhorse of every panel, bar, edge and separator.</summary>
        public static Image Plate(Transform parent, UiRect bounds, Color color, string name = "Plate")
        {
            var rect = NewRect(parent, name, bounds);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>A 9-sliced skin sprite (panel frame, slot, rarity frame) stretched to the rect; a null sprite degrades to a plate.</summary>
        public static Image Sliced(Transform parent, UiRect bounds, Sprite sprite, Color? tint = null, string name = "Sprite")
        {
            var rect = NewRect(parent, name, bounds);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 1f;
                image.color = tint ?? Color.white;
            }
            else
            {
                image.color = tint ?? UiTheme.Charcoal;
            }

            return image;
        }

        /// <summary>A 1 px border drawn as four plates, on exact pixels at any size.</summary>
        public static IReadOnlyList<Image> Border(Transform parent, UiRect bounds, Color color, int thickness = 1)
        {
            var t = Mathf.Max(1, thickness);
            return new[]
            {
                Plate(parent, new UiRect(bounds.X, bounds.Y, bounds.Width, t), color, "EdgeTop"),
                Plate(parent, new UiRect(bounds.X, bounds.Bottom - t, bounds.Width, t), color, "EdgeBottom"),
                Plate(parent, new UiRect(bounds.X, bounds.Y, t, bounds.Height), color, "EdgeLeft"),
                Plate(parent, new UiRect(bounds.Right - t, bounds.Y, t, bounds.Height), color, "EdgeRight")
            };
        }

        /// <summary>A content panel: plate plus border, on the 4 px grid.</summary>
        public static GameObject Panel(Transform parent, UiRect bounds, string name = "Panel", Color? fill = null, Color? edge = null)
        {
            var root = NewRect(parent, name, bounds).gameObject;
            var inner = new UiRect(0, 0, bounds.Width, bounds.Height);
            Plate(root.transform, inner, fill ?? UiTheme.Plate, "Fill");
            Border(root.transform, inner, edge ?? UiTheme.PanelEdgeSoft);
            return root;
        }

        /// <summary>
        /// A text box in the pixel face: the box is exactly the space the string may occupy, the scale is an integer
        /// multiple applied as a transform (the glyph grid stays whole-pixel), the first line is nudged into the box
        /// and the text truncates vertically instead of running into whatever is drawn beside it.
        /// </summary>
        public static Text Label(Transform parent, string text, UiRect bounds, int scale = 1,
            TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null, bool wrap = false, string name = "Label")
        {
            var s = Mathf.Max(1, scale);
            var rect = NewRect(parent, name, bounds);
            rect.localScale = new Vector3(s, s, 1f);
            rect.sizeDelta = new Vector2(Mathf.Max(0, bounds.Width) / (float)s, Mathf.Max(0, bounds.Height) / (float)s);
            rect.anchoredPosition = new Vector2(bounds.X, -(bounds.Y + UiFont.TopOffset * s));

            var label = rect.gameObject.AddComponent<Text>();
            label.font = UiFont.Font();
            label.fontSize = UiText.GlyphHeight;
            label.lineSpacing = 1f; // the font asset carries the authored line height: exact 9 px line boxes
            label.color = color ?? UiTheme.Ink;
            label.alignment = anchor;
            label.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            label.text = text ?? string.Empty;
            return label;
        }

        /// <summary>The eight 3 px corner strokes that mark focus by shape rather than by colour (spec 18.4).</summary>
        public static IReadOnlyList<Image> Brackets(Transform parent, UiRect bounds, Color color)
        {
            const int len = 3;
            var right = bounds.Right - len;
            var bottom = bounds.Bottom - 1;
            return new[]
            {
                Plate(parent, new UiRect(bounds.X, bounds.Y, len, 1), color, "BracketTL_H"),
                Plate(parent, new UiRect(bounds.X, bounds.Y, 1, len), color, "BracketTL_V"),
                Plate(parent, new UiRect(right, bounds.Y, len, 1), color, "BracketTR_H"),
                Plate(parent, new UiRect(bounds.Right - 1, bounds.Y, 1, len), color, "BracketTR_V"),
                Plate(parent, new UiRect(bounds.X, bottom, len, 1), color, "BracketBL_H"),
                Plate(parent, new UiRect(bounds.X, bounds.Bottom - len, 1, len), color, "BracketBL_V"),
                Plate(parent, new UiRect(right, bottom, len, 1), color, "BracketBR_H"),
                Plate(parent, new UiRect(bounds.Right - 1, bounds.Bottom - len, 1, len), color, "BracketBR_V")
            };
        }
    }
}
