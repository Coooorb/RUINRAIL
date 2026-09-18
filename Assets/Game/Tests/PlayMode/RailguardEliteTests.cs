using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 088: Railguard (450 HP, 20–25 strongest, 1.8 speed, XP 300) on the shared moveset/telegraph/stagger
    /// foundations, and the Elite room engagement (one Elite at a time) in a Ruined Metro combat room.
    /// </summary>
    public class RailguardEliteTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _railguard;

        [SetUp]
        public void SetUp()
        {
            _railguard = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_Railguard.asset");
            Assert.IsNotNull(_railguard);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private (GameObject go, HealthComponent health) SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);
            return (go, health);
        }

        private EliteEncounter Spawn(Vector2 position, Transform target = null, IDamageRoller roller = null)
        {
            var encounter = new DefaultEliteSpawner().Spawn(_railguard, position, null, target);
            _created.Add(encounter.gameObject);
            if (roller != null) encounter.Elite.SetDamageRoller(roller);
            return encounter;
        }

        [Test]
        public void Railguard_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var encounter = Spawn(Vector2.zero);
            var elite = encounter.Elite;
            Assert.AreEqual(450, elite.Health.MaxHealth);
            Assert.AreEqual(300, elite.XpValue);
            Assert.AreEqual(1.8f, elite.Definition.MoveSpeed, 0.001f);

            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            elite.SetTarget(player.transform);
            Assert.AreEqual("Burst Cannon", elite.SelectAttack().DisplayName, "Far target: Burst Cannon.");
            player.transform.position = new Vector2(3.5f, 0f);
            Assert.AreEqual("Rail Sweep", elite.SelectAttack().DisplayName, "Mid range: Rail Sweep.");
            player.transform.position = new Vector2(1.5f, 0f);
            Assert.AreEqual("Ground Shock", elite.SelectAttack().DisplayName, "Close: Ground Shock denial.");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack(), "Out of every range: keep advancing (slowly).");
        }

        [UnityTest]
        public IEnumerator GroundShock_TelegraphsThenHitsOnceFor20To25_AndBurstCannonFiresThreeShots()
        {
            var encounter = Spawn(Vector2.zero, null, new FixedDamageRoller { FixedValue = 22 });
            var elite = encounter.Elite;
            var (player, health) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            var telegraphs = new List<string>();
            elite.AttackTelegraphStarted += (_, a) => telegraphs.Add(a.DisplayName);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual("Ground Shock", elite.CurrentAttack.DisplayName);
            Assert.AreEqual(500, health.CurrentHealth, "Nothing lands during the telegraph.");
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(500, health.CurrentHealth, "Still telegraphing at 0.4 s of 0.7 s.");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(500 - 22, health.CurrentHealth, "One Ground Shock hit of the fixed roll inside 20-25.");
            CollectionAssert.AreEqual(new[] { "Ground Shock" }, telegraphs);

            // Second Railguard at range: Burst Cannon fires a burst of three projectiles.
            var far = Spawn(new Vector2(0f, 30f), null, new FixedDamageRoller { FixedValue = 11 });
            var (farPlayer, _) = SpawnPlayerDummy(new Vector2(8f, 30f));
            var pool = far.Elite.GetComponent<ProjectilePool>();
            far.Elite.SetTarget(farPlayer.transform);
            yield return null;
            yield return null;
            Assert.AreEqual("Burst Cannon", far.Elite.CurrentAttack.DisplayName);
            yield return new WaitForSeconds(0.8f + 0.2f * 3 + 0.3f);
            Assert.AreEqual(3, pool.SpawnCount, "Burst Cannon: three shots.");
        }

        [UnityTest]
        public IEnumerator Railguard_DiesOnce_AwardsXp300_AndTheEncounterCompletesOnce()
        {
            var encounter = Spawn(Vector2.zero);
            var elite = encounter.Elite;
            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            var completed = new List<int>();
            var died = 0;
            encounter.Completed += (_, xp) => completed.Add(xp);
            elite.Died += _ => died++;
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.IsTrue(encounter.IsStarted);

            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(449)));
            Assert.IsTrue(elite.IsAlive);
            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(1)));
            Assert.IsFalse(elite.IsAlive);
            Assert.AreEqual(1, died);
            CollectionAssert.AreEqual(new[] { 300 }, completed);
            Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(50)));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(MovesetActorState.Dead, elite.State, "No phases, no respawn.");
        }

        // ---- Elite room integration ----

        private RoomRuntime CreateCombatRoom(Vector2 offset)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "metro_elite_test");
            Set(definition, "_roomType", RoomType.Combat);
            Set(definition, "_supportsElite", true);
            var go = new GameObject("EliteRoom");
            _created.Add(go);
            go.transform.position = offset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(24, 16));
            foreach (var cell in new[] { new Vector2Int(20, 13), new Vector2Int(20, 2), new Vector2Int(3, 13) })
            {
                var marker = new GameObject("EnemySpawn").AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(RoomMarkerRole.EnemySpawn, cell);
                marker.SnapToGrid();
            }

            var socket = new GameObject("Door_S").AddComponent<DoorSocket>();
            socket.transform.SetParent(go.transform, false);
            socket.Configure(DoorDirection.South, new Vector2Int(11, 0));
            socket.SnapToGrid();
            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, 4, depth: 10, partySize: 2, isElite: true);
            return runtime;
        }

        [UnityTest]
        public IEnumerator EliteRoom_SpawnsOneScaledElite_LocksDoors_AndClearsOnceWithXp()
        {
            var room = CreateCombatRoom(Vector2.zero);
            var engagement = new EliteEngagement(_railguard, new DefaultEliteSpawner(), depth: 10, partySize: 2);
            var xpAwarded = new List<int>();
            engagement.EliteDefeated += (_, xp) => xpAwarded.Add(xp);
            room.SetEngagement(engagement);
            var cleared = new List<RoomClearedContext>();
            room.Cleared += (_, c) => cleared.Add(c);
            var (player, _) = SpawnPlayerDummy(new Vector2(11.5f, 1f));

            Assert.IsTrue(room.NotifyPlayerEntered(player));
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            Assert.IsTrue(room.DoorsLocked);
            Assert.IsNotNull(engagement.Encounter);
            Assert.AreEqual(1, Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None).Length, "Exactly one Elite at a time.");
            var elite = engagement.Encounter.Elite;
            Assert.AreEqual(DepthScaling.ScaledHealth(450, 10, 2), elite.Health.MaxHealth, "Depth + party scaled like every enemy.");
            Assert.GreaterOrEqual(Vector2.Distance(elite.transform.position, player.transform.position), RoomRuntime.MinSpawnDistanceFromEntryTiles);
            Assert.AreSame(player.transform, elite.Target);
            yield return null;
            Assert.IsTrue(engagement.Encounter.IsStarted);
            Assert.IsFalse(room.NotifyPlayerEntered(player));

            elite.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.IsFalse(room.DoorsLocked);
            Assert.AreEqual(1, cleared.Count);
            Assert.IsTrue(cleared[0].IsElite);
            CollectionAssert.AreEqual(new[] { 300 }, xpAwarded);
            yield return null;
            Assert.AreEqual(1, cleared.Count);
            Assert.AreEqual(1, xpAwarded.Count);
        }

        [Test]
        public void ElitePick_IsSeededPerRoom_AmongTheBiomesTwoElites()
        {
            var stalker = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_TunnelStalker.asset");
            var elites = new[] { _railguard, stalker };
            var a = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var b = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var picks = Enumerable.Range(0, 40).Select(i => a.PickElite(Biome.RuinedMetro, i).Id).ToList();
            CollectionAssert.AreEqual(picks, Enumerable.Range(0, 40).Select(i => b.PickElite(Biome.RuinedMetro, i).Id).ToList());
            CollectionAssert.Contains(picks, "elite_railguard");
            CollectionAssert.Contains(picks, "elite_tunnel_stalker");
            Assert.IsNull(a.PickElite(Biome.Rustworks, 0), "No Rustworks Elites authored yet.");
        }
    }
}
