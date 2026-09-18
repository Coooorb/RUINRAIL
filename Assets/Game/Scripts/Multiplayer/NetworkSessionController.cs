using System;
using System.Threading.Tasks;

namespace RuinRail.Networking
{
    /// <summary>
    /// The local session lifecycle (80/82): Offline ⇄ Hosting/Joining → Connected → Leaving → Offline, with Failed for
    /// service/transport errors. Solo play never touches the services (offline is the default). There is exactly one
    /// host per session and no host migration: a dropped transport on a client or host ends in Failed, never in a
    /// role swap. Sessions are always private (no public matchmaking) and capped at the party limit of 3.
    /// </summary>
    public sealed class NetworkSessionController
    {
        private readonly IMultiplayerServices _services;
        private readonly INetworkDriver _driver;

        public NetworkSessionController(IMultiplayerServices services, INetworkDriver driver)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _driver.Stopped += OnDriverStopped;
        }

        public const bool SupportsHostMigration = false;
        public const bool SupportsPublicMatchmaking = false;
        public const bool SupportsDedicatedServer = false;

        public NetworkLifecycleState State { get; private set; } = NetworkLifecycleState.Offline;
        public NetworkRole Role { get; private set; } = NetworkRole.Offline;
        public SessionHandle Session { get; private set; }
        public ServiceResult LastError { get; private set; } = ServiceResult.Ok();
        public bool IsOffline => State == NetworkLifecycleState.Offline;
        public bool IsConnected => State == NetworkLifecycleState.Connected;
        public bool IsHostAuthority => Role != NetworkRole.Client;

        public IAuthorityContext Authority => new RoleAuthorityContext(this);

        public event Action<NetworkSessionController, NetworkLifecycleState> StateChanged;

        private void Transition(NetworkLifecycleState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(this, next);
        }

        /// <summary>Solo: no services, no transport. Always succeeds; also the state after leaving a session.</summary>
        public void StartOffline()
        {
            if (State == NetworkLifecycleState.Hosting || State == NetworkLifecycleState.Joining || State == NetworkLifecycleState.Connected)
            {
                throw new InvalidOperationException("Leave the session before going offline.");
            }

            Role = NetworkRole.Offline;
            Session = null;
            LastError = ServiceResult.Ok();
            Transition(NetworkLifecycleState.Offline);
        }

        public async Task<ServiceResult> HostAsync(SessionRequest request)
        {
            if (State != NetworkLifecycleState.Offline && State != NetworkLifecycleState.Failed)
            {
                return Fail(ServiceResult.Fail(ServiceErrorKind.InvalidState, $"cannot host from {State}"), keepState: true);
            }

            Transition(NetworkLifecycleState.Initializing);
            var init = await EnsureServicesAsync();
            if (!init.Success) return Fail(init);

            Transition(NetworkLifecycleState.Hosting);
            var created = await _services.CreateSessionAsync(request);
            if (!created.Success) return Fail(created.AsPlain());

            var started = _driver.StartHost();
            if (!started.Success)
            {
                await _services.LeaveSessionAsync();
                return Fail(started);
            }

            Session = created.Value;
            Role = NetworkRole.Host;
            LastError = ServiceResult.Ok();
            Transition(NetworkLifecycleState.Connected);
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> JoinAsync(string joinCode)
        {
            if (State != NetworkLifecycleState.Offline && State != NetworkLifecycleState.Failed)
            {
                return Fail(ServiceResult.Fail(ServiceErrorKind.InvalidState, $"cannot join from {State}"), keepState: true);
            }

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return Fail(ServiceResult.Fail(ServiceErrorKind.InvalidCode, "empty join code"));
            }

            Transition(NetworkLifecycleState.Initializing);
            var init = await EnsureServicesAsync();
            if (!init.Success) return Fail(init);

            Transition(NetworkLifecycleState.Joining);
            var joined = await _services.JoinSessionByCodeAsync(joinCode.Trim());
            if (!joined.Success) return Fail(joined.AsPlain());

            var started = _driver.StartClient();
            if (!started.Success)
            {
                await _services.LeaveSessionAsync();
                return Fail(started);
            }

            Session = joined.Value;
            Role = NetworkRole.Client;
            LastError = ServiceResult.Ok();
            Transition(NetworkLifecycleState.Connected);
            return ServiceResult.Ok();
        }

        public async Task<ServiceResult> LeaveAsync()
        {
            if (State == NetworkLifecycleState.Offline) return ServiceResult.Ok();
            Transition(NetworkLifecycleState.Leaving);
            _driver.Shutdown();
            var left = await _services.LeaveSessionAsync();
            Session = null;
            Role = NetworkRole.Offline;
            Transition(NetworkLifecycleState.Offline);
            return left;
        }

        private async Task<ServiceResult> EnsureServicesAsync()
        {
            if (!_services.IsAvailable) return ServiceResult.Fail(ServiceErrorKind.Unavailable, "multiplayer services unavailable");
            if (_services.IsInitialized && _services.IsSignedIn) return ServiceResult.Ok();
            return await _services.InitializeAndSignInAsync();
        }

        private ServiceResult Fail(ServiceResult error, bool keepState = false)
        {
            LastError = error;
            if (!keepState)
            {
                _driver.Shutdown();
                Session = null;
                Role = NetworkRole.Offline;
                Transition(NetworkLifecycleState.Failed);
            }

            return error;
        }

        /// <summary>Transport dropped underneath us: no migration, the session is over for this peer.</summary>
        private void OnDriverStopped(INetworkDriver driver)
        {
            if (State != NetworkLifecycleState.Connected) return;
            _ = _services.LeaveSessionAsync();
            Session = null;
            Role = NetworkRole.Offline;
            LastError = ServiceResult.Fail(ServiceErrorKind.Timeout, "connection lost");
            Transition(NetworkLifecycleState.Failed);
        }

        private sealed class RoleAuthorityContext : IAuthorityContext
        {
            private readonly NetworkSessionController _controller;
            public RoleAuthorityContext(NetworkSessionController controller) { _controller = controller; }
            public NetworkRole Role => _controller.Role;
            public bool IsAuthority => _controller.IsHostAuthority;
        }
    }
}
