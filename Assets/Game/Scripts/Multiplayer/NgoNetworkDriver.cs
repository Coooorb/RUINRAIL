using System;
using Unity.Netcode;

namespace RuinRail.Networking
{
    /// <summary>
    /// INetworkDriver over Netcode for GameObjects' NetworkManager. The Sessions package wires Relay into the
    /// transport when a session is created/joined with Relay networking, so this driver only starts/stops the local
    /// endpoint. Host only — no dedicated server start path exists here on purpose.
    /// </summary>
    public sealed class NgoNetworkDriver : INetworkDriver
    {
        private readonly NetworkManager _manager;

        public NgoNetworkDriver(NetworkManager manager)
        {
            _manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            _manager.OnClientStopped += OnStopped;
            _manager.OnServerStopped += OnStopped;
        }

        public bool IsListening => _manager != null && _manager.IsListening;
        public NetworkRole Role => !IsListening ? NetworkRole.Offline : _manager.IsHost ? NetworkRole.Host : NetworkRole.Client;

        public event Action<INetworkDriver> Stopped;

        public ServiceResult StartHost()
        {
            if (IsListening) return ServiceResult.Fail(ServiceErrorKind.InvalidState, "already listening");
            return _manager.StartHost() ? ServiceResult.Ok() : ServiceResult.Fail(ServiceErrorKind.Unknown, "NetworkManager.StartHost failed");
        }

        public ServiceResult StartClient()
        {
            if (IsListening) return ServiceResult.Fail(ServiceErrorKind.InvalidState, "already listening");
            return _manager.StartClient() ? ServiceResult.Ok() : ServiceResult.Fail(ServiceErrorKind.Unknown, "NetworkManager.StartClient failed");
        }

        public void Shutdown()
        {
            if (_manager != null && _manager.IsListening) _manager.Shutdown();
        }

        private void OnStopped(bool wasHost) => Stopped?.Invoke(this);
    }
}
