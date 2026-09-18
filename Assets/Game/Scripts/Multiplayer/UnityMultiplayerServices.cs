using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;

namespace RuinRail.Networking
{
    /// <summary>
    /// The one live adapter over Unity Services: Core initialization, anonymous sign-in, Sessions (private, capped
    /// at the party limit) with Relay networking. Every exception becomes a ServiceResult so callers never see
    /// service-specific types. Requires a Unity Cloud project link at runtime — tests use the fake instead.
    /// </summary>
    public sealed class UnityMultiplayerServices : IMultiplayerServices
    {
        private ISession _session;

        public bool IsAvailable => true;
        public bool IsInitialized => UnityServices.State == ServicesInitializationState.Initialized;
        public bool IsSignedIn => IsInitialized && AuthenticationService.Instance.IsSignedIn;
        public SessionHandle CurrentSession { get; private set; }

        public async Task<ServiceResult> InitializeAndSignInAsync()
        {
            try
            {
                if (!IsInitialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return ServiceResult.Ok();
            }
            catch (Exception e)
            {
                return ServiceResult.Fail(ServiceErrorKind.Unavailable, e.Message);
            }
        }

        public async Task<ServiceResult<SessionHandle>> CreateSessionAsync(SessionRequest request)
        {
            if (!IsSignedIn) return ServiceResult<SessionHandle>.Fail(ServiceErrorKind.NotSignedIn);
            if (_session != null) return ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidState, "already in a session");
            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = request.MaxPlayers,
                    IsPrivate = true
                }.WithRelayNetwork(request.Region);
                var hosted = await MultiplayerService.Instance.CreateSessionAsync(options);
                _session = hosted;
                CurrentSession = new SessionHandle(hosted.Id, hosted.Code, true, hosted.MaxPlayers, hosted.PlayerCount);
                return ServiceResult<SessionHandle>.Ok(CurrentSession);
            }
            catch (Exception e)
            {
                return ServiceResult<SessionHandle>.Fail(Classify(e), e.Message);
            }
        }

        public async Task<ServiceResult<SessionHandle>> JoinSessionByCodeAsync(string joinCode)
        {
            if (!IsSignedIn) return ServiceResult<SessionHandle>.Fail(ServiceErrorKind.NotSignedIn);
            if (_session != null) return ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidState, "already in a session");
            if (string.IsNullOrWhiteSpace(joinCode)) return ServiceResult<SessionHandle>.Fail(ServiceErrorKind.InvalidCode);
            try
            {
                var joined = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim());
                _session = joined;
                CurrentSession = new SessionHandle(joined.Id, joined.Code, joined.IsHost, joined.MaxPlayers, joined.PlayerCount);
                return ServiceResult<SessionHandle>.Ok(CurrentSession);
            }
            catch (Exception e)
            {
                return ServiceResult<SessionHandle>.Fail(Classify(e), e.Message);
            }
        }

        public async Task<ServiceResult> LeaveSessionAsync()
        {
            var session = _session;
            _session = null;
            CurrentSession = null;
            if (session == null) return ServiceResult.Ok();
            try
            {
                await session.LeaveAsync();
                return ServiceResult.Ok();
            }
            catch (Exception e)
            {
                return ServiceResult.Fail(ServiceErrorKind.Unknown, e.Message);
            }
        }

        private static ServiceErrorKind Classify(Exception e)
        {
            var message = e.Message ?? string.Empty;
            if (message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0) return ServiceErrorKind.InvalidCode;
            if (message.IndexOf("full", StringComparison.OrdinalIgnoreCase) >= 0) return ServiceErrorKind.SessionFull;
            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0) return ServiceErrorKind.Timeout;
            return ServiceErrorKind.Unknown;
        }
    }
}
