using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.SecureRelay
{
    /// <summary>
    /// The Secure Relay terminal window (57.7) in the inventory's visual language: the member's worn slots and backpack
    /// as the same graphical slots the inventory draws (items that cannot be secured are shaded and marked), the item
    /// details with the relay's verdict on the right, and SECURE → CONFIRM / LEAVE at the bottom. Once this member's use
    /// is spent the carried grid gives way to the ITEM SECURED card. Mouse, keyboard and controller share one focus
    /// list; nothing here touches gameplay except through the view model.
    /// </summary>
    public sealed class SecureRelayView : MonoBehaviour
    {
        public const int ReferenceWidth = UiTheme.ScreenWidth;
        public const int ReferenceHeight = UiTheme.ScreenHeight;
        public static readonly UiRect Window = new(60, 40, 520, 280);
        public const int TitleHeight = 26;
        public static readonly UiRect CarriedPanel = new(68, 72, 224, 176);
        public static readonly UiRect DetailsPanel = new(300, 72, 272, 176);
        public static readonly UiRect ActionPanel = new(68, 256, 504, 56);
        public static readonly UiRect SecuredPanel = new(68, 72, 504, 176);
        public const int CellSize = 36;
        public const int CellGap = 4;
        public const int BackpackColumns = 4;
        public const int DetailLines = 10;
        public const string CellFocusPrefix = "relay.cell.";
        public const string SecureFocusId = "relay.secure";
        public const string CloseFocusId = "relay.close";

        private SecureRelayViewModel _viewModel;
        private Canvas _canvas;
        private RectTransform _root;
        private GameObject _panel;
        private GameObject _carried;
        private GameObject _details;
        private GameObject _secured;
        private FocusList _list;
        private UiSkin _skin;
        private readonly List<InventorySlotView> _cells = new();
        private readonly List<Image> _cellShades = new();
        private readonly List<Image> _cellMarks = new();
        private readonly List<Text> _detailRows = new();
        private readonly DetailPager _detailPager = new(DetailLines);
        private SecureRelayCell _detailCell;
        private readonly Dictionary<string, UiControl> _buttons = new();
        private Text _title;
        private Text _hints;
        private Text _detailTitle;
        private Text _detailSubtitle;
        private Text _status;
        private Text _message;
        private Text _storageLine;
        private InventorySlotView _securedSlot;
        private Text _securedHeadline;
        private Text _securedName;
        private Text _securedLine1;
        private Text _securedLine2;
        private bool _syncingFocus;
        private int _opensRendered;
        private SecureRelayStage _stageRendered;

        public SecureRelayViewModel ViewModel => _viewModel;
        public FocusList FocusList => _list;
        public bool IsVisible => _panel != null && _panel.activeSelf;
        public bool ShowsSecuredCard => _secured != null && _secured.activeSelf;
        public IReadOnlyList<InventorySlotView> CellViews => _cells;
        public IReadOnlyDictionary<string, UiControl> Buttons => _buttons;
        public string TitleText => _title != null ? _title.text : string.Empty;
        public string StatusText => _status != null ? _status.text : string.Empty;
        public string MessageText => _message != null ? _message.text : string.Empty;
        public string StorageText => _storageLine != null ? _storageLine.text : string.Empty;
        public string SecureButtonText => _buttons.TryGetValue(SecureFocusId, out var c) ? c.GetComponentInChildren<Text>().text : string.Empty;
        public string SecuredHeadlineText => _securedHeadline != null ? _securedHeadline.text : string.Empty;
        public string SecuredNameText => _securedName != null ? _securedName.text : string.Empty;
        public bool CellShaded(int index) => index >= 0 && index < _cellShades.Count && _cellShades[index].enabled;
        public int Renders { get; private set; }

        public static SecureRelayView Create(SecureRelayViewModel viewModel, string name = "SecureRelayUI")
        {
            var go = new GameObject(name);
            var view = go.AddComponent<SecureRelayView>();
            view.Build();
            view.Bind(viewModel);
            return view;
        }

        public void Bind(SecureRelayViewModel viewModel)
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
            _viewModel = viewModel;
            _list = BuildFocusList();
            foreach (var cell in _cells) cell.BindList(_list);
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
            _canvas.sortingOrder = 30; // the overlay band the inventory, merchant and cache windows share
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
            BuildCarried(window.transform);
            BuildDetails(window.transform);
            BuildSecured(window.transform);
            BuildActions(window.transform);
            _panel.SetActive(false);
        }

        private static UiRect Local(UiRect panel) => new(panel.X - Window.X, panel.Y - Window.Y, panel.Width, panel.Height);

        private void BuildTitle(Transform window)
        {
            var bar = new UiRect(0, 0, Window.Width, TitleHeight);
            UiBuild.Plate(window, bar, UiTheme.ChromeBar, "TitleBar");
            UiBuild.Plate(window, new UiRect(0, TitleHeight - 1, Window.Width, 1), UiTheme.PanelEdge, "TitleRule");
            // The uplink lamp: the terminal's one emissive cue, green like the relay's screen in the world.
            UiBuild.Plate(window, new UiRect(UiTheme.PadLarge, 9, 6, 6), UiTheme.Terminal, "UplinkLamp");
            _title = UiBuild.Label(window, "SECURE RELAY", new UiRect(UiTheme.PadLarge + 10, 4, 260, UiText.LineHeight * 2), 2, TextAnchor.UpperLeft, UiTheme.Terminal, false, "Title");
            _hints = UiBuild.Label(window, string.Empty, new UiRect(Window.Width - UiTheme.PadLarge - 220, 9, 220, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.InkMuted, false, "Hints");
        }

        private GameObject SectionPanel(Transform window, UiRect bounds, string name, string header)
        {
            var panel = UiBuild.Panel(window, Local(bounds), name, UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft);
            if (header != null)
            {
                UiBuild.Label(panel.transform, header, new UiRect(UiTheme.Pad, 5, bounds.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, name + "Header");
                UiBuild.Plate(panel.transform, new UiRect(UiTheme.Pad, 17, bounds.Width - UiTheme.Pad * 2, 1), UiTheme.PanelEdge, "HeaderRule");
            }

            return panel;
        }

        /// <summary>Cell origin inside the carried panel: the worn row, then the backpack as two rows of four.</summary>
        public static UiRect CellBounds(int index)
        {
            const int left = 8;
            if (index < SecureRelayViewModel.EquippedCells) return new UiRect(left + index * (CellSize + CellGap), 34, CellSize, CellSize);
            var b = index - SecureRelayViewModel.EquippedCells;
            return new UiRect(left + (b % BackpackColumns) * (CellSize + CellGap), 88 + (b / BackpackColumns) * (CellSize + CellGap), CellSize, CellSize);
        }

        private void BuildCarried(Transform window)
        {
            _carried = SectionPanel(window, CarriedPanel, "Carried", "SELECT ONE ITEM TO SEND HOME");
            UiBuild.Label(_carried.transform, "LOADOUT", new UiRect(8, 23, 120, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkFaint, false, "LoadoutLabel");
            UiBuild.Label(_carried.transform, "BACKPACK", new UiRect(8, 77, 120, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkFaint, false, "BackpackLabel");
            for (var i = 0; i < SecureRelayViewModel.CellCount; i++)
            {
                var index = i;
                var bounds = CellBounds(i);
                var slot = i < SecureRelayViewModel.EquippedCells ? new InventorySlotRef(InventorySlotKind.Equipped, i) : new InventorySlotRef(InventorySlotKind.Backpack, i - SecureRelayViewModel.EquippedCells);
                var view = InventorySlotView.Create(_carried.transform, bounds, slot, CellFocusPrefix + i, null,
                    _skin != null ? _skin.InventorySlot : null, rarity => _skin != null ? _skin.RarityFrame(rarity) : null,
                    _ => { }, _ => _viewModel?.SetCursor(index), null, _ => index < (_viewModel?.Cells.Count ?? 0) ? _viewModel.Cells[index].Icon : null);
                // Shade + red corner tab: this slot cannot be secured (ammo, starter gear, no room at home).
                var shade = UiBuild.Plate(view.transform, new UiRect(2, 2, CellSize - 4, CellSize - 4), UiTheme.WithAlpha(UiTheme.NearBlack, 0.62f), "Shade");
                shade.raycastTarget = false;
                shade.enabled = false;
                var mark = UiBuild.Plate(view.transform, new UiRect(CellSize - 8, 3, 5, 5), UiTheme.Danger, "Refused");
                mark.raycastTarget = false;
                mark.enabled = false;
                _cells.Add(view);
                _cellShades.Add(shade);
                _cellMarks.Add(mark);
            }
        }

        private void BuildDetails(Transform window)
        {
            _details = SectionPanel(window, DetailsPanel, "Details", "DETAILS");
            var inner = DetailsPanel.Width - UiTheme.Pad * 2;
            _detailTitle = UiBuild.Label(_details.transform, string.Empty, new UiRect(UiTheme.Pad, 22, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "DetailTitle");
            _detailSubtitle = UiBuild.Label(_details.transform, string.Empty, new UiRect(UiTheme.Pad, 32, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "DetailSubtitle");
            _status = UiBuild.Label(_details.transform, string.Empty, new UiRect(UiTheme.Pad, 43, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Terminal, false, "Status");
            UiBuild.Plate(_details.transform, new UiRect(UiTheme.Pad, 54, inner, 1), UiTheme.PanelEdgeSoft, "DetailRule");
            for (var i = 0; i < DetailLines; i++)
                _detailRows.Add(UiBuild.Label(_details.transform, string.Empty, new UiRect(UiTheme.Pad, 58 + i * (UiText.LineHeight + 1), inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Detail" + i));
        }

        /// <summary>The spent-relay card: the secured item in a green-framed slot under a large ITEM SECURED headline.</summary>
        private void BuildSecured(Transform window)
        {
            _secured = SectionPanel(window, SecuredPanel, "Secured", null);
            var w = SecuredPanel.Width;
            const int slotSize = 44;
            var slotX = (w - slotSize) / 2;
            UiBuild.Border(_secured.transform, new UiRect(slotX - 4, 22, slotSize + 8, slotSize + 8), UiTheme.Terminal, 2);
            _securedSlot = InventorySlotView.Create(_secured.transform, new UiRect(slotX, 26, slotSize, slotSize), new InventorySlotRef(InventorySlotKind.Storage, 0), "relay.secured", null,
                _skin != null ? _skin.InventorySlot : null, rarity => _skin != null ? _skin.RarityFrame(rarity) : null, null, null, null, null);
            _securedHeadline = UiBuild.Label(_secured.transform, "ITEM SECURED", new UiRect(0, 82, w, UiText.LineHeight * 2), 2, TextAnchor.UpperCenter, UiTheme.Terminal, false, "Headline");
            _securedName = UiBuild.Label(_secured.transform, string.Empty, new UiRect(UiTheme.Pad, 106, w - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Ink, false, "SecuredName");
            _securedLine1 = UiBuild.Label(_secured.transform, "STORED IN YOUR SHELTER STORAGE — SAFE EVEN IF THE RUN IS LOST", new UiRect(UiTheme.Pad, 124, w - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "SecuredLine1");
            _securedLine2 = UiBuild.Label(_secured.transform, "ONE TRANSFER PER SURVIVOR: THIS RELAY IS SPENT FOR YOU", new UiRect(UiTheme.Pad, 136, w - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkFaint, false, "SecuredLine2");
            _secured.SetActive(false);
        }

        private void BuildActions(Transform window)
        {
            var panel = UiBuild.Panel(window, Local(ActionPanel), "Actions", UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft);
            _storageLine = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 12, ActionPanel.Width / 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Storage");
            _message = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 12 + UiText.LineHeight + 4, ActionPanel.Width / 2 + 20, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Danger, false, "Message");
            const int buttonWidth = 120;
            _buttons[SecureFocusId] = BuildButton(panel.transform, new UiRect(ActionPanel.Width - UiTheme.Pad - buttonWidth * 2 - 8, 16, buttonWidth, 24), SecureFocusId, "SECURE", ControlRole.Primary);
            _buttons[CloseFocusId] = BuildButton(panel.transform, new UiRect(ActionPanel.Width - UiTheme.Pad - buttonWidth, 16, buttonWidth, 24), CloseFocusId, "LEAVE", ControlRole.Exit);
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
            var list = new FocusList("SecureRelay");
            if (_viewModel == null) return list;
            for (var i = 0; i < SecureRelayViewModel.CellCount; i++)
            {
                var index = i;
                list.Add(CellFocusPrefix + i, "Slot " + (i + 1), () => { _viewModel.SetCursor(index); _viewModel.Secure(); },
                    () => _viewModel.Stage != SecureRelayStage.Secured);
            }

            list.Add(SecureFocusId, "SECURE", () => _viewModel.Secure(), () => _viewModel.CanSecure);
            list.Add(CloseFocusId, "LEAVE", () => _viewModel.Close());
            list.Navigator = Navigate;
            list.FocusChanged += OnFocusChanged;

            foreach (var pair in _buttons)
            {
                var id = pair.Key;
                var control = pair.Value;
                var item = list.Find(id);
                var role = id == SecureFocusId ? ControlRole.Primary : ControlRole.Exit;
                control.Bind(list, item, role, control.GetComponent<Image>(), control.GetComponentInChildren<Text>(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Edge", StringComparison.Ordinal)).ToList(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Bracket", StringComparison.Ordinal)).ToList(),
                    control.GetComponentsInChildren<Image>().First(i => i.name == "SelectedMarker"),
                    null, i => { list.Focus(i.Id); list.ActivateFocused(); }, Mathf.RoundToInt(((RectTransform)control.transform).sizeDelta.x), 1);
            }

            return list;
        }

        /// <summary>Grid navigation: worn row, backpack 4×2, then SECURE / LEAVE side by side.</summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null || _viewModel == null) return null;
            const int eq = SecureRelayViewModel.EquippedCells;
            if (focused.Id.StartsWith(CellFocusPrefix, StringComparison.Ordinal) && int.TryParse(focused.Id.Substring(CellFocusPrefix.Length), out var i))
            {
                if (i < eq)
                {
                    if (direction.x != 0) return i + direction.x >= 0 && i + direction.x < eq ? CellFocusPrefix + (i + direction.x) : (direction.x > 0 ? FirstAction() : null);
                    if (direction.y < 0) return CellFocusPrefix + (eq + Mathf.Min(i, BackpackColumns - 1));
                    return null;
                }

                var b = i - eq;
                var column = b % BackpackColumns;
                var row = b / BackpackColumns;
                if (direction.x != 0)
                {
                    var next = column + direction.x;
                    if (next >= 0 && next < BackpackColumns) return CellFocusPrefix + (i + direction.x);
                    return direction.x > 0 ? FirstAction() : null;
                }

                if (direction.y < 0) return row == 0 ? CellFocusPrefix + (i + BackpackColumns) : FirstAction();
                if (direction.y > 0) return row == 1 ? CellFocusPrefix + (i - BackpackColumns) : CellFocusPrefix + Mathf.Min(column, eq - 1);
                return null;
            }

            var actions = EnabledActions();
            var at = actions.IndexOf(focused.Id);
            if (at < 0) return null;
            var grid = _viewModel.Stage != SecureRelayStage.Secured;
            if (direction.y > 0) return grid ? CellFocusPrefix + (eq + BackpackColumns) : null;
            if (direction.x > 0) return at < actions.Count - 1 ? actions[at + 1] : null;
            if (direction.x < 0) return at > 0 ? actions[at - 1] : (grid ? CellFocusPrefix + (eq + BackpackColumns * 2 - 1) : null);
            return null;
        }

        private string FirstAction() => EnabledActions().First();

        private List<string> EnabledActions() =>
            new[] { SecureFocusId, CloseFocusId }.Where(id => _list != null && _list.Find(id) != null && _list.Find(id).IsEnabled).DefaultIfEmpty(CloseFocusId).ToList();

        private void OnFocusChanged(FocusItem focused)
        {
            if (_syncingFocus || focused == null || _viewModel == null) return;
            if (!focused.Id.StartsWith(CellFocusPrefix, StringComparison.Ordinal)) return;
            if (int.TryParse(focused.Id.Substring(CellFocusPrefix.Length), out var index) && _viewModel.Cursor != index) _viewModel.SetCursor(index);
        }

        private void SyncFocusToCursor()
        {
            if (_list == null) return;
            if (_viewModel.Stage == SecureRelayStage.Secured)
            {
                if (_list.Focused == null || _list.Focused.Id != CloseFocusId) { _syncingFocus = true; _list.Focus(CloseFocusId); _syncingFocus = false; }
                return;
            }

            var id = CellFocusPrefix + _viewModel.Cursor;
            if (_list.Focused != null && _list.Focused.Id == id) return;
            if (_list.Focused != null && !_list.Focused.Id.StartsWith(CellFocusPrefix, StringComparison.Ordinal)) return;
            _syncingFocus = true;
            _list.Focus(id);
            _syncingFocus = false;
        }

        // ---- rendering ----

        private void Render()
        {
            Renders++;
            if (_viewModel == null || _panel == null) return;
            if (_panel.activeSelf != _viewModel.IsOpen) _panel.SetActive(_viewModel.IsOpen);
            if (!_viewModel.IsOpen) return;
            var stage = _viewModel.Stage;
            if (_opensRendered != _viewModel.Opens || (_stageRendered != stage && stage == SecureRelayStage.Secured))
            {
                _opensRendered = _viewModel.Opens;
                _syncingFocus = true;
                if (stage == SecureRelayStage.Secured || !_list.Focus(CellFocusPrefix + _viewModel.Cursor)) _list.Focus(CloseFocusId);
                _syncingFocus = false;
            }

            _stageRendered = stage;
            var secured = stage == SecureRelayStage.Secured;
            _carried.SetActive(!secured);
            _details.SetActive(!secured);
            _secured.SetActive(secured);

            _title.text = "SECURE RELAY — DEPTH " + _viewModel.Depth;
            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            _hints.text = secured ? (gamepad ? "B: CLOSE" : "ESC: CLOSE") : gamepad ? "A: SECURE   B: LEAVE   D-PAD: MOVE" : "ENTER: SECURE   ESC: LEAVE";
            _storageLine.text = _viewModel.HasStorage ? $"SHELTER STORAGE  {_viewModel.StorageUsed} / {_viewModel.StorageCapacity}" : "SHELTER STORAGE OFFLINE";

            var secureButton = _buttons[SecureFocusId];
            secureButton.gameObject.SetActive(!secured);
            secureButton.GetComponentInChildren<Text>().text = stage == SecureRelayStage.Confirming ? "CONFIRM" : stage == SecureRelayStage.Pending ? "SENDING…" : "SECURE";
            _buttons[CloseFocusId].GetComponentInChildren<Text>().text = secured ? "CLOSE" : "LEAVE";
            foreach (var control in _buttons.Values) control.Refresh();

            _message.text = _viewModel.Message;
            _message.color = _viewModel.MessageIsError ? UiTheme.Danger : stage == SecureRelayStage.Confirming ? UiTheme.Amber : UiTheme.Terminal;

            if (secured) { RenderSecured(); return; }

            for (var i = 0; i < _cells.Count; i++)
            {
                var cell = i < _viewModel.Cells.Count ? _viewModel.Cells[i] : null;
                _cells[i].Show(cell?.Item, cell?.Icon, cell?.Definition != null && cell.Definition.IsStackable);
                _cells[i].SetSelected(i == _viewModel.Cursor);
                var refused = cell != null && !cell.IsEmpty && !cell.IsEligible;
                _cellShades[i].enabled = refused;
                _cellMarks[i].enabled = refused;
            }

            SyncFocusToCursor();
            RenderDetails();
        }

        private void RenderSecured()
        {
            var definition = _viewModel.SecuredDefinition;
            _securedSlot.Show(definition != null ? new ItemInstance(definition.Id) : null, definition != null ? definition.Icon : null, false);
            _securedName.text = UiText.Fit(definition != null ? definition.DisplayName.ToUpperInvariant() : "YOUR ITEM", SecuredPanel.Width - UiTheme.Pad * 2);
            SyncFocusToCursor();
        }

        private void RenderDetails()
        {
            var cell = _viewModel.Selected;
            var tooltip = _viewModel.TooltipFor(cell);
            foreach (var r in _detailRows) r.text = string.Empty;
            var width = DetailsPanel.Width - UiTheme.Pad * 2;
            _status.text = _viewModel.StatusLine;
            _status.color = cell != null && cell.IsEligible ? UiTheme.Terminal : cell == null || cell.IsEmpty ? UiTheme.InkFaint : UiTheme.Danger;
            if (tooltip == null)
            {
                _detailPager.SetRows(null, false);
                _detailCell = null;
                _detailTitle.text = "EMPTY SLOT";
                _detailTitle.color = UiTheme.InkMuted;
                _detailSubtitle.text = "Pick a slot that holds an item.";
                return;
            }

            var style = RarityStyle.For(tooltip.Rarity);
            _detailTitle.text = UiText.Fit(tooltip.Name, width);
            _detailTitle.color = Readable(style.Color);
            _detailSubtitle.text = UiText.Fit(style.Label + " · " + tooltip.CategoryText, width);
            var sameItem = ReferenceEquals(cell, _detailCell);
            _detailCell = cell;
            _detailPager.SetRows(ItemDetailLayout.Compose(tooltip, Array.Empty<ComparisonLine>(), width), sameItem);
            var visible = _detailPager.Visible(DetailPagingInput.Hint());
            for (var i = 0; i < _detailRows.Count && i < visible.Count; i++)
            {
                _detailRows[i].text = ItemDetailLayout.Render(visible[i], width);
                _detailRows[i].color = visible[i].Color;
            }
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf || !_detailPager.Overflows || !_details.activeSelf) return;
            var step = DetailPagingInput.Poll();
            if (step > 0 && _detailPager.PageDown()) RenderDetails();
            else if (step < 0 && _detailPager.PageUp()) RenderDetails();
        }

        private static Color Readable(Color color)
        {
            var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance < 0.45f ? Color.Lerp(color, Color.white, (0.45f - luminance) / 0.45f) : color;
        }
    }
}
