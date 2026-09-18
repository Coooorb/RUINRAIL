using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 066 — Brute archetype: slow heavy melee with Heavy Swing and telegraphed Ground Slam, high resistances.</summary>
    public class BruteEnemyTests
    {
        private EnemyDefinition _brute;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private EnemyMovesetAttack _attack;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _brute = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Brute.asset");
            Assert.IsNotNull(_brute);
            _targetObject = new GameObject("Target");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(30f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("Brute");
            _created.Add(_enemyObject);
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 25 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_brute);
            var config = StaggerConfig.Create();
            _created.Add(config);
            _enemy.SetStaggerConfig(config);
            _attack = _enemyObject.GetComponent<EnemyMovesetAttack>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Brute_DataMatchesCombat44_WithTwoAuthoredAttacks_AndHighResistances()
        {
            Assert.AreEqual("brute", _brute.Id);
            Assert.AreEqual(90, _brute.BaseHealth);
            Assert.AreEqual((18, 28), (_brute.DamageMin, _brute.DamageMax), "18-28 depending on attack.");
            Assert.AreEqual(1.8f, _brute.MoveSpeed, 0.001f);
            Assert.AreEqual(40, _brute.BaseXp);
            Assert.AreEqual(5, _brute.UnlockDepth, "Unlocks from Depth 5.");
            Assert.AreEqual(EnemyAttackKind.Moveset, _brute.AttackKind);
            Assert.AreEqual(2, _brute.Moveset.Length);
            var slam = _brute.Moveset.Single(a => a.Id == "brute_ground_slam");
            var swing = _brute.Moveset.Single(a => a.Id == "brute_heavy_swing");
            Assert.AreEqual(AttackMotion.Slam, slam.Motion);
            Assert.AreEqual(AttackMotion.Stationary, swing.Motion);
            Assert.IsTrue(swing.DamageMin >= 18 && slam.DamageMax <= 28 && swing.DamageMax <= slam.DamageMax, "Both attacks sit inside the 18-28 band.");
            Assert.GreaterOrEqual(slam.TelegraphSeconds, swing.TelegraphSeconds, "The slam is the clearly telegraphed one.");
            Assert.GreaterOrEqual(swing.TelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.7f, "Readable wind-up even at the scaling cap.");
            Assert.GreaterOrEqual(_brute.StaggerResistancePercent, 50, "High stagger resistance.");
            Assert.GreaterOrEqual(_brute.KnockbackResistancePercent, 50, "High knockback resistance.");
            CollectionAssert.Contains(_brute.SpawnTags, "heavy");
            Assert.IsNotNull(_attack);
            Assert.AreSame(_attack, _enemy.Attack);
            Assert.AreEqual(90, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(40, _enemy.XpValue);
            Assert.AreEqual(60, _enemy.Impact.Profile.StaggerResistancePercent);
        }

        [UnityTest]
        public IEnumerator Brute_UsesTheSlamWhenReady_ThenTheSwing_WithEachMovesOwnTelegraph()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(1.2f, 0f, 0f);
            yield return null; // Idle -> Chase
            yield return null; // Chase -> Telegraph: the slam (first in priority, ready)
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual("brute_ground_slam", _attack.PendingAttack.Id);
            Assert.AreEqual(Vector2.zero, _enemyObject.GetComponent<Rigidbody2D>().linearVelocity, "Stands still for the wind-up.");
            yield return new WaitForSeconds(0.9f);
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State, "Ground Slam telegraphs for 1.1 s.");
            Assert.AreEqual(0, _target.HitCount);
            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(1, _attack.AttacksStarted);
            Assert.AreEqual("brute_ground_slam", _attack.LastStartedAttack.Id);
            Assert.AreEqual(1, _target.HitCount, "One slam hit through the shared path.");
            Assert.AreEqual(25, _target.LastDamageAmount, "Integer band roll (fixed 25).");
            Assert.AreEqual(EnemyState.Recovery, _enemy.State);

            // Slam on cooldown (5 s): the next attack is the Heavy Swing with its 0.8 s telegraph.
            yield return new WaitForSeconds(1.5f); // slam recovery 1.4
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual("brute_heavy_swing", _attack.PendingAttack.Id);
            Assert.Greater(_attack.CooldownRemaining(_brute.Moveset.Single(a => a.Id == "brute_ground_slam")), 0f);
            yield return new WaitForSeconds(0.85f);
            Assert.AreEqual(2, _attack.AttacksStarted);
            Assert.AreEqual(2, _target.HitCount);
        }

        [UnityTest]
        public IEnumerator Brute_IsSlow_ResistsPushes_AndStopsOnDeath()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(12f, 0f, 0f);
            yield return null;
            for (var i = 0; i < 25; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            Assert.AreEqual(1.8f * 25 * Time.fixedDeltaTime, _enemyObject.transform.position.x, 0.25f, "Slow: 1.8 units/s.");

            // High resistances: a push that moves a grunt 2 units moves a brute 0.8; stagger pressure is cut to 40%.
            var result = _enemy.Impact.ApplyKnockback(new ImpactRequest(Vector2.right, 8f, 0f));
            Assert.AreEqual(0.8f, result.Distance, 1e-3f);
            var stagger = _enemy.Impact.ApplyStagger(new ImpactRequest(Vector2.right, 0f, 10f));
            Assert.AreEqual(4f, stagger.Applied, 1e-3f);
            Assert.IsFalse(stagger.Triggered, "A threshold-sized hit does not stagger a brute.");

            yield return new WaitForSeconds(0.3f);
            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(Vector2.zero, _enemyObject.GetComponent<Rigidbody2D>().linearVelocity);
            Assert.AreEqual(0, _attack.AttacksStarted);
        }
    }
}
