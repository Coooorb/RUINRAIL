using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;

namespace RuinRail.UI.Base
{
    /// <summary>What a stash cell does if the player activates it now.</summary>
    public enum StashAction
    {
        None,
        /// <summary>Survivor item → Storage.</summary>
        Store,
        /// <summary>Storage item → the survivor's backpack.</summary>
        Take,
        /// <summary>The move exists but a rule refuses it right now (the reason says which).</summary>
        Blocked
    }

    /// <summary>The action a cell would perform, and — when it cannot — the rule that stops it, in words the player reads.</summary>
    public readonly struct StashIntent
    {
        public StashIntent(StashAction action, string label, string reason = null)
        {
            Action = action;
            Label = label ?? string.Empty;
            Reason = reason ?? string.Empty;
        }

        public StashAction Action { get; }
        /// <summary>Short verb for the action strip: STORE, TAKE, or the blocking rule.</summary>
        public string Label { get; }
        public string Reason { get; }
        public bool CanAct => Action == StashAction.Store || Action == StashAction.Take;
    }

    /// <summary>
    /// The Shelter stash (94 Storage station): the survivor — five worn slots and the eight-slot backpack — beside a
    /// paged Storage grid, so the loot brought home can be put away item by item or all at once.
    ///
    /// It owns no items and no rules. Every move goes through the station's existing authority —
    /// <see cref="StoragePanelViewModel.Deposit"/> / <see cref="StoragePanelViewModel.Withdraw"/> over StorageService
    /// and ItemTransferService, and <see cref="LoadoutPanelViewModel.EquipFromStorage"/> for a drop onto a worn slot —
    /// so capacity, at-risk and slot rules, the no-duplication guarantee and the autosave hooks are exactly the ones the
    /// rest of the Shelter already uses. What it adds is the choice of item, which the text station never offered.
    /// </summary>
    public sealed class StashViewModel : IDisposable
    {
        public const int StorageColumns = 6;
        public const int StorageRows = 4;
        public const int PageSize = StorageColumns * StorageRows;
        public const int EquippedSlots = 5;

        private readonly BaseSession _session;
        private readonly StoragePanelViewModel _storage;
        private readonly LoadoutPanelViewModel _loadout;
        private readonly BackpackContainer _backpack;

        public StashViewModel(BaseSession session, StoragePanelViewModel storage, LoadoutPanelViewModel loadout)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _loadout = loadout;
            _backpack = new BackpackContainer(session.Loadout);
            _session.Storage.Changed += OnChanged;
            _session.Loadout.EquippedChanged += OnEquipped;
            _session.Loadout.BackpackChanged += OnChanged;
        }

        public event Action Changed;

        public int Page { get; private set; }
        public int PageCount => Math.Max(1, (StorageItems.Count + PageSize - 1) / PageSize);
        public int StoredCount => _storage.Count;
        public int Capacity => _storage.Capacity;
        /// <summary>No free Storage cell: only a stack that merges into one already stored can still go in.</summary>
        public bool StorageFull => StoredCount >= Capacity;
        public int BackpackCount => _session.Loadout.BackpackSlots.Count(i => i != null);
        public int CarriedCount => BackpackCount + Enumerable.Range(0, EquippedSlots).Count(i => _session.Loadout.GetEquipped((EquippedSlot)i) != null);

        /// <summary>The cell the pointer is over or the focus is on (details and the action strip follow it).</summary>
        public InventorySlotRef? Cursor { get; private set; }

        public string Message { get; private set; } = string.Empty;
        public bool MessageIsError { get; private set; }

        public ItemCategory? Filter => _storage.Filter;
        public bool SortByRarity => _storage.SortByRarity;

        /// <summary>Storage contents as the grid shows them: the station's own filter and sort.</summary>
        public IReadOnlyList<ItemInstance> StorageItems => _storage.Items;

        public static InventorySlotRef StorageCell(int index) => new(InventorySlotKind.Storage, index);

        public ItemInstance ItemAt(InventorySlotRef cell)
        {
            switch (cell.Kind)
            {
                case InventorySlotKind.Equipped:
                    return cell.Index >= 0 && cell.Index < EquippedSlots ? _session.Loadout.GetEquipped(cell.EquippedSlot) : null;
                case InventorySlotKind.Backpack:
                    return cell.Index >= 0 && cell.Index < _session.Loadout.BackpackSlots.Count ? _session.Loadout.BackpackSlots[cell.Index] : null;
                default:
                    var items = StorageItems;
                    var index = Page * PageSize + cell.Index;
                    return cell.Index >= 0 && cell.Index < PageSize && index < items.Count ? items[index] : null;
            }
        }

        public ItemDefinition DefinitionOf(ItemInstance item) => item == null ? null : _session.Configs.Resolve(item.DefinitionId);

        /// <summary>What activating this cell would do now, or why it cannot.</summary>
        public StashIntent IntentFor(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            if (item == null) return new StashIntent(StashAction.None, string.Empty);
            if (cell.Kind == InventorySlotKind.Storage)
            {
                return _backpack.CanAccept(item)
                    ? new StashIntent(StashAction.Take, "TAKE")
                    : new StashIntent(StashAction.Blocked, "BACKPACK FULL", "No free backpack slot. Store something first.");
            }

            if (item.IsAtRisk) return new StashIntent(StashAction.Blocked, "AT RISK", "Items carried on an expedition can be stored only once you are home.");
            return _session.Storage.CanAccept(item)
                ? new StashIntent(StashAction.Store, "STORE")
                : new StashIntent(StashAction.Blocked, "STORAGE FULL", "No free Storage slot. Upgrade it at the Workshop or take something out.");
        }

        public void SetCursor(InventorySlotRef? cell)
        {
            if (Nullable.Equals(Cursor, cell)) return;
            Cursor = cell;
            Changed?.Invoke();
        }

        /// <summary>Performs the cell's action through the station authority. A blocked or empty cell only explains itself.</summary>
        public bool Activate(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            var intent = IntentFor(cell);
            if (item == null) return Report(false, string.Empty);
            if (!intent.CanAct) return Report(false, intent.Reason);

            var ok = intent.Action == StashAction.Store ? _storage.Deposit(item.InstanceId) : _storage.Withdraw(item.InstanceId);
            return Report(ok, ok ? (intent.Action == StashAction.Store ? "Stored " : "Took ") + NameOf(item) + "." : _storage.Feedback.Text);
        }

        /// <summary>
        /// Drag and drop: survivor → any Storage cell stores, Storage → a backpack cell takes, Storage → a worn slot equips
        /// straight from Storage. Anything else (survivor → survivor is the Loadout station's business) does nothing.
        /// </summary>
        public bool Drop(InventorySlotRef from, InventorySlotRef to)
        {
            var fromStorage = from.Kind == InventorySlotKind.Storage;
            var toStorage = to.Kind == InventorySlotKind.Storage;
            if (fromStorage == toStorage) return false;
            if (!fromStorage) return Activate(from);
            if (to.Kind == InventorySlotKind.Backpack) return Activate(from);

            var item = ItemAt(from);
            if (item == null || _loadout == null) return false;
            var ok = _loadout.EquipFromStorage(item.InstanceId, to.EquippedSlot);
            return Report(ok, ok ? "Equipped " + NameOf(item) + "." : _loadout.Feedback.Text);
        }

        /// <summary>Stores every backpack item it can (in slot order); stops at the first refusal and says why.</summary>
        public int StoreBackpack()
        {
            var stored = 0;
            string refusal = null;
            for (var i = 0; i < _session.Loadout.BackpackSlots.Count; i++)
            {
                var cell = new InventorySlotRef(InventorySlotKind.Backpack, i);
                var item = ItemAt(cell);
                if (item == null) continue;
                var intent = IntentFor(cell);
                if (!intent.CanAct) { refusal = intent.Label; continue; }
                if (_storage.Deposit(item.InstanceId)) stored++;
                else refusal ??= _storage.Feedback.Text;
            }

            Report(stored > 0 && refusal == null,
                stored == 0 && refusal == null ? "The backpack is empty." : refusal == null ? $"Stored {stored} item{(stored == 1 ? string.Empty : "s")}." : $"Stored {stored}. {refusal}.");
            return stored;
        }

        public bool NextPage() => SetPage(Page + 1);
        public bool PrevPage() => SetPage(Page - 1);

        private bool SetPage(int page)
        {
            page = Math.Max(0, Math.Min(PageCount - 1, page));
            if (page == Page) return false;
            Page = page;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Cycles the Storage view through ALL and every category (the station's own filter).</summary>
        public void CycleFilter()
        {
            var categories = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
            _storage.Filter = _storage.Filter == null ? categories[0] : Array.IndexOf(categories, _storage.Filter.Value) + 1 < categories.Length ? categories[Array.IndexOf(categories, _storage.Filter.Value) + 1] : null;
            Page = 0;
            Changed?.Invoke();
        }

        public void ToggleSort()
        {
            _storage.SortByRarity = !_storage.SortByRarity;
            Changed?.Invoke();
        }

        public string FilterLabel => _storage.Filter == null ? "ALL" : _storage.Filter.Value.ToString().ToUpperInvariant();

        private string NameOf(ItemInstance item) => DefinitionOf(item)?.DisplayName ?? item.DefinitionId;

        private bool Report(bool ok, string message)
        {
            Message = message ?? string.Empty;
            MessageIsError = !ok && Message.Length > 0;
            Changed?.Invoke();
            return ok;
        }

        private void OnEquipped(EquippedSlot slot, ItemInstance item) => OnChanged();

        private void OnChanged()
        {
            var clamped = Math.Min(Page, PageCount - 1);
            if (clamped != Page) Page = clamped;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            _session.Storage.Changed -= OnChanged;
            _session.Loadout.EquippedChanged -= OnEquipped;
            _session.Loadout.BackpackChanged -= OnChanged;
        }
    }
}
