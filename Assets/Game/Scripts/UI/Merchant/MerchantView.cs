using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Merchant
{
    /// <summary>
    /// One list row of the merchant window: icon slot with rarity frame, name, rarity · category, price, and the
    /// SOLD / STARTER state. A <see cref="UiControl"/> makes it a focusable, clickable row.
    /// </summary>
    public sealed class MerchantRowView : MonoBehaviour
    {
        public const int Height = 32;
        public const int SlotSize = 28;
        public const int IconSize = 24;

        private Image _plate;
        private Image _rarity;
        private Image _icon;
        private Text _name;
        private Text _subtitle;
        private Text _price;
        private UiControl _control;

        public UiControl Control => _control;
        public string NameText => _name != null ? _name.text : string.Empty;
        public string SubtitleText => _subtitle != null ? _subtitle.text : string.Empty;
        public string PriceText => _price != null ? _price.text : string.Empty;
        public Sprite IconSprite => _icon != null && _icon.enabled ? _icon.sprite : null;
        public Sprite FrameSprite => _rarity != null && _rarity.enabled ? _rarity.sprite : null;
        public bool IconVisible => _icon != null && _icon.enabled;
        public MerchantRow Row { get; private set; }

        public static MerchantRowView Create(Transform parent, UiRect bounds, Sprite slotSprite, string name)
        {
            var rect = UiBuild.NewRect(parent, name, bounds);
            var view = rect.gameObject.AddComponent<MerchantRowView>();
            view.Build(bounds.Width, bounds.Height, slotSprite);
            return view;
        }

        private void Build(int width, int height, Sprite slotSprite)
        {
            var inner = new UiRect(0, 0, width, height);
            var fill = UiBuild.Plate(transform, inner, UiTheme.Plate, "Fill");
            fill.raycastTarget = true;
            var edges = UiBuild.Border(transform, inner, UiTheme.PanelEdgeSoft);
            var marker = UiBuild.Plate(transform, new UiRect(0, 0, 2, height), UiTheme.Amber, "SelectedMarker");
            marker.enabled = false;
            var brackets = UiBuild.Brackets(transform, inner, UiTheme.Amber);

            var slot = new UiRect(4, (height - SlotSize) / 2, SlotSize, SlotSize);
            _plate = UiBuild.Sliced(transform, slot, slotSprite, null, "Slot");
            _rarity = UiBuild.Sliced(transform, slot, null, Color.white, "RarityFrame");
            _rarity.enabled = false;
            var iconRect = UiBuild.NewRect(transform, "Icon", new UiRect(slot.X + (SlotSize - IconSize) / 2, slot.Y + (SlotSize - IconSize) / 2, IconSize, IconSize));
            _icon = iconRect.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.enabled = false;

            var textX = slot.Right + 6;
            var priceWidth = 64;
            var textWidth = width - textX - priceWidth - 6;
            _name = UiBuild.Label(transform, string.Empty, new UiRect(textX, 6, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Label");
            _subtitle = UiBuild.Label(transform, string.Empty, new UiRect(textX, 6 + DungeonHudLine.Pitch, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Subtitle");
            _price = UiBuild.Label(transform, string.Empty, new UiRect(width - priceWidth - 6, 6 + DungeonHudLine.Pitch / 2, priceWidth, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Amber, false, "Price");

            _control = gameObject.AddComponent<UiControl>();
            _control.Bind(null, null, ControlRole.Row, fill, _name, edges, brackets, marker, null, null, textWidth, 1);
        }

        public void Bind(FocusList list, FocusItem item, Action<FocusItem> onActivate)
        {
            var images = GetComponentsInChildren<Image>(true);
            _control.Bind(list, item, ControlRole.Row, images.First(i => i.name == "Fill"), _name,
                images.Where(i => i.name.StartsWith("Edge", StringComparison.Ordinal)).ToList(),
                images.Where(i => i.name.StartsWith("Bracket", StringComparison.Ordinal)).ToList(),
                images.First(i => i.name == "SelectedMarker"), null, onActivate, ((RectTransform)_name.transform).sizeDelta.x > 0 ? Mathf.RoundToInt(((RectTransform)_name.transform).sizeDelta.x) : 0, 1);
        }

        public void Show(MerchantRow row, Func<int, Sprite> rarityFrame, int width)
        {
            Row = row;
            gameObject.SetActive(row != null);
            if (row == null) return;
            var frame = rarityFrame?.Invoke(row.Rarity);
            _rarity.enabled = frame != null;
            _rarity.sprite = frame;
            _icon.enabled = row.Icon != null;
            _icon.sprite = row.Icon;
            var style = RarityStyle.For(row.Item.Rarity);
            var textWidth = Mathf.RoundToInt(((RectTransform)_name.transform).sizeDelta.x);
            _name.text = UiText.Fit(row.Name, textWidth);
            var subtitle = style.Label + " · " + (row.Definition != null ? row.Definition.Category.ToString().ToUpperInvariant() : "ITEM");
            if (row.Item.Quantity > 1) subtitle += $" · x{row.Item.Quantity}";
            if (row.IsSold) subtitle = "SOLD";
            else if (row.IsUnsellable) subtitle += " · STARTER";
            _subtitle.text = UiText.Fit(subtitle, textWidth);
            _subtitle.color = row.IsSold ? UiTheme.InkFaint : UiTheme.InkMuted;
            _price.text = row.IsSold ? "—" : $"{row.Price} C";
            _price.color = row.IsSold ? UiTheme.InkFaint : row.Tab == MerchantTab.Sell && row.IsUnsellable ? UiTheme.InkFaint : UiTheme.Amber;
            _icon.color = row.IsSold ? new Color(1f, 1f, 1f, 0.35f) : Color.white;
            _plate.color = _plate.sprite != null ? (row.IsSold ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white) : UiTheme.Charcoal;
            _control.Refresh();
        }
    }

    /// <summary>Line pitch shared with the HUD so stacked rows never share pixels.</summary>
    internal static class DungeonHudLine
    {
        public const int Pitch = UiText.LineHeight + 1;
    }

    /// <summary>
    /// The graphical merchant window (dungeon/58) in the inventory's visual language (spec 18): a titled window over
    /// a dim, a BUY / SELL tab pair over a list of icon rows, a details panel reusing the item tooltip and comparison,
    /// and the BUY/SELL + CLOSE actions. Mouse, keyboard and controller share one focus list; the window never
    /// touches gameplay except through the view model.
    /// </summary>
    public sealed class MerchantView : MonoBehaviour
    {
        public const int ReferenceWidth = UiTheme.ScreenWidth;
        public const int ReferenceHeight = UiTheme.ScreenHeight;
        public static readonly UiRect Window = new(20, 12, 600, 336);
        public const int TitleHeight = 26;
        public static readonly UiRect ListPanel = new(28, 44, 296, 296);
        public static readonly UiRect DetailsPanel = new(332, 44, 272, 232);
        public static readonly UiRect ActionPanel = new(332, 284, 272, 56);
        public const int MaxRows = 8;
        public const int DetailLines = 12;
        public const string BuyTabId = "merchant.tab.buy";
        public const string SellTabId = "merchant.tab.sell";
        public const string ActionFocusId = "merchant.action";
        public const string CloseFocusId = "merchant.close";

        private MerchantViewModel _viewModel;
        private Canvas _canvas;
        private RectTransform _root;
        private GameObject _panel;
        private FocusList _list;
        private UiSkin _skin;
        private readonly List<MerchantRowView> _rows = new();
        private readonly List<Text> _detailRows = new();
        private readonly Dictionary<string, UiControl> _buttons = new();
        private Text _title;
        private Text _coins;
        private Text _hints;
        private Text _listHeader;
        private Text _emptyList;
        private Text _detailTitle;
        private Text _detailSubtitle;
        private Text _message;
        private Text _backpackLine;
        private bool _syncingFocus;
        private string _lastRowFocusId;
        private int _opensRendered;

        public MerchantViewModel ViewModel => _viewModel;
        public FocusList FocusList => _list;
        public bool IsVisible => _panel != null && _panel.activeSelf;
        public IReadOnlyList<MerchantRowView> RowViews => _rows;
        public IReadOnlyDictionary<string, UiControl> Buttons => _buttons;
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string CoinsText => _coins != null ? _coins.text : string.Empty;
        public string BackpackText => _backpackLine != null ? _backpackLine.text : string.Empty;
        public string MessageText => _message != null ? _message.text : string.Empty;
        public string DetailTitleText => _detailTitle != null ? _detailTitle.text : string.Empty;
        public string DetailSubtitleText => _detailSubtitle != null ? _detailSubtitle.text : string.Empty;
        public IReadOnlyList<string> DetailRowTexts => _detailRows.Select(r => r.text).ToList();
        public string HintsText => _hints != null ? _hints.text : string.Empty;
        public int Renders { get; private set; }

        public static MerchantView Create(MerchantViewModel viewModel, string name = "MerchantUI")
        {
            var go = new GameObject(name);
            var view = go.AddComponent<MerchantView>();
            view.Build();
            view.Bind(viewModel);
            return view;
        }

        public void Bind(MerchantViewModel viewModel)
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
            _viewModel = viewModel;
            _list = BuildFocusList();
            if (_viewModel != null)
            {
                _viewModel.Changed += Render;
                Render();
            }
        }

        private void OnDestroy()
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
        }

        // ---- construction ----

        private void Build()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.pixelPerfect = true;
            _canvas.sortingLayerName = RuinRail.Core.Rendering.SortingLayers.ScreenUI;
            _canvas.sortingOrder = 30; // same layer as the inventory window: above the run HUD and prompts
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("ReferenceRoot", typeof(RectTransform));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);

            _skin = UiSkin.Load();
            var dim = UiBuild.Plate(_root, ScreenLayout.Screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.62f), "Dim");
            dim.raycastTarget = true;
            _panel = dim.gameObject;

            var window = UiBuild.Panel(_panel.transform, Window, "Panel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.97f), UiTheme.PanelEdge);
            UiBuild.Sliced(window.transform, new UiRect(0, 0, Window.Width, Window.Height), _skin != null ? _skin.PanelFrame : null, _skin != null && _skin.PanelFrame != null ? Color.white : new Color(0f, 0f, 0f, 0f), "Frame");
            BuildTitle(window.transform);
            BuildList(window.transform);
            BuildDetails(window.transform);
            BuildActions(window.transform);
            _panel.SetActive(false);
        }

        private static UiRect Local(UiRect panel) => new(panel.X - Window.X, panel.Y - Window.Y, panel.Width, panel.Height);

        private void BuildTitle(Transform window)
        {
            var bar = new UiRect(0, 0, Window.Width, TitleHeight);
            UiBuild.Plate(window, bar, UiTheme.ChromeBar, "TitleBar");
            UiBuild.Plate(window, new UiRect(0, TitleHeight - 1, Window.Width, 1), UiTheme.PanelEdge, "TitleRule");
            _title = UiBuild.Label(window, "MERCHANT", new UiRect(UiTheme.PadLarge, 4, 232, UiText.LineHeight * 2), 2, TextAnchor.UpperLeft, UiTheme.Amber, false, "Title");
            _hints = UiBuild.Label(window, string.Empty, new UiRect(250, 9, 200, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "Hints");
            _coins = UiBuild.Label(window, string.Empty, new UiRect(Window.Width - UiTheme.PadLarge - 150, 9, 150, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Amber, false, "Coins");
        }

        private GameObject SectionPanel(Transform window, UiRect bounds, string name, string header, out Text headerLabel)
        {
            var local = Local(bounds);
            var panel = UiBuild.Panel(window, local, name, UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft);
            headerLabel = UiBuild.Label(panel.transform, header, new UiRect(UiTheme.Pad, 5, bounds.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, name + "Header");
            UiBuild.Plate(panel.transform, new UiRect(UiTheme.Pad, 17, bounds.Width - UiTheme.Pad * 2, 1), UiTheme.PanelEdge, "HeaderRule");
            return panel;
        }

        private void BuildList(Transform window)
        {
            var panel = SectionPanel(window, ListPanel, "List", "STOCK", out _listHeader);
            // Tab pair on the header line, right-aligned.
            var tabWidth = 56;
            _buttons[BuyTabId] = BuildButton(panel.transform, new UiRect(ListPanel.Width - UiTheme.Pad - tabWidth * 2 - 4, 2, tabWidth, 14), BuyTabId, "BUY", ControlRole.Tab);
            _buttons[SellTabId] = BuildButton(panel.transform, new UiRect(ListPanel.Width - UiTheme.Pad - tabWidth, 2, tabWidth, 14), SellTabId, "SELL", ControlRole.Tab);

            var rowWidth = ListPanel.Width - UiTheme.Pad * 2;
            for (var i = 0; i < MaxRows; i++)
            {
                var row = MerchantRowView.Create(panel.transform, new UiRect(UiTheme.Pad, 22 + i * (MerchantRowView.Height + 2), rowWidth, MerchantRowView.Height), _skin != null ? _skin.InventorySlot : null, "Row" + i);
                row.gameObject.SetActive(false);
                _rows.Add(row);
            }

            _emptyList = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 30, rowWidth, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "EmptyList");
        }

        private void BuildDetails(Transform window)
        {
            var panel = SectionPanel(window, DetailsPanel, "Details", "DETAILS", out _);
            var inner = DetailsPanel.Width - UiTheme.Pad * 2;
            _detailTitle = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 22, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "DetailTitle");
            _detailSubtitle = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 32, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "DetailSubtitle");
            UiBuild.Plate(panel.transform, new UiRect(UiTheme.Pad, 43, inner, 1), UiTheme.PanelEdgeSoft, "DetailRule");
            for (var i = 0; i < DetailLines; i++)
                _detailRows.Add(UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 47 + i * DungeonHudLine.Pitch, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Detail" + i));
            _message = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, DetailsPanel.Height - UiTheme.Pad - UiText.LineHeight, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Danger, false, "Message");
        }

        private void BuildActions(Transform window)
        {
            var local = Local(ActionPanel);
            var panel = UiBuild.Panel(window, local, "Actions", UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft);
            _backpackLine = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 5, ActionPanel.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Backpack");
            var buttonWidth = (ActionPanel.Width - UiTheme.Pad * 2 - 8) / 2;
            _buttons[ActionFocusId] = BuildButton(panel.transform, new UiRect(UiTheme.Pad, 22, buttonWidth, 24), ActionFocusId, "BUY", ControlRole.Primary);
            _buttons[CloseFocusId] = BuildButton(panel.transform, new UiRect(UiTheme.Pad + buttonWidth + 8, 22, buttonWidth, 24), CloseFocusId, "CLOSE", ControlRole.Exit);
        }

        private UiControl BuildButton(Transform parent, UiRect bounds, string id, string label, ControlRole role)
        {
            var rect = UiBuild.NewRect(parent, "Control:" + id, bounds);
            var fill = rect.gameObject.AddComponent<Image>();
            fill.raycastTarget = true;
            var inner = new UiRect(0, 0, bounds.Width, bounds.Height);
            var edges = UiBuild.Border(rect, inner, UiTheme.PanelEdgeSoft);
            var marker = UiBuild.Plate(rect, new UiRect(0, 0, 2, bounds.Height), UiTheme.Amber, "SelectedMarker");
            marker.enabled = false;
            var brackets = UiBuild.Brackets(rect, inner, UiTheme.Amber);
            var text = UiBuild.Label(rect, label, new UiRect(0, (bounds.Height - UiText.LineHeight) / 2, bounds.Width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Ink, false, "Label");
            var control = rect.gameObject.AddComponent<UiControl>();
            control.Bind(null, null, role, fill, text, edges, brackets, marker, null, null, bounds.Width, 1);
            return control;
        }

        // ---- focus list ----

        private FocusList BuildFocusList()
        {
            var list = new FocusList("Merchant");
            if (_viewModel == null) return list;
            list.Add(BuyTabId, "BUY", () => _viewModel.SetTab(MerchantTab.Buy));
            list.Add(SellTabId, "SELL", () => _viewModel.SetTab(MerchantTab.Sell));
            for (var i = 0; i < MaxRows; i++)
            {
                var index = i;
                list.Add("merchant.row." + i, $"Row {i + 1}", () => { _viewModel.SetCursor(index); _viewModel.Act(); }, () => index < _viewModel.Rows.Count);
            }

            list.Add(ActionFocusId, "TRADE", () => _viewModel.Act(), () => _viewModel.CanAct);
            list.Add(CloseFocusId, "CLOSE", () => _viewModel.Close());
            list.Navigator = Navigate;
            list.FocusChanged += OnFocusChanged;

            foreach (var (id, control) in _buttons)
            {
                var item = list.Find(id);
                var role = id == ActionFocusId ? ControlRole.Primary : id == CloseFocusId ? ControlRole.Exit : ControlRole.Tab;
                var isActive = id == BuyTabId ? new Func<bool>(() => _viewModel.Tab == MerchantTab.Buy) : id == SellTabId ? () => _viewModel.Tab == MerchantTab.Sell : null;
                control.Bind(list, item, role, control.GetComponent<Image>(), control.GetComponentInChildren<Text>(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Edge", StringComparison.Ordinal)).ToList(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Bracket", StringComparison.Ordinal)).ToList(),
                    control.GetComponentsInChildren<Image>().First(i => i.name == "SelectedMarker"),
                    isActive, i => { list.Focus(i.Id); list.ActivateFocused(); }, Mathf.RoundToInt(((RectTransform)control.transform).sizeDelta.x), 1);
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var index = i;
                // A click selects the row (the details follow); ENTER / A on a focused row or the BUY/SELL button trades it.
                row.Bind(list, list.Find("merchant.row." + i), _ => _viewModel.SetCursor(index));
            }

            return list;
        }

        /// <summary>Tabs on top (left/right switches), rows beneath (up/down), then the action and close buttons.</summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null || _viewModel == null) return null;
            var rowCount = Mathf.Min(_viewModel.Rows.Count, MaxRows);
            string FirstRowOrAction() => rowCount > 0 ? "merchant.row.0" : FirstAction();
            string LastRowOrTab() => rowCount > 0 ? "merchant.row." + (rowCount - 1) : BuyTabId;

            if (focused.Id == BuyTabId || focused.Id == SellTabId)
            {
                if (direction.x > 0) return SellTabId;
                if (direction.x < 0) return BuyTabId;
                if (direction.y < 0) return FirstRowOrAction();
                return null;
            }

            if (focused.Id.StartsWith("merchant.row.", StringComparison.Ordinal) && int.TryParse(focused.Id.Substring("merchant.row.".Length), out var index))
            {
                if (direction.y < 0) return index < rowCount - 1 ? "merchant.row." + (index + 1) : FirstAction();
                if (direction.y > 0) return index > 0 ? "merchant.row." + (index - 1) : (_viewModel.Tab == MerchantTab.Buy ? BuyTabId : SellTabId);
                if (direction.x > 0) return FirstAction();
                return null;
            }

            var actions = EnabledActions();
            var at = actions.IndexOf(focused.Id);
            if (at < 0) return null;
            if (direction.y > 0) return at == 0 ? LastRowOrTab() : actions[at - 1];
            if (direction.y < 0) return at < actions.Count - 1 ? actions[at + 1] : null;
            if (direction.x > 0) return at < actions.Count - 1 ? actions[at + 1] : null;
            if (direction.x < 0) return at > 0 ? actions[at - 1] : LastRowOrTab();
            return null;
        }

        private string FirstAction() => EnabledActions().First();
        private List<string> EnabledActions() => new[] { ActionFocusId, CloseFocusId }.Where(id => _list?.Find(id)?.IsEnabled ?? false).DefaultIfEmpty(CloseFocusId).ToList();

        private void OnFocusChanged(FocusItem focused)
        {
            if (_syncingFocus || focused == null || _viewModel == null) return;
            if (!focused.Id.StartsWith("merchant.row.", StringComparison.Ordinal)) return;
            _lastRowFocusId = focused.Id;
            if (int.TryParse(focused.Id.Substring("merchant.row.".Length), out var index) && _viewModel.Cursor != index) _viewModel.SetCursor(index);
        }

        private void SyncFocusToCursor()
        {
            if (_list == null || _viewModel.Rows.Count == 0) return;
            var id = "merchant.row." + _viewModel.Cursor;
            if (_list.Focused != null && _list.Focused.Id == id) return;
            var focusedElsewhere = _list.Focused != null && !_list.Focused.Id.StartsWith("merchant.row.", StringComparison.Ordinal);
            if (focusedElsewhere) return; // tabs and buttons keep their focus; they act on the cursor row
            _syncingFocus = true;
            _list.Focus(id);
            _lastRowFocusId = id;
            _syncingFocus = false;
        }

        // ---- rendering ----

        private void Render()
        {
            Renders++;
            if (_viewModel == null || _panel == null) return;
            if (_panel.activeSelf != _viewModel.IsOpen) _panel.SetActive(_viewModel.IsOpen);
            if (!_viewModel.IsOpen) return;
            if (_opensRendered != _viewModel.Opens)
            {
                // A fresh open starts on the first row (or the BUY tab when the stock is empty), whatever was focused last time.
                _opensRendered = _viewModel.Opens;
                _syncingFocus = true;
                if (!_list.Focus("merchant.row.0")) _list.Focus(BuyTabId);
                _syncingFocus = false;
            }

            _title.text = $"MERCHANT — DEPTH {_viewModel.Depth}";
            _coins.text = $"CARRIED COINS  {_viewModel.Coins}";
            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            _hints.text = gamepad ? "A: TRADE   B: BACK   D-PAD: MOVE" : "ENTER: TRADE   ESC: CLOSE";
            _listHeader.text = _viewModel.Tab == MerchantTab.Buy ? "STOCK" : "YOUR BACKPACK";
            _backpackLine.text = $"BACKPACK  {_viewModel.FreeSlots} / {_viewModel.BackpackCapacity} SLOTS FREE";

            var rowWidth = ListPanel.Width - UiTheme.Pad * 2;
            Func<int, Sprite> rarityFrame = _skin != null ? _skin.RarityFrame : null;
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].Show(i < _viewModel.Rows.Count ? _viewModel.Rows[i] : null, rarityFrame, rowWidth);
            _emptyList.text = _viewModel.Rows.Count == 0 ? (_viewModel.Tab == MerchantTab.Buy ? "NOTHING IN STOCK" : "NOTHING TO SELL") : string.Empty;

            var actionLabel = _buttons[ActionFocusId].GetComponentInChildren<Text>();
            if (actionLabel != null) actionLabel.text = _viewModel.ActionLabel;

            SyncFocusToCursor();
            RenderDetails();
        }

        private void RenderDetails()
        {
            var row = _viewModel.Selected;
            var tooltip = _viewModel.TooltipFor(row);
            foreach (var r in _detailRows) r.text = string.Empty;
            _message.text = !string.IsNullOrEmpty(_viewModel.Message) ? _viewModel.Message : _viewModel.BlockReason;
            _message.color = _viewModel.MessageIsError || string.IsNullOrEmpty(_viewModel.Message) ? UiTheme.Danger : UiTheme.Terminal;
            var width = DetailsPanel.Width - UiTheme.Pad * 2;
            if (tooltip == null)
            {
                _detailTitle.text = _viewModel.Tab == MerchantTab.Buy ? "NO OFFER SELECTED" : "NOTHING TO SELL";
                _detailTitle.color = UiTheme.InkMuted;
                _detailSubtitle.text = _viewModel.Tab == MerchantTab.Buy ? "Select an offer to see its details." : "Dungeon-held backpack items can be sold here.";
                return;
            }

            var style = RarityStyle.For(tooltip.Rarity);
            _detailTitle.text = UiText.Fit(tooltip.Name, width);
            _detailTitle.color = Readable(style.Color);
            var subtitle = style.Label + " · " + tooltip.CategoryText;
            if (tooltip.Quantity.HasValue && !(row.Tab == MerchantTab.Buy && row.IsSold)) subtitle += $" · x{tooltip.Quantity.Value}"; // a sold stack was absorbed into the backpack (its own count is 0)
            subtitle += row.Tab == MerchantTab.Buy ? (row.IsSold ? " · SOLD" : $" · PRICE {row.Price} C") : (row.IsUnsellable ? " · STARTER" : $" · SELLS FOR {row.Price} C");
            _detailSubtitle.text = UiText.Fit(subtitle, width);

            var lines = new List<(string key, string value, Color color)>();
            foreach (var stat in tooltip.BaseStats) lines.Add((stat.Label, stat.Value, UiTheme.Ink));
            foreach (var affix in tooltip.Affixes) lines.Add((affix.Label, affix.Value, UiTheme.Terminal));
            if (!string.IsNullOrEmpty(tooltip.LegendaryText)) lines.Add((tooltip.LegendaryText, string.Empty, UiTheme.Amber));
            var comparison = _viewModel.CompareFor(row);
            if (comparison.Count > 0)
            {
                lines.Add(("— VS EQUIPPED —", string.Empty, UiTheme.InkMuted));
                foreach (var line in comparison) lines.Add((line.Label, $"{line.Candidate} vs {line.Current} ({(line.Delta > 0 ? "+" : line.Delta < 0 ? "-" : "=")})", line.Delta > 0 ? UiTheme.Terminal : line.Delta < 0 ? UiTheme.Danger : UiTheme.InkMuted));
            }

            for (var i = 0; i < _detailRows.Count && i < lines.Count; i++)
            {
                var (key, value, color) = lines[i];
                _detailRows[i].text = string.IsNullOrEmpty(value) ? UiText.Fit(key, width) : StatLine(key, value, width);
                _detailRows[i].color = color;
            }
        }

        private static string StatLine(string key, string value, int width)
        {
            var chars = UiText.CharsFor(width);
            var k = UiText.Fit(key, width - UiText.Width(value) - UiText.Advance);
            var pad = Math.Max(1, chars - k.Length - value.Length);
            return k + new string(' ', pad) + value;
        }

        private static Color Readable(Color color)
        {
            var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance < 0.45f ? Color.Lerp(color, Color.white, (0.45f - luminance) / 0.45f) : color;
        }
    }
}
