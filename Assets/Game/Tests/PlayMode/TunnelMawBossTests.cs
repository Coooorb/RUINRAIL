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
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 089: Tunnel Maw (1,150 HP, 28–34 strongest, XP 700) with a once-only phase 2 at 50%, limited Swarm roar,
    /// and the second Ruined Metro arena binding through the room runtime with once-only cache/transit hooks.
    /// </summary>
    public class TunnelMawBossTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _tunnelMaw;
        private BossDefinition _conductor;

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
            _tunnelMaw = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TunnelMaw.asset");
            _conductor = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TheConductor.asset");
            Assert.IsNotNull(_tunnelMaw);
            Assert.IsNotNull(_conductor);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var boss in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None)) if (boss != null) Object.DestroyImmediate(boss.transform.parent != null ? boss.transform.parent.gameObject : boss.gameObject);
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

        private (GameObject go, HealthComponent health) SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(2000);
            return (go, health);
        }

        [Test]
        public void TunnelMaw_ExposesApprovedStats_AndPicksAttacksByRange()
        {
            var spawner = new DefaultBossSpawner(new[] { _tunnelMaw, _conductor });
            var encounter = spawner.Spawn(_tunnelMaw, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            Assert.AreEqual(1150, boss.Health.MaxHealth);
            Assert.AreEqual(700, boss.XpValue);
            Assert.AreEqual(1, boss.Phase);

            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            boss.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 1.5f, "Claw Sweep", "Close: the claw sweep.");
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 3f, "Bite/Lunge", "Bite/Lunge is the band's attack here");
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 9f, "Marked Leap", "Beyond roar range the leap closes the gap.");
        }

        [UnityTest]
        public IEnumerator PhaseTwo_StartsOnceAtHalfHealth_SpeedsTiming_AndAddsTheBurrowEmergence()
        {
            var encounter = new DefaultBossSpawner(new[] { _tunnelMaw }).Spawn(_tunnelMaw, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            var phases = new List<int>();
            encounter.PhaseChanged += (_, p) => phases.Add(p);
            var (player, _) = SpawnPlayerDummy(new Vector2(40f, 0f));
            boss.SetTarget(player.transform);
            yield return null;

            boss.Health.TryApplyDamage(new DamageRequest(574));
            Assert.AreEqual(1, boss.Phase, "576/1150 > 50%: still phase 1.");
            Assert.AreEqual(1f, boss.TimingMultiplier, 0.0001f);
            boss.Health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(2, boss.Phase, "575/1150 = 50%: phase 2.");
            Assert.AreEqual(0.8f, boss.TimingMultiplier, 0.0001f);
            CollectionAssert.AreEqual(new[] { 2 }, phases);

            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 5f, "Burrow Emergence", "Phase 2 prepends the burrow emergence to the rotation.");

            boss.Health.TryApplyDamage(new DamageRequest(300));
            Assert.AreEqual(2, boss.Phase);
            CollectionAssert.AreEqual(new[] { 2 }, phases, "Phase transition happens exactly once.");
        }

        [UnityTest]
        public IEnumerator Roar_SummonsLimitedSwarm_ThroughTheRoomSpawner_NeverAboveTheCap()
        {
            var summons = new TrackingSpawner();
            var encounter = new DefaultBossSpawner(new[] { _tunnelMaw }, null, summons).Spawn(_tunnelMaw, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            var (player, _) = SpawnPlayerDummy(new Vector2(6f, 0f));
            var waves = new List<int>();
            boss.Summoned += (_, s) => waves.Add(s.Count);
            boss.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual("Roar", boss.CurrentAttack.DisplayName, "At 6 tiles the roar is the first ready attack in range.");

            var guard = 0;
            while (waves.Count == 0 && guard++ < 40) yield return new WaitForSeconds(0.1f);
            CollectionAssert.AreEqual(new[] { 2 }, waves, "Roar summons two Swarm.");
            Assert.AreEqual(2, summons.Spawned.Count);
            Assert.IsTrue(summons.Spawned.All(s => s.Definition.Id == "swarm"));
            Assert.AreEqual(2, boss.LivingSummons.Count);

            // Let a second roar happen without killing summons: the cap of 4 limits the pressure.
            for (var i = 0; i < 1; i++)
            {
                boss.SetTarget(player.transform);
                var before = waves.Count;
                var wait = 0;
                while (waves.Count == before && wait++ < 150) yield return new WaitForSeconds(0.1f);
            }

            Assert.LessOrEqual(summons.Spawned.Count, 4, "Never above MaxLivingSummons.");
            Assert.LessOrEqual(boss.LivingSummons.Count, 4);
        }

        // ---- Arena binding through the room runtime ----

        private RoomRuntime CreateArena(string[] tags, Vector2 offset)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "metro_boss_test");
            Set(definition, "_roomType", RoomType.Boss);
            Set(definition, "_biome", Biome.RuinedMetro);
            Set(definition, "_tags", tags);
            var go = new GameObject("Arena");
            _created.Add(go);
            go.transform.position = offset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(36, 24));
            foreach (var (role, cell) in new[] { (RoomMarkerRole.BossAnchor, new Vector2Int(18, 12)), (RoomMarkerRole.ChestSpawn, new Vector2Int(15, 5)), (RoomMarkerRole.InteractableSpawn, new Vector2Int(5, 2)) })
            {
                var marker = new GameObject(role.ToString()).AddComponent<RoomMarker>();
                marker.transform.SetParent(go.transform, false);
                marker.Configure(role, cell);
                marker.SnapToGrid();
            }

            var socket = new GameObject("Door_S").AddComponent<DoorSocket>();
            socket.transform.SetParent(go.transform, false);
            socket.Configure(DoorDirection.South, new Vector2Int(17, 0));
            socket.SnapToGrid();
            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, 9, depth: 3, partySize: 1);
            return runtime;
        }

        [UnityTest]
        public IEnumerator TaggedArena_BindsTunnelMaw_ScalesIt_AndDefeatOpensCacheAndTransitOnce()
        {
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { _conductor, _tunnelMaw }))
            };
            var context = new DungeonRuntimeContext(11, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner());
            var room = CreateArena(new[] { "metro", "boss", "boss:tunnel_maw" }, Vector2.zero);
            var binding = RoomCategoryComposer.Compose(room, context, services);
            Assert.IsNotNull(binding.Boss);
            Assert.AreEqual("boss_tunnel_maw", binding.Boss.Boss.Definition.Id, "The arena tag binds the Tunnel Maw.");
            Assert.AreEqual(DepthScaling.ScaledHealth(1150, 3, 1, true), binding.Boss.Boss.Health.MaxHealth, "Boss HP scaled by depth on the boss curve.");
            Assert.IsTrue(binding.BossCache.IsLocked);
            Assert.IsFalse(binding.Transit.IsActivated);

            var other = CreateArena(new[] { "metro", "boss", "boss:the_conductor" }, new Vector2(80f, 0f));
            var otherBinding = RoomCategoryComposer.Compose(other, context, services);
            Assert.AreEqual("boss_the_conductor", otherBinding.Boss.Boss.Definition.Id, "Either Ruined Metro boss loads as configured.");

            var (player, _) = SpawnPlayerDummy(new Vector2(17.5f, 1f));
            var cacheUnlocks = 0;
            var transitActivations = 0;
            binding.Transit.Activated += _ => transitActivations++;
            var cleared = 0;
            room.Cleared += (_, _) => cleared++;
            Assert.IsTrue(room.NotifyPlayerEntered(player));
            Assert.IsTrue(room.DoorsLocked);
            yield return null;

            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.IsTrue(binding.Boss.IsDefeated);
            if (!binding.BossCache.IsLocked) cacheUnlocks++;
            Assert.AreEqual(1, cacheUnlocks);
            Assert.AreEqual(1, transitActivations);
            Assert.AreEqual(1, cleared);
            Assert.IsFalse(room.DoorsLocked);

            // Re-firing defeat paths cannot duplicate the reward hooks.
            Assert.IsFalse(binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(1)));
            binding.Transit.Activate();
            yield return null;
            Assert.AreEqual(1, transitActivations);
            Assert.AreEqual(1, cleared);
            Assert.IsTrue(binding.BossCache.TryOpen(out _));
            Assert.IsFalse(binding.BossCache.TryOpen(out _), "Boss Cache pays once.");
            foreach (var go in services.GroundLoot.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }
    }
}
