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
    /// TASK 117: Scrap King (1,000 HP, 22–28 strongest, XP 650) — range-based attack picks, a once-only aggressive
    /// phase 2 at 50% (armor shed: timing x0.7), and the scrap arena binding with once-only cache/transit/XP hooks.
    /// </summary>
    public class ScrapKingBossTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _king;

        [SetUp]
        public void SetUp()
        {
            _king = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_ScrapKing.asset");
            Assert.IsNotNull(_king);
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
        public void ScrapKing_ExposesApprovedStats_AndPicksAttacksByRange()
        {
            var encounter = new DefaultBossSpawner(new[] { _king }).Spawn(_king, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            Assert.AreEqual(1000, boss.Health.MaxHealth);
            Assert.AreEqual(650, boss.XpValue);
            Assert.AreEqual(1, boss.Phase);

            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            boss.SetTarget(player.transform);
            Assert.AreEqual("Heavy Melee Swing", boss.SelectAttack().DisplayName, "Point blank: the swing.");
            player.transform.position = new Vector2(3f, 0f);
            Assert.AreEqual("Combat Roll", boss.SelectAttack().DisplayName, "Mid range: reposition first.");
            player.transform.position = new Vector2(7f, 0f);
            Assert.AreEqual("Grenade Throw", boss.SelectAttack().DisplayName, "Grenade range.");
            player.transform.position = new Vector2(9.5f, 0f);
            Assert.AreEqual("Automatic Burst", boss.SelectAttack().DisplayName, "Far: the burst.");
        }

        [UnityTest]
        public IEnumerator PhaseTwo_StartsOnceAtHalfHealth_ShedsArmorForAggression()
        {
            var encounter = new DefaultBossSpawner(new[] { _king }).Spawn(_king, Vector2.zero, null);
            _created.Add(encounter.gameObject);
            var boss = encounter.Boss;
            var phases = new List<int>();
            encounter.PhaseChanged += (_, p) => phases.Add(p);
            var (player, _) = SpawnPlayerDummy(new Vector2(40f, 0f));
            boss.SetTarget(player.transform);
            yield return null;

            boss.Health.TryApplyDamage(new DamageRequest(499));
            Assert.AreEqual(1, boss.Phase, "501/1000 > 50%: still phase 1.");
            Assert.AreEqual(1f, boss.TimingMultiplier, 0.0001f);
            boss.Health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(2, boss.Phase, "500/1000 = 50%: armor shed.");
            Assert.AreEqual(0.7f, boss.TimingMultiplier, 0.0001f, "More movement/aggression: every telegraph and recovery runs at 70%.");
            CollectionAssert.AreEqual(new[] { 2 }, phases);

            player.transform.position = new Vector2(3f, 0f);
            Assert.AreEqual("Combat Roll", boss.SelectAttack().DisplayName, "Same known mechanics, faster.");

            boss.Health.TryApplyDamage(new DamageRequest(300));
            Assert.AreEqual(2, boss.Phase);
            CollectionAssert.AreEqual(new[] { 2 }, phases, "Phase transition happens exactly once.");
        }

        private RoomRuntime CreateArena(string[] tags)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "rust_boss_test_king");
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
        public IEnumerator ScrapArena_BindsTheKing_ScalesIt_AndDefeatOpensCacheAndTransitOnce()
        {
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { _king }))
            };
            var context = new DungeonRuntimeContext(11, 3, 2, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner());
            var room = CreateArena(new[] { "rustworks", "boss", "boss:scrap_king" });
            var binding = RoomCategoryComposer.Compose(room, context, services);
            Assert.IsNotNull(binding.Boss);
            Assert.AreEqual("boss_scrap_king", binding.Boss.Boss.Definition.Id);
            Assert.AreEqual(DepthScaling.ScaledHealth(1000, 3, 2, true), binding.Boss.Boss.Health.MaxHealth, "Boss HP on the boss curve (duo x1.65 after depth).");
            Assert.IsTrue(binding.BossCache.IsLocked);
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
            CollectionAssert.AreEqual(new[] { 650 }, defeated, "XP 650 once.");
            Assert.IsFalse(binding.BossCache.IsLocked, "Boss Cache unlocks on defeat.");
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
