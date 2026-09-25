using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>How the host world resolves the party (supplied by the run's composition root).</summary>
    public sealed class CoopHostParty
    {
        /// <summary>The member's player entity on the host (its own or the host copy of a client's).</summary>
        public Func<ulong, GameObject> EntityOf = _ => null;
        public Func<ulong, string> ParticipantOf = _ => null;
        /// <summary>The largest single hit the member's equipped weapons (and grenades) can deal, for request validation.</summary>
        public Func<ulong, int> MaxHitOf = _ => DefaultMaxHit;
        public const int DefaultMaxHit = 2000;
    }

    /// <summary>
    /// The host half of the co-op expedition (82): everything the host decides is published to the clients, and every
    /// client request is validated here against what the host knows — never against what the request claims.
    ///
    /// Published: every enemy/elite/boss (spawn by definition id, 15 Hz motion/state, death once), every room's
    /// lifecycle and resolved interactions, every ground pickup (spawn, move, gone), boss phase/defeat, the Transit
    /// vote and its single result, and the depth payload. Requests handled: hits and impacts on enemies, heals on the
    /// sender's own character, votes, merchant trades, Weapon Cache choices, drops, inventory mirrors and resyncs.
    /// Solo never composes this; a host without clients composes it but has nobody to send to.
    /// </summary>
    public sealed class CoopHostWorld : IDisposable
    {
        public const float StateIntervalSeconds = 1f / 15f;
        public const float ScanIntervalSeconds = 0.2f;
        public const float MaxHitDistance = 40f;
        public const int MaxHitsPerSecond = 60;
        public const float MaxKnockback = 30f;
        public const float MaxStagger = 1000f;
        public const float HealCooldownSeconds = 0.25f;
        public const int StatesPerMessage = 20;
        public const float GameplayReleaseTimeoutSeconds = 25f;

        private readonly ICoopBus _bus;
        private readonly CoopHostParty _party;
        private readonly LootAuthorityService _loot;
        private readonly ExpeditionService _expedition;
        private readonly Func<string, ItemDefinition> _resolve;
        private readonly Dictionary<uint, Tracked> _actors = new();
        private readonly Dictionary<int, uint> _actorIds = new();
        private readonly Dictionary<uint, TrackedLoot> _loot2 = new();
        private readonly Dictionary<int, uint> _lootIds = new();
        private readonly Dictionary<int, string> _roomSignatures = new();
        private readonly Dictionary<int, uint> _roomVersions = new();
        private readonly Dictionary<ulong, RateWindow> _hitRates = new();
        private readonly Dictionary<ulong, float> _lastHeal = new();
        private readonly HashSet<ulong> _depthReady = new();
        private readonly List<EnemyNetState> _stateBuffer = new();
        private IReadOnlyDictionary<int, RoomRuntime> _rooms = new Dictionary<int, RoomRuntime>();
        private GroundLootRegistry _ground;
        private TransitDecision _transit;
        private GameObject _localEntity;
        private uint _nextActorId;
        private uint _nextLootId;
        private uint _stateVersion;
        private float _stateTimer;
        private float _scanTimer;
        private float _lootTimer;
        private int _depth;
        private bool _gameplayHeld;
        private float _gameplayHoldStarted;
        private bool _disposed;

        public CoopHostWorld(ICoopBus bus, CoopHostParty party, LootAuthorityService loot, ExpeditionService expedition, Func<string, ItemDefinition> resolve)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _party = party ?? new CoopHostParty();
            _loot = loot;
            _expedition = expedition;
            _resolve = resolve ?? (_ => null);
            _bus.Received += OnReceived;
            _bus.ShotReceived += OnShotReceived;
            ProjectilePool.Launched += OnProjectileLaunched;
            if (_expedition != null)
            {
                _expedition.TransitOpened += OnTransitOpened;
                _expedition.ExpeditionEnded += OnExpeditionEnded;
            }
        }

        // ---------------------------------------------------------------- diagnostics

        public int Depth => _depth;
        public int TrackedActors => _actors.Count;
        public int LiveActors => _actors.Values.Count(a => a.Alive);
        public int TrackedLootCount => _loot2.Count;
        public int SpawnsSent { get; private set; }
        public int DeathsSent { get; private set; }
        public int RoomStatesSent { get; private set; }
        public int LootSpawnsSent { get; private set; }
        public int LootGoneSent { get; private set; }
        public int StateMessagesSent { get; private set; }
        public int HitsApplied { get; private set; }
        public int HitDamageApplied { get; private set; }
        public int HitsRejected { get; private set; }
        public int ImpactsApplied { get; private set; }
        public int HealsApplied { get; private set; }
        public int VotesApplied { get; private set; }
        public int VotesRejected { get; private set; }
        public int TradesResolved { get; private set; }
        public int GrantsSent { get; private set; }
        public int RevokesSent { get; private set; }
        public int ShotsRelayed { get; private set; }
        public int ResyncsServed { get; private set; }
        public int TransitResolutionsSent { get; private set; }
        public string LastRejection { get; private set; } = string.Empty;
        public IReadOnlyCollection<ulong> DepthReadyPeers => _depthReady;
        public bool GameplayHeld => _gameplayHeld;
        public int GameplayReleases { get; private set; }
        public IReadOnlyDictionary<ulong, int> HitsBySender => _hitsBySender;
        private readonly Dictionary<ulong, int> _hitsBySender = new();
        public IReadOnlyDictionary<ulong, int> DamageBySender => _damageBySender;
        private readonly Dictionary<ulong, int> _damageBySender = new();

        /// <summary>A client's inventory mirror arrived (the composition root applies it to the member's host copy).</summary>
        public event Action<ulong, InventorySnapshotMessage> InventoryMirrorReceived;
        /// <summary>A client asked to drop an item; the composition root resolves it through the loot authority.</summary>
        public event Action<ulong, TradeRequestMessage> DropRequested;
        /// <summary>A member used a Defibrillator; the run (which owns the party's revive authority) resolves it.</summary>
        public event Action<ulong, ReviveRequestMessage> ReviveRequested;

        /// <summary>Kills attributed to a member's validated hit (diagnostics / proof).</summary>
        public int KillsAttributed { get; private set; }
        /// <summary>Every peer reported the depth ready (or the wait timed out): gameplay may begin.</summary>
        public event Action<int, bool> GameplayReleased;
        /// <summary>A client asked for the full current state (reconnect / late composition).</summary>
        public event Action<ulong> ResyncRequested;
        /// <summary>Proof channel (built-player proof only): a client's report.</summary>
        public event Action<ulong, ProofMessage> ProofReportReceived;
        /// <summary>A projectile presentation from a client arrived (host draws it, zero damage).</summary>
        public event Action<ShotNetRecord> RemoteShot;

        // ---------------------------------------------------------------- depth binding

        /// <summary>
        /// Binds the freshly built depth: every room, the boss, the ground loot, and publishes the depth payload. Clients
        /// rebuild the same layout from it, verify the fingerprint and report ready.
        /// </summary>
        public void BindDepth(int depth, IReadOnlyDictionary<int, RoomRuntime> rooms, GroundLootRegistry ground, in DungeonSyncPayload payload, GameObject localEntity)
        {
            // Everything of the depth left behind is gone on the clients too.
            foreach (var id in _actors.Keys.ToList()) SendGone(id, false);
            _actors.Clear();
            _actorIds.Clear();
            foreach (var id in _loot2.Keys.ToList()) _bus.SendToClients(CoopKinds.LootGone, CoopJson.Write(new LootGoneMessage { LootId = id }));
            _loot2.Clear();
            _lootIds.Clear();
            _roomSignatures.Clear();
            _roomVersions.Clear();
            _depthReady.Clear();
            _transit = null;

            _depth = depth;
            _rooms = rooms ?? new Dictionary<int, RoomRuntime>();
            _ground = ground;
            _localEntity = localEntity;
            foreach (var room in _rooms.Values)
            {
                if (room == null) continue;
                var node = room.State.NodeId;
                room.EnemySpawned += (r, enemy) => Register(enemy != null ? enemy.gameObject : null, node, countsForXp: true);
                if (room.Engagement is EliteEngagement elite)
                {
                    elite.Spawned += (_, encounter) => { if (encounter?.Elite != null) Register(encounter.Elite.gameObject, node, countsForXp: true); };
                    elite.EliteDefeated += (_, xp) => SendXp(xp, CoopActorKind.Elite);
                }

                var binding = room.GetComponent<RoomContentBinding>();
                if (binding?.Boss != null)
                {
                    var encounter = binding.Boss;
                    if (encounter.Boss != null) Register(encounter.Boss.gameObject, node, countsForXp: false);
                    encounter.BossStarted += e => SendBossState(e, node);
                    encounter.PhaseChanged += (e, _) => SendBossState(e, node);
                    encounter.BossDefeated += (e, _) => SendBossState(e, node);
                }
            }

            if (_bus.IsHost) _bus.PublishDepth(payload);
            SendAllRooms(null);
            HoldGameplayUntilPeersReady();
        }

        private void HoldGameplayUntilPeersReady()
        {
            if (_bus.RemoteClients.Count == 0) return;
            if (!_gameplayHeld) RuinRail.Core.Input.GameplayInputGate.Hold();
            _gameplayHeld = true;
            _gameplayHoldStarted = Time.realtimeSinceStartup;
        }

        private void ReleaseGameplay(bool timedOut)
        {
            if (!_gameplayHeld) return;
            _gameplayHeld = false;
            RuinRail.Core.Input.GameplayInputGate.Release();
            GameplayReleases++;
            _bus.SendToClients(CoopKinds.GameplayRelease, CoopJson.Write(new GameplayReleaseMessage { Depth = _depth, ReadyPeers = _depthReady.Count, TimedOut = timedOut }));
            GameplayReleased?.Invoke(_depth, timedOut);
        }

        // ---------------------------------------------------------------- per-frame

        public void Tick(float deltaTime)
        {
            if (_disposed) return;
            _scanTimer -= deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = ScanIntervalSeconds;
                ScanActors();
            }

            _stateTimer -= deltaTime;
            if (_stateTimer <= 0f)
            {
                _stateTimer = StateIntervalSeconds;
                SendStates();
                SendChangedRooms();
            }

            _lootTimer -= deltaTime;
            if (_lootTimer <= 0f)
            {
                _lootTimer = 0.1f;
                SyncLoot();
            }

            PublishWallets();
            if (_gameplayHeld)
            {
                var expected = _bus.RemoteClients;
                if (expected.All(id => _depthReady.Contains(id))) ReleaseGameplay(false);
                else if (Time.realtimeSinceStartup - _gameplayHoldStarted > GameplayReleaseTimeoutSeconds) ReleaseGameplay(true);
            }
        }

        // ---------------------------------------------------------------- actors

        private sealed class Tracked
        {
            public uint NetId;
            public GameObject Go;
            public EnemyController Enemy;
            public MovesetActorController Actor;
            public CoopActorKind Kind;
            public string DefinitionId;
            public int Node;
            public IReadOnlyList<EnemyAttackDefinition> Moveset;
            public bool CountsForXp;
            public bool Alive = true;
            public int Xp;
        }

        /// <summary>Moveset slot order both peers agree on: the authored moveset, then the boss's phase-two hazards.</summary>
        public static IReadOnlyList<EnemyAttackDefinition> MovesetOf(MovesetActorController actor)
        {
            switch (actor)
            {
                case BossController boss when boss.Definition != null:
                    return boss.Definition.Moveset.Concat(boss.Definition.PhaseTwoArenaHazards ?? (IReadOnlyList<EnemyAttackDefinition>)Array.Empty<EnemyAttackDefinition>()).ToList();
                case EliteController elite when elite.Definition != null:
                    return elite.Definition.Moveset.ToList();
                default:
                    return Array.Empty<EnemyAttackDefinition>();
            }
        }

        /// <summary>Registers one host actor for replication (idempotent). Returns its network id.</summary>
        public uint Register(GameObject go, int node, bool countsForXp)
        {
            if (go == null) return 0;
            var key = go.GetInstanceID();
            if (_actorIds.TryGetValue(key, out var existing))
            {
                if (countsForXp && _actors.TryGetValue(existing, out var known)) known.CountsForXp = true;
                return existing;
            }

            var enemy = go.GetComponent<EnemyController>();
            var actor = go.GetComponent<MovesetActorController>();
            if (enemy == null && actor == null) return 0;
            var tracked = new Tracked
            {
                NetId = ++_nextActorId,
                Go = go,
                Enemy = enemy,
                Actor = actor,
                Kind = actor is BossController ? CoopActorKind.Boss : actor is EliteController ? CoopActorKind.Elite : CoopActorKind.Normal,
                DefinitionId = enemy != null ? enemy.Definition?.Id : actor is BossController b ? b.Definition?.Id : (actor as EliteController)?.Definition?.Id,
                Node = node >= 0 ? node : NodeAt(go.transform.position),
                Moveset = actor != null ? MovesetOf(actor) : Array.Empty<EnemyAttackDefinition>(),
                CountsForXp = countsForXp,
                Xp = enemy != null ? enemy.XpValue : actor != null ? actor.XpValue : 0
            };
            _actors[tracked.NetId] = tracked;
            _actorIds[key] = tracked.NetId;
            if (enemy != null) enemy.Died += _ => OnActorDied(tracked);
            if (actor != null) actor.Died += _ => OnActorDied(tracked);
            SendSpawn(tracked, null);
            return tracked.NetId;
        }

        public bool TryGetNetId(GameObject go, out uint netId) => _actorIds.TryGetValue(go != null ? go.GetInstanceID() : 0, out netId);

        public GameObject ActorOf(uint netId) => _actors.TryGetValue(netId, out var tracked) ? tracked.Go : null;

        private int NodeAt(Vector2 position)
        {
            foreach (var room in _rooms.Values)
            {
                if (room != null && room.InteriorWorldBounds.Contains(position)) return room.State.NodeId;
            }

            return -1;
        }

        /// <summary>Catches every actor no room event announced (boss summons, summoner waves): nothing fights unseen.</summary>
        private void ScanActors()
        {
            foreach (var enemy in UnityEngine.Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
            {
                if (enemy != null && enemy.IsAlive && !_actorIds.ContainsKey(enemy.gameObject.GetInstanceID())) Register(enemy.gameObject, -1, false);
            }

            foreach (var actor in UnityEngine.Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None))
            {
                if (actor != null && actor.IsAlive && !_actorIds.ContainsKey(actor.gameObject.GetInstanceID())) Register(actor.gameObject, -1, false);
            }

            foreach (var tracked in _actors.Values.ToList())
            {
                if (tracked.Go == null)
                {
                    SendGone(tracked.NetId, false);
                    _actors.Remove(tracked.NetId);
                }
            }
        }

        private EnemySpawnMessage SpawnMessageOf(Tracked tracked)
        {
            var health = tracked.Go != null ? tracked.Go.GetComponent<HealthComponent>() : null;
            var position = tracked.Go != null ? (Vector2)tracked.Go.transform.position : Vector2.zero;
            return new EnemySpawnMessage
            {
                NetId = tracked.NetId,
                Kind = (int)tracked.Kind,
                DefinitionId = tracked.DefinitionId,
                X = position.x,
                Y = position.y,
                Depth = _depth,
                RoomNode = tracked.Node,
                Health = health != null ? health.CurrentHealth : 0,
                MaxHealth = health != null ? health.MaxHealth : 0,
                Phase = tracked.Actor is BossController boss ? boss.Phase : 1
            };
        }

        private void SendSpawn(Tracked tracked, ulong? only)
        {
            var json = CoopJson.Write(SpawnMessageOf(tracked));
            if (only.HasValue) _bus.SendToClient(only.Value, CoopKinds.EnemySpawn, json);
            else _bus.SendToClients(CoopKinds.EnemySpawn, json);
            SpawnsSent++;
        }

        private void OnActorDied(Tracked tracked)
        {
            if (!tracked.Alive) return;
            tracked.Alive = false;
            // The final state carries the 0 HP / dead flag so the replica plays its death, then the record retires it.
            SendStates(tracked);
            var xp = tracked.CountsForXp && tracked.Kind == CoopActorKind.Normal ? tracked.Xp : 0;
            _bus.SendToClients(CoopKinds.EnemyGone, CoopJson.Write(new EnemyGoneMessage { NetId = tracked.NetId, Died = true, Kind = (int)tracked.Kind, Xp = xp }));
            DeathsSent++;
        }

        private void SendGone(uint netId, bool died)
        {
            _bus.SendToClients(CoopKinds.EnemyGone, CoopJson.Write(new EnemyGoneMessage { NetId = netId, Died = died }));
        }

        private EnemyNetState Capture(Tracked tracked)
        {
            if (tracked.Actor != null) return AuthoritativeEnemySpawner.Capture(tracked.NetId, tracked.Actor, tracked.Moveset, ++_stateVersion, _bus.NetworkTime);
            return AuthoritativeEnemySpawner.Capture(tracked.NetId, tracked.Enemy, ++_stateVersion, _bus.NetworkTime);
        }

        private void SendStates(Tracked only = null)
        {
            _stateBuffer.Clear();
            if (only != null)
            {
                if (only.Go != null) _stateBuffer.Add(Capture(only));
            }
            else
            {
                foreach (var tracked in _actors.Values)
                {
                    if (tracked.Go == null || !tracked.Alive) continue;
                    _stateBuffer.Add(Capture(tracked));
                }
            }

            for (var i = 0; i < _stateBuffer.Count; i += StatesPerMessage)
            {
                _bus.SendEnemyStates(_stateBuffer.Skip(i).Take(StatesPerMessage).ToArray());
                StateMessagesSent++;
            }
        }

        private void SendXp(int xp, CoopActorKind kind)
        {
            if (xp <= 0) return;
            _bus.SendToClients(CoopKinds.Xp, CoopJson.Write(new XpMessage { Amount = xp, Kind = (int)kind }));
        }

        private void SendBossState(BossEncounter encounter, int node, ulong? only = null)
        {
            if (encounter == null) return;
            var boss = encounter.Boss;
            TryGetNetId(boss != null ? boss.gameObject : null, out var netId);
            var message = new BossStateMessage
            {
                NetId = netId,
                RoomNode = node,
                Phase = boss != null ? boss.Phase : 1,
                Started = encounter.IsStarted,
                Defeated = encounter.IsDefeated,
                Health = boss != null && boss.Health != null ? boss.Health.CurrentHealth : 0,
                MaxHealth = boss != null && boss.Health != null ? boss.Health.MaxHealth : 0,
                Version = ++_stateVersion
            };
            var json = CoopJson.Write(message);
            if (only.HasValue) _bus.SendToClient(only.Value, CoopKinds.BossState, json);
            else _bus.SendToClients(CoopKinds.BossState, json);
        }

        // ---------------------------------------------------------------- rooms

        /// <summary>The replicated form of one room: lifecycle, counts, door lock and every resolved interaction.</summary>
        public RoomStateMessage CaptureRoom(RoomRuntime room)
        {
            var state = room.State;
            var binding = room.GetComponent<RoomContentBinding>();
            return new RoomStateMessage
            {
                Depth = _depth,
                NodeId = state.NodeId,
                State = (int)state.State,
                EntryCount = state.EntryCount,
                EnemiesSpawned = state.EnemiesSpawned,
                EnemiesDefeated = state.EnemiesDefeated,
                EnemiesRemaining = room.EnemiesRemaining,
                DoorsLocked = room.DoorsLocked,
                Resolved = (state.Resolved ?? new List<string>()).ToArray(),
                MerchantSold = binding?.Merchant?.Merchant != null ? binding.Merchant.Merchant.State.SoldOfferIndices.ToArray() : Array.Empty<int>(),
                BossCacheLocked = binding?.BossCache != null && binding.BossCache.IsLocked,
                TransitActive = binding?.Transit != null && binding.Transit.IsActivated,
                EventPhase = binding?.EventInstance != null ? (int)binding.EventInstance.Phase : -1
            };
        }

        private static string SignatureOf(RoomStateMessage m) =>
            $"{m.State}|{m.EntryCount}|{m.EnemiesSpawned}|{m.EnemiesDefeated}|{m.EnemiesRemaining}|{m.DoorsLocked}|{string.Join(",", m.Resolved)}|{string.Join(",", m.MerchantSold)}|{m.BossCacheLocked}|{m.TransitActive}|{m.EventPhase}";

        private void SendChangedRooms()
        {
            foreach (var room in _rooms.Values)
            {
                if (room == null) continue;
                var message = CaptureRoom(room);
                var signature = SignatureOf(message);
                if (_roomSignatures.TryGetValue(message.NodeId, out var previous) && previous == signature) continue;
                _roomSignatures[message.NodeId] = signature;
                SendRoom(message, null);
            }
        }

        private void SendAllRooms(ulong? only)
        {
            foreach (var room in _rooms.Values)
            {
                if (room == null) continue;
                var message = CaptureRoom(room);
                _roomSignatures[message.NodeId] = SignatureOf(message);
                SendRoom(message, only);
            }
        }

        private void SendRoom(RoomStateMessage message, ulong? only)
        {
            _roomVersions.TryGetValue(message.NodeId, out var version);
            message.Version = ++version;
            _roomVersions[message.NodeId] = version;
            var json = CoopJson.Write(message);
            if (only.HasValue) _bus.SendToClient(only.Value, CoopKinds.RoomState, json);
            else _bus.SendToClients(CoopKinds.RoomState, json);
            RoomStatesSent++;
        }

        // ---------------------------------------------------------------- loot

        private sealed class TrackedLoot
        {
            public uint LootId;
            public GameObject Go;
            public Vector2 LastPosition;
        }

        private void SyncLoot()
        {
            if (_ground == null) return;
            foreach (var go in _ground.Tracked)
            {
                if (go == null) continue;
                var key = go.GetInstanceID();
                if (_lootIds.ContainsKey(key)) continue;
                var message = LootMessageOf(go);
                if (message == null) continue;
                var tracked = new TrackedLoot { LootId = ++_nextLootId, Go = go, LastPosition = go.transform.position };
                message.LootId = tracked.LootId;
                _loot2[tracked.LootId] = tracked;
                _lootIds[key] = tracked.LootId;
                _bus.SendToClients(CoopKinds.LootSpawn, CoopJson.Write(message));
                LootSpawnsSent++;
            }

            foreach (var tracked in _loot2.Values.ToList())
            {
                var gone = tracked.Go == null
                           || (tracked.Go.GetComponent<WorldItemPickup>() is { } item && (item.IsConsumed || item.Item == null))
                           || (tracked.Go.GetComponent<CoinPickup>() is { } coins && coins.IsCollected);
                if (gone)
                {
                    _loot2.Remove(tracked.LootId);
                    _bus.SendToClients(CoopKinds.LootGone, CoopJson.Write(new LootGoneMessage { LootId = tracked.LootId }));
                    LootGoneSent++;
                    continue;
                }

                var position = (Vector2)tracked.Go.transform.position;
                if ((position - tracked.LastPosition).sqrMagnitude > 0.09f)
                {
                    tracked.LastPosition = position;
                    _bus.SendToClients(CoopKinds.LootMove, CoopJson.Write(new LootMoveMessage { LootId = tracked.LootId, X = position.x, Y = position.y }));
                }
            }
        }

        private LootSpawnMessage LootMessageOf(GameObject go)
        {
            var item = go.GetComponent<WorldItemPickup>();
            if (item != null && item.Item != null && !item.IsConsumed)
                return new LootSpawnMessage { Depth = _depth, Item = item.Item.ToSnapshot(), X = go.transform.position.x, Y = go.transform.position.y };
            var coins = go.GetComponent<CoinPickup>();
            if (coins != null && !coins.IsCollected && coins.Amount > 0)
                return new LootSpawnMessage { Depth = _depth, Coins = coins.Amount, X = go.transform.position.x, Y = go.transform.position.y };
            return null;
        }

        private void SendAllLoot(ulong clientId)
        {
            foreach (var tracked in _loot2.Values)
            {
                if (tracked.Go == null) continue;
                var message = LootMessageOf(tracked.Go);
                if (message == null) continue;
                message.LootId = tracked.LootId;
                _bus.SendToClient(clientId, CoopKinds.LootSpawn, CoopJson.Write(message));
            }
        }

        // ---------------------------------------------------------------- wallets

        private readonly Dictionary<ulong, int> _publishedCoins = new();

        /// <summary>58/84: each member's Carried Coins as the host holds them, on its player object.</summary>
        private void PublishWallets()
        {
            foreach (var clientId in _bus.RemoteClients.Concat(new[] { _bus.LocalClientId }))
            {
                var entity = _party.EntityOf(clientId);
                if (entity == null) continue;
                var receiver = entity.GetComponent<PlayerLootReceiver>();
                var net = entity.GetComponent<NetworkPlayerObject>();
                if (receiver?.Wallet == null || net == null || !net.IsSpawned) continue;
                var balance = receiver.Wallet.Balance;
                if (_publishedCoins.TryGetValue(clientId, out var published) && published == balance) continue;
                _publishedCoins[clientId] = balance;
                net.SetCarriedCoins(balance);
            }
        }

        // ---------------------------------------------------------------- transit

        private void OnTransitOpened(TransitDecision decision)
        {
            _transit = decision;
            decision.Submitted += (_, _, _) => SendVotes(null);
            decision.VotersChanged += _ => SendVotes(null);
            decision.Resolved += OnTransitResolved;
            var boss = _rooms.Values.Select(r => r != null ? r.GetComponent<RoomContentBinding>() : null).FirstOrDefault(b => b?.Boss != null)?.Boss;
            var message = new TransitOpenMessage
            {
                Depth = _depth,
                BossXp = boss != null ? boss.XpAwarded : 0,
                Living = decision.LivingPlayers.ToArray(),
                Dead = decision.DeadPlayers.ToArray()
            };
            _bus.SendToClients(CoopKinds.TransitOpen, CoopJson.Write(message));
            SendVotes(null);
        }

        private void SendVotes(ulong? only)
        {
            if (_transit == null) return;
            var message = new TransitVotesMessage
            {
                Voters = _transit.Choices.Keys.ToArray(),
                Choices = _transit.Choices.Values.Select(c => (int)c).ToArray(),
                Living = _transit.LivingPlayers.ToArray(),
                Submissions = _transit.Submissions
            };
            var json = CoopJson.Write(message);
            if (only.HasValue) _bus.SendToClient(only.Value, CoopKinds.TransitVotes, json);
            else _bus.SendToClients(CoopKinds.TransitVotes, json);
        }

        private void OnTransitResolved(TransitDecision decision, TransitChoice choice)
        {
            decision.Resolved -= OnTransitResolved;
            TransitResolutionsSent++;
            _bus.SendToClients(CoopKinds.TransitResolved, CoopJson.Write(new TransitResolvedMessage { Depth = _depth, Choice = (int)choice }));
        }

        private void OnExpeditionEnded(ExpeditionSummary summary)
        {
            // A Return reaches the clients as the Transit result (each runs its own Return); anything else ended the
            // expedition for the whole party (wipe, host quit) and every client fails with it (85).
            if (summary != null && summary.IsSuccess) return;
            _bus.SendToClients(CoopKinds.RunEnded, CoopJson.Write(new RunEndedMessage { Outcome = 1, Reason = "party_failed" }));
        }

        // ---------------------------------------------------------------- shots

        private void OnProjectileLaunched(ProjectilePool pool, Vector2 origin, ProjectileSpawnData data)
        {
            if (_bus.RemoteClients.Count == 0) return;
            // Enemy fire and the host's own shots are drawn on every client; a client's own shots arrive from it.
            var isEnemy = data.SourceTeam == DamageTeam.Enemy;
            var isHostPlayer = _localEntity != null && data.Source == _localEntity;
            if (!isEnemy && !isHostPlayer) return;
            _bus.SendShot(new ShotNetRecord
            {
                Origin = origin,
                Direction = data.Direction,
                Speed = data.Speed,
                Range = data.MaxRange,
                Team = (int)data.SourceTeam,
                Visual = new Unity.Collections.FixedString32Bytes(data.VisualId ?? string.Empty)
            });
        }

        private void OnShotReceived(ShotNetRecord shot)
        {
            ShotsRelayed++;
            RemoteShot?.Invoke(shot);
        }

        // ---------------------------------------------------------------- requests

        private void OnReceived(ulong sender, string kind, string json)
        {
            if (_disposed || !_bus.IsHost) return;
            switch (kind)
            {
                case CoopKinds.Hit: HandleHit(sender, CoopJson.Read<HitRequestMessage>(json)); break;
                case CoopKinds.Impact: HandleImpact(sender, CoopJson.Read<ImpactRequestMessage>(json)); break;
                case CoopKinds.Heal: HandleHeal(sender, CoopJson.Read<HealRequestMessage>(json)); break;
                case CoopKinds.Vote: HandleVote(sender, CoopJson.Read<VoteRequestMessage>(json)); break;
                case CoopKinds.Buy: HandleBuy(sender, CoopJson.Read<TradeRequestMessage>(json)); break;
                case CoopKinds.Sell: HandleSell(sender, CoopJson.Read<TradeRequestMessage>(json)); break;
                case CoopKinds.CacheChoose: HandleCache(sender, CoopJson.Read<TradeRequestMessage>(json)); break;
                case CoopKinds.Drop: DropRequested?.Invoke(sender, CoopJson.Read<TradeRequestMessage>(json)); break;
                case CoopKinds.Revive: ReviveRequested?.Invoke(sender, CoopJson.Read<ReviveRequestMessage>(json)); break;
                case CoopKinds.InventorySnapshot: InventoryMirrorReceived?.Invoke(sender, CoopJson.Read<InventorySnapshotMessage>(json)); break;
                case CoopKinds.Resync: ServeResync(sender); break;
                case CoopKinds.DepthReady: HandleDepthReady(sender, CoopJson.Read<DepthReadyMessage>(json)); break;
                case CoopKinds.ProofReport: ProofReportReceived?.Invoke(sender, CoopJson.Read<ProofMessage>(json)); break;
            }
        }

        private void Reject(string reason)
        {
            HitsRejected++;
            LastRejection = reason;
        }

        /// <summary>
        /// 82 damage validation: the sender must be a living member that may act, the target a living enemy the host
        /// replicated, within reach of the sender's host position, at a rate and a size the sender's weapons allow.
        /// </summary>
        public bool HandleHit(ulong sender, HitRequestMessage hit)
        {
            if (hit == null) { Reject("malformed"); return false; }
            var entity = _party.EntityOf(sender);
            if (entity == null) { Reject("unknown sender"); return false; }
            var gate = entity.GetComponent<IPlayerActionGate>();
            if (gate != null && !gate.CanAct) { Reject("sender cannot act (downed/dead)"); return false; }
            if (!_actors.TryGetValue(hit.NetId, out var tracked) || tracked.Go == null || !tracked.Alive) { Reject("unknown or dead target"); return false; }
            if (hit.Amount <= 0 || hit.Amount > Math.Max(1, _party.MaxHitOf(sender))) { Reject($"hit size {hit.Amount} out of range"); return false; }
            if (Vector2.Distance(entity.transform.position, tracked.Go.transform.position) > MaxHitDistance) { Reject("target out of reach"); return false; }
            if (!RateOk(sender)) { Reject("hit rate exceeded"); return false; }
            var health = tracked.Go.GetComponent<HealthComponent>();
            if (health == null) { Reject("target has no health"); return false; }
            var before = health.CurrentHealth;
            var direction = new Vector2(hit.DirX, hit.DirY);
            var applied = health.TryApplyDamage(new DamageRequest(hit.Amount, (DamageKind)hit.DamageKind, Mathf.Clamp(hit.StaggerPower, 0f, MaxStagger), direction));
            if (!applied) { LastRejection = "target refused (invulnerable/shielded)"; return false; }
            HitsApplied++;
            // This member's hit took the enemy's last health: the kill is the member's (its own rig's kill passives).
            // A melee hit carries no direction and no blast tag.
            if (before > 0 && !health.IsAlive)
            {
                KillsAttributed++;
                _bus.SendToClient(sender, CoopKinds.Kill, CoopJson.Write(new KillMessage { Melee = (DamageKind)hit.DamageKind == DamageKind.Normal && direction == Vector2.zero }));
            }

            var dealt = Math.Max(0, before - health.CurrentHealth);
            HitDamageApplied += dealt;
            _hitsBySender[sender] = (_hitsBySender.TryGetValue(sender, out var h) ? h : 0) + 1;
            _damageBySender[sender] = (_damageBySender.TryGetValue(sender, out var d) ? d : 0) + dealt;
            return true;
        }

        private bool RateOk(ulong sender)
        {
            if (!_hitRates.TryGetValue(sender, out var window)) _hitRates[sender] = window = new RateWindow();
            return window.Allow(Time.realtimeSinceStartup, MaxHitsPerSecond);
        }

        private sealed class RateWindow
        {
            private float _start;
            private int _count;

            public bool Allow(float now, int perSecond)
            {
                if (now - _start >= 1f) { _start = now; _count = 0; }
                _count++;
                return _count <= perSecond;
            }
        }

        public bool HandleImpact(ulong sender, ImpactRequestMessage impact)
        {
            if (impact == null) return false;
            var entity = _party.EntityOf(sender);
            if (entity == null) return false;
            var gate = entity.GetComponent<IPlayerActionGate>();
            if (gate != null && !gate.CanAct) return false;
            if (!_actors.TryGetValue(impact.NetId, out var tracked) || tracked.Go == null || !tracked.Alive) return false;
            if (Vector2.Distance(entity.transform.position, tracked.Go.transform.position) > MaxHitDistance) return false;
            var struck = tracked.Go.GetComponentInChildren<Collider2D>();
            if (struck == null) return false;
            // The member's impact hooks (Wallbreaker, Arc Stagger) run on the host's copy of its body, where this impact
            // is resolved; the member's own rig never runs them.
            ImpactDispatcher.Apply(struck, new ImpactRequest(new Vector2(impact.DirX, impact.DirY), Mathf.Clamp(impact.Knockback, 0f, MaxKnockback),
                Mathf.Clamp(impact.StaggerPower, 0f, MaxStagger), (DamageKind)impact.DamageKind, entity, entity.GetComponent<PlayerImpactReceiver>()));
            ImpactsApplied++;
            return true;
        }

        public bool HandleHeal(ulong sender, HealRequestMessage heal)
        {
            if (heal == null || heal.Amount <= 0) return false;
            var entity = _party.EntityOf(sender);
            var health = entity != null ? entity.GetComponent<HealthComponent>() : null;
            var life = entity != null ? entity.GetComponent<PlayerLifeStateComponent>() : null;
            if (health == null || (life != null && !life.IsAlive)) return false;
            var now = Time.realtimeSinceStartup;
            if (_lastHeal.TryGetValue(sender, out var last) && now - last < HealCooldownSeconds) return false;
            _lastHeal[sender] = now;
            if (!health.Heal(Mathf.Min(heal.Amount, health.MaxHealth))) return false;
            HealsApplied++;
            return true;
        }

        /// <summary>86: the vote is the sender's own (its participant id), counted once; dead players have none.</summary>
        public bool HandleVote(ulong sender, VoteRequestMessage vote)
        {
            var transit = _expedition?.Transit;
            var participant = _party.ParticipantOf(sender);
            if (vote == null || transit == null || string.IsNullOrEmpty(participant) || (vote.Depth != 0 && vote.Depth != _depth) || !transit.CanVote(participant))
            {
                VotesRejected++;
                return false;
            }

            VotesApplied++;
            transit.Submit(participant, (TransitChoice)vote.Choice);
            SendVotes(null);
            return true;
        }

        private RoomContentBinding BindingAt(int node) => _rooms.TryGetValue(node, out var room) && room != null ? room.GetComponent<RoomContentBinding>() : null;

        private void HandleBuy(ulong sender, TradeRequestMessage request)
        {
            if (request == null || _loot == null) return;
            var merchant = BindingAt(request.RoomNode)?.Merchant?.Merchant;
            var offer = merchant?.Offers.FirstOrDefault(o => o.Index == request.OfferIndex);
            var snapshot = offer?.Item?.ToSnapshot();
            var result = _loot.RequestMerchantBuy(request.TransactionId, sender, merchant, request.OfferIndex);
            TradesResolved++;
            if (result.Verdict == LootVerdict.Accepted && snapshot != null) SendGrant(sender, snapshot, request.TransactionId, "merchant");
            _bus.SendToClient(sender, CoopKinds.TradeResult, CoopJson.Write(new TradeResultMessage
            {
                TransactionId = request.TransactionId, IsBuy = true, Verdict = (int)result.Verdict, Error = result.Detail,
                ItemName = offer != null ? offer.Definition.DisplayName : string.Empty, Coins = result.Coins, RoomNode = request.RoomNode, OfferIndex = request.OfferIndex
            }));
        }

        private void HandleSell(ulong sender, TradeRequestMessage request)
        {
            if (request == null || _loot == null) return;
            var merchant = BindingAt(request.RoomNode)?.Merchant?.Merchant;
            var result = _loot.RequestMerchantSell(request.TransactionId, sender, merchant, request.InstanceId);
            TradesResolved++;
            if (result.Verdict == LootVerdict.Accepted) SendRevoke(sender, request.InstanceId, result.Quantity, request.TransactionId);
            _bus.SendToClient(sender, CoopKinds.TradeResult, CoopJson.Write(new TradeResultMessage
            {
                TransactionId = request.TransactionId, IsBuy = false, Verdict = (int)result.Verdict, Error = result.Detail, Coins = result.Coins, RoomNode = request.RoomNode
            }));
        }

        private void HandleCache(ulong sender, TradeRequestMessage request)
        {
            if (request == null || _loot == null) return;
            var binding = BindingAt(request.RoomNode);
            var cache = binding?.EventInstance as WeaponCacheEvent;
            var entity = _party.EntityOf(sender);
            var actor = entity != null ? DungeonEventInteractable.ActorFor(entity) : null;
            var choice = cache != null && request.OfferIndex >= 0 && request.OfferIndex < cache.Choices.Count ? cache.Choices[request.OfferIndex] : null;
            var snapshot = choice?.Item?.ToSnapshot();
            var result = _loot.RequestCacheChoose(request.TransactionId, sender, cache, actor, request.OfferIndex);
            TradesResolved++;
            if (result.Verdict == LootVerdict.Accepted && snapshot != null) SendGrant(sender, snapshot, request.TransactionId, "weapon_cache");
            _bus.SendToClient(sender, CoopKinds.CacheResult, CoopJson.Write(new CacheResultMessage
            {
                TransactionId = request.TransactionId, Accepted = result.Verdict == LootVerdict.Accepted, AlreadyTaken = result.Verdict == LootVerdict.AlreadyTaken,
                ItemName = choice?.Definition != null ? choice.Definition.DisplayName : string.Empty, RoomNode = request.RoomNode
            }));
        }

        /// <summary>The host's verdict on a member's Defibrillator use (the member spends the unit only when accepted).</summary>
        public void SendReviveResult(ulong clientId, ReviveResultMessage result)
        {
            if (result == null || clientId == _bus.LocalClientId) return;
            _bus.SendToClient(clientId, CoopKinds.ReviveResult, CoopJson.Write(result));
        }

        /// <summary>Hands an item the host granted a remote member to that member's own inventory.</summary>
        public void SendGrant(ulong clientId, ItemInstanceSnapshot item, string transactionId, string source)
        {
            if (item == null || clientId == _bus.LocalClientId) return;
            GrantsSent++;
            _bus.SendToClient(clientId, CoopKinds.Grant, CoopJson.Write(new GrantMessage { TransactionId = transactionId, Source = source, Item = item }));
        }

        /// <summary>Removes an item the host took from a remote member (sold, dropped) from that member's own inventory.</summary>
        public void SendRevoke(ulong clientId, string instanceId, int quantity, string transactionId)
        {
            if (string.IsNullOrEmpty(instanceId) || clientId == _bus.LocalClientId) return;
            RevokesSent++;
            _bus.SendToClient(clientId, CoopKinds.Revoke, CoopJson.Write(new RevokeMessage { TransactionId = transactionId, InstanceId = instanceId, Quantity = quantity }));
        }

        /// <summary>Tells one member what its interaction on the host did (the same notice line a local press shows).</summary>
        public void SendNotice(ulong clientId, string text, bool isProblem)
        {
            if (string.IsNullOrEmpty(text) || clientId == _bus.LocalClientId) return;
            _bus.SendToClient(clientId, CoopKinds.Notice, CoopJson.Write(new NoticeMessage { Text = text, IsProblem = isProblem }));
        }

        private void HandleDepthReady(ulong sender, DepthReadyMessage ready)
        {
            if (ready == null) return;
            if (!ready.Success)
            {
                Debug.LogError($"COOP-HOST client {sender} could not build depth {ready.Depth}: {ready.Error}");
                return;
            }

            if (ready.Depth != _depth) return;
            _depthReady.Add(sender);
        }

        /// <summary>
        /// 85: a reconnecting (or late-composing) client gets the current world — every room as it stands now (never
        /// its start state), every living enemy, the boss, the ground loot and the open vote — not the depth's start.
        /// </summary>
        public void ServeResync(ulong clientId)
        {
            ResyncsServed++;
            foreach (var tracked in _actors.Values.Where(a => a.Go != null && a.Alive)) SendSpawn(tracked, clientId);
            SendAllRooms(clientId);
            SendAllLoot(clientId);
            foreach (var room in _rooms.Values)
            {
                var binding = room != null ? room.GetComponent<RoomContentBinding>() : null;
                if (binding?.Boss != null) SendBossState(binding.Boss, room.State.NodeId, clientId);
            }

            if (_transit != null && _transit.State != TransitDecisionState.Closed)
            {
                var boss = _rooms.Values.Select(r => r != null ? r.GetComponent<RoomContentBinding>() : null).FirstOrDefault(b => b?.Boss != null)?.Boss;
                _bus.SendToClient(clientId, CoopKinds.TransitOpen, CoopJson.Write(new TransitOpenMessage { Depth = _depth, BossXp = boss != null ? boss.XpAwarded : 0, Living = _transit.LivingPlayers.ToArray(), Dead = _transit.DeadPlayers.ToArray() }));
                SendVotes(clientId);
            }

            // A reconnecting member joins a depth whose gameplay already runs: it is released at once.
            if (!_gameplayHeld) _bus.SendToClient(clientId, CoopKinds.GameplayRelease, CoopJson.Write(new GameplayReleaseMessage { Depth = _depth, ReadyPeers = _depthReady.Count }));
            ResyncRequested?.Invoke(clientId);
        }

        /// <summary>Proof orchestration (built-player proof only): a command to one client or to all.</summary>
        public void SendProof(ulong? clientId, ProofMessage message)
        {
            var json = CoopJson.Write(message);
            if (clientId.HasValue) _bus.SendToClient(clientId.Value, CoopKinds.ProofCommand, json);
            else _bus.SendToClients(CoopKinds.ProofCommand, json);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bus.Received -= OnReceived;
            _bus.ShotReceived -= OnShotReceived;
            ProjectilePool.Launched -= OnProjectileLaunched;
            if (_expedition != null)
            {
                _expedition.TransitOpened -= OnTransitOpened;
                _expedition.ExpeditionEnded -= OnExpeditionEnded;
            }

            if (_gameplayHeld)
            {
                _gameplayHeld = false;
                RuinRail.Core.Input.GameplayInputGate.Release();
            }
        }
    }
}
