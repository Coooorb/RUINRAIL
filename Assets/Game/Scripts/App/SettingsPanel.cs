using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.UI.Navigation;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The Settings panel over whatever screen opened it (Main Menu or the in-run pause menu) — one builder, so both
    /// places show the identical panel over the identical view model.
    ///
    /// It is category-based (ui/90): the root lists the real categories (VIDEO, AUDIO, CONTROLS, GAMEPLAY) plus RESET
    /// TO DEFAULTS and BACK; choosing one opens that category's page, where every row is an adjustable control that
    /// shows its current value — sliders for the volumes and shake intensity, selectors for display mode, resolution,
    /// frame-rate limit and the binding scheme, toggles for the on/off preferences, and the rebind rows. Enter / A /
    /// click activates a row; left / right (arrows, D-pad, stick) steps sliders and selectors; the pointer can also
    /// drag a slider. BACK on a page applies and returns to the categories; BACK on the categories closes Settings.
    ///
    /// Long pages (CONTROLS) scroll by focus, never by a scrollbar, so keyboard, controller and pointer reach every row.
    /// </summary>
    public static class SettingsPanel
    {
        public static readonly UiRect Bounds = new(160, 36, 320, 288);
        public const int CategoryHeight = 22;
        public const int CategoryGap = 4;
        public const int RowHeight = 16;
        public const int RowGap = 2;
        public const int ValueWidth = 118;

        public sealed class Instance
        {
            public GameObject Panel;
            /// <summary>The focus list that owns the input right now (the categories, or the open page).</summary>
            public FocusList List => Driver != null ? Driver.List : null;
            public readonly List<UiControl> Controls = new();
            public SettingsPanelDriver Driver;
            public SettingsViewModel Settings;
        }

        /// <summary>Builds the panel showing the view model's current page and pushes its focus list on the input stack.</summary>
        public static Instance Build(Transform root, SettingsViewModel settings, MenuInput input)
        {
            var instance = new Instance { Settings = settings };
            instance.Panel = UiKit.Panel(root, Bounds, "SettingsPanel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.96f), UiTheme.PanelEdge);
            instance.Driver = instance.Panel.AddComponent<SettingsPanelDriver>();
            instance.Driver.Bind(instance, settings, input);
            return instance;
        }

        /// <summary>Removes the panel and its focus list from the stack.</summary>
        public static void Close(Instance instance)
        {
            if (instance == null) return;
            instance.Driver?.Detach();
            if (instance.Panel != null) UnityEngine.Object.Destroy(instance.Panel);
            instance.Panel = null;
        }
    }

    /// <summary>
    /// One row of a settings page: the shared <see cref="UiControl"/> plate (focus brackets, hover, click) with the
    /// value on the right — text, and a fill bar for sliders that the pointer can drag.
    /// </summary>
    public sealed class SettingsRowView : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        private Text _value;
        private Image _barBack;
        private Image _barFill;
        private RectTransform _barRect;
        private FocusList _list;
        private Func<SettingsRow> _row;

        public UiControl Control { get; private set; }
        public string ValueText => _value != null ? _value.text : string.Empty;
        public float Fill => _barFill != null ? _barFill.fillAmount : 0f;
        public bool ShowsBar => _barRect != null && _barRect.gameObject.activeSelf;
        public int PointerFills { get; private set; }

        public static SettingsRowView Attach(UiControl control, FocusList list, Func<SettingsRow> row, int width, int height)
        {
            var view = control.gameObject.AddComponent<SettingsRowView>();
            view.Control = control;
            view._list = list;
            view._row = row;
            var valueX = width - SettingsPanel.ValueWidth - UiTheme.PadSmall;
            view._valueZoneX = valueX;
            var barBounds = new UiRect(valueX, (height - 6) / 2, SettingsPanel.ValueWidth - SliderTextWidth - 4, 6);
            view._barRect = UiBuild.NewRect(control.transform, "Bar", barBounds);
            view._barBack = view._barRect.gameObject.AddComponent<Image>();
            view._barBack.color = UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f);
            view._barBack.raycastTarget = false;
            UiBuild.Border(view._barRect, new UiRect(0, 0, barBounds.Width, barBounds.Height), UiTheme.PanelEdge);
            view._barFill = UiBuild.Fillable(view._barRect, new UiRect(0, 0, barBounds.Width, barBounds.Height), UiTheme.Amber, Image.FillMethod.Horizontal, 0, "Fill");
            view._value = UiBuild.Label(control.transform, string.Empty, new UiRect(valueX, (height - UiText.LineHeight) / 2, SettingsPanel.ValueWidth, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Amber, false, "Value");
            view.Refresh();
            return view;
        }

        /// <summary>Re-reads the row's value and fill (called every frame by the driver; values live in the view model).</summary>
        public void Refresh()
        {
            var row = _row?.Invoke();
            var slider = row != null && row.Kind == SettingsControlKind.Slider;
            if (_barRect != null) _barRect.gameObject.SetActive(slider); // the bar (frame included) exists only on a slider row
            if (row == null)
            {
                if (_value != null) _value.text = string.Empty;
                return;
            }

            if (slider) _barFill.fillAmount = Mathf.Clamp01(row.Fill?.Invoke() ?? 0f);
            var text = row.ValueText();
            _value.text = slider ? text : UiText.Fit(text, SettingsPanel.ValueWidth);
            _value.color = row.IsEnabled ? (row.Kind == SettingsControlKind.Action ? UiTheme.InkMuted : UiTheme.Amber) : UiTheme.InkDisabled;
            // A slider's percentage sits in the 36 px right of its bar, inside the row; every other value uses the whole value zone.
            var valueX = _valueZoneX + (slider ? SettingsPanel.ValueWidth - SliderTextWidth : 0);
            _value.rectTransform.anchoredPosition = new Vector2(valueX, _value.rectTransform.anchoredPosition.y);
            _value.rectTransform.sizeDelta = new Vector2(slider ? SliderTextWidth : SettingsPanel.ValueWidth, _value.rectTransform.sizeDelta.y);
        }

        private const int SliderTextWidth = 36;
        private int _valueZoneX;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            SetFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            SetFromPointer(eventData);
        }

        /// <summary>A press or drag along the slider bar sets the value by position (0..1); anywhere else on the row leaves the click to the control.</summary>
        private void SetFromPointer(PointerEventData eventData)
        {
            var row = _row?.Invoke();
            if (row == null || !row.AcceptsFill || row.SetFill == null || !row.IsEnabled || _barRect == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_barRect, eventData.position, eventData.pressEventCamera, out var local)) return;
            var width = _barRect.rect.width;
            if (width <= 0f) return;
            var t = Mathf.Clamp01((local.x - _barRect.rect.xMin) / width);
            if (local.y < _barRect.rect.yMin - 4f || local.y > _barRect.rect.yMax + 4f) return;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            _list?.Focus(row.Id);
            row.SetFill(Mathf.Round(t / SettingsViewModel.VolumeStep) * SettingsViewModel.VolumeStep);
            PointerFills++;
            Refresh();
        }

        /// <summary>Drives the pointer path without a device (tests): sets the slider at a fraction of its width.</summary>
        public void SimulateFill(float t)
        {
            var row = _row?.Invoke();
            if (row == null || !row.AcceptsFill || row.SetFill == null || !row.IsEnabled) return;
            _list?.Focus(row.Id);
            row.SetFill(Mathf.Round(Mathf.Clamp01(t) / SettingsViewModel.VolumeStep) * SettingsViewModel.VolumeStep);
            PointerFills++;
            Refresh();
        }
    }

    /// <summary>
    /// Owns the panel's content for as long as it exists: builds the category root or the open page, swaps the focus
    /// list on the input stack when the view model's page changes, refreshes every row's value and ticks the video
    /// KEEP/REVERT timer.
    /// </summary>
    public sealed class SettingsPanelDriver : MonoBehaviour
    {
        private SettingsPanel.Instance _instance;
        private SettingsViewModel _settings;
        private MenuInput _input;
        private GameObject _content;
        private SettingsTab? _builtPage;
        private bool _built;
        private Text _title;
        private Text _message;
        private FocusWindow _window;
        private readonly List<SettingsRowView> _rowViews = new();
        private readonly Dictionary<string, SettingsRow> _rows = new();

        public FocusList List { get; private set; }
        public SettingsTab? Page => _builtPage;
        public bool IsOnCategories => _built && !_builtPage.HasValue;
        public int Rebuilds { get; private set; }
        public IReadOnlyList<SettingsRowView> RowViews => _rowViews;
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string MessageText => _message != null ? _message.text : string.Empty;

        public void Bind(SettingsPanel.Instance instance, SettingsViewModel settings, MenuInput input)
        {
            _instance = instance;
            _settings = settings;
            _input = input;
            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, SettingsPanel.Bounds.Width - UiTheme.Pad * 2, SettingsPanel.Bounds.Height - UiTheme.Pad * 2);
            _title = UiKit.Label(transform, "SETTINGS", new UiRect(inner.X, inner.Y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(transform, new UiRect(inner.X, inner.Y + UiText.Height() + 3, inner.Width, 1), UiTheme.PanelEdge, "Rule");
            _message = UiKit.Label(transform, string.Empty, new UiRect(inner.X, inner.Bottom - UiText.Height(2), inner.Width, UiText.Height(2)), 1, TextAnchor.LowerLeft, UiTheme.InkMuted, wrap: true);
            _settings.Changed += OnSettingsChanged;
            Rebuild();
        }

        public void Detach()
        {
            if (_settings != null) _settings.Changed -= OnSettingsChanged;
            if (_input != null && List != null) _input.Stack.Remove(List);
            List = null;
        }

        private void OnDestroy() => Detach();

        private void OnSettingsChanged()
        {
            if (_settings == null) return;
            if (!_built || _builtPage != _settings.Page) Rebuild();
            else if (_builtPage == SettingsTab.Controls && _rows.Count != _settings.RowsFor(SettingsTab.Controls).Count + 1) Rebuild(); // scheme switched: different rows
            RefreshRows();
        }

        private void LateUpdate()
        {
            if (_settings == null) return;
            _settings.Tick(Time.unscaledDeltaTime);
            _window?.Refresh();
            RefreshRows();
        }

        private void RefreshRows()
        {
            foreach (var view in _rowViews) if (view != null) view.Refresh();
            if (_message != null) _message.text = _settings.Message;
        }

        /// <summary>Builds the content for the view model's page (or the categories) and puts its list on the input stack.</summary>
        public void Rebuild()
        {
            if (_input != null && List != null) _input.Stack.Remove(List);
            foreach (var control in _rowViews.Select(v => v.Control).Where(c => c != null)) _instance.Controls.Remove(control);
            foreach (var control in _instance.Controls.ToList()) if (control == null || (_content != null && control.transform.IsChildOf(_content.transform))) _instance.Controls.Remove(control);
            _rowViews.Clear();
            _rows.Clear();
            _window = null;
            if (_content != null) Destroy(_content);

            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, SettingsPanel.Bounds.Width - UiTheme.Pad * 2, SettingsPanel.Bounds.Height - UiTheme.Pad * 2);
            var top = inner.Y + UiText.Height() + 9;
            var area = new UiRect(inner.X, top, inner.Width, inner.Bottom - UiText.Height(2) - 4 - top);
            _content = UiBuild.NewRect(transform, "Content", area).gameObject;
            var local = new UiRect(0, 0, area.Width, area.Height);
            _builtPage = _settings.Page;
            _built = true;
            Rebuilds++;

            if (!_builtPage.HasValue)
            {
                _title.text = "SETTINGS";
                List = ScreenNavigation.Settings(_settings);
                BuildCategories(local);
            }
            else
            {
                _title.text = "SETTINGS  ·  " + SettingsViewModel.CategoryLabel(_builtPage.Value);
                List = ScreenNavigation.SettingsPage(_settings, _builtPage.Value);
                foreach (var row in _settings.RowsFor(_builtPage.Value)) _rows[row.Id] = row;
                BuildPage(local);
            }

            _input?.Stack.Push(List);
            RefreshRows();
        }

        private void BuildCategories(UiRect area)
        {
            var y = area.Y;
            var focusList = List;
            foreach (var item in focusList.Items)
            {
                var isCategory = item.Id.StartsWith("settings.category.");
                var height = isCategory ? SettingsPanel.CategoryHeight : SettingsPanel.RowHeight;
                var control = UiKit.Control(_content.transform, focusList, item, new UiRect(area.X, y, area.Width, height),
                    isCategory ? ControlRole.Button : ControlRole.Row, i => { focusList.Focus(i.Id); focusList.ActivateFocused(); }, labelAnchor: TextAnchor.MiddleLeft);
                _instance.Controls.Add(control);
                y += height;
                if (isCategory)
                {
                    var tab = (SettingsTab)Enum.Parse(typeof(SettingsTab), item.Id.Substring("settings.category.".Length));
                    UiBuild.Label(_content.transform, SettingsViewModel.CategoryHint(tab), new UiRect(area.X + UiTheme.PadSmall + 2, y + 2, area.Width - UiTheme.PadSmall * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Hint");
                    y += UiText.LineHeight + 2;
                }

                y += SettingsPanel.CategoryGap;
                if (item.Id == ScreenNavigation.SettingsDefaultsId) y += 2;
            }
        }

        private void BuildPage(UiRect area)
        {
            var focusList = List;
            var capacity = ScreenLayout.RowCapacity(area, SettingsPanel.RowHeight, SettingsPanel.RowGap);
            var rows = ScreenLayout.Rows(area, SettingsPanel.RowHeight, SettingsPanel.RowGap, capacity);
            _window = new FocusWindow(focusList, capacity);
            for (var slot = 0; slot < rows.Count && slot < focusList.Items.Count; slot++)
            {
                var control = UiKit.Control(_content.transform, focusList, focusList.Items[slot], rows[slot], ControlRole.Row,
                    item => { focusList.Focus(item.Id); focusList.ActivateFocused(); }, labelAnchor: TextAnchor.MiddleLeft);
                var c = control;
                var view = SettingsRowView.Attach(control, focusList, () => c.Item != null && _rows.TryGetValue(c.Item.Id, out var r) ? r : null, rows[slot].Width, rows[slot].Height);
                _window.Register(slot, control);
                _rowViews.Add(view);
                _instance.Controls.Add(control);
            }

            _window.Refresh();
        }
    }
}
