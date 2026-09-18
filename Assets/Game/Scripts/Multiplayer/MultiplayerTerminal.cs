using System;
using System.Text;
using System.Threading.Tasks;

namespace RuinRail.Networking
{
    /// <summary>81 Multiplayer Terminal choices.</summary>
    public enum TerminalMode
    {
        Solo,
        HostCoop,
        JoinCoop
    }

    /// <summary>UI-facing state of the terminal flow (the controller's lifecycle folded into what a screen shows).</summary>
    public enum TerminalState
    {
        Idle,
        Solo,
        Busy,
        InSession,
        Error
    }

    /// <summary>User-readable error model (English, player-facing) for every service/transport failure kind.</summary>
    public readonly struct SessionError
    {
        public SessionError(ServiceErrorKind kind, string message, bool isRetryable)
        {
            Kind = kind;
            Message = message;
            IsRetryable = isRetryable;
        }

        public ServiceErrorKind Kind { get; }
        public string Message { get; }
        public bool IsRetryable { get; }
        public bool IsNone => Kind == ServiceErrorKind.None;

        public static readonly SessionError None = new(ServiceErrorKind.None, string.Empty, false);

        public static SessionError For(ServiceResult result)
        {
            return result.Success ? None : For(result.Error);
        }

        public static SessionError For(ServiceErrorKind kind)
        {
            return kind switch
            {
                ServiceErrorKind.None => None,
                ServiceErrorKind.Unavailable => new SessionError(kind, "Online services are unavailable right now. You can still play solo.", true),
                ServiceErrorKind.NotSignedIn => new SessionError(kind, "Could not sign in to online services. Check your connection and try again.", true),
                ServiceErrorKind.InvalidCode => new SessionError(kind, "That join code is invalid or has expired.", true),
                ServiceErrorKind.SessionFull => new SessionError(kind, "That party is already full (3 players).", false),
                ServiceErrorKind.Timeout => new SessionError(kind, "The connection was lost. Return to the terminal to host or join again.", true),
                ServiceErrorKind.InvalidState => new SessionError(kind, "Leave your current session before starting another one.", false),
                _ => new SessionError(kind, "Something went wrong with the online session. Please try again.", true)
            };
        }
    }

    /// <summary>Join-code rules: 6 characters, letters/digits, case-insensitive, whitespace/dashes ignored.</summary>
    public static class JoinCode
    {
        public const int Length = 6;

        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }

        public static bool IsWellFormed(string raw)
        {
            var code = Normalize(raw);
            if (code.Length != Length) return false;
            foreach (var c in code)
            {
                if (c > 127 || !char.IsLetterOrDigit(c)) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// 81 Base Flow: SOLO / HOST CO-OP / JOIN CO-OP over the session controller. Hosting yields the short join code
    /// friends type in; joining validates the code before touching services; every failure becomes a SessionError the
    /// terminal screen shows without corrupting the session state. Idempotent: repeating the current choice is a no-op.
    /// </summary>
    public sealed class MultiplayerTerminalService
    {
        private readonly NetworkSessionController _controller;
        private int _busy;

        public MultiplayerTerminalService(NetworkSessionController controller)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _controller.StateChanged += OnControllerState;
        }

        public NetworkSessionController Controller => _controller;
        public TerminalMode? Mode { get; private set; }
        public TerminalState State { get; private set; } = TerminalState.Idle;
        public SessionError LastError { get; private set; } = SessionError.None;
        public string JoinCodeToShare => _controller.Session?.IsHost == true ? _controller.Session.JoinCode : null;
        public int PartySize => _controller.Session?.PlayerCount ?? (Mode == TerminalMode.Solo ? 1 : 0);
        public int MaxPartySize => SessionRequest.MaxPartySize;
        public bool IsInSession => _controller.IsConnected;
        public bool IsBusy => _busy > 0;

        public event Action<MultiplayerTerminalService> Changed;

        private void Set(TerminalState state, SessionError error)
        {
            State = state;
            LastError = error;
            Changed?.Invoke(this);
        }

        /// <summary>SOLO: offline, no services. Leaves any session first.</summary>
        public async Task<SessionError> SelectSoloAsync()
        {
            if (Mode == TerminalMode.Solo && _controller.IsOffline) return SessionError.None;
            if (IsBusy) return SessionError.For(ServiceErrorKind.InvalidState);
            _busy++;
            try
            {
                if (!_controller.IsOffline) await _controller.LeaveAsync();
                if (_controller.State == NetworkLifecycleState.Failed) _controller.StartOffline();
                Mode = TerminalMode.Solo;
                Set(TerminalState.Solo, SessionError.None);
                return SessionError.None;
            }
            finally
            {
                _busy--;
            }
        }

        /// <summary>HOST CO-OP: private session for up to 3, exposes JoinCodeToShare on success.</summary>
        public async Task<SessionError> HostCoopAsync(string region = null)
        {
            if (Mode == TerminalMode.HostCoop && _controller.IsConnected) return SessionError.None;
            if (IsBusy) return SessionError.For(ServiceErrorKind.InvalidState);
            if (_controller.IsConnected) return Fail(SessionError.For(ServiceErrorKind.InvalidState), keepState: true);
            _busy++;
            try
            {
                Mode = TerminalMode.HostCoop;
                Set(TerminalState.Busy, SessionError.None);
                var result = await _controller.HostAsync(new SessionRequest(SessionRequest.MaxPartySize, region));
                if (!result.Success) return Fail(SessionError.For(result));
                Set(TerminalState.InSession, SessionError.None);
                return SessionError.None;
            }
            finally
            {
                _busy--;
            }
        }

        /// <summary>JOIN CO-OP by code. Malformed codes never reach the services.</summary>
        public async Task<SessionError> JoinCoopAsync(string rawCode)
        {
            if (IsBusy) return SessionError.For(ServiceErrorKind.InvalidState);
            if (_controller.IsConnected)
            {
                var same = _controller.Session != null && string.Equals(_controller.Session.JoinCode, JoinCode.Normalize(rawCode), StringComparison.OrdinalIgnoreCase);
                return same ? SessionError.None : Fail(SessionError.For(ServiceErrorKind.InvalidState), keepState: true);
            }

            if (!JoinCode.IsWellFormed(rawCode)) return Fail(SessionError.For(ServiceErrorKind.InvalidCode), keepState: true);
            _busy++;
            try
            {
                Mode = TerminalMode.JoinCoop;
                Set(TerminalState.Busy, SessionError.None);
                var result = await _controller.JoinAsync(JoinCode.Normalize(rawCode));
                if (!result.Success) return Fail(SessionError.For(result));
                Set(TerminalState.InSession, SessionError.None);
                return SessionError.None;
            }
            finally
            {
                _busy--;
            }
        }

        /// <summary>Leaves whatever session exists; safe to repeat.</summary>
        public async Task<SessionError> LeaveAsync()
        {
            if (IsBusy) return SessionError.For(ServiceErrorKind.InvalidState);
            _busy++;
            try
            {
                var result = await _controller.LeaveAsync();
                if (_controller.State == NetworkLifecycleState.Failed) _controller.StartOffline();
                Mode = null;
                Set(TerminalState.Idle, SessionError.For(result));
                return SessionError.For(result);
            }
            finally
            {
                _busy--;
            }
        }

        private SessionError Fail(SessionError error, bool keepState = false)
        {
            if (keepState) LastError = error;
            else Set(TerminalState.Error, error);
            Changed?.Invoke(this);
            return error;
        }

        private void OnControllerState(NetworkSessionController controller, NetworkLifecycleState state)
        {
            // A connection lost while in session surfaces as an error the screen can show; nothing else changes.
            if (state == NetworkLifecycleState.Failed && State == TerminalState.InSession)
            {
                Set(TerminalState.Error, SessionError.For(controller.LastError));
            }
        }
    }
}
