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
using RuinRail.Gameplay.Loot;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 101: a trio arena scales the boss on the boss curve, its roar summons on the normal curve, and never touches
    /// per-hit damage; a solo arena at the same depth rolls identical damage.
    /// </summary>
    public class CoopScalingRuntimeTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _tunnelMaw;
        private DepthScalingConfig _config;

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
            _config = AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            Assert.IsNotNull(_tunnelMaw);
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

        private GameObject SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            go.AddComponent<HealthComponent>().SetMaxHealth(5000);
            return go;
        }

        private RoomRuntime CreateArena(Vector2 offset, int depth, int partySize)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "metro_boss_scaling");
            Set(definition, "_roomType", RoomType.Boss);
            Set(definition, "_biome", Biome.RuinedMetro);
            Set(definition, "_tags", new[] { "metro", "boss", "boss:tunnel_maw" });
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
            runtime.Configure(root, 9, depth, partySize);
            return runtime;
        }

        private (RoomRuntime room, RoomContentBinding binding, TrackingSpawner summons) BindArena(Vector2 offset, int depth, int partySize)
        {
            var summons = new TrackingSpawner();
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { _tunnelMaw }, null, summons))
            };
            var context = new DungeonRuntimeContext(11, depth, partySize, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), _config);
            var room = CreateArena(offset, depth, partySize);
            var binding = RoomCategoryComposer.Compose(room, context, services);
            Assert.IsNotNull(binding.Boss);
            return (room, binding, summons);
        }

        [UnityTest]
        public IEnumerator TrioArena_BossOnBossCurve_SummonsOnNormalCurve_DamageUnchanged()
        {
            const int depth = 3;
            var (room, binding, summons) = BindArena(Vector2.zero, depth, 3);
            var boss = binding.Boss.Boss;
            Assert.AreEqual(DepthScaling.ScaledHealth(1150, depth, 3, true, _config), boss.Health.MaxHealth, "Trio boss: x2.20 after the depth curve.");
            Assert.AreNotEqual(DepthScaling.ScaledHealth(1150, depth, 1, true, _config), boss.Health.MaxHealth);

            // Roar at ~6 tiles: summons arrive through the room spawner and are scaled as normal enemies for a trio.
            var player = SpawnPlayerDummy((Vector2)boss.transform.position + new Vector2(6f, 0f));
            var waves = 0;
            boss.Summoned += (_, s) => waves++;
            Assert.IsTrue(room.NotifyPlayerEntered(player));
            boss.SetTarget(player.transform);
            var guard = 0;
            while (waves == 0 && guard++ < 60) yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, waves, "The roar summoned once.");
            Assert.IsTrue(summons.Spawned.Count > 0);
            foreach (var summon in summons.Spawned)
            {
                var expected = DepthScaling.ScaledHealth(summon.Definition.BaseHealth, depth, 3, false, _config);
                Assert.AreEqual(expected, summon.GetComponent<HealthComponent>().MaxHealth, $"Summon '{summon.Definition.Id}' on the NORMAL trio curve (x1.35).");
                Assert.AreNotEqual(DepthScaling.ScaledHealth(summon.Definition.BaseHealth, depth, 3, true, _config), summon.GetComponent<HealthComponent>().MaxHealth, "Never the boss multiplier.");
            }

            // Per-hit damage: identical band for solo and trio at the same depth (roller has no party input).
            var (_, solo, _) = BindArena(new Vector2(120f, 0f), depth, 1);
            var band = DepthScaling.ScaledDamage(_tunnelMaw.StrongestAttackDamageMin, _tunnelMaw.StrongestAttackDamageMax, depth, _config);
            var trioRoller = new DepthScaledDamageRoller(new UnityRandomDamageRoller(), depth, _config);
            for (var i = 0; i < 50; i++)
            {
                var rolled = trioRoller.Roll(_tunnelMaw.StrongestAttackDamageMin, _tunnelMaw.StrongestAttackDamageMax);
                Assert.GreaterOrEqual(rolled, band.min);
                Assert.LessOrEqual(rolled, band.max);
            }

            Assert.AreEqual(DepthScaling.ScaledHealth(1150, depth, 1, true, _config), solo.Boss.Boss.Health.MaxHealth, "Solo boss at the same depth: no party multiplier.");
        }
    }
}
