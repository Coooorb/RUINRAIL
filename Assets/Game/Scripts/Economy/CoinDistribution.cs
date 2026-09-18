using System;
using System.Collections.Generic;

namespace RuinRail.Gameplay.Economy
{
    /// <summary>One participant's share of a distributed coin amount.</summary>
    public readonly struct CoinShare
    {
        public CoinShare(string participantId, int amount)
        {
            ParticipantId = participantId;
            Amount = amount;
        }

        public string ParticipantId { get; }
        public int Amount { get; }
    }

    /// <summary>
    /// The authoritative outcome of one coin pickup distribution: the total, the per-participant shares (which always
    /// sum to the total) and the sequence number that fixed the remainder rotation. A host can serialize this as-is.
    /// </summary>
    public sealed class CoinDistributionResult
    {
        public CoinDistributionResult(int total, IReadOnlyList<CoinShare> shares, int sequence, string reason)
        {
            Total = total;
            Shares = shares ?? Array.Empty<CoinShare>();
            Sequence = sequence;
            Reason = reason;
        }

        public int Total { get; }
        public IReadOnlyList<CoinShare> Shares { get; }
        public int Sequence { get; }
        public string Reason { get; }
        public bool IsEmpty => Total <= 0 || Shares.Count == 0;

        public int ShareFor(string participantId)
        {
            foreach (var share in Shares)
            {
                if (share.ParticipantId == participantId) return share.Amount;
            }

            return 0;
        }
    }

    /// <summary>
    /// 58 / 32: coins found as world currency are distributed evenly across the current expedition participants.
    /// Pure and deterministic: total / n each; the total % n leftover coins go one each to consecutive participants
    /// in roster order starting at index (sequence % n), so successive pickups rotate who receives the odd coin and
    /// every participant is treated equally over time. Solo (n = 1) receives the full amount. Shares always conserve
    /// the total exactly; nothing is rounded away.
    /// </summary>
    public static class CoinDistribution
    {
        public static IReadOnlyList<CoinShare> Split(int total, IReadOnlyList<string> participantIds, int sequence = 0)
        {
            if (participantIds == null || participantIds.Count == 0 || total <= 0)
            {
                return Array.Empty<CoinShare>();
            }

            var n = participantIds.Count;
            var each = total / n;
            var remainder = total % n;
            var start = ((sequence % n) + n) % n;
            var shares = new CoinShare[n];
            for (var i = 0; i < n; i++)
            {
                // Distance from the rotation start in roster order; the first `remainder` participants get +1.
                var offset = (i - start + n) % n;
                shares[i] = new CoinShare(participantIds[i], each + (offset < remainder ? 1 : 0));
            }

            return shares;
        }
    }

    /// <summary>Receives one pickup's coins and applies the party rule (solo: full amount to the local wallet).</summary>
    public interface ICoinDistributor
    {
        CoinDistributionResult Distribute(int amount, string reason);
    }

    /// <summary>A participant of the current expedition's coin bookkeeping: an id and the Carried wallet it owns.</summary>
    public sealed class CoinParticipant
    {
        public CoinParticipant(string id, CoinWallet wallet)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Participant id required.", nameof(id));
            Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            if (wallet.Domain != CoinDomain.Carried) throw new ArgumentException("Pickups credit Carried Coins only.", nameof(wallet));
            Id = id;
        }

        public string Id { get; }
        public CoinWallet Wallet { get; }
    }

    /// <summary>
    /// Credits a picked-up amount evenly into every current participant's Carried wallet (77: world coins never touch
    /// Banked Coins). The roster is supplied by the party bookkeeping; with one participant this is the solo path.
    /// Transport-agnostic: the host runs it and can forward the returned result; nothing here knows about the network.
    /// </summary>
    public sealed class PartyCoinDistributor : ICoinDistributor
    {
        private readonly List<CoinParticipant> _participants = new();
        private int _sequence;

        public PartyCoinDistributor(params CoinParticipant[] participants)
        {
            SetParticipants(participants);
        }

        public IReadOnlyList<CoinParticipant> Participants => _participants;
        public int Sequence => _sequence;

        public event Action<CoinDistributionResult> Distributed;

        /// <summary>Replaces the roster (order = roster order used by the remainder rotation). Duplicate ids are rejected.</summary>
        public void SetParticipants(IEnumerable<CoinParticipant> participants)
        {
            var next = new List<CoinParticipant>();
            if (participants != null)
            {
                foreach (var p in participants)
                {
                    if (p == null) continue;
                    if (next.Exists(x => x.Id == p.Id)) throw new ArgumentException($"Duplicate participant '{p.Id}'.", nameof(participants));
                    next.Add(p);
                }
            }

            _participants.Clear();
            _participants.AddRange(next);
        }

        public CoinDistributionResult Distribute(int amount, string reason)
        {
            if (amount <= 0 || _participants.Count == 0)
            {
                return new CoinDistributionResult(0, Array.Empty<CoinShare>(), _sequence, reason);
            }

            var ids = new string[_participants.Count];
            for (var i = 0; i < ids.Length; i++) ids[i] = _participants[i].Id;
            var sequence = _sequence++;
            var shares = CoinDistribution.Split(amount, ids, sequence);
            for (var i = 0; i < shares.Count; i++)
            {
                if (shares[i].Amount > 0) _participants[i].Wallet.Credit(shares[i].Amount, reason);
            }

            var result = new CoinDistributionResult(amount, shares, sequence, reason);
            Distributed?.Invoke(result);
            return result;
        }
    }
}
