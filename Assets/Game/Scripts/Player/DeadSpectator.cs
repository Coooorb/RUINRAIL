using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Expedition;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Which teammate a Dead player watches (85 Dead Spectator): only living (not Dead) party members are valid
    /// targets, the selection cycles through them in roster order, and there is no free position at all — the camera
    /// can only ever sit on a teammate, so undiscovered areas cannot be scouted.
    /// </summary>
    public sealed class SpectatorTargetSelector
    {
        private readonly PartyLifeRoster _roster;
        private readonly PlayerLifeStateComponent _self;
        private readonly List<PlayerLifeStateComponent> _candidates = new();

        public SpectatorTargetSelector(PartyLifeRoster roster, PlayerLifeStateComponent self)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _self = self;
        }

        public PlayerLifeStateComponent Current { get; private set; }
        public IReadOnlyList<PlayerLifeStateComponent> Candidates
        {
            get
            {
                Refresh();
                return _candidates;
            }
        }

        public event Action<PlayerLifeStateComponent> TargetChanged;

        /// <summary>Re-reads the living teammates; drops a target that died or left and picks the first valid one.</summary>
        public void Refresh()
        {
            _candidates.Clear();
            foreach (var member in _roster.Members)
            {
                if (member != null && member != _self && !member.IsDead) _candidates.Add(member);
            }

            if (Current != null && _candidates.Contains(Current)) return;
            Select(_candidates.Count > 0 ? _candidates[0] : null);
        }

        public void CycleNext() => Cycle(+1);
        public void CyclePrevious() => Cycle(-1);

        private void Cycle(int step)
        {
            Refresh();
            if (_candidates.Count == 0) return;
            var index = Current != null ? _candidates.IndexOf(Current) : -1;
            index = ((index + step) % _candidates.Count + _candidates.Count) % _candidates.Count;
            Select(_candidates[index]);
        }

        private void Select(PlayerLifeStateComponent target)
        {
            if (target == Current) return;
            Current = target;
            TargetChanged?.Invoke(target);
        }
    }


    /// <summary>
    /// Binds the party's life states to this peer's expedition transactions (84):
    ///   • team wipe (nobody Alive) → the expedition fails immediately, exactly once, never waiting for bleedouts;
    ///   • Return while the local player is Dead → the local at-risk state is lost (ExpeditionService.ReturnWithParty);
    ///   • Descend never consults life state: a Dead player rides along as spectator;
    ///   • 86 voting: the living (not Dead) members are the voters when the boss falls, Dead members feed the Return
    ///     warning, a voter that dies while the vote is open loses its vote (no deadlock on someone who cannot answer),
    ///     a revived player gains one. A disconnected player in reconnect grace is still living (its vote is awaited)
    ///     and either returns to cast it or expires to Dead within the grace, which removes it.
    /// </summary>
    public sealed class PartyExpeditionBinding : IDisposable
    {
        private readonly ExpeditionService _expedition;
        private readonly PartyLifeRoster _roster;
        private readonly PlayerLifeStateComponent _local;

        public PartyExpeditionBinding(ExpeditionService expedition, PartyLifeRoster roster, PlayerLifeStateComponent localPlayer)
        {
            _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _local = localPlayer;
            _expedition.SetLocalLifeCheck(() => _local != null && _local.IsDead);
            _expedition.SetPartyTransit(LivingIds, DeadIds, new PartyTransitPolicy());
            _roster.TeamWiped += OnTeamWiped;
            _roster.MemberStateChanged += OnMemberStateChanged;
        }

        private IEnumerable<string> LivingIds() => _roster.Members.Where(m => m != null && !m.IsDead).Select(m => m.ParticipantId);
        private IEnumerable<string> DeadIds() => _roster.Members.Where(m => m != null && m.IsDead).Select(m => m.ParticipantId);

        private void OnMemberStateChanged(PlayerLifeStateComponent member, PlayerLifeState from, PlayerLifeState to)
        {
            var transit = _expedition.Transit;
            if (transit == null || transit.State != TransitDecisionState.Open) return;
            if (to == PlayerLifeState.Dead) transit.RemoveVoter(member.ParticipantId);
            else if (from == PlayerLifeState.Dead) transit.AddVoter(member.ParticipantId);
        }

        public int WipeFailures { get; private set; }

        /// <summary>
        /// False on a co-op client (82/84): the wipe is the host's decision, which reaches the client as the run's end;
        /// a client's replicated view of the party must never fail its own transaction from a transient state.
        /// </summary>
        public bool FailOnWipe { get; set; } = true;

        private void OnTeamWiped(PartyLifeRoster roster)
        {
            if (!FailOnWipe || !_expedition.IsExpeditionActive) return;
            WipeFailures++;
            _expedition.Fail();
        }

        public void Dispose()
        {
            _roster.TeamWiped -= OnTeamWiped;
            _roster.MemberStateChanged -= OnMemberStateChanged;
            _expedition.SetLocalLifeCheck(null);
            _expedition.SetPartyTransit(null, null, null);
        }
    }
}
