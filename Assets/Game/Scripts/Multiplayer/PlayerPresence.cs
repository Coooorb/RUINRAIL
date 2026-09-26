using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>A session member as the host knows it: stable client id, sanitized display name, host flag.</summary>
    [Serializable]
    public sealed class PlayerIdentity
    {
        public ulong ClientId;
        public string DisplayName;
        public bool IsHost;

        public PlayerIdentity(ulong clientId, string displayName, bool isHost)
        {
            ClientId = clientId;
            DisplayName = displayName;
            IsHost = isHost;
        }
    }

    /// <summary>
    /// Host-authoritative party roster (max 3): members are added when their connection is accepted, with the raw
    /// display name they sent sanitized through the profile rules (10). The local profile is never modified — only
    /// the transmitted copy is normalized; a rejected name falls back to "Player N".
    /// </summary>
    public sealed class SessionRoster
    {
        private readonly List<PlayerIdentity> _members = new();
        private readonly DisplayNamePolicy _policy;

        public SessionRoster(DisplayNamePolicy policy = null)
        {
            _policy = policy;
        }

        public IReadOnlyList<PlayerIdentity> Members => _members;
        public int Count => _members.Count;
        public int MaxMembers => SessionRequest.MaxPartySize;

        public event Action<PlayerIdentity> MemberAdded;
        public event Action<PlayerIdentity> MemberRemoved;

        public bool Contains(ulong clientId) => _members.Any(m => m.ClientId == clientId);
        public PlayerIdentity Get(ulong clientId) => _members.FirstOrDefault(m => m.ClientId == clientId);

        /// <summary>Sanitizes a transmitted name: valid names normalized, anything else replaced by a fallback.</summary>
        public string Sanitize(string rawName, ulong clientId)
        {
            if (_policy != null)
            {
                var result = DisplayNameValidator.Validate(rawName ?? string.Empty, _policy);
                if (result.IsValid) return result.Normalized;
            }
            else if (!string.IsNullOrWhiteSpace(rawName) && rawName.Length <= 16 && rawName.All(c => !char.IsControl(c) && c != '<' && c != '>'))
            {
                return rawName.Trim();
            }

            return $"Player {clientId + 1}";
        }

        public PlayerIdentity Add(ulong clientId, string rawName, bool isHost)
        {
            var existing = Get(clientId);
            if (existing != null) return existing;
            if (_members.Count >= MaxMembers) throw new InvalidOperationException($"Party is full ({MaxMembers}).");
            var identity = new PlayerIdentity(clientId, Sanitize(rawName, clientId), isHost);
            _members.Add(identity);
            MemberAdded?.Invoke(identity);
            return identity;
        }

        /// <summary>
        /// Re-sanitizes a member's name (the local player renamed before hosting). Identity and host flag are kept.
        /// </summary>
        public bool Rename(ulong clientId, string rawName)
        {
            var member = Get(clientId);
            if (member == null) return false;
            member.DisplayName = Sanitize(rawName, clientId);
            return true;
        }

        public bool Remove(ulong clientId)
        {
            var member = Get(clientId);
            if (member == null) return false;
            _members.Remove(member);
            MemberRemoved?.Invoke(member);
            return true;
        }

        public void Clear()
        {
            foreach (var member in _members.ToArray()) Remove(member.ClientId);
        }
    }

    /// <summary>Connection events the presence service reacts to (NGO adapter or a fake in tests).</summary>
    public interface IConnectionEvents
    {
        ulong LocalClientId { get; }
        bool IsHost { get; }
        event Action<ulong, string> ClientConnected;
        event Action<ulong> ClientDisconnected;
        /// <summary>85: a connection presenting a reconnect token, raised before ClientConnected for that client id.</summary>
        event Action<ulong, string> ClientReconnecting;
    }

    /// <summary>Creates/destroys the shared player composition for a session member.</summary>
    public interface IPlayerEntityFactory
    {
        GameObject Spawn(PlayerIdentity identity, bool isLocalOwner);
        void Despawn(GameObject entity);
    }

    /// <summary>One spawned player entity with its ownership.</summary>
    public sealed class NetworkPlayerEntity
    {
        public NetworkPlayerEntity(PlayerIdentity identity, GameObject gameObject, bool isLocalOwner)
        {
            Identity = identity;
            GameObject = gameObject;
            IsLocalOwner = isLocalOwner;
        }

        public PlayerIdentity Identity { get; }
        public GameObject GameObject { get; }
        public bool IsLocalOwner { get; }
        public ulong OwnerClientId => Identity.ClientId;
        /// <summary>85: the token the host issued for reclaiming this entity after a disconnect.</summary>
        public string ReconnectToken { get; internal set; }
    }

    /// <summary>
    /// Host-driven player presence (82): exactly one entity per connected member, owned by that member's client id,
    /// spawned when the connection is accepted and despawned exactly once when it leaves. Only the entity owned by the
    /// local client is built with local input; every other replica is input-isolated.
    /// </summary>
    public sealed class PlayerPresenceService : IDisposable
    {
        private readonly IConnectionEvents _connection;
        private readonly IPlayerEntityFactory _factory;
        private readonly SessionRoster _roster;
        private readonly Dictionary<ulong, NetworkPlayerEntity> _entities = new();

        private readonly ReconnectGraceService _grace;

        public PlayerPresenceService(IConnectionEvents connection, IPlayerEntityFactory factory, SessionRoster roster, ReconnectGraceService grace = null)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _grace = grace;
            _connection.ClientConnected += OnClientConnected;
            _connection.ClientDisconnected += OnClientDisconnected;
            _connection.ClientReconnecting += OnClientReconnecting;
        }

        public ReconnectGraceService Grace => _grace;
        public int ReconnectCount { get; private set; }

        /// <summary>
        /// 81/83: the party is agreed in the lobby and fixed when the expedition starts, and 83 pins the scaling to it.
        /// Once the run's composition root has closed the party, a connection that is not already a member and did not
        /// reclaim a held entity (85) is admitted as nothing: no roster entry and no player object. Without this, a
        /// stray or replayed connection mid-expedition would mint a fourth character the run was never scaled for.
        /// </summary>
        public bool IsPartyClosed { get; private set; }

        public void CloseParty() => IsPartyClosed = true;
        public event Action<NetworkPlayerEntity, ulong> Reconnected;

        public SessionRoster Roster => _roster;
        public IReadOnlyDictionary<ulong, NetworkPlayerEntity> Entities => _entities;
        public NetworkPlayerEntity LocalEntity => _entities.Values.FirstOrDefault(e => e.IsLocalOwner);
        public int SpawnCount { get; private set; }
        public int DespawnCount { get; private set; }

        public event Action<NetworkPlayerEntity> Spawned;
        public event Action<NetworkPlayerEntity> Despawned;

        private void OnClientConnected(ulong clientId, string rawName)
        {
            // Spawning is a host decision (82); clients learn about entities through replication.
            if (!_connection.IsHost) return;
            if (_entities.ContainsKey(clientId)) return;
            if (_roster.Count >= _roster.MaxMembers && !_roster.Contains(clientId)) return;
            // A closed party admits only members it already has (a reconnect re-adds itself before this callback).
            if (IsPartyClosed && !_roster.Contains(clientId)) return;
            var identity = _roster.Add(clientId, rawName, clientId == _connection.LocalClientId);
            var isLocal = clientId == _connection.LocalClientId;
            var go = _factory.Spawn(identity, isLocal);
            if (go == null)
            {
                // A member without an entity is not a member: counting it would let the run believe it composed a party
                // it never built, and the dungeon would be scaled for a player who does not exist (83).
                _roster.Remove(clientId);
                Debug.LogError($"No player entity could be spawned for client {clientId}; the member is not admitted.");
                return;
            }

            var entity = new NetworkPlayerEntity(identity, go, isLocal);
            entity.ReconnectToken = ReconnectGraceService.NewToken();
            _entities[clientId] = entity;
            SpawnCount++;
            Spawned?.Invoke(entity);
        }

        /// <summary>85: a reconnect within the grace rebinds the reserved entity to the new client id; nothing is spawned.</summary>
        private void OnClientReconnecting(ulong newClientId, string token)
        {
            if (!_connection.IsHost || _grace == null) return;
            var pending = _grace.TryReclaim(token, newClientId);
            if (pending == null) return;
            var entity = pending.Entity;
            _entities[newClientId] = entity;
            if (!_roster.Contains(newClientId)) _roster.Add(newClientId, entity.Identity.DisplayName, entity.Identity.IsHost);
            ReconnectCount++;
            Reconnected?.Invoke(entity, newClientId);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!_entities.TryGetValue(clientId, out var entity)) return;
            _entities.Remove(clientId);
            _roster.Remove(clientId);
            if (_connection.IsHost && _grace != null && _grace.ShouldHold() && !entity.Identity.IsHost)
            {
                // Mid-expedition: the character stays represented and at risk under its reconnect token (85).
                _grace.Hold(entity, clientId);
                return;
            }

            _factory.Despawn(entity.GameObject);
            DespawnCount++;
            Despawned?.Invoke(entity);
        }

        public void Dispose()
        {
            _connection.ClientConnected -= OnClientConnected;
            _connection.ClientDisconnected -= OnClientDisconnected;
            _connection.ClientReconnecting -= OnClientReconnecting;
            foreach (var id in _entities.Keys.ToArray()) OnClientDisconnected(id);
        }
    }

    /// <summary>Test/offline connection events: scripts connects/disconnects for any client id.</summary>
    public sealed class FakeConnectionEvents : IConnectionEvents
    {
        public FakeConnectionEvents(ulong localClientId, bool isHost)
        {
            LocalClientId = localClientId;
            IsHost = isHost;
        }

        public ulong LocalClientId { get; }
        public bool IsHost { get; }
        public event Action<ulong, string> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, string> ClientReconnecting;

        public void Connect(ulong clientId, string rawName) => ClientConnected?.Invoke(clientId, rawName);
        public void Disconnect(ulong clientId) => ClientDisconnected?.Invoke(clientId);

        /// <summary>A reconnecting client: token first (reclaim), then the regular connected callback.</summary>
        public void Reconnect(ulong newClientId, string token, string rawName)
        {
            ClientReconnecting?.Invoke(newClientId, token);
            ClientConnected?.Invoke(newClientId, rawName);
        }
    }

    /// <summary>Builds the shared player composition through PlayerEntityBuilder (local input only for the owner).</summary>
    public sealed class LocalPlayerEntityFactory : IPlayerEntityFactory
    {
        private readonly PlayerBalanceConfig _balance;
        private readonly Gameplay.Items.GlobalStatCapsConfig _caps;
        private readonly Func<PlayerIdentity, Vector2> _spawnPosition;
        private readonly Action<GameObject, bool> _decorate;
        private readonly PartyLifeRoster _roster;
        private readonly Func<PlayerIdentity, string> _participantId;

        /// <param name="decorate">Presentation composition run on every spawned entity (local owner or replica) — the app passes its player visual composer so a session member is never a bare collider.</param>
        /// <param name="roster">
        /// 84: the party roster every spawned member joins. Without it a presence-spawned entity is invisible to
        /// Downed/revive/wipe and to transit voting, which is exactly the gap that made the run solo-shaped — the
        /// machinery was all party-aware, but only one entity ever reached the roster.
        /// </param>
        /// <param name="participantId">The party participant id for revive/loot/vote lookups; defaults to the client id.</param>
        public LocalPlayerEntityFactory(PlayerBalanceConfig balance = null, Gameplay.Items.GlobalStatCapsConfig caps = null, Func<PlayerIdentity, Vector2> spawnPosition = null, Action<GameObject, bool> decorate = null,
            PartyLifeRoster roster = null, Func<PlayerIdentity, string> participantId = null)
        {
            _balance = balance;
            _caps = caps;
            _spawnPosition = spawnPosition;
            _decorate = decorate;
            _roster = roster;
            _participantId = participantId;
        }

        public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
        {
            var entity = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options
            {
                Name = $"Player_{identity.ClientId}_{identity.DisplayName}",
                IsLocal = isLocalOwner,
                BalanceConfig = _balance,
                Caps = _caps,
                Position = _spawnPosition?.Invoke(identity) ?? Vector2.zero,
                LifeRoster = _roster,
                ParticipantId = _participantId?.Invoke(identity) ?? identity.ClientId.ToString()
            });
            _decorate?.Invoke(entity, isLocalOwner);
            return entity;
        }

        public void Despawn(GameObject entity)
        {
            if (entity == null) return;
            // Editor-time composition (validators, editor tests) has no next frame to destroy in.
            if (Application.isPlaying) UnityEngine.Object.Destroy(entity);
            else UnityEngine.Object.DestroyImmediate(entity);
        }
    }
}
