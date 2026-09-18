using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 070 — Summoner: 2-3 Swarms every 9 s, at most 6 living, weak direct attack, stops on death.</summary>
    public class SummonerEnemyTests
    {
        private EnemyDefinition _summoner;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private GameObject _targetObject;
        private readonly List<Object> _created = new();

        private sealed class RecordingSpawner : IEnemySpawner
        {
            public readonly List<(EnemyDefinition definition, Vector2 position)> Spawns = new();
            public readonly List<GameObject> Objects = new();
            public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
            {
                Spawns.Add((definition, position));
                var controller = new DefaultEnemySpawner().Spawn(definition, position, target);
                Objects.Add(controller.gameObject);
                return controller;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _summoner = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Summoner.asset");
            Assert.IsNotNull(_summoner);
            _targetObject = new GameObject("Player");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(6f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("Summoner");
            _created.Add(_enemyObject);
            _enemyObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            _enemyObject.AddComponent<ProjectilePool>();
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 5 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_summoner);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            _created.Clear();
        }

        [Test]
        public void Summoner_DataMatchesCombat44_AndSummonsOnlySwarms()
        {
            Assert.AreEqual("summoner", _summoner.Id);
            Assert.AreEqual(65, _summoner.BaseHealth);
            Assert.AreEqual((4, 6), (_summoner.DamageMin, _summoner.DamageMax), "Low direct damage.");
            Assert.AreEqual(2.0f, _summoner.MoveSpeed, 0.001f);
            Assert.AreEqual(45, _summoner.BaseXp);
            Assert.AreEqual(14, _summoner.UnlockDepth, "Unlocks from Depth 14.");
            Assert.IsTrue(_summoner.IsSummoner);
            Assert.AreEqual("swarm", _summoner.SummonDefinition.Id, "44: summons only Swarm enemies.");
            Assert.AreEqual(9f, _summoner.SummonIntervalSeconds, 0.001f);
            Assert.AreEqual((2, 3), (_summoner.SummonCountMin, _summoner.SummonCountMax));
            Assert.AreEqual(6, _summoner.MaxLivingSummons);
            Assert.AreEqual(EnemyAttackKind.Projectile, _summoner.AttackKind);
            Assert.GreaterOrEqual(_summoner.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.45f);
            CollectionAssert.Contains(_summoner.SpawnTags, "summoner");
            Assert.IsNotNull(_enemy.Summoner);
            Assert.AreEqual(65, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(45, _enemy.XpValue);
        }

        [Test]
        public void Waves_Spawn2To3Swarms_CappedAt6Living_Deterministically()
        {
            var spawner = new RecordingSpawner();
            _enemy.Summoner.SetSpawner(spawner);
            _enemy.Summoner.SetRandom(new SeededRandom(7));

            var first = _enemy.Summoner.SummonWave();
            Assert.IsTrue(first is 2 or 3);
            Assert.IsTrue(spawner.Spawns.All(s => s.definition.Id == "swarm"));
            Assert.AreEqual(first, _enemy.Summoner.LivingSummons);
            var second = _enemy.Summoner.SummonWave();
            var third = _enemy.Summoner.SummonWave();
            Assert.LessOrEqual(_enemy.Summoner.LivingSummons, 6, "Never more than six living summons per summoner.");
            Assert.AreEqual(_enemy.Summoner.LivingSummons, spawner.Spawns.Count);
            Assert.AreEqual(0, _enemy.Summoner.SummonWave(), "At the cap a wave creates nothing.");
            Assert.AreEqual(6, _enemy.Summoner.LivingSummons);

            // Killing summons frees room; the next wave refills up to the cap.
            spawner.Objects[0].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            spawner.Objects[1].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(4, _enemy.Summoner.LivingSummons);
            var refill = _enemy.Summoner.SummonWave();
            Assert.AreEqual(2, refill, "Room for two only.");
            Assert.AreEqual(6, _enemy.Summoner.LivingSummons);

            // Same seed, same wave sizes.
            var a = new List<int>();
            var b = new List<int>();
            var ra = new SeededRandom(99);
            var rb = new SeededRandom(99);
            for (var i = 0; i < 20; i++) { a.Add(ra.NextInt(2, 3)); b.Add(rb.NextInt(2, 3)); }
            CollectionAssert.AreEqual(a, b);
            Assert.IsTrue(a.All(v => v is 2 or 3));
            foreach (var o in spawner.Objects) Object.DestroyImmediate(o);
        }

        [UnityTest]
        public IEnumerator Clock_RunsEvery9Seconds_WhileEngaged_AndDeathStopsSummoningAndAttacks()
        {
            var spawner = new RecordingSpawner();
            _enemy.Summoner.SetSpawner(spawner);
            _enemy.Summoner.SetRandom(new SeededRandom(3));
            yield return null;
            Assert.AreEqual(0, _enemy.Summoner.Waves);
            Assert.Greater(_enemy.Summoner.UntilNextWave, 8.5f, "The 9 s clock starts once engaged.");

            // Fast-forward the clock deterministically instead of waiting 9 s of wall time.
            typeof(EnemySummoner).GetField("_untilNextWave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(_enemy.Summoner, 0.05f);
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(1, _enemy.Summoner.Waves);
            Assert.IsTrue(_enemy.Summoner.LivingSummons is 2 or 3);
            Assert.AreEqual(9f, _enemy.Summoner.UntilNextWave, 0.15f, "Rearmed for the next 9 s.");
            Assert.IsTrue(spawner.Spawns.All(s => Vector2.Distance(s.position, _enemyObject.transform.position) < 2f), "Summons appear around the summoner.");

            // Direct attack: a weak projectile through the shared path.
            var attack = _enemyObject.GetComponent<EnemyProjectileAttack>();
            yield return new WaitForSeconds(0.6f);
            Assert.GreaterOrEqual(attack.ShotsFired, 1);
            Assert.AreEqual(5, attack.LastDamageDealt, "Integer band (fixed 5).");

            // Death: no further waves, no shots.
            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            var waves = _enemy.Summoner.Waves;
            var shots = attack.ShotsFired;
            typeof(EnemySummoner).GetField("_untilNextWave", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(_enemy.Summoner, 0.05f);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(waves, _enemy.Summoner.Waves, "A dead summoner summons nothing.");
            Assert.AreEqual(shots, attack.ShotsFired);
            foreach (var o in spawner.Objects) if (o != null) Object.DestroyImmediate(o);
        }
    }
}
