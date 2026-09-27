using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Events
{
    /// <summary>Why the Secure Relay refused a transfer (None = it went through). Every refusal changes nothing.</summary>
    public enum SecureRelayRefusal
    {
        None,
        AlreadySecured,
        NoItem,
        Ammo,
        StarterItem,
        NotEligible,
        StorageFull,
        TransferFailed
    }

    /// <summary>Outcome of one Secure Relay transfer attempt.</summary>
    public sealed class SecureRelayResult
    {
        public SecureRelayRefusal Refusal;
        /// <summary>The carried instance the unit came from.</summary>
        public string SourceInstanceId;
        /// <summary>The instance now in Storage (a new id when one unit left a stack).</summary>
        public string StoredInstanceId;
        public string DefinitionId;
        public int Quantity;

        public bool Success => Refusal == SecureRelayRefusal.None;

        public static SecureRelayResult Refused(SecureRelayRefusal refusal, string instanceId = null) => new() { Refusal = refusal, SourceInstanceId = instanceId };
    }

    /// <summary>
    /// 57.7 Secure Relay: a rare terminal where every party member may, once, send exactly one carried item — one
    /// Weapon, Armor, Accessory or one unit of a Consumable — into their own Shelter Storage. Coins are never items,
    /// Ammo and Starter Kit gear (unsellable) are refused. The move is one <see cref="ItemTransferService"/> transfer:
    /// it either lands in Storage (the at-risk flag cleared there) and leaves the run, or nothing changes. The relay
    /// itself never resolves for the party: it records, per participant id, who has used it, so each member keeps an
    /// independent single use (and a reconnect under the same participant id cannot reset it).
    /// </summary>
    public sealed class SecureRelayEvent : DungeonEventBase
    {
        /// <summary>Room-state id prefix of one member's use ("relay:&lt;participantId&gt;"). Deliberately not "event:": that prefix resolves the whole event.</summary>
        public const string ResolvedPrefix = "relay:";

        private readonly Func<string, ItemDefinition> _resolve;
        private readonly ItemTransferService _transfer = new();
        private readonly Dictionary<string, string> _securedBy = new(StringComparer.Ordinal);

        public SecureRelayEvent(DungeonEventContext context, Func<string, ItemDefinition> resolveDefinition)
            : base(DungeonEventKind.SecureRelay, context)
        {
            _resolve = resolveDefinition ?? (_ => null);
        }

        public event Action<SecureRelayEvent, string, SecureRelayResult> Secured;

        public int SecuredCount => _securedBy.Count;
        public IEnumerable<string> SecuredBy => _securedBy.Keys;

        public static string ResolvedIdFor(string participantId) => ResolvedPrefix + (participantId ?? string.Empty);

        public bool HasSecured(string participantId) => participantId != null && _securedBy.ContainsKey(participantId);

        /// <summary>Definition id of what the participant secured here (null when unknown or not used).</summary>
        public string SecuredDefinitionOf(string participantId) =>
            participantId != null && _securedBy.TryGetValue(participantId, out var definition) ? definition : null;

        /// <summary>Restores a member's use from room state (revisit, co-op resync) without replaying any transfer.</summary>
        public void RestoreSecured(string participantId, string definitionId = null)
        {
            if (string.IsNullOrEmpty(participantId)) return;
            if (!_securedBy.TryGetValue(participantId, out var known) || known == null) _securedBy[participantId] = definitionId;
        }

        /// <summary>The terminal always opens for a living actor: the screen shows the choice, or this member's secured state.</summary>
        public override bool CanActivate(EventActor actor) => Phase == DungeonEventPhase.Available && actor != null;

        protected override DungeonEventResult OnActivate(EventActor actor) =>
            new(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: DungeonEventDetails.ChoiceRequired);

        /// <summary>Weapons, Armor, Accessories and Consumables qualify; Ammo and Starter Kit (unsellable) items never do.</summary>
        public static SecureRelayRefusal Eligibility(ItemInstance item, ItemDefinition definition)
        {
            if (item == null || item.Quantity <= 0) return SecureRelayRefusal.NoItem;
            if (definition == null) return SecureRelayRefusal.NotEligible;
            if (definition.Category == ItemCategory.Ammo) return SecureRelayRefusal.Ammo;
            if (item.IsUnsellable) return SecureRelayRefusal.StarterItem;
            return definition.Category is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory or ItemCategory.Consumable
                ? SecureRelayRefusal.None
                : SecureRelayRefusal.NotEligible;
        }

        public SecureRelayRefusal EligibilityOf(ItemInstance item) => Eligibility(item, item != null ? _resolve(item.DefinitionId) : null);

        /// <summary>What <see cref="Secure"/> would answer right now, without changing anything.</summary>
        public SecureRelayRefusal Check(string participantId, IReadOnlyList<IItemContainer> carried, string instanceId, IItemContainer destination, bool authorized = false)
        {
            if (!authorized && HasSecured(participantId)) return SecureRelayRefusal.AlreadySecured;
            var source = SourceOf(carried, instanceId);
            var item = source?.Find(instanceId);
            if (item == null) return SecureRelayRefusal.NoItem;
            var eligibility = EligibilityOf(item);
            if (eligibility != SecureRelayRefusal.None) return eligibility;
            if (destination == null || !destination.CanAccept(UnitOf(item))) return SecureRelayRefusal.StorageFull;
            return SecureRelayRefusal.None;
        }

        /// <summary>
        /// Moves one unit of the carried item into <paramref name="destination"/> and records the member's single use.
        /// <paramref name="authorized"/> is only for a co-op member committing a transfer its host already granted
        /// (the host's relay recorded the use; this peer's copy may have learned it first from the room state).
        /// </summary>
        public SecureRelayResult Secure(string participantId, IReadOnlyList<IItemContainer> carried, string instanceId, IItemContainer destination, bool authorized = false)
        {
            if (string.IsNullOrEmpty(participantId) || Phase != DungeonEventPhase.Available) return SecureRelayResult.Refused(SecureRelayRefusal.NotEligible, instanceId);
            var refusal = Check(participantId, carried, instanceId, destination, authorized);
            if (refusal != SecureRelayRefusal.None) return SecureRelayResult.Refused(refusal, instanceId);

            var source = SourceOf(carried, instanceId);
            var definitionId = source.Find(instanceId).DefinitionId;
            var transfer = _transfer.TransferQuantity(source, instanceId, 1, destination);
            if (!transfer.Success) return SecureRelayResult.Refused(transfer.Error == TransferError.DestinationRejected ? SecureRelayRefusal.StorageFull : SecureRelayRefusal.TransferFailed, instanceId);

            _securedBy[participantId] = definitionId;
            var result = new SecureRelayResult { SourceInstanceId = instanceId, StoredInstanceId = transfer.InstanceId, DefinitionId = definitionId, Quantity = 1 };
            Secured?.Invoke(this, participantId, result);
            return result;
        }

        private static IItemContainer SourceOf(IReadOnlyList<IItemContainer> carried, string instanceId) =>
            string.IsNullOrEmpty(instanceId) || carried == null ? null : carried.FirstOrDefault(c => c != null && c.Find(instanceId) != null);

        /// <summary>The unit that would travel: the item itself, or a one-unit portion of a stack (for the room check).</summary>
        private static ItemInstance UnitOf(ItemInstance item) =>
            item.Quantity <= 1 ? item : new ItemInstance(item.DefinitionId, 1, item.Rarity) { IsAtRisk = item.IsAtRisk };
    }

    /// <summary>Where a member's escrowed Secure Relay unit stands. Only <see cref="Accepted"/> ever enters Storage.</summary>
    public enum SecureRelayEscrowState
    {
        /// <summary>Sent (or about to be re-sent); no host verdict has reached this member. Never treated as accepted.</summary>
        Pending,
        /// <summary>The host's acceptance reached this member: the unit belongs in Storage (released now, or retried at the Shelter).</summary>
        Accepted,
        /// <summary>The host refused; the unit is on its way back into the run (waiting for room there).</summary>
        Refused
    }

    /// <summary>
    /// A co-op member's Secure Relay transfer between its request and the host's verdict (57.7). The unit has already left
    /// the run and this record — persisted in the member's save — is its only copy, so it can be neither lost nor
    /// duplicated whatever the link does. It enters Storage only on the host's acceptance (received directly, or replayed
    /// from the host's record after a reconnect). Anything else — an explicit refusal, or a run that ends / a process that
    /// dies before a verdict arrived — puts the unit back to the run's own fate: back into the carried inventory before the
    /// Return or failure transaction, or lost with a run abandoned mid-way. An unresolved escrow is never secured.
    /// </summary>
    [Serializable]
    public sealed class SecureRelayEscrow
    {
        public SecureRelayEscrowState State = SecureRelayEscrowState.Pending;
        /// <summary>The request's transaction id; a re-send after a reconnect uses the same one.</summary>
        public string TransactionId = "";
        public string RunTransactionId = "";
        public string ParticipantId = "";
        public int Depth;
        public int RoomNode;
        /// <summary>The carried container the unit came from ("backpack", "equipped:Armor"…), where a refusal returns it.</summary>
        public string SourceContainerId = "";
        public string SourceInstanceId = "";
        public ItemInstanceSnapshot Unit;
    }

    /// <summary>Opening, releasing and returning a <see cref="SecureRelayEscrow"/>; every path moves the one unit exactly once.</summary>
    public static class SecureRelayEscrows
    {
        /// <summary>
        /// Takes one unit of the carried item out of the run into a new escrow, after the same checks a local transfer makes
        /// (member's use, eligibility, room in this member's Storage). Nothing changes on a refusal.
        /// </summary>
        public static SecureRelayRefusal Open(SecureRelayEvent relay, string participantId, IReadOnlyList<IItemContainer> carried, string instanceId, Storage storage, out SecureRelayEscrow escrow)
        {
            escrow = null;
            if (relay == null || storage == null || string.IsNullOrEmpty(participantId)) return SecureRelayRefusal.NotEligible;
            var refusal = relay.Check(participantId, carried, instanceId, new SecureRelayStorageTarget(storage));
            if (refusal != SecureRelayRefusal.None) return refusal;
            var source = carried.First(c => c != null && c.Find(instanceId) != null);
            var hold = new ListItemContainer("relay_escrow");
            var moved = new ItemTransferService().TransferQuantity(source, instanceId, 1, hold);
            var unit = moved.Success ? hold.Items.SingleOrDefault() : null;
            if (unit == null) return SecureRelayRefusal.TransferFailed;
            escrow = new SecureRelayEscrow
            {
                ParticipantId = participantId, SourceContainerId = source.ContainerId, SourceInstanceId = instanceId, Unit = unit.ToSnapshot()
            };
            return SecureRelayRefusal.None;
        }

        /// <summary>The escrowed unit goes into Storage (no longer at risk). False when Storage cannot take it; the escrow then stays.</summary>
        public static bool Release(SecureRelayEscrow escrow, Storage storage) =>
            escrow?.Unit != null && storage != null && storage.TryAddSecured(ItemInstance.FromSnapshot(escrow.Unit));

        /// <summary>The host refused: the unit goes back into the run (its own container first), at risk as before.</summary>
        public static bool Return(SecureRelayEscrow escrow, IReadOnlyList<IItemContainer> carried)
        {
            if (escrow?.Unit == null || carried == null) return false;
            var unit = ItemInstance.FromSnapshot(escrow.Unit);
            unit.IsAtRisk = true;
            foreach (var container in carried.Where(c => c != null).OrderBy(c => c.ContainerId == escrow.SourceContainerId ? 0 : 1))
                if (container.CanAccept(unit) && container.TryAdd(unit)) return true;
            return false;
        }
    }

    /// <summary>Shelter Storage as the relay's destination: the only container that takes a carried (at-risk) item mid-run.</summary>
    public sealed class SecureRelayStorageTarget : IItemContainer
    {
        private readonly Storage _storage;

        public SecureRelayStorageTarget(Storage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }

        public string ContainerId => _storage.ContainerId;
        public IEnumerable<ItemInstance> Items => _storage.Items;
        public ItemInstance Find(string instanceId) => _storage.Find(instanceId);
        public bool CanAccept(ItemInstance item) => _storage.CanAcceptSecured(item);
        public bool TryAdd(ItemInstance item) => _storage.TryAddSecured(item);
        public ItemInstance TryRemove(string instanceId) => _storage.TryRemove(instanceId);
    }
}
