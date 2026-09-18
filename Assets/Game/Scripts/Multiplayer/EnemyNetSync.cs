using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Attacks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>One authoritative spawn: stable network id, definition, position. Clients build a replica from it.</summary>
    [Serializable]
    public struct EnemySpawnRecord : INetworkSerializable
    {
        public uint NetId;
        public FixedString64Bytes DefinitionId;
        public Vector2 Position;
        public bool IsBossOrElite;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetId);
            serializer.SerializeValue(ref DefinitionId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref IsBossOrElite);
        }
    }

    /// <summary>Readable replica state: where it is, what it is doing (telegraph/attack/stagger/dead), how hurt it is.</summary>
    [Serializable]
    public struct EnemyNetState : INetworkSerializable
    {
        public uint NetId;
        public Vector2 Position;
        public Vector2 Facing;
        public int State;
        public int Health;
        public int MaxHealth;
        public bool IsAlive;
        public uint Version;
        public double Time;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref NetId);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref Version);
            serializer.SerializeValue(ref Time);
        }
    }

    /// <summary>
    /// The only spawner the room runtime and summoners use in a session: on the host it delegates to the real spawner
    /// and assigns network ids; on a client it spawns nothing (82: enemy spawning is a host decision) and counts the
    /// attempt so tests and diagnostics can see it.
    /// </summary>
    public sealed class AuthoritativeEnemySpawner : IEnemySpawner
    {
        private readonly IEnemySpawner _inner;
        private readonly IAuthorityContext _authority;
        private readonly Dictionary<uint, EnemyController> _actors = new();
        private readonly Dictionary<EnemyController, uint> _ids = new();
        private uint _nextId;

        public AuthoritativeEnemySpawner(IEnemySpawner inner, IAuthorityContext authority)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public IReadOnlyDictionary<uint, EnemyController> Actors => _actors;
        public int RefusedSpawns { get; private set; }
        public event Action<EnemySpawnRecord, EnemyController> Spawned;

        public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
        {
            if (!HostAuthorityContract.CanDecide(_authority, AuthoritativeDomain.EnemySpawning))
            {
                RefusedSpawns++;
                return null;
            }

            var actor = _inner.Spawn(definition, position, target);
            if (actor == null) return null;
            var id = ++_nextId;
            _actors[id] = actor;
            _ids[actor] = id;
            actor.Died += a => { if (_ids.TryGetValue(a, out var dead)) { _actors.Remove(dead); _ids.Remove(a); } };
            Spawned?.Invoke(new EnemySpawnRecord { NetId = id, DefinitionId = new FixedString64Bytes(definition != null ? definition.Id : string.Empty), Position = position }, actor);
            return actor;
        }

        public bool TryGetId(EnemyController actor, out uint id) => _ids.TryGetValue(actor, out id);

        /// <summary>Host snapshot of one actor for replication.</summary>
        public static EnemyNetState Capture(uint netId, EnemyController actor, uint version, double time)
        {
            var health = actor.GetComponent<HealthComponent>();
            return new EnemyNetState
            {
                NetId = netId,
                Position = actor.transform.position,
                Facing = actor.Target != null ? ((Vector2)actor.Target.position - (Vector2)actor.transform.position).normalized : Vector2.right,
                State = (int)actor.State,
                Health = health != null ? health.CurrentHealth : 0,
                MaxHealth = health != null ? health.MaxHealth : 0,
                IsAlive = actor.IsAlive,
                Version = version,
                Time = time
            };
        }
    }

    /// <summary>
    /// Client-side stand-in for an authoritative enemy: presentation state only (position, facing, telegraph/attack
    /// state, replicated health). It has no EnemyController, no attack behaviours and no local AI — nothing here can
    /// spawn, move or kill an enemy on its own.
    /// </summary>
    public sealed class EnemyReplica : MonoBehaviour
    {
        private readonly ReplicaInterpolator _interpolator = new();
        private HealthReplicaApplier _health;

        public uint NetId { get; private set; }
        public string DefinitionId { get; private set; }
        public EnemyState State { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.right;
        public bool IsDead { get; private set; }
        public uint LastVersion { get; private set; }
        public int AppliedStates { get; private set; }
        public HealthComponent Health { get; private set; }

        public event Action<EnemyReplica> Died;

        public void Initialize(in EnemySpawnRecord record)
        {
            NetId = record.NetId;
            DefinitionId = record.DefinitionId.ToString();
            transform.position = record.Position;
            Health = GetComponent<HealthComponent>() != null ? GetComponent<HealthComponent>() : gameObject.AddComponent<HealthComponent>();
            _health = new HealthReplicaApplier(Health);
        }

        public bool Apply(in EnemyNetState state)
        {
            if (state.NetId != NetId) return false;
            if (AppliedStates > 0 && state.Version <= LastVersion) return false;
            LastVersion = state.Version;
            AppliedStates++;
            State = (EnemyState)state.State;
            Facing = state.Facing;
            _interpolator.Push(new PlayerNetState { Position = state.Position, Time = state.Time });
            _health.Apply(new HealthNetState { Current = state.Health, Max = state.MaxHealth, IsAlive = state.IsAlive, Version = state.Version });
            if (!state.IsAlive && !IsDead)
            {
                IsDead = true;
                Died?.Invoke(this);
            }

            return true;
        }

        public Vector2 SampledPosition(double now) => _interpolator.Sample(now).Position;
    }

    /// <summary>Client registry: exactly one replica per network id, created from spawn records, removed once.</summary>
    public sealed class EnemyReplicaRegistry
    {
        private readonly Dictionary<uint, EnemyReplica> _replicas = new();
        private readonly Transform _parent;

        public EnemyReplicaRegistry(Transform parent = null)
        {
            _parent = parent;
        }

        public IReadOnlyDictionary<uint, EnemyReplica> Replicas => _replicas;
        public int DuplicateSpawnsIgnored { get; private set; }

        public EnemyReplica Spawn(in EnemySpawnRecord record)
        {
            if (_replicas.TryGetValue(record.NetId, out var existing))
            {
                DuplicateSpawnsIgnored++;
                return existing;
            }

            var go = new GameObject($"EnemyReplica_{record.NetId}_{record.DefinitionId}");
            if (_parent != null) go.transform.SetParent(_parent, false);
            var replica = go.AddComponent<EnemyReplica>();
            replica.Initialize(record);
            _replicas[record.NetId] = replica;
            return replica;
        }

        public bool Apply(in EnemyNetState state) => _replicas.TryGetValue(state.NetId, out var replica) && replica.Apply(state);

        public bool Despawn(uint netId)
        {
            if (!_replicas.TryGetValue(netId, out var replica)) return false;
            _replicas.Remove(netId);
            if (replica != null) UnityEngine.Object.Destroy(replica.gameObject);
            return true;
        }
    }

    /// <summary>Boss/Elite headline state for clients: phase changes and the defeat happen once, on the host.</summary>
    [Serializable]
    public struct BossNetState : INetworkSerializable
    {
        public int Phase;
        public bool IsStarted;
        public bool IsDefeated;
        public int Health;
        public int MaxHealth;
        public uint Version;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref IsStarted);
            serializer.SerializeValue(ref IsDefeated);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref Version);
        }
    }

    public static class BossStateSync
    {
        public static BossNetState Capture(BossEncounter encounter, uint version)
        {
            var boss = encounter.Boss;
            return new BossNetState
            {
                Phase = boss != null ? boss.Phase : 1,
                IsStarted = encounter.IsStarted,
                IsDefeated = encounter.IsDefeated,
                Health = boss != null && boss.Health != null ? boss.Health.CurrentHealth : 0,
                MaxHealth = boss != null && boss.Health != null ? boss.Health.MaxHealth : 0,
                Version = version
            };
        }
    }

    /// <summary>Client boss replica: raises phase/defeat events exactly once regardless of repeated state messages.</summary>
    public sealed class BossReplica
    {
        private uint _lastVersion;
        private bool _any;

        public int Phase { get; private set; } = 1;
        public bool IsDefeated { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }

        public event Action<int> PhaseChanged;
        public event Action Defeated;

        public bool Apply(in BossNetState state)
        {
            if (_any && state.Version <= _lastVersion) return false;
            _any = true;
            _lastVersion = state.Version;
            Health = state.Health;
            MaxHealth = state.MaxHealth;
            if (state.Phase != Phase)
            {
                Phase = state.Phase;
                PhaseChanged?.Invoke(Phase);
            }

            if (state.IsDefeated && !IsDefeated)
            {
                IsDefeated = true;
                Defeated?.Invoke();
            }

            return true;
        }
    }
}
