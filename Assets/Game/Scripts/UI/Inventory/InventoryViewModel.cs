using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.UI.Inventory
{
    /// <summary>The five equipment slots then the eight backpack slots, in navigation order (92).</summary>
    public enum InventorySlotKind
    {
        Equipped,
        Backpack,
        /// <summary>A cell of the Shelter stash's Storage grid (index = cell on the current page); never an inventory slot.</summary>
        Storage
    }

    public readonly struct InventorySlotRef : IEquatable<InventorySlotRef>
    {
        public InventorySlotRef(InventorySlotKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public InventorySlotKind Kind { get; }
        public int Index { get; }
        public EquippedSlot EquippedSlot => (EquippedSlot)Index;
        public bool Equals(InventorySlotRef other) => Kind == other.Kind && Index == other.Index;
        public override bool Equals(object obj) => obj is InventorySlotRef other && Equals(other);
        public override int GetHashCode() => (int)Kind * 31 + Index;
        public override string ToString() => Kind == InventorySlotKind.Equipped ? EquippedSlot.ToString() : $"Backpack {Index + 1}";
    }

    /// <summary>92: solo inventory pauses gameplay; co-op inventory never pauses the shared game.</summary>
    public interface IWorldPause
    {
        void Pause();
        void Resume();
    }

    /// <summary>Time-scale pause for the solo client. Counted, so the inventory and the pause menu (TASK 135) can overlap without one un-pausing the other.</summary>
    public sealed class TimeScalePause : IWorldPause
    {
        public int Holds { get; private set; }
        public bool IsPaused => Holds > 0;
        public void Pause() { Holds++; Time.timeScale = 0f; }
        public void Resume() { if (Holds == 0) return; Holds--; if (Holds == 0) Time.timeScale = 1f; }
    }

    public enum InventoryActionResult
    {
        Done,
        NothingSelected,
        IncompatibleSlot,
        BackpackFull,
        Refused
    }

    /// <summary>
    /// The Tab inventory (92) over the existing inventory/transfer services: five equipment slots, exactly eight
    /// backpack slots, Carried Coins shown separately. Every equip/unequip/move/drop goes through ItemTransferService
    /// (equipped-slot containers, backpack container, ground via PlayerLootReceiver) and every swap through the
    /// inventory's own atomic exchange; the UI never edits a container list. Opening pauses only in solo. A cursor gives keyboard/controller navigation; the mouse sets it directly.
    /// </summary>
    public sealed class InventoryViewModel : IDisposable
    {
        public const int BackpackSlots = PlayerInventory.BackpackCapacity;
        private static readonly EquippedSlot[] EquipmentOrder = { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.Accessory, EquippedSlot.ActiveConsumable };

        private readonly ItemTransferService _transfer = new();
        private readonly Dictionary<EquippedSlot, EquippedSlotContainer> _equipped = new();
        private PlayerInventory _inventory;
        private BackpackContainer _backpack;
        private PlayerLootReceiver _drops;
        private Func<int> _coins;
        private LegendarySpecialRegistry _specials;
        private IWorldPause _pause;
        private bool _isCoop;
        /// <summary>True for the in-run inventory (configured with a world pause): it holds gameplay input and plays the open/close cues. The Shelter's loadout panel is not over gameplay.</summary>
        private bool OverGameplay => _pause != null;

        public bool IsOpen { get; private set; }
        public InventorySlotRef Cursor { get; private set; } = new(InventorySlotKind.Equipped, 0);
        public InventorySlotRef? Selected { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public int Opens { get; private set; }
        public int Coins => _coins?.Invoke() ?? 0;
        public bool IsBackpackFull => _inventory != null && _inventory.BackpackSlots.All(s => s != null);
        public IReadOnlyList<EquippedSlot> EquipmentSlots => EquipmentOrder;

        public event Action Changed;

        public void Bind(PlayerInventory inventory, PlayerLootReceiver drops = null, Func<int> coins = null, LegendarySpecialRegistry specials = null)
        {
            if (_inventory != null) { _inventory.EquippedChanged -= OnEquippedChanged; _inventory.BackpackChanged -= Raise; }
            _inventory = inventory;
            _equipped.Clear();
            _backpack = inventory != null ? new BackpackContainer(inventory) : null;
            if (inventory != null)
            {
                foreach (var slot in EquipmentOrder) _equipped[slot] = new EquippedSlotContainer(inventory, slot);
                _inventory.EquippedChanged += OnEquippedChanged;
                _inventory.BackpackChanged += Raise;
            }

            _drops = drops;
            _coins = coins;
            _specials = specials;
            Raise();
        }

        /// <summary>Solo pauses through the world pause; co-op never pauses (92).</summary>
        public void ConfigurePause(IWorldPause pause, bool isCoop)
        {
            _pause = pause;
            _isCoop = isCoop;
        }

        public bool IsCoop => _isCoop;

        // ---- Open / close ----

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            Opens++;
            Message = string.Empty;
            if (!_isCoop) _pause?.Pause();
            if (OverGameplay)
            {
                // The inventory owns the screen: no shot, dash, interact or weapon change leaks through a click or a menu key.
                RuinRail.Core.Input.GameplayInputGate.Hold();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            }

            Raise();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Selected = null;
            Message = string.Empty;
            if (!_isCoop) _pause?.Resume();
            if (OverGameplay)
            {
                RuinRail.Core.Input.GameplayInputGate.Release();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            }

            Raise();
        }

        public void Toggle()
        {
            if (IsOpen) Close(); else Open();
        }

        // ---- Reading ----

        public ItemInstance ItemAt(InventorySlotRef slot)
        {
            if (_inventory == null) return null;
            return slot.Kind == InventorySlotKind.Equipped ? _inventory.GetEquipped(slot.EquippedSlot) : slot.Index >= 0 && slot.Index < _inventory.BackpackSlots.Count ? _inventory.BackpackSlots[slot.Index] : null;
        }

        public ItemTooltip TooltipAt(InventorySlotRef slot)
        {
            var item = ItemAt(slot);
            return item == null ? null : ItemTooltip.Build(item, _inventory.Resolve(item.DefinitionId), _specials);
        }

        /// <summary>93: the candidate against the equipped item of the slot it would go to (null when nothing to compare).</summary>
        public IReadOnlyList<ComparisonLine> CompareAt(InventorySlotRef slot)
        {
            var item = ItemAt(slot);
            if (item == null || slot.Kind == InventorySlotKind.Equipped) return Array.Empty<ComparisonLine>();
            var target = ComparisonSlotFor(item);
            if (target == null) return Array.Empty<ComparisonLine>();
            var current = _inventory.GetEquipped(target.Value);
            if (current == null) return Array.Empty<ComparisonLine>();
            return TooltipComparison.Compare(TooltipAt(slot), ItemTooltip.Build(current, _inventory.Resolve(current.DefinitionId), _specials));
        }

        /// <summary>93: a weapon compares against the equipped weapon (Primary first); other gear against its own slot.</summary>
        public EquippedSlot? ComparisonSlotFor(ItemInstance item)
        {
            var definition = _inventory?.Resolve(item.DefinitionId);
            if (definition == null) return null;
            if (definition.Category != ItemCategory.Weapon) return DefaultSlotFor(item);
            if (_inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null) return EquippedSlot.PrimaryWeapon;
            if (_inventory.GetEquipped(EquippedSlot.SecondaryWeapon) != null) return EquippedSlot.SecondaryWeapon;
            return null;
        }

        /// <summary>The item's definition (null for an unknown id).</summary>
        public ItemDefinition DefinitionOf(ItemInstance item) => item != null ? _inventory?.Resolve(item.DefinitionId) : null;

        /// <summary>The item icon bound on its definition (the one icon system: ItemDefinition.Icon); null when unbound.</summary>
        public Sprite IconOf(ItemInstance item) => DefinitionOf(item)?.Icon;

        /// <summary>Reserve of one ammo type (26: ammo lives in backpack stacks).</summary>
        public int AmmoReserve(AmmoType type) => _inventory?.Get(type) ?? 0;

        /// <summary>Per-slot stack cap of one ammo type (26), 0 when its definition is unknown.</summary>
        public int AmmoCap(AmmoType type)
        {
            var definition = _inventory?.Resolve("ammo_" + type.ToString().ToLowerInvariant());
            return definition != null ? _inventory.MaxStackFor(definition) : 0;
        }

        /// <summary>The equipment slot caption (92).</summary>
        public static string SlotLabel(EquippedSlot slot) => slot switch
        {
            EquippedSlot.PrimaryWeapon => "PRIMARY",
            EquippedSlot.SecondaryWeapon => "SECONDARY",
            EquippedSlot.Armor => "ARMOR",
            EquippedSlot.Accessory => "ACCESSORY",
            EquippedSlot.ActiveConsumable => "CONSUMABLE",
            _ => slot.ToString().ToUpperInvariant()
        };

        /// <summary>What the primary action button does for the cursor slot: EQUIP a backpack item, UNEQUIP an equipped one, nothing on an empty slot.</summary>
        public string PrimaryActionLabel => ItemAt(Cursor) == null ? string.Empty : Cursor.Kind == InventorySlotKind.Equipped ? "UNEQUIP" : DefaultSlotFor(ItemAt(Cursor)) == null ? string.Empty : "EQUIP";

        public bool CanPrimaryAction => !string.IsNullOrEmpty(PrimaryActionLabel);

        /// <summary>Equip the cursor's backpack item into its default slot, or unequip the cursor's equipped item into the backpack.</summary>
        public InventoryActionResult PrimaryAction()
        {
            if (ItemAt(Cursor) == null) return Fail(InventoryActionResult.NothingSelected, "Nothing selected.");
            Selected = null;
            return Cursor.Kind == InventorySlotKind.Equipped ? Unequip(Cursor.EquippedSlot) : Equip(Cursor);
        }

        public string DisplayNameOf(ItemInstance item)
        {
            var definition = item != null ? _inventory?.Resolve(item.DefinitionId) : null;
            return definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : item?.DefinitionId ?? string.Empty;
        }

        public EquippedSlot? DefaultSlotFor(ItemInstance item)
        {
            var definition = _inventory?.Resolve(item.DefinitionId);
            if (definition == null) return null;
            switch (definition.Category)
            {
                case ItemCategory.Weapon: return _inventory.GetEquipped(EquippedSlot.PrimaryWeapon) == null ? EquippedSlot.PrimaryWeapon : _inventory.GetEquipped(EquippedSlot.SecondaryWeapon) == null ? EquippedSlot.SecondaryWeapon : EquippedSlot.PrimaryWeapon;
                case ItemCategory.Armor: return EquippedSlot.Armor;
                case ItemCategory.Accessory: return EquippedSlot.Accessory;
                case ItemCategory.Consumable: return EquippedSlot.ActiveConsumable;
                default: return null;
            }
        }

        // ---- Navigation (keyboard/controller); the mouse calls SetCursor directly ----

        public void SetCursor(InventorySlotRef slot)
        {
            Cursor = slot;
            Raise();
        }

        /// <summary>Pure step of the slot cursor (the view's focus navigator uses the same rule): equipment is one column of 5, the backpack a 2×4 grid to its right; left/right cross between them at the matching row, edges clamp.</summary>
        public static InventorySlotRef NextCursor(InventorySlotRef from, Vector2Int direction)
        {
            if (from.Kind == InventorySlotKind.Equipped)
            {
                if (direction.y != 0) return new InventorySlotRef(InventorySlotKind.Equipped, Mathf.Clamp(from.Index - direction.y, 0, EquipmentOrder.Length - 1));
                if (direction.x > 0) return new InventorySlotRef(InventorySlotKind.Backpack, Mathf.Clamp(from.Index, 0, 1) * 4);
                return from;
            }

            var row = from.Index / 4;
            var col = from.Index % 4;
            if (direction.x < 0 && col == 0) return new InventorySlotRef(InventorySlotKind.Equipped, row);
            col = Mathf.Clamp(col + direction.x, 0, 3);
            row = Mathf.Clamp(row - direction.y, 0, 1);
            return new InventorySlotRef(InventorySlotKind.Backpack, row * 4 + col);
        }

        /// <summary>Equipment is one column (5 rows); the backpack is a 2x4 grid to its right. Left/right cross between them.</summary>
        public void MoveCursor(Vector2Int direction)
        {
            var before = Cursor;
            Cursor = NextCursor(Cursor, direction);
            if (!Cursor.Equals(before)) RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        /// <summary>Primary action on the cursor: nothing selected → select; selected → equip/move into the cursor slot (or swap).</summary>
        public InventoryActionResult Activate()
        {
            if (Selected == null)
            {
                if (ItemAt(Cursor) == null) return InventoryActionResult.NothingSelected;
                Selected = Cursor;
                Raise();
                return InventoryActionResult.Done;
            }

            var from = Selected.Value;
            Selected = null;
            if (from.Equals(Cursor)) { Raise(); return InventoryActionResult.Done; }
            return MoveTo(from, Cursor);
        }

        public void CancelSelection()
        {
            Selected = null;
            Raise();
        }

        // ---- Transfers: always through ItemTransferService ----

        /// <summary>
        /// Equip/Swap/Move between any two slots. Swapping a worn item with a backpack item (either drag direction) or
        /// the two weapon slots is one atomic inventory exchange, so it works with a full backpack (20, 92).
        /// </summary>
        public InventoryActionResult MoveTo(InventorySlotRef from, InventorySlotRef to)
        {
            var item = ItemAt(from);
            if (item == null) return Fail(InventoryActionResult.NothingSelected, "Nothing to move.");
            if (to.Kind == InventorySlotKind.Equipped)
            {
                if (!Fits(item, to.EquippedSlot)) return Fail(InventoryActionResult.IncompatibleSlot, "That item does not fit this slot.");
                if (_inventory.GetEquipped(to.EquippedSlot) != null)
                {
                    if (from.Kind == InventorySlotKind.Equipped) return Swap(from, to);
                    // Backpack -> occupied slot: the worn item takes exactly the backpack slot the candidate leaves.
                    return _inventory.TrySwapEquippedWithBackpack(to.EquippedSlot, from.Index) ? Done() : Fail(InventoryActionResult.Refused, "That item cannot be swapped in.");
                }

                var result = _transfer.Transfer(SourceOf(from), item.InstanceId, _equipped[to.EquippedSlot]);
                return result.Success ? Done() : Fail(InventoryActionResult.Refused, result.Error.ToString());
            }

            if (from.Kind == InventorySlotKind.Equipped)
            {
                // Worn item dropped onto a backpack item that can be worn in its place: the same atomic exchange.
                var target = ItemAt(to);
                if (target != null && Fits(target, from.EquippedSlot))
                {
                    return _inventory.TrySwapEquippedWithBackpack(from.EquippedSlot, to.Index) ? Done() : Fail(InventoryActionResult.Refused, "That item cannot be swapped in.");
                }

                return UnequipToBackpack(from.EquippedSlot);
            }

            // Backpack -> backpack: a manual reorder. The item lands in exactly the slot the player chose — an empty
            // slot is a move, an occupied slot swaps (or merges a same-definition stack) — and the order stays as put:
            // nothing compacts or re-sorts. Ownership never changes, so this is the container's own slot operation.
            var reorder = _inventory.MoveBackpackSlot(from.Index, to.Index);
            switch (reorder)
            {
                case SlotMoveResult.Moved:
                case SlotMoveResult.Swapped:
                case SlotMoveResult.Merged:
                    LastReorder = reorder;
                    return Done();
                case SlotMoveResult.Unchanged:
                    return Done();
                default:
                    return Fail(InventoryActionResult.Refused, "Nothing to move.");
            }
        }

        /// <summary>The outcome of the last backpack-to-backpack reorder (diagnostics/tests).</summary>
        public SlotMoveResult LastReorder { get; private set; }

        private InventoryActionResult Swap(InventorySlotRef a, InventorySlotRef b)
        {
            if (!Fits(ItemAt(a), b.EquippedSlot) || !Fits(ItemAt(b), a.EquippedSlot)) return Fail(InventoryActionResult.IncompatibleSlot, "That item does not fit this slot.");
            return _inventory.TrySwapEquipped(a.EquippedSlot, b.EquippedSlot) ? Done() : Fail(InventoryActionResult.Refused, "Those items cannot be swapped.");
        }

        /// <summary>Taking a worn item off has no swap partner, so it needs a free backpack slot.</summary>
        private InventoryActionResult UnequipToBackpack(EquippedSlot slot)
        {
            var item = _inventory.GetEquipped(slot);
            if (item == null) return Fail(InventoryActionResult.NothingSelected, "Nothing to move.");
            if (IsBackpackFull) return Fail(InventoryActionResult.BackpackFull, "BACKPACK FULL");
            var result = _transfer.Transfer(_equipped[slot], item.InstanceId, _backpack);
            return result.Success ? Done() : Fail(InventoryActionResult.Refused, result.Error.ToString());
        }

        private bool Fits(ItemInstance item, EquippedSlot slot) => item != null && PlayerInventory.IsSlotCompatible(_inventory.Resolve(item.DefinitionId)?.Category ?? ItemCategory.Ammo, slot);

        public InventoryActionResult Equip(InventorySlotRef from)
        {
            var item = ItemAt(from);
            if (item == null) return Fail(InventoryActionResult.NothingSelected, "Nothing selected.");
            var slot = DefaultSlotFor(item);
            return slot == null ? Fail(InventoryActionResult.IncompatibleSlot, "That item cannot be equipped.") : MoveTo(from, new InventorySlotRef(InventorySlotKind.Equipped, (int)slot.Value));
        }

        public InventoryActionResult Unequip(EquippedSlot slot) => UnequipToBackpack(slot);

        public InventoryActionResult SetActiveConsumable(InventorySlotRef from) => MoveTo(from, new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedSlot.ActiveConsumable));

        /// <summary>Drop to the ground through the loot receiver (ItemDropService → ItemTransferService).</summary>
        public InventoryActionResult Drop(InventorySlotRef from)
        {
            var item = ItemAt(from);
            if (item == null) return Fail(InventoryActionResult.NothingSelected, "Nothing to drop.");
            if (_drops == null) return Fail(InventoryActionResult.Refused, "Dropping is unavailable here.");
            // The receiver moves the item into a ground pickup in one transfer (or, for a co-op member, asks the host,
            // which revokes it): a refused drop leaves the item exactly where it was.
            var result = _drops.TryDrop(item.InstanceId);
            if (!result.Success) return Fail(InventoryActionResult.Refused, result.Transfer.Error == TransferError.InvalidRequest ? "You cannot drop that right now." : "Could not drop that.");
            Selected = null;
            return Done();
        }

        private IItemContainer SourceOf(InventorySlotRef slot) => slot.Kind == InventorySlotKind.Equipped ? _equipped[slot.EquippedSlot] : _backpack;

        private InventoryActionResult Done()
        {
            Message = string.Empty;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            Raise();
            return InventoryActionResult.Done;
        }

        private InventoryActionResult Fail(InventoryActionResult result, string message)
        {
            Message = message;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Failure);
            Raise();
            return result;
        }

        private void OnEquippedChanged(EquippedSlot _, ItemInstance __) => Raise();
        private void Raise() => Changed?.Invoke();

        public void Dispose()
        {
            if (IsOpen) Close();
            Bind(null);
        }
    }
}
