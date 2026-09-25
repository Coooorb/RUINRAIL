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
        /// <summary>Elite/Boss only: 1-based slot of the attack being telegraphed/performed in the actor's moveset (0 = none).</summary>
        public int AttackSlot;
        /// <summary>Elite/Boss only: the actor's state is a <see cref="MovesetActorState"/> rather than an <see cref="EnemyState"/>.</summary>
        public bool IsMoveset;

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
            serializer.SerializeValue(ref AttackSlot);
            serializer.SerializeValue(ref IsMoveset);
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

        /// <summary>
        /// Host snapshot of one Elite/Boss for replication: position, the direction the actor faces (the locked attack
        /// direction while it telegraphs or strikes, its target otherwise), its moveset state and which attack it is on,
        /// so a client draws the same telegraph shape the host's players are dodging.
        /// </summary>
        public static EnemyNetState Capture(uint netId, MovesetActorController actor, IReadOnlyList<EnemyAttackDefinition> moveset, uint version, double time)
        {
            var health = actor.Health;
            var attacking = actor.State == MovesetActorState.Telegraph || actor.State == MovesetActorState.Attacking;
            var facing = attacking && actor.LockedDirection.sqrMagnitude > 0.0001f ? actor.LockedDirection
                : actor.Target != null ? ((Vector2)actor.Target.position - (Vector2)actor.transform.position).normalized : Vector2.right;
            var slot = 0;
            if (actor.CurrentAttack != null && moveset != null)
            {
                for (var i = 0; i < moveset.Count; i++)
                {
                    if (moveset[i] == actor.CurrentAttack) { slot = i + 1; break; }
                }
            }

            return new EnemyNetState
            {
                NetId = netId,
                Position = actor.transform.position,
                Facing = facing,
                State = (int)actor.State,
                Health = health != null ? health.CurrentHealth : 0,
                MaxHealth = health != null ? health.MaxHealth : 0,
                IsAlive = actor.IsAlive,
                Version = version,
                Time = time,
                AttackSlot = slot,
                IsMoveset = true
            };
        }

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
    /// state, replicated health). It has no EnemyController, no attack behaviours, no physics body and no local AI —
    /// nothing here can spawn, move or kill an enemy on its own. It does carry the enemy team tag and a hurtbox, so the
    /// local player's shots land on it; the hit becomes a request to the host (DamageAuthority relay), never damage.
    /// </summary>
    public sealed class EnemyReplica : MonoBehaviour, IReplicatedActorView
    {
        private readonly ReplicaInterpolator _interpolator = new();
        private HealthReplicaApplier _health;
        private IReadOnlyList<EnemyAttackDefinition> _moveset = Array.Empty<EnemyAttackDefinition>();
        private Vector2 _lastSampled;
        private bool _hasSample;

        public uint NetId { get; private set; }
        public string DefinitionId { get; private set; }
        public EnemyState State { get; private set; }
        public Vector2 Facing { get; private set; } = Vector2.right;
        public bool IsDead { get; private set; }
        public uint LastVersion { get; private set; }
        public int AppliedStates { get; private set; }
        public HealthComponent Health { get; private set; }
        public CoopActorKind Kind { get; private set; }
        public int RoomNode { get; private set; } = -1;
        public int AttackSlot { get; private set; }
        public int Strikes { get; private set; }

        public bool IsMoveset { get; private set; }
        public bool IsEliteOrBoss => Kind != CoopActorKind.Normal;
        public EnemyState EnemyState => State;
        public MovesetActorState MovesetState { get; private set; }
        public Vector2 Velocity { get; private set; }
        public EnemyDefinition EnemyDefinition { get; private set; }
        public EnemyAttackDefinition CurrentAttack => AttackSlot > 0 && AttackSlot <= _moveset.Count ? _moveset[AttackSlot - 1] : null;

        public event Action<EnemyReplica> Died;
        public event Action<IReplicatedActorView> Struck;

        public void Initialize(in EnemySpawnRecord record)
        {
            NetId = record.NetId;
            DefinitionId = record.DefinitionId.ToString();
            transform.position = record.Position;
            Health = GetComponent<HealthComponent>() != null ? GetComponent<HealthComponent>() : gameObject.AddComponent<HealthComponent>();
            _health = new HealthReplicaApplier(Health);
        }

        /// <summary>
        /// Makes the replica hittable by the local player (enemy team, enemy layer, the actor-class hurtbox) and ties
        /// it to its definitions for presentation. Still no body: it cannot collide, move or act.
        /// </summary>
        public void ConfigureCombatPresence(CoopActorKind kind, int roomNode, EnemyDefinition enemy, IReadOnlyList<EnemyAttackDefinition> moveset)
        {
            Kind = kind;
            RoomNode = roomNode;
            EnemyDefinition = enemy;
            _moveset = moveset ?? Array.Empty<EnemyAttackDefinition>();
            IsMoveset = kind != CoopActorKind.Normal;
            CombatLayers.TagEnemyBody(gameObject);
            if (GetComponent<TeamMember>() == null) gameObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            var (size, offset) = kind switch
            {
                CoopActorKind.Boss => (CombatHurtbox.BossSize, CombatHurtbox.BossOffset),
                CoopActorKind.Elite => (CombatHurtbox.EliteSize, CombatHurtbox.EliteOffset),
                _ => (CombatHurtbox.NormalSize, CombatHurtbox.NormalOffset)
            };
            CombatHurtbox.Attach(gameObject, size, offset);
        }

        /// <summary>The spawn-time health, before the first motion state arrives.</summary>
        public void ApplyInitialHealth(int current, int max)
        {
            if (max > 0) Health.ApplyReplicatedHealth(current, max);
        }

        public bool Apply(in EnemyNetState state)
        {
            if (state.NetId != NetId) return false;
            if (AppliedStates > 0 && state.Version <= LastVersion) return false;
            LastVersion = state.Version;
            AppliedStates++;
            var wasTelegraph = IsMoveset ? MovesetState == MovesetActorState.Telegraph : State == EnemyState.Telegraph;
            if (state.IsMoveset)
            {
                IsMoveset = true;
                MovesetState = (MovesetActorState)state.State;
                State = MovesetState == MovesetActorState.Dead ? EnemyState.Dead : MovesetState == MovesetActorState.Telegraph ? EnemyState.Telegraph : MovesetState == MovesetActorState.Recovery ? EnemyState.Recovery : EnemyState.Chase;
            }
            else
            {
                State = (EnemyState)state.State;
            }

            AttackSlot = state.AttackSlot;
            if (state.Facing.sqrMagnitude > 0.0001f) Facing = state.Facing;
            var nowTelegraph = IsMoveset ? MovesetState == MovesetActorState.Telegraph : State == EnemyState.Telegraph;
            if (wasTelegraph && !nowTelegraph && state.IsAlive)
            {
                Strikes++;
                Struck?.Invoke(this);
            }

            _interpolator.Push(new PlayerNetState { Position = state.Position, Time = state.Time });
            _health.Apply(new HealthNetState { Current = state.Health, Max = state.MaxHealth, IsAlive = state.IsAlive, Version = state.Version });
            if (!state.IsAlive) MarkDead();
            return true;
        }

        /// <summary>The host retired this actor as dead (reliable record): death plays once even if the last state was lost.</summary>
        public void MarkDead()
        {
            if (IsDead) return;
            IsDead = true;
            State = EnemyState.Dead;
            MovesetState = MovesetActorState.Dead;
            if (Health != null && Health.CurrentHealth > 0) Health.ApplyReplicatedHealth(0, Mathf.Max(1, Health.MaxHealth));
            Died?.Invoke(this);
        }

        public Vector2 SampledPosition(double now) => _interpolator.Sample(now).Position;

        /// <summary>Moves the replica to its interpolated host position and derives the presentation velocity.</summary>
        public void Step(double now, float deltaTime)
        {
            if (_interpolator.BufferedSamples == 0) return;
            var position = SampledPosition(now);
            if (_hasSample && deltaTime > 0f) Velocity = (position - _lastSampled) / deltaTime;
            _lastSampled = position;
            _hasSample = true;
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }
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
        public int Spawns { get; private set; }
        public int Despawns { get; private set; }

        public bool TryGet(uint netId, out EnemyReplica replica) => _replicas.TryGetValue(netId, out replica);

        /// <summary>Retires every replica (depth change / teardown).</summary>
        public void Clear()
        {
            foreach (var id in new List<uint>(_replicas.Keys)) Despawn(id);
        }

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
            Spawns++;
            return replica;
        }

        public bool Apply(in EnemyNetState state) => _replicas.TryGetValue(state.NetId, out var replica) && replica.Apply(state);

        public bool Despawn(uint netId)
        {
            if (!_replicas.TryGetValue(netId, out var replica)) return false;
            _replicas.Remove(netId);
            Despawns++;
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
