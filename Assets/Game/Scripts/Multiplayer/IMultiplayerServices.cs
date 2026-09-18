using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RuinRail.Networking
{
    /// <summary>
    /// Project-owned boundary over Unity Multiplayer Services (Sessions + Relay) and authentication (80/119): gameplay
    /// and UI only ever talk to this interface, so every test runs against the in-memory fake and the live adapter is
    /// the single place that touches Unity service APIs.
    /// </summary>
    public interface IMultiplayerServices
    {
        /// <summary>True when the service layer can be used at all (packages present, environment configured).</summary>
        bool IsAvailable { get; }
        bool IsInitialized { get; }
        bool IsSignedIn { get; }
        SessionHandle CurrentSession { get; }

        Task<ServiceResult> InitializeAndSignInAsync();
        Task<ServiceResult<SessionHandle>> CreateSessionAsync(SessionRequest request);
        Task<ServiceResult<SessionHandle>> JoinSessionByCodeAsync(string joinCode);
        Task<ServiceResult> LeaveSessionAsync();
    }

    /// <summary>
    /// Transport/NGO driver boundary: starting/stopping the local host or client endpoint. Wrapped so lifecycle logic
    /// and tests never reference NetworkManager directly.
    /// </summary>
    public interface INetworkDriver
    {
        bool IsListening { get; }
        NetworkRole Role { get; }
        ServiceResult StartHost();
        ServiceResult StartClient();
        void Shutdown();
        event Action<INetworkDriver> Stopped;
    }

    /// <summary>
    /// In-memory services for tests and offline development: sessions live in a shared registry keyed by join code so
    /// a "host" and a "client" instance in the same process can find each other; availability and failures are
    /// scriptable per instance.
    /// </summary>
    public sealed class FakeMultiplayerServices : IMultiplayerServices
    {
        private sealed class FakeSession
        {
            public string Id;
            public string Code;
            public int MaxPlayers;
            public int PlayerCount;
        }

        private static readonly Dictionary<string, FakeSession> Registry = new(StringComparer.OrdinalIgnoreCase);
        private static int _nextId;

        private FakeSession _joined;
        private SessionHandle _current;

        public bool IsAvailable { get; set; } = true;
        public bool IsInitialized { get; private set; }
        public bool IsSignedIn { get; private set; }

        /// <summary>Live view: the player count follows the shared registry (a peer leaving shrinks the host's party).</summary>
        public SessionHandle CurrentSession
        {
            get
            {
                if (_current != null && _joined != null) _current.PlayerCount = _joined.PlayerCount;
                return _current;
            }
            private set => _current = value;
        }

        /// <summary>Next call fails with this kind (then resets), to script service outages in tests.</summary>
        public ServiceErrorKind FailNextWith { get; set; } = ServiceErrorKind.None;

        public int InitializeCalls { get; private set; }

        public static void ResetRegistry()
        {
            Registry.Clear();
            _nextId = 0;
        }

        public static string GenerateJoinCode(int index)
        {
            // Six uppercase letters/digits, deterministic per index for tests.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var chars = new char[6];
            var value = index * 7919 + 12345;
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = alphabet[value % alphabet.Length];
                value /= 3;
                value += 17;
            }

            return new string(chars);
        }

        private bool TakeScriptedFailure(out ServiceErrorKind kind)
        {
            kind = FailNextWith;
            FailNextWith = ServiceErrorKind.None;
            return kind != ServiceErrorKind.None;
        }

        public Task<ServiceResult> InitializeAndSignInAsync()
        {
            InitializeCalls++;
            if (!IsAvailable) return Task.FromResult(ServiceResult.Fail(ServiceErrorKind.Unavailable, "services unavailable"));
            if (TakeScriptedFailure(out var kind)) return Task.FromResult(ServiceResult.Fail(kind, "scripted"));
            IsInitialized = true;
            IsSignedIn = true;
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult<SessionHandle>> CreateSessionAsync(SessionRequest request)
        {
            if (!IsAvailable) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.Unavailable));
            if (!IsSignedIn) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.NotSignedIn));
            if (CurrentSession != null) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidState, "already in a session"));
            if (TakeScriptedFailure(out var kind)) return Task.FromResult(ServiceResult<SessionHandle>.Fail(kind, "scripted"));

            var index = ++_nextId;
            var session = new FakeSession { Id = $"fake-session-{index}", Code = GenerateJoinCode(index), MaxPlayers = request.MaxPlayers, PlayerCount = 1 };
            Registry[session.Code] = session;
            _joined = session;
            _current = new SessionHandle(session.Id, session.Code, true, session.MaxPlayers, session.PlayerCount);
            return Task.FromResult(ServiceResult<SessionHandle>.Ok(CurrentSession));
        }

        public Task<ServiceResult<SessionHandle>> JoinSessionByCodeAsync(string joinCode)
        {
            if (!IsAvailable) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.Unavailable));
            if (!IsSignedIn) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.NotSignedIn));
            if (CurrentSession != null) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidState, "already in a session"));
            if (TakeScriptedFailure(out var kind)) return Task.FromResult(ServiceResult<SessionHandle>.Fail(kind, "scripted"));
            if (string.IsNullOrWhiteSpace(joinCode) || !Registry.TryGetValue(joinCode.Trim(), out var session))
            {
                return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidCode, "no such session"));
            }

            if (session.PlayerCount >= session.MaxPlayers) return Task.FromResult(ServiceResult<SessionHandle>.Fail(ServiceErrorKind.SessionFull));
            session.PlayerCount++;
            _joined = session;
            _current = new SessionHandle(session.Id, session.Code, false, session.MaxPlayers, session.PlayerCount);
            return Task.FromResult(ServiceResult<SessionHandle>.Ok(CurrentSession));
        }

        public Task<ServiceResult> LeaveSessionAsync()
        {
            if (CurrentSession == null) return Task.FromResult(ServiceResult.Ok());
            if (_joined != null)
            {
                if (CurrentSession.IsHost) Registry.Remove(_joined.Code);
                else _joined.PlayerCount = Math.Max(0, _joined.PlayerCount - 1);
            }

            _joined = null;
            _current = null;
            return Task.FromResult(ServiceResult.Ok());
        }
    }

    /// <summary>Driver fake: records role transitions; can simulate a lost connection through Drop().</summary>
    public sealed class FakeNetworkDriver : INetworkDriver
    {
        public bool IsListening { get; private set; }
        public NetworkRole Role { get; private set; } = NetworkRole.Offline;
        public int HostStarts { get; private set; }
        public int ClientStarts { get; private set; }
        public int Shutdowns { get; private set; }
        public ServiceErrorKind FailNextWith { get; set; } = ServiceErrorKind.None;

        public event Action<INetworkDriver> Stopped;

        private bool TakeFailure(out ServiceErrorKind kind)
        {
            kind = FailNextWith;
            FailNextWith = ServiceErrorKind.None;
            return kind != ServiceErrorKind.None;
        }

        public ServiceResult StartHost()
        {
            if (IsListening) return ServiceResult.Fail(ServiceErrorKind.InvalidState, "already listening");
            if (TakeFailure(out var kind)) return ServiceResult.Fail(kind, "scripted");
            HostStarts++;
            IsListening = true;
            Role = NetworkRole.Host;
            return ServiceResult.Ok();
        }

        public ServiceResult StartClient()
        {
            if (IsListening) return ServiceResult.Fail(ServiceErrorKind.InvalidState, "already listening");
            if (TakeFailure(out var kind)) return ServiceResult.Fail(kind, "scripted");
            ClientStarts++;
            IsListening = true;
            Role = NetworkRole.Client;
            return ServiceResult.Ok();
        }

        public void Shutdown()
        {
            if (!IsListening) return;
            Shutdowns++;
            IsListening = false;
            Role = NetworkRole.Offline;
        }

        /// <summary>Simulates the transport dropping (host left, timeout).</summary>
        public void Drop()
        {
            if (!IsListening) return;
            IsListening = false;
            Role = NetworkRole.Offline;
            Stopped?.Invoke(this);
        }
    }
}
