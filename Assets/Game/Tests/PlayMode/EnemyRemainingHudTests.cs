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
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The enemy-remaining readout (91): its number is the room encounter's own membership (living + queued
    /// reinforcements, summons once they exist), it exists only while a standard combat encounter is active in the
    /// player's room, never in a Boss arena or a non-combat room, and a client shows the host's replicated number.
    /// </summary>
    public class EnemyRemainingHudTests
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
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var elite in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (elite != null) Object.DestroyImmediate(elite.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RoomRuntime CreateRoom(RoomType type, int nodeId, Vector2 worldOffset = default, bool isElite = false)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", $"test_{type}_{nodeId}");
            Set(definition, "_roomType", type);
            var go = new GameObject($"Room_{type}_{nodeId}");
            _created.Add(go);
            go.transform.position = worldOffset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            foreach (var cell in new[] { new Vector2Int(2, 2), new Vector2Int(13, 9), new Vector2Int(13, 2), new Vector2Int(8, 9) })
            {
                var marker = new GameObject("EnemySpawn").AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(RoomMarkerRole.EnemySpawn, cell);
                marker.SnapToGrid();
            }

            var north = new GameObject("Door_N").AddComponent<DoorSocket>();
            north.transform.SetParent(go.transform, false);
            north.Configure(DoorDirection.North, new Vector2Int(7, 11));
            north.SnapToGrid();
            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, depth: 1, partySize: 1, isElite: isElite);
            return runtime;
        }

        private GameObject CreatePlayer(Vector2 position)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = position;
            player.AddComponent<CircleCollider2D>().isTrigger = true;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<TestDamageableTarget>();
            return player;
        }

        private EncounterPlan Plan(int grunts, int nodeId = 3)
        {
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(7, 1, 1, Biome.RuinedMetro, nodeId, null, 4);
            return new EncounterPlan(context, 2f, 3f, 2f, new[] { new EncounterEntry(grunt, grunts) });
        }

        private static void Kill(EnemyController actor) => actor.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));

        [UnityTest]
        public IEnumerator CombatRoom_ShowsThePlannedCount_DecrementsPerDeath_AndHidesOnClear()
        {
            var room = CreateRoom(RoomType.Combat, 3);
            var spawner = new TrackingSpawner();
            room.SetEncounter(Plan(3), spawner);
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            Assert.IsFalse(room.ShowsEnemyCount, "before the encounter activates nothing is counted");
            Assert.AreEqual(0, room.EnemiesRemaining);

            Assert.IsTrue(room.NotifyPlayerEntered(player));
            yield return null;
            Assert.IsTrue(room.ShowsEnemyCount);
            Assert.AreEqual(3, room.EnemiesRemaining, "starts with every planned enemy (living + queued)");
            Assert.AreEqual(3, room.State.EnemiesRemaining, "the replicated state carries the same number");

            Kill(spawner.Spawned[0]);
            yield return null;
            Assert.AreEqual(2, room.EnemiesRemaining, "one death → one less, at once");
            Kill(spawner.Spawned[1]);
            yield return null;
            Assert.AreEqual(1, room.EnemiesRemaining);
            Kill(spawner.Spawned[2]);
            yield return null;
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.AreEqual(0, room.EnemiesRemaining);
            Assert.IsFalse(room.ShowsEnemyCount, "a cleared room shows no count");
        }

        [UnityTest]
        public IEnumerator Reinforcements_AreCounted_WhileStillQueued_AndDeadActorsNever()
        {
            var room = CreateRoom(RoomType.Combat, 3);
            var spawner = new TrackingSpawner();
            // 12 grunts over a 10-cap: two queue as reinforcements and are still "remaining".
            room.SetEncounter(Plan(12), spawner);
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            room.NotifyPlayerEntered(player);
            yield return null;
            Assert.AreEqual(10, room.Encounter.LivingCount);
            Assert.AreEqual(2, room.Encounter.PendingCount);
            Assert.AreEqual(12, room.EnemiesRemaining, "living + pending, not just what is on the floor");
            Kill(spawner.Spawned[0]);
            yield return null;
            Assert.AreEqual(11, room.EnemiesRemaining, "a death that pulls in a reinforcement still lowers the total by one");
            Assert.AreEqual(11, spawner.Spawned.Count);
            var dead = spawner.Spawned[0];
            Assert.IsFalse(dead == null && room.Encounter.Living.Contains(dead), "a dead actor is not a member");
        }

        [UnityTest]
        public IEnumerator EnemiesOfAnAdjacentRoom_AreNotCounted()
        {
            var here = CreateRoom(RoomType.Combat, 3);
            var there = CreateRoom(RoomType.Combat, 4, new Vector2(40f, 0f));
            var hereSpawner = new TrackingSpawner();
            var thereSpawner = new TrackingSpawner();
            here.SetEncounter(Plan(2, 3), hereSpawner);
            there.SetEncounter(Plan(5, 4), thereSpawner);
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            var other = CreatePlayer(new Vector2(42.5f, 2.5f));
            here.NotifyPlayerEntered(player);
            there.NotifyPlayerEntered(other);
            yield return null;
            Assert.AreEqual(2, here.EnemiesRemaining, "only this room's encounter");
            Assert.AreEqual(5, there.EnemiesRemaining);
            Assert.AreEqual(7, Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length, "seven enemies exist in the scene; the count never sweeps the scene");
            Kill(thereSpawner.Spawned[0]);
            yield return null;
            Assert.AreEqual(2, here.EnemiesRemaining, "a death next door changes nothing here");
            Assert.AreEqual(4, there.EnemiesRemaining);
        }

        [UnityTest]
        public IEnumerator EliteRoom_CountsTheElite_UntilItDies()
        {
            var elite = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_Railguard.asset");
            var room = CreateRoom(RoomType.Combat, 5, isElite: true);
            room.SetEngagement(new EliteEngagement(elite, new DefaultEliteSpawner(), depth: 1, partySize: 1));
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            room.NotifyPlayerEntered(player);
            yield return null;
            Assert.IsTrue(room.ShowsEnemyCount);
            Assert.AreEqual(1, room.EnemiesRemaining, "the Elite counts as one remaining enemy");
            ((EliteEngagement)room.Engagement).Encounter.Elite.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            yield return null;
            Assert.AreEqual(0, room.EnemiesRemaining);
            Assert.IsFalse(room.ShowsEnemyCount);
        }

        [UnityTest]
        public IEnumerator BossRoom_NeverShowsTheCount_EvenWithSummonsOnTheFloor()
        {
            var room = CreateRoom(RoomType.Boss, 9);
            var go = new GameObject("BossEncounter");
            _created.Add(go);
            var encounter = go.AddComponent<BossEncounter>();
            var bossGo = new GameObject("Boss");
            bossGo.transform.SetParent(go.transform, false);
            bossGo.AddComponent<CircleCollider2D>();
            bossGo.AddComponent<Rigidbody2D>().gravityScale = 0f;
            bossGo.AddComponent<HealthComponent>();
            var boss = bossGo.AddComponent<BossController>();
            encounter.Bind(boss);
            room.SetEngagement(new BossEngagement(encounter));
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            room.NotifyPlayerEntered(player);
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            Assert.IsFalse(room.ShowsEnemyCount, "the boss bar is the arena's readout");
            Assert.AreEqual(0, room.EnemiesRemaining);
            // Normal enemies spawned into the arena (summons) change nothing: the arena is still not counted.
            room.SpawnAdditionalEncounter(Plan(2, 9), player);
            yield return null;
            Assert.IsFalse(room.ShowsEnemyCount);
            Assert.AreEqual(0, room.EnemiesRemaining);
        }

        [UnityTest]
        public IEnumerator NonCombatRooms_NeverShowTheCount_EvenWithEventWaves()
        {
            foreach (var type in new[] { RoomType.Start, RoomType.Loot, RoomType.Treasure, RoomType.Merchant, RoomType.Event, RoomType.MedicalRecovery })
            {
                var room = CreateRoom(type, 20 + (int)type, new Vector2(20f * (int)type, 0f));
                room.SetSpawner(new TrackingSpawner());
                var player = CreatePlayer(new Vector2(20f * (int)type + 2.5f, 2.5f));
                room.NotifyPlayerEntered(player);
                yield return null;
                Assert.IsFalse(room.ShowsEnemyCount, type.ToString());
                Assert.AreEqual(0, room.EnemiesRemaining, type.ToString());
                if (type == RoomType.Event)
                {
                    // A Cursed Chest / Supply Signal wave runs in the room; the room stays a non-combat room for the readout.
                    room.SpawnAdditionalEncounter(Plan(3, 25), player);
                    yield return null;
                    Assert.IsFalse(room.ShowsEnemyCount, "event waves are not the standard encounter");
                    Assert.AreEqual(0, room.EnemiesRemaining);
                }
            }
        }

        [UnityTest]
        public IEnumerator ClientRoom_ShowsTheHostsReplicatedCount_WithoutCountingReplicas()
        {
            var host = CreateRoom(RoomType.Combat, 3);
            var spawner = new TrackingSpawner();
            host.SetEncounter(Plan(4), spawner);
            var client = CreateRoom(RoomType.Combat, 3, new Vector2(60f, 0f));
            client.SetAuthoritative(false);
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            host.NotifyPlayerEntered(player);
            yield return null;
            Assert.AreEqual(4, host.EnemiesRemaining);
            client.RestoreState(host.State.Clone());
            Assert.IsTrue(client.ShowsEnemyCount, "the client mirrors Active + Combat");
            Assert.AreEqual(4, client.EnemiesRemaining, "the replicated number, not a local count (the client has no enemies of its own)");
            Kill(spawner.Spawned[0]);
            Kill(spawner.Spawned[1]);
            yield return null;
            Assert.AreEqual(2, host.EnemiesRemaining);
            client.RestoreState(host.State.Clone());
            Assert.AreEqual(2, client.EnemiesRemaining, "client display matches the authoritative count after replication");
            Assert.AreEqual(2, client.State.EnemiesRemaining);
        }

        [UnityTest]
        public IEnumerator HudViewModelAndView_ShowTheChipOnlyWhileEligible_AndUpdateAtOnce()
        {
            var room = CreateRoom(RoomType.Combat, 3);
            var spawner = new TrackingSpawner();
            room.SetEncounter(Plan(2), spawner);
            var vm = new DungeonHudViewModel();
            RoomRuntime current = null;
            vm.BindEnemyCount(() => current != null ? (current.ShowsEnemyCount, current.EnemiesRemaining) : (false, 0));
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            yield return null;
            Assert.IsFalse(view.EnemyCountVisible, "no room: hidden");
            Assert.AreEqual(string.Empty, view.EnemiesText);
            Assert.IsTrue(view.EnemyCount.HasIconSprite, "the hostile token from the skin");
            Assert.IsFalse(DungeonHudView.EnemiesRect.Overlaps(DungeonHudView.CoinsRect), "sits under the coins, never over them");
            Assert.GreaterOrEqual(DungeonHudView.EnemiesRect.Y, DungeonHudView.CoinsRect.Bottom);

            current = room;
            var player = CreatePlayer(new Vector2(2.5f, 2.5f));
            room.NotifyPlayerEntered(player);
            yield return null;
            vm.Tick();
            Assert.IsTrue(vm.Snapshot.EnemiesVisible);
            Assert.AreEqual(2, vm.Snapshot.EnemiesRemaining);
            Assert.IsTrue(view.EnemyCountVisible);
            Assert.AreEqual("x2", view.EnemiesText);
            Kill(spawner.Spawned[0]);
            yield return null;
            vm.Tick();
            Assert.AreEqual("x1", view.EnemiesText);
            Kill(spawner.Spawned[1]);
            yield return null;
            yield return null;
            vm.Tick();
            Assert.IsFalse(view.EnemyCountVisible, "cleared: the chip is gone");
            Assert.AreEqual(string.Empty, view.EnemiesText);
            vm.Dispose();
        }
    }
}
