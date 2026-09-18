using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 069 — Sniper enemy: long telegraph whose aim tracks, locks, then fires a very fast visible projectile.</summary>
    public class SniperEnemyTests
    {
        private EnemyDefinition _sniper;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private EnemyProjectileAttack _attack;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _sniper = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/SniperEnemy.asset");
            Assert.IsNotNull(_sniper);
            _targetObject = new GameObject("Player");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(40f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("Sniper");
            _created.Add(_enemyObject);
            _enemyObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            _enemyObject.AddComponent<ProjectilePool>();
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 30 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_sniper);
            _attack = _enemyObject.GetComponent<EnemyProjectileAttack>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void Sniper_DataMatchesCombat44_AndIsAProjectileArchetype()
        {
            Assert.AreEqual("sniper_enemy", _sniper.Id);
            Assert.AreEqual(30, _sniper.BaseHealth);
            Assert.AreEqual((28, 34), (_sniper.DamageMin, _sniper.DamageMax));
            Assert.AreEqual(2.2f, _sniper.MoveSpeed, 0.001f);
            Assert.AreEqual(35, _sniper.BaseXp);
            Assert.AreEqual(11, _sniper.UnlockDepth, "Unlocks from Depth 11.");
            Assert.AreEqual(EnemyAttackKind.Projectile, _sniper.AttackKind, "A very fast projectile, never hitscan.");
            Assert.Greater(_sniper.ProjectileSpeed, 20f);
            Assert.LessOrEqual(_sniper.ProjectileSpeed, 34f, "Not faster than the player's sniper baseline.");
            Assert.GreaterOrEqual(_sniper.AttackTelegraphSeconds, 1f, "Long telegraph.");
            Assert.Greater(_sniper.AimLockSeconds, 0f, "The aim locks before the shot.");
            Assert.GreaterOrEqual((_sniper.AttackTelegraphSeconds - _sniper.AimLockSeconds) / EnemyController.MaxAttackSpeedMultiplier, 0.8f, "Tracking phase stays readable at the cap.");
            Assert.IsTrue(_sniper.KeepsDistance);
            CollectionAssert.Contains(_sniper.SpawnTags, "sniper");
            Assert.IsNotNull(_attack);
            Assert.AreSame(_attack, _enemy.Attack);
            Assert.AreEqual(30, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(35, _enemy.XpValue);
        }

        [UnityTest]
        public IEnumerator AimLine_TracksThenLocks_AndTheShotFollowsTheLockedLine()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(10f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(Vector2.right, _attack.AimDirection);
            Assert.IsFalse(_attack.IsAimLocked);

            // While tracking (first 1.0 s) the line follows the player.
            _targetObject.transform.position = new Vector3(10f, 4f, 0f);
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(_attack.IsAimLocked);
            var expected = new Vector2(10f, 4f).normalized;
            Assert.AreEqual(expected.x, _attack.AimDirection.x, 0.02f);
            Assert.AreEqual(expected.y, _attack.AimDirection.y, 0.02f, "Aim line tracks during the telegraph.");

            // After the lock (last 0.4 s) the player's movement no longer changes the line.
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(_attack.IsAimLocked, "Locked 0.4 s before the shot.");
            var locked = _attack.AimDirection;
            _targetObject.transform.position = new Vector3(10f, -4f, 0f);
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(locked, _attack.AimDirection, "Locked: no more tracking.");
            Assert.AreEqual(0, _attack.ShotsFired, "Nothing fired before the telegraph ends.");

            yield return new WaitForSeconds(0.25f);
            Assert.AreEqual(1, _attack.ShotsFired);
            var shot = _attack.SpawnedProjectiles.Single();
            Assert.AreEqual(locked.x, shot.Data.Direction.x, 0.01f);
            Assert.AreEqual(locked.y, shot.Data.Direction.y, 0.01f, "The shot leaves along the locked line, not toward the dodged player.");
            Assert.AreEqual(30f, shot.Data.Speed);
            Assert.AreEqual(30, shot.Data.Damage, "Integer band roll (fixed 30).");
            Assert.IsTrue(shot.gameObject.activeSelf, "Visible projectile.");
            for (var i = 0; i < 30; i++) yield return new WaitForFixedUpdate();
            Assert.AreEqual(0, _target.HitCount, "The dodge worked: the shot went to the old line.");
        }

        [UnityTest]
        public IEnumerator Sniper_HitsAStandingTarget_KeepsRange_AndStopsOnDeath()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(10f, 0f, 0f);
            yield return null;
            yield return null;
            yield return new WaitForSeconds(1.45f);
            Assert.AreEqual(1, _attack.ShotsFired);
            for (var i = 0; i < 25; i++) yield return new WaitForFixedUpdate(); // 10 tiles at 30 u/s
            Assert.AreEqual(1, _target.HitCount);
            Assert.AreEqual(30, _target.LastDamageAmount);

            // Too close: repositions away before another shot.
            _targetObject.transform.position = _enemyObject.transform.position + new Vector3(3f, 0f, 0f);
            yield return new WaitForSeconds(3.1f); // cooldown 3.0
            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            var x = _enemyObject.transform.position.x;
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.Less(_enemyObject.transform.position.x, x - 0.2f, "Backs away at 2.2 u/s inside the preferred 8 tiles.");

            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            Assert.IsFalse(_attack.IsAimLocked);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(1, _attack.ShotsFired, "A dead sniper never fires again.");
        }
    }
}
