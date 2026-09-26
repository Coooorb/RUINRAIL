using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Base;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Inventory
{
    /// <summary>
    /// The Shelter stash window (94 Storage): the survivor's five worn slots and eight backpack slots on the left, a
    /// paged 6×4 Storage grid on the right, and one strip at the bottom saying what the pointed-at item is and what
    /// activating it will do — or which rule stops it. Same window, slot, rarity-frame and focus language as the in-run
    /// inventory (<see cref="InventorySlotView"/>): click / Enter / A moves the item across, dragging does too (a
    /// Storage item dropped on a worn slot is equipped), arrows / D-pad step a two-dimensional grid, Back closes.
    /// It draws a <see cref="StashViewModel"/>; every move is that view model's, so the view owns no item and no rule.
    /// </summary>
    public sealed class StashView : MonoBehaviour
    {
        public static readonly UiRect Window = new(20, 12, 600, 336);
        public const int TitleHeight = 26;
        public static readonly UiRect SurvivorPanel = new(28, 44, 244, 226);
        public static readonly UiRect StoragePanel = new(300, 44, 312, 226);
        public static readonly UiRect ButtonRow = new(28, 274, 584, 18);
        public static readonly UiRect DetailsStrip = new(28, 296, 584, 44);
        public const int SlotSize = InventorySlotView.Size;
        public const int WornPitch = SlotSize + 2;
        public const int BackpackColumns = 4;
        public const int BackpackPitch = SlotSize + 8;
        public const int StoragePitch = SlotSize + 4;

        public const string StoreBackpackId = "stash.store_backpack";
        public const string PrevId = "stash.prev";
        public const string NextId = "stash.next";
        public const string FilterId = "stash.filter";
        public const string SortId = "stash.sort";
        public const string CloseId = "stash.close";

        private static readonly string[] WornCaptions = { "PRIMARY", "SECOND", "ARMOR", "ACCESS.", "USE" };

        private StashViewModel _viewModel;
        private System.Action _close;
        private FocusList _list;
        private RectTransform _root;
        private GameObject _panel;
        private readonly List<InventorySlotView> _worn = new();
        private readonly List<InventorySlotView> _backpack = new();
        private readonly List<InventorySlotView> _storage = new();
        private readonly Dictionary<string, UiControl> _buttons = new();
        private readonly Dictionary<string, Vector2> _centres = new();
        private Text _hints;
        private Text _carrying;
        private Text _backpackHeader;
        private Text _storageCount;
        private Text _page;
        private Image _capacityFill;
        private Image _survivorEdgeTint;
        private Image _storageEdgeTint;
        private Image _detailIcon;
        private Image _detailFrame;
        private Text _detailTitle;
        private Text _detailSubtitle;
        private Text _action;
        private Text _message;
        private bool _syncing;

        public StashViewModel ViewModel => _viewModel;
        public FocusList FocusList => _list;
        public bool IsVisible => _panel != null && _panel.activeSelf;
        public IReadOnlyList<InventorySlotView> WornSlots => _worn;
        public IReadOnlyList<InventorySlotView> BackpackSlots => _backpack;
        public IReadOnlyList<InventorySlotView> StorageSlots => _storage;
        public IReadOnlyDictionary<string, UiControl> Buttons => _buttons;
        public string ActionText => _action != null ? _action.text : string.Empty;
        public Color ActionColor => _action != null ? _action.color : Color.clear;
        public string MessageText => _message != null ? _message.text : string.Empty;
        public string DetailTitleText => _detailTitle != null ? _detailTitle.text : string.Empty;
        public string StorageCountText => _storageCount != null ? _storageCount.text : string.Empty;
        public string BackpackHeaderText => _backpackHeader != null ? _backpackHeader.text : string.Empty;
        public string PageText => _page != null ? _page.text : string.Empty;

        public static StashView Create(StashViewModel viewModel, System.Action close, string name = "StashUI")
        {
            var go = new GameObject(name);
            var view = go.AddComponent<StashView>();
            view._viewModel = viewModel;
            view._close = close;
            view.Build();
            view._list = view.BuildFocusList();
            foreach (var slot in view._worn.Concat(view._backpack).Concat(view._storage)) slot.BindList(view._list);
            viewModel.Changed += view.Render;
            view.Render();
            return view;
        }

        private void OnDestroy()
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
        }

        public static string IdOf(InventorySlotRef cell) => cell.Kind switch
        {
            InventorySlotKind.Equipped => "stash.worn." + cell.Index,
            InventorySlotKind.Backpack => "stash.bag." + cell.Index,
            _ => "stash.store." + cell.Index
        };

        public static bool TryParse(string id, out InventorySlotRef cell)
        {
            cell = default;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var (prefix, kind) in new[] { ("stash.worn.", InventorySlotKind.Equipped), ("stash.bag.", InventorySlotKind.Backpack), ("stash.store.", InventorySlotKind.Storage) })
            {
                if (id.StartsWith(prefix, System.StringComparison.Ordinal) && int.TryParse(id.Substring(prefix.Length), out var index))
                {
                    cell = new InventorySlotRef(kind, index);
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- construction

        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingLayerName = RuinRail.Core.Rendering.SortingLayers.ScreenUI;
            canvas.sortingOrder = 30; // the overlay band the inventory, merchant and cache windows share
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiTheme.ScreenWidth, UiTheme.ScreenHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("ReferenceRoot", typeof(RectTransform));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.sizeDelta = new Vector2(UiTheme.ScreenWidth, UiTheme.ScreenHeight);

            var skin = UiSkin.Load();
            // Heavier than the in-run inventory's dim: the Shelter's big header wordmark sits just above the window edge.
            var dim = UiBuild.Plate(_root, ScreenLayout.Screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.88f), "Dim");
            dim.raycastTarget = true; // nothing of the Shelter underneath can be clicked while the stash is up
            _panel = dim.gameObject;

            // Opaque: the Shelter's header wordmark sits right behind the title bar and must not show through it.
            var window = UiBuild.Panel(_panel.transform, Window, "Panel", UiTheme.NearBlack, UiTheme.PanelEdge).transform;
            UiBuild.Sliced(window, new UiRect(0, 0, Window.Width, Window.Height), skin != null ? skin.PanelFrame : null,
                skin != null && skin.PanelFrame != null ? Color.white : new Color(0f, 0f, 0f, 0f), "Frame");

            // Title bar: name, input hints.
            UiBuild.Plate(window, new UiRect(0, 0, Window.Width, TitleHeight), UiTheme.NearBlack, "TitleBarBase");
            UiBuild.Plate(window, new UiRect(0, 0, Window.Width, TitleHeight), UiTheme.ChromeBar, "TitleBar");
            UiBuild.Plate(window, new UiRect(0, TitleHeight - 1, Window.Width, 1), UiTheme.PanelEdge, "TitleRule");
            UiBuild.Label(window, "STASH", new UiRect(UiTheme.PadLarge, 4, 90, UiText.LineHeight * 2), 2, TextAnchor.UpperLeft, UiTheme.Amber, false, "Title");
            _hints = UiBuild.Label(window, string.Empty, new UiRect(120, 9, Window.Width - 120 - UiTheme.PadLarge, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.InkMuted, false, "Hints");
            _hints.alignment = TextAnchor.UpperRight;

            BuildSurvivor(window, skin);
            BuildTransferArrows(window);
            BuildStorage(window, skin);
            BuildButtons(window);
            BuildDetails(window);
        }

        private static UiRect Local(UiRect r) => new(r.X - Window.X, r.Y - Window.Y, r.Width, r.Height);

        private GameObject Section(Transform window, UiRect bounds, string name, string header, out Image edgeTint)
        {
            var panel = UiBuild.Panel(window, Local(bounds), name, UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft);
            // A drop/target tint: the panel an action would move the item into lights up.
            edgeTint = UiBuild.Plate(panel.transform, new UiRect(0, 0, bounds.Width, 2), UiTheme.Terminal, name + "TargetTint");
            edgeTint.enabled = false;
            UiBuild.Label(panel.transform, header, new UiRect(UiTheme.Pad, 5, bounds.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, name + "Header");
            UiBuild.Plate(panel.transform, new UiRect(UiTheme.Pad, 17, bounds.Width - UiTheme.Pad * 2, 1), UiTheme.PanelEdge, "HeaderRule");
            return panel;
        }

        private void BuildSurvivor(Transform window, UiSkin skin)
        {
            var panel = Section(window, SurvivorPanel, "Survivor", "SURVIVOR", out _survivorEdgeTint).transform;
            _carrying = UiBuild.Label(panel, string.Empty, new UiRect(UiTheme.Pad, 5, SurvivorPanel.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Ink, false, "Carrying");
            _carrying.alignment = TextAnchor.UpperRight;

            UiBuild.Label(panel, "WORN", new UiRect(UiTheme.Pad, 22, 60, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, "WornHeader");
            for (var i = 0; i < StashViewModel.EquippedSlots; i++)
            {
                var bounds = new UiRect(UiTheme.Pad + i * WornPitch, 33, SlotSize, SlotSize);
                _worn.Add(CreateSlot(panel, bounds, new InventorySlotRef(InventorySlotKind.Equipped, i), skin));
                var caption = UiBuild.Label(panel, WornCaptions[i], new UiRect(bounds.X - 1, bounds.Bottom + 2, SlotSize + 2, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkFaint, false, "WornCaption" + i);
                caption.alignment = TextAnchor.UpperCenter;
            }

            _backpackHeader = UiBuild.Label(panel, string.Empty, new UiRect(UiTheme.Pad, 102, SurvivorPanel.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, "BackpackHeader");
            var gridWidth = BackpackColumns * SlotSize + (BackpackColumns - 1) * (BackpackPitch - SlotSize);
            var x0 = (SurvivorPanel.Width - gridWidth) / 2;
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
            {
                var bounds = new UiRect(x0 + i % BackpackColumns * BackpackPitch, 114 + i / BackpackColumns * (SlotSize + 6), SlotSize, SlotSize);
                _backpack.Add(CreateSlot(panel, bounds, new InventorySlotRef(InventorySlotKind.Backpack, i), skin));
            }
        }

        /// <summary>Two chevrons between the panels: the stash moves both ways.</summary>
        private static void BuildTransferArrows(Transform window)
        {
            var x = SurvivorPanel.Right + 4 - Window.X;
            var width = StoragePanel.X - SurvivorPanel.Right - 8;
            var right = UiBuild.Label(window, ">>", new UiRect(x, 120 - Window.Y, width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Amber, false, "ArrowStore");
            right.alignment = TextAnchor.UpperCenter;
            var left = UiBuild.Label(window, "<<", new UiRect(x, 150 - Window.Y, width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "ArrowTake");
            left.alignment = TextAnchor.UpperCenter;
        }

        private void BuildStorage(Transform window, UiSkin skin)
        {
            var panel = Section(window, StoragePanel, "Storage", "STORAGE", out _storageEdgeTint).transform;
            _storageCount = UiBuild.Label(panel, string.Empty, new UiRect(64, 5, 100, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "StorageCount");
            _page = UiBuild.Label(panel, string.Empty, new UiRect(StoragePanel.Width - UiTheme.Pad - 90, 5, 90, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.InkMuted, false, "Page");
            _page.alignment = TextAnchor.UpperRight;
            // Capacity as a bar in the header rule's place: how full Storage is, red when it is.
            var bar = new UiRect(UiTheme.Pad, 17, StoragePanel.Width - UiTheme.Pad * 2, 3);
            UiBuild.Plate(panel, bar, UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "CapacityTrack");
            _capacityFill = UiBuild.Plate(panel, bar, UiTheme.Amber, "CapacityFill");
            _capacityFill.rectTransform.pivot = new Vector2(0f, 1f);

            var gridWidth = StashViewModel.StorageColumns * StoragePitch - (StoragePitch - SlotSize);
            var x0 = (StoragePanel.Width - gridWidth) / 2;
            for (var i = 0; i < StashViewModel.PageSize; i++)
            {
                var bounds = new UiRect(x0 + i % StashViewModel.StorageColumns * StoragePitch, 27 + i / StashViewModel.StorageColumns * StoragePitch, SlotSize, SlotSize);
                _storage.Add(CreateSlot(panel, bounds, StashViewModel.StorageCell(i), skin));
            }
        }

        private void BuildButtons(Transform window)
        {
            var y = ButtonRow.Y - Window.Y;
            AddButton(window, StoreBackpackId, "STORE WHOLE BACKPACK", new UiRect(SurvivorPanel.X - Window.X, y, SurvivorPanel.Width, ButtonRow.Height), true);
            var x = StoragePanel.X - Window.X;
            foreach (var (id, width) in new[] { (PrevId, 28), (NextId, 28), (FilterId, 92), (SortId, 92), (CloseId, 48) })
            {
                AddButton(window, id, string.Empty, new UiRect(x, y, width, ButtonRow.Height), false);
                x += width + 6;
            }
        }

        private void AddButton(Transform parent, string id, string label, UiRect bounds, bool primary)
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
            text.alignment = TextAnchor.UpperCenter;
            var control = rect.gameObject.AddComponent<UiControl>();
            control.Bind(null, null, primary ? ControlRole.Primary : ControlRole.Button, fill, text, edges, brackets, marker, null, null, bounds.Width, 1);
            _buttons[id] = control;
            _centres[id] = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
        }

        private void BuildDetails(Transform window)
        {
            var strip = UiBuild.Panel(window, Local(DetailsStrip), "Details", UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft).transform;
            _detailFrame = UiBuild.Plate(strip, new UiRect(6, 5, 34, 34), UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "DetailIconFrame");
            var iconRect = UiBuild.NewRect(strip, "DetailIcon", new UiRect(7, 6, 32, 32));
            _detailIcon = iconRect.gameObject.AddComponent<Image>();
            _detailIcon.raycastTarget = false;
            _detailIcon.preserveAspect = true;
            _detailIcon.enabled = false;
            _detailTitle = UiBuild.Label(strip, string.Empty, new UiRect(48, 5, 300, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "DetailTitle");
            _detailSubtitle = UiBuild.Label(strip, string.Empty, new UiRect(48, 16, 300, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "DetailSubtitle");
            _action = UiBuild.Label(strip, string.Empty, new UiRect(48, 28, DetailsStrip.Width - 56, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Terminal, false, "Action");
            _message = UiBuild.Label(strip, string.Empty, new UiRect(356, 5, DetailsStrip.Width - 364, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Terminal, false, "Message");
            _message.alignment = TextAnchor.UpperRight;
        }

        private InventorySlotView CreateSlot(Transform parent, UiRect bounds, InventorySlotRef cell, UiSkin skin)
        {
            var slot = InventorySlotView.Create(parent, bounds, cell, IdOf(cell), null,
                skin != null ? skin.InventorySlot : null, rarity => skin != null ? skin.RarityFrame(rarity) : null,
                OnHovered, OnClicked, OnDropped, c => IconOf(_viewModel?.ItemAt(c)));
            // Window-space centre of the slot, for the two-dimensional focus steps.
            var parentRect = (RectTransform)parent;
            var panelOrigin = parent.name == "Survivor" ? SurvivorPanel : StoragePanel;
            _centres[IdOf(cell)] = new Vector2(panelOrigin.X - Window.X + bounds.X + bounds.Width * 0.5f, panelOrigin.Y - Window.Y + bounds.Y + bounds.Height * 0.5f);
            return slot;
        }

        private Sprite IconOf(ItemInstance item) => _viewModel?.DefinitionOf(item)?.Icon;

        // ---------------------------------------------------------------- focus (keyboard / controller / mouse share it)

        private FocusList BuildFocusList()
        {
            var list = new FocusList("Stash");
            foreach (var slot in _worn.Concat(_backpack).Concat(_storage))
            {
                var cell = slot.Slot;
                list.Add(IdOf(cell), IdOf(cell), () => _viewModel.Activate(cell));
            }

            list.Add(StoreBackpackId, "STORE WHOLE BACKPACK", () => _viewModel.StoreBackpack(), () => _viewModel.BackpackCount > 0);
            list.Add(PrevId, "<", () => _viewModel.PrevPage(), () => _viewModel.Page > 0);
            list.Add(NextId, ">", () => _viewModel.NextPage(), () => _viewModel.Page < _viewModel.PageCount - 1);
            list.Add(FilterId, "SHOW", () => _viewModel.CycleFilter());
            list.Add(SortId, "SORT", () => _viewModel.ToggleSort());
            list.Add(CloseId, "CLOSE", () => _close?.Invoke());
            list.Navigator = Navigate;
            list.FocusChanged += OnFocusChanged;

            foreach (var (id, control) in _buttons)
            {
                var item = list.Find(id);
                control.Bind(list, item, id == StoreBackpackId ? ControlRole.Primary : ControlRole.Button, control.GetComponent<Image>(), control.GetComponentInChildren<Text>(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Edge")).ToList(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Bracket")).ToList(),
                    control.GetComponentsInChildren<Image>().First(i => i.name == "SelectedMarker"),
                    null, i => { list.Focus(i.Id); list.ActivateFocused(); }, control.Rect().Width, 1);
            }

            list.Focus(IdOf(new InventorySlotRef(InventorySlotKind.Backpack, 0)));
            return list;
        }

        /// <summary>
        /// Two-dimensional steps over the whole window: the nearest enabled stop in the pressed direction, weighing the
        /// cross-axis offset double, so rows and columns read as rows and columns and the two panels join sideways.
        /// </summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null || !_centres.TryGetValue(focused.Id, out var from)) return null;
            var step = new Vector2(direction.x, -direction.y); // window space grows downward
            string best = null;
            var bestScore = float.MaxValue;
            foreach (var item in _list.Items)
            {
                if (item.Id == focused.Id || !item.IsEnabled || !_centres.TryGetValue(item.Id, out var to)) continue;
                var delta = to - from;
                var along = Vector2.Dot(delta, step);
                if (along <= 1f) continue;
                var across = Mathf.Abs(step.x != 0f ? delta.y : delta.x);
                var score = along + across * 2f;
                if (score < bestScore) { bestScore = score; best = item.Id; }
            }

            return best;
        }

        private void OnFocusChanged(FocusItem focused)
        {
            if (_syncing || focused == null) return;
            _viewModel.SetCursor(TryParse(focused.Id, out var cell) ? cell : (InventorySlotRef?)null);
        }

        private void OnHovered(InventorySlotRef cell)
        {
            _list.Focus(IdOf(cell));
            _viewModel.SetCursor(cell);
        }

        private void OnClicked(InventorySlotRef cell)
        {
            _list.Focus(IdOf(cell));
            _viewModel.Activate(cell);
        }

        private void OnDropped(InventorySlotRef from, InventorySlotRef to)
        {
            _viewModel.Drop(from, to);
            _viewModel.SetCursor(to);
        }

        // ---------------------------------------------------------------- rendering

        private void Render()
        {
            if (_viewModel == null || _panel == null) return;
            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            _hints.text = gamepad ? "A: MOVE ITEM   D-PAD: SELECT   B: CLOSE" : "CLICK / ENTER: MOVE ITEM   DRAG: MOVE   ESC: CLOSE";

            var carried = _viewModel.CarriedCount;
            _carrying.text = carried == 0 ? "NOTHING CARRIED" : $"CARRYING {carried}";
            _carrying.color = carried == 0 ? UiTheme.InkFaint : UiTheme.Ink;
            for (var i = 0; i < _worn.Count; i++) Show(_worn[i]);
            for (var i = 0; i < _backpack.Count; i++) Show(_backpack[i]);
            for (var i = 0; i < _storage.Count; i++) Show(_storage[i]);

            var bagFull = _viewModel.BackpackCount >= PlayerInventory.BackpackCapacity;
            _backpackHeader.text = $"BACKPACK  {_viewModel.BackpackCount}/{PlayerInventory.BackpackCapacity}" + (bagFull ? "  FULL" : string.Empty);
            _backpackHeader.color = bagFull ? UiTheme.Danger : UiTheme.Amber;

            var full = _viewModel.StorageFull;
            _storageCount.text = $"{_viewModel.StoredCount} / {_viewModel.Capacity}" + (full ? "  FULL" : string.Empty);
            _storageCount.color = full ? UiTheme.Danger : UiTheme.Ink;
            _page.text = $"PAGE {_viewModel.Page + 1}/{_viewModel.PageCount}";
            var fill = _viewModel.Capacity <= 0 ? 0f : Mathf.Clamp01(_viewModel.StoredCount / (float)_viewModel.Capacity);
            _capacityFill.rectTransform.sizeDelta = new Vector2(Mathf.Round(fill * (StoragePanel.Width - UiTheme.Pad * 2)), _capacityFill.rectTransform.sizeDelta.y);
            _capacityFill.color = full ? UiTheme.Danger : UiTheme.Amber;

            SetLabel(PrevId, "<");
            SetLabel(NextId, ">");
            SetLabel(FilterId, "SHOW: " + _viewModel.FilterLabel);
            SetLabel(SortId, _viewModel.SortByRarity ? "SORT: RARITY" : "SORT: NAME");
            SetLabel(CloseId, "CLOSE");

            RenderDetails();
            _message.text = UiText.Fit(_viewModel.Message, DetailsStrip.Width - 364);
            _message.color = _viewModel.MessageIsError ? UiTheme.Danger : UiTheme.Terminal;
        }

        private void Show(InventorySlotView slot)
        {
            var item = _viewModel.ItemAt(slot.Slot);
            var definition = _viewModel.DefinitionOf(item);
            slot.Show(item, IconOf(item), definition == null || definition.IsStackable);
            slot.SetSelected(_viewModel.Cursor.HasValue && _viewModel.Cursor.Value.Equals(slot.Slot) && item != null);
        }

        private void SetLabel(string id, string label)
        {
            if (!_buttons.TryGetValue(id, out var control)) return;
            var text = control.GetComponentInChildren<Text>();
            if (text != null) text.text = UiText.Fit(label, control.Rect().Width - 6);
        }

        private void RenderDetails()
        {
            var cell = _viewModel.Cursor;
            var item = cell.HasValue ? _viewModel.ItemAt(cell.Value) : null;
            _survivorEdgeTint.enabled = false;
            _storageEdgeTint.enabled = false;
            if (item == null)
            {
                _detailIcon.enabled = false;
                _detailFrame.color = UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f);
                _detailTitle.text = cell.HasValue ? EmptyTitle(cell.Value) : "POINT AT AN ITEM";
                _detailTitle.color = UiTheme.InkMuted;
                _detailSubtitle.text = string.Empty;
                _action.text = cell.HasValue && cell.Value.Kind != InventorySlotKind.Storage ? "Drag a Storage item here to take it." : "Pick an item on the survivor to store it, or in Storage to take it.";
                _action.color = UiTheme.InkMuted;
                return;
            }

            var definition = _viewModel.DefinitionOf(item);
            var style = RarityStyle.For(item.Rarity);
            _detailIcon.sprite = definition != null ? definition.Icon : null;
            _detailIcon.enabled = _detailIcon.sprite != null;
            _detailFrame.color = UiTheme.WithAlpha(style.Color, 0.85f);
            _detailTitle.text = UiText.Fit((definition != null ? definition.DisplayName : item.DefinitionId) + (item.Quantity > 1 ? "  x" + item.Quantity : string.Empty), 300);
            _detailTitle.color = item.Rarity == Rarity.Common ? UiTheme.Ink : Readable(style.Color);
            var where = cell.Value.Kind == InventorySlotKind.Equipped ? "WORN" : cell.Value.Kind == InventorySlotKind.Backpack ? "IN BACKPACK" : "IN STORAGE";
            _detailSubtitle.text = $"{style.Label}  ·  {(definition != null ? definition.Category.ToString().ToUpperInvariant() : "ITEM")}  ·  {where}";

            var intent = _viewModel.IntentFor(cell.Value);
            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            var verb = gamepad ? "A" : "CLICK / ENTER";
            switch (intent.Action)
            {
                case StashAction.Store:
                    _action.text = $"{verb}:  STORE  >>  STORAGE";
                    _action.color = UiTheme.Terminal;
                    _storageEdgeTint.enabled = true;
                    _storageEdgeTint.color = UiTheme.Terminal;
                    break;
                case StashAction.Take:
                    _action.text = $"{verb}:  TAKE  <<  INTO BACKPACK   (or drag onto a worn slot to equip)";
                    _action.color = UiTheme.Terminal;
                    _survivorEdgeTint.enabled = true;
                    _survivorEdgeTint.color = UiTheme.Terminal;
                    break;
                default:
                    _action.text = UiText.Fit($"{intent.Label}: {intent.Reason}", DetailsStrip.Width - 56);
                    _action.color = UiTheme.Danger;
                    var blockedSide = cell.Value.Kind == InventorySlotKind.Storage ? _survivorEdgeTint : _storageEdgeTint;
                    blockedSide.enabled = true;
                    blockedSide.color = UiTheme.Danger;
                    break;
            }
        }

        private static string EmptyTitle(InventorySlotRef cell) => cell.Kind switch
        {
            InventorySlotKind.Equipped => WornCaptions[Mathf.Clamp(cell.Index, 0, WornCaptions.Length - 1)] + " SLOT — EMPTY",
            InventorySlotKind.Backpack => $"BACKPACK SLOT {cell.Index + 1} — EMPTY",
            _ => "EMPTY STORAGE CELL"
        };

        /// <summary>Rarity colours lifted enough to read as text on the dark strip (Common stays ink).</summary>
        private static Color Readable(Color c) => Color.Lerp(c, Color.white, 0.25f);

        // ---------------------------------------------------------------- test seams

        public InventorySlotView SlotFor(InventorySlotRef cell) => cell.Kind switch
        {
            InventorySlotKind.Equipped => _worn[cell.Index],
            InventorySlotKind.Backpack => _backpack[cell.Index],
            _ => _storage[cell.Index]
        };
    }
}
