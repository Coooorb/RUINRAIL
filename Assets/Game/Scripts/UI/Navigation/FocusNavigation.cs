using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.UI.Navigation
{
    /// <summary>One focusable control of a screen: label for the view, enabled state read live, activation = the view model's action.</summary>
    public sealed class FocusItem
    {
        public FocusItem(string id, string label, Action activate, Func<bool> isEnabled = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Label = label ?? id;
            Activate = activate;
            _isEnabled = isEnabled;
        }

        private readonly Func<bool> _isEnabled;

        public string Id { get; }
        public string Label { get; }
        public Action Activate { get; }
        public bool IsEnabled => _isEnabled?.Invoke() ?? true;
        public int Activations { get; private set; }

        public bool TryActivate()
        {
            if (!IsEnabled || Activate == null) return false;
            Activations++;
            Activate();
            return true;
        }
    }

    /// <summary>
    /// The ordered controls of one panel: wrap-around navigation (up/down or left/right, both map to ±1; grids may use
    /// a column count), skipping disabled controls; the mouse sets focus directly by id. Nothing here is pointer-only:
    /// every control is reached by stepping.
    /// </summary>
    public sealed class FocusList
    {
        private readonly List<FocusItem> _items = new();
        private int _index;

        public FocusList(string name, int columns = 1)
        {
            Name = name ?? "panel";
            Columns = Math.Max(1, columns);
        }

        public string Name { get; }
        public int Columns { get; }
        public IReadOnlyList<FocusItem> Items => _items;
        public FocusItem Focused => _items.Count == 0 ? null : _items[Math.Clamp(_index, 0, _items.Count - 1)];
        public int FocusedIndex => _items.Count == 0 ? -1 : Math.Clamp(_index, 0, _items.Count - 1);

        public event Action<FocusItem> FocusChanged;

        /// <summary>
        /// Optional two-dimensional navigation: given the focused item and a direction (x right, y up), the id of the
        /// item to focus, or null to stay. A panel that is not a plain list (the inventory's equipment column beside
        /// its backpack grid) sets this; the menu input then routes every arrow/D-pad step here and never turns a
        /// left/right press into a section change.
        /// </summary>
        public Func<FocusItem, Vector2Int, string> Navigator { get; set; }

        public bool HasNavigator => Navigator != null;

        /// <summary>One directional step: through the navigator when the panel has one, otherwise ±1 for vertical steps only.</summary>
        public bool Navigate(Vector2Int direction)
        {
            if (Navigator == null) return direction.y != 0 && Move(-direction.y);
            var target = Navigator(Focused, direction);
            return !string.IsNullOrEmpty(target) && Focus(target);
        }

        public FocusList Add(string id, string label, Action activate, Func<bool> isEnabled = null)
        {
            _items.Add(new FocusItem(id, label, activate, isEnabled));
            return this;
        }

        public FocusItem Find(string id) => _items.FirstOrDefault(i => i.Id == id);

        /// <summary>Steps through the list (wrapping) to the next enabled control; returns false when nothing else is enabled.</summary>
        public bool Move(int delta)
        {
            if (_items.Count == 0) return false;
            var start = FocusedIndex;
            var index = start;
            for (var n = 0; n < _items.Count; n++)
            {
                index = ((index + delta) % _items.Count + _items.Count) % _items.Count;
                if (_items[index].IsEnabled && index != start)
                {
                    SetIndex(index);
                    return true;
                }
            }

            return false;
        }

        public bool MoveNext() => Move(+1);
        public bool MovePrevious() => Move(-1);
        /// <summary>Vertical step in a grid (or ±1 in a list).</summary>
        public bool MoveDown() => Move(Columns);
        public bool MoveUp() => Move(-Columns);

        /// <summary>Pointer hover / click: focus by id (disabled controls are not focusable).</summary>
        public bool Focus(string id)
        {
            var index = _items.FindIndex(i => i.Id == id);
            if (index < 0 || !_items[index].IsEnabled) return false;
            SetIndex(index);
            return true;
        }

        /// <summary>Keeps focus valid when the focused control became disabled or vanished (never a dead end).</summary>
        public void EnsureValid()
        {
            if (_items.Count == 0) return;
            if (Focused != null && Focused.IsEnabled) return;
            Move(+1);
        }

        public bool ActivateFocused()
        {
            EnsureValid();
            return Focused != null && Focused.TryActivate();
        }

        private void SetIndex(int index)
        {
            if (_index == index) return;
            _index = index;
            FocusChanged?.Invoke(Focused);
        }
    }

    /// <summary>
    /// Panel stack with predictable focus restoration: opening a dialog/panel pushes its list; closing (or a panel being
    /// destroyed under the stack) pops it and focus returns to exactly the control the parent had focused. Focus can
    /// never rest behind a closed panel — the top of the stack is always the live focus owner.
    /// </summary>
    public sealed class FocusStack
    {
        private readonly List<(FocusList list, string restoreId)> _stack = new();

        public FocusList Current => _stack.Count == 0 ? null : _stack[^1].list;
        public int Depth => _stack.Count;
        public FocusItem Focused => Current?.Focused;
        public string CurrentPanel => Current?.Name ?? string.Empty;

        public event Action<FocusList> PanelChanged;

        public void Push(FocusList list)
        {
            if (list == null) throw new ArgumentNullException(nameof(list));
            var parentFocus = Current?.Focused?.Id;
            _stack.Add((list, parentFocus));
            list.EnsureValid();
            PanelChanged?.Invoke(list);
        }

        /// <summary>Closes the top panel and restores the parent's focus (the control it had before the push).</summary>
        public FocusList Pop()
        {
            if (_stack.Count == 0) return null;
            var (closed, restoreId) = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            var parent = Current;
            if (parent != null)
            {
                if (restoreId == null || !parent.Focus(restoreId)) parent.EnsureValid();
            }

            PanelChanged?.Invoke(parent);
            return closed;
        }

        /// <summary>A panel closed/destroyed out of order (e.g. a station panel while a dialog sat above it): it and everything above it go, focus returns below it.</summary>
        public void Remove(FocusList list)
        {
            var index = _stack.FindIndex(e => e.list == list);
            if (index < 0) return;
            while (_stack.Count > index) Pop();
        }

        /// <summary>True when the panel is anywhere on the stack (not necessarily on top).</summary>
        public bool Contains(FocusList list) => list != null && _stack.Exists(e => e.list == list);

        public bool Move(int delta) => Current?.Move(delta) ?? false;
        /// <summary>Directional step on the live panel (navigator-aware); false when the panel has no navigator and the step is horizontal.</summary>
        public bool Navigate(Vector2Int direction) => Current?.Navigate(direction) ?? false;
        public bool CurrentHasNavigator => Current != null && Current.HasNavigator;
        public bool Activate() => Current?.ActivateFocused() ?? false;

        /// <summary>Every control of every open panel that the top panel can reach — the checklist a test walks.</summary>
        public IEnumerable<FocusItem> Reachable()
        {
            var list = Current;
            if (list == null) yield break;
            foreach (var item in list.Items) yield return item;
        }
    }
}
