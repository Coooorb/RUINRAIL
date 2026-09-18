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
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Armor;
using RuinRail.Gameplay.Items.Passives;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 080: room entry → lock → encounter → clear lifecycle, exactly once, with passive hooks.</summary>
    public class RoomRuntimeTests
    {
        private readonly List<Object> _created = new();
        private List<EnemyDefinition> _archetypes;

        private sealed class TrackingSpawner : IEnemySpawner
        {
            public readonly List<EnemyController> Spawned = new();
            public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
            {
                var actor = new DefaultEnemySpawner().Spawn(definition, position, target);
                Spawned.Add(actor);
                return actor;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RoomRoot CreateRoom(RoomType type, Vector2Int size, IEnumerable<Vector2Int> enemySpawns, Vector2 worldOffset = default)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", $"test_{type}");
            Set(definition, "_roomType", type);
            var go = new GameObject($"Room_{type}");
            _created.Add(go);
            go.transform.position = worldOffset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, size);
            foreach (var cell in enemySpawns)
            {
                var marker = new GameObject("EnemySpawn").AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(RoomMarkerRole.EnemySpawn, cell);
                marker.SnapToGrid();
            }

            var north = new GameObject("Door_N").AddComponent<DoorSocket>();
            north.transform.SetParent(go.transform, false);
            north.Configure(DoorDirection.North, new Vector2Int(size.x / 2 - 1, size.y - 1));
            north.SnapToGrid();
            var south = new GameObject("Door_S").AddComponent<DoorSocket>();
            south.transform.SetParent(go.transform, false);
            south.Configure(DoorDirection.South, new Vector2Int(size.x / 2 - 1, 0));
            south.SnapToGrid();
            return root;
        }

        private (GameObject player, PlayerCombatEvents events, PlayerRoomEventsRelay relay) CreatePlayer(Vector2 position)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = position;
            player.AddComponent<CircleCollider2D>().isTrigger = true;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<TestDamageableTarget>();
            var events = new PlayerCombatEvents();
            var relay = player.AddComponent<PlayerRoomEventsRelay>();
            relay.SetEvents(events);
            return (player, events, relay);
        }

        private EncounterPlan SmallPlan(int depth = 1)
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(7, depth, 1, Biome.RuinedMetro, 3);
            return new EncounterPlan(context, 2f, 3f, 2f, new[] { new EncounterEntry(grunt, 2) });
        }

        private static void Kill(EnemyController actor) => actor.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));

        // ---- Acceptance 1 + 2 + 3: lifecycle exactly once, doors, all deaths required, no respawn on re-entry ----

        [UnityTest]
        public IEnumerator CombatRoom_LocksOnEntry_SpawnsAwayFromEntry_ClearsOnceWhenAllEnemiesDie_AndNeverRespawns()
        {
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(2, 2), new Vector2Int(13, 9), new Vector2Int(13, 2) });
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 3, depth: 1, partySize: 1);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(), spawner);
            var (player, events, relay) = CreatePlayer(new Vector2(2.5f, 2.5f));
            var entered = 0;
            var cleared = new List<RoomClearedContext>();
            events.CombatRoomEntered += () => entered++;
            runtime.Cleared += (_, c) => cleared.Add(c);
            var activated = 0;
            runtime.Activated += _ => activated++;

            Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle);
            Assert.IsFalse(runtime.DoorsLocked);
            Assert.AreEqual(2, runtime.Doors.Count);

            Assert.IsTrue(runtime.NotifyPlayerEntered(player));
            Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle);
            Assert.IsTrue(runtime.DoorsLocked, "Doors lock while combat is active.");
            Assert.IsTrue(runtime.Doors.All(d => d.GetComponentInChildren<EnvironmentObstacle>() != null && d.GetComponentInChildren<BoxCollider2D>().enabled));
            Assert.AreEqual(1, activated);
            Assert.AreEqual(1, entered, "CombatRoomEntered raised once for the occupant.");
            Assert.AreEqual(1, runtime.State.EntryCount);
            yield return null;

            Assert.AreEqual(2, spawner.Spawned.Count);
            foreach (var enemy in spawner.Spawned)
            {
                Assert.GreaterOrEqual(Vector2.Distance(enemy.transform.position, player.transform.position), RoomRuntime.MinSpawnDistanceFromEntryTiles, "Spawned only at markers ≥5 tiles from the entry.");
            }

            Assert.IsFalse(runtime.NotifyPlayerEntered(player), "Duplicate entry while active does nothing.");
            Assert.AreEqual(2, spawner.Spawned.Count);

            Kill(spawner.Spawned[0]);
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle, "One enemy alive: not cleared.");
            Assert.IsTrue(runtime.DoorsLocked);
            Assert.AreEqual(0, cleared.Count);
            Assert.AreEqual(1, runtime.State.EnemiesDefeated);

            Kill(spawner.Spawned[1]);
            yield return null;
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
            Assert.IsFalse(runtime.DoorsLocked, "Doors unlock on clear.");
            Assert.AreEqual(1, cleared.Count, "Clear event exactly once.");
            Assert.AreEqual(3, cleared[0].NodeId);
            Assert.AreEqual("test_Combat", cleared[0].RoomId);
            Assert.AreEqual(RoomType.Combat, cleared[0].RoomType);
            Assert.AreEqual(2, cleared[0].EnemiesDefeated);
            Assert.AreEqual(1, relay.RoomsCleared);
            Assert.AreEqual(2, runtime.State.EnemiesSpawned);

            // Re-entry after clear: no respawn, no second event.
            runtime.NotifyPlayerLeft(player);
            Assert.IsFalse(runtime.NotifyPlayerEntered(player));
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(2, spawner.Spawned.Count);
            Assert.AreEqual(1, cleared.Count);
            Assert.AreEqual(1, entered);
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
        }

        // ---- Acceptance 4: room-clear event drives armor passives ----

        [UnityTest]
        public IEnumerator RoomClear_TriggersPatchworkHeal_AndSecondWindResetsPerRoom()
        {
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(13, 9) });
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 1, 1, 1);
            var spawner = new TrackingSpawner();
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            runtime.SetEncounter(new EncounterPlan(new EncounterContext(1, 1, 1, Biome.RuinedMetro, 1), 1f, 1f, 1f, new[] { new EncounterEntry(grunt, 1) }), spawner);
            var (player, events, _) = CreatePlayer(new Vector2(1f, 1f));

            var stats = new PlayerStats(ScriptableObject.CreateInstance<GlobalStatCapsConfig>());
            var health = 40;
            var dashResets = 0;
            var context = new PassiveContext(stats, events, () => health, () => 100, amount => { health = Mathf.Min(100, health + amount); return true; }, () => dashResets++);
            var patchwork = new PatchworkPassive();
            patchwork.Attach(context);
            var secondWind = new SecondWindPassive();
            secondWind.Attach(context);

            runtime.NotifyPlayerEntered(player);
            yield return null;
            events.RaiseHealthChanged(20, 100);
            Assert.AreEqual(1, dashResets, "Second Wind fires once inside the room.");
            events.RaiseHealthChanged(10, 100);
            Assert.AreEqual(1, dashResets, "Once per Combat Room.");

            Kill(spawner.Spawned[0]);
            yield return null;
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
            Assert.AreEqual(46, health, "Patchwork: +6% of Max HP on clear.");

            // Next combat room: the once-per-room reset applies again.
            var root2 = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(13, 9) }, new Vector2(40f, 0f));
            var runtime2 = root2.gameObject.AddComponent<RoomRuntime>();
            runtime2.Configure(root2, 2, 1, 1);
            runtime2.SetEncounter(new EncounterPlan(new EncounterContext(1, 1, 1, Biome.RuinedMetro, 2), 1f, 1f, 1f, new[] { new EncounterEntry(grunt, 1) }), spawner);
            player.transform.position = new Vector2(41f, 1f);
            events.RaiseHealthChanged(60, 100);
            runtime2.NotifyPlayerEntered(player);
            yield return null;
            events.RaiseHealthChanged(10, 100);
            Assert.AreEqual(2, dashResets);
            patchwork.Detach();
            secondWind.Detach();
        }

        // ---- Requirement 4: Start rooms never spawn combat ----

        [UnityTest]
        public IEnumerator StartRoom_WithSpawnMarkers_NeverSpawnsCombat_AndIsClearedOnEntry()
        {
            var root = CreateRoom(RoomType.Start, new Vector2Int(16, 12), new[] { new Vector2Int(13, 9), new Vector2Int(2, 9) });
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 0, 1, 1);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(), spawner);
            var (player, events, _) = CreatePlayer(new Vector2(1f, 1f));
            var entered = 0;
            events.CombatRoomEntered += () => entered++;
            var cleared = 0;
            runtime.Cleared += (_, _) => cleared++;

            Assert.IsTrue(runtime.NotifyPlayerEntered(player));
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
            Assert.AreEqual(0, spawner.Spawned.Count, "No immediate enemies in the Start room.");
            Assert.IsFalse(runtime.DoorsLocked);
            Assert.AreEqual(0, entered);
            Assert.AreEqual(0, cleared, "No combat clear event for a non-combat room.");
        }

        // ---- Requirement 3 fallback + depth scaling + serializable state ----

        [UnityTest]
        public IEnumerator SmallRoom_WithoutFarMarkers_FallsBackToFarthest_AndScalesSpawnsForDepth()
        {
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(3, 3), new Vector2Int(5, 5) });
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 4, depth: 10, partySize: 2);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(10), spawner);
            var (player, _, _) = CreatePlayer(new Vector2(3.5f, 3.5f));

            var points = runtime.SpawnPointsFor(player.transform.position);
            Assert.AreEqual(2, points.Count, "Fallback keeps every marker, farthest first.");
            Assert.AreEqual(new Vector2(5.5f, 5.5f), points[0]);

            runtime.NotifyPlayerEntered(player);
            yield return null;
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var expectedHealth = DepthScaling.ScaledHealth(grunt.BaseHealth, 10, 2);
            Assert.Greater(expectedHealth, grunt.BaseHealth);
            foreach (var enemy in spawner.Spawned)
            {
                Assert.AreEqual(expectedHealth, enemy.GetComponent<HealthComponent>().MaxHealth, "Depth + party health scaling applied on spawn.");
                Assert.AreEqual(DepthScaling.MovementSpeedMultiplier(10), enemy.MovementSpeedMultiplier, 0.0001f);
            }

            var json = JsonUtility.ToJson(runtime.State);
            var restored = JsonUtility.FromJson<RoomRuntimeState>(json);
            Assert.AreEqual(4, restored.NodeId);
            Assert.AreEqual(RoomLifecycleState.Active, restored.State);
            Assert.AreEqual(runtime.Plan.Signature, restored.EncounterSignature);
            Assert.AreEqual(2, restored.EnemiesSpawned);
        }

        [UnityTest]
        public IEnumerator RestoredClearedState_DoesNotRunTheEncounterAgain()
        {
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(13, 9) });
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 5, 1, 1);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(), spawner);
            runtime.RestoreState(new RoomRuntimeState { State = RoomLifecycleState.Cleared, EntryCount = 1, EnemiesSpawned = 2, EnemiesDefeated = 2 });
            var (player, _, _) = CreatePlayer(new Vector2(1f, 1f));

            Assert.IsFalse(runtime.NotifyPlayerEntered(player));
            yield return null;
            Assert.AreEqual(0, spawner.Spawned.Count);
            Assert.IsFalse(runtime.DoorsLocked);
            Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle);
        }

        [UnityTest]
        public IEnumerator EntryTrigger_DetectsAPlayerMovementBody()
        {
            var root = CreateRoom(RoomType.Combat, new Vector2Int(16, 12), new[] { new Vector2Int(13, 9) }, new Vector2(100f, 100f));
            var runtime = root.gameObject.AddComponent<RoomRuntime>();
            runtime.Configure(root, 6, 1, 1);
            var spawner = new TrackingSpawner();
            runtime.SetEncounter(SmallPlan(), spawner);
            RoomEntryTrigger.Attach(runtime);

            var player = new GameObject("MovingPlayer");
            _created.Add(player);
            player.transform.position = new Vector2(80f, 80f);
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            player.AddComponent<CircleCollider2D>().radius = 0.3f;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<PlayerMovement>();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle);

            // Two tiles in is the doorway/wall ring; the activation volume starts past it.
            body.position = new Vector2(101.5f, 101.5f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(RoomLifecycleState.Unentered, runtime.Lifecycle, "Standing in the door ring does not activate the room.");

            body.position = new Vector2(104f, 104f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(RoomLifecycleState.Active, runtime.Lifecycle, "Walking into the room interior activates it.");
            Assert.IsTrue(runtime.Occupants.Contains(player));
        }
    }
}
