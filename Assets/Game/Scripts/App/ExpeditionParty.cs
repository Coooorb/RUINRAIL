using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>Why a party could not be composed for the expedition the lobby agreed to start.</summary>
    public enum PartyCompositionError
    {
        None,
        /// <summary>The start snapshot named more members than the party limit (80: max 3).</summary>
        TooManyMembers,
        /// <summary>Fewer player entities were composed than the snapshot expects — the run would be scaled for a party that is not there (83).</summary>
        Underfilled,
        /// <summary>The local client is not among the snapshot's members, so this peer has no player to own.</summary>
        NoLocalMember
    }

    /// <summary>
    /// The run-level party composition: every expedition participant's player entity, on every peer.
    ///
    /// This is the piece the shipping run was missing. All the party machinery already existed and was already wired —
    /// <see cref="PartyLifeRoster"/> drives Downed/revive/wipe, <see cref="PartyExpeditionBinding"/> derives transit
    /// voters from it, <see cref="RoomRuntime"/> tracks every occupant and activates once, <see cref="LootAuthorityService"/>
    /// arbitrates pickups — but exactly one entity was ever built, so all of it behaved as solo by arithmetic rather
    /// than by design. This composes one entity per member through the existing <see cref="PlayerPresenceService"/>,
    /// which keeps its own one-entity-per-client invariant, and registers each into the roster and the loot authority.
    ///
    /// It is deliberately run-level, not per-player: the local client's camera, HUD, input and audio listener are
    /// composed once by <see cref="ExpeditionScene"/> against <see cref="LocalEntity"/> and are never created here, so
    /// a remote member can never mint a second camera, listener, input reader or HUD.
    /// </summary>
    public sealed class ExpeditionParty : IDisposable
    {
        /// <summary>80: maximum 3 players total.</summary>
        public const int MaxPartySize = 3;

        private readonly PlayerPresenceService _presence;
        private readonly SessionRoster _roster;
        private readonly PartyLifeRoster _lifeRoster;
        private readonly LootAuthorityService _loot;
        private readonly AdoptingEntityFactory _factory;
        private readonly FakeConnectionEvents _offlineConnection;
        private readonly ItemDefinitionRegistry _items;
        private readonly AmmoBalanceConfig _ammo;
        private PartyCoinDistributor _coinDistributor;
        private IConnectionEvents _live;
        private Action<ulong> _liveDisconnected;
        private Action<ulong, string> _liveReconnecting;
        private bool _disposed;

        private ExpeditionParty(PlayerPresenceService presence, SessionRoster roster, PartyLifeRoster lifeRoster,
            LootAuthorityService loot, AdoptingEntityFactory factory, FakeConnectionEvents offlineConnection, int expectedSize, ulong localClientId,
            ItemDefinitionRegistry items, AmmoBalanceConfig ammo)
        {
            _presence = presence;
            _roster = roster;
            _lifeRoster = lifeRoster;
            _loot = loot;
            _factory = factory;
            _offlineConnection = offlineConnection;
            ExpectedPartySize = expectedSize;
            LocalClientId = localClientId;
            _items = items;
            _ammo = ammo;
        }

        /// <summary>The party size the lobby's start snapshot agreed on — what the dungeon would be scaled for.</summary>
        public int ExpectedPartySize { get; }

        /// <summary>How many player entities actually exist right now.</summary>
        public int ComposedPartySize => _presence.Entities.Count;

        public ulong LocalClientId { get; }

        /// <summary>
        /// 83: scaling is only safe once the composed presence matches what the snapshot promised. Until this is true
        /// the run must not scale for a party that is not there.
        /// </summary>
        public bool IsFullyComposed => ComposedPartySize >= ExpectedPartySize && ExpectedPartySize > 0;

        /// <summary>The party size the dungeon may scale for: the composed count, never the promised one.</summary>
        public int ScalingPartySize => Mathf.Clamp(ComposedPartySize, 1, MaxPartySize);

        /// <summary>True when this run has more than one composed player — the one source of the co-op UI flag.</summary>
        public bool IsCoop => ComposedPartySize > 1;

        public PlayerPresenceService Presence => _presence;
        public SessionRoster Roster => _roster;
        public PartyLifeRoster LifeRoster => _lifeRoster;
        public LootAuthorityService Loot => _loot;

        /// <summary>The entity this client owns; the only one local presentation may bind to.</summary>
        public GameObject LocalEntity => _presence.LocalEntity?.GameObject;

        /// <summary>Every composed member, local first.</summary>
        public IEnumerable<NetworkPlayerEntity> Members => _presence.Entities.Values.OrderByDescending(e => e.IsLocalOwner).ThenBy(e => e.OwnerClientId);

        /// <summary>Every member that is not this client's own.</summary>
        public IEnumerable<NetworkPlayerEntity> RemoteMembers => _presence.Entities.Values.Where(e => !e.IsLocalOwner).OrderBy(e => e.OwnerClientId);

        public event Action<NetworkPlayerEntity> MemberComposed;

        /// <summary>
        /// Composes the party for a started expedition.
        ///
        /// <paramref name="localEntity"/> is the already-built local player (the run's <c>PlayerRig</c>): it is adopted
        /// rather than rebuilt, so the local player keeps its input, stats, progression and at-risk inventory exactly as
        /// the solo path builds them. Every other member is built through <see cref="PlayerEntityBuilder"/> with the
        /// null input reader, which is what makes a remote replica input-isolated by construction.
        /// </summary>
        public static ExpeditionParty Compose(PartyCompositionRequest request, out PartyCompositionError error)
        {
            error = PartyCompositionError.None;
            if (request == null) { error = PartyCompositionError.NoLocalMember; return null; }
            var members = (request.Members ?? Array.Empty<PartyMemberDescriptor>()).Where(m => m != null).ToList();
            if (members.Count == 0) members.Add(new PartyMemberDescriptor(request.LocalClientId, request.LocalDisplayName, request.LocalParticipantId, true));
            if (members.Count > MaxPartySize) { error = PartyCompositionError.TooManyMembers; return null; }
            if (members.All(m => m.ClientId != request.LocalClientId)) { error = PartyCompositionError.NoLocalMember; return null; }

            var roster = new SessionRoster(request.DisplayNamePolicy);
            var grace = request.Grace;
            // On a live session the host spawns each member as the release network player object so every peer receives
            // it; without one (solo, or a proof/diagnostic run) the same composition is built locally.
            var remote = request.RemoteFactory ?? new LocalPlayerEntityFactory(
                request.Balance, request.Caps, request.SpawnPosition, request.Decorate, request.LifeRoster,
                identity => ParticipantIdOf(members, identity, request));
            var factory = new AdoptingEntityFactory(request.LocalClientId, request.LocalEntity, remote);

            var connection = new FakeConnectionEvents(request.LocalClientId, request.IsHost);
            var presence = new PlayerPresenceService(connection, factory, roster, grace);
            var party = new ExpeditionParty(presence, roster, request.LifeRoster, request.Loot, factory, connection, members.Count, request.LocalClientId, request.Items, request.Ammo);

            presence.Spawned += party.OnSpawned;
            presence.Despawned += party.OnDespawned;
            // 85: real churn during the run (a member's connection dropping, a member reconnecting with its token)
            // reaches the same presence service that composed the party.
            party.ForwardLiveConnection(request.LiveConnection);
            // The host is the only peer that spawns (82). A client peer composes nothing itself and receives replicas,
            // so this offline/loopback path drives the same presence service the NGO adapter drives in a live session.
            foreach (var member in members.OrderByDescending(m => m.ClientId == request.LocalClientId).ThenBy(m => m.ClientId))
            {
                connection.Connect(member.ClientId, member.DisplayName);
            }

            if (!party.IsFullyComposed)
            {
                error = PartyCompositionError.Underfilled;
                party.Dispose();
                return null;
            }

            // 81/83: the expedition's party is now fixed. From here only a reconnecting member (85) is admitted.
            presence.CloseParty();
            return party;
        }

        private static string ParticipantIdOf(IReadOnlyList<PartyMemberDescriptor> members, PlayerIdentity identity, PartyCompositionRequest request)
        {
            var member = members.FirstOrDefault(m => m.ClientId == identity.ClientId);
            if (member != null && !string.IsNullOrEmpty(member.ParticipantId)) return member.ParticipantId;
            return identity.ClientId == request.LocalClientId && !string.IsNullOrEmpty(request.LocalParticipantId)
                ? request.LocalParticipantId
                : identity.ClientId.ToString();
        }

        private void OnSpawned(NetworkPlayerEntity entity)
        {
            if (entity?.GameObject == null) return;
            ProvisionInventory(entity);
            RegisterWithLoot(entity);
            RebuildCoinDistribution();
            MemberComposed?.Invoke(entity);
        }

        private void OnDespawned(NetworkPlayerEntity entity) => RebuildCoinDistribution();

        /// <summary>
        /// A member that did not arrive with an inventory (every member but this peer's own, whose at-risk inventory the
        /// run already built) gets an empty expedition inventory. Without a backpack a member cannot be a loot
        /// participant at all, which would silently make the host the only player able to receive anything.
        /// </summary>
        private void ProvisionInventory(NetworkPlayerEntity entity)
        {
            var receiver = entity.GameObject.GetComponent<PlayerLootReceiver>();
            if (receiver == null || receiver.Inventory != null || _items == null) return;
            receiver.SetInventory(PlayerInventory.FromRegistry(_items, _ammo));
        }

        /// <summary>
        /// 58: a coin pile is split evenly across the current participants' own Carried wallets — never all into the
        /// picker's. Every member's receiver shares one distributor over the whole party, so the split is the same
        /// regardless of who walked over the pile, and it is rebuilt whenever the party's membership changes.
        /// </summary>
        private void RebuildCoinDistribution()
        {
            var receivers = new List<PlayerLootReceiver>();
            var participants = new List<CoinParticipant>();
            foreach (var entity in Members)
            {
                if (entity.GameObject == null) continue;
                var receiver = entity.GameObject.GetComponent<PlayerLootReceiver>();
                if (receiver == null) continue;
                var life = entity.GameObject.GetComponent<PlayerLifeStateComponent>();
                var id = life != null && !string.IsNullOrEmpty(life.ParticipantId) ? life.ParticipantId : entity.OwnerClientId.ToString();
                if (participants.Exists(p => p.Id == id)) continue;
                receivers.Add(receiver);
                participants.Add(new CoinParticipant(id, receiver.Wallet));
            }

            // Solo keeps the solo path exactly (null distributor: the full amount into the one wallet).
            var distributor = participants.Count > 1 ? new PartyCoinDistributor(participants.ToArray()) : null;
            _coinDistributor = distributor;
            foreach (var receiver in receivers) receiver.SetCoinDistributor(distributor);
        }

        /// <summary>The live party coin split (null while solo).</summary>
        public PartyCoinDistributor CoinDistribution => _coinDistributor;

        /// <summary>
        /// Every member is a loot participant on the host (82): a shared pickup resolves through one arbiter, into the
        /// claiming player's own backpack, against the expedition's shared Carried wallet.
        /// </summary>
        private void RegisterWithLoot(NetworkPlayerEntity entity)
        {
            if (_loot == null) return;
            var receiver = entity.GameObject.GetComponent<PlayerLootReceiver>();
            if (receiver?.Inventory == null) return;
            var life = entity.GameObject.GetComponent<PlayerLifeStateComponent>();
            _loot.RegisterParticipant(new LootParticipant(entity.OwnerClientId,
                life != null ? life.ParticipantId : entity.OwnerClientId.ToString(),
                receiver.Backpack, receiver.Wallet, entity.GameObject,
                // The member's own carried containers (backpack + equipped slots): a host-resolved drop comes from
                // these and never from a container the request names.
                receiver.CarriedContainers));
        }

        /// <summary>
        /// Forwards a live session's disconnect/reconnect events into the party's presence driver. Connects are not
        /// forwarded: the party is closed at expedition start (81/83), so only a reconnecting member is admitted.
        /// </summary>
        private void ForwardLiveConnection(IConnectionEvents live)
        {
            if (live == null || _offlineConnection == null) return;
            _live = live;
            _liveDisconnected = id => _offlineConnection.Disconnect(id);
            _liveReconnecting = (id, token) => _offlineConnection.Reconnect(id, token, _roster.Get(id)?.DisplayName);
            live.ClientDisconnected += _liveDisconnected;
            live.ClientReconnecting += _liveReconnecting;
        }

        /// <summary>Test/diagnostic seam: drives a mid-run join through the same presence service the live adapter uses.</summary>
        public void SimulateConnect(ulong clientId, string displayName) => _offlineConnection?.Connect(clientId, displayName);

        /// <summary>Test/diagnostic seam: drives a disconnect (grace applies exactly as the live adapter's would).</summary>
        public void SimulateDisconnect(ulong clientId) => _offlineConnection?.Disconnect(clientId);

        /// <summary>Test/diagnostic seam: a reconnect presenting the held token reclaims the same entity.</summary>
        public void SimulateReconnect(ulong newClientId, string token, string displayName) => _offlineConnection?.Reconnect(newClientId, token, displayName);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_live != null)
            {
                if (_liveDisconnected != null) _live.ClientDisconnected -= _liveDisconnected;
                if (_liveReconnecting != null) _live.ClientReconnecting -= _liveReconnecting;
                _live = null;
            }

            _presence.Spawned -= OnSpawned;
            _presence.Despawned -= OnDespawned;
            _presence.Dispose();
        }

        /// <summary>
        /// Wraps the real factory so the local client's already-built entity is adopted instead of rebuilt, and is
        /// never destroyed by presence teardown — the run's <c>PlayerRig</c> owns its lifetime.
        /// </summary>
        private sealed class AdoptingEntityFactory : IPlayerEntityFactory
        {
            private readonly ulong _localClientId;
            private readonly GameObject _localEntity;
            private readonly IPlayerEntityFactory _inner;

            public AdoptingEntityFactory(ulong localClientId, GameObject localEntity, IPlayerEntityFactory inner)
            {
                _localClientId = localClientId;
                _localEntity = localEntity;
                _inner = inner;
            }

            public GameObject Spawn(PlayerIdentity identity, bool isLocalOwner)
            {
                if (identity.ClientId == _localClientId && _localEntity != null) return _localEntity;
                return _inner.Spawn(identity, isLocalOwner);
            }

            public void Despawn(GameObject entity)
            {
                if (entity == null || entity == _localEntity) return;
                _inner.Despawn(entity);
            }
        }
    }

    /// <summary>Runs an action on dispose; lets a composition root unsubscribe through the same disposables list it already keeps.</summary>
    public sealed class ActionDisposable : IDisposable
    {
        private Action _onDispose;

        public ActionDisposable(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            var action = _onDispose;
            _onDispose = null;
            action?.Invoke();
        }
    }

    /// <summary>One member of the expedition the lobby agreed to start.</summary>
    public sealed class PartyMemberDescriptor
    {
        public PartyMemberDescriptor(ulong clientId, string displayName, string participantId, bool isHost)
        {
            ClientId = clientId;
            DisplayName = displayName;
            ParticipantId = participantId;
            IsHost = isHost;
        }

        public ulong ClientId { get; }
        public string DisplayName { get; }
        public string ParticipantId { get; }
        public bool IsHost { get; }
    }

    /// <summary>Everything the run-level composition needs to build the party; assembled by the composition root.</summary>
    public sealed class PartyCompositionRequest
    {
        public IReadOnlyList<PartyMemberDescriptor> Members;
        public ulong LocalClientId;
        public string LocalDisplayName = "Player 1";
        public string LocalParticipantId;
        public GameObject LocalEntity;
        public bool IsHost = true;
        public PlayerBalanceConfig Balance;
        public GlobalStatCapsConfig Caps;
        public PartyLifeRoster LifeRoster;
        public LootAuthorityService Loot;
        /// <summary>The factory for members other than this peer's own; null builds them locally (solo/diagnostic).</summary>
        public IPlayerEntityFactory RemoteFactory;
        /// <summary>The live session's connection stream, so real disconnects/reconnects reach the presence service (85).</summary>
        public IConnectionEvents LiveConnection;
        /// <summary>Item definitions used to give a member without one an empty expedition inventory.</summary>
        public ItemDefinitionRegistry Items;
        public AmmoBalanceConfig Ammo;
        public ReconnectGraceService Grace;
        public DisplayNamePolicy DisplayNamePolicy;
        public Func<PlayerIdentity, Vector2> SpawnPosition;
        /// <summary>Presentation composition for every member (the app's player visual composer); never local input/camera/HUD.</summary>
        public Action<GameObject, bool> Decorate;
    }
}
