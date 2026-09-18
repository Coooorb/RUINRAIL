using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 098: host-only spawning/AI/resolution, one replica per spawn, once-only boss events, clients cannot mutate, room clear waits on the host.</summary>
    public class NetworkEnemyAuthorityTests
    {
        private readonly List<Object> _created = new();
        private List<EnemyDefinition> _archetypes;

        private sealed class Authority : IAuthorityContext
        {
            public Authority(NetworkRole role) { Role = role; }
            public NetworkRole Role { get; }
            public bool IsAuthority => Role != NetworkRole.Client;
        }

        [SetUp]
        public void SetUp()
        {
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" }).Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var replica in Object.FindObjectsByType<EnemyReplica>(FindObjectsSortMode.None)) if (replica != null) Object.DestroyImmediate(replica.gameObject);
            foreach (var boss in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None)) if (boss != null) Object.DestroyImmediate(boss.transform.parent != null ? boss.transform.parent.gameObject : boss.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private GameObject Target()
        {
            var go = new GameObject("Target");
            _created.Add(go);
            go.transform.position = new Vector3(50f, 50f, 0f);
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            go.AddComponent<TestDamageableTarget>();
            return go;
        }

        // ---- Acceptance 1 + 3: host spawns once per actor, clients never spawn ----

        [UnityTest]
        public IEnumerator HostSpawnsOneAuthoritativeActorPerSpawn_ClientsSpawnNothing()
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var target = Target();
            var plan = new EncounterPlan(new EncounterContext(1, 1, 1, Biome.RuinedMetro, 0), 3f, 3f, 3f, new[] { new EncounterEntry(grunt, 3) });

            var host = new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new Authority(NetworkRole.Host));
            var records = new List<EnemySpawnRecord>();
            host.Spawned += (r, _) => records.Add(r);
            var hostRuntime = new EncounterRuntime(plan, host, new List<Vector2> { new(50f, 55f), new(55f, 50f) }, target.transform);
            hostRuntime.Start();
            yield return null;
            Assert.AreEqual(3, host.Actors.Count);
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, records.Select(r => r.NetId));
            Assert.IsTrue(records.All(r => r.DefinitionId.ToString() == "grunt"));

            var client = new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new Authority(NetworkRole.Client));
            var clientRuntime = new EncounterRuntime(plan, client, new List<Vector2> { new(50f, 55f) }, target.transform);
            clientRuntime.Start();
            yield return null;
            Assert.AreEqual(0, client.Actors.Count, "A client never rolls or spawns combat actors.");
            Assert.AreEqual(3, client.RefusedSpawns);
            Assert.AreEqual(3, Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length, "Only the host's actors exist.");
        }

        [Test]
        public void ReplicaRegistry_BuildsExactlyOneReplicaPerNetId_AndDespawnsOnce()
        {
            var registry = new EnemyReplicaRegistry();
            var record = new EnemySpawnRecord { NetId = 7, DefinitionId = new Unity.Collections.FixedString64Bytes("shooter"), Position = new Vector2(3f, 4f) };
            var a = registry.Spawn(record);
            var b = registry.Spawn(record);
            Assert.AreSame(a, b, "Repeated spawn message: same replica.");
            Assert.AreEqual(1, registry.Replicas.Count);
            Assert.AreEqual(1, registry.DuplicateSpawnsIgnored);
            Assert.IsNull(a.GetComponent<EnemyController>(), "Replicas carry no AI/attack logic.");
            Assert.AreEqual("shooter", a.DefinitionId);
            Assert.AreEqual(new Vector2(3f, 4f), (Vector2)a.transform.position);

            Assert.IsTrue(registry.Despawn(7));
            Assert.IsFalse(registry.Despawn(7), "Despawn is once.");
            Assert.AreEqual(0, registry.Replicas.Count);
        }

        [Test]
        public void ClientCannotKillAReplica_ByLocalMutation_OnlyReplicatedStateDoes()
        {
            var registry = new EnemyReplicaRegistry();
            var replica = registry.Spawn(new EnemySpawnRecord { NetId = 1, DefinitionId = new Unity.Collections.FixedString64Bytes("grunt"), Position = Vector2.zero });
            replica.Apply(new EnemyNetState { NetId = 1, Health = 30, MaxHealth = 30, IsAlive = true, State = (int)EnemyState.Chase, Version = 1 });
            DamageAuthority.LocalIsAuthoritative = false;
            var died = 0;
            replica.Died += _ => died++;

            Assert.IsFalse(replica.Health.TryApplyDamage(new DamageRequest(9999)), "Local damage on a client is refused.");
            Assert.AreEqual(30, replica.Health.CurrentHealth);
            Assert.IsFalse(replica.IsDead);

            Assert.IsTrue(replica.Apply(new EnemyNetState { NetId = 1, Health = 0, MaxHealth = 30, IsAlive = false, State = (int)EnemyState.Dead, Version = 2 }));
            Assert.IsTrue(replica.IsDead);
            Assert.AreEqual(EnemyState.Dead, replica.State);
            Assert.IsFalse(replica.Apply(new EnemyNetState { NetId = 1, Health = 0, MaxHealth = 30, IsAlive = false, State = (int)EnemyState.Dead, Version = 2 }), "Duplicate state ignored.");
            Assert.IsFalse(replica.Apply(new EnemyNetState { NetId = 1, Health = 30, MaxHealth = 30, IsAlive = true, Version = 1 }), "Late state ignored.");
            Assert.AreEqual(1, died);
            Assert.IsFalse(replica.Apply(new EnemyNetState { NetId = 2, Version = 9 }), "Other ids are not this replica.");
        }

        [UnityTest]
        public IEnumerator HostState_CapturesReadablePresentationFields_AndReplicaInterpolates()
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var target = Target();
            var host = new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new Authority(NetworkRole.Host));
            var actor = host.Spawn(grunt, new Vector2(40f, 50f), target.transform);
            yield return null;
            Assert.IsTrue(host.TryGetId(actor, out var id));
            var state = AuthoritativeEnemySpawner.Capture(id, actor, 1, 0.0);
            Assert.AreEqual(id, state.NetId);
            Assert.AreEqual(actor.GetComponent<HealthComponent>().MaxHealth, state.MaxHealth);
            Assert.IsTrue(state.IsAlive);
            Assert.Greater(state.Facing.x, 0.9f, "Facing points at the target.");

            var registry = new EnemyReplicaRegistry();
            var replica = registry.Spawn(new EnemySpawnRecord { NetId = id, DefinitionId = new Unity.Collections.FixedString64Bytes("grunt"), Position = new Vector2(40f, 50f) });
            registry.Apply(new EnemyNetState { NetId = id, Position = new Vector2(40f, 50f), Version = 1, Time = 0.0, IsAlive = true, MaxHealth = 30, Health = 30 });
            registry.Apply(new EnemyNetState { NetId = id, Position = new Vector2(42f, 50f), Version = 2, Time = 0.1, IsAlive = true, MaxHealth = 30, Health = 30, State = (int)EnemyState.Telegraph });
            Assert.AreEqual(41f, replica.SampledPosition(0.15).x, 0.001f, "Buffered interpolation between authoritative samples.");
            Assert.AreEqual(EnemyState.Telegraph, replica.State, "Telegraph state is readable on the client.");
        }

        // ---- Acceptance 2: boss phase/death once ----

        [UnityTest]
        public IEnumerator BossPhaseAndDefeat_HappenOnceOnHost_AndReplicateOnceToClients()
        {
            var tunnelMaw = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset");
            var encounter = new DefaultBossSpawner(new[] { tunnelMaw }).Spawn(tunnelMaw, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var target = Target();
            encounter.Boss.SetTarget(target.transform);
            yield return null;
            var hostPhases = new List<int>();
            var hostDefeats = 0;
            encounter.PhaseChanged += (_, p) => hostPhases.Add(p);
            encounter.BossDefeated += (_, _) => hostDefeats++;

            var client = new BossReplica();
            var clientPhases = new List<int>();
            var clientDefeats = 0;
            client.PhaseChanged += clientPhases.Add;
            client.Defeated += () => clientDefeats++;
            uint version = 0;
            void Replicate() { var s = BossStateSync.Capture(encounter, ++version); client.Apply(s); client.Apply(s); }

            Replicate();
            encounter.Boss.Health.TryApplyDamage(new DamageRequest(575));
            Replicate();
            Replicate();
            encounter.Boss.Health.TryApplyDamage(new DamageRequest(200));
            Replicate();
            encounter.Boss.Health.TryApplyDamage(new DamageRequest(99999));
            Replicate();
            Replicate();

            CollectionAssert.AreEqual(new[] { 2 }, hostPhases);
            Assert.AreEqual(1, hostDefeats);
            CollectionAssert.AreEqual(new[] { 2 }, clientPhases, "Phase 2 raised once on the client despite repeated states.");
            Assert.AreEqual(1, clientDefeats);
            Assert.AreEqual(2, client.Phase);
            Assert.IsTrue(client.IsDefeated);
            Assert.AreEqual(0, client.Health);
        }

        // ---- Acceptance 4: room clear waits on the host ----

        [UnityTest]
        public IEnumerator ClientRoom_NeverClearsOnItsOwn_FollowsReplicatedHostState()
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var target = Target();
            RoomRuntime Room(string name, Vector2 offset)
            {
                var definition = ScriptableObject.CreateInstance<RoomDefinition>();
                _created.Add(definition);
                Set(definition, "_id", "net_room");
                Set(definition, "_roomType", RoomType.Combat);
                var go = new GameObject(name);
                _created.Add(go);
                go.transform.position = offset;
                go.AddComponent<UnityEngine.Grid>();
                var root = go.AddComponent<RoomRoot>();
                root.Configure(definition, new Vector2Int(16, 12));
                var marker = new GameObject("EnemySpawn").AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(RoomMarkerRole.EnemySpawn, new Vector2Int(13, 9));
                marker.SnapToGrid();
                var socket = new GameObject("Door_N").AddComponent<DoorSocket>();
                socket.transform.SetParent(go.transform, false);
                socket.Configure(DoorDirection.North, new Vector2Int(7, 11));
                socket.SnapToGrid();
                var runtime = go.AddComponent<RoomRuntime>();
                runtime.Configure(root, 3, 1, 1);
                return runtime;
            }

            var plan = new EncounterPlan(new EncounterContext(1, 1, 1, Biome.RuinedMetro, 3), 1f, 1f, 1f, new[] { new EncounterEntry(grunt, 1) });
            var hostRoom = Room("HostRoom", Vector2.zero);
            hostRoom.SetEncounter(plan, new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new Authority(NetworkRole.Host)));
            var clientRoom = Room("ClientRoom", new Vector2(60f, 0f));
            clientRoom.SetAuthoritative(false);
            clientRoom.SetEncounter(plan, new AuthoritativeEnemySpawner(new DefaultEnemySpawner(), new Authority(NetworkRole.Client)));
            var clientCleared = 0;
            clientRoom.Cleared += (_, _) => clientCleared++;

            target.transform.position = new Vector3(1f, 1f, 0f);
            Assert.IsTrue(hostRoom.NotifyPlayerEntered(target));
            Assert.IsFalse(clientRoom.NotifyPlayerEntered(target), "Client entry never advances the room locally.");
            Assert.AreEqual(RoomLifecycleState.Unentered, clientRoom.Lifecycle);
            clientRoom.RestoreState(hostRoom.State.Clone());
            Assert.AreEqual(RoomLifecycleState.Active, clientRoom.Lifecycle);
            Assert.IsTrue(clientRoom.DoorsLocked, "Client doors follow the host.");
            yield return null;
            Assert.AreEqual(1, Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length, "Only the host spawned the encounter.");

            clientRoom.RestoreState(hostRoom.State.Clone());
            Assert.AreEqual(0, clientCleared, "Still active on the host: the client cannot clear.");

            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
            yield return null;
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, hostRoom.Lifecycle);
            clientRoom.RestoreState(hostRoom.State.Clone());
            Assert.AreEqual(RoomLifecycleState.Cleared, clientRoom.Lifecycle);
            Assert.IsFalse(clientRoom.DoorsLocked);
            Assert.AreEqual(1, clientCleared, "Replicated clear raises the client's room-clear once.");
            clientRoom.RestoreState(hostRoom.State.Clone());
            Assert.AreEqual(1, clientCleared);
        }

        [Test]
        public void ActiveCaps_AreUnchangedByNetworking()
        {
            Assert.AreEqual(10, PartyScaling.ActiveNormalCap(1));
            Assert.AreEqual(14, PartyScaling.ActiveNormalCap(2));
            Assert.AreEqual(18, PartyScaling.ActiveNormalCap(3));
            Assert.AreEqual(1, PartyScaling.ActiveEliteCap);
        }
    }
}
