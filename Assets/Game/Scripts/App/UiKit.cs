using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The code-built uGUI toolkit for the 640x360 reference screens.
    ///
    /// Everything is authored in reference pixels with the origin at the top-left, so a builder call reads the same
    /// way the <see cref="ScreenLayout"/> rectangles do and the same way a screenshot does. The canvas scaler turns
    /// those pixels into whatever the window actually is.
    ///
    /// The typeface is a bitmap font, so it renders at the size it was authored at and a heading is an integer scale
    /// of that face rather than a resampled one — which is why every text builder takes a <c>scale</c> rather than a
    /// point size. A non-integer size would resample a pixel face into mush at this resolution.
    /// </summary>
    public static class UiKit
    {
        public const int ReferenceWidth = UiTheme.ScreenWidth;
        public const int ReferenceHeight = UiTheme.ScreenHeight;

        public static Canvas Canvas(string name, int order = 0)
        {
            EnsureEventSystem();
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingLayerName = SortingLayers.ScreenUI;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            // Expand, not MatchWidthOrHeight. With a 0.5 match a 4:3 window produces a canvas only ~554 reference
            // pixels wide, so a layout authored across the full 640 — a tab bar reaching the right margin, a footer
            // spanning the screen — is silently clipped off the right edge. Expand keeps the canvas at least the
            // reference size on both axes, so the authored layout always fits and the surplus becomes margin.
            // At 16:9 the two modes are identical, so nothing changes for the resolution the game is authored for.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        /// <summary>
        /// A child rect of exactly the reference resolution, centred in the canvas.
        ///
        /// Screens build into this rather than into the canvas itself, so every coordinate in <see cref="ScreenLayout"/>
        /// means the same thing at any window size and any aspect ratio: the layout is a fixed 640x360 frame, and a
        /// window that is not 16:9 gets margin around it instead of a cropped or stretched interface.
        /// </summary>
        public static RectTransform ReferenceRoot(Transform parent)
        {
            var go = new GameObject("ReferenceRoot");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);
            return rect;
        }

        /// <summary>The event system the pointer needs. Created once per process and kept across scene loads.</summary>
        public const string EventSystemName = "RuinRailEventSystem";

        /// <summary>
        /// Guarantees the scene has an <see cref="EventSystem"/>.
        ///
        /// Without one, uGUI performs no raycasts at all: every menu was keyboard-and-controller-only regardless of
        /// what the controls themselves implemented. The project runs the Input System package exclusively
        /// (activeInputHandler = 1), so the module must be <see cref="InputSystemUIInputModule"/> — the legacy
        /// standalone module would throw on the first frame. Adding the module at runtime makes it assign the
        /// package's default UI actions itself, so no project input asset is involved and no gameplay binding is
        /// touched.
        /// </summary>
        public static EventSystem EnsureEventSystem()
        {
            if (EventSystem.current != null) return EventSystem.current;

            var existing = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
            if (existing != null)
            {
                EventSystem.current = existing;
                return existing;
            }

            var go = new GameObject(EventSystemName);
            var system = go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
            if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(go);
            EventSystem.current = system;
            return system;
        }

        /// <summary>Where the generated RUINRAIL pixel font is loaded from at runtime (see <see cref="UiFont"/>).</summary>
        public const string PixelFontResource = UiFont.PixelFontResource;

        /// <summary>The UI typeface: the project's own pixel font, shared with the in-run HUD through <see cref="UiFont"/>.</summary>
        public static Font Font() => UiFont.Font();

        /// <summary>True when the real pixel font is what the UI is drawing with.</summary>
        public static bool UsingPixelFont => UiFont.UsingPixelFont;

        /// <summary>First-line nudge so a label lands inside its box (see <see cref="UiFont.TopOffset"/>).</summary>
        public static int TopOffset => UiFont.TopOffset;

        // ---------------- primitives ----------------

        // The primitives live in the UI assembly (UiBuild) so the in-run inventory can draw with the same code; these forward.
        private static RectTransform NewRect(Transform parent, string name, UiRect bounds) => UiBuild.NewRect(parent, name, bounds);

        /// <summary>A flat colour plate. The workhorse of every panel, bar, edge and separator.</summary>
        public static Image Plate(Transform parent, UiRect bounds, Color color, string name = "Plate") => UiBuild.Plate(parent, bounds, color, name);

        /// <summary>A 1 px border drawn as four plates (exact pixels at any panel size; a stretched sprite border drifts).</summary>
        public static IReadOnlyList<Image> Border(Transform parent, UiRect bounds, Color color, int thickness = 1) => UiBuild.Border(parent, bounds, color, thickness);

        /// <summary>A content panel: plate plus border, on the 4 px grid.</summary>
        public static GameObject Panel(Transform parent, UiRect bounds, string name = "Panel", Color? fill = null, Color? edge = null) => UiBuild.Panel(parent, bounds, name, fill, edge);

        /// <summary>A horizontal rule between sections of a panel.</summary>
        public static Image Separator(Transform parent, UiRect bounds) =>
            Plate(parent, new UiRect(bounds.X, bounds.Y, bounds.Width, 1), UiTheme.PanelEdgeSoft, "Separator");

        /// <summary>
        /// A text box.
        ///
        /// The box is exactly the space the string is allowed to occupy: the layout already decided that, and the
        /// text is never allowed to grow past it, so a long string truncates or wraps instead of running through
        /// whatever is drawn beside it.
        /// </summary>
        public static Text Label(Transform parent, string text, UiRect bounds, int scale = 1,
            TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null, bool wrap = false) =>
            UiBuild.Label(parent, text, bounds, scale, anchor, color, wrap);

        /// <summary>Renders a measured text box exactly where the layout put it, with the string the layout approved.</summary>
        public static Text Label(Transform parent, MeasuredText measured, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null) =>
            Label(parent, measured.Text, measured.Bounds, measured.Scale, anchor, color);

        /// <summary>A "LABEL    value" row: the shape every stat, slot and status line in the shelter is built from.</summary>
        public static (Text Key, Text Value) StatRow(Transform parent, UiRect bounds, string key, string value,
            Color? keyColor = null, Color? valueColor = null)
        {
            // The key gets the larger share. Keys are names and labels — a survivor, an item, an attribute — while
            // values are short: a count, a status word, a coin figure. An even split truncated the interesting half.
            var half = Mathf.Max(UiText.Advance, Mathf.RoundToInt(bounds.Width * 0.56f));
            var k = Label(parent, UiText.Fit(key, half), new UiRect(bounds.X, bounds.Y, half, bounds.Height), 1,
                TextAnchor.UpperLeft, keyColor ?? UiTheme.InkMuted);
            var v = Label(parent, UiText.Fit(value, bounds.Width - half), new UiRect(bounds.X + half, bounds.Y, bounds.Width - half, bounds.Height), 1,
                TextAnchor.UpperRight, valueColor ?? UiTheme.Ink);
            return (k, v);
        }

        // ---------------- controls ----------------

        /// <summary>
        /// Builds one interactive control at exactly its layout rectangle.
        ///
        /// The click target is the visible plate itself, so there is no invisible margin around a control and no dead
        /// zone inside one. Keyboard/controller reach the same control through the focus list; both paths end in the
        /// action the <see cref="FocusItem"/> carries.
        /// </summary>
        public static UiControl Control(Transform parent, FocusList list, FocusItem item, UiRect bounds,
            ControlRole role, Action<FocusItem> onActivate, Func<bool> isActive = null,
            TextAnchor labelAnchor = TextAnchor.MiddleLeft, string labelOverride = null, int labelScale = 1)
        {
            var rect = NewRect(parent, "Control:" + item.Id, bounds);
            var fill = rect.gameObject.AddComponent<Image>();
            fill.raycastTarget = true;

            var inner = new UiRect(0, 0, bounds.Width, bounds.Height);
            var edges = Border(rect, inner, UiTheme.PanelEdgeSoft);

            // Selected marker: a 2 px notch on the leading edge. It is a shape, so it survives a colourblind read and
            // it stays visible when the pointer and the focus have both moved on.
            var marker = Plate(rect, new UiRect(0, 0, 2, bounds.Height), UiTheme.Amber, "SelectedMarker");
            marker.enabled = false;

            var brackets = Brackets(rect, inner);

            var textLeft = UiTheme.PadSmall + 2;
            var labelWidth = Mathf.Max(0, bounds.Width - textLeft * 2);
            var text = labelOverride ?? item.Label;
            var labelBounds = new UiRect(textLeft, (bounds.Height - UiText.Height(1, labelScale)) / 2, labelWidth, UiText.Height(1, labelScale));
            var label = Label(rect, UiText.Fit(text, labelWidth, labelScale), labelBounds, labelScale, labelAnchor);
            if (labelAnchor is TextAnchor.MiddleCenter or TextAnchor.UpperCenter or TextAnchor.LowerCenter)
                label.alignment = labelAnchor;

            var control = rect.gameObject.AddComponent<UiControl>();
            control.Bind(list, item, role, fill, label, edges, brackets, marker, isActive, onActivate, labelWidth, labelScale);
            return control;
        }

        /// <summary>The eight 3 px corner strokes that mark focus by shape rather than by colour (spec 18.4).</summary>
        private static IReadOnlyList<Image> Brackets(Transform parent, UiRect bounds) => UiBuild.Brackets(parent, bounds, UiTheme.Amber);

        // ---------------- chrome ----------------

        /// <summary>The full-screen backdrop that gives a menu its sense of place. Falls back to a flat ground.</summary>
        public static Image Backdrop(Transform parent, Sprite sprite)
        {
            var image = Plate(parent, ScreenLayout.Screen, sprite != null ? Color.white : UiTheme.Charcoal, "Backdrop");
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }

            return image;
        }

        /// <summary>A solid chrome bar: header, tab bar or footer.</summary>
        public static GameObject ChromeBar(Transform parent, UiRect bounds, string name, bool ruleAtBottom = true)
        {
            var root = NewRect(parent, name, bounds).gameObject;
            Plate(root.transform, new UiRect(0, 0, bounds.Width, bounds.Height), UiTheme.ChromeBar, "Fill");
            if (ruleAtBottom) Plate(root.transform, new UiRect(0, bounds.Height - 1, bounds.Width, 1), UiTheme.PanelEdge, "Rule");
            else Plate(root.transform, new UiRect(0, 0, bounds.Width, 1), UiTheme.PanelEdge, "Rule");
            return root;
        }
    }

    /// <summary>
    /// Menu navigation from the last-used device (116: menus support keyboard/mouse and controller): arrows/D-pad
    /// step the focus list, Enter/A activates, Esc/B goes back. Reads devices directly — gameplay bindings are never
    /// involved or altered.
    ///
    /// Horizontal and vertical steps are both offered because the front-end has both a horizontal tab bar and
    /// vertical lists; each screen decides which axis its focused panel consumes.
    /// </summary>
    public sealed class MenuInput : MonoBehaviour
    {
        public FocusStack Stack { get; } = new();
        public event Action Back;
        /// <summary>Raised on a horizontal step; screens that own a tab bar use it to change section.</summary>
        public event Action<int> Horizontal;
        public int Steps { get; private set; }

        /// <summary>Set while a scene transition owns the screen: the menu must not accept input in that window.</summary>
        public Func<bool> InputBlocked { get; set; }

        /// <summary>
        /// False when Escape belongs to another owner on the same screen (the in-run Pause action of the player input
        /// reader): keyboard Back is then not raised here, so one key press cannot both open and close the same menu.
        /// Controller B keeps raising Back.
        /// </summary>
        public bool KeyboardBackEnabled { get; set; } = true;

        public void Poll()
        {
            if (InputBlocked != null && InputBlocked()) return;

            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            var down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame));
            var up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame));
            var right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.right.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame));
            var left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.qKey.wasPressedThisFrame)) || (pad != null && (pad.dpad.left.wasPressedThisFrame || pad.leftShoulder.wasPressedThisFrame));
            var confirm = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            var back = (KeyboardBackEnabled && kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);

            if (pad != null && (down || up || left || right || confirm || back) && !(kb != null && kb.anyKey.wasPressedThisFrame)) ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            else if (kb != null && kb.anyKey.wasPressedThisFrame) ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            else if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.delta.ReadValue().sqrMagnitude > 1f)) ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);

            // A panel with its own 2D navigator (the inventory) consumes every direction itself; a plain list steps
            // vertically; left/right first go to an adjustable focused control (a settings slider / selector) and
            // otherwise to the screen (tab bars).
            var grid = Stack.CurrentHasNavigator;
            if (down) { if (grid) Stack.Navigate(Vector2Int.down); else Stack.Move(+1); Steps++; }
            if (up) { if (grid) Stack.Navigate(Vector2Int.up); else Stack.Move(-1); Steps++; }
            if (right) { if (grid) Stack.Navigate(Vector2Int.right); else if (!Stack.Adjust(+1)) Horizontal?.Invoke(+1); Steps++; }
            if (left) { if (grid) Stack.Navigate(Vector2Int.left); else if (!Stack.Adjust(-1)) Horizontal?.Invoke(-1); Steps++; }
            if (confirm) Stack.Activate();
            if (back) Back?.Invoke();
        }

        private void Update() => Poll();
    }
}
