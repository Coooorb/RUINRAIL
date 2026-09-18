using System;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items.Consumables;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The two fully-Dead return sources (84 Dead): the Defibrillator consumable and the paid Medical Station revive.
    /// Both end in the same authoritative transition (life state back to Alive at the requested percent of Max HP,
    /// revive protection), which flips control, action gate and spectator follow back automatically. Only a fully
    /// Dead teammate other than the requester is a valid target; Downed/Alive/self are refused before anything is
    /// spent. Host-only (DamageAuthority); a client's request is refused, never applied.
    /// </summary>
    public sealed class PartyReviveAuthority : IReviveAuthority
    {
        private readonly PartyLifeRoster _roster;
        private readonly PlayerBalanceConfig _balance;

        public PartyReviveAuthority(PartyLifeRoster roster, PlayerBalanceConfig balance = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _balance = balance;
        }

        public PartyLifeRoster Roster => _roster;
        public int Revives { get; private set; }

        public event Action<PlayerLifeStateComponent, string> Revived;

        public PlayerLifeStateComponent Find(string participantId)
        {
            if (string.IsNullOrEmpty(participantId)) return null;
            foreach (var member in _roster.Members)
            {
                if (member != null && string.Equals(member.ParticipantId, participantId, StringComparison.Ordinal)) return member;
            }

            return null;
        }

        /// <summary>IReviveAuthority: fully Dead only (Downed is revived by the standard hold, never bought).</summary>
        public bool IsDead(string participantId)
        {
            var member = Find(participantId);
            return member != null && member.IsDead;
        }

        /// <summary>IReviveAuthority (Medical Station): the station has already validated cost; this applies the transition once.</summary>
        public bool Revive(string participantId, int healthPercent) => ReviveMember(Find(participantId), healthPercent, "medical_station");

        /// <summary>
        /// Defibrillator: the user revives the closest fully Dead teammate within reach. Returns true only when the
        /// revive happened, which is what lets the consumable spend its single unit (a refused use costs nothing).
        /// </summary>
        public bool ReviveWithDefibrillator(PlayerLifeStateComponent user, ReviveRequest request)
        {
            if (user == null || request == null || !user.CanAct) return false;
            var target = FindDeadTeammateInReach(user);
            return ReviveMember(target, request.HealthPercent, request.ConsumableId);
        }

        public float ReachTiles => _balance != null ? _balance.ReviveRangeTiles : 1.5f;

        public PlayerLifeStateComponent FindDeadTeammateInReach(PlayerLifeStateComponent user)
        {
            PlayerLifeStateComponent best = null;
            var bestDistance = float.MaxValue;
            foreach (var member in _roster.Members)
            {
                if (member == null || member == user || !member.IsDead) continue;
                var distance = Vector2.Distance(member.transform.position, user.transform.position);
                if (distance <= ReachTiles && distance < bestDistance)
                {
                    best = member;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private bool ReviveMember(PlayerLifeStateComponent member, int healthPercent, string source)
        {
            if (!DamageAuthority.LocalIsAuthoritative || member == null || !member.IsDead) return false;
            var health = member.GetComponent<HealthComponent>();
            var restored = Mathf.Max(1, Mathf.RoundToInt((health != null ? health.MaxHealth : 1) * Mathf.Clamp(healthPercent, 1, 100) / 100f));
            if (!member.ReturnToAlive(restored)) return false;
            var protection = member.GetComponent<ReviveProtection>();
            if (protection != null) protection.Begin(_balance != null ? _balance.ReviveProtectionSeconds : 1.5f);
            Revives++;
            Revived?.Invoke(member, source);
            return true;
        }
    }
}
