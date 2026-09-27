using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using UnityEngine;

namespace RuinRail.UI.SecureRelay
{
    /// <summary>One carried slot as the relay screen shows it: the item, whether it may be secured, and why not.</summary>
    public sealed class SecureRelayCell
    {
        public InventorySlotRef Slot;
        public ItemInstance Item;
        public ItemDefinition Definition;
        public SecureRelayRefusal Refusal;
        public bool IsEmpty => Item == null;
        public bool IsEligible => Item != null && Refusal == SecureRelayRefusal.None;
        public string Name => Definition != null && !string.IsNullOrEmpty(Definition.DisplayName) ? Definition.DisplayName : Item?.DefinitionId ?? string.Empty;
        public Sprite Icon => Definition != null ? Definition.Icon : null;
    }

    public enum SecureRelayStage
    {
        /// <summary>Picking the one item to send.</summary>
        Choosing,
        /// <summary>SECURE pressed once: the second press commits.</summary>
        Confirming,
        /// <summary>Co-op member: the request is with the host.</summary>
        Pending,
        /// <summary>This member's single use is spent: the terminal shows ITEM SECURED.</summary>
        Secured
    }

    /// <summary>
    /// The Secure Relay screen (57.7) over <see cref="SecureRelayEvent"/>: the acting member's five worn slots and
    /// eight backpack slots, which of them may be secured (and why not), a two-step SECURE → CONFIRM, and afterwards the
    /// ITEM SECURED state. The view model never moves an item: the run's <see cref="Commit"/> (solo / host) or
    /// <see cref="Request"/> (co-op member, answered through <see cref="ReportRemoteResult"/>) is the only path, and the
    /// relay's own Secure stays the authority. While open it holds gameplay input and pauses the solo world like the
    /// Weapon Cache.
    /// </summary>
    public sealed class SecureRelayViewModel : IDisposable
    {
        public const int EquippedCells = 5;
        public const int CellCount = EquippedCells + PlayerInventory.BackpackCapacity;

        private static readonly EquippedSlot[] EquippedOrder =
            { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.Accessory, EquippedSlot.ActiveConsumable };

        private SecureRelayEvent _relay;
        private EventActor _actor;
        private PlayerInventory _inventory;
        private IReadOnlyList<IItemContainer> _carried;
        private Storage _storage;
        private Func<string, ItemDefinition> _resolve;
        private LegendarySpecialRegistry _specials;
        private IWorldPause _pause;
        private bool _isCoop;
        private readonly List<SecureRelayCell> _cells = new();
        private int _cursor;
        private bool _disposed;

        public bool IsOpen { get; private set; }
        public int Opens { get; private set; }
        public int Secures { get; private set; }
        public SecureRelayStage Stage { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public bool MessageIsError { get; private set; }
        public SecureRelayEvent Relay => _relay;
        public bool IsBound => _relay != null && _actor != null && _inventory != null;
        public bool IsDisposed => _disposed;
        public int Depth => _relay != null && _relay.Context != null ? _relay.Context.Depth : 0;
        public IReadOnlyList<SecureRelayCell> Cells => _cells;
        public int Cursor => _cursor;
        public SecureRelayCell Selected => _cells.Count == 0 ? null : _cells[Mathf.Clamp(_cursor, 0, _cells.Count - 1)];
        public bool HasStorage => _storage != null;
        public int StorageUsed => _storage != null ? _storage.OccupiedSlots : 0;
        public int StorageCapacity => _storage != null ? _storage.Capacity : 0;
        public bool UsedByActor => _relay != null && _actor != null && _relay.HasSecured(_actor.ParticipantId);

        /// <summary>What this member secured here (for the ITEM SECURED card); null when unknown.</summary>
        public ItemDefinition SecuredDefinition
        {
            get
            {
                var id = _relay != null && _actor != null ? _relay.SecuredDefinitionOf(_actor.ParticipantId) : null;
                return id != null ? _resolve?.Invoke(id) : null;
            }
        }

        /// <summary>Solo / host: moves the unit now (relay.Secure into Storage + the save). Set by the run.</summary>
        public Func<string, SecureRelayResult> Commit { get; set; }

        /// <summary>Co-op member: asks the host; the answer arrives through <see cref="ReportRemoteResult"/>. Null in solo and on the host.</summary>
        public Func<string, bool> Request { get; set; }

        /// <summary>Co-op member: true while one of its requests still waits for the host (it opens in the uplinking state).</summary>
        public Func<bool> AwaitingHost { get; set; }

        public event Action Changed;

        public void Bind(SecureRelayEvent relay, EventActor actor, PlayerInventory inventory, IReadOnlyList<IItemContainer> carried, Storage storage,
            Func<string, ItemDefinition> resolve, LegendarySpecialRegistry specials = null)
        {
            if (_disposed) return;
            _relay = relay;
            _actor = actor;
            _inventory = inventory;
            _carried = carried;
            _storage = storage;
            _resolve = resolve;
            _specials = specials;
            RebuildCells();
            Raise();
        }

        public void ConfigurePause(IWorldPause pause, bool isCoop)
        {
            _pause = pause;
            _isCoop = isCoop;
        }

        // ---- Open / close ----

        public void Open()
        {
            if (IsOpen || !IsBound) return;
            IsOpen = true;
            Opens++;
            var awaiting = !UsedByActor && AwaitingHost != null && AwaitingHost();
            Stage = UsedByActor ? SecureRelayStage.Secured : awaiting ? SecureRelayStage.Pending : SecureRelayStage.Choosing;
            SetMessage(awaiting ? "UPLINKING…" : string.Empty, false);
            RebuildCells();
            _cursor = FirstEligibleOrZero();
            if (!_isCoop) _pause?.Pause();
            if (_pause != null)
            {
                RuinRail.Core.Input.GameplayInputGate.Hold();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            }

            Raise();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (Stage != SecureRelayStage.Secured) Stage = SecureRelayStage.Choosing; // a pending request keeps no UI state; its answer still lands
            SetMessage(string.Empty, false);
            if (!_isCoop) _pause?.Resume();
            if (_pause != null)
            {
                RuinRail.Core.Input.GameplayInputGate.Release();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            }

            Raise();
        }

        // ---- Navigation ----

        public void SetCursor(int index)
        {
            var clamped = _cells.Count == 0 ? 0 : Mathf.Clamp(index, 0, _cells.Count - 1);
            if (clamped == _cursor) return;
            _cursor = clamped;
            // A new slot disarms a pending CONFIRM and drops the last verdict: the status line speaks for the new slot.
            if (Stage == SecureRelayStage.Confirming) Stage = SecureRelayStage.Choosing;
            if (Stage == SecureRelayStage.Choosing) SetMessage(string.Empty, false);
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        // ---- Reading ----

        public ItemTooltip TooltipFor(SecureRelayCell cell) =>
            cell == null || cell.Item == null ? null : ItemTooltip.Build(cell.Item, cell.Definition, _specials);

        public bool CanSecure => IsBound && (Stage == SecureRelayStage.Choosing || Stage == SecureRelayStage.Confirming) && Selected != null && Selected.IsEligible;

        /// <summary>The line that says what the highlighted slot would do, or why it cannot be secured.</summary>
        public string StatusLine
        {
            get
            {
                var cell = Selected;
                if (cell == null || cell.IsEmpty) return "EMPTY SLOT";
                return cell.Refusal switch
                {
                    SecureRelayRefusal.None => cell.Item.Quantity > 1 ? $"SECURES 1 OF {cell.Item.Quantity}" : "CAN BE SECURED",
                    SecureRelayRefusal.Ammo => "AMMO CANNOT BE SECURED",
                    SecureRelayRefusal.StarterItem => "STARTER GEAR CANNOT BE SECURED",
                    SecureRelayRefusal.StorageFull => "SHELTER STORAGE FULL",
                    _ => "CANNOT BE SECURED"
                };
            }
        }

        // ---- Securing (two presses; the relay is the authority) ----

        /// <summary>First press arms the transfer, the second commits it. Returns true when something happened.</summary>
        public bool Secure()
        {
            if (!IsBound || !IsOpen) return false;
            if (Stage == SecureRelayStage.Pending || Stage == SecureRelayStage.Secured) return false;
            var cell = Selected;
            if (cell == null || !cell.IsEligible) { Fail(cell == null || cell.IsEmpty ? "NOTHING SELECTED" : StatusLine); return false; }
            if (Stage == SecureRelayStage.Choosing)
            {
                Stage = SecureRelayStage.Confirming;
                SetMessage("PRESS CONFIRM TO SEND IT HOME", false);
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
                Raise();
                return true;
            }

            var instanceId = cell.Item.InstanceId;
            if (Request != null)
            {
                if (!Request(instanceId)) { Stage = SecureRelayStage.Choosing; Fail("REQUEST NOT SENT"); return false; }
                Stage = SecureRelayStage.Pending;
                SetMessage("UPLINKING…", false);
                Raise();
                return true;
            }

            var result = Commit != null ? Commit(instanceId) : SecureRelayResult.Refused(SecureRelayRefusal.TransferFailed, instanceId);
            Apply(result.Success, result.Refusal);
            return result.Success;
        }

        /// <summary>The host's answer to this member's request (after the member committed it at home).</summary>
        public void ReportRemoteResult(bool accepted, SecureRelayRefusal refusal) => Apply(accepted, refusal);

        private void Apply(bool success, SecureRelayRefusal refusal)
        {
            if (success)
            {
                Secures++;
                Stage = SecureRelayStage.Secured;
                SetMessage("ITEM SECURED", false);
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Purchase);
                RebuildCells();
                Raise();
                return;
            }

            Stage = refusal == SecureRelayRefusal.AlreadySecured ? SecureRelayStage.Secured : SecureRelayStage.Choosing;
            RebuildCells();
            Fail(refusal switch
            {
                SecureRelayRefusal.AlreadySecured => "RELAY ALREADY USED",
                SecureRelayRefusal.StorageFull => "SHELTER STORAGE FULL — NOTHING SENT",
                SecureRelayRefusal.Ammo => "AMMO CANNOT BE SECURED",
                SecureRelayRefusal.StarterItem => "STARTER GEAR CANNOT BE SECURED",
                SecureRelayRefusal.NoItem => "ITEM NO LONGER CARRIED",
                _ => "TRANSFER FAILED — NOTHING SENT"
            });
            Raise();
        }

        /// <summary>Re-reads the carried slots (the inventory changed underneath an open screen).</summary>
        public void Refresh()
        {
            if (!IsBound) return;
            RebuildCells();
            Raise();
        }

        // ---- Cells ----

        private void RebuildCells()
        {
            _cells.Clear();
            if (_inventory == null) return;
            var target = _storage != null ? new SecureRelayStorageTarget(_storage) : null;
            for (var i = 0; i < CellCount; i++)
            {
                var slot = i < EquippedCells
                    ? new InventorySlotRef(InventorySlotKind.Equipped, (int)EquippedOrder[i])
                    : new InventorySlotRef(InventorySlotKind.Backpack, i - EquippedCells);
                var item = slot.Kind == InventorySlotKind.Equipped ? _inventory.GetEquipped(slot.EquippedSlot) : _inventory.BackpackSlots[slot.Index];
                var refusal = item == null ? SecureRelayRefusal.NoItem
                    : _relay == null ? SecureRelayRefusal.NotEligible
                    : _relay.Check(_actor?.ParticipantId, _carried, item.InstanceId, target, authorized: true);
                _cells.Add(new SecureRelayCell { Slot = slot, Item = item, Definition = item != null ? _resolve?.Invoke(item.DefinitionId) : null, Refusal = refusal });
            }

            _cursor = _cells.Count == 0 ? 0 : Mathf.Clamp(_cursor, 0, _cells.Count - 1);
        }

        private int FirstEligibleOrZero()
        {
            var index = _cells.FindIndex(c => c.IsEligible);
            return index >= 0 ? index : 0;
        }

        private void SetMessage(string text, bool error)
        {
            Message = text ?? string.Empty;
            MessageIsError = error;
        }

        private void Fail(string text)
        {
            SetMessage(text, true);
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Failure);
            Raise();
        }

        private void Raise() => Changed?.Invoke();

        public void Dispose()
        {
            if (IsOpen) Close();
            _disposed = true;
            _relay = null;
            _actor = null;
            _inventory = null;
            _carried = null;
            _storage = null;
        }
    }
}
