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
    /// TASK 116: The Foundry Titan (1,350 HP, 30–36 strongest, XP 800) — range-based attack picks, a once-only phase 2
    /// at 50% that speeds timing and adds the reactor burn zones, and the Rustworks foundry arena binding with
    /// once-only cache/transit/XP hooks through the shared boss framework.
    /// </summary>
    public class FoundryTitanBossTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _titan;

        [SetUp]
        public void SetUp()
        {
            _titan = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_TheFoundryTitan.asset");
            Assert.IsNotNull(_titan);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
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
        public void Titan_ExposesApprovedStats_AndPicksAttacksByRange()
        {
            var encounter = new DefaultBossSpawner(new[] { _titan }).Spawn(_titan, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            Assert.AreEqual(1350, boss.Health.MaxHealth);
            Assert.AreEqual(800, boss.XpValue);
            Assert.AreEqual(1, boss.Phase);

            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            boss.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 1.5f, "Arm Sweep", "Point blank: the sweep.");
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 2.7f, "Hydraulic Slam", "Just outside the sweep: the slam.");
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 5f, "Furnace Blast", "Mid range: the cone.");
            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 8.5f, "Marked Rocket Barrage", "Far: marked rockets.");
        }

        [UnityTest]
        public IEnumerator PhaseTwo_StartsOnceAtHalfHealth_SpeedsTiming_AndAddsTheReactorBurn()
        {
            var encounter = new DefaultBossSpawner(new[] { _titan }).Spawn(_titan, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            var phases = new List<int>();
            encounter.PhaseChanged += (_, p) => phases.Add(p);
            var (player, _) = SpawnPlayerDummy(new Vector2(40f, 0f));
            boss.SetTarget(player.transform);
            yield return null;

            boss.Health.TryApplyDamage(new DamageRequest(674));
            Assert.AreEqual(1, boss.Phase, "676/1350 > 50%: still phase 1.");
            Assert.AreEqual(1f, boss.TimingMultiplier, 0.0001f);
            boss.Health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(2, boss.Phase, "675/1350 = 50%: reactor overload.");
            Assert.AreEqual(0.8f, boss.TimingMultiplier, 0.0001f);
            CollectionAssert.AreEqual(new[] { 2 }, phases);

            BossSelectionAssert.CanSelectAt(boss, player.transform, Vector2.zero, 5f, "Reactor Overload Burn", "Phase 2 prepends the burning zones to the rotation.");

            boss.Health.TryApplyDamage(new DamageRequest(300));
            Assert.AreEqual(2, boss.Phase);
            CollectionAssert.AreEqual(new[] { 2 }, phases, "Phase transition happens exactly once.");
        }

        private RoomRuntime CreateArena(string[] tags)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "rust_boss_test");
            Set(definition, "_roomType", RoomType.Boss);
            Set(definition, "_biome", Biome.Rustworks);
            Set(definition, "_tags", tags);
            var go = new GameObject("Arena");
            _created.Add(go);
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
            runtime.Configure(root, 9, depth: 3, partySize: 2);
            return runtime;
        }

        [UnityTest]
        public IEnumerator FoundryArena_BindsTheTitan_ScalesIt_AndDefeatOpensCacheAndTransitOnce()
        {
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { _titan }))
            };
            var context = new DungeonRuntimeContext(11, 3, 2, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner());
            var room = CreateArena(new[] { "rustworks", "boss", "boss:the_foundry_titan" });
            var binding = RoomCategoryComposer.Compose(room, context, services);
            Assert.IsNotNull(binding.Boss);
            Assert.AreEqual("boss_the_foundry_titan", binding.Boss.Boss.Definition.Id);
            Assert.AreEqual(DepthScaling.ScaledHealth(1350, 3, 2, true), binding.Boss.Boss.Health.MaxHealth, "Boss HP on the boss curve (duo x1.65 after depth).");
            Assert.IsNull(binding.BossCache, "The Boss Cache does not exist before the boss dies (46: boss death spawns it).");
            Assert.IsFalse(binding.Transit.IsActivated);

            var (player, _) = SpawnPlayerDummy(new Vector2(17.5f, 1f));
            var transitActivations = 0;
            binding.Transit.Activated += _ => transitActivations++;
            var cleared = 0;
            room.Cleared += (_, _) => cleared++;
            var defeated = new List<int>();
            binding.Boss.BossDefeated += (_, xp) => defeated.Add(xp);
            Assert.IsTrue(room.NotifyPlayerEntered(player));
            Assert.IsTrue(room.DoorsLocked);
            yield return null;

            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(10)), "Clients never damage the boss.");
            DamageAuthority.LocalIsAuthoritative = true;
            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.IsTrue(binding.Boss.IsDefeated);
            CollectionAssert.AreEqual(new[] { 800 }, defeated, "XP 800 once.");
            Assert.IsTrue(binding.BossCache != null && !binding.BossCache.IsLocked, "The boss's death spawns one openable Boss Cache.");
            Assert.AreEqual(1, transitActivations);
            Assert.AreEqual(1, cleared);
            Assert.IsFalse(room.DoorsLocked);

            Assert.IsFalse(binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(1)));
            binding.Transit.Activate();
            yield return null;
            Assert.AreEqual(1, transitActivations);
            Assert.AreEqual(1, cleared);
            Assert.AreEqual(1, defeated.Count);
            Assert.IsTrue(binding.BossCache.TryOpen(out _));
            Assert.IsFalse(binding.BossCache.TryOpen(out _), "Boss Cache pays once.");
            foreach (var go in services.GroundLoot.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }
    }
}
