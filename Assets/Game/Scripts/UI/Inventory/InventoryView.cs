using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.UI.Inventory
{
    /// <summary>
    /// The graphical inventory window (92) at the 640×360 reference, code-built from the skin's sliced frames and the
    /// item definitions' icons:
    ///
    /// <code>
    /// ┌ INVENTORY ──────────────── hints ──────────── CARRIED COINS ┐
    /// │ EQUIPMENT      │ SURVIVOR        │ BACKPACK n/8            │
    /// │ [P] PRIMARY    │ ┌──────────┐    │ [ ][ ][ ][ ]            │
    /// │ [S] SECONDARY  │ │ portrait │    │ [ ][ ][ ][ ]            │
    /// │ [A] ARMOR      │ └──────────┘    ├─────────────────────────┤
    /// │ [+] ACCESSORY  │ AMMO reserves   │ DETAILS                 │
    /// │ [C] CONSUMABLE │ [EQUIP][DROP]   │ name / rarity / stats   │
    /// │                │ [CLOSE]         │ comparison / message    │
    /// └────────────────┴─────────────────┴─────────────────────────┘
    /// </code>
    ///
    /// Left: the five equipment slots as graphical slots with captions. Centre: the survivor's portrait (the player's
    /// idle sprite), the four ammo reserves and the three action buttons. Right: the 4×2 backpack grid and the details
    /// panel of the cursor slot (name, rarity as text, category, stats, affixes, Legendary special, comparison against
    /// the equipped item, the last action message). Pure presentation of <see cref="InventoryViewModel"/>: the mouse
    /// (hover/click/drag), the keyboard and the controller all move the same cursor and end in the same actions; the
    /// owner routes the focus list through its <c>MenuInput</c> stack.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        public const int ReferenceWidth = UiTheme.ScreenWidth;
        public const int ReferenceHeight = UiTheme.ScreenHeight;

        // ---- layout (reference pixels, top-left origin, 4 px grid) ----
        public static readonly UiRect Window = new(20, 12, 600, 336);
        public const int TitleHeight = 26;
        public static readonly UiRect EquipmentPanel = new(28, 44, 152, 296);
        public static readonly UiRect CharacterPanel = new(188, 44, 128, 296);
        public static readonly UiRect BackpackPanel = new(324, 44, 280, 124);
        public static readonly UiRect DetailsPanel = new(324, 176, 280, 164);
        public const int SlotSize = InventorySlotView.Size;
        public const int SlotGap = 8;
        public const int EquipmentRowPitch = 52;
        public const int BackpackColumns = 4;
        public const int DetailLines = 12;
        public const string ActionFocusId = "inventory.action";
        public const string DropFocusId = "inventory.drop";
        public const string CloseFocusId = "inventory.close";

        private InventoryViewModel _viewModel;
        private Canvas _canvas;
        private RectTransform _root;
        private GameObject _panel;
        private FocusList _list;
        private readonly List<InventorySlotView> _equipment = new();
        private readonly List<InventorySlotView> _backpack = new();
        private readonly List<Text> _equipmentCaptions = new();
        private readonly List<Text> _equipmentNames = new();
        private readonly List<Text> _equipmentRarities = new();
        private readonly List<Text> _ammoRows = new();
        private readonly List<Text> _detailRows = new();
        private readonly DetailPager _detailPager = new(DetailLines - 2);
        private InventorySlotRef? _detailCursor;
        private readonly Dictionary<string, UiControl> _buttons = new();
        private Text _coins;
        private Text _backpackHeader;
        private Text _detailTitle;
        private Text _detailSubtitle;
        private Text _message;
        private Text _hints;
        private Text _survivorName;
        private Image _portrait;
        private Sprite _portraitSprite;
        private bool _syncingFocus;
        private string _lastSlotFocusId = "slot.PrimaryWeapon";

        public InventoryViewModel ViewModel => _viewModel;
        public FocusList FocusList => _list;
        public bool IsVisible => _panel != null && _panel.activeSelf;
        public IReadOnlyList<InventorySlotView> EquipmentSlots => _equipment;
        public IReadOnlyList<InventorySlotView> BackpackSlots => _backpack;
        public IReadOnlyDictionary<string, UiControl> Buttons => _buttons;
        /// <summary>Caption + item line + rarity line of each equipment row (tests read these).</summary>
        public IReadOnlyList<string> EquipmentTexts => _equipmentCaptions.Select((c, i) => c.text + "\n" + _equipmentNames[i].text + "\n" + _equipmentRarities[i].text).ToList();
        /// <summary>The count text of each backpack slot ("" when empty or single).</summary>
        public IReadOnlyList<string> BackpackTexts => _backpack.Select(b => b.CountText).ToList();
        public string CoinsText => _coins != null ? _coins.text : string.Empty;
        public string MessageText => _message != null ? _message.text : string.Empty;
        public string DetailTitleText => _detailTitle != null ? _detailTitle.text : string.Empty;
        public string DetailSubtitleText => _detailSubtitle != null ? _detailSubtitle.text : string.Empty;
        /// <summary>The details panel as one string (title, subtitle, every stat row) — the successor of the old tooltip text.</summary>
        public string TooltipText => string.Join("\n", new[] { DetailTitleText, DetailSubtitleText }.Concat(_detailRows.Select(r => r.text)).Where(s => !string.IsNullOrEmpty(s)));
        public IReadOnlyList<string> DetailRowTexts => _detailRows.Select(r => r.text).ToList();
        /// <summary>The details pager: every composed row of the cursor item and the page in view (tests read it; the inputs step it).</summary>
        public DetailPager DetailPager => _detailPager;
        /// <summary>Steps the details page (mouse wheel / PageDown / right stick); public for the tests.</summary>
        public bool DetailsPageDown() { if (!_detailPager.PageDown()) return false; RenderDetails(); return true; }
        public bool DetailsPageUp() { if (!_detailPager.PageUp()) return false; RenderDetails(); return true; }
        public IReadOnlyList<string> AmmoTexts => _ammoRows.Select(r => r.text).ToList();
        public string HintsText => _hints != null ? _hints.text : string.Empty;
        public Sprite PortraitSprite => _portrait != null && _portrait.enabled ? _portrait.sprite : null;
        public int Renders { get; private set; }

        public static InventoryView Create(InventoryViewModel viewModel, string name = "InventoryUI")
        {
            var go = new GameObject(name);
            var view = go.AddComponent<InventoryView>();
            view.Build();
            view.Bind(viewModel);
            return view;
        }

        public void Bind(InventoryViewModel viewModel)
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
            _viewModel = viewModel;
            _list = BuildFocusList();
            foreach (var slot in _equipment.Concat(_backpack)) slot.BindList(_list);
            if (_viewModel != null)
            {
                _viewModel.Changed += Render;
                Render();
            }
        }

        /// <summary>The survivor portrait (the player's idle sprite): the composition root supplies it, the view never loads art.</summary>
        public void SetPortrait(Sprite sprite)
        {
            _portraitSprite = sprite;
            if (_portrait == null) return;
            _portrait.sprite = sprite;
            _portrait.enabled = sprite != null;
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
            _canvas.sortingOrder = 30; // above the run HUD and prompts (RunUi at 20): the window is the topmost layer while it is up
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // the 640×360 frame always fits; surplus becomes margin
            gameObject.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("ReferenceRoot", typeof(RectTransform));
            _root = (RectTransform)rootGo.transform;
            _root.SetParent(transform, false);
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.5f);
            _root.anchoredPosition = Vector2.zero;
            _root.sizeDelta = new Vector2(ReferenceWidth, ReferenceHeight);

            var skin = UiSkin.Load();
            // The dim swallows every click on the world beneath while the window is up.
            var dim = UiBuild.Plate(_root, ScreenLayout.Screen, UiTheme.WithAlpha(UiTheme.NearBlack, 0.62f), "Dim");
            dim.raycastTarget = true;
            _panel = dim.gameObject;

            var window = UiBuild.Panel(_panel.transform, Window, "Panel", UiTheme.WithAlpha(UiTheme.NearBlack, 0.97f), UiTheme.PanelEdge);
            UiBuild.Sliced(window.transform, new UiRect(0, 0, Window.Width, Window.Height), skin != null ? skin.PanelFrame : null, skin != null && skin.PanelFrame != null ? Color.white : new Color(0f, 0f, 0f, 0f), "Frame");
            BuildTitle(window.transform);
            BuildEquipment(window.transform, skin);
            BuildCharacter(window.transform, skin);
            BuildBackpack(window.transform, skin);
            BuildDetails(window.transform, skin);
            _panel.SetActive(false);
        }

        private static UiRect Local(UiRect panel) => new(panel.X - Window.X, panel.Y - Window.Y, panel.Width, panel.Height);

        private void BuildTitle(Transform window)
        {
            var bar = new UiRect(0, 0, Window.Width, TitleHeight);
            UiBuild.Plate(window, bar, UiTheme.ChromeBar, "TitleBar");
            UiBuild.Plate(window, new UiRect(0, TitleHeight - 1, Window.Width, 1), UiTheme.PanelEdge, "TitleRule");
            UiBuild.Label(window, "INVENTORY", new UiRect(UiTheme.PadLarge, 4, 130, UiText.LineHeight * 2), 2, TextAnchor.UpperLeft, UiTheme.Amber, false, "Title");
            _hints = UiBuild.Label(window, string.Empty, new UiRect(150, 9, 280, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkMuted, false, "Hints");
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

        private void BuildEquipment(Transform window, UiSkin skin)
        {
            var panel = SectionPanel(window, EquipmentPanel, "Equipment", "EQUIPMENT", out _);
            var y = 22;
            for (var i = 0; i < 5; i++)
            {
                var slotRef = new InventorySlotRef(InventorySlotKind.Equipped, i);
                var equipped = (EquippedSlot)i;
                var slot = CreateSlot(panel.transform, new UiRect(UiTheme.Pad, y, SlotSize, SlotSize), slotRef, "slot." + equipped, skin);
                _equipment.Add(slot);
                var textX = UiTheme.Pad + SlotSize + 6;
                var textWidth = EquipmentPanel.Width - textX - UiTheme.Pad;
                _equipmentCaptions.Add(UiBuild.Label(panel.transform, InventoryViewModel.SlotLabel(equipped), new UiRect(textX, y + 4, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "Caption" + i));
                _equipmentNames.Add(UiBuild.Label(panel.transform, string.Empty, new UiRect(textX, y + 16, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "ItemName" + i));
                _equipmentRarities.Add(UiBuild.Label(panel.transform, string.Empty, new UiRect(textX, y + 28, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkFaint, false, "ItemRarity" + i));
                y += EquipmentRowPitch;
            }
        }

        private void BuildCharacter(Transform window, UiSkin skin)
        {
            var panel = SectionPanel(window, CharacterPanel, "Character", "SURVIVOR", out _survivorName);
            var frame = new UiRect((CharacterPanel.Width - 96) / 2, 22, 96, 112);
            UiBuild.Sliced(panel.transform, frame, skin != null ? skin.InventorySlot : null, null, "PortraitFrame");
            // The idle sprite is 32×48 drawn at ×2; the frame is the stage it stands on.
            var portraitRect = UiBuild.NewRect(panel.transform, "Portrait", new UiRect(frame.X + (frame.Width - 64) / 2, frame.Y + (frame.Height - 96) / 2, 64, 96));
            _portrait = portraitRect.gameObject.AddComponent<Image>();
            _portrait.raycastTarget = false;
            _portrait.preserveAspect = true;
            _portrait.enabled = false;
            UiBuild.Plate(panel.transform, new UiRect(frame.X + 12, frame.Bottom - 8, frame.Width - 24, 2), UiTheme.WithAlpha(UiTheme.NearBlack, 0.8f), "Shadow");

            var ammoY = frame.Bottom + 8;
            UiBuild.Label(panel.transform, "AMMO", new UiRect(UiTheme.Pad, ammoY, CharacterPanel.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "AmmoHeader");
            for (var i = 0; i < 4; i++)
            {
                _ammoRows.Add(UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, ammoY + 11 + i * 10, CharacterPanel.Width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Ammo" + i));
            }

            var buttonY = ammoY + 11 + 4 * 10 + 8;
            var buttonWidth = CharacterPanel.Width - UiTheme.Pad * 2;
            foreach (var (id, label) in new[] { (ActionFocusId, "EQUIP"), (DropFocusId, "DROP"), (CloseFocusId, "CLOSE") })
            {
                _buttons[id] = BuildButton(panel.transform, new UiRect(UiTheme.Pad, buttonY, buttonWidth, 20), id, label);
                buttonY += 24;
            }
        }

        private void BuildBackpack(Transform window, UiSkin skin)
        {
            var panel = SectionPanel(window, BackpackPanel, "Backpack", "BACKPACK", out _backpackHeader);
            var gridWidth = BackpackColumns * SlotSize + (BackpackColumns - 1) * SlotGap;
            var x0 = (BackpackPanel.Width - gridWidth) / 2;
            for (var i = 0; i < InventoryViewModel.BackpackSlots; i++)
            {
                var row = i / BackpackColumns;
                var col = i % BackpackColumns;
                var bounds = new UiRect(x0 + col * (SlotSize + SlotGap), 22 + row * (SlotSize + SlotGap), SlotSize, SlotSize);
                _backpack.Add(CreateSlot(panel.transform, bounds, new InventorySlotRef(InventorySlotKind.Backpack, i), "backpack." + i, skin));
            }
        }

        private void BuildDetails(Transform window, UiSkin skin)
        {
            var panel = SectionPanel(window, DetailsPanel, "Details", "DETAILS", out _);
            var inner = DetailsPanel.Width - UiTheme.Pad * 2;
            _detailTitle = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 22, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "DetailTitle");
            _detailSubtitle = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 32, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "DetailSubtitle");
            UiBuild.Plate(panel.transform, new UiRect(UiTheme.Pad, 43, inner, 1), UiTheme.PanelEdgeSoft, "DetailRule");
            for (var i = 0; i < DetailLines - 2; i++)
            {
                _detailRows.Add(UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, 47 + i * 10, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "Detail" + i));
            }

            _message = UiBuild.Label(panel.transform, string.Empty, new UiRect(UiTheme.Pad, DetailsPanel.Height - UiTheme.Pad - UiText.LineHeight, inner, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Danger, false, "Message");
        }

        private InventorySlotView CreateSlot(Transform parent, UiRect bounds, InventorySlotRef slotRef, string focusId, UiSkin skin) =>
            InventorySlotView.Create(parent, bounds, slotRef, focusId, null,
                skin != null ? skin.InventorySlot : null, rarity => skin != null ? skin.RarityFrame(rarity) : null,
                OnSlotHovered, OnSlotClicked, OnSlotDropped, s => _viewModel?.IconOf(_viewModel.ItemAt(s)));

        private UiControl BuildButton(Transform parent, UiRect bounds, string id, string label)
        {
            // The same control the pause menu and the front-end use: plate, edges, focus brackets, pressed inset.
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
            control.Bind(null, null, ControlRole.Button, fill, text, edges, brackets, marker, null, null, bounds.Width, 1);
            return control;
        }

        // ---- focus list (keyboard / controller / mouse share it) ----

        private FocusList BuildFocusList()
        {
            var list = new FocusList("Inventory");
            if (_viewModel == null) return list;
            foreach (var slot in _viewModel.EquipmentSlots)
            {
                var slotRef = new InventorySlotRef(InventorySlotKind.Equipped, (int)slot);
                list.Add("slot." + slot, InventoryViewModel.SlotLabel(slot), () => ActivateSlot(slotRef));
            }

            for (var i = 0; i < InventoryViewModel.BackpackSlots; i++)
            {
                var slotRef = new InventorySlotRef(InventorySlotKind.Backpack, i);
                list.Add("backpack." + i, $"Backpack {i + 1}", () => ActivateSlot(slotRef));
            }

            list.Add(ActionFocusId, "EQUIP", () => _viewModel.PrimaryAction(), () => _viewModel.CanPrimaryAction);
            list.Add(DropFocusId, "DROP", () => _viewModel.Drop(_viewModel.Cursor), () => _viewModel.ItemAt(_viewModel.Cursor) != null);
            list.Add(CloseFocusId, "CLOSE", () => _viewModel.Close());
            list.Navigator = Navigate;
            list.FocusChanged += OnFocusChanged;

            foreach (var (id, control) in _buttons)
            {
                var item = list.Find(id);
                control.Bind(list, item, ControlRole.Button, control.GetComponent<Image>(), control.GetComponentInChildren<Text>(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Edge")).ToList(),
                    control.GetComponentsInChildren<Image>().Where(i => i.name.StartsWith("Bracket")).ToList(),
                    control.GetComponentsInChildren<Image>().First(i => i.name == "SelectedMarker"),
                    null, i => { list.Focus(i.Id); list.ActivateFocused(); }, control.Rect().Width, 1);
            }

            return list;
        }

        /// <summary>Two-dimensional stepping: slots follow the view model's cursor rule; below the last rows sit the action buttons.</summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null) return null;
            if (TryParseSlot(focused.Id, out var slot))
            {
                var next = InventoryViewModel.NextCursor(slot, direction);
                if (!next.Equals(slot)) return IdOf(next);
                return direction.y < 0 ? EnabledActions().FirstOrDefault() : null; // off the bottom of a column → the first usable action; other edges clamp
            }

            // Among the actions only the enabled ones are stops (a disabled EQUIP/DROP is skipped, CLOSE always exists): never a dead end.
            var actions = EnabledActions();
            var index = actions.IndexOf(focused.Id);
            if (index < 0) return null;
            if (direction.y > 0) return index == 0 ? _lastSlotFocusId : actions[index - 1];
            if (direction.y < 0) return index < actions.Count - 1 ? actions[index + 1] : null;
            return null;
        }

        private List<string> EnabledActions() => new[] { ActionFocusId, DropFocusId, CloseFocusId }.Where(id => _list?.Find(id)?.IsEnabled ?? false).ToList();

        private static string IdOf(InventorySlotRef slot) => slot.Kind == InventorySlotKind.Equipped ? "slot." + slot.EquippedSlot : "backpack." + slot.Index;

        public static bool TryParseSlot(string id, out InventorySlotRef slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(id)) return false;
            if (id.StartsWith("slot.") && Enum.TryParse<EquippedSlot>(id.Substring(5), out var equipped)) { slot = new InventorySlotRef(InventorySlotKind.Equipped, (int)equipped); return true; }
            if (id.StartsWith("backpack.") && int.TryParse(id.Substring(9), out var index)) { slot = new InventorySlotRef(InventorySlotKind.Backpack, index); return true; }
            return false;
        }

        private void OnFocusChanged(FocusItem focused)
        {
            if (_syncingFocus || focused == null || _viewModel == null) return;
            if (!TryParseSlot(focused.Id, out var slot)) return;
            _lastSlotFocusId = focused.Id;
            if (!_viewModel.Cursor.Equals(slot)) _viewModel.SetCursor(slot);
        }

        private void ActivateSlot(InventorySlotRef slot)
        {
            if (_viewModel == null) return;
            _viewModel.SetCursor(slot);
            _viewModel.Activate();
        }

        private void OnSlotHovered(InventorySlotRef slot)
        {
            // Hover moves the cursor: the details panel follows the pointer and a following arrow key continues from here.
            _list?.Focus(IdOf(slot));
            if (_viewModel != null && !_viewModel.Cursor.Equals(slot)) _viewModel.SetCursor(slot);
        }

        private void OnSlotClicked(InventorySlotRef slot)
        {
            _list?.Focus(IdOf(slot));
            ActivateSlot(slot);
        }

        private void OnSlotDropped(InventorySlotRef from, InventorySlotRef to)
        {
            if (_viewModel == null) return;
            _viewModel.CancelSelection();
            _viewModel.SetCursor(to);
            _viewModel.MoveTo(from, to);
        }

        // ---- rendering ----

        private void Render()
        {
            Renders++;
            if (_viewModel == null || _panel == null) return;
            if (_panel.activeSelf != _viewModel.IsOpen) _panel.SetActive(_viewModel.IsOpen);
            if (!_viewModel.IsOpen) return;

            SyncFocusToCursor();
            _coins.text = $"CARRIED COINS  {_viewModel.Coins}";
            var device = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            _hints.text = device ? "A: SELECT, MOVE   B: BACK   D-PAD: MOVE" : "CLICK/ENTER: SELECT, MOVE   ESC: CLOSE";
            if (_portrait != null) { _portrait.sprite = _portraitSprite; _portrait.enabled = _portraitSprite != null; }

            for (var i = 0; i < _equipment.Count; i++)
            {
                var slotRef = new InventorySlotRef(InventorySlotKind.Equipped, i);
                var item = _viewModel.ItemAt(slotRef);
                var definition = _viewModel.DefinitionOf(item);
                _equipment[i].Show(item, _viewModel.IconOf(item), definition == null || definition.IsStackable);
                _equipment[i].SetSelected(_viewModel.Selected.HasValue && _viewModel.Selected.Value.Equals(slotRef));
                var width = EquipmentPanel.Width - (UiTheme.Pad + SlotSize + 6) - UiTheme.Pad;
                if (item == null)
                {
                    _equipmentNames[i].text = "— empty —";
                    _equipmentNames[i].color = UiTheme.InkFaint;
                    _equipmentRarities[i].text = string.Empty;
                }
                else
                {
                    var style = RarityStyle.For(item.Rarity);
                    _equipmentNames[i].text = UiText.Fit(_viewModel.DisplayNameOf(item) + (item.Quantity > 1 ? " x" + item.Quantity : string.Empty), width);
                    _equipmentNames[i].color = UiTheme.Ink;
                    _equipmentRarities[i].text = style.Label;
                    _equipmentRarities[i].color = Readable(style.Color);
                }
            }

            var carried = 0;
            for (var i = 0; i < _backpack.Count; i++)
            {
                var slotRef = new InventorySlotRef(InventorySlotKind.Backpack, i);
                var item = _viewModel.ItemAt(slotRef);
                if (item != null) carried++;
                var definition = _viewModel.DefinitionOf(item);
                _backpack[i].Show(item, _viewModel.IconOf(item), definition == null || definition.IsStackable);
                _backpack[i].SetSelected(_viewModel.Selected.HasValue && _viewModel.Selected.Value.Equals(slotRef));
            }

            _backpackHeader.text = $"BACKPACK  {carried} / {InventoryViewModel.BackpackSlots}" + (_viewModel.IsBackpackFull ? "  — FULL" : string.Empty);
            _backpackHeader.color = _viewModel.IsBackpackFull ? UiTheme.Amber : UiTheme.InkMuted;

            var types = new[] { AmmoType.Light, AmmoType.Medium, AmmoType.Heavy, AmmoType.Shells };
            for (var i = 0; i < _ammoRows.Count; i++)
            {
                var cap = _viewModel.AmmoCap(types[i]);
                _ammoRows[i].text = $"{types[i].ToString().ToUpperInvariant(),-7}{_viewModel.AmmoReserve(types[i]),4}" + (cap > 0 ? $"/{cap}" : string.Empty);
            }

            _buttons[ActionFocusId].GetComponentInChildren<Text>().text = _viewModel.CanPrimaryAction ? _viewModel.PrimaryActionLabel : "EQUIP";
            RenderDetails();
        }

        /// <summary>Keeps the focus list on the view model's cursor slot (mouse/keyboard/controller agree on one cursor).</summary>
        private void SyncFocusToCursor()
        {
            if (_list == null) return;
            var id = IdOf(_viewModel.Cursor);
            if (_list.Focused != null && _list.Focused.Id == id) return;
            var focusedOnAction = _list.Focused != null && !TryParseSlot(_list.Focused.Id, out _);
            if (focusedOnAction) return; // the actions keep their focus; they act on the cursor slot
            _syncingFocus = true;
            _list.Focus(id);
            _lastSlotFocusId = id;
            _syncingFocus = false;
        }

        private void RenderDetails()
        {
            var cursor = _viewModel.Cursor;
            var tooltip = _viewModel.TooltipAt(cursor);
            foreach (var row in _detailRows) row.text = string.Empty;
            _message.text = _viewModel.Message;
            if (tooltip == null)
            {
                _detailPager.SetRows(null, false);
                _detailCursor = null;
                _detailTitle.text = cursor.Kind == InventorySlotKind.Equipped ? InventoryViewModel.SlotLabel(cursor.EquippedSlot) + " — EMPTY" : $"BACKPACK SLOT {cursor.Index + 1} — EMPTY";
                _detailTitle.color = UiTheme.InkMuted;
                _detailSubtitle.text = _viewModel.Selected.HasValue ? "Select a slot to move the picked item here." : "Select an item to see its details.";
                return;
            }

            var style = RarityStyle.For(tooltip.Rarity);
            var width = DetailsPanel.Width - UiTheme.Pad * 2;
            _detailTitle.text = UiText.Fit(tooltip.Name, width);
            _detailTitle.color = Readable(style.Color);
            var subtitle = style.Label + " · " + tooltip.CategoryText;
            if (tooltip.Quantity.HasValue) subtitle += $" · x{tooltip.Quantity.Value}";
            if (cursor.Kind == InventorySlotKind.Equipped) subtitle += " · EQUIPPED";
            if (tooltip.IsUnsellable) subtitle += " · STARTER";
            _detailSubtitle.text = UiText.Fit(subtitle, width);

            // Description first, then Legendary, stats, affixes and the comparison; the pager keeps the tail reachable.
            var sameItem = _detailCursor.HasValue && _detailCursor.Value.Equals(cursor);
            _detailCursor = cursor;
            _detailPager.SetRows(ItemDetailLayout.Compose(tooltip, _viewModel.CompareAt(cursor), width), sameItem);
            var visible = _detailPager.Visible(DetailPagingInput.Hint());
            for (var i = 0; i < _detailRows.Count && i < visible.Count; i++)
            {
                _detailRows[i].text = ItemDetailLayout.Render(visible[i], width);
                _detailRows[i].color = visible[i].Color;
            }
        }

        private void Update()
        {
            if (_panel == null || !_panel.activeSelf || !_detailPager.Overflows) return;
            var step = DetailPagingInput.Poll();
            if (step > 0) DetailsPageDown();
            else if (step < 0) DetailsPageUp();
        }

        /// <summary>Rarity colours are tuned for slot frames; text needs a floor on luminance to stay readable on the charcoal plate.</summary>
        private static Color Readable(Color color)
        {
            var luminance = 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
            return luminance < 0.45f ? Color.Lerp(color, Color.white, (0.45f - luminance) / 0.45f) : color;
        }
    }

    internal static class UiControlExtensions
    {
        public static UiRect Rect(this UiControl control)
        {
            var rect = (RectTransform)control.transform;
            return new UiRect(Mathf.RoundToInt(rect.anchoredPosition.x), Mathf.RoundToInt(-rect.anchoredPosition.y), Mathf.RoundToInt(rect.sizeDelta.x), Mathf.RoundToInt(rect.sizeDelta.y));
        }
    }
}
