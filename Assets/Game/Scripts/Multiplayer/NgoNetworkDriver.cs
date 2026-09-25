using System;
using Unity.Netcode;
using UnityEngine;

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
            _manager.OnClientDisconnectCallback += RaiseClientDisconnected;
        }

        public bool IsListening => _manager != null && _manager.IsListening;

        /// <summary>
        /// The live connection stream this driver's endpoint produces (82): who the host accepted, who left and who
        /// presented a reconnect token. The lobby and the expedition's presence composition both read the session
        /// through this, so a real connection becomes a real party member instead of only a player count.
        /// </summary>
        public IConnectionEvents Connections => _connections ??= new NgoConnectionEvents(_manager);
        private NgoConnectionEvents _connections;

        /// <summary>Approval hook: records the name a joining client sent and refuses beyond the party limit (80/81).</summary>
        public void ConfigureApproval(SessionRoster roster) => ((NgoConnectionEvents)Connections).ConfigureApproval(roster);

        /// <summary>The player object the host spawns per member; null when the prefab is not registered.</summary>
        public IPlayerEntityFactory CreatePlayerFactory(GameObject prefab, Func<PlayerIdentity, Vector3> spawnPosition = null, Func<PlayerIdentity, string> participantId = null, RuinRail.Gameplay.Player.PartyLifeRoster roster = null)
        {
            var networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            return networkObject != null ? new NgoPlayerEntityFactory(networkObject, spawnPosition, participantId, roster) : null;
        }

        /// <summary>The NGO client id of this process (0 on a host); meaningful only while listening.</summary>
        public ulong LocalNetworkClientId => _manager != null ? _manager.LocalClientId : 0;

        /// <summary>True while this process is a connected client of a live session (not the host).</summary>
        public bool IsConnectedClient => _manager != null && _manager.IsConnectedClient && !_manager.IsHost;

        /// <summary>The live connection-data payload a joining client presents (display name + reconnect token, 85).</summary>
        public string ConnectionPayloadText => _manager?.NetworkConfig?.ConnectionData != null ? System.Text.Encoding.UTF8.GetString(_manager.NetworkConfig.ConnectionData) : string.Empty;

        /// <summary>
        /// Points the process transport at a direct address (the local-network path the built-player proof and LAN
        /// play use; the live Relay path replaces this with the session's allocation).
        /// </summary>
        public void SetDirectAddress(string address, ushort port, bool listenOnAny)
        {
            var transport = _manager != null ? _manager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>() : null;
            transport?.SetConnectionData(address, port, listenOnAny ? "0.0.0.0" : null);
        }

        /// <summary>Every spawned player object on this peer (owned and replicas).</summary>
        public System.Collections.Generic.IEnumerable<NetworkPlayerObject> PlayerObjects()
        {
            if (_manager == null || _manager.SpawnManager == null) yield break;
            foreach (var networkObject in _manager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null) continue;
                var player = networkObject.GetComponent<NetworkPlayerObject>();
                if (player != null) yield return player;
            }
        }

        /// <summary>Spawned network objects on this peer (leak diagnostics).</summary>
        public int SpawnedObjectCount => _manager != null && _manager.SpawnManager != null ? _manager.SpawnManager.SpawnedObjectsList.Count : 0;

        /// <summary>Hands ownership of a held player object to a reconnected client (85).</summary>
        public bool TransferOwnership(GameObject entity, ulong newClientId)
        {
            var networkObject = entity != null ? entity.GetComponent<NetworkObject>() : null;
            if (networkObject == null || !networkObject.IsSpawned || _manager == null || !_manager.IsServer) return false;
            if (networkObject.OwnerClientId == newClientId) return true;
            networkObject.ChangeOwnership(newClientId);
            return true;
        }

        public event Action ClientDisconnectedFromHost;

        private void RaiseClientDisconnected(ulong clientId)
        {
            if (_manager != null && !_manager.IsServer && clientId == _manager.LocalClientId) ClientDisconnectedFromHost?.Invoke();
        }

        /// <summary>The name/token payload this peer presents when it connects (85).</summary>
        public void SetConnectionPayload(string displayName, string reconnectToken = null)
        {
            if (_manager == null || _manager.NetworkConfig == null) return;
            _manager.NetworkConfig.ConnectionData = System.Text.Encoding.UTF8.GetBytes(ConnectionPayload.Encode(displayName, reconnectToken));
        }

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
