using System;
using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Gameplay.Expedition
{
    public enum TransitChoice
    {
        ReturnToShelter,
        DescendDeeper
    }

    public enum TransitDecisionState
    {
        Closed,
        Open,
        Resolved
    }

    /// <summary>
    /// Turns submitted choices into a party decision. Solo resolves on the first choice; the co-op policy
    /// (86_TRANSIT_VOTING: Continue requires unanimity of living players, any Return returns everyone) plugs in here
    /// without touching the extraction transactions.
    /// </summary>
    public interface ITransitResolutionPolicy
    {
        /// <summary>Returns the party choice once it is decidable, otherwise null.</summary>
        TransitChoice? Resolve(IReadOnlyDictionary<string, TransitChoice> choices, IReadOnlyCollection<string> livingPlayerIds);
    }

    public sealed class SoloTransitPolicy : ITransitResolutionPolicy
    {
        public TransitChoice? Resolve(IReadOnlyDictionary<string, TransitChoice> choices, IReadOnlyCollection<string> livingPlayerIds)
        {
            return choices.Count > 0 ? choices.Values.First() : null;
        }
    }

    /// <summary>
    /// 86 co-op rule: any living player's Return returns the whole party at once; Descend needs every living player's
    /// Descend. Only living voters count (Dead players have no vote); a voter that dies or expires is removed by the
    /// decision, so the vote can never wait forever on someone who cannot answer.
    /// </summary>
    public sealed class PartyTransitPolicy : ITransitResolutionPolicy
    {
        public TransitChoice? Resolve(IReadOnlyDictionary<string, TransitChoice> choices, IReadOnlyCollection<string> livingPlayerIds)
        {
            var living = livingPlayerIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (living.Count == 0) return null;
            foreach (var id in living)
            {
                if (choices.TryGetValue(id, out var choice) && choice == TransitChoice.ReturnToShelter) return TransitChoice.ReturnToShelter;
            }

            return living.All(id => choices.TryGetValue(id, out var choice) && choice == TransitChoice.DescendDeeper) ? TransitChoice.DescendDeeper : null;
        }
    }

    /// <summary>
    /// 82/86 on a co-op client: the client's copy of the decision never resolves by itself — votes are forwarded to the
    /// host, and the host's result arrives through <see cref="TransitDecision.ResolveFromAuthority"/>.
    /// </summary>
    public sealed class HostDecidedTransitPolicy : ITransitResolutionPolicy
    {
        public TransitChoice? Resolve(IReadOnlyDictionary<string, TransitChoice> choices, IReadOnlyCollection<string> livingPlayerIds) => null;
    }

    /// <summary>86: what the party is told before confirming Return while a teammate is still Dead.</summary>
    public sealed class TransitReturnWarning
    {
        public TransitReturnWarning(IReadOnlyList<string> deadPlayerIds)
        {
            DeadPlayerIds = deadPlayerIds;
        }

        public IReadOnlyList<string> DeadPlayerIds { get; }
        public bool IsRequired => DeadPlayerIds.Count > 0;
        public string Message => IsRequired
            ? $"{string.Join(", ", DeadPlayerIds)} {(DeadPlayerIds.Count == 1 ? "is" : "are")} Dead. Returning now loses their carried gear, loot and coins."
            : string.Empty;
    }

    /// <summary>
    /// The post-boss decision (60_EXTRACTION_TRANSIT step 6). Opens once per depth after the boss falls, collects
    /// choices per living player id, resolves exactly once through the policy and never re-resolves. The living set
    /// can change while open (a voter dies or is revived); Dead players are never voters. The whole party moves as one:
    /// there is one Result, never a per-player transition.
    /// </summary>
    public sealed class TransitDecision
    {
        private readonly ITransitResolutionPolicy _policy;
        private readonly Dictionary<string, TransitChoice> _choices = new();
        private readonly HashSet<string> _livingPlayers = new();
        private readonly List<string> _deadPlayers = new();

        public TransitDecision(ITransitResolutionPolicy policy, IEnumerable<string> livingPlayerIds, IEnumerable<string> deadPlayerIds = null)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
            foreach (var id in livingPlayerIds) _livingPlayers.Add(id);
            if (deadPlayerIds != null) _deadPlayers.AddRange(deadPlayerIds.Where(id => !string.IsNullOrEmpty(id) && !_livingPlayers.Contains(id)));
        }

        public TransitDecisionState State { get; private set; } = TransitDecisionState.Closed;
        public TransitChoice? Result { get; private set; }
        public IReadOnlyDictionary<string, TransitChoice> Choices => _choices;
        public IReadOnlyCollection<string> LivingPlayers => _livingPlayers;
        public IReadOnlyList<string> DeadPlayers => _deadPlayers;
        public int Submissions { get; private set; }
        public int Resolutions { get; private set; }

        /// <summary>Living players who have not voted yet (what the vote is still waiting for).</summary>
        public IEnumerable<string> PendingVoters => _livingPlayers.Where(id => !_choices.ContainsKey(id));

        /// <summary>86: the Return warning to surface before confirmation whenever a teammate is still Dead.</summary>
        public TransitReturnWarning ReturnWarning => new(_deadPlayers.ToArray());
        public bool RequiresReturnWarning => _deadPlayers.Count > 0;

        public event Action<TransitDecision, TransitChoice> Resolved;
        public event Action<TransitDecision> VotersChanged;

        /// <summary>A living player's choice was recorded (co-op: a client forwards its own vote to the host from here).</summary>
        public event Action<TransitDecision, string, TransitChoice> Submitted;

        /// <summary>
        /// 86 on a co-op client: the party decision is the host's, never re-derived locally. The client's copy of the
        /// decision takes the host's resolved result exactly once (a repeat is ignored), which runs this peer's own
        /// Descend or Return transaction through the same Resolved path a local resolution would.
        /// </summary>
        public bool ResolveFromAuthority(TransitChoice choice)
        {
            if (State == TransitDecisionState.Resolved) return false;
            State = TransitDecisionState.Resolved;
            Result = choice;
            Resolutions++;
            Resolved?.Invoke(this, choice);
            return true;
        }

        /// <summary>A client mirrors another member's recorded vote for display; it never resolves anything.</summary>
        public bool MirrorVote(string playerId, TransitChoice choice)
        {
            if (!CanVote(playerId)) return false;
            if (_choices.TryGetValue(playerId, out var existing) && existing == choice) return false;
            _choices[playerId] = choice;
            VotersChanged?.Invoke(this);
            return true;
        }

        public void Open()
        {
            if (State != TransitDecisionState.Closed) return;
            State = TransitDecisionState.Open;
        }

        public bool CanVote(string playerId) => State == TransitDecisionState.Open && !string.IsNullOrEmpty(playerId) && _livingPlayers.Contains(playerId);

        /// <summary>Records a living player's choice (a changed vote replaces the previous one); true when this submission resolved the decision.</summary>
        public bool Submit(string playerId, TransitChoice choice)
        {
            if (!CanVote(playerId))
            {
                return false;
            }

            Submissions++;
            _choices[playerId] = choice;
            Submitted?.Invoke(this, playerId, choice);
            return TryResolve();
        }

        /// <summary>A voter died (or its grace expired): it loses its vote; the decision re-evaluates without it.</summary>
        public bool RemoveVoter(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || !_livingPlayers.Remove(playerId)) return false;
            _choices.Remove(playerId);
            if (!_deadPlayers.Contains(playerId)) _deadPlayers.Add(playerId);
            VotersChanged?.Invoke(this);
            if (State == TransitDecisionState.Open) TryResolve();
            return true;
        }

        /// <summary>A player returned to the living (revive) while the vote is open: it gains a vote and must cast it.</summary>
        public bool AddVoter(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || State == TransitDecisionState.Resolved || !_livingPlayers.Add(playerId)) return false;
            _deadPlayers.Remove(playerId);
            VotersChanged?.Invoke(this);
            return true;
        }

        private bool TryResolve()
        {
            if (State != TransitDecisionState.Open) return false;
            var result = _policy.Resolve(_choices, _livingPlayers);
            if (result == null)
            {
                return false;
            }

            State = TransitDecisionState.Resolved;
            Result = result;
            Resolutions++;
            Resolved?.Invoke(this, result.Value);
            return true;
        }
    }
}
