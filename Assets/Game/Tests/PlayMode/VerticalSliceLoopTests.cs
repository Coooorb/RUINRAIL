using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Solo vertical slice: expedition start → seeded depth graph → combat kill → boss defeat → transit → return / descend.
    /// </summary>
    public class VerticalSliceLoopTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private DungeonGraphRules _rules;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _rules = DungeonGraphRules.CreateDefault();
            _created.Add(_rules);
            var pistol = ScriptableObject.CreateInstance<TestItemDefinition>();
            _created.Add(pistol);
            typeof(ItemDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pistol, "weapon_p9_ranger");
            typeof(ItemDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pistol, ItemCategory.Weapon);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { pistol });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private ExpeditionService NewService()
        {
            return new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, _ => null, _ammoBalance);
        }

        private (BossController boss, BossEncounter encounter) SpawnBoss(Vector2 position)
        {
            var definition = ScriptableObject.CreateInstance<BossDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "boss_the_conductor");
            Set(definition, "_baseHealth", 1050);
            Set(definition, "_baseXp", 650);
            Set(definition, "_moveset", new EnemyAttackDefinition[0]);
            Set(definition, "_phaseTwoArenaHazards", new EnemyAttackDefinition[0]);
            var root = new GameObject("BossRoom");
            _created.Add(root);
            var encounter = root.AddComponent<BossEncounter>();
            var go = new GameObject("Boss");
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.7f;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<HealthComponent>();
            var boss = go.AddComponent<BossController>();
            boss.SetDefinition(definition);
            encounter.Bind(boss);
            return (boss, encounter);
        }

        private EnemyController SpawnGrunt(Vector2 position)
        {
            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            _created.Add(definition);
            Set(definition, "_id", "grunt");
            Set(definition, "_baseHealth", 30);
            Set(definition, "_baseXp", 12);
            var go = new GameObject("Grunt");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<HealthComponent>();
            var enemy = go.AddComponent<EnemyController>();
            enemy.SetDefinition(definition);
            return enemy;
        }

        [UnityTest]
        public IEnumerator SoloLoop_StartCombatBossTransitReturn_SecuresLootOnce()
        {
            var service = NewService();
            var profile = new PlayerProfile
            {
                BankedCoins = 50,
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = new InventorySnapshot.Entry[0]
                }
            };
            var state = service.Start(profile, 2026, Biome.RuinedMetro);

            // Depth 1 dungeon from the expedition context (graph only; rooms are authored in later tasks).
            var graph = new DungeonGraphGenerator(_rules).Generate(state.CurrentDepth.RunSeed, state.CurrentDepth.Depth);
            Assert.IsTrue(graph.Success, graph.Error);
            Assert.AreEqual(RoomType.Start, graph.Graph.GetNode(graph.Graph.StartId).Type);

            // Combat: a grunt dies and awards XP through the shared enemy foundation.
            var grunt = SpawnGrunt(new Vector2(5f, 0f));
            grunt.Died += e => service.RecordEnemyDefeated(e.XpValue);
            grunt.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(30));
            Assert.AreEqual(1, state.Stats.EnemiesDefeated);
            Assert.AreEqual(12, state.Stats.XpEarned);
            service.RecordRoomCleared();

            // Loot picked up during the run is at risk.
            var found = new ItemInstance("weapon_p9_ranger") { IsAtRisk = true };
            Assert.IsTrue(state.Inventory.TryAddToBackpack(found));
            service.AddCarriedCoins(120);

            // Boss room: transit is inert until the boss falls.
            var (boss, encounter) = SpawnBoss(new Vector2(10f, 0f));
            var transitObject = new GameObject("TransitCar");
            _created.Add(transitObject);
            transitObject.transform.position = new Vector2(12f, 0f);
            transitObject.AddComponent<BoxCollider2D>().isTrigger = true;
            var transit = transitObject.AddComponent<TransitCar>();
            transit.Configure(service, encounter);

            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = new Vector2(12f, 0.5f);
            var interactor = player.AddComponent<PlayerInteractor>();
            var input = new FakePlayerInputReader();
            interactor.SetInputReader(input);
            yield return new WaitForFixedUpdate();

            Assert.IsFalse(transit.IsActivated);
            input.RaiseInteract();
            Assert.IsFalse(transit.IsBoarded, "Cannot board before the boss is defeated.");

            boss.Health.TryApplyDamage(new DamageRequest(1050));
            Assert.IsTrue(encounter.IsDefeated);
            Assert.IsTrue(transit.IsActivated);
            Assert.IsNotNull(service.Transit);
            Assert.AreEqual(TransitDecisionState.Open, service.Transit.State);
            Assert.AreEqual(12 + 650, state.Stats.XpEarned);
            yield return new WaitForFixedUpdate();

            input.RaiseInteract();
            Assert.IsTrue(transit.IsBoarded);
            Assert.IsTrue(transit.Choose(TransitChoice.ReturnToShelter));
            Assert.IsFalse(transit.Choose(TransitChoice.DescendDeeper), "Duplicate/late choice is ignored.");

            Assert.IsFalse(service.IsExpeditionActive);
            Assert.AreEqual(ExpeditionOutcome.Extracted, state.Outcome);
            Assert.AreEqual(170, profile.BankedCoins);
            Assert.AreEqual(662, profile.TotalXp);
            Assert.AreEqual(2, profile.SafeLoadout.Equipped.Length + profile.SafeLoadout.Backpack.Length);
            Assert.IsTrue(profile.SafeLoadout.Backpack.Any(e => e.Item.InstanceId == found.InstanceId));
            Assert.AreEqual(1, profile.SafeLoadout.Backpack.Count(e => e.Item.InstanceId == found.InstanceId), "Secured exactly once.");
            Assert.AreEqual(1, service.LastSummary.EnemiesDefeated);
            Assert.AreEqual(1, service.LastSummary.RoomsCleared);
            Assert.AreEqual(1, service.LastSummary.BossesDefeated);
        }

        [UnityTest]
        public IEnumerator SoloLoop_DescendGeneratesNextDepthContext_WithoutSecuring()
        {
            var service = NewService();
            var profile = new PlayerProfile { BankedCoins = 50, SafeLoadout = new InventorySnapshot { Equipped = new InventorySnapshot.Entry[0], Backpack = new InventorySnapshot.Entry[0] } };
            var state = service.Start(profile, 77, Biome.OvergrownLabs);
            var depth1 = new DungeonGraphGenerator(_rules).Generate(state.CurrentDepth.RunSeed, state.CurrentDepth.Depth).Graph.Signature();
            var loot = new ItemInstance("weapon_p9_ranger") { IsAtRisk = true };
            state.Inventory.TryAddToBackpack(loot);
            service.AddCarriedCoins(80);

            var (boss, encounter) = SpawnBoss(new Vector2(10f, 0f));
            var transit = new GameObject("TransitCar").AddComponent<TransitCar>();
            _created.Add(transit.gameObject);
            transit.Configure(service, encounter);
            boss.Health.TryApplyDamage(new DamageRequest(1050));
            yield return null;

            transit.Interact(new GameObject("p"));
            Assert.IsTrue(transit.Choose(TransitChoice.DescendDeeper));

            Assert.IsTrue(service.IsExpeditionActive);
            Assert.AreEqual(2, state.Depth);
            Assert.AreEqual(2, state.CurrentDepth.Depth);
            Assert.AreEqual(77, state.CurrentDepth.RunSeed);
            var depth2 = new DungeonGraphGenerator(_rules).Generate(state.CurrentDepth.RunSeed, state.CurrentDepth.Depth).Graph.Signature();
            Assert.AreNotEqual(depth1, depth2, "Descending yields a newly generated depth.");
            Assert.AreSame(loot, state.Inventory.BackpackSlots.Single(s => s != null));
            Assert.IsTrue(loot.IsAtRisk);
            Assert.AreEqual(80, state.CarriedCoins);
            Assert.AreEqual(50, profile.BankedCoins);
            Assert.IsNull(profile.SafeLoadout);
            Assert.IsFalse(transit.CanInteract(null), "The old transit is spent; the next depth spawns its own.");

            // Dying on depth 2 keeps nothing.
            service.Fail();
            Assert.AreEqual(50, profile.BankedCoins);
            Assert.IsNull(profile.SafeLoadout);
            Assert.AreEqual(650, profile.TotalXp, "Boss XP was permanent even though the run failed.");
        }
    }
}
