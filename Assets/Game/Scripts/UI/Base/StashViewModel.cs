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
    /// and ItemTransferService, and <see cref="LoadoutPanelViewModel.EquipFromStorage"/> for EQUIP / a drop onto a worn slot —
    /// so capacity, at-risk and slot rules, the no-duplication guarantee and the autosave hooks are exactly the ones the
    /// rest of the Shelter already uses. What it adds is the choice of item, which the text station never offered.
    /// </summary>
    public sealed class StashViewModel : IDisposable
    {
        public const int StorageColumns = 6;
        public const int StorageRows = 4;
        public const int PageSize = StorageColumns * StorageRows;
        public const int EquippedSlots = 5;
        /// <summary>Characters the stash's message line shows whole; a longer confirmation is phrased shorter rather than cut.</summary>
        public const int MessageLineChars = 36;

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

        /// <summary>Legendary specials for the inspection panel's Legendary line (the app's registry; null leaves it out).</summary>
        public RuinRail.Gameplay.Combat.Weapons.Specials.LegendarySpecialRegistry Specials { get; set; }

        /// <summary>The item's authoritative tooltip (the same one every item window uses) for the inspection panel.</summary>
        public ItemTooltip TooltipFor(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            return item == null ? null : ItemTooltip.Build(item, DefinitionOf(item), Specials);
        }

        /// <summary>The worn item a Storage or backpack item compares with (the one in-game rule), as its own tooltip; null for none.</summary>
        public ItemTooltip ComparedTooltipFor(InventorySlotRef cell)
        {
            if (cell.Kind == InventorySlotKind.Equipped) return null;
            var current = InventoryViewModel.ComparedWith(_session.Loadout, ItemAt(cell));
            return current == null ? null : ItemTooltip.Build(current, DefinitionOf(current), Specials);
        }

        /// <summary>What activating this cell would do now, or why it cannot.</summary>
        public StashIntent IntentFor(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            if (item == null) return new StashIntent(StashAction.None, string.Empty);
            if (cell.Kind == InventorySlotKind.Storage)
            {
                if (_backpack.CanAccept(item)) return new StashIntent(StashAction.Take, "TAKE");
                var bagRoom = _backpack.RoomFor(item);
                return bagRoom > 0
                    ? new StashIntent(StashAction.Take, $"TAKE {bagRoom} OF {item.Quantity}")
                    : new StashIntent(StashAction.Blocked, "BACKPACK FULL", "No free backpack slot. Drag it onto a backpack item to swap.");
            }

            if (item.IsAtRisk) return new StashIntent(StashAction.Blocked, "AT RISK", "Items carried on an expedition can be stored only once you are home.");
            if (_session.Storage.CanAccept(item)) return new StashIntent(StashAction.Store, "STORE");
            var storageRoom = _session.Storage.RoomFor(item);
            return storageRoom > 0
                ? new StashIntent(StashAction.Store, $"STORE {storageRoom} OF {item.Quantity}")
                : new StashIntent(StashAction.Blocked, "STORAGE FULL", "No free Storage slot. Drag it onto a Storage item to swap.");
        }

        public void SetCursor(InventorySlotRef? cell)
        {
            if (Nullable.Equals(Cursor, cell)) return;
            Cursor = cell;
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- swap targeting (keyboard / controller)

        /// <summary>The backpack or Storage item picked for a swap (R / pad Y) while the player chooses what to trade it for.</summary>
        public InventorySlotRef? SwapSource { get; private set; }
        private string _swapSourceId;
        public bool IsTargetingSwap => SwapSource.HasValue;

        /// <summary>The item picked for the swap, found by identity (Storage can be paged while choosing); null once it is gone.</summary>
        public ItemInstance SwapSourceItem
        {
            get
            {
                if (!SwapSource.HasValue || _swapSourceId == null) return null;
                return SwapSource.Value.Kind == InventorySlotKind.Storage
                    ? _session.Storage.Find(_swapSourceId)
                    : _session.Loadout.BackpackSlots.FirstOrDefault(i => i != null && i.InstanceId == _swapSourceId);
            }
        }

        /// <summary>True for the cell that currently shows the picked item (it may be on another Storage page).</summary>
        public bool IsSwapSource(InventorySlotRef cell) => _swapSourceId != null && ItemAt(cell)?.InstanceId == _swapSourceId;

        /// <summary>
        /// Picks a backpack or Storage item for a swap: the keyboard / controller counterpart of starting a drag. Nothing
        /// moves until a target is chosen; worn slots and empty cells cannot start one.
        /// </summary>
        public bool BeginSwap(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            if (item == null) return Report(false, string.Empty);
            if (cell.Kind == InventorySlotKind.Equipped) return Report(false, "Swaps trade backpack and Storage items.");
            SwapSource = cell;
            _swapSourceId = item.InstanceId;
            return Report(true, cell.Kind == InventorySlotKind.Storage ? "Pick a backpack item to swap with." : "Pick a Storage item to swap with.");
        }

        /// <summary>Leaves swap targeting; nothing has moved.</summary>
        public bool CancelSwap()
        {
            if (!IsTargetingSwap) return false;
            SwapSource = null;
            _swapSourceId = null;
            Report(false, string.Empty);
            return true;
        }

        /// <summary>A cell the picked item can trade places with: an occupied cell on the other side, of another kind (same-kind stacks merge instead).</summary>
        public bool IsSwapTarget(InventorySlotRef cell)
        {
            var source = SwapSourceItem;
            if (source == null || cell.Kind == InventorySlotKind.Equipped || cell.Kind == SwapSource.Value.Kind) return false;
            var target = ItemAt(cell);
            return target != null && !SameStack(source, target);
        }

        /// <summary>
        /// Completes the swap on <paramref name="target"/> through the exact route a mouse drag takes (Drop → the Storage
        /// station's exchange), so there is one swap transaction. An invalid target explains itself and keeps the pick.
        /// </summary>
        public bool CompleteSwap(InventorySlotRef target)
        {
            if (!IsTargetingSwap) return false;
            if (SwapSourceItem == null) { CancelSwap(); return Report(false, "That item is no longer there."); }
            if (!IsSwapTarget(target))
                return Report(false, SwapSource.Value.Kind == InventorySlotKind.Storage ? "Pick a backpack item to swap with." : "Pick a Storage item to swap with.");
            var picked = SwapSourceItem;
            var other = ItemAt(target);
            var fromStorage = SwapSource.Value.Kind == InventorySlotKind.Storage;
            SwapSource = null;
            _swapSourceId = null;
            return fromStorage
                ? Exchange(picked, other, target.Index)
                : Exchange(other, picked, _session.Loadout.BackpackSlots.ToList().IndexOf(picked));
        }

        /// <summary>Performs the cell's action through the station authority. A blocked or empty cell only explains itself.</summary>
        public bool Activate(InventorySlotRef cell)
        {
            if (IsTargetingSwap) return CompleteSwap(cell);
            var item = ItemAt(cell);
            var intent = IntentFor(cell);
            if (item == null) return Report(false, string.Empty);
            if (!intent.CanAct) return Report(false, intent.Reason);

            var name = NameOf(item);
            var total = item.Quantity;
            var ok = intent.Action == StashAction.Store ? _storage.Deposit(item.InstanceId) : _storage.Withdraw(item.InstanceId);
            if (!ok) return Report(false, _storage.Feedback.Text);
            var verb = intent.Action == StashAction.Store ? "Stored " : "Took ";
            return Report(true, _storage.LastWasPartial ? $"{verb}{_storage.LastMoved}/{total} {name}." : verb + name + ".");
        }

        /// <summary>
        /// The worn slot a stored item would be equipped into (EQUIP: F / X): its category's slot; a weapon takes the
        /// free weapon slot, Primary first, and swaps with Primary when both are taken. Null when nothing can be worn.
        /// </summary>
        public EquippedSlot? EquipSlotFor(InventorySlotRef cell)
        {
            if (cell.Kind != InventorySlotKind.Storage) return null;
            var definition = DefinitionOf(ItemAt(cell));
            if (definition == null) return null;
            switch (definition.Category)
            {
                case ItemCategory.Weapon:
                    if (_session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon) == null) return EquippedSlot.PrimaryWeapon;
                    return _session.Loadout.GetEquipped(EquippedSlot.SecondaryWeapon) == null ? EquippedSlot.SecondaryWeapon : EquippedSlot.PrimaryWeapon;
                case ItemCategory.Armor: return EquippedSlot.Armor;
                case ItemCategory.Accessory: return EquippedSlot.Accessory;
                case ItemCategory.Consumable: return EquippedSlot.ActiveConsumable;
                default: return null;
            }
        }

        /// <summary>The worn item an EQUIP of this cell would send to Storage (null when the target slot is empty).</summary>
        public ItemInstance EquipDisplaces(InventorySlotRef cell)
        {
            var slot = EquipSlotFor(cell);
            return slot.HasValue ? _session.Loadout.GetEquipped(slot.Value) : null;
        }

        /// <summary>
        /// EQUIP: the stored item goes straight into its worn slot; a worn item there moves into the stored item's Storage
        /// cell in the same exchange (the Loadout station's EquipFromStorage), so no free backpack or Storage slot is needed.
        /// </summary>
        public bool Equip(InventorySlotRef cell)
        {
            var item = ItemAt(cell);
            if (item == null) return Report(false, string.Empty);
            if (cell.Kind != InventorySlotKind.Storage) return Report(false, "Pick an item in Storage to equip it.");
            var slot = EquipSlotFor(cell);
            if (slot == null || _loadout == null) return Report(false, NameOf(item) + " cannot be worn.");
            return EquipInto(item, slot.Value);
        }

        /// <summary>One equip from Storage with the stash's compact confirmation (the strip's message line is short).</summary>
        private bool EquipInto(ItemInstance item, EquippedSlot slot)
        {
            var displaced = _session.Loadout.GetEquipped(slot);
            var ok = _loadout.EquipFromStorage(item.InstanceId, slot);
            if (!ok) return Report(false, _loadout.Feedback.Text);
            return Report(true, displaced != null ? $"Worn: {NameOf(item)} · Stored: {NameOf(displaced)}" : $"Equipped {NameOf(item)}.");
        }

        /// <summary>
        /// Drag and drop: survivor → any Storage cell stores, Storage → a backpack cell takes, Storage → a worn slot equips
        /// straight from Storage (swapping with the worn item there). Anything else (survivor → survivor is the Loadout
        /// station's business) does nothing.
        /// </summary>
        public bool Drop(InventorySlotRef from, InventorySlotRef to)
        {
            var fromStorage = from.Kind == InventorySlotKind.Storage;
            var toStorage = to.Kind == InventorySlotKind.Storage;
            if (fromStorage == toStorage) return false;
            // Backpack ⇄ Storage onto an occupied cell of another kind: the two items trade places, needing no free slot.
            var moving = ItemAt(from);
            var target = ItemAt(to);
            if (moving != null && target != null && !SameStack(moving, target) && (from.Kind == InventorySlotKind.Backpack || to.Kind == InventorySlotKind.Backpack))
            {
                return fromStorage ? Exchange(moving, target, to.Index) : Exchange(target, moving, from.Index);
            }

            if (!fromStorage) return Activate(from);
            if (to.Kind == InventorySlotKind.Backpack) return TakeInto(from, to.Index);

            var item = ItemAt(from);
            if (item == null || _loadout == null) return false;
            return EquipInto(item, to.EquippedSlot);
        }

        /// <summary>The one swap (drag or keyboard / controller): the Storage station's exchange of a stored item with a backpack slot.</summary>
        private bool Exchange(ItemInstance stored, ItemInstance carried, int backpackIndex)
        {
            var swapped = _storage.Exchange(stored.InstanceId, backpackIndex);
            if (!swapped) return Report(false, _storage.Feedback.Text);
            var both = $"Took {NameOf(stored)} · Stored {NameOf(carried)}";
            return Report(true, both.Length <= MessageLineChars ? both : $"Swapped for {NameOf(stored)}.");
        }

        /// <summary>Two items of one stackable kind merge rather than trade places.</summary>
        private bool SameStack(ItemInstance a, ItemInstance b) => a.DefinitionId == b.DefinitionId && DefinitionOf(a)?.IsStackable == true;

        /// <summary>Takes a stored item and, for a single item dropped on an empty backpack cell, puts it in exactly that cell.</summary>
        private bool TakeInto(InventorySlotRef from, int backpackIndex)
        {
            var item = ItemAt(from);
            if (!Activate(from) || item == null) return false;
            var landed = _session.Loadout.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == item.InstanceId);
            if (landed >= 0 && landed != backpackIndex && _session.Loadout.BackpackSlots[backpackIndex] == null) _session.Loadout.MoveBackpackSlot(landed, backpackIndex);
            return true;
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
