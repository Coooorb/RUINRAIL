using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Networking
{
    public enum LootVerdict
    {
        Accepted,
        Duplicate,
        AlreadyTaken,
        Rejected,
        NotAuthority,
        Unknown
    }

    /// <summary>Resolved outcome of one loot/economy request; identical for every peer and replayable by transaction id.</summary>
    [Serializable]
    public sealed class LootTransactionResult
    {
        public string TransactionId;
        public ulong ClientId;
        public LootVerdict Verdict;
        public string Detail;
        public int Coins;
        public string InstanceId;
        public int Quantity;
    }

    /// <summary>
    /// Idempotent transaction ledger: a transaction id maps to exactly one resolved result. Re-sent requests (lag,
    /// retries, duplicate RPCs) return the stored result and never execute again.
    /// </summary>
    public sealed class TransactionLedger
    {
        private readonly Dictionary<string, LootTransactionResult> _results = new(StringComparer.Ordinal);

        public int Count => _results.Count;
        public IEnumerable<LootTransactionResult> Results => _results.Values;

        /// <summary>Transaction ids are client-generated, so a result is scoped to (client, id): one client can never pre-register or read another client's transaction (TASK 144).</summary>
        public static string KeyFor(ulong clientId, string transactionId) => clientId + ":" + (transactionId ?? string.Empty);

        public bool TryGet(ulong clientId, string transactionId, out LootTransactionResult result) => _results.TryGetValue(KeyFor(clientId, transactionId), out result);

        public LootTransactionResult Record(LootTransactionResult result)
        {
            _results[KeyFor(result.ClientId, result.TransactionId)] = result;
            return result;
        }
    }

    /// <summary>A party member's receiving side on the host: its backpack container and Carried wallet.</summary>
    public sealed class LootParticipant
    {
        public LootParticipant(ulong clientId, string participantId, IItemContainer backpack, CoinWallet wallet, GameObject entity = null)
        {
            ClientId = clientId;
            ParticipantId = participantId;
            Backpack = backpack;
            Wallet = wallet;
            Entity = entity;
        }

        public ulong ClientId { get; }
        public string ParticipantId { get; }
        public IItemContainer Backpack { get; }
        public CoinWallet Wallet { get; }
        public GameObject Entity { get; }
    }

    /// <summary>
    /// Host-side arbitration of every loot/economy request (82): pickups, drops, coin piles, chests, events and
    /// merchant purchases execute exactly once on the host through the existing transfer/loot/event/merchant services;
    /// clients only send requests carrying a transaction id and receive the resolved result. Simultaneous pickups of
    /// one instance resolve to exactly one winner (the transfer service refuses the second, nothing is duplicated).
    /// </summary>
    public sealed class LootAuthorityService
    {
        private readonly IAuthorityContext _authority;
        private readonly Dictionary<ulong, LootParticipant> _participants = new();
        private readonly TransactionLedger _ledger = new();
        private readonly ItemTransferService _transfer = new();
        private PartyCoinDistributor _coins;
        private ItemDropService _drops;

        public LootAuthorityService(IAuthorityContext authority)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public TransactionLedger Ledger => _ledger;
        public IReadOnlyDictionary<ulong, LootParticipant> Participants => _participants;
        public PartyCoinDistributor Coins => _coins;

        public event Action<LootTransactionResult> Resolved;

        public void SetDropService(ItemDropService drops) => _drops = drops;

        public void RegisterParticipant(LootParticipant participant)
        {
            _participants[participant.ClientId] = participant;
            RebuildDistributor();
        }

        public void UnregisterParticipant(ulong clientId)
        {
            if (_participants.Remove(clientId)) RebuildDistributor();
        }

        private void RebuildDistributor()
        {
            _coins = new PartyCoinDistributor(_participants.Values.OrderBy(p => p.ClientId).Select(p => new CoinParticipant(p.ParticipantId, p.Wallet)).ToArray());
        }

        private bool Gate(string transactionId, ulong clientId, out LootTransactionResult cached)
        {
            cached = null;
            if (!HostAuthorityContract.CanDecide(_authority, AuthoritativeDomain.WorldPickupValidity))
            {
                cached = new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.NotAuthority, Detail = "only the host resolves loot" };
                return false;
            }

            if (_ledger.TryGet(clientId, transactionId, out var previous))
            {
                cached = previous;
                return false;
            }

            // 84/85: a Downed or Dead participant performs no loot action (no pickup, no drop, no chest/event/merchant),
            // so a dead teammate's carried gear can never be salvaged or handed over.
            if (_participants.TryGetValue(clientId, out var participant) && participant.Entity != null)
            {
                var gate = participant.Entity.GetComponent<RuinRail.Gameplay.Combat.IPlayerActionGate>();
                if (gate != null && !gate.CanAct)
                {
                    cached = Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Rejected, Detail = "participant cannot act (downed/dead)" });
                    return false;
                }
            }

            return true;
        }

        private LootTransactionResult Finish(LootTransactionResult result)
        {
            _ledger.Record(result);
            Resolved?.Invoke(result);
            return result;
        }

        /// <summary>World item pickup: the transfer service makes the first request win; later ones see the item gone.</summary>
        public LootTransactionResult RequestPickup(string transactionId, ulong clientId, WorldItemPickup pickup)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (!_participants.TryGetValue(clientId, out var participant)) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Unknown, Detail = "unknown participant" });
            if (pickup == null || pickup.IsConsumed || pickup.Item == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.AlreadyTaken });

            var instanceId = pickup.Item.InstanceId;
            var quantity = pickup.Item.Quantity;
            var transfer = pickup.TryPickUp(participant.Backpack, _transfer);
            var verdict = transfer.Success ? LootVerdict.Accepted : transfer.Error == TransferError.SourceMissingItem ? LootVerdict.AlreadyTaken : LootVerdict.Rejected;
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = verdict, Detail = transfer.Error.ToString(), InstanceId = instanceId, Quantity = transfer.Success ? quantity : 0 });
        }

        public LootTransactionResult RequestDrop(string transactionId, ulong clientId, IEnumerable<IItemContainer> sources, string instanceId, int quantity, Vector2 position)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (_drops == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Rejected, Detail = "no drop service" });
            var drop = _drops.DropFromAny(sources, instanceId, quantity, position);
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = drop.Success ? LootVerdict.Accepted : LootVerdict.Rejected, Detail = drop.Transfer.Error.ToString(), InstanceId = drop.Success ? drop.Pickup.Item.InstanceId : instanceId, Quantity = drop.Transfer.Quantity });
        }

        /// <summary>Coin pile: one authoritative even split across the current party; the result is what clients receive.</summary>
        public LootTransactionResult RequestCoins(string transactionId, ulong clientId, CoinPickup pile)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (pile == null || pile.IsCollected || _coins == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.AlreadyTaken });
            var amount = pile.Amount;
            var distribution = _coins.Distribute(amount, $"pickup:{transactionId}");
            if (distribution.IsEmpty) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Rejected, Detail = "no participants" });
            pile.MarkCollectedByAuthority();
            LastDistribution = distribution;
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Accepted, Coins = amount, Detail = string.Join(",", distribution.Shares.Select(s => $"{s.ParticipantId}={s.Amount}")) });
        }

        public CoinDistributionResult LastDistribution { get; private set; }

        public LootTransactionResult RequestChestOpen(string transactionId, ulong clientId, SupplyChest chest)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (chest == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Unknown });
            var opened = chest.TryOpen(out var loot);
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = opened ? LootVerdict.Accepted : LootVerdict.AlreadyTaken, Quantity = opened ? loot.Items.Count : 0, Coins = opened ? loot.Coins : 0 });
        }

        public LootTransactionResult RequestEventActivate(string transactionId, ulong clientId, IDungeonEvent dungeonEvent, EventActor actor)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (dungeonEvent == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Unknown });
            var result = dungeonEvent.Activate(actor);
            var verdict = result.Outcome switch
            {
                DungeonEventOutcome.Success or DungeonEventOutcome.Started or DungeonEventOutcome.Failed => LootVerdict.Accepted,
                DungeonEventOutcome.None => LootVerdict.AlreadyTaken,
                _ => LootVerdict.Rejected
            };
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = verdict, Detail = result.Outcome.ToString(), Coins = result.CoinsSpent });
        }

        /// <summary>57.5/84 paid Medical Station revive of a fully Dead teammate: charged and applied exactly once per transaction id.</summary>
        public LootTransactionResult RequestMedicalRevive(string transactionId, ulong clientId, MedicalStationEvent station, EventActor requester, string targetParticipantId)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (station == null || requester == null) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Unknown });
            var result = station.RequestRevive(requester, targetParticipantId);
            var verdict = result.Outcome == DungeonEventOutcome.Success ? LootVerdict.Accepted : LootVerdict.Rejected;
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = verdict, Detail = result.Outcome + (string.IsNullOrEmpty(result.Detail) ? string.Empty : ":" + result.Detail), Coins = result.CoinsSpent });
        }

        public LootTransactionResult RequestMerchantBuy(string transactionId, ulong clientId, DungeonMerchantService merchant, int offerIndex)
        {
            if (!Gate(transactionId, clientId, out var cached)) return cached;
            if (merchant == null || !_participants.TryGetValue(clientId, out var participant)) return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = LootVerdict.Unknown });
            var offer = merchant.Offers.FirstOrDefault(o => o.Index == offerIndex);
            var error = merchant.Buy(offerIndex, participant.Backpack);
            var verdict = error == TradeError.None ? LootVerdict.Accepted : error == TradeError.AlreadySold ? LootVerdict.AlreadyTaken : LootVerdict.Rejected;
            return Finish(new LootTransactionResult { TransactionId = transactionId, ClientId = clientId, Verdict = verdict, Detail = error.ToString(), Coins = error == TradeError.None && offer != null ? offer.Price : 0, InstanceId = offer?.Item.InstanceId });
        }
    }

    /// <summary>Serializable ground state for late joiners: every unresolved pickup of the current depth.</summary>
    [Serializable]
    public sealed class GroundPickupRecord
    {
        public string InstanceId;
        public string DefinitionId;
        public int Quantity;
        public int Rarity;
        public int Coins;
        public Vector2 Position;
        public bool IsAtRisk;
        public AffixRoll[] AffixRolls;
    }

    [Serializable]
    public sealed class GroundLootSnapshot
    {
        public int Depth;
        public List<GroundPickupRecord> Pickups = new();

        public static GroundLootSnapshot Capture(GroundLootRegistry registry, int depth)
        {
            var snapshot = new GroundLootSnapshot { Depth = depth };
            foreach (var go in registry.Tracked)
            {
                if (go == null) continue;
                var item = go.GetComponent<WorldItemPickup>();
                if (item != null && item.Item != null && !item.IsConsumed)
                {
                    snapshot.Pickups.Add(new GroundPickupRecord { InstanceId = item.Item.InstanceId, DefinitionId = item.Item.DefinitionId, Quantity = item.Item.Quantity, Rarity = (int)item.Item.Rarity, Position = go.transform.position, IsAtRisk = item.Item.IsAtRisk, AffixRolls = item.Item.AffixRolls.ToArray() });
                    continue;
                }

                var coins = go.GetComponent<CoinPickup>();
                if (coins != null && !coins.IsCollected) snapshot.Pickups.Add(new GroundPickupRecord { Coins = coins.Amount, Position = go.transform.position });
            }

            return snapshot;
        }

        /// <summary>Rebuilds presentation pickups on a joining client with the host's instance ids (never new rolls).</summary>
        public int Restore(LootSpawner spawner, Func<string, ItemDefinition> resolveDefinition)
        {
            var count = 0;
            foreach (var record in Pickups)
            {
                if (record.Coins > 0)
                {
                    spawner.CreateCoinPickup(record.Position).SetAmount(record.Coins);
                }
                else
                {
                    var pickup = spawner.CreateItemPickup(record.Position);
                    var instance = ItemInstance.FromSnapshot(new ItemInstanceSnapshot { InstanceId = record.InstanceId, DefinitionId = record.DefinitionId, Quantity = record.Quantity, Rarity = record.Rarity, IsAtRisk = record.IsAtRisk, AffixRolls = record.AffixRolls });
                    var definition = resolveDefinition?.Invoke(record.DefinitionId);
                    pickup.Hold(instance, definition != null ? definition.Category : null);
                }

                count++;
            }

            return count;
        }
    }
}
