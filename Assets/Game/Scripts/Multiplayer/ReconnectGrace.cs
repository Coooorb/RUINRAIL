using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// Connection-approval payload: the display name, optionally followed by the reconnect token the host issued
    /// to this player earlier in the session. Never anything else.
    /// </summary>
    public static class ConnectionPayload
    {
        private const char Separator = (char)0x1F;

        public static string Encode(string displayName, string reconnectToken = null)
        {
            var name = (displayName ?? string.Empty).Replace(Separator.ToString(), string.Empty);
            return string.IsNullOrEmpty(reconnectToken) ? name : name + Separator + reconnectToken;
        }

        public static (string displayName, string reconnectToken) Decode(string payload)
        {
            if (string.IsNullOrEmpty(payload)) return (string.Empty, null);
            var index = payload.IndexOf(Separator);
            if (index < 0) return (payload, null);
            var token = payload.Substring(index + 1);
            return (payload.Substring(0, index), string.IsNullOrEmpty(token) ? null : token);
        }
    }

    /// <summary>A disconnected member the host keeps represented and at risk until the grace deadline.</summary>
    public sealed class PendingReconnect
    {
        public PendingReconnect(NetworkPlayerEntity entity, string token, ulong previousClientId, double deadline)
        {
            Entity = entity;
            Token = token;
            PreviousClientId = previousClientId;
            Deadline = deadline;
        }

        public NetworkPlayerEntity Entity { get; }
        public string Token { get; }
        public ulong PreviousClientId { get; }
        public double Deadline { get; }
    }

    /// <summary>
    /// Host-side reconnect grace (85): a client that drops during an expedition keeps its entity, life state, item
    /// instances and Carried coins exactly as they are; its identity is reserved under a reconnect token. Reclaiming
    /// within the grace rebinds the same entity to the new connection (nothing respawned or duplicated). Expiry makes
    /// the character Dead once — gear is never dropped. Outside an expedition there is nothing to hold.
    /// </summary>
    public sealed class ReconnectGraceService
    {
        private readonly Dictionary<string, PendingReconnect> _pending = new(StringComparer.Ordinal);
        private readonly Func<bool> _expeditionActive;
        private readonly Func<double> _clock;

        /// <summary>85: "Initial reconnect grace target: ~60 s".</summary>
        public const float DefaultGraceSeconds = 60f;

        public ReconnectGraceService(float graceSeconds, Func<bool> expeditionActive, Func<double> clock = null)
        {
            GraceSeconds = Mathf.Max(0f, graceSeconds);
            _expeditionActive = expeditionActive ?? (() => false);
            _clock = clock ?? (() => Time.timeAsDouble);
        }

        public float GraceSeconds { get; }
        public IReadOnlyCollection<PendingReconnect> Pending => _pending.Values;
        public int Reclaimed { get; private set; }
        public int Expired { get; private set; }

        public event Action<PendingReconnect> Held;
        public event Action<PendingReconnect, ulong> ReclaimedBy;
        public event Action<PendingReconnect> GraceExpired;

        /// <summary>Only an active expedition holds a character; in the Base a dropped member is simply gone.</summary>
        public bool ShouldHold() => _expeditionActive() && GraceSeconds > 0f;

        public static string NewToken() => Guid.NewGuid().ToString("N");

        public PendingReconnect Hold(NetworkPlayerEntity entity, ulong previousClientId)
        {
            if (entity == null || string.IsNullOrEmpty(entity.ReconnectToken)) return null;
            var pending = new PendingReconnect(entity, entity.ReconnectToken, previousClientId, _clock() + GraceSeconds);
            _pending[pending.Token] = pending;
            Held?.Invoke(pending);
            return pending;
        }

        public bool HasPending(string token) => !string.IsNullOrEmpty(token) && _pending.ContainsKey(token);

        /// <summary>Rebinds the reserved entity to the new connection; a second reclaim of the same token finds nothing.</summary>
        public PendingReconnect TryReclaim(string token, ulong newClientId)
        {
            if (string.IsNullOrEmpty(token) || !_pending.TryGetValue(token, out var pending)) return null;
            if (_clock() > pending.Deadline)
            {
                Expire(pending);
                return null;
            }

            _pending.Remove(token);
            pending.Entity.Identity.ClientId = newClientId;
            Reclaimed++;
            ReclaimedBy?.Invoke(pending, newClientId);
            return pending;
        }

        /// <summary>Advances the grace: every expired reservation becomes Dead exactly once.</summary>
        public int Tick()
        {
            var now = _clock();
            var expired = _pending.Values.Where(p => now > p.Deadline).ToList();
            foreach (var pending in expired) Expire(pending);
            return expired.Count;
        }

        private void Expire(PendingReconnect pending)
        {
            if (!_pending.Remove(pending.Token)) return;
            var life = pending.Entity.GameObject != null ? pending.Entity.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
            life?.MarkDeadByAuthority("reconnect_grace_expired");
            Expired++;
            GraceExpired?.Invoke(pending);
        }
    }

    /// <summary>
    /// Binds the session lifecycle to this peer's expedition transaction (85 Host Disconnect / quit rule): losing the
    /// session while an expedition is active (host gone, transport dropped, or leaving mid-run) fails the expedition
    /// exactly once — the at-risk state is lost, nothing is partially extracted. No host migration exists in V1.
    /// </summary>
    public sealed class SessionExpeditionBinding : IDisposable
    {
        private readonly NetworkSessionController _session;
        private readonly ExpeditionService _expedition;

        public SessionExpeditionBinding(NetworkSessionController session, ExpeditionService expedition)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
            _session.StateChanged += OnStateChanged;
        }

        public int Failures { get; private set; }
        public string LastReason { get; private set; }

        private void OnStateChanged(NetworkSessionController controller, NetworkLifecycleState state)
        {
            if (state != NetworkLifecycleState.Failed && state != NetworkLifecycleState.Leaving) return;
            if (!_expedition.IsExpeditionActive) return;
            Failures++;
            LastReason = state == NetworkLifecycleState.Failed ? (controller.LastError.Message ?? "connection lost") : "left session";
            _expedition.Fail();
        }

        public void Dispose()
        {
            _session.StateChanged -= OnStateChanged;
        }
    }
}
