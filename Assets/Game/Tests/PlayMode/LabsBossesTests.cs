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
    /// TASK 126/127: Subject Omega (1,250 HP, 28–34, XP 750) and A.E.G.I.S. Core (1,050 HP, 28–34, XP 700) — range picks,
    /// once-only phase 2 at 50% with the combined mechanic, and the Labs arena bindings with once-only cache/transit/XP hooks.
    /// </summary>
    public class LabsBossesTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _omega;
        private BossDefinition _aegis;

        [SetUp]
        public void SetUp()
        {
            _omega = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_SubjectOmega.asset");
            _aegis = AssetDatabase.LoadAssetAtPath<BossDefinition>("Assets/Game/ScriptableObjects/Enemies/Bosses/Boss_AegisCore.asset");
            Assert.IsNotNull(_omega);
            Assert.IsNotNull(_aegis);
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
        public void LabsBosses_ExposeApprovedStats_AndPickAttacksByRange()
        {
            var omega = new DefaultBossSpawner(new[] { _omega }).Spawn(_omega, Vector2.zero, null);
            _created.Add(omega.gameObject);
            Assert.AreEqual(1250, omega.Boss.Health.MaxHealth);
            Assert.AreEqual(750, omega.Boss.XpValue);
            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            omega.Boss.SetTarget(player.transform);
            Assert.AreEqual("Arm Slam", omega.Boss.SelectAttack().DisplayName);
            player.transform.position = new Vector2(4f, 0f);
            Assert.AreEqual("Charge", omega.Boss.SelectAttack().DisplayName);
            player.transform.position = new Vector2(8.5f, 0f);
            Assert.AreEqual("Spore Projectile Burst", omega.Boss.SelectAttack().DisplayName);

            var aegis = new DefaultBossSpawner(new[] { _aegis }).Spawn(_aegis, new Vector2(60f, 0f), null);
            _created.Add(aegis.gameObject);
            Assert.AreEqual(1050, aegis.Boss.Health.MaxHealth);
            Assert.AreEqual(700, aegis.Boss.XpValue);
            var (target, _) = SpawnPlayerDummy(new Vector2(61.5f, 0f));
            aegis.Boss.SetTarget(target.transform);
            Assert.AreEqual("Radial Projectile Ring", aegis.Boss.SelectAttack().DisplayName, "Close: the ring.");
            target.transform.position = new Vector2(65.5f, 0f);
            Assert.AreEqual("Reposition Dash", aegis.Boss.SelectAttack().DisplayName, "Mid: reposition.");
            target.transform.position = new Vector2(68f, 0f);
            Assert.AreEqual("Triple Energy Burst", aegis.Boss.SelectAttack().DisplayName, "Far: the burst.");
            target.transform.position = new Vector2(71f, 0f);
            Assert.AreEqual("Line Energy Attack", aegis.Boss.SelectAttack().DisplayName, "Beyond the burst: the line.");
        }

        [UnityTest]
        public IEnumerator PhaseTwo_StartsOnceAtHalfHealth_ForBothLabsBosses_AndAddsTheCombinedMechanic()
        {
            foreach (var (definition, half, extra) in new[] { (_omega, 625, "Organic Area Denial"), (_aegis, 525, "Ring While Charging") })
            {
                var encounter = new DefaultBossSpawner(new[] { definition }).Spawn(definition, Vector2.zero, null);
                _created.Add(encounter.gameObject);
                var boss = encounter.Boss;
                var phases = new List<int>();
                encounter.PhaseChanged += (_, p) => phases.Add(p);
                var (player, _) = SpawnPlayerDummy(new Vector2(40f, 0f));
                boss.SetTarget(player.transform);
                yield return null;

                boss.Health.TryApplyDamage(new DamageRequest(half - 1));
                Assert.AreEqual(1, boss.Phase, $"{definition.Id}: one above half: still phase 1.");
                boss.Health.TryApplyDamage(new DamageRequest(1));
                Assert.AreEqual(2, boss.Phase, $"{definition.Id}: exactly half: phase 2.");
                Assert.AreEqual(definition.PhaseTwoTimingMultiplier, boss.TimingMultiplier, 0.0001f);
                CollectionAssert.AreEqual(new[] { 2 }, phases);
                player.transform.position = new Vector2(3f, 0f);
                Assert.AreEqual(extra, boss.SelectAttack().DisplayName, $"{definition.Id}: phase 2 prepends the combined mechanic.");
                boss.Health.TryApplyDamage(new DamageRequest(200));
                CollectionAssert.AreEqual(new[] { 2 }, phases, "Phase transition happens exactly once.");
                Object.DestroyImmediate(encounter.gameObject);
                Object.DestroyImmediate(player);
            }
        }

        private RoomRuntime CreateArena(string[] tags)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "labs_boss_test");
            Set(definition, "_roomType", RoomType.Boss);
            Set(definition, "_biome", Biome.OvergrownLabs);
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
        public IEnumerator LabsArenas_BindTheirBoss_ScaleIt_AndDefeatOpensCacheAndTransitOnce()
        {
            foreach (var (tag, id, hp, xpExpected) in new[] { ("boss:subject_omega", "boss_subject_omega", 1250, 750), ("boss:aegis_core", "boss_aegis_core", 1050, 700) })
            {
                yield return ArenaRun(tag, id, hp, xpExpected);
            }
        }

        private IEnumerator ArenaRun(string tag, string id, int hp, int xpExpected)
        {
            var services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(new[] { _omega, _aegis }))
            };
            var context = new DungeonRuntimeContext(11, 3, 2, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner());
            var room = CreateArena(new[] { "labs", "boss", tag });
            var binding = RoomCategoryComposer.Compose(room, context, services);
            Assert.IsNotNull(binding.Boss);
            Assert.AreEqual(id, binding.Boss.Boss.Definition.Id);
            Assert.AreEqual(DepthScaling.ScaledHealth(hp, 3, 2, true), binding.Boss.Boss.Health.MaxHealth, "Boss HP on the boss curve (duo x1.65 after depth).");
            Assert.IsTrue(binding.BossCache.IsLocked);
            Assert.IsFalse(binding.Transit.IsActivated);

            var (player, _) = SpawnPlayerDummy((Vector2)room.transform.position + new Vector2(17.5f, 1f));
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
            CollectionAssert.AreEqual(new[] { xpExpected }, defeated, "XP once.");
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
            Object.DestroyImmediate(room.gameObject);
            Object.DestroyImmediate(player);
        }
    }
}
