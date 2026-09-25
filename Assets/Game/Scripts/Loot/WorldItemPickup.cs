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

        /// <summary>Takeable right now: an interactor that can receive it, and a backpack with room for this stack.</summary>
        public bool CanBeCollectedBy(GameObject interactor) =>
            CanInteract(interactor) && interactor.GetComponent<IItemReceiver>()?.Backpack?.CanAccept(_item) == true;

        /// <summary>Moves the held item into the interactor's inventory via the central transfer service.</summary>
        public bool Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return false;
            // The collector's pickup hooks (Ammo Pouch / Scavenger's Reserve) size the stack before it moves, so the
            // solo transfer, the host arbiter and the grant it sends a co-op member all carry the same final quantity.
            // A pickup that does not happen is put back exactly as it was.
            var baseQuantity = _item.Quantity;
            var hook = interactor.GetComponent<IPickupQuantityHook>();
            var sized = hook != null ? hook.PickupQuantityFor(_item) : baseQuantity;
            if (sized != baseQuantity) _item.SetQuantity(sized);
            // In co-op the run installs the host arbiter here, so a shared pickup is resolved once for the whole party
            // instead of by whichever player's collider got there first (82). Solo installs nothing and is unchanged.
            var arbitrated = PickupArbiter.Items?.Invoke(this, interactor);
            bool taken;
            if (arbitrated.HasValue) taken = arbitrated.Value;
            else
            {
                var receiver = interactor.GetComponent<IItemReceiver>();
                taken = TryPickUp(receiver.Backpack, receiver.TransferService).Success;
            }

            if (!taken && _item != null && sized != baseQuantity) _item.SetQuantity(baseQuantity);
            return taken;
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

        Transform transform { get; }
    }
}
