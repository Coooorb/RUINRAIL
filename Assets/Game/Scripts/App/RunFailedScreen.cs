using System.Collections.Generic;
using RuinRail.UI.Navigation;
using RuinRail.UI.RunEnd;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The Death / Run Lost screen over <see cref="RunFailedViewModel"/>, in the established RUINRAIL style: a full
    /// charcoal dim, one steel-edged panel with the restrained danger-red title rule, the run's figures as label/value
    /// rows in the pixel font, and two real controls — RETURN TO SHELTER (primary) and MAIN MENU — built with the same
    /// <see cref="UiKit"/> controls as the pause menu, so mouse, keyboard and controller all reach them through one
    /// <see cref="FocusList"/> on the run's menu input stack. The view model owns every state; this renders it.
    /// </summary>
    public sealed class RunFailedScreen : MonoBehaviour
    {
        public const int PanelWidth = 300;
        public const int RowHeight = 12;
        public const int ControlHeight = 24;
        public const int ControlGap = 6;

        private RunFailedViewModel _failed;
        private MenuInput _input;
        private GameObject _overlay;
        private GameObject _panel;
        private Text _title;
        private Text _subtitle;
        private FocusList _list;
        private readonly List<UiControl> _controls = new();
        private readonly List<GameObject> _rows = new();
        private int _rowsTop;
        private int _innerWidth;

        public RunFailedViewModel Failed => _failed;
        public FocusList List => _list;
        public IReadOnlyList<UiControl> Controls => _controls;
        public bool IsShowing => _overlay != null && _overlay.activeSelf;
        public string TitleText => _title != null ? _title.text : string.Empty;
        /// <summary>The label/value rows currently drawn, in order (tests read them).</summary>
        public IReadOnlyList<(string label, string value)> RowTexts
        {
            get
            {
                var result = new List<(string, string)>();
                foreach (var row in _rows)
                {
                    var texts = row.GetComponentsInChildren<Text>();
                    if (texts.Length >= 2) result.Add((texts[0].text, texts[1].text));
                }

                return result;
            }
        }

        public static RunFailedScreen Create(Transform canvas, MenuInput input, RunFailedViewModel failed)
        {
            var go = new GameObject("RunFailedScreen");
            go.transform.SetParent(canvas, false);
            var screen = go.AddComponent<RunFailedScreen>();
            screen.Build(input, failed);
            return screen;
        }

        private void Build(MenuInput input, RunFailedViewModel failed)
        {
            _input = input;
            _failed = failed;
            var root = UiKit.ReferenceRoot(transform);

            _overlay = new GameObject("Overlay");
            _overlay.transform.SetParent(root, false);
            var overlayRect = _overlay.AddComponent<RectTransform>();
            overlayRect.anchorMin = overlayRect.anchorMax = new Vector2(0f, 1f);
            overlayRect.pivot = new Vector2(0f, 1f);
            overlayRect.sizeDelta = new Vector2(UiKit.ReferenceWidth, UiKit.ReferenceHeight);
            overlayRect.anchoredPosition = Vector2.zero;
            var dim = UiKit.Plate(_overlay.transform, ScreenLayout.Screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.82f), "Dim");
            dim.raycastTarget = true; // swallows every click on the world beneath: nothing reaches the dead player's weapons

            _list = ScreenNavigation.RunFailed(_failed);
            var titleBlock = UiTheme.Pad + UiText.Height(1, 2) + 6 + UiText.Height(2) + 10;
            var height = titleBlock + RunFailedViewModel.MaxLines * RowHeight + 12 + ControlHeight + UiTheme.Pad;
            var panel = new UiRect((UiKit.ReferenceWidth - PanelWidth) / 2, (UiKit.ReferenceHeight - height) / 2, PanelWidth, height);
            _panel = UiKit.Panel(_overlay.transform, panel, "RunLostPanel", UiTheme.WithAlpha(UiTheme.Charcoal, 0.98f), UiTheme.PanelEdge);
            _innerWidth = PanelWidth - UiTheme.Pad * 2;
            _title = UiKit.Label(_panel.transform, _failed.Title, new UiRect(UiTheme.Pad, UiTheme.Pad, _innerWidth, UiText.Height(1, 2)), 2, TextAnchor.UpperLeft, UiTheme.Danger);
            UiKit.Plate(_panel.transform, new UiRect(UiTheme.Pad, UiTheme.Pad + UiText.Height(1, 2) + 3, _innerWidth, 1), UiTheme.Danger, "TitleRule");
            _subtitle = UiKit.Label(_panel.transform, string.Empty, new UiRect(UiTheme.Pad, UiTheme.Pad + UiText.Height(1, 2) + 6, _innerWidth, UiText.Height(2)), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);
            _rowsTop = titleBlock;

            var buttonsY = height - UiTheme.Pad - ControlHeight;
            var buttonWidth = (_innerWidth - ControlGap) / 2;
            var x = UiTheme.Pad;
            foreach (var item in _list.Items)
            {
                var primary = item.Id == "runfailed.shelter";
                var control = UiKit.Control(_panel.transform, _list, item, new UiRect(x, buttonsY, buttonWidth, ControlHeight),
                    primary ? ControlRole.Primary : ControlRole.Button,
                    i => { _list.Focus(i.Id); _list.ActivateFocused(); },
                    labelAnchor: TextAnchor.MiddleCenter);
                _controls.Add(control);
                x += buttonWidth + ControlGap;
            }

            _overlay.SetActive(false);
            _failed.Changed += Render;
        }

        private void OnDestroy()
        {
            if (_failed != null) _failed.Changed -= Render;
        }

        private void Render()
        {
            if (_overlay == null) return;
            var open = _failed.IsOpen && !_failed.IsResolved;
            if (_overlay.activeSelf != open) _overlay.SetActive(open);
            if (!open)
            {
                _input?.Stack.Remove(_list);
                return;
            }

            _title.text = _failed.Title;
            _subtitle.text = _failed.Subtitle;
            foreach (var row in _rows) Destroy(row);
            _rows.Clear();
            var y = _rowsTop;
            foreach (var line in _failed.Lines)
            {
                var row = new GameObject("Row:" + line.Label);
                row.transform.SetParent(_panel.transform, false);
                var rect = row.AddComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(_innerWidth, RowHeight);
                rect.anchoredPosition = new Vector2(UiTheme.Pad, -y);
                UiKit.StatRow(row.transform, new UiRect(0, 0, _innerWidth, RowHeight), line.Label, line.Value, UiTheme.InkMuted, line.Emphasis ? UiTheme.Danger : UiTheme.Ink);
                _rows.Add(row);
                y += RowHeight;
            }

            if (_input != null && !_input.Stack.Contains(_list))
            {
                _input.Stack.Push(_list);
                _list.Focus("runfailed.shelter");
            }
        }
    }
}
