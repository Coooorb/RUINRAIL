using System;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// NGO connection events: the host learns about accepted clients (with the display name they sent as connection
    /// payload) and about disconnects. Clients see only their own connection.
    /// </summary>
    public sealed class NgoConnectionEvents : IConnectionEvents
    {
        private readonly NetworkManager _manager;

        public NgoConnectionEvents(NetworkManager manager)
        {
            _manager = manager != null ? manager : throw new ArgumentNullException(nameof(manager));
            _manager.OnClientConnectedCallback += id =>
            {
                var (name, token) = ConnectionPayload.Decode(PendingNames.Take(id));
                if (!string.IsNullOrEmpty(token)) ClientReconnecting?.Invoke(id, token);
                ClientConnected?.Invoke(id, name);
            };
            _manager.OnClientDisconnectCallback += id => ClientDisconnected?.Invoke(id);
        }

        public ulong LocalClientId => _manager.LocalClientId;
        public bool IsHost => _manager.IsHost;
        public event Action<ulong, string> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, string> ClientReconnecting;

        /// <summary>Connection-approval hook: records the raw name a joining client sent and approves within capacity.</summary>
        public void ConfigureApproval(SessionRoster roster)
        {
            _manager.ConnectionApprovalCallback = (request, response) =>
            {
                var name = request.Payload != null && request.Payload.Length > 0 ? System.Text.Encoding.UTF8.GetString(request.Payload) : string.Empty;
                PendingNames.Store(request.ClientNetworkId, name);
                response.Approved = roster.Count < roster.MaxMembers;
                response.CreatePlayerObject = false; // the presence service spawns exactly one entity per member
                response.Reason = response.Approved ? string.Empty : "Party is full.";
            };
        }

        /// <summary>Raw names keyed by client id between approval and the connected callback.</summary>
        public static class PendingNames
        {
            private static readonly System.Collections.Generic.Dictionary<ulong, string> Names = new();
            public static void Store(ulong clientId, string name) => Names[clientId] = name;
            public static string Take(ulong clientId)
            {
                if (!Names.TryGetValue(clientId, out var name)) return string.Empty;
                Names.Remove(clientId);
                return name;
            }
        }
    }


    /// <summary>Spawns the network player prefab as the member's owned object (host only); despawns on leave.</summary>
    public sealed class NgoPlayerEntityFactory : IPlayerEntityFactory
    {
        private readonly NetworkObject _prefab;
        private readonly Func<PlayerIdentity, Vector3> _spawnPosition;
        private readonly Func<PlayerIdentity, string> _participantId;
        private readonly PartyLifeRoster _roster;

        /// <param name="participantId">
        /// The party participant id per member (84/86: revive, vote and loot lookups). Written to the object before it is
        /// spawned and replicated, so every peer's roster names the member the same way.
        /// </param>
        /// <param name="roster">84: every spawned member joins the host's party life roster (Downed/revive/wipe, voters).</param>
        public NgoPlayerEntityFactory(NetworkObject prefab, Func<PlayerIdentity, Vector3> spawnPosition = null, Func<PlayerIdentity, string> participantId = null, PartyLifeRoster roster = null)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _spawnPosition = spawnPosition;
            _participantId = participantId;
            _roster = roster;
        }

        public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
        {
            var instance = UnityEngine.Object.Instantiate(_prefab, _spawnPosition?.Invoke(identity) ?? Vector3.zero, Quaternion.identity);
            instance.name = $"Player_{identity.ClientId}_{identity.DisplayName}";
            // The expedition spans scene loads on every peer; only the host's despawn ends a player object.
            UnityEngine.Object.DontDestroyOnLoad(instance.gameObject);
            var participant = _participantId?.Invoke(identity);
            var life = instance.GetComponent<PlayerLifeStateComponent>();
            if (life != null)
            {
                if (!string.IsNullOrEmpty(participant)) life.SetParticipantId(participant);
                if (_roster != null) life.SetRoster(_roster);
            }

            instance.SpawnAsPlayerObject(identity.ClientId, true);
            var net = instance.GetComponent<NetworkPlayerObject>();
            net?.SetDisplayName(identity.DisplayName);
            if (net != null && !string.IsNullOrEmpty(participant)) net.SetParticipantId(participant);
            return instance.gameObject;
        }

        public void Despawn(GameObject entity)
        {
            if (entity == null) return;
            var networkObject = entity.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned) networkObject.Despawn(true);
            else UnityEngine.Object.Destroy(entity);
        }
    }
}
