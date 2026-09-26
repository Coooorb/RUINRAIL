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
    /// The Shelter LOADOUT station body (94): the stash's graphical language inside the station panel — five worn
    /// slots with captions, the 4×2 backpack grid under them, and one strip saying what the pointed-at item is and what
    /// activating it does (or which rule stops it). Click / Enter / A picks an item up and places it on the slot chosen
    /// next (worn ⇄ backpack swaps in place), dragging does the same, EQUIP / UNEQUIP is the one-press shortcut, and a
    /// picked-up item marks the worn slots it fits. Every move is the bound <see cref="InventoryViewModel"/>'s; the view
    /// owns no item and no rule.
    /// </summary>
    public sealed class LoadoutPanelView : MonoBehaviour
    {
        public const string ActionId = "loadout.action";
        public const int SlotSize = InventorySlotView.Size;
        public const int WornPitch = SlotSize + 6;
        public const int BackpackColumns = 4;
        public const int BackpackPitch = SlotSize + 8;
        public const int StripHeight = 50;
        /// <summary>Height the body needs: worn row + captions, backpack header + two rows, the details strip.</summary>
        public const int RequiredHeight = 240;
        private const int GearHeight = 186;

        private static readonly string[] WornCaptions = { "PRIMARY", "SECOND", "ARMOR", "ACCESS.", "USE" };

        private InventoryViewModel _viewModel;
        private FocusList _list;
        private int _width;
        private int _height;
        private readonly List<InventorySlotView> _worn = new();
        private readonly List<InventorySlotView> _backpack = new();
        private readonly List<Image> _wornTargets = new();
        private readonly Dictionary<string, Vector2> _centres = new();
        private Text _backpackHeader;
        private Image _detailIcon;
        private Image _detailFrame;
        private Text _detailTitle;
        private Text _detailSubtitle;
        private Text _action;
        private UiControl _actionButton;
        private string _seenMessage = string.Empty;
        private InventorySlotRef _messageCursor;
        private Text _actionButtonLabel;

        public FocusList FocusList => _list;
        public IReadOnlyList<InventorySlotView> WornSlots => _worn;
        public IReadOnlyList<InventorySlotView> BackpackSlots => _backpack;
        public UiControl ActionButton => _actionButton;
        public string ActionButtonText => _actionButtonLabel != null ? _actionButtonLabel.text : string.Empty;
        public string ActionText => _action != null ? _action.text : string.Empty;
        public Color ActionColor => _action != null ? _action.color : Color.clear;
        public string DetailTitleText => _detailTitle != null ? _detailTitle.text : string.Empty;
        public string DetailSubtitleText => _detailSubtitle != null ? _detailSubtitle.text : string.Empty;
        public string BackpackHeaderText => _backpackHeader != null ? _backpackHeader.text : string.Empty;
        /// <summary>True while the worn slot is marked as a place the picked-up (or cursor) item can go.</summary>
        public bool IsMarkedTarget(EquippedSlot slot) => _wornTargets[(int)slot].enabled;

        public static string IdOf(InventorySlotRef cell) => cell.Kind == InventorySlotKind.Equipped ? "slot." + cell.EquippedSlot : "backpack." + cell.Index;

        public static bool TryParse(string id, out InventorySlotRef cell)
        {
            cell = default;
            if (string.IsNullOrEmpty(id)) return false;
            if (id.StartsWith("backpack.", System.StringComparison.Ordinal) && int.TryParse(id.Substring(9), out var index))
            {
                cell = new InventorySlotRef(InventorySlotKind.Backpack, index);
                return true;
            }

            if (id.StartsWith("slot.", System.StringComparison.Ordinal) && System.Enum.TryParse<EquippedSlot>(id.Substring(5), out var slot))
            {
                cell = new InventorySlotRef(InventorySlotKind.Equipped, (int)slot);
                return true;
            }

            return false;
        }

        /// <summary>Builds the body at <paramref name="region"/> (parent-local reference pixels) over the station's focus list.</summary>
        public static LoadoutPanelView Create(Transform parent, UiRect region, InventoryViewModel viewModel, FocusList list)
        {
            var rect = UiBuild.NewRect(parent, "LoadoutBody", region);
            var view = rect.gameObject.AddComponent<LoadoutPanelView>();
            view._viewModel = viewModel;
            view._list = list;
            view._width = region.Width;
            view._height = region.Height;
            view.Build();
            foreach (var slot in view._worn.Concat(view._backpack)) slot.BindList(list);
            list.Navigator = view.Navigate;
            list.FocusChanged += view.OnFocusChanged;
            viewModel.Changed += view.Render;
            list.Focus(IdOf(new InventorySlotRef(InventorySlotKind.Equipped, 0)));
            view.OnFocusChanged(list.Focused);
            view.Render();
            return view;
        }

        private void OnDestroy()
        {
            if (_viewModel != null) _viewModel.Changed -= Render;
            if (_list != null) _list.FocusChanged -= OnFocusChanged;
        }

        // ---------------------------------------------------------------- construction

        private void Build()
        {
            var skin = UiSkin.Load();
            // One opaque backing for worn + backpack (the stash's section panel), so the Shelter never shows through the grid.
            UiBuild.Panel(transform, new UiRect(0, 0, _width, GearHeight), "Gear", UiTheme.WithAlpha(UiTheme.Charcoal, 0.94f), UiTheme.PanelEdgeSoft);
            UiBuild.Label(transform, "WORN", new UiRect(UiTheme.PadSmall + 2, 3, 80, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, "WornHeader");
            var wornWidth = WornCaptions.Length * WornPitch - (WornPitch - SlotSize);
            var wornX = (_width - wornWidth) / 2;
            for (var i = 0; i < WornCaptions.Length; i++)
            {
                var bounds = new UiRect(wornX + i * WornPitch, 14, SlotSize, SlotSize);
                _worn.Add(CreateSlot(bounds, new InventorySlotRef(InventorySlotKind.Equipped, i), skin));
                // Where the picked-up item can go: a bright bar under every worn slot it fits.
                var target = UiBuild.Plate(transform, new UiRect(bounds.X + 4, bounds.Bottom + 1, SlotSize - 8, 2), UiTheme.Terminal, "WornTarget" + i);
                target.enabled = false;
                _wornTargets.Add(target);
                var caption = UiBuild.Label(transform, WornCaptions[i], new UiRect(bounds.X - 3, bounds.Bottom + 4, SlotSize + 6, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.InkFaint, false, "WornCaption" + i);
                caption.alignment = TextAnchor.UpperCenter;
            }

            _backpackHeader = UiBuild.Label(transform, string.Empty, new UiRect(UiTheme.PadSmall + 2, 77, _width - UiTheme.Pad * 2, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Amber, false, "BackpackHeader");
            var gridWidth = BackpackColumns * BackpackPitch - (BackpackPitch - SlotSize);
            var gridX = (_width - gridWidth) / 2;
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
            {
                var bounds = new UiRect(gridX + i % BackpackColumns * BackpackPitch, 89 + i / BackpackColumns * (SlotSize + 6), SlotSize, SlotSize);
                _backpack.Add(CreateSlot(bounds, new InventorySlotRef(InventorySlotKind.Backpack, i), skin));
            }

            BuildDetails(Mathf.Max(GearHeight + 4, _height - StripHeight));
        }

        private void BuildDetails(int top)
        {
            var strip = UiBuild.Panel(transform, new UiRect(0, top, _width, StripHeight), "Details", UiTheme.WithAlpha(UiTheme.Charcoal, 0.9f), UiTheme.PanelEdgeSoft).transform;
            _detailFrame = UiBuild.Plate(strip, new UiRect(5, 5, 34, 34), UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "DetailIconFrame");
            var iconRect = UiBuild.NewRect(strip, "DetailIcon", new UiRect(6, 6, 32, 32));
            _detailIcon = iconRect.gameObject.AddComponent<Image>();
            _detailIcon.raycastTarget = false;
            _detailIcon.preserveAspect = true;
            _detailIcon.enabled = false;
            var textWidth = _width - 46 - ActionButtonWidth - 8;
            _detailTitle = UiBuild.Label(strip, string.Empty, new UiRect(46, 5, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Ink, false, "DetailTitle");
            _detailSubtitle = UiBuild.Label(strip, string.Empty, new UiRect(46, 16, textWidth, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, false, "DetailSubtitle");
            _action = UiBuild.Label(strip, string.Empty, new UiRect(46, 32, _width - 52, UiText.LineHeight), 1, TextAnchor.UpperLeft, UiTheme.Terminal, false, "Action");
            BuildActionButton(strip, new UiRect(_width - ActionButtonWidth - 5, 5, ActionButtonWidth, 18));
            _centres[ActionId] = new Vector2(_width - ActionButtonWidth / 2f - 5, top + 14);
        }

        private const int ActionButtonWidth = 64;

        private void BuildActionButton(Transform parent, UiRect bounds)
        {
            var rect = UiBuild.NewRect(parent, "Control:" + ActionId, bounds);
            var fill = rect.gameObject.AddComponent<Image>();
            fill.raycastTarget = true;
            var inner = new UiRect(0, 0, bounds.Width, bounds.Height);
            var edges = UiBuild.Border(rect, inner, UiTheme.PanelEdgeSoft);
            var marker = UiBuild.Plate(rect, new UiRect(0, 0, 2, bounds.Height), UiTheme.Amber, "SelectedMarker");
            marker.enabled = false;
            var brackets = UiBuild.Brackets(rect, inner, UiTheme.Amber);
            _actionButtonLabel = UiBuild.Label(rect, string.Empty, new UiRect(0, (bounds.Height - UiText.LineHeight) / 2, bounds.Width, UiText.LineHeight), 1, TextAnchor.UpperCenter, UiTheme.Ink, false, "Label");
            _actionButtonLabel.alignment = TextAnchor.UpperCenter;
            _actionButton = rect.gameObject.AddComponent<UiControl>();
            _actionButton.Bind(_list, _list.Find(ActionId), ControlRole.Primary, fill, _actionButtonLabel, edges, brackets, marker, null,
                i => { _list.Focus(i.Id); _list.ActivateFocused(); }, bounds.Width, 1);
        }

        private InventorySlotView CreateSlot(UiRect bounds, InventorySlotRef cell, UiSkin skin)
        {
            var slot = InventorySlotView.Create(transform, bounds, cell, IdOf(cell), null,
                skin != null ? skin.InventorySlot : null, rarity => skin != null ? skin.RarityFrame(rarity) : null,
                OnHovered, OnClicked, OnDropped, c => IconOf(_viewModel?.ItemAt(c)));
            _centres[IdOf(cell)] = new Vector2(bounds.X + bounds.Width * 0.5f, bounds.Y + bounds.Height * 0.5f);
            return slot;
        }

        private Sprite IconOf(ItemInstance item) => _viewModel?.DefinitionOf(item)?.Icon;

        // ---------------------------------------------------------------- focus (keyboard / controller / mouse share it)

        /// <summary>Two-dimensional steps: the nearest enabled stop in the pressed direction, the cross axis weighed double.</summary>
        private string Navigate(FocusItem focused, Vector2Int direction)
        {
            if (focused == null || !_centres.TryGetValue(focused.Id, out var from)) return null;
            var step = new Vector2(direction.x, -direction.y);
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
            if (focused == null || !TryParse(focused.Id, out var cell)) return;
            if (!_viewModel.Cursor.Equals(cell)) _viewModel.SetCursor(cell);
        }

        private void OnHovered(InventorySlotRef cell) => _list.Focus(IdOf(cell));

        private void OnClicked(InventorySlotRef cell)
        {
            _list.Focus(IdOf(cell));
            _list.ActivateFocused();
        }

        private void OnDropped(InventorySlotRef from, InventorySlotRef to)
        {
            _viewModel.CancelSelection();
            _list.Focus(IdOf(to));
            _viewModel.MoveTo(from, to);
        }

        // ---------------------------------------------------------------- rendering

        private void Render()
        {
            if (_viewModel == null || _backpackHeader == null) return;
            foreach (var slot in _worn.Concat(_backpack)) Show(slot);

            var count = PlayerInventory.BackpackCapacity - Enumerable.Range(0, PlayerInventory.BackpackCapacity).Count(i => _viewModel.ItemAt(new InventorySlotRef(InventorySlotKind.Backpack, i)) == null);
            var full = _viewModel.IsBackpackFull;
            _backpackHeader.text = $"BACKPACK  {count}/{PlayerInventory.BackpackCapacity}" + (full ? "  FULL" : string.Empty);
            _backpackHeader.color = full ? UiTheme.Danger : UiTheme.Amber;

            RenderTargets();
            RenderDetails();
            _actionButtonLabel.text = _viewModel.CanPrimaryAction ? _viewModel.PrimaryActionLabel : "EQUIP";
            _actionButton.Refresh();
        }

        private void Show(InventorySlotView slot)
        {
            var item = _viewModel.ItemAt(slot.Slot);
            var definition = _viewModel.DefinitionOf(item);
            slot.Show(item, IconOf(item), definition == null || definition.IsStackable);
            slot.SetSelected(_viewModel.Selected.HasValue && _viewModel.Selected.Value.Equals(slot.Slot) && item != null);
        }

        /// <summary>A picked-up item marks every worn slot it fits; otherwise the backpack item under the cursor marks the slot EQUIP would fill.</summary>
        private void RenderTargets()
        {
            foreach (var target in _wornTargets) target.enabled = false;
            if (_viewModel.Selected.HasValue)
            {
                var picked = _viewModel.DefinitionOf(_viewModel.ItemAt(_viewModel.Selected.Value));
                if (picked == null) return;
                for (var i = 0; i < _wornTargets.Count; i++)
                    _wornTargets[i].enabled = PlayerInventory.IsSlotCompatible(picked.Category, (EquippedSlot)i) && !_viewModel.Selected.Value.Equals(new InventorySlotRef(InventorySlotKind.Equipped, i));
                return;
            }

            var cursorItem = _viewModel.ItemAt(_viewModel.Cursor);
            if (cursorItem == null || _viewModel.Cursor.Kind != InventorySlotKind.Backpack) return;
            var slot = _viewModel.DefaultSlotFor(cursorItem);
            if (slot.HasValue) _wornTargets[(int)slot.Value].enabled = true;
        }

        private void RenderDetails()
        {
            var cell = _viewModel.Cursor;
            var item = _viewModel.ItemAt(cell);
            var gamepad = RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad;
            var verb = gamepad ? "A" : "CLICK / ENTER";
            var textWidth = _width - 46 - ActionButtonWidth - 8;

            if (item == null)
            {
                _detailIcon.enabled = false;
                _detailFrame.color = UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f);
                _detailTitle.text = EmptyTitle(cell);
                _detailTitle.color = UiTheme.InkMuted;
                _detailSubtitle.text = string.Empty;
            }
            else
            {
                var definition = _viewModel.DefinitionOf(item);
                var style = RarityStyle.For(item.Rarity);
                _detailIcon.sprite = definition != null ? definition.Icon : null;
                _detailIcon.enabled = _detailIcon.sprite != null;
                _detailFrame.color = UiTheme.WithAlpha(style.Color, 0.85f);
                _detailTitle.text = UiText.Fit(_viewModel.DisplayNameOf(item) + (item.Quantity > 1 ? "  x" + item.Quantity : string.Empty), textWidth);
                _detailTitle.color = item.Rarity == Rarity.Common ? UiTheme.Ink : Color.Lerp(style.Color, Color.white, 0.25f);
                var where = cell.Kind == InventorySlotKind.Equipped ? "WORN" : "BACKPACK";
                _detailSubtitle.text = UiText.Fit($"{style.Label} · {(definition != null ? definition.Category.ToString().ToUpperInvariant() : "ITEM")} · {where}", textWidth);
            }

            // One line: the refusal that just happened (until the cursor moves on), else what activating does now.
            if (_viewModel.Message != _seenMessage)
            {
                _seenMessage = _viewModel.Message;
                _messageCursor = cell;
            }

            if (!string.IsNullOrEmpty(_viewModel.Message) && _messageCursor.Equals(cell))
            {
                _action.text = UiText.Fit(_viewModel.Message, _width - 52);
                _action.color = UiTheme.Danger;
                return;
            }

            _action.color = UiTheme.Terminal;
            if (_viewModel.Selected.HasValue)
            {
                // The selected frame shows what is moving and the green bars where it fits; the line only says what to do.
                _action.text = UiText.Fit($"CHOOSE A SLOT · {(gamepad ? "B" : "ESC")}: CANCEL", _width - 52);
                return;
            }

            if (item == null)
            {
                _action.text = cell.Kind == InventorySlotKind.Equipped ? "Pick a backpack item, then this slot." : "Free slot.";
                _action.color = UiTheme.InkMuted;
                return;
            }

            if (cell.Kind == InventorySlotKind.Equipped && _viewModel.IsBackpackFull)
            {
                _action.text = UiText.Fit("BACKPACK FULL: SWAP WITH A BAG ITEM", _width - 52);
                _action.color = UiTheme.Amber;
                return;
            }

            _action.text = UiText.Fit($"{verb}: PICK UP   DRAG: MOVE", _width - 52);
        }

        private static string EmptyTitle(InventorySlotRef cell) => cell.Kind == InventorySlotKind.Equipped
            ? WornCaptions[Mathf.Clamp(cell.Index, 0, WornCaptions.Length - 1)] + " — EMPTY"
            : $"BACKPACK {cell.Index + 1} — EMPTY";

        // ---------------------------------------------------------------- test seams

        public InventorySlotView SlotFor(InventorySlotRef cell) => cell.Kind == InventorySlotKind.Equipped ? _worn[cell.Index] : _backpack[cell.Index];
    }
}
