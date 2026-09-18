using System.Collections.Generic;
using RuinRail.UI.Navigation;
using UnityEngine;

namespace RuinRail.UI.Theme
{
    /// <summary>
    /// Shows a long <see cref="FocusList"/> through a fixed number of rows, keeping the focused entry inside the
    /// window.
    ///
    /// A scrollbar would be the obvious alternative and the wrong one here: it is a pointer-only affordance, and the
    /// front-end has to work identically from a keyboard, a controller and a mouse. Scrolling by focus means the same
    /// step that moves the selection moves the window, so all three devices reach every entry the same way, and there
    /// is no widget that only one of them can grab.
    ///
    /// The row controls are reused rather than rebuilt: the window rebinds the same objects onto different items,
    /// which keeps the control count (and the hit-test set) constant however long the list is.
    /// </summary>
    public sealed class FocusWindow
    {
        private readonly FocusList _list;
        private readonly Dictionary<int, UiControl> _slots = new();

        public FocusWindow(FocusList list, int capacity)
        {
            _list = list;
            Capacity = Mathf.Max(1, capacity);
        }

        public int Capacity { get; }

        /// <summary>Index of the list entry shown in the first row.</summary>
        public int Offset { get; private set; }

        /// <summary>True when the list is longer than the window, i.e. the window actually scrolls.</summary>
        public bool Scrolls => _list != null && _list.Items.Count > Capacity;

        public void Register(int slot, UiControl control)
        {
            if (control == null) return;
            _slots[slot] = control;
        }

        /// <summary>
        /// Moves the window the smallest distance that brings the focused entry inside it, then repaints every row.
        /// </summary>
        public void Refresh()
        {
            if (_list == null || _slots.Count == 0) return;

            var count = _list.Items.Count;
            var focused = Mathf.Clamp(_list.FocusedIndex, 0, Mathf.Max(0, count - 1));

            var maxOffset = Mathf.Max(0, count - Capacity);
            if (focused < Offset) Offset = focused;
            else if (focused >= Offset + Capacity) Offset = focused - Capacity + 1;
            Offset = Mathf.Clamp(Offset, 0, maxOffset);

            foreach (var (slot, control) in _slots)
            {
                if (control == null) continue;
                var index = Offset + slot;
                var visible = index >= 0 && index < count;
                if (control.gameObject.activeSelf != visible) control.gameObject.SetActive(visible);
                if (visible) control.Rebind(_list.Items[index]);
            }
        }
    }

    /// <summary>Ticks a <see cref="FocusWindow"/> for as long as the panel that owns it exists.</summary>
    public sealed class FocusWindowDriver : MonoBehaviour
    {
        private FocusWindow _window;

        public FocusWindow Window => _window;

        public void Bind(FocusWindow window)
        {
            _window = window;
            _window?.Refresh();
        }

        private void LateUpdate() => _window?.Refresh();
    }
}
