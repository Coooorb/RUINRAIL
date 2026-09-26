using RuinRail.Core.Input;
using RuinRail.UI.Navigation;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The Shelter's display-name field: a small modal over the Character station, drawn with the shared kit.
    ///
    /// While it is open it owns the input — the Shelter's <see cref="MenuInput"/> is blocked through
    /// <see cref="OwnsInput"/>, because WASD, Q/E and Space are menu keys there and would otherwise navigate while the
    /// player types. A keyboard types into it (Enter saves, Esc cancels, Backspace deletes); a controller edits the
    /// last character with Up/Down, adds with Right, deletes with Left or X, saves with A and cancels with B; the
    /// mouse clicks SAVE / CANCEL. The key that opened or closed the field is never read twice in the same frame.
    /// </summary>
    public sealed class DisplayNameEntryView : MonoBehaviour
    {
        private const int PanelWidth = 232;
        private const int PanelHeight = 100;

        private DisplayNameEntry _entry;
        private GameObject _modal;
        private Text _field;
        private Text _error;
        private Text _hint;
        private FocusList _buttons;
        private readonly System.Text.StringBuilder _typed = new();
        private Keyboard _keyboard;
        private int _openedFrame = -1;
        private int _closedFrame = -1;

        /// <summary>True while the field is open, and for the frame it closed in (so its Enter/Esc is not re-read).</summary>
        public bool OwnsInput => _entry != null && (_entry.IsOpen || Time.frameCount == _closedFrame);

        public bool IsVisible => _modal != null && _modal.activeSelf;
        public string FieldText => _field != null ? _field.text : string.Empty;
        public string ErrorText => _error != null ? _error.text : string.Empty;
        public FocusList Buttons => _buttons;

        public void Bind(DisplayNameEntry entry, Transform root)
        {
            _entry = entry;
            _entry.Changed += OnChanged;
            Build(root);
            _modal.SetActive(false);
        }

        private void Build(Transform root)
        {
            var screen = ScreenLayout.Screen;
            _modal = new GameObject("DisplayNameEntry");
            _modal.transform.SetParent(root, false);
            var rect = _modal.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(screen.Width, screen.Height);

            // The dimmer catches every click, so nothing behind the field can be activated while it is open.
            var dim = UiKit.Plate(_modal.transform, screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "Dim");
            dim.raycastTarget = true;

            var bounds = new UiRect((screen.Width - PanelWidth) / 2, (screen.Height - PanelHeight) / 2, PanelWidth, PanelHeight);
            var panel = UiKit.Panel(_modal.transform, bounds, "NamePanel").transform;
            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, PanelWidth - UiTheme.Pad * 2, PanelHeight - UiTheme.Pad * 2);
            var y = inner.Y;

            UiKit.Label(panel, "DISPLAY NAME", new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(panel, new UiRect(inner.X, y + UiText.Height() + 1, inner.Width, 1), UiTheme.AmberDim, "HeadingRule");
            y += UiText.LineHeight + 5;

            // The field is sized for the longest name the policy allows plus the caret, so a maximum-length name fits.
            var fieldWidth = UiText.Width(new string('W', Mathf.Max(1, _entry.MaxLength) + 1)) + UiTheme.PadSmall * 2;
            var field = new UiRect(inner.X, y, Mathf.Min(inner.Width, fieldWidth), UiText.Height() + 6);
            UiKit.Plate(panel, field, UiTheme.NearBlack, "Field");
            UiKit.Border(panel, field, UiTheme.PanelEdge);
            _field = UiKit.Label(panel, string.Empty, new UiRect(field.X + UiTheme.PadSmall, field.Y + 3, field.Width - UiTheme.PadSmall * 2, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.Ink);
            y += field.Height + 3;

            _error = UiKit.Label(panel, string.Empty, new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Danger);
            y += UiText.LineHeight;
            _hint = UiKit.Label(panel, string.Empty, new UiRect(inner.X, y, inner.Width, UiText.Height(2)), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);

            _buttons = new FocusList("NameEntry", 2);
            _buttons.Add("name.save", "SAVE", () => _entry.Submit());
            _buttons.Add("name.cancel", "CANCEL", () => _entry.Cancel());
            const int buttonWidth = 64;
            const int buttonHeight = 14;
            var buttonY = inner.Bottom - buttonHeight;
            var bx = inner.Right - buttonWidth * 2 - 4;
            for (var i = 0; i < _buttons.Items.Count; i++)
            {
                UiKit.Control(panel, _buttons, _buttons.Items[i], new UiRect(bx + i * (buttonWidth + 4), buttonY, buttonWidth, buttonHeight),
                    i == 0 ? ControlRole.Primary : ControlRole.Button,
                    item => { _buttons.Focus(item.Id); _buttons.ActivateFocused(); }, labelAnchor: TextAnchor.MiddleCenter);
            }
        }

        /// <summary>Opens the field on the saved name; false (field stays closed) when the entry refused to open.</summary>
        public bool Open()
        {
            if (_entry == null || !_entry.Open()) return false;
            _openedFrame = Time.frameCount;
            _buttons.Focus("name.save");
            return true;
        }

        private void OnChanged()
        {
            if (_entry.IsOpen && !_modal.activeSelf)
            {
                // Station panels are built after the field, so it is brought to the front each time it opens.
                _modal.transform.SetAsLastSibling();
                _modal.SetActive(true);
                _typed.Clear();
                _keyboard = Keyboard.current;
                if (_keyboard != null) _keyboard.onTextInput += OnTextInput;
            }
            else if (!_entry.IsOpen && _modal.activeSelf)
            {
                _modal.SetActive(false);
                _closedFrame = Time.frameCount;
                Unhook();
            }

            Render();
        }

        private void OnTextInput(char c) => _typed.Append(c);

        private void Update()
        {
            if (_entry == null || !_entry.IsOpen) return;
            if (Time.frameCount != _openedFrame) Poll();
            else _typed.Clear();
            if (_entry.IsOpen) Render();
        }

        private void Poll()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            if (_typed.Length > 0)
            {
                _entry.Type(_typed.ToString());
                _typed.Clear();
                ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            }

            if (kb != null)
            {
                if (kb.backspaceKey.wasPressedThisFrame) _entry.Backspace();
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { _entry.Submit(); return; }
                if (kb.escapeKey.wasPressedThisFrame) { _entry.Cancel(); return; }
            }

            if (pad == null) return;
            var up = pad.dpad.up.wasPressedThisFrame || pad.leftStick.up.wasPressedThisFrame;
            var down = pad.dpad.down.wasPressedThisFrame || pad.leftStick.down.wasPressedThisFrame;
            var right = pad.dpad.right.wasPressedThisFrame || pad.leftStick.right.wasPressedThisFrame;
            var left = pad.dpad.left.wasPressedThisFrame || pad.leftStick.left.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame;
            if (up || down || right || left || pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame)
                ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            if (up) _entry.Cycle(+1);
            if (down) _entry.Cycle(-1);
            if (right) _entry.AddCharacter();
            if (left) _entry.Backspace();
            if (pad.buttonSouth.wasPressedThisFrame) { _entry.Submit(); return; }
            if (pad.buttonEast.wasPressedThisFrame) _entry.Cancel();
        }

        private void Render()
        {
            if (_field == null) return;
            var caret = _entry.IsOpen && _entry.Text.Length < _entry.MaxLength && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f ? "_" : string.Empty;
            _field.text = _entry.Text + caret;
            _error.text = _entry.Error;
            _hint.text = ActiveInputDevice.Current == InputDeviceKind.Gamepad
                ? "Up/Down letter  Right add  Left delete\nA save  B cancel"
                : $"{_entry.MinLength}-{_entry.MaxLength}: letters, numbers, space, _ -\nEnter save  Esc cancel";
        }

        private void Unhook()
        {
            if (_keyboard != null) _keyboard.onTextInput -= OnTextInput;
            _keyboard = null;
            _typed.Clear();
        }

        private void OnDestroy()
        {
            if (_entry != null) _entry.Changed -= OnChanged;
            Unhook();
        }
    }
}
