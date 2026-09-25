using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using UnityEngine;

namespace RuinRail.UI.WeaponCache
{
    /// <summary>One presented weapon of the cache as the screen shows it.</summary>
    public sealed class WeaponCacheRow
    {
        public int Index;
        public ItemInstance Item;
        public ItemDefinition Definition;
        public bool IsChosen;
        public string Name => Definition != null && !string.IsNullOrEmpty(Definition.DisplayName) ? Definition.DisplayName : Item?.DefinitionId ?? string.Empty;
        public Sprite Icon => Definition != null ? Definition.Icon : null;
        public int Rarity => Item != null ? (int)Item.Rarity : 0;
        public string Id => "cache.choice." + Index;
    }

    /// <summary>
    /// The Weapon Cache selection screen (57.6) over the existing <see cref="WeaponCacheEvent"/>: the three rolled
    /// weapons the event already decided are presented, the player takes exactly one, and the event's own
    /// <c>Choose</c> is the authority for the transfer and for consuming the cache. The view model never creates an
    /// item, never edits a container and never re-rolls; it opens, presents and forwards one confirm.
    ///
    /// While open it holds gameplay input and pauses the solo world exactly like the inventory and the merchant.
    /// </summary>
    public sealed class WeaponCacheViewModel : IDisposable
    {
        private WeaponCacheEvent _cache;
        private EventActor _actor;
        private PlayerInventory _inventory;
        private LegendarySpecialRegistry _specials;
        private IWorldPause _pause;
        private bool _isCoop;
        private readonly List<WeaponCacheRow> _rows = new();
        private int _cursor;
        private bool _disposed;
        private bool OverGameplay => _pause != null;

        public bool IsOpen { get; private set; }
        public int Opens { get; private set; }
        public int Takes { get; private set; }
        public string Message { get; private set; } = string.Empty;
        public bool MessageIsError { get; private set; }
        public int Depth => _cache != null && _cache.Context != null ? _cache.Context.Depth : 0;
        public IReadOnlyList<WeaponCacheRow> Rows => _rows;
        public int Cursor => _cursor;
        public WeaponCacheRow Selected => _rows.Count == 0 ? null : _rows[Mathf.Clamp(_cursor, 0, _rows.Count - 1)];
        public WeaponCacheEvent Cache => _cache;
        public bool IsBound => _cache != null && _actor != null;
        public bool IsConsumed => _cache != null && _cache.IsConsumed;
        public bool IsDisposed => _disposed;
        public int FreeSlots => _inventory != null ? _inventory.BackpackSlots.Count(s => s == null) : 0;
        public int BackpackCapacity => PlayerInventory.BackpackCapacity;

        public event Action Changed;

        /// <summary>Binds the world cache the player is standing at, together with the acting player's actor record.</summary>
        public void Bind(WeaponCacheEvent cache, EventActor actor, PlayerInventory inventory = null, LegendarySpecialRegistry specials = null)
        {
            if (_disposed) return; // a screen torn down with its scene never comes back through a late request
            _cache = cache;
            _actor = actor;
            _inventory = inventory;
            _specials = specials;
            RebuildRows();
            Raise();
        }

        /// <summary>Solo pauses through the world pause; co-op never pauses (92 applies to every gameplay overlay).</summary>
        public void ConfigurePause(IWorldPause pause, bool isCoop)
        {
            _pause = pause;
            _isCoop = isCoop;
        }

        // ---- Open / close ----

        public void Open()
        {
            if (IsOpen || !IsBound || IsConsumed) return;
            IsOpen = true;
            Opens++;
            _cursor = 0;
            SetMessage(string.Empty, false);
            RebuildRows();
            if (!_isCoop) _pause?.Pause();
            if (OverGameplay)
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
            SetMessage(string.Empty, false);
            if (!_isCoop) _pause?.Resume();
            if (OverGameplay)
            {
                RuinRail.Core.Input.GameplayInputGate.Release();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            }

            Raise();
        }

        // ---- Navigation ----

        public void SetCursor(int index)
        {
            var clamped = _rows.Count == 0 ? 0 : Mathf.Clamp(index, 0, _rows.Count - 1);
            if (clamped == _cursor) return;
            _cursor = clamped;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        public void MoveCursor(int delta) => SetCursor(_cursor + delta);

        // ---- Reading ----

        public ItemTooltip TooltipFor(WeaponCacheRow row) =>
            row == null || row.Item == null ? null : ItemTooltip.Build(row.Item, row.Definition, _specials);

        /// <summary>93: the highlighted weapon compares against the equipped weapon it would most likely replace.</summary>
        public IReadOnlyList<ComparisonLine> CompareFor(WeaponCacheRow row)
        {
            if (row == null || row.Item == null || _inventory == null) return Array.Empty<ComparisonLine>();
            EquippedSlot? target = _inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null ? EquippedSlot.PrimaryWeapon
                : _inventory.GetEquipped(EquippedSlot.SecondaryWeapon) != null ? EquippedSlot.SecondaryWeapon
                : null;
            if (target == null) return Array.Empty<ComparisonLine>();
            var current = _inventory.GetEquipped(target.Value);
            if (current == null) return Array.Empty<ComparisonLine>();
            return TooltipComparison.Compare(TooltipFor(row), ItemTooltip.Build(current, _inventory.Resolve(current.DefinitionId), _specials));
        }

        public bool CanTake
        {
            get
            {
                var row = Selected;
                return row != null && IsBound && !IsConsumed && _cache.CanChoose(_actor, row.Index);
            }
        }

        /// <summary>Why the highlighted weapon cannot be taken, for the details panel; empty when it can.</summary>
        public string BlockReason
        {
            get
            {
                if (!IsBound) return string.Empty;
                if (IsConsumed) return "CACHE ALREADY EMPTIED";
                var row = Selected;
                if (row == null) return "NOTHING TO CHOOSE";
                if (_actor.Backpack == null || !_actor.Backpack.CanAccept(row.Item)) return "BACKPACK FULL";
                return string.Empty;
            }
        }

        // ---- Taking (exactly once; the event is the authority) ----

        public DungeonEventOutcome Take()
        {
            var row = Selected;
            if (row == null || !IsBound) { Fail("NOTHING SELECTED"); return DungeonEventOutcome.Unavailable; }
            if (ChooseRequest != null)
            {
                // Co-op client (82): the host's cache decides, once for the whole party; the answer comes back here.
                if (!ChooseRequest(row.Index)) { Fail("REQUEST NOT SENT"); return DungeonEventOutcome.Unavailable; }
                SetMessage("TAKING " + row.Name.ToUpperInvariant() + "…", false);
                Raise();
                return DungeonEventOutcome.Started;
            }

            var result = _cache.Choose(_actor, row.Index);
            switch (result.Outcome)
            {
                case DungeonEventOutcome.Success:
                    Takes++;
                    SetMessage("TOOK " + row.Name.ToUpperInvariant(), false);
                    RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Purchase);
                    RebuildRows();
                    Raise();
                    Close();
                    return result.Outcome;
                case DungeonEventOutcome.None:
                    Fail("CACHE ALREADY EMPTIED");
                    break;
                default:
                    Fail(result.Detail == "backpack_rejected" ? "BACKPACK FULL" : "CANNOT TAKE THAT");
                    break;
            }

            RebuildRows();
            Raise();
            return result.Outcome;
        }

        /// <summary>Co-op client: set by the run so Take becomes a request to the host's cache. Null in solo and on the host.</summary>
        public Func<int, bool> ChooseRequest { get; set; }

        /// <summary>The host's answer to this screen's request (same messages as a local take).</summary>
        public void ReportRemoteResult(bool accepted, bool alreadyTaken, string itemName)
        {
            if (accepted)
            {
                Takes++;
                SetMessage("TOOK " + (itemName ?? string.Empty).ToUpperInvariant(), false);
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Purchase);
                RebuildRows();
                Raise();
                Close();
                return;
            }

            Fail(alreadyTaken ? "CACHE ALREADY EMPTIED" : "CANNOT TAKE THAT");
            RebuildRows();
            Raise();
        }

        // ---- Rows ----

        private void RebuildRows()
        {
            _rows.Clear();
            if (_cache == null) return;
            foreach (var choice in _cache.Choices)
                _rows.Add(new WeaponCacheRow { Index = choice.Index, Item = choice.Item, Definition = choice.Definition, IsChosen = _cache.ChosenIndex == choice.Index });
            _cursor = _rows.Count == 0 ? 0 : Mathf.Clamp(_cursor, 0, _rows.Count - 1);
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
        }

        private void Raise() => Changed?.Invoke();

        public void Dispose()
        {
            if (IsOpen) Close();
            _disposed = true;
            _cache = null;
            _actor = null;
            _inventory = null;
        }
    }
}
