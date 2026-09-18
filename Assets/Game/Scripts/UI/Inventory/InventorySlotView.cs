using System;
using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RuinRail.UI.Inventory
{
    /// <summary>
    /// One graphical inventory slot (92): the skin's sliced slot plate, the rarity frame when occupied, the item icon
    /// at an integer scale, the stack count, and the three state cues the player must be able to tell apart without
    /// colour — hover (lighter plate), focus (corner brackets, keyboard/controller) and selected (2 px inner frame:
    /// the item picked up for a move). Pointer enter/click/drag and the focus list both end in the same view-model
    /// calls, so a slot can never do one thing on Enter and another on click.
    /// </summary>
    public sealed class InventorySlotView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public const int Size = 44;
        public const int IconSize = 32;
        private const int Inset = 3;

        private InventorySlotRef _slot;
        private FocusList _list;
        private string _focusId;
        private Image _plate;
        private Image _rarity;
        private Image _hoverOverlay;
        private Image _selectedFill;
        private readonly List<Image> _selectedFrame = new();
        private readonly List<Image> _brackets = new();
        private Image _icon;
        private Image _emptyMark;
        private Image _countBack;
        private Text _count;
        private Sprite _slotSprite;
        private Func<int, Sprite> _rarityFrame;
        private Action<InventorySlotRef> _hovered;
        private Action<InventorySlotRef> _clicked;
        private Action<InventorySlotRef, InventorySlotRef> _dropped;
        private Func<InventorySlotRef, Sprite> _iconOf;
        private bool _isHovered;
        private bool _isPressed;
        private bool _occupied;

        public InventorySlotRef Slot => _slot;
        public string FocusId => _focusId;
        public bool IsHovered => _isHovered;
        public bool IsPressed => _isPressed;
        public bool IsOccupied => _occupied;
        public bool ShowsFocusBrackets => _brackets.Count > 0 && _brackets[0].enabled;
        public bool ShowsSelectedFrame => _selectedFrame.Count > 0 && _selectedFrame[0].enabled;
        public bool ShowsHover => _isHovered && _hoverOverlay != null && _hoverOverlay.enabled;
        public Sprite IconSprite => _icon != null && _icon.enabled ? _icon.sprite : null;
        public Sprite FrameSprite => _rarity != null && _rarity.enabled ? _rarity.sprite : null;
        public string CountText => _count != null && _count.enabled ? _count.text : string.Empty;
        public int PointerActivations { get; private set; }
        public RectTransform Rect => (RectTransform)transform;

        /// <summary>Builds a slot at <paramref name="bounds"/> (reference pixels, top-left origin) under <paramref name="parent"/>.</summary>
        public static InventorySlotView Create(Transform parent, UiRect bounds, InventorySlotRef slot, string focusId, FocusList list,
            Sprite slotSprite, Func<int, Sprite> rarityFrame,
            Action<InventorySlotRef> hovered, Action<InventorySlotRef> clicked, Action<InventorySlotRef, InventorySlotRef> dropped, Func<InventorySlotRef, Sprite> iconOf)
        {
            var rect = UiBuild.NewRect(parent, "Slot:" + focusId, bounds);
            var view = rect.gameObject.AddComponent<InventorySlotView>();
            view._slot = slot;
            view._focusId = focusId;
            view._list = list;
            view._slotSprite = slotSprite;
            view._rarityFrame = rarityFrame;
            view._hovered = hovered;
            view._clicked = clicked;
            view._dropped = dropped;
            view._iconOf = iconOf;
            view.Build(bounds.Width, bounds.Height);
            if (list != null) list.FocusChanged += view.OnFocusChanged;
            return view;
        }

        private void Build(int width, int height)
        {
            var inner = new UiRect(0, 0, width, height);
            // The plate is the raycast target: the whole visible slot is the click/hover/drop area, nothing outside it.
            _plate = UiBuild.Sliced(transform, inner, _slotSprite, null, "Plate");
            _plate.raycastTarget = true;
            _rarity = UiBuild.Sliced(transform, inner, null, Color.white, "RarityFrame");
            _rarity.enabled = false;
            _hoverOverlay = UiBuild.Plate(transform, inner.Inset(Inset), new Color(1f, 1f, 1f, 0.08f), "Hover");
            _hoverOverlay.enabled = false;
            _selectedFill = UiBuild.Plate(transform, inner.Inset(Inset), UiTheme.WithAlpha(UiTheme.Amber, 0.14f), "SelectedFill");
            _selectedFill.enabled = false;
            _selectedFrame.AddRange(UiBuild.Border(transform, inner.Inset(2), UiTheme.Amber, 2));
            foreach (var edge in _selectedFrame) edge.enabled = false;
            _emptyMark = UiBuild.Plate(transform, new UiRect((width - 4) / 2, (height - 4) / 2, 4, 4), UiTheme.InkDisabled, "EmptyMark");

            var icon = UiBuild.NewRect(transform, "Icon", new UiRect((width - IconSize) / 2, (height - IconSize) / 2, IconSize, IconSize));
            _icon = icon.gameObject.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
            _icon.enabled = false;

            _countBack = UiBuild.Plate(transform, new UiRect(width - Inset - 26, height - Inset - UiText.LineHeight, 26, UiText.LineHeight), UiTheme.WithAlpha(UiTheme.NearBlack, 0.78f), "CountBack");
            _countBack.enabled = false;
            _count = UiBuild.Label(transform, string.Empty, new UiRect(width - Inset - 26, height - Inset - UiText.LineHeight, 26, UiText.LineHeight), 1, TextAnchor.UpperRight, UiTheme.Ink, false, "Count");
            _count.enabled = false;

            _brackets.AddRange(UiBuild.Brackets(transform, inner, UiTheme.Amber));
            foreach (var bracket in _brackets) bracket.enabled = false;
        }

        /// <summary>Points the slot at the focus list that owns the keyboard/controller cursor (rebinding replaces the previous list).</summary>
        public void BindList(FocusList list)
        {
            if (_list != null) _list.FocusChanged -= OnFocusChanged;
            _list = list;
            if (_list != null) _list.FocusChanged += OnFocusChanged;
            RefreshFocus();
        }

        private void OnDestroy()
        {
            if (_list != null) _list.FocusChanged -= OnFocusChanged;
        }

        private void OnFocusChanged(FocusItem _) => RefreshFocus();

        public bool IsFocused => _list != null && _list.Focused != null && _list.Focused.Id == _focusId;

        /// <summary>Draws the slot for an item (null = empty): frame by rarity, the definition's icon, the stack count.</summary>
        public void Show(ItemInstance item, Sprite icon, bool stackable)
        {
            _occupied = item != null;
            var frame = _occupied ? _rarityFrame?.Invoke((int)item.Rarity) : null;
            _rarity.enabled = frame != null;
            if (frame != null) { _rarity.sprite = frame; _rarity.type = Image.Type.Sliced; _rarity.color = Color.white; }
            _icon.enabled = _occupied && icon != null;
            _icon.sprite = icon;
            _emptyMark.enabled = !_occupied;
            var count = _occupied && stackable && item.Quantity > 1;
            _count.enabled = count;
            _countBack.enabled = count;
            if (count) _count.text = "x" + item.Quantity;
            RefreshFocus();
        }

        public void SetSelected(bool selected)
        {
            foreach (var edge in _selectedFrame) edge.enabled = selected;
            _selectedFill.enabled = selected;
        }

        private void RefreshFocus()
        {
            var focused = IsFocused;
            foreach (var bracket in _brackets) bracket.enabled = focused;
            _hoverOverlay.enabled = _isHovered || _isPressed || focused;
            _hoverOverlay.color = _isPressed ? new Color(0f, 0f, 0f, 0.25f) : focused ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.08f);
        }

        // ---- pointer ----

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isHovered = true;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            CursorService.SetHover(true);
            _hovered?.Invoke(_slot);
            RefreshFocus();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_isHovered) CursorService.SetHover(false);
            _isHovered = false;
            _isPressed = false;
            RefreshFocus();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _isPressed = true;
            RefreshFocus();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _isPressed = false;
            RefreshFocus();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            if (eventData.dragging) return; // a drag that ended here is a drop, not a click
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            PointerActivations++;
            _clicked?.Invoke(_slot);
        }

        // ---- drag and drop (pointer only; keyboard/controller use select → move on the same view-model path) ----

        public static InventorySlotView Dragging { get; private set; }
        private static Image _ghost;

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_occupied) { eventData.pointerDrag = null; return; }
            Dragging = this;
            var sprite = _iconOf?.Invoke(_slot);
            var canvas = GetComponentInParent<Canvas>();
            if (_ghost == null && canvas != null)
            {
                var go = new GameObject("DragGhost", typeof(RectTransform));
                go.transform.SetParent(canvas.transform, false);
                _ghost = go.AddComponent<Image>();
                _ghost.raycastTarget = false; // the drop target must stay reachable under the ghost
                _ghost.preserveAspect = true;
                ((RectTransform)go.transform).sizeDelta = new Vector2(IconSize, IconSize);
            }

            if (_ghost != null)
            {
                _ghost.sprite = sprite;
                _ghost.enabled = sprite != null;
                _ghost.transform.SetAsLastSibling();
                MoveGhost(eventData);
            }
        }

        public void OnDrag(PointerEventData eventData) => MoveGhost(eventData);

        private void MoveGhost(PointerEventData eventData)
        {
            if (_ghost == null) return;
            var canvas = _ghost.GetComponentInParent<Canvas>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)canvas.transform, eventData.position, canvas.worldCamera, out var local);
            ((RectTransform)_ghost.transform).anchoredPosition = local;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_ghost != null) _ghost.enabled = false;
            Dragging = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var source = Dragging;
            if (source == null || source == this) return;
            _dropped?.Invoke(source._slot, _slot);
        }

        // ---- test seams (drive the same handlers without a pointer device) ----

        public void SimulateHover(bool entering)
        {
            var data = new PointerEventData(EventSystem.current);
            if (entering) OnPointerEnter(data); else OnPointerExit(data);
        }

        public void SimulateClick() => OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });

        public void SimulateDrop(InventorySlotView source)
        {
            Dragging = source;
            OnDrop(new PointerEventData(EventSystem.current));
            Dragging = null;
        }
    }
}
