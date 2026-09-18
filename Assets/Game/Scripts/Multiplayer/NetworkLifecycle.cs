using System;

namespace RuinRail.Networking
{
    /// <summary>80: client-hosted, host-authoritative co-op (solo/duo/trio). Offline = solo without any service.</summary>
    public enum NetworkRole
    {
        Offline,
        Host,
        Client
    }

    /// <summary>Lifecycle of the local session. No host migration: a lost host ends in Failed/Offline, never Host.</summary>
    public enum NetworkLifecycleState
    {
        Offline,
        Initializing,
        Hosting,
        Joining,
        Connected,
        Leaving,
        Failed
    }

    public enum ServiceErrorKind
    {
        None,
        Unavailable,
        NotSignedIn,
        InvalidCode,
        SessionFull,
        Timeout,
        InvalidState,
        Unknown
    }

    /// <summary>Outcome of one service call; failures carry a kind the UI can map to a message, never an exception.</summary>
    public readonly struct ServiceResult
    {
        private ServiceResult(bool success, ServiceErrorKind error, string message)
        {
            Success = success;
            Error = error;
            Message = message;
        }

        public bool Success { get; }
        public ServiceErrorKind Error { get; }
        public string Message { get; }

        public static ServiceResult Ok() => new(true, ServiceErrorKind.None, null);
        public static ServiceResult Fail(ServiceErrorKind error, string message = null) => new(false, error, message);
    }

    public readonly struct ServiceResult<T>
    {
        private ServiceResult(bool success, T value, ServiceErrorKind error, string message)
        {
            Success = success;
            Value = value;
            Error = error;
            Message = message;
        }

        public bool Success { get; }
        public T Value { get; }
        public ServiceErrorKind Error { get; }
        public string Message { get; }

        public static ServiceResult<T> Ok(T value) => new(true, value, ServiceErrorKind.None, null);
        public static ServiceResult<T> Fail(ServiceErrorKind error, string message = null) => new(false, default, error, message);
        public ServiceResult AsPlain() => Success ? ServiceResult.Ok() : ServiceResult.Fail(Error, Message);
    }

    /// <summary>What the host asks for. Private only (no public matchmaking in V1); party limit 3 (80).</summary>
    public readonly struct SessionRequest
    {
        public const int MaxPartySize = 3;

        private readonly int _maxPlayers;

        public SessionRequest(int maxPlayers = MaxPartySize, string region = null)
        {
            _maxPlayers = Math.Clamp(maxPlayers, 1, MaxPartySize);
            Region = region;
        }

        /// <summary>A default-constructed request (struct default, MaxPlayers unset) still means the full party limit.</summary>
        public int MaxPlayers => _maxPlayers <= 0 ? MaxPartySize : _maxPlayers;
        public string Region { get; }
        public bool IsPrivate => true;
    }

    /// <summary>The joined/hosted session as gameplay sees it: identity, join code, role and capacity.</summary>
    public sealed class SessionHandle
    {
        public SessionHandle(string sessionId, string joinCode, bool isHost, int maxPlayers, int playerCount)
        {
            SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
            JoinCode = joinCode;
            IsHost = isHost;
            MaxPlayers = maxPlayers;
            PlayerCount = playerCount;
        }

        public string SessionId { get; }
        public string JoinCode { get; }
        public bool IsHost { get; }
        public int MaxPlayers { get; }
        public int PlayerCount { get; internal set; }
    }
}
