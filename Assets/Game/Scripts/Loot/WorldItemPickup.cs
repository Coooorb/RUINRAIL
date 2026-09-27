using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// One shared world pickup holding exactly one item instance (equipment, or an ammo/consumable stack). It is an
    /// IItemContainer, so picking up is an ordinary ItemTransferService move: the instance leaves the pickup atomically
    /// and the pickup despawns only after the transfer succeeded — never duplicated, never lost.
    /// Re-entrant or same-frame duplicate pickup callbacks (a second interaction, a trigger overlap, a PickedUp
    /// listener) are rejected: the pickup resolves at most one transfer for its lifetime.
    /// </summary>
    public sealed class WorldItemPickup : MonoBehaviour, IItemContainer, IInteractable, IAttractablePickup, IInteractionPrompt
    {
        private ItemInstance _item;
        private ItemCategory? _category;
        private string _displayName = string.Empty;
        private bool _resolving;
        private bool _consumed;

        public string ContainerId => $"pickup:{name}";
        public ItemInstance Item => _item;
        public bool IsEmpty => _item == null;

        /// <summary>Category of the held item when the spawner/dropper knew it; null when unknown (never attractable then).</summary>
        public ItemCategory? Category => _category;

        /// <summary>True once the item left through a successful pickup; the object is on its way to destruction.</summary>
        public bool IsConsumed => _consumed;

        /// <summary>
        /// 30 Magnetic Coil / 34 Room Sweep: only Coins and Ammo pickups are attraction-eligible; equipment and
        /// consumables always require a deliberate interaction.
        /// </summary>
        public bool IsAttractionEligible => _item != null && _category == ItemCategory.Ammo;

        public event Action<WorldItemPickup> PickedUp;

        /// <summary>The player whose drop this is, while their attraction still holds it back (null otherwise).</summary>
        public GameObject DroppedBy { get; private set; }

        /// <summary>Marks a manual drop (32): the dropper's attraction leaves it alone until their reach has left it once.</summary>
        public void MarkDroppedBy(GameObject dropper) => DroppedBy = dropper;

        public bool IsHeldBackFrom(GameObject collector) => collector != null && DroppedBy != null && DroppedBy == collector;

        public void ReleaseHoldBack(GameObject collector)
        {
            if (IsHeldBackFrom(collector)) DroppedBy = null;
        }

        public IEnumerable<ItemInstance> Items
        {
            get
            {
                if (_item != null) yield return _item;
            }
        }

        public void Hold(ItemInstance item, ItemCategory? category = null)
        {
            _item = item;
            _category = category;
        }

        public void SetCategory(ItemCategory? category) => _category = category;

        /// <summary>Display name of the held item for the interaction prompt (the spawner resolves it; empty when unknown).</summary>
        public string DisplayName => _displayName;

        public void SetDisplayName(string displayName) => _displayName = displayName ?? string.Empty;

        public string PromptFor(GameObject interactor)
        {
            if (!CanInteract(interactor)) return string.Empty;
            var name = string.IsNullOrEmpty(_displayName) ? _item.DefinitionId : _displayName;
            return ("TAKE " + name + (_item.Quantity > 1 ? " x" + _item.Quantity : string.Empty)).ToUpperInvariant();
        }

        public ItemInstance Find(string instanceId) => _item != null && _item.InstanceId == instanceId ? _item : null;

        public bool CanAccept(ItemInstance item) => item != null && _item == null && !_consumed;

        public bool TryAdd(ItemInstance item)
        {
            if (!CanAccept(item)) return false;
            _item = item;
            return true;
        }

        public ItemInstance TryRemove(string instanceId)
        {
            if (_item == null || _item.InstanceId != instanceId) return null;
            var removed = _item;
            _item = null;
            return removed;
        }

        public bool CanInteract(GameObject interactor) => _item != null && !_resolving && !_consumed && interactor != null && interactor.GetComponent<IItemReceiver>() != null;

        /// <summary>
        /// How much of this pickup <paramref name="destination"/> could take right now: the whole stack, the part that
        /// fits its matching stacks and free slots (a stack only), or nothing.
        /// </summary>
        public int CollectableInto(IItemContainer destination)
        {
            if (_item == null || _resolving || _consumed || destination == null) return 0;
            if (destination is IStackRoom room) return Mathf.Clamp(room.RoomFor(_item), 0, _item.Quantity);
            return destination.CanAccept(_item) ? _item.Quantity : 0;
        }

        /// <summary>Takeable right now: an interactor that can receive it, and a backpack with room for at least part of this stack.</summary>
        public bool CanBeCollectedBy(GameObject interactor) =>
            CanInteract(interactor) && CollectableInto(interactor.GetComponent<IItemReceiver>()?.Backpack) > 0;

        /// <summary>True when the whole stack fits (attraction pulls only those; a partial fit is taken where it lies).</summary>
        public bool FitsWhollyFor(GameObject interactor) =>
            CanInteract(interactor) && CollectableInto(interactor.GetComponent<IItemReceiver>()?.Backpack) >= _item.Quantity;

        /// <summary>Raised when a partial pickup leaves the rest here (the new remaining quantity).</summary>
        public event Action<WorldItemPickup, int> QuantityChanged;

        /// <summary>Moves the held item (or the part that fits) into the interactor's inventory via the central transfer service.</summary>
        public bool Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return false;
            // In co-op the run installs the host arbiter here, so a shared pickup is resolved once for the whole party
            // instead of by whichever player's collider got there first (82); it ends in the same Collect below.
            var arbitrated = PickupArbiter.Items?.Invoke(this, interactor);
            if (arbitrated.HasValue) return arbitrated.Value;
            var receiver = interactor.GetComponent<IItemReceiver>();
            return Collect(receiver.Backpack, receiver.TransferService, interactor.GetComponent<IPickupQuantityHook>()).Success;
        }

        /// <summary>
        /// The one pickup transaction — a manual interaction, the auto-pickup pull and the co-op host arbiter all end here.
        /// Whole stack fits: the collector's pickup hook (Ammo Pouch / Scavenger's Reserve) sizes it, clipped to the room
        /// there is, and the whole pickup moves (and despawns). Only part fits: exactly that part moves into the
        /// matching stacks / free slots, and the rest stays in this pickup with its exact remaining quantity (the bonus
        /// hook applies to whole pickups; the remainder gets it when it is taken). Nothing fits: nothing changes.
        /// </summary>
        public TransferResult Collect(IItemContainer destination, ItemTransferService transferService, IPickupQuantityHook hook = null)
        {
            if (_item == null || _resolving || _consumed) return TransferResult.Fail(TransferError.SourceMissingItem);
            var room = CollectableInto(destination);
            if (room <= 0) return TransferResult.Fail(TransferError.DestinationRejected, _item.InstanceId);
            var baseQuantity = _item.Quantity;
            if (room >= baseQuantity)
            {
                var sized = hook != null ? hook.PickupQuantityFor(_item) : baseQuantity;
                if (sized > baseQuantity && destination is IStackRoom stackRoom) sized = Mathf.Max(baseQuantity, Mathf.Min(sized, stackRoom.RoomFor(_item)));
                if (sized != baseQuantity) _item.SetQuantity(sized);
                var whole = TryPickUp(destination, transferService);
                if (!whole.Success && _item != null && !_consumed) _item.SetQuantity(baseQuantity); // not taken: exactly as it was
                return whole;
            }

            _resolving = true;
            try
            {
                var part = (transferService ?? new ItemTransferService()).TransferQuantity(this, _item.InstanceId, room, destination);
                if (part.Success) QuantityChanged?.Invoke(this, _item.Quantity);
                return part;
            }
            finally
            {
                _resolving = false;
            }
        }

        public TransferResult TryPickUp(IItemContainer destination, ItemTransferService transferService)
        {
            if (_item == null || _resolving || _consumed)
            {
                return TransferResult.Fail(TransferError.SourceMissingItem);
            }

            _resolving = true;
            try
            {
                var result = (transferService ?? new ItemTransferService()).Transfer(this, _item.InstanceId, destination);
                if (result.Success)
                {
                    _consumed = true;
                    PickedUp?.Invoke(this);
                    Destroy(gameObject);
                }

                return result;
            }
            finally
            {
                _resolving = false;
            }
        }
    }

    /// <summary>Narrow seam an interactor exposes so pickups can hand items over through the transfer service.</summary>
    public interface IItemReceiver
    {
        IItemContainer Backpack { get; }
        ItemTransferService TransferService { get; }
    }

    /// <summary>
    /// Optional seam on a collector: the quantity a ground stack is taken with (wearer pickup passives). Resolved where
    /// the pickup is decided — the solo player, the host player, or the host's copy of a co-op member.
    /// </summary>
    public interface IPickupQuantityHook
    {
        int PickupQuantityFor(ItemInstance item);
    }

    /// <summary>A pickup a magnetic radius may pull toward a player and collect on arrival (Coins, Ammo stacks).</summary>
    public interface IAttractablePickup : IInteractable
    {
        bool IsAttractionEligible { get; }

        /// <summary>
        /// True when this pickup could actually be taken by the interactor *right now*, capacity included.
        ///
        /// Attraction has to ask this and not just <see cref="IInteractable.CanInteract"/>: a stack the backpack has no
        /// room for would otherwise be dragged onto the player, fail to be collected, and then sit at their feet as the
        /// nearest interactable — silently taking the interaction prompt away from the chest, cache or event object
        /// they are standing at. A pickup that cannot be taken is left exactly where it fell.
        /// </summary>
        bool CanBeCollectedBy(GameObject interactor);

        /// <summary>True when the whole pickup fits the interactor now; a pickup that only partly fits is taken where it lies, never pulled.</summary>
        bool FitsWhollyFor(GameObject interactor);

        /// <summary>
        /// True while this pickup is held back from one collector's attraction: a player who dropped it (32) does not
        /// pull their own drop straight back from under their feet. Everyone else may collect it at once, and a
        /// deliberate interaction is never held back.
        /// </summary>
        bool IsHeldBackFrom(GameObject collector);

        /// <summary>The collector's reach has left the pickup once: from now on it attracts to them like any other.</summary>
        void ReleaseHoldBack(GameObject collector);

        Transform transform { get; }
    }
}
