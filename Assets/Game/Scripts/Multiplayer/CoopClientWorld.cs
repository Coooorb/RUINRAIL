using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using Unity.Collections;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// The client half of the co-op expedition (82): this peer's copy of the host's world. It builds nothing
    /// authoritative — rooms mirror the host's lifecycle, enemies are replicas the host moves and kills, loot on the
    /// ground is presentation the host resolves — and it turns this player's actions into requests: hits and impacts
    /// on replicas, heals on the local player, votes, trades, cache choices, drops. What comes back (grants, revokes,
    /// XP, the vote result, the run's end) is raised as events for the run's composition root to apply to this peer's
    /// own inventory, wallet and expedition transaction.
    /// </summary>
    public sealed class CoopClientWorld : IDisposable
    {
        public const float CorpseSeconds = 1.2f;
        public const float InventoryMirrorIntervalSeconds = 0.25f;

        private readonly ICoopBus _bus;
        private readonly EnemyReplicaRegistry _replicas;
        private readonly Transform _actorRoot;
        private readonly Dictionary<uint, GameObject> _loot = new();
        private readonly Dictionary<uint, EnemySpawnMessage> _spawnRecords = new();
        private readonly Dictionary<uint, float> _dying = new();
        private readonly Dictionary<int, uint> _roomVersions = new();
        private readonly List<EnemyNetState[]> _pendingStates = new();
        private IReadOnlyDictionary<int, RoomRuntime> _rooms = new Dictionary<int, RoomRuntime>();
        private LootSpawner _lootSpawner;
        private Func<string, ItemDefinition> _resolve;
        private ProjectilePool _shotPool;
        private GameObject _localPlayer;
        private uint _hitSequence;
        private int _depth;
        private bool _disposed;
        private bool _relaysInstalled;
        private float _inventoryTimer;
        private bool _inventoryDirty;
        private uint _inventoryVersion;
        private Func<InventorySnapshot> _inventorySource;
        private Func<int[]> _skillsSource;
        private Func<int> _coinsBroughtIn;

        public CoopClientWorld(ICoopBus bus, Transform actorRoot = null)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _actorRoot = actorRoot;
            _replicas = new EnemyReplicaRegistry(actorRoot);
            _bus.Received += OnReceived;
            _bus.EnemyStatesReceived += OnEnemyStates;
            _bus.ShotReceived += OnShot;
        }

        // ---------------------------------------------------------------- diagnostics

        public int Depth => _depth;
        public EnemyReplicaRegistry Replicas => _replicas;
        public IReadOnlyDictionary<uint, GameObject> Loot => _loot;
        public int RoomStatesApplied { get; private set; }
        public int RoomStatesIgnored { get; private set; }
        public int EnemyStatesApplied { get; private set; }
        public int EnemyDeaths { get; private set; }
        public int LootSpawns { get; private set; }
        public int LootRemovals { get; private set; }
        public int HitsSent { get; private set; }
        public int ImpactsSent { get; private set; }
        public int HealsSent { get; private set; }
        public int ShotsDrawn { get; private set; }
        public int ShotsSent { get; private set; }
        public int GrantsReceived { get; private set; }
        public int RevokesReceived { get; private set; }
        public int InventoryMirrorsSent { get; private set; }
        public int XpReceived { get; private set; }
        public int BossStatesReceived { get; private set; }
        public BossStateMessage LastBossState { get; private set; }
        public TransitVotesMessage LastVotes { get; private set; }

        /// <summary>A replica was created; the composition root composes its presentation (body, bar, telegraph, audio).</summary>
        public event Action<EnemyReplica, EnemySpawnMessage> ActorSpawned;
        public event Action<EnemyReplica> ActorDied;
        /// <summary>A ground pickup appeared; the composition root attaches its audio/prompt hooks.</summary>
        public event Action<GameObject> LootSpawned;
        public event Action<XpMessage> Xp;
        public event Action<BossStateMessage, EnemyReplica> BossState;
        public event Action<TransitOpenMessage> TransitOpened;
        public event Action<TransitVotesMessage> TransitVotes;
        public event Action<TransitResolvedMessage> TransitResolved;
        public event Action<GrantMessage> Granted;
        public event Action<RevokeMessage> Revoked;
        public event Action<TradeResultMessage> TradeResult;
        public event Action<CacheResultMessage> CacheResult;
        public event Action<ReviveResultMessage> ReviveResult;
        public event Action<KillMessage> KillConfirmed;
        public event Action<NoticeMessage> Notice;
        public event Action<GameplayReleaseMessage> GameplayReleased;
        public event Action<RunEndedMessage> RunEnded;
        public event Action<ProofMessage> ProofCommand;
        public event Action<RoomRuntime, RoomStateMessage> RoomApplied;

        // ---------------------------------------------------------------- depth binding

        /// <summary>
        /// Binds this peer's rebuild of the host's depth. Every room here is non-authoritative (82): it only ever takes
        /// the host's lifecycle, so it can neither spawn, lock, clear nor reward on its own.
        /// </summary>
        public void BindDepth(int depth, IReadOnlyDictionary<int, RoomRuntime> rooms, LootSpawner presentationLoot, Func<string, ItemDefinition> resolve)
        {
            _replicas.Clear();
            _spawnRecords.Clear();
            _dying.Clear();
            foreach (var go in _loot.Values) if (go != null) UnityEngine.Object.Destroy(go);
            _loot.Clear();
            _roomVersions.Clear();
            _depth = depth;
            _rooms = rooms ?? new Dictionary<int, RoomRuntime>();
            _lootSpawner = presentationLoot;
            _resolve = resolve ?? (_ => null);
            foreach (var room in _rooms.Values) room?.SetAuthoritative(false);
        }

        /// <summary>Tells the host this peer built the depth (or why it could not).</summary>
        public void ReportDepth(int depth, bool success, string layoutFingerprint, string error)
        {
            _bus.SendToHost(CoopKinds.DepthReady, CoopJson.Write(new DepthReadyMessage { Depth = depth, Success = success, LayoutFingerprint = layoutFingerprint, Error = error, Rooms = _rooms.Count }));
        }

        /// <summary>Asks the host for everything as it stands now (after composing late or reconnecting).</summary>
        public void RequestResync() => _bus.SendToHost(CoopKinds.Resync, "{}");

        /// <summary>A Defibrillator use: the host picks the Dead teammate in reach and answers with the verdict.</summary>
        public void RequestRevive(string consumableId, string transactionId) =>
            _bus.SendToHost(CoopKinds.Revive, CoopJson.Write(new ReviveRequestMessage { TransactionId = transactionId, ConsumableId = consumableId }));

        // ---------------------------------------------------------------- relays (hits / heals / impacts / shots)

        /// <summary>
        /// Installs the request relays for the local player (82): its hits on replicas, its impacts and its heals go to
        /// the host; its shots are announced for the other peers to draw. Removed again in <see cref="Dispose"/>.
        /// </summary>
        public void InstallRelays(GameObject localPlayer)
        {
            _localPlayer = localPlayer;
            if (_relaysInstalled) return;
            _relaysInstalled = true;
            DamageAuthority.RemoteDamageRelay = RelayDamage;
            DamageAuthority.RemoteHealRelay = RelayHeal;
            ImpactDispatcher.RemoteImpactRelay = RelayImpact;
            ProjectilePool.Launched += OnLocalLaunch;
        }

        /// <summary>Rebinds the relays to the local player after it was re-attached (reconnect).</summary>
        public void SetLocalPlayer(GameObject localPlayer) => _localPlayer = localPlayer;

        private bool RelayDamage(HealthComponent target, DamageRequest request)
        {
            if (target == null || request.Amount <= 0) return false;
            var replica = target.GetComponent<EnemyReplica>();
            if (replica == null || replica.IsDead) return false;
            HitsSent++;
            _bus.SendToHost(CoopKinds.Hit, CoopJson.Write(new HitRequestMessage
            {
                NetId = replica.NetId,
                Amount = request.Amount,
                DamageKind = (int)request.Kind,
                StaggerPower = request.StaggerPower,
                DirX = request.HitDirection.x,
                DirY = request.HitDirection.y,
                Sequence = ++_hitSequence
            }));
            return true;
        }

        private bool RelayImpact(Component struck, ImpactRequest request)
        {
            var replica = struck != null ? struck.GetComponentInParent<EnemyReplica>() : null;
            if (replica == null || replica.IsDead || (!request.HasKnockback && !request.HasStagger)) return false;
            ImpactsSent++;
            _bus.SendToHost(CoopKinds.Impact, CoopJson.Write(new ImpactRequestMessage
            {
                NetId = replica.NetId,
                DirX = request.Direction.x,
                DirY = request.Direction.y,
                Knockback = request.Knockback,
                StaggerPower = request.StaggerPower,
                DamageKind = (int)request.Kind
            }));
            return true;
        }

        private bool RelayHeal(HealthComponent target, int amount)
        {
            if (target == null || _localPlayer == null || target.gameObject != _localPlayer || amount <= 0) return false;
            HealsSent++;
            _bus.SendToHost(CoopKinds.Heal, CoopJson.Write(new HealRequestMessage { Amount = amount }));
            return true;
        }

        private void OnLocalLaunch(ProjectilePool pool, Vector2 origin, ProjectileSpawnData data)
        {
            if (_localPlayer == null || data.Source != _localPlayer) return;
            ShotsSent++;
            _bus.SendShot(new ShotNetRecord
            {
                Origin = origin,
                Direction = data.Direction,
                Speed = data.Speed,
                Range = data.MaxRange,
                Team = (int)data.SourceTeam,
                Visual = new FixedString32Bytes(data.VisualId ?? string.Empty)
            });
        }

        private void OnShot(ShotNetRecord shot)
        {
            if (shot.Shooter == _bus.LocalClientId) return;
            if (_shotPool == null)
            {
                var go = new GameObject("CoopShotPresentation");
                if (_actorRoot != null) go.transform.SetParent(_actorRoot, false);
                _shotPool = go.AddComponent<ProjectilePool>();
                _shotPool.IsPresentationOnly = true;
            }

            ShotsDrawn++;
            // Zero damage and no knockback: another peer's shot is drawn along its real path and resolves nothing here.
            _shotPool.Spawn(shot.Origin, new ProjectileSpawnData(0, shot.Speed, shot.Range, 0f, 0f, shot.Direction, null, null, 0f,
                (DamageTeam)shot.Team, false, shot.Visual.ToString()));
        }

        // ---------------------------------------------------------------- inventory mirror

        /// <summary>
        /// This player's inventory stays its own (its save, its transaction); the host keeps a mirror of it for capacity
        /// checks, drops and stats. Changes are sent at most four times a second.
        /// </summary>
        public void BindInventoryMirror(Func<InventorySnapshot> inventory, Func<int[]> skills, Func<int> coinsBroughtIn = null)
        {
            _inventorySource = inventory;
            _skillsSource = skills;
            _coinsBroughtIn = coinsBroughtIn;
            MarkInventoryDirty();
        }

        public void MarkInventoryDirty() => _inventoryDirty = true;

        private void FlushInventory()
        {
            if (!_inventoryDirty || _inventorySource == null) return;
            _inventoryDirty = false;
            InventoryMirrorsSent++;
            _bus.SendToHost(CoopKinds.InventorySnapshot, CoopJson.Write(new InventorySnapshotMessage
            {
                Version = ++_inventoryVersion,
                Inventory = _inventorySource(),
                SkillRanks = _skillsSource?.Invoke() ?? Array.Empty<int>(),
                CoinsBroughtIn = _coinsBroughtIn?.Invoke() ?? -1
            }));
        }

        // ---------------------------------------------------------------- requests

        public static string NewTransactionId() => Guid.NewGuid().ToString("N");

        public void SendVote(TransitChoice choice) => _bus.SendToHost(CoopKinds.Vote, CoopJson.Write(new VoteRequestMessage { Depth = _depth, Choice = (int)choice }));

        public string SendBuy(int roomNode, int offerIndex, string transactionId = null)
        {
            var tx = transactionId ?? NewTransactionId();
            _bus.SendToHost(CoopKinds.Buy, CoopJson.Write(new TradeRequestMessage { TransactionId = tx, Depth = _depth, RoomNode = roomNode, OfferIndex = offerIndex }));
            return tx;
        }

        public string SendSell(int roomNode, string instanceId, string transactionId = null)
        {
            var tx = transactionId ?? NewTransactionId();
            _bus.SendToHost(CoopKinds.Sell, CoopJson.Write(new TradeRequestMessage { TransactionId = tx, Depth = _depth, RoomNode = roomNode, InstanceId = instanceId }));
            return tx;
        }

        public string SendCacheChoice(int roomNode, int index, string transactionId = null)
        {
            var tx = transactionId ?? NewTransactionId();
            _bus.SendToHost(CoopKinds.CacheChoose, CoopJson.Write(new TradeRequestMessage { TransactionId = tx, Depth = _depth, RoomNode = roomNode, OfferIndex = index }));
            return tx;
        }

        public string SendDrop(string instanceId, int quantity, string transactionId = null)
        {
            var tx = transactionId ?? NewTransactionId();
            _bus.SendToHost(CoopKinds.Drop, CoopJson.Write(new TradeRequestMessage { TransactionId = tx, Depth = _depth, InstanceId = instanceId, Quantity = quantity }));
            return tx;
        }

        public void SendProofReport(ProofMessage report) => _bus.SendToHost(CoopKinds.ProofReport, CoopJson.Write(report));

        // ---------------------------------------------------------------- per-frame

        public void Tick(float deltaTime)
        {
            if (_disposed) return;
            var now = _bus.NetworkTime;
            foreach (var replica in _replicas.Replicas.Values)
            {
                if (replica != null) replica.Step(now, deltaTime);
            }

            if (_dying.Count > 0)
            {
                var clock = Time.time;
                foreach (var pair in _dying.ToList())
                {
                    if (clock < pair.Value) continue;
                    _dying.Remove(pair.Key);
                    _replicas.Despawn(pair.Key);
                }
            }

            _inventoryTimer -= deltaTime;
            if (_inventoryTimer <= 0f)
            {
                _inventoryTimer = InventoryMirrorIntervalSeconds;
                FlushInventory();
            }
        }

        // ---------------------------------------------------------------- dispatch

        private void OnReceived(ulong sender, string kind, string json)
        {
            if (_disposed || _bus.IsHost) return;
            switch (kind)
            {
                case CoopKinds.EnemySpawn: OnEnemySpawn(CoopJson.Read<EnemySpawnMessage>(json)); break;
                case CoopKinds.EnemyGone: OnEnemyGone(CoopJson.Read<EnemyGoneMessage>(json)); break;
                case CoopKinds.BossState: OnBossState(CoopJson.Read<BossStateMessage>(json)); break;
                case CoopKinds.RoomState: ApplyRoom(CoopJson.Read<RoomStateMessage>(json)); break;
                case CoopKinds.LootSpawn: OnLootSpawn(CoopJson.Read<LootSpawnMessage>(json)); break;
                case CoopKinds.LootGone: OnLootGone(CoopJson.Read<LootGoneMessage>(json)); break;
                case CoopKinds.LootMove: OnLootMove(CoopJson.Read<LootMoveMessage>(json)); break;
                case CoopKinds.Xp:
                    XpReceived++;
                    Xp?.Invoke(CoopJson.Read<XpMessage>(json));
                    break;
                case CoopKinds.TransitOpen: TransitOpened?.Invoke(CoopJson.Read<TransitOpenMessage>(json)); break;
                case CoopKinds.TransitVotes:
                    LastVotes = CoopJson.Read<TransitVotesMessage>(json);
                    TransitVotes?.Invoke(LastVotes);
                    break;
                case CoopKinds.TransitResolved: TransitResolved?.Invoke(CoopJson.Read<TransitResolvedMessage>(json)); break;
                case CoopKinds.Grant:
                    GrantsReceived++;
                    Granted?.Invoke(CoopJson.Read<GrantMessage>(json));
                    break;
                case CoopKinds.Revoke:
                    RevokesReceived++;
                    Revoked?.Invoke(CoopJson.Read<RevokeMessage>(json));
                    break;
                case CoopKinds.TradeResult: TradeResult?.Invoke(CoopJson.Read<TradeResultMessage>(json)); break;
                case CoopKinds.CacheResult: CacheResult?.Invoke(CoopJson.Read<CacheResultMessage>(json)); break;
                case CoopKinds.ReviveResult: ReviveResult?.Invoke(CoopJson.Read<ReviveResultMessage>(json)); break;
                case CoopKinds.Kill: KillConfirmed?.Invoke(CoopJson.Read<KillMessage>(json)); break;
                case CoopKinds.Notice: Notice?.Invoke(CoopJson.Read<NoticeMessage>(json)); break;
                case CoopKinds.GameplayRelease: GameplayReleased?.Invoke(CoopJson.Read<GameplayReleaseMessage>(json)); break;
                case CoopKinds.RunEnded: RunEnded?.Invoke(CoopJson.Read<RunEndedMessage>(json)); break;
                case CoopKinds.ProofCommand: ProofCommand?.Invoke(CoopJson.Read<ProofMessage>(json)); break;
            }
        }

        private void OnEnemySpawn(EnemySpawnMessage message)
        {
            if (message == null || (message.Depth != 0 && message.Depth != _depth)) return;
            var existed = _replicas.TryGet(message.NetId, out _);
            var replica = _replicas.Spawn(new EnemySpawnRecord { NetId = message.NetId, DefinitionId = new FixedString64Bytes(message.DefinitionId ?? string.Empty), Position = message.Position, IsBossOrElite = message.Kind != 0 });
            if (existed) return; // a repeated record (resync) never builds a second replica
            _spawnRecords[message.NetId] = message;
            replica.ApplyInitialHealth(message.Health, message.MaxHealth);
            replica.Died += OnReplicaDied;
            ActorSpawned?.Invoke(replica, message);
            if (_pendingStates.Count > 0)
            {
                foreach (var batch in _pendingStates) ApplyStates(batch);
                _pendingStates.Clear();
            }
        }

        private void OnReplicaDied(EnemyReplica replica)
        {
            EnemyDeaths++;
            ActorDied?.Invoke(replica);
            _dying[replica.NetId] = Time.time + CorpseSeconds;
        }

        private void OnEnemyGone(EnemyGoneMessage message)
        {
            if (message == null) return;
            if (!_replicas.TryGet(message.NetId, out var replica)) return;
            if (message.Died) replica.MarkDead();
            else
            {
                _replicas.Despawn(message.NetId);
                _dying.Remove(message.NetId);
            }

            if (message.Died && message.Xp > 0)
            {
                XpReceived++;
                Xp?.Invoke(new XpMessage { Amount = message.Xp, Kind = message.Kind });
            }
        }

        private void OnEnemyStates(EnemyNetState[] states)
        {
            if (states == null) return;
            if (!ApplyStates(states) && _pendingStates.Count < 8) _pendingStates.Add(states);
        }

        private bool ApplyStates(EnemyNetState[] states)
        {
            var allKnown = true;
            foreach (var state in states)
            {
                if (_replicas.TryGet(state.NetId, out var replica))
                {
                    if (replica.Apply(state)) EnemyStatesApplied++;
                }
                else allKnown = false;
            }

            return allKnown;
        }

        private void OnBossState(BossStateMessage message)
        {
            if (message == null) return;
            BossStatesReceived++;
            LastBossState = message;
            _replicas.TryGet(message.NetId, out var replica);
            BossState?.Invoke(message, replica);
        }

        /// <summary>
        /// Applies the host's state of one room (82): lifecycle and counts through the room's own restore path (so a
        /// clear raises Cleared exactly once here too), then every resolved interaction onto this peer's copies of the
        /// room's objects — an opened chest reads opened, a used event reads used, a sold offer reads sold.
        /// </summary>
        public bool ApplyRoom(RoomStateMessage message)
        {
            if (message == null || (message.Depth != 0 && message.Depth != _depth)) { RoomStatesIgnored++; return false; }
            if (!_rooms.TryGetValue(message.NodeId, out var room) || room == null) { RoomStatesIgnored++; return false; }
            if (_roomVersions.TryGetValue(message.NodeId, out var seen) && message.Version != 0 && message.Version <= seen) { RoomStatesIgnored++; return false; }
            _roomVersions[message.NodeId] = message.Version;
            room.SetAuthoritative(false);
            room.RestoreState(new RoomRuntimeState
            {
                NodeId = message.NodeId,
                RoomId = room.State.RoomId,
                RoomType = room.State.RoomType,
                IsElite = room.State.IsElite,
                State = (RoomLifecycleState)message.State,
                EntryCount = message.EntryCount,
                EnemiesSpawned = message.EnemiesSpawned,
                EnemiesDefeated = message.EnemiesDefeated,
                EnemiesRemaining = message.EnemiesRemaining,
                Resolved = new List<string>(message.Resolved ?? Array.Empty<string>())
            });
            if (message.DoorsLocked != room.DoorsLocked)
            {
                if (message.DoorsLocked) room.LockDoors(); else room.UnlockDoors();
            }

            ApplyBindings(room, message);
            RoomStatesApplied++;
            RoomApplied?.Invoke(room, message);
            return true;
        }

        private static void ApplyBindings(RoomRuntime room, RoomStateMessage message)
        {
            var binding = room.GetComponent<RoomContentBinding>();
            if (binding == null) return;
            var resolved = new HashSet<string>(message.Resolved ?? Array.Empty<string>());
            for (var i = 0; i < binding.Chests.Count; i++)
            {
                var chest = binding.Chests[i];
                if (chest == null || chest.IsOpened) continue;
                var id = chest == binding.RewardChest ? binding.RewardChestResolvedId
                    : chest == binding.BossCache ? "boss_cache" : chest.Kind == LootSourceKind.SupplyChest ? RoomCategoryComposer.SupplyChestResolvedId : $"chest:{binding.Chests.IndexOf(chest)}";
                if (resolved.Contains(id)) chest.RestoreOpened();
            }

            if (binding.BossCache != null)
            {
                if (resolved.Contains("boss_cache") && !binding.BossCache.IsOpened) binding.BossCache.RestoreOpened();
                if (binding.BossCache.IsLocked != message.BossCacheLocked) binding.BossCache.SetLocked(message.BossCacheLocked);
            }

            if (binding.EventInstance is DungeonEventBase restorable && binding.EventInstance.Phase != DungeonEventPhase.Completed)
            {
                var eventId = resolved.FirstOrDefault(r => r.StartsWith("event:", StringComparison.Ordinal) && !r.EndsWith(":success", StringComparison.Ordinal));
                if (eventId != null)
                {
                    restorable.RestoreResolved(resolved.Contains(eventId + ":success"));
                    binding.Event?.GetComponent<WorldObjectVisual>()?.SetTint(WorldObjectVisual.ResolvedTint);
                }
            }

            var merchant = binding.Merchant?.Merchant;
            if (merchant != null && message.MerchantSold != null)
            {
                foreach (var index in message.MerchantSold) merchant.ApplySoldFromAuthority(index);
            }

            if (binding.Transit != null && message.TransitActive && !binding.Transit.IsActivated) binding.Transit.Activate();
        }

        private void OnLootSpawn(LootSpawnMessage message)
        {
            if (message == null || _lootSpawner == null || (message.Depth != 0 && message.Depth != _depth)) return;
            if (_loot.ContainsKey(message.LootId)) return; // resync / duplicate: one presentation per host pickup
            GameObject go;
            if (message.Coins > 0)
            {
                var coins = _lootSpawner.CreateCoinPickup(message.Position);
                coins.SetAmount(message.Coins);
                go = coins.gameObject;
            }
            else if (message.Item != null)
            {
                var pickup = _lootSpawner.CreateItemPickup(message.Position);
                var definition = _resolve(message.Item.DefinitionId);
                pickup.Hold(ItemInstance.FromSnapshot(message.Item), definition != null ? definition.Category : null);
                if (definition != null) pickup.SetDisplayName(definition.DisplayName);
                go = pickup.gameObject;
            }
            else return;

            go.name = $"CoopLoot_{message.LootId}";
            _loot[message.LootId] = go;
            LootSpawns++;
            LootSpawned?.Invoke(go);
        }

        private void OnLootGone(LootGoneMessage message)
        {
            if (message == null || !_loot.TryGetValue(message.LootId, out var go)) return;
            _loot.Remove(message.LootId);
            LootRemovals++;
            if (go != null) UnityEngine.Object.Destroy(go);
        }

        private void OnLootMove(LootMoveMessage message)
        {
            if (message == null || !_loot.TryGetValue(message.LootId, out var go) || go == null) return;
            go.transform.position = new Vector3(message.X, message.Y, go.transform.position.z);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bus.Received -= OnReceived;
            _bus.EnemyStatesReceived -= OnEnemyStates;
            _bus.ShotReceived -= OnShot;
            if (_relaysInstalled)
            {
                ProjectilePool.Launched -= OnLocalLaunch;
                DamageAuthority.ClearRelays();
                ImpactDispatcher.RemoteImpactRelay = null;
            }

            _replicas.Clear();
            foreach (var go in _loot.Values) if (go != null) UnityEngine.Object.Destroy(go);
            _loot.Clear();
            if (_shotPool != null) UnityEngine.Object.Destroy(_shotPool.gameObject);
        }
    }
}
