using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;

namespace RuinRail.UI.Multiplayer
{
    public enum TerminalAction
    {
        Solo,
        Host,
        Join,
        Leave,
        ToggleReady,
        CopyCode
    }

    /// <summary>One party line at the terminal (94 Multiplayer): name, host flag, Ready and loadout validity.</summary>
    public sealed class TerminalRosterLine
    {
        public string Name = string.Empty;
        public bool IsHost;
        public bool IsReady;
        public bool HasValidLoadout;
        public bool IsLocal;
        public string StatusText => IsReady ? "READY" : HasValidLoadout ? "NOT READY" : "LOADOUT INVALID";
    }

    /// <summary>
    /// Multiplayer Terminal (94/81): Solo / Host / Join by code / Leave, the join code to share (copy), the party
    /// roster with Ready states (max 3). Every action goes through MultiplayerTerminalService and PartyLobby; the
    /// view model only mirrors their authoritative state and maps SessionError to the approved English text —
    /// raw exception text never reaches the screen. A selection index gives keyboard/controller navigation.
    /// </summary>
    public sealed class TerminalViewModel : IDisposable
    {
        private readonly MultiplayerTerminalService _terminal;
        private readonly PartyLobby _lobby;
        private readonly ulong _localClientId;
        private readonly SessionRoster _roster;

        public TerminalViewModel(MultiplayerTerminalService terminal, PartyLobby lobby, ulong localClientId, SessionRoster roster = null)
        {
            _terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
            _lobby = lobby ?? throw new ArgumentNullException(nameof(lobby));
            _localClientId = localClientId;
            _roster = roster;
            _terminal.Changed += OnTerminalChanged;
            _lobby.MemberChanged += OnMemberChanged;
        }

        public static readonly TerminalAction[] Actions = (TerminalAction[])Enum.GetValues(typeof(TerminalAction));
        public int Selection { get; private set; }
        public TerminalAction SelectedAction => Actions[Selection];
        public string JoinCodeInput { get; private set; } = string.Empty;
        public string JoinCodeToShare => _terminal.JoinCodeToShare;
        public bool IsInSession => _terminal.IsInSession;
        public bool IsBusy => _terminal.IsBusy;
        public TerminalState State => _terminal.State;
        public int MaxPartySize => _terminal.MaxPartySize;
        public string ErrorText => _terminal.LastError.IsNone ? string.Empty : _terminal.LastError.Message;
        public bool ErrorIsRetryable => !_terminal.LastError.IsNone && _terminal.LastError.IsRetryable;
        public int CopyRequests { get; private set; }
        public string LastCopiedCode { get; private set; }

        /// <summary>
        /// Owner hook run right before the local player goes Ready: returns true when it changed the loadout (the
        /// Starter Loadout fallback for a player with nothing equipped, base/75), which the terminal then reports as
        /// a notice. Null = nothing to prepare.
        /// </summary>
        public Func<bool> PrepareLoadout { get; set; }

        /// <summary>The non-blocking notice shown by the station after a Ready that prepared the loadout (empty otherwise).</summary>
        public string Notice { get; private set; } = string.Empty;
        public const string StarterLoadoutEquippedNotice = "STARTER LOADOUT EQUIPPED";

        public event Action<TerminalViewModel> Changed;

        public string StatusText => _terminal.State switch
        {
            TerminalState.Idle => "Choose Solo, Host or Join.",
            TerminalState.Solo => "Solo expedition.",
            TerminalState.Busy => "Connecting…",
            TerminalState.InSession => _terminal.Mode == TerminalMode.HostCoop ? $"Hosting — join code {JoinCodeToShare}" : "In party.",
            TerminalState.Error => ErrorText,
            _ => string.Empty
        };

        public static string Label(TerminalAction action) => action switch
        {
            TerminalAction.Solo => "SOLO",
            TerminalAction.Host => "HOST CO-OP",
            TerminalAction.Join => "JOIN BY CODE",
            TerminalAction.Leave => "LEAVE SESSION",
            TerminalAction.ToggleReady => "READY",
            TerminalAction.CopyCode => "COPY JOIN CODE",
            _ => action.ToString().ToUpperInvariant()
        };

        public bool IsEnabled(TerminalAction action) => action switch
        {
            TerminalAction.Leave => IsInSession,
            TerminalAction.CopyCode => !string.IsNullOrEmpty(JoinCodeToShare),
            TerminalAction.ToggleReady => _lobby.Get(_localClientId) != null && !_lobby.HasStarted,
            TerminalAction.Host or TerminalAction.Join => !IsInSession && !IsBusy,
            _ => !IsBusy
        };

        /// <summary>Optional name source for joined members (host: the name each client reported); null falls back to the roster.</summary>
        public Func<ulong, string> MemberName { get; set; }

        public IReadOnlyList<TerminalRosterLine> Roster => _lobby.Members.Select(m => new TerminalRosterLine
        {
            Name = MemberName?.Invoke(m.ClientId) ?? _roster?.Get(m.ClientId)?.DisplayName ?? m.ParticipantId,
            IsHost = m.IsHost,
            IsReady = m.IsReady,
            HasValidLoadout = m.HasValidLoadout,
            IsLocal = m.ClientId == _localClientId
        }).ToList();

        // ---- Navigation ----

        public void MoveSelection(int delta)
        {
            Selection = ((Selection + delta) % Actions.Length + Actions.Length) % Actions.Length;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        public void SetJoinCodeInput(string raw)
        {
            JoinCodeInput = JoinCode.Normalize(raw ?? string.Empty);
            Raise();
        }

        public bool JoinCodeInputIsWellFormed => JoinCode.IsWellFormed(JoinCodeInput);

        /// <summary>Activates the selected action; async actions complete through the terminal's Changed event.</summary>
        public Task<SessionError> ActivateAsync() => ActivateAsync(SelectedAction);

        public async Task<SessionError> ActivateAsync(TerminalAction action)
        {
            var result = await ActivateCoreAsync(action);
            RuinRail.Core.Rendering.UiSoundBus.Raise(result.IsNone ? RuinRail.Core.Rendering.UiSound.Confirm : RuinRail.Core.Rendering.UiSound.Failure);
            return result;
        }

        private async Task<SessionError> ActivateCoreAsync(TerminalAction action)
        {
            if (!IsEnabled(action)) return SessionError.For(ServiceErrorKind.InvalidState);
            switch (action)
            {
                case TerminalAction.Solo: return await _terminal.SelectSoloAsync();
                case TerminalAction.Host: return await _terminal.HostCoopAsync();
                case TerminalAction.Join:
                    if (!JoinCodeInputIsWellFormed) return SessionError.For(ServiceErrorKind.InvalidCode);
                    return await _terminal.JoinCoopAsync(JoinCodeInput);
                case TerminalAction.Leave: return await _terminal.LeaveAsync();
                case TerminalAction.ToggleReady:
                    var member = _lobby.Get(_localClientId);
                    var goingReady = !member.IsReady;
                    // Going Ready with nothing equipped is not a refusal: the owner's hook equips the Starter Loadout first.
                    Notice = goingReady && PrepareLoadout != null && PrepareLoadout() ? StarterLoadoutEquippedNotice : string.Empty;
                    _lobby.SetReady(_localClientId, goingReady);
                    Raise();
                    return SessionError.None;
                case TerminalAction.CopyCode:
                    CopyRequests++;
                    LastCopiedCode = JoinCodeToShare;
                    Raise();
                    return SessionError.None;
                default: return SessionError.None;
            }
        }

        private void OnTerminalChanged(MultiplayerTerminalService _) => Raise();
        private void OnMemberChanged(LobbyMember _) => Raise();
        private void Raise() => Changed?.Invoke(this);

        public void Dispose()
        {
            _terminal.Changed -= OnTerminalChanged;
            _lobby.MemberChanged -= OnMemberChanged;
        }
    }

    /// <summary>What the in-run party panel shows for one teammate (84/85).</summary>
    public sealed class PartyMemberStatus
    {
        public string Name = string.Empty;
        public PlayerLifeState LifeState;
        public bool IsDisconnected;
        public float BleedoutRemaining;
        public float ReviveProgress01;
        public string Text => IsDisconnected ? $"{Name}: DISCONNECTED (reconnecting…)"
            : LifeState == PlayerLifeState.Downed ? (ReviveProgress01 > 0f ? $"{Name}: REVIVING {Mathf.RoundToInt(ReviveProgress01 * 100f)}%" : $"{Name}: DOWNED {Mathf.CeilToInt(BleedoutRemaining)}s")
            : LifeState == PlayerLifeState.Dead ? $"{Name}: DEAD"
            : $"{Name}: ALIVE";
    }

    /// <summary>
    /// In-run co-op status (84/85): the local revive prompt/progress, own Downed bleedout or being-revived progress,
    /// the Dead spectator target with the cycle prompt, teammates' life/connection states and the host-failure outcome.
    /// Reads authoritative models only; nothing here can force a revive, a ready or a vote.
    /// </summary>
    public sealed class PartyStatusViewModel
    {
        private readonly PartyLifeRoster _roster;
        private readonly PlayerLifeStateComponent _local;
        private readonly PlayerReviver _reviver;
        private readonly DeadSpectatorFollow _spectator;
        private readonly Func<string, bool> _isDisconnected;
        private readonly Func<string, string> _displayName;

        public PartyStatusViewModel(PartyLifeRoster roster, PlayerLifeStateComponent local, PlayerReviver reviver = null, DeadSpectatorFollow spectator = null, Func<string, bool> isDisconnected = null, Func<string, string> displayName = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _local = local;
            _reviver = reviver;
            _spectator = spectator;
            _isDisconnected = isDisconnected ?? (_ => false);
            _displayName = displayName ?? (id => id);
        }

        public string SessionOutcomeText { get; private set; } = string.Empty;

        /// <summary>85: host loss / connection loss as a plain outcome, never an exception string.</summary>
        public void ReportSessionLost(NetworkLifecycleState state, bool expeditionFailed)
        {
            SessionOutcomeText = expeditionFailed
                ? "Connection to the host was lost. The expedition failed: carried gear, loot and coins are lost; XP is kept."
                : state == NetworkLifecycleState.Failed ? "Connection lost." : string.Empty;
        }

        public IReadOnlyList<PartyMemberStatus> Members => _roster.Members.Where(m => m != null && m != _local).Select(m => new PartyMemberStatus
        {
            Name = _displayName(m.ParticipantId),
            LifeState = m.State,
            IsDisconnected = _isDisconnected(m.ParticipantId),
            BleedoutRemaining = m.IsDowned ? m.BleedoutRemaining : 0f,
            ReviveProgress01 = ChannelOn(m)?.Progress ?? 0f
        }).ToList();

        private ReviveChannel ChannelOn(PlayerLifeStateComponent target) => _roster.Revives.ChannelOn(target);

        // ---- Local player ----

        public PlayerLifeState LocalState => _local != null ? _local.State : PlayerLifeState.Alive;
        public float LocalBleedoutRemaining => _local != null && _local.IsDowned ? _local.BleedoutRemaining : 0f;
        public float BeingRevivedProgress01 => _local != null ? ChannelOn(_local)?.Progress ?? 0f : 0f;

        /// <summary>"Hold Interact to revive X" while a Downed teammate is in reach; the channel progress while holding.</summary>
        public string RevivePrompt
        {
            get
            {
                if (_reviver == null || !_reviver.CanRevive) return string.Empty;
                if (_reviver.IsReviving) return $"Reviving {_displayName(_reviver.Channel.Target.ParticipantId)}… {Mathf.RoundToInt(_reviver.Channel.Progress * 100f)}%";
                var target = _reviver.FindDownedTeammateInRange();
                return target != null ? $"Hold Interact to revive {_displayName(target.ParticipantId)}" : string.Empty;
            }
        }

        public float ReviveProgress01 => _reviver != null && _reviver.IsReviving ? _reviver.Channel.Progress : 0f;

        public string LocalStateText => LocalState switch
        {
            PlayerLifeState.Downed => BeingRevivedProgress01 > 0f ? $"DOWNED — being revived {Mathf.RoundToInt(BeingRevivedProgress01 * 100f)}%" : $"DOWNED — {Mathf.CeilToInt(LocalBleedoutRemaining)}s",
            PlayerLifeState.Dead => "DEAD — spectating",
            _ => string.Empty
        };

        // ---- Dead spectator ----

        public bool IsSpectating => _spectator != null && _spectator.IsSpectating;
        public string SpectatorTargetName => IsSpectating ? _displayName(_spectator.Selector.Current.ParticipantId) : string.Empty;
        public string SpectatorPrompt => IsSpectating ? $"Spectating {SpectatorTargetName} — Interact: next teammate" : string.Empty;
        public int SpectatorCandidates => _spectator?.Selector?.Candidates.Count ?? 0;
    }

    /// <summary>
    /// Transit vote UI (86): Return / Descend controls for living voters only, pending voters, the party result, and the
    /// dead-return warning that must be acknowledged before a Return vote while a teammate is Dead. Votes are submitted
    /// to the authoritative TransitDecision; the view model cannot resolve anything itself.
    /// </summary>
    public sealed class TransitVoteViewModel : IDisposable
    {
        private readonly TransitDecision _decision;
        private readonly string _localId;
        private readonly Func<string, string> _displayName;

        public TransitVoteViewModel(TransitDecision decision, string localParticipantId, Func<string, string> displayName = null)
        {
            _decision = decision ?? throw new ArgumentNullException(nameof(decision));
            _localId = localParticipantId;
            _displayName = displayName ?? (id => id);
            _decision.Resolved += OnResolved;
            _decision.VotersChanged += OnVotersChanged;
        }

        public bool HasVoteControls => _decision.CanVote(_localId);
        public bool IsResolved => _decision.State == TransitDecisionState.Resolved;
        public TransitChoice? Result => _decision.Result;
        public TransitChoice? LocalVote => _decision.Choices.TryGetValue(_localId ?? string.Empty, out var c) ? c : null;
        public IReadOnlyList<string> PendingVoterNames => _decision.PendingVoters.Select(_displayName).ToList();
        public bool AwaitingReturnConfirmation { get; private set; }
        /// <summary>Players with a vote (the living party at the decision).</summary>
        public int VoterCount => _decision.LivingPlayers.Count;
        /// <summary>Votes cast so far for one option (presentation of the existing tally; never a rule).</summary>
        public int VotesFor(TransitChoice choice) => _decision.Choices.Values.Count(c => c == choice);
        public string ReturnWarningText => _decision.RequiresReturnWarning ? new TransitReturnWarning(_decision.DeadPlayers.Select(_displayName).ToList()).Message : string.Empty;
        public string NoVoteText => HasVoteControls ? string.Empty : IsResolved ? string.Empty : "Dead players have no vote.";

        public event Action<TransitVoteViewModel> Changed;

        public string StatusText => IsResolved
            ? (Result == TransitChoice.DescendDeeper ? "The party descends deeper." : "The party returns to the Shelter.")
            : PendingVoterNames.Count > 0 ? $"Waiting for: {string.Join(", ", PendingVoterNames)}" : "Waiting for the vote to resolve…";

        /// <summary>Descend: submitted directly. Return: asks for confirmation first when a teammate is Dead (86).</summary>
        public bool Vote(TransitChoice choice)
        {
            if (!HasVoteControls) return false;
            if (choice == TransitChoice.ReturnToShelter && _decision.RequiresReturnWarning && !AwaitingReturnConfirmation)
            {
                AwaitingReturnConfirmation = true;
                Raise();
                return false;
            }

            AwaitingReturnConfirmation = false;
            var submitted = _decision.Submit(_localId, choice);
            Raise();
            return submitted || LocalVote == choice;
        }

        public bool ConfirmReturn()
        {
            if (!AwaitingReturnConfirmation) return false;
            return Vote(TransitChoice.ReturnToShelter);
        }

        public void CancelReturn()
        {
            AwaitingReturnConfirmation = false;
            Raise();
        }

        private void OnResolved(TransitDecision _, TransitChoice __) => Raise();
        private void OnVotersChanged(TransitDecision _) => Raise();
        private void Raise() => Changed?.Invoke(this);

        public void Dispose()
        {
            _decision.Resolved -= OnResolved;
            _decision.VotersChanged -= OnVotersChanged;
        }
    }
}
