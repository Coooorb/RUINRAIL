using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 071 — the runtime spawns a plan at markers under the active cap, reinforces on death, signals completion, caps summons.</summary>
    public class EncounterRuntimeTests
    {
        private List<EnemyDefinition> _archetypes;
        private GameObject _targetObject;
        private readonly List<Object> _created = new();

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
            _targetObject = new GameObject("Player");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(50f, 50f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _targetObject.AddComponent<TestDamageableTarget>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            _created.Clear();
        }

        private static void Kill(EnemyController actor) => actor.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));

        [UnityTest]
        public IEnumerator Runtime_SpawnsAtMarkersUnderTheCap_ReinforcesOnDeath_AndSignalsCompletion()
        {
            // Trio at depth 30: budget 21-26 threat -> more than 18 actors is impossible, so force a big cheap plan instead.
            var swarm = _archetypes.Single(a => a.Id == "swarm");
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(1, 30, 1, Biome.RuinedMetro, 0);
            var plan = new EncounterPlan(context, 12f, 15f, 14f, new[] { new EncounterEntry(swarm, 9), new EncounterEntry(grunt, 4) });
            Assert.AreEqual(13, plan.TotalCount);
            Assert.AreEqual(10, plan.ActiveCap);

            var spawner = new TrackingSpawner();
            var points = new List<Vector2> { new(0f, 0f), new(3f, 0f), new(0f, 3f) };
            var runtime = new EncounterRuntime(plan, spawner, points, _targetObject.transform);
            var completed = 0;
            runtime.Completed += _ => completed++;
            runtime.Start();
            yield return null;

            Assert.AreEqual(10, runtime.SpawnedCount, "First wave fills the solo cap of 10.");
            Assert.AreEqual(3, runtime.PendingCount, "The rest wait as reinforcements.");
            Assert.AreEqual(10, runtime.LivingCount);
            Assert.IsFalse(runtime.IsComplete);
            CollectionAssert.AreEqual(new[] { "swarm", "swarm", "swarm", "swarm", "swarm", "swarm", "swarm", "swarm", "swarm", "grunt" }, spawner.Spawned.Select(s => s.Definition.Id));
            Assert.AreEqual(points[1], (Vector2)spawner.Spawned[1].transform.position, "Markers cycle deterministically.");
            Assert.AreEqual(points[0], (Vector2)spawner.Spawned[3].transform.position);

            Kill(spawner.Spawned[0]);
            Assert.AreEqual(11, runtime.SpawnedCount, "A death frees capacity: one reinforcement spawns immediately.");
            Assert.AreEqual(10, runtime.LivingCount);
            Kill(spawner.Spawned[1]);
            Kill(spawner.Spawned[2]);
            Assert.AreEqual(13, runtime.SpawnedCount);
            Assert.AreEqual(0, runtime.PendingCount);
            Assert.AreEqual(0, completed, "Not complete while actors live.");

            foreach (var actor in spawner.Spawned.Where(a => a.IsAlive).ToList()) Kill(actor);
            Assert.IsTrue(runtime.IsComplete);
            Assert.AreEqual(1, completed, "Completion signalled exactly once when every spawned actor is resolved.");
            runtime.Tick();
            Assert.AreEqual(1, completed);
        }

        [UnityTest]
        public IEnumerator SummonedUnits_ObeyTheActiveCap()
        {
            var summoner = _archetypes.Single(a => a.Id == "summoner");
            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var context = new EncounterContext(2, 20, 1, Biome.Rustworks, 0);
            var plan = new EncounterPlan(context, 10f, 13f, 12f, new[] { new EncounterEntry(summoner, 1), new EncounterEntry(grunt, 8) });
            var spawner = new TrackingSpawner();
            var runtime = new EncounterRuntime(plan, spawner, new List<Vector2> { Vector2.zero }, _targetObject.transform);
            runtime.Start();
            yield return null;
            Assert.AreEqual(9, runtime.LivingCount);

            var summonerActor = spawner.Spawned.Single(s => s.Definition.Id == "summoner");
            Assert.IsNotNull(summonerActor.Summoner);
            var created = summonerActor.Summoner.SummonWave();
            Assert.AreEqual(1, created, "Only one slot was free under the cap of 10.");
            Assert.AreEqual(10, runtime.LivingCount);
            Assert.AreEqual(0, summonerActor.Summoner.SummonWave(), "At the cap a wave summons nothing.");
            Assert.AreEqual(10, runtime.LivingCount);

            // Summoned units are part of the encounter: completion waits for them (the summon also went through the tracking spawner).
            Assert.AreEqual(10, spawner.Spawned.Count);
            foreach (var actor in spawner.Spawned.Take(9).ToList()) Kill(actor);
            Assert.IsFalse(runtime.IsComplete, "The summoned swarm is still alive.");
            foreach (var actor in runtime.Living.ToList()) Kill(actor);
            Assert.IsTrue(runtime.IsComplete);
        }

        [Test]
        public void ComposedPlan_SpawnsOnlyAuthoredDefinitions_ThroughGenericCode()
        {
            var plan = EncounterDirector.Compose(new EncounterContext(99, 15, 2, Biome.OvergrownLabs, 4), _archetypes);
            var spawner = new TrackingSpawner();
            var runtime = new EncounterRuntime(plan, spawner, new List<Vector2> { Vector2.zero, Vector2.one }, _targetObject.transform);
            runtime.Start();
            Assert.AreEqual(Mathf.Min(plan.TotalCount, 14), runtime.SpawnedCount);
            Assert.IsTrue(spawner.Spawned.All(s => _archetypes.Contains(s.Definition)));
            Assert.IsTrue(spawner.Spawned.All(s => s.Target == _targetObject.transform));
            foreach (var actor in spawner.Spawned) Object.DestroyImmediate(actor.gameObject);
        }
    }
}
