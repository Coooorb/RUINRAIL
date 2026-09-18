using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 064 — Swarm archetype: small, fast melee pressure in groups on the shared framework.</summary>
    public class SwarmEnemyTests
    {
        private EnemyDefinition _swarm;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _swarm = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Swarm.asset");
            Assert.IsNotNull(_swarm);
            _targetObject = new GameObject("Target");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(30f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _target = _targetObject.AddComponent<TestDamageableTarget>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private EnemyController Spawn(Vector2 position)
        {
            var go = new GameObject("Swarm");
            _created.Add(go);
            go.transform.position = position;
            var enemy = go.AddComponent<EnemyController>();
            enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 4 });
            enemy.SetTarget(_targetObject.transform);
            enemy.SetDefinition(_swarm);
            return enemy;
        }

        [Test]
        public void Swarm_DataMatchesCombat44()
        {
            Assert.AreEqual("swarm", _swarm.Id);
            Assert.AreEqual(10, _swarm.BaseHealth);
            Assert.AreEqual((3, 5), (_swarm.DamageMin, _swarm.DamageMax));
            Assert.AreEqual(4.0f, _swarm.MoveSpeed, 0.001f);
            Assert.AreEqual(5, _swarm.BaseXp);
            Assert.AreEqual(1, _swarm.UnlockDepth);
            Assert.AreEqual(EnemyAttackKind.MeleeContact, _swarm.AttackKind);
            Assert.IsFalse(_swarm.KeepsDistance, "Pure pursuer.");
            Assert.AreEqual(0, _swarm.StaggerResistancePercent, "Small enemies react fully to stagger/knockback.");
            Assert.GreaterOrEqual(_swarm.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.25f, "Short but readable telegraph at the deepest scale.");
            CollectionAssert.Contains(_swarm.SpawnTags, "swarm");
            Assert.AreEqual(0.5f, _swarm.ThreatCost, "47: Swarm threat 0.5.");
        }

        [UnityTest]
        public IEnumerator Swarm_PursuesAtFour_TelegraphsThenBites_AndStopsOnDeath()
        {
            var swarm = Spawn(Vector2.zero);
            _targetObject.transform.position = new Vector3(10f, 0f, 0f);
            yield return null;
            for (var i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(EnemyState.Chase, swarm.State);
            Assert.AreEqual(4f * 25 * Time.fixedDeltaTime, swarm.transform.position.x, 0.3f, "Moves at 4.0 units/s.");
            Assert.AreEqual(10, swarm.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(5, swarm.XpValue);

            _targetObject.transform.position = swarm.transform.position + new Vector3(0.5f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, swarm.State);
            Assert.AreEqual(0, _target.HitCount, "No damage during the telegraph.");
            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(1, _target.HitCount);
            Assert.AreEqual(4, _target.LastDamageAmount, "Integer band through the shared IDamageable path.");

            swarm.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(10));
            Assert.AreEqual(EnemyState.Dead, swarm.State);
            var hits = _target.HitCount;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(hits, _target.HitCount, "A dead swarm never bites again.");
            Assert.AreEqual(Vector2.zero, swarm.GetComponent<Rigidbody2D>().linearVelocity);
        }

        [UnityTest]
        public IEnumerator ManySwarms_RunTogether_DeterministicallyAndWithoutGlobalSearches()
        {
            var swarms = new List<EnemyController>();
            for (var i = 0; i < 12; i++) swarms.Add(Spawn(new Vector2(-2f - i * 0.6f, (i % 3 - 1) * 1.5f)));
            _targetObject.transform.position = new Vector3(6f, 0f, 0f);
            yield return null;
            for (var i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

            Assert.IsTrue(swarms.All(s => s.State == EnemyState.Chase), "Every instance chases the injected target.");
            Assert.IsTrue(swarms.All(s => s.GetComponent<Rigidbody2D>().linearVelocity.magnitude > 3.9f), "All at 4.0 units/s.");
            Assert.IsTrue(swarms.All(s => s.Target == _targetObject.transform), "Injected target: no scene search per instance.");

            // Deterministic: the same start yields the same positions on a second run.
            var snapshot = swarms.Select(s => (Vector2)s.transform.position).ToList();
            foreach (var s in swarms) Object.DestroyImmediate(s.gameObject);
            var again = new List<EnemyController>();
            for (var i = 0; i < 12; i++) again.Add(Spawn(new Vector2(-2f - i * 0.6f, (i % 3 - 1) * 1.5f)));
            yield return null;
            for (var i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 12; i++)
            {
                Assert.AreEqual(snapshot[i].x, again[i].transform.position.x, 0.05f, $"swarm {i} x");
                Assert.AreEqual(snapshot[i].y, again[i].transform.position.y, 0.05f, $"swarm {i} y");
            }

            // The per-frame path contains no scene-wide searches (only the Awake fallback when no target is injected).
            var source = System.IO.File.ReadAllText("Assets/Game/Scripts/Enemies/EnemyController.cs");
            var updateStart = source.IndexOf("private void Update()", System.StringComparison.Ordinal);
            Assert.Greater(updateStart, 0);
            Assert.IsFalse(source.Substring(updateStart).Contains("FindFirstObjectByType") || source.Substring(updateStart).Contains("FindObjectsByType"), "No global searches in Update/FixedUpdate.");
        }
    }
}
