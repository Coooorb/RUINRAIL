using System.Collections.Generic;
using RuinRail.UI.Navigation;
using RuinRail.UI.Pause;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The in-run pause screen (116 Pause) over <see cref="PauseMenuViewModel"/>: a dimmed backdrop, the title and
    /// four real controls — RESUME, SETTINGS, RETURN TO MAIN MENU, QUIT GAME — built with the same
    /// <see cref="UiKit"/> controls as the front-end, so hover, click, focus brackets and the pressed inset all work
    /// and every control is reachable from mouse, keyboard and controller through one <see cref="FocusStack"/>.
    ///
    /// The view model owns every state; this renders it. Root, Settings and the two confirmations are each one focus
    /// list pushed while that screen is open and removed when it closes, so there is never a stacked duplicate pause
    /// layer and Back always reaches the panel that is actually on top.
    /// </summary>
    public sealed class PauseMenuScreen : MonoBehaviour
    {
        public const int PanelWidth = 232;
        public const int ControlHeight = 24;
        public const int ControlGap = 6;

        private PauseMenuViewModel _pause;
        private MenuInput _input;
        private RectTransform _root;
        private GameObject _overlay;
        private Text _title;
        private FocusList _rootList;
        private FocusList _confirmList;
        private GameObject _confirmPanel;
        private GameObject _rootPanel;
        private Text _confirmTitle;
        private Text _confirmText;
        private SettingsPanel.Instance _settings;
        private readonly List<UiControl> _controls = new();

        public PauseMenuViewModel Pause => _pause;
        public FocusList RootList => _rootList;
        public FocusList ConfirmList => _confirmList;
        public FocusList SettingsList => _settings?.List;
        /// <summary>Every interactive control currently built, in build order (the interaction tests walk this).</summary>
        public IReadOnlyList<UiControl> Controls => _controls;
        public bool IsShowing => _overlay != null && _overlay.activeSelf;
        public bool SettingsShowing => _settings != null;
        public bool ConfirmationShowing => _confirmPanel != null && _confirmPanel.activeSelf;
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string ConfirmationText => _confirmText != null ? _confirmText.text : string.Empty;

        /// <summary>Builds the (hidden) pause layer inside an existing canvas and binds it to the menu input's focus stack.</summary>
        public static PauseMenuScreen Create(Transform canvas, MenuInput input, PauseMenuViewModel pause)
        {
            var go = new GameObject("PauseScreen");
            go.transform.SetParent(canvas, false);
            var screen = go.AddComponent<PauseMenuScreen>();
            screen.Build(input, pause);
            return screen;
        }

        private void Build(MenuInput input, PauseMenuViewModel pause)
        {
            _input = input;
            _pause = pause;
            _root = UiKit.ReferenceRoot(transform);

            _overlay = new GameObject("Overlay");
            _overlay.transform.SetParent(_root, false);
            var overlayRect = _overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = overlayRect.anchorMax = new Vector2(0f, 1f);
            overlayRect.pivot = new Vector2(0f, 1f);
            overlayRect.sizeDelta = new Vector2(UiKit.ReferenceWidth, UiKit.ReferenceHeight);
            overlayRect.anchoredPosition = Vector2.zero;
            var dim = UiKit.Plate(_overlay.transform, ScreenLayout.Screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "Dim");
            dim.raycastTarget = true; // swallows clicks on the world beneath while paused

            _rootList = ScreenNavigation.PauseMenu(_pause);
            var height = UiTheme.Pad + UiText.Height(1, 2) + 14 + PauseMenuViewModel.Items.Length * (ControlHeight + ControlGap) - ControlGap + UiTheme.Pad;
            var panel = new UiRect((UiKit.ReferenceWidth - PanelWidth) / 2, (UiKit.ReferenceHeight - height) / 2, PanelWidth, height);
            var panelGo = UiKit.Panel(_overlay.transform, panel, "PausePanel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.96f), UiTheme.PanelEdge);
            _rootPanel = panelGo;
            var innerWidth = PanelWidth - UiTheme.Pad * 2;
            _title = UiKit.Label(panelGo.transform, _pause.Title, new UiRect(UiTheme.Pad, UiTheme.Pad, innerWidth, UiText.Height(1, 2)), 2, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(panelGo.transform, new UiRect(UiTheme.Pad, UiTheme.Pad + UiText.Height(1, 2) + 5, innerWidth, 1), UiTheme.Amber, "TitleRule");

            var y = UiTheme.Pad + UiText.Height(1, 2) + 14;
            foreach (var item in _rootList.Items)
            {
                var primary = item.Id == "pause." + PauseMenuItem.Resume;
                var control = UiKit.Control(panelGo.transform, _rootList, item, new UiRect(UiTheme.Pad, y, innerWidth, ControlHeight),
                    primary ? ControlRole.Primary : ControlRole.Button,
                    i => { _rootList.Focus(i.Id); _rootList.ActivateFocused(); },
                    labelAnchor: TextAnchor.MiddleLeft);
                _controls.Add(control);
                y += ControlHeight + ControlGap;
            }

            BuildConfirmation();
            _overlay.SetActive(false);
            _pause.Changed += Render;
        }

        private void BuildConfirmation()
        {
            _confirmList = ScreenNavigation.PauseConfirmation(_pause);
            var bounds = new UiRect(150, 104, 340, 152);
            _confirmPanel = UiKit.Panel(_overlay.transform, bounds, "ConfirmPanel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.98f), UiTheme.Danger);
            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, bounds.Width - UiTheme.Pad * 2, bounds.Height - UiTheme.Pad * 2);
            _confirmTitle = UiKit.Label(_confirmPanel.transform, string.Empty, new UiRect(inner.X, inner.Y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Danger);
            UiKit.Plate(_confirmPanel.transform, new UiRect(inner.X, inner.Y + UiText.Height() + 3, inner.Width, 1), UiTheme.PanelEdge, "Rule");
            _confirmText = UiKit.Label(_confirmPanel.transform, string.Empty, new UiRect(inner.X, inner.Y + UiText.Height() + 8, inner.Width, UiText.Height(6)), 1, TextAnchor.UpperLeft, UiTheme.Ink, wrap: true);

            var buttonsY = inner.Bottom - ControlHeight;
            var buttonWidth = (inner.Width - UiTheme.Pad) / 2;
            var x = inner.X;
            foreach (var item in _confirmList.Items)
            {
                var confirm = item.Id == "pause.confirm.yes";
                var control = UiKit.Control(_confirmPanel.transform, _confirmList, item, new UiRect(x, buttonsY, buttonWidth, ControlHeight),
                    confirm ? ControlRole.Primary : ControlRole.Button,
                    i => { _confirmList.Focus(i.Id); _confirmList.ActivateFocused(); },
                    labelAnchor: TextAnchor.MiddleCenter);
                _controls.Add(control);
                x += buttonWidth + UiTheme.Pad;
            }

            _confirmPanel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_pause != null) _pause.Changed -= Render;
        }

        /// <summary>Renders the view model's screen: which panel is up and which focus list is on the stack.</summary>
        private void Render()
        {
            if (_overlay == null) return;
            var open = _pause.IsOpen;
            if (_overlay.activeSelf != open) _overlay.SetActive(open);
            _title.text = _pause.Title;

            if (!open)
            {
                CloseSettings();
                _input.Stack.Remove(_confirmList);
                _input.Stack.Remove(_rootList);
                return;
            }

            if (!_input.Stack.Contains(_rootList)) _input.Stack.Push(_rootList);

            var confirming = _pause.IsConfirming;
            _confirmPanel.SetActive(confirming);
            // One panel at a time: the root menu steps aside while a confirmation asks its question.
            _rootPanel.SetActive(!confirming);
            if (confirming)
            {
                _confirmTitle.text = _pause.ConfirmationTitle;
                _confirmText.text = _pause.ConfirmationText;
                if (!_input.Stack.Contains(_confirmList))
                {
                    _input.Stack.Push(_confirmList);
                    _confirmList.Focus("pause.confirm.cancel"); // the safe choice has the focus; CONFIRM is a deliberate step
                }
            }
            else
            {
                _input.Stack.Remove(_confirmList);
            }

            if (_pause.Screen == UI.Pause.PauseScreen.Settings)
            {
                if (_settings == null)
                {
                    _settings = SettingsPanel.Build(_overlay.transform, _pause.Settings);
                    _controls.AddRange(_settings.Controls);
                    _input.Stack.Push(_settings.List);
                }
            }
            else
            {
                CloseSettings();
            }
        }

        private void CloseSettings()
        {
            if (_settings == null) return;
            _input.Stack.Remove(_settings.List);
            foreach (var control in _settings.Controls) _controls.Remove(control);
            Destroy(_settings.Panel);
            _settings = null;
        }
    }
}
