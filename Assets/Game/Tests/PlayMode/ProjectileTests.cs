using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class ProjectileTests
    {
        private GameObject _poolObject;
        private ProjectilePool _pool;
        private readonly List<GameObject> _spawnedObjects = new();

        [SetUp]
        public void SetUp()
        {
            _poolObject = new GameObject("TestProjectilePool");
            _pool = _poolObject.AddComponent<ProjectilePool>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_poolObject != null)
            {
                Object.DestroyImmediate(_poolObject);
            }

            foreach (var spawnedObject in _spawnedObjects)
            {
                if (spawnedObject != null)
                {
                    Object.DestroyImmediate(spawnedObject);
                }
            }

            _spawnedObjects.Clear();
        }

        private TestDamageableTarget CreateTarget(Vector2 position)
        {
            var targetObject = new GameObject("TestTarget");
            targetObject.transform.position = position;
            var collider = targetObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;
            _spawnedObjects.Add(targetObject);
            return targetObject.AddComponent<TestDamageableTarget>();
        }

        private static IEnumerator RunFixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Projectile_TravelsInConfiguredDirection()
        {
            var data = new ProjectileSpawnData(5, 10f, 100f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(10);

            Assert.Greater(projectile.transform.position.x, 0f);
            Assert.AreEqual(0f, projectile.transform.position.y, 0.01f);
        }

        [UnityTest]
        public IEnumerator Speed_ComesFromSuppliedRuntimeData()
        {
            const float speed = 8f;
            var data = new ProjectileSpawnData(5, speed, 100f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            const int steps = 20;
            var elapsed = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            Assert.AreEqual(speed * elapsed, projectile.transform.position.x, 0.1f);
        }

        [UnityTest]
        public IEnumerator DiagonalDirection_IsNormalized()
        {
            const float speed = 10f;
            var data = new ProjectileSpawnData(5, speed, 100f, 0f, 0f, new Vector2(1f, 1f));
            var projectile = _pool.Spawn(Vector2.zero, data);

            const int steps = 20;
            var elapsed = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            var traveled = ((Vector2)projectile.transform.position).magnitude;
            Assert.AreEqual(speed * elapsed, traveled, 0.1f,
                "Unnormalized diagonal input should not make the projectile travel faster than its configured speed.");
        }

        [UnityTest]
        public IEnumerator Projectile_ExpiresAtConfiguredMaxRange()
        {
            var data = new ProjectileSpawnData(5, 10f, 1f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(20);

            Assert.IsFalse(projectile.gameObject.activeSelf, "Projectile should have expired and returned to the pool.");
        }

        [UnityTest]
        public IEnumerator Projectile_ExpiresViaLifetimeSafetyLimit_WhenDistanceNeverAccumulates()
        {
            var data = new ProjectileSpawnData(5, 0f, 1000f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);
            projectile.SetMaxLifetimeSeconds(0.05f);

            yield return RunFixedSteps(10);

            Assert.IsFalse(projectile.gameObject.activeSelf,
                "A projectile that never accumulates range (e.g. speed 0) must still expire via the lifetime safety limit.");
        }

        [UnityTest]
        public IEnumerator ValidHit_AppliesExactDamageOnceAndReturnsProjectileToPool()
        {
            var target = CreateTarget(new Vector2(0.5f, 0f));
            var data = new ProjectileSpawnData(7, 10f, 100f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(5);

            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(7, target.LastDamageAmount);
            Assert.IsFalse(projectile.gameObject.activeSelf, "Projectile should return to the pool after a terminal hit.");
        }

        [Test]
        public void Hit_AppliesDamageExactlyOnce_EvenWithDuplicateTriggerCallback()
        {
            var target = CreateTarget(Vector2.zero);
            var targetCollider = target.GetComponent<Collider2D>();

            var data = new ProjectileSpawnData(7, 10f, 100f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            var onTriggerEnter = typeof(Projectile).GetMethod("OnTriggerEnter2D", BindingFlags.NonPublic | BindingFlags.Instance);
            onTriggerEnter.Invoke(projectile, new object[] { targetCollider });
            onTriggerEnter.Invoke(projectile, new object[] { targetCollider });

            Assert.AreEqual(1, target.HitCount,
                "A second, duplicate collision callback must not apply damage again.");
        }

        [UnityTest]
        public IEnumerator ExpiredProjectile_ReturnsToPoolWithoutDealingDamage()
        {
            var target = CreateTarget(new Vector2(50f, 0f));
            var data = new ProjectileSpawnData(7, 10f, 0.5f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(10);

            Assert.IsFalse(projectile.gameObject.activeSelf);
            Assert.AreEqual(0, target.HitCount);
        }

        [UnityTest]
        public IEnumerator Projectile_DoesNotDamageItsOwnSource_ButCanDamageOtherTargets()
        {
            var sourceObject = new GameObject("SourceObject");
            sourceObject.transform.position = Vector2.zero;
            var sourceCollider = sourceObject.AddComponent<CircleCollider2D>();
            sourceCollider.isTrigger = true;
            sourceCollider.radius = 0.3f;
            var sourceTarget = sourceObject.AddComponent<TestDamageableTarget>();
            _spawnedObjects.Add(sourceObject);

            var data = new ProjectileSpawnData(7, 10f, 100f, 0f, 0f, Vector2.right, source: sourceObject);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(5);

            Assert.AreEqual(0, sourceTarget.HitCount, "Projectile should not damage its own source.");
            Assert.IsTrue(projectile.gameObject.activeSelf, "Projectile should still be travelling after ignoring its source.");

            var otherTarget = CreateTarget(new Vector2(2f, 0f));
            yield return RunFixedSteps(30);

            Assert.AreEqual(1, otherTarget.HitCount, "Non-source targets should still be damageable.");
        }

        [UnityTest]
        public IEnumerator Projectile_TerminatesOnEnvironmentObstacle_WithoutDealingDamage()
        {
            var wallObject = new GameObject("TestWall");
            wallObject.transform.position = new Vector3(1f, 0f, 0f);
            var wallCollider = wallObject.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.5f, 3f);
            wallObject.AddComponent<EnvironmentObstacle>();
            _spawnedObjects.Add(wallObject);

            var data = new ProjectileSpawnData(5, 20f, 100f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(15);

            Assert.IsFalse(projectile.gameObject.activeSelf, "Projectile should be deactivated by the obstacle.");
            Assert.Less(projectile.transform.position.x, 1.4f, "Projectile should not pass through the obstacle.");
        }

        [UnityTest]
        public IEnumerator Projectile_PassesThrough_NonObstacleNonDamageableTrigger()
        {
            var triggerObject = new GameObject("SomeFutureTrigger");
            triggerObject.transform.position = new Vector3(1f, 0f, 0f);
            var collider = triggerObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;
            _spawnedObjects.Add(triggerObject);

            var data = new ProjectileSpawnData(5, 20f, 5f, 0f, 0f, Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            yield return RunFixedSteps(15);

            Assert.Greater(projectile.transform.position.x, 1.5f,
                "A trigger that is neither IDamageable nor an EnvironmentObstacle must not block the projectile.");
        }

        [Test]
        public void SpawnData_KnockbackAndStagger_SurviveActivation()
        {
            var data = new ProjectileSpawnData(7, 10f, 100f, knockback: 3.5f, staggerPower: 2.25f, direction: Vector2.right);
            var projectile = _pool.Spawn(Vector2.zero, data);

            Assert.AreEqual(7, projectile.Data.Damage);
            Assert.AreEqual(3.5f, projectile.Data.Knockback, 0.001f);
            Assert.AreEqual(2.25f, projectile.Data.StaggerPower, 0.001f);
        }
    }
}
