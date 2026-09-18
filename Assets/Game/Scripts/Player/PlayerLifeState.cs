using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>15: Alive, Downed (co-op only), Dead. There is no down counter (84).</summary>
    public enum PlayerLifeState
    {
        Alive,
        Downed,
        Dead
    }

    /// <summary>
    /// The expedition party's life states (host-side registry). Co-op = more than one member. "Can act" = Alive: a
    /// Downed or Dead teammate cannot revive anyone, so the last standing player at 0 HP dies outright and a party
    /// with nobody Alive is wiped (84 Team Wipe; the failure transaction itself is bound by the expedition layer).
    /// </summary>
    public sealed class PartyLifeRoster
    {
        private readonly List<PlayerLifeStateComponent> _members = new();

        private ReviveArbiter _revives;

        public IReadOnlyList<PlayerLifeStateComponent> Members => _members;
        /// <summary>The party's host-side revive arbitration (84 Standard Revive).</summary>
        public ReviveArbiter Revives => _revives ??= new ReviveArbiter();
        public int Count => _members.Count;
        public bool IsCoop => _members.Count > 1;
        public bool AnyoneAlive => _members.Any(m => m != null && m.State == PlayerLifeState.Alive);
        public bool IsWiped => _members.Count > 0 && !AnyoneAlive;

        public event Action<PlayerLifeStateComponent, PlayerLifeState, PlayerLifeState> MemberStateChanged;
        /// <summary>Raised once per wipe: every member is Downed or Dead, so no revive is possible (84).</summary>
        public event Action<PartyLifeRoster> TeamWiped;

        public void Register(PlayerLifeStateComponent member)
        {
            if (member == null || _members.Contains(member)) return;
            _members.Add(member);
            member.StateChanged += OnMemberStateChanged;
        }

        public void Unregister(PlayerLifeStateComponent member)
        {
            if (member == null || !_members.Remove(member)) return;
            member.StateChanged -= OnMemberStateChanged;
        }

        /// <summary>True when a teammate other than <paramref name="self"/> is Alive and could therefore revive.</summary>
        public bool AnyTeammateCanAct(PlayerLifeStateComponent self)
        {
            foreach (var member in _members)
            {
                if (member != null && member != self && member.State == PlayerLifeState.Alive) return true;
            }

            return false;
        }

        private bool _wipeRaised;

        private void OnMemberStateChanged(PlayerLifeStateComponent member, PlayerLifeState from, PlayerLifeState to)
        {
            MemberStateChanged?.Invoke(member, from, to);
            if (to == PlayerLifeState.Alive)
            {
                _wipeRaised = false;
            }
            else if (!_wipeRaised && IsWiped)
            {
                _wipeRaised = true;
                TeamWiped?.Invoke(this);
            }
        }
    }

}
