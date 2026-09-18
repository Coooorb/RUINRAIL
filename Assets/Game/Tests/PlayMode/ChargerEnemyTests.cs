using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 065 — Charger archetype: 0.7 s direction telegraph, straight-line charge, wall-safe stop and recovery.</summary>
    public class ChargerEnemyTests
    {
        private EnemyDefinition _charger;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private EnemyChargeAttack _attack;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _charger = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Charger.asset");
            Assert.IsNotNull(_charger);
            _targetObject = new GameObject("Target");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(30f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("Charger");
            _created.Add(_enemyObject);
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 20 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_charger);
            _attack = _enemyObject.GetComponent<EnemyChargeAttack>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private GameObject Wall(Vector2 position)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.4f, 6f);
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        [Test]
        public void Charger_DataMatchesCombat44_AndComposesTheChargeBehaviour()
        {
            Assert.AreEqual("charger", _charger.Id);
            Assert.AreEqual(45, _charger.BaseHealth);
            Assert.AreEqual((18, 22), (_charger.DamageMin, _charger.DamageMax));
            Assert.AreEqual(2.7f, _charger.MoveSpeed, 0.001f);
            Assert.AreEqual(25, _charger.BaseXp);
            Assert.AreEqual(3, _charger.UnlockDepth, "Unlocks from Depth 3.");
            Assert.AreEqual(EnemyAttackKind.Charge, _charger.AttackKind);
            Assert.IsNotNull(_charger.ChargeAttack);
            Assert.AreEqual(AttackMotion.Dash, _charger.ChargeAttack.Motion);
            Assert.AreEqual((18, 22), (_charger.ChargeAttack.DamageMin, _charger.ChargeAttack.DamageMax), "The charge carries the core damage.");
            Assert.AreEqual(0.7f, _charger.AttackTelegraphSeconds, 0.001f, "44: roughly 0.7 s direction telegraph.");
            Assert.GreaterOrEqual(_charger.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.6f, "Readable at the deepest attack-speed scale.");
            CollectionAssert.Contains(_charger.SpawnTags, "charger");
            Assert.IsNotNull(_attack);
            Assert.AreSame(_attack, _enemy.Attack);
            Assert.AreEqual(45, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(25, _enemy.XpValue);
        }

        [UnityTest]
        public IEnumerator Charger_LocksDirectionAtTelegraph_ChargesStraight_HitsOnce_ThenRecovers()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(4f, 0f, 0f);
            yield return null; // Idle -> Chase
            yield return null; // Chase -> Telegraph (4 within 1.5..6)
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(Vector2.right, _attack.LockedDirection);
            Assert.AreEqual(Vector2.zero, _enemyObject.GetComponent<Rigidbody2D>().linearVelocity, "Stops for the telegraph.");

            // The target sidesteps during the telegraph: the charge still goes where it was aimed.
            _targetObject.transform.position = new Vector3(4f, 3f, 0f);
            yield return new WaitForSeconds(0.75f);
            Assert.AreEqual(1, _attack.ChargesStarted);
            Assert.IsTrue(_attack.IsResolving);
            Assert.AreEqual(EnemyState.Recovery, _enemy.State);
            yield return new WaitForSeconds(0.6f); // 6 tiles at 12 u/s = 0.5 s
            Assert.IsFalse(_attack.IsResolving);
            Assert.AreEqual(6f, _enemyObject.transform.position.x, 0.6f, "Straight line, full distance on a miss.");
            Assert.AreEqual(0f, _enemyObject.transform.position.y, 0.1f);
            Assert.AreEqual(0, _target.HitCount, "A sidestep dodges the charge.");
            Assert.AreEqual(EnemyState.Recovery, _enemy.State, "A miss leaves the recovery window.");
            _targetObject.transform.position = new Vector3(40f, 0f, 0f); // out of range so recovery ends into a plain chase
            yield return new WaitForSeconds(1.1f);
            Assert.AreEqual(EnemyState.Chase, _enemy.State);

            // A charge through the target hits it exactly once with the integer band.
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(3f, 0f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(Vector2.right, _attack.LockedDirection);
            yield return new WaitForSeconds(0.75f);
            Assert.AreEqual(2, _attack.ChargesStarted);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(1, _target.HitCount, "Hit once while being run through.");
            Assert.AreEqual(20, _target.LastDamageAmount);
        }

        [UnityTest]
        public IEnumerator Charger_StopsAtWalls_AndDeathCancelsTheCharge()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(4f, 0f, 0f);
            Wall(new Vector2(2.5f, 0f));
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            yield return new WaitForSeconds(0.75f);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(_attack.IsResolving);
            Assert.IsTrue(_attack.LastChargeStoppedByWall, "Hitting a wall ends the charge early.");
            Assert.LessOrEqual(_enemyObject.transform.position.x, 2.4f, "Never clips through the wall.");
            Assert.AreEqual(0, _target.HitCount);
            Assert.AreEqual(EnemyState.Recovery, _enemy.State, "Clear recovery window after the wall.");

            // Death mid-charge cancels the motion.
            _targetObject.transform.position = new Vector3(40f, 0f, 0f);
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            _enemyObject.transform.position = new Vector3(-6f, 4f, 0f);
            _targetObject.transform.position = new Vector3(-2f, 4f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForSeconds(0.1f);
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            yield return new WaitForSeconds(0.75f);
            Assert.IsTrue(_attack.IsResolving);
            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            Assert.IsFalse(_attack.IsResolving, "Death cancels the charge.");
            var x = _enemyObject.transform.position.x;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(x, _enemyObject.transform.position.x, 0.05f, "A dead charger does not keep sliding.");
        }
    }
}
