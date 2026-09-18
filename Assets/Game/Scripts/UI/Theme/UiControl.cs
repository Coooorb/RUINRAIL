using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.UI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// One interactive control of a menu screen: a tab, a button or a list row.
    ///
    /// The control is the single place where pointer and focus meet. Keyboard and controller move the screen's
    /// <see cref="FocusList"/>; the pointer hovers, presses and clicks this object. Both end in the same
    /// <see cref="FocusItem.TryActivate"/>, so a control can never do one thing on Enter and another on click, and no
    /// control is reachable by only one of the two.
    ///
    /// Why this replaces the uGUI <c>Button</c> the screens used before: <c>Button</c> carries its own selection and
    /// colour-tint state machine, which competes with the focus list for "what is selected" and offers no persistent
    /// selected state at all. Here the focus list stays the single source of truth for focus, the view model stays the
    /// source of truth for which section is active, and this component only renders those two facts.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiControl : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private FocusList _list;
        private FocusItem _item;
        private ControlRole _role;
        private Func<bool> _isActive;
        private Action<FocusItem> _onActivate;

        private Image _fill;
        private Text _label;
        private readonly List<Image> _edges = new(4);
        private readonly List<Image> _brackets = new(8);
        private Image _marker;
        private RectTransform _labelRect;
        private Vector2 _labelHome;

        private bool _hovered;
        private bool _pressed;

        /// <summary>The state the control is currently rendering. Tests assert on this rather than on pixel colours.</summary>
        public ControlState State { get; private set; } = ControlState.Normal;

        /// <summary>True while the persistent selected marker is shown, whatever else the control is doing.</summary>
        public bool ShowsSelectedMarker => _marker != null && _marker.enabled;

        /// <summary>True while the focus brackets are shown. This is the cue that must be visible without colour.</summary>
        public bool ShowsFocusBrackets => _brackets.Count > 0 && _brackets[0].enabled;

        public bool IsHovered => _hovered;
        public bool IsPressed => _pressed;
        public FocusItem Item => _item;
        public string Id => _item?.Id ?? string.Empty;
        public bool IsEnabled => _item?.IsEnabled ?? false;
        public bool IsFocused => _list != null && _item != null && ReferenceEquals(_list.Focused, _item);
        public bool IsActiveSection => _isActive != null && _isActive();

        /// <summary>How many times the pointer has activated this control; a duplicate-activation guard for the tests.</summary>
        public int PointerActivations { get; private set; }

        private int _labelWidth;
        private int _labelScale = 1;

        public void Bind(FocusList list, FocusItem item, ControlRole role, Image fill, Text label,
            IReadOnlyList<Image> edges, IReadOnlyList<Image> brackets, Image marker,
            Func<bool> isActive, Action<FocusItem> onActivate, int labelWidth = 0, int labelScale = 1)
        {
            _labelWidth = labelWidth;
            _labelScale = Math.Max(1, labelScale);
            _list = list;
            _item = item;
            _role = role;
            _fill = fill;
            _label = label;
            _labelRect = label != null ? label.rectTransform : null;
            _labelHome = _labelRect != null ? _labelRect.anchoredPosition : Vector2.zero;
            _edges.Clear();
            _edges.AddRange(edges);
            _brackets.Clear();
            _brackets.AddRange(brackets);
            _marker = marker;
            _isActive = isActive;
            _onActivate = onActivate;

            if (_list != null) _list.FocusChanged += OnFocusChanged;
            Refresh();
        }

        /// <summary>
        /// Points an existing control at a different entry of the same list.
        ///
        /// Used by <see cref="FocusWindow"/> so a long list is drawn through a fixed set of row objects. The label is
        /// re-fitted to the control's own width, so a rebind can never introduce text that runs past the plate.
        /// </summary>
        public void Rebind(FocusItem item)
        {
            if (ReferenceEquals(_item, item)) { Refresh(); return; }
            _item = item;
            _pressed = false;
            if (_label != null && _labelWidth > 0)
                _label.text = UiText.Fit(item?.Label ?? string.Empty, _labelWidth, _labelScale);
            Refresh();
        }

        private void OnDestroy()
        {
            if (_list != null) _list.FocusChanged -= OnFocusChanged;
        }

        private void OnFocusChanged(FocusItem _) => Refresh();

        /// <summary>
        /// Re-reads the control's inputs and repaints.
        ///
        /// Called on every focus change and once per frame, because two of the inputs — whether the item is still
        /// enabled and whether its section is still the active one — live in the view models and change without any
        /// event this control subscribes to.
        /// </summary>
        public void Refresh()
        {
            if (_item == null) return;

            var enabled = _item.IsEnabled;
            var active = IsActiveSection;
            var focused = IsFocused;

            State = !enabled ? ControlState.Disabled
                : _pressed ? ControlState.Pressed
                : focused ? ControlState.Focused
                : _hovered ? ControlState.Hover
                : active ? ControlState.Active
                : ControlState.Normal;

            var visual = UiTheme.Visual(_role, State);

            if (_fill != null) _fill.color = visual.Fill;
            foreach (var edge in _edges) edge.color = visual.Edge;
            foreach (var bracket in _brackets)
            {
                bracket.enabled = visual.ShowBrackets;
                bracket.color = visual.Marker;
            }

            if (_marker != null)
            {
                // The selected marker is owned by the section, not by the state: it must survive the pointer leaving
                // and the keyboard focus moving away, which is exactly the bug the tab bar had.
                _marker.enabled = enabled && (active || visual.ShowMarker);
                _marker.color = active ? visual.Marker : UiTheme.WithAlpha(visual.Marker, 0.65f);
            }

            if (_label != null)
            {
                _label.color = visual.Label;
                if (_labelRect != null)
                    _labelRect.anchoredPosition = _labelHome + new Vector2(visual.Inset, -visual.Inset);
            }
        }

        private void Update() => Refresh();

        // ---------------- pointer ----------------

        public void OnPointerEnter(PointerEventData eventData)
        {
            // Hover is feedback, not focus: the pointer passing over a control must not drag the keyboard cursor
            // away from wherever the player left it (A1: the two devices coexist).
            if (!IsEnabled) return;
            _hovered = true;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            CursorService.SetHover(true);
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_hovered) CursorService.SetHover(false);
            _hovered = false;
            _pressed = false;
            Refresh();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsEnabled || eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = true;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            Refresh();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _pressed = false;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            // A disabled control swallows the click rather than passing it through to whatever sits underneath.
            if (!IsEnabled) return;

            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            PointerActivations++;

            // Clicking takes focus as well as acting, so a following Arrow keypress continues from what was clicked.
            _list?.Focus(_item.Id);
            _onActivate?.Invoke(_item);
            Refresh();
        }

        /// <summary>Drives the same path a real click does, for tests that run without a pointer device.</summary>
        public void SimulateClick()
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            OnPointerClick(data);
        }

        /// <summary>Drives the hover path without a pointer device.</summary>
        public void SimulateHover(bool entering)
        {
            var data = new PointerEventData(EventSystem.current);
            if (entering) OnPointerEnter(data);
            else OnPointerExit(data);
        }

        /// <summary>Drives the press path without a pointer device.</summary>
        public void SimulatePress(bool down)
        {
            var data = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            if (down) OnPointerDown(data);
            else OnPointerUp(data);
        }
    }
}
