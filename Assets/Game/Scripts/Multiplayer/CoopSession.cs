using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>What the host knows about a joined member before the expedition: its Ready, loadout and attribute ranks.</summary>
    public sealed class CoopMemberProfile
    {
        public ulong ClientId;
        public string DisplayName;
        public bool Ready;
        public InventorySnapshot Loadout;
        public int[] SkillRanks = Array.Empty<int>();
        /// <summary>Banked Coins the member said it takes into the run (the lobby captures it at start).</summary>
        public int CarriedCoins;

        public SkillAllocation Skills()
        {
            var allocation = new SkillAllocation();
            for (var i = 0; i < SkillRules.All.Length && i < (SkillRanks?.Length ?? 0); i++) allocation.SetRank(SkillRules.All[i], SkillRanks[i]);
            return allocation;
        }

        public static int[] RanksOf(SkillAllocation allocation) =>
            allocation == null ? Array.Empty<int>() : SkillRules.All.Select(allocation.GetRank).ToArray();
    }

    /// <summary>
    /// The process's co-op session layer above NGO (81/82): it turns the live session into the party the expedition is
    /// started for, and every peer's expedition into the same one.
    ///
    /// Host: each joined client's Ready and loadout reach the host's <see cref="PartyLobby"/> (the lobby the host starts
    /// from); when the host's expedition starts, the authoritative start — seed, first biome, party size and a unique
    /// participant id per member — is published once and kept for late/reconnecting peers.
    /// Client: its own Ready/loadout travel to the host; the host's start arrives here and the client starts its own
    /// expedition transaction from it exactly once (never from a seed of its own).
    ///
    /// Lives for the whole process (the session outlives every scene); the link it talks through may be replaced when
    /// a client reconnects, so it always resolves the current one.
    /// </summary>
    public sealed class CoopSessionService : IDisposable
    {
        private readonly Dictionary<ulong, CoopMemberProfile> _profiles = new();
        private readonly ExpeditionStartCoordinator _coordinator = new();
        private CoopRunLink _link;
        private PartyLobby _hostLobby;
        private ExpeditionService _hostExpedition;
        private Func<string> _hostDisplayName;
        private PartyLobby _clientLobby;
        private ulong _clientLobbyMemberId;
        private Func<LobbyMemberMessage> _clientProfile;
        private ExpeditionService _clientExpedition;
        private PlayerProfile _clientPlayerProfile;
        private bool _disposed;

        public CoopSessionService()
        {
            CoopRunLink.Available += OnLinkAvailable;
            CoopRunLink.Lost += OnLinkLost;
            if (CoopRunLink.Current != null) OnLinkAvailable(CoopRunLink.Current);
        }

        /// <summary>The current session link (null outside a live session).</summary>
        public CoopRunLink Link => _link != null && _link.IsSpawned ? _link : null;

        public ICoopBus Bus => Link;
        public bool IsLive => Link != null;
        public bool IsHost => Link != null && Link.IsHost;
        public bool IsClient => Link != null && !Link.IsHost;
        public ulong LocalClientId => Link != null ? Link.LocalClientId : 0;

        /// <summary>The expedition the session is in (host: published; client: received). Null between runs.</summary>
        public RunStartMessage CurrentRun { get; private set; }

        /// <summary>This client's reconnect token and participant id as the host issued them (85).</summary>
        public ReconnectTokenMessage ReconnectToken { get; private set; }

        public IReadOnlyDictionary<ulong, CoopMemberProfile> MemberProfiles => _profiles;
        public LobbyStateMessage LastLobbyState { get; private set; }
        public int RunStartsPublished { get; private set; }
        public int RunStartsApplied => _coordinator.Applied;
        public int RunStartsIgnored => _coordinator.Ignored;
        public int LobbyMessagesApplied { get; private set; }

        public event Action<RunStartMessage> RunStartReceived;
        public event Action<RunEndedMessage> RunEndedReceived;
        public event Action<CoopRunLink> LinkAvailable;
        public event Action LinkLost;

        /// <summary>Raised on the host for every client message the session layer does not handle (the expedition runtime's).</summary>
        public event Action<ulong, string, string> Received;

        private void OnLinkAvailable(CoopRunLink link)
        {
            if (_link != null) _link.Received -= OnReceived;
            _link = link;
            _link.Received += OnReceived;
            if (_link.IsHost) PublishLobbyState();
            else SendClientLobbyMember();
            LinkAvailable?.Invoke(link);
        }

        private void OnLinkLost(CoopRunLink link)
        {
            if (link != _link) return;
            _link.Received -= OnReceived;
            _link = null;
            LinkLost?.Invoke();
        }

        // ---------------------------------------------------------------- host

        /// <summary>
        /// Host: joined clients' Ready/loadout feed this lobby, and this expedition's start becomes the published start.
        /// Called by the Shelter composition each time it builds (idempotent for the same lobby).
        /// </summary>
        public void BindHost(PartyLobby lobby, ExpeditionService expedition, Func<string> localDisplayName = null)
        {
            // The host's own lobby line and run member carry its saved profile name. Its participant id was fixed when
            // the Shelter session was created (before the name step), so it is identity, not a name to show.
            _hostDisplayName = localDisplayName;
            if (_hostExpedition != null) _hostExpedition.ExpeditionStarted -= OnHostExpeditionStarted;
            if (_hostLobby != null) _hostLobby.MemberChanged -= OnHostLobbyChanged;
            _hostLobby = lobby;
            _hostExpedition = expedition;
            if (_hostLobby != null) _hostLobby.MemberChanged += OnHostLobbyChanged;
            if (_hostExpedition != null) _hostExpedition.ExpeditionStarted += OnHostExpeditionStarted;
        }

        private void OnHostLobbyChanged(LobbyMember member)
        {
            if (IsHost) PublishLobbyState();
        }

        private void PublishLobbyState()
        {
            if (!IsHost || _hostLobby == null) return;
            var state = new LobbyStateMessage();
            foreach (var member in _hostLobby.Members)
            {
                state.Members.Add(new LobbyStateMessage.LobbyLine
                {
                    ClientId = member.ClientId,
                    Name = NameOf(member.ClientId, member.ParticipantId),
                    IsHost = member.IsHost,
                    Ready = member.IsReady,
                    ValidLoadout = member.HasValidLoadout
                });
            }

            LastLobbyState = state;
            Link.SendToClients(CoopKinds.LobbyState, CoopJson.Write(state));
        }

        /// <summary>
        /// Host start: the lobby captured the party and the host's own transaction exists, so every member gets its
        /// participant id now — the host's is its own transaction id, every other member a fresh unique id — and the
        /// start is published to the clients exactly once per expedition.
        /// </summary>
        private void OnHostExpeditionStarted(ExpeditionState state)
        {
            if (!IsHost || _hostLobby?.StartSnapshot == null || state == null) return;
            var snapshot = _hostLobby.StartSnapshot;
            if (CurrentRun != null && CurrentRun.StartId == snapshot.StartTransactionId) return;
            var run = new RunStartMessage
            {
                StartId = snapshot.StartTransactionId,
                RunSeed = state.RunSeed,
                Biome = (int)state.Biome,
                PartySize = snapshot.PartySize,
                Depth = state.Depth,
                ContentVersion = Application.version
            };
            foreach (var member in snapshot.Members.OrderBy(m => m.ClientId))
            {
                var isHost = member.ClientId == LocalClientId;
                run.Members.Add(new RunStartMessage.RunMember
                {
                    ClientId = member.ClientId,
                    ParticipantId = isHost ? state.TransactionId : Guid.NewGuid().ToString("N"),
                    DisplayName = NameOf(member.ClientId, member.ParticipantId),
                    IsHost = isHost,
                    CarriedCoins = member.CarriedCoins
                });
            }

            CurrentRun = run;
            RunStartsPublished++;
            Link.SendToClients(CoopKinds.RunStart, CoopJson.Write(run));
            Debug.Log($"COOP-SESSION host published run {run.StartId} seed={run.RunSeed} biome={(Biome)run.Biome} party={run.PartySize}.");
        }

        private string NameOf(ulong clientId, string participantId)
        {
            if (clientId == LocalClientId && _hostDisplayName != null)
            {
                var own = _hostDisplayName();
                if (!string.IsNullOrEmpty(own)) return own;
            }

            return _profiles.TryGetValue(clientId, out var p) && !string.IsNullOrEmpty(p.DisplayName) ? p.DisplayName : participantId;
        }

        /// <summary>Host: the expedition ended for the party (wipe/quit = failed, return = extracted).</summary>
        public void PublishRunEnded(bool extracted, string reason)
        {
            if (!IsHost) return;
            Link.SendToClients(CoopKinds.RunEnded, CoopJson.Write(new RunEndedMessage { Outcome = extracted ? 0 : 1, Reason = reason }));
        }

        /// <summary>Host: the run is over and the party reopens; the next start publishes a new run.</summary>
        public void ClearRun() => CurrentRun = null;

        /// <summary>Host: hands a member its reconnect token (85) so it can reclaim its character after a drop.</summary>
        public void SendReconnectToken(ulong clientId, string token, string participantId)
        {
            if (!IsHost || string.IsNullOrEmpty(token)) return;
            Link.SendToClient(clientId, CoopKinds.ReconnectToken, CoopJson.Write(new ReconnectTokenMessage { Token = token, ParticipantId = participantId }));
        }

        /// <summary>Host: re-sends the current run start to one (reconnected/late) client.</summary>
        public void ResendRunStart(ulong clientId)
        {
            if (!IsHost || CurrentRun == null) return;
            Link.SendToClient(clientId, CoopKinds.RunStart, CoopJson.Write(CurrentRun));
        }

        private void ApplyLobbyMember(ulong clientId, LobbyMemberMessage message)
        {
            if (message == null) return;
            if (!_profiles.TryGetValue(clientId, out var profile)) _profiles[clientId] = profile = new CoopMemberProfile { ClientId = clientId };
            profile.DisplayName = string.IsNullOrEmpty(message.DisplayName) ? profile.DisplayName : message.DisplayName;
            profile.Loadout = message.Loadout;
            profile.SkillRanks = message.SkillRanks ?? Array.Empty<int>();
            profile.Ready = message.Ready;
            profile.CarriedCoins = Math.Max(0, message.CarriedCoins);
            if (_hostLobby == null || _hostLobby.HasStarted) return;
            // The lobby validates the loadout itself (81); a changed loadout clears Ready exactly as a local edit does.
            if (_hostLobby.Get(clientId) == null) return;
            _hostLobby.SetLoadout(clientId, message.Loadout);
            _hostLobby.SetCarriedCoins(clientId, message.CarriedCoins);
            _hostLobby.SetReady(clientId, message.Ready);
            LobbyMessagesApplied++;
        }

        // ---------------------------------------------------------------- client

        /// <summary>
        /// Client: this peer's own lobby member (the Shelter's Ready and loadout) is mirrored to the host, and the host's
        /// start starts this peer's expedition through the same coordinator the host uses.
        /// </summary>
        public void BindClient(PartyLobby localLobby, ulong localLobbyMemberId, Func<LobbyMemberMessage> profile, ExpeditionService expedition, PlayerProfile playerProfile)
        {
            if (_clientLobby != null) _clientLobby.MemberChanged -= OnClientLobbyChanged;
            _clientLobby = localLobby;
            _clientLobbyMemberId = localLobbyMemberId;
            _clientProfile = profile;
            _clientExpedition = expedition;
            _clientPlayerProfile = playerProfile;
            if (_clientLobby != null) _clientLobby.MemberChanged += OnClientLobbyChanged;
            SendClientLobbyMember();
        }

        private void OnClientLobbyChanged(LobbyMember member)
        {
            if (member != null && member.ClientId == _clientLobbyMemberId) SendClientLobbyMember();
        }

        /// <summary>Client: sends this peer's current Ready/loadout/ranks to the host (also re-sent when a link appears).</summary>
        public void SendClientLobbyMember()
        {
            if (!IsClient || _clientLobby == null) return;
            var member = _clientLobby.Get(_clientLobbyMemberId);
            var message = _clientProfile?.Invoke() ?? new LobbyMemberMessage();
            message.Ready = member != null && member.IsReady;
            message.Loadout ??= member?.Loadout;
            message.CarriedCoins = member?.CarriedCoins ?? 0;
            Link.SendToHost(CoopKinds.LobbyMember, CoopJson.Write(message));
        }

        /// <summary>
        /// Client: starts this peer's own expedition transaction from the host's start (seed, biome, party size and the
        /// participant id the host assigned), exactly once per start id.
        /// </summary>
        public ExpeditionState ApplyRunStart(RunStartMessage run)
        {
            if (run == null || _clientExpedition == null || _clientPlayerProfile == null) return null;
            var me = run.MemberFor(LocalClientId);
            if (me == null) { Debug.LogError($"COOP-SESSION run {run.StartId} does not include client {LocalClientId}."); return null; }
            if (_clientExpedition.IsExpeditionActive && _clientExpedition.State.TransactionId == me.ParticipantId) return _clientExpedition.State;
            var snapshot = new ExpeditionStartSnapshot { StartTransactionId = run.StartId, RunSeed = run.RunSeed, Biome = run.Biome, PartySize = run.PartySize };
            // The coins are the amount the host captured for this member at start, so the host's seed of its wallet for
            // this member and this peer's own banked debit start from the same number.
            return _coordinator.Apply(snapshot, _clientExpedition, _clientPlayerProfile, me.ParticipantId, me.CarriedCoins);
        }

        // ---------------------------------------------------------------- dispatch

        private void OnReceived(ulong sender, string kind, string json)
        {
            if (Link == null) return;
            if (Link.IsHost)
            {
                switch (kind)
                {
                    case CoopKinds.LobbyMember:
                        ApplyLobbyMember(sender, CoopJson.Read<LobbyMemberMessage>(json));
                        PublishLobbyState();
                        return;
                }

                Received?.Invoke(sender, kind, json);
                return;
            }

            switch (kind)
            {
                case CoopKinds.LobbyState:
                    LastLobbyState = CoopJson.Read<LobbyStateMessage>(json);
                    return;
                case CoopKinds.RunStart:
                    var run = CoopJson.Read<RunStartMessage>(json);
                    if (run == null) return;
                    CurrentRun = run;
                    Debug.Log($"COOP-SESSION client {LocalClientId} received run {run.StartId} seed={run.RunSeed} biome={(Biome)run.Biome} party={run.PartySize}.");
                    RunStartReceived?.Invoke(run);
                    return;
                case CoopKinds.RunEnded:
                    RunEndedReceived?.Invoke(CoopJson.Read<RunEndedMessage>(json));
                    return;
                case CoopKinds.ReconnectToken:
                    ReconnectToken = CoopJson.Read<ReconnectTokenMessage>(json);
                    return;
            }

            Received?.Invoke(sender, kind, json);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CoopRunLink.Available -= OnLinkAvailable;
            CoopRunLink.Lost -= OnLinkLost;
            if (_link != null) _link.Received -= OnReceived;
            BindHost(null, null);
            if (_clientLobby != null) _clientLobby.MemberChanged -= OnClientLobbyChanged;
        }
    }
}
