using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class ProjectilePoolTests
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

        private static IEnumerator RunFixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Pool_ReusesReturnedInstance_ByIdentity()
        {
            var shortRangeData = new ProjectileSpawnData(5, 10f, 0.1f, 0f, 0f, Vector2.right);

            var first = _pool.Spawn(Vector2.zero, shortRangeData);
            yield return RunFixedSteps(10);
            Assert.IsFalse(first.gameObject.activeSelf, "Precondition: first projectile should have expired and returned.");

            var second = _pool.Spawn(Vector2.zero, shortRangeData);

            Assert.AreSame(first, second, "The pool should reuse the previously returned instance rather than creating a new one.");
        }

        [Test]
        public void Pool_GrowsWhenEmpty_InsteadOfFailingOrReusingAnActiveInstance()
        {
            var longRangeData = new ProjectileSpawnData(5, 10f, 100f, 0f, 0f, Vector2.right);

            var first = _pool.Spawn(Vector2.zero, longRangeData);
            var second = _pool.Spawn(Vector2.zero, longRangeData);

            Assert.IsNotNull(second);
            Assert.AreNotSame(first, second, "Spawning beyond the pool's initial size should create a new instance.");
            Assert.IsTrue(first.gameObject.activeSelf);
            Assert.IsTrue(second.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator ReusedProjectile_HasRuntimeStateFullyReset()
        {
            var shortRangeData = new ProjectileSpawnData(5, 10f, 0.1f, 1f, 1f, Vector2.right);

            var first = _pool.Spawn(Vector2.zero, shortRangeData);
            yield return RunFixedSteps(10);
            Assert.IsTrue(first.IsResolved, "Precondition: projectile should be resolved (expired) after its first use.");

            var longRangeData = new ProjectileSpawnData(9, 10f, 100f, 0f, 0f, Vector2.right);
            var second = _pool.Spawn(Vector2.zero, longRangeData);

            Assert.AreSame(first, second);
            Assert.IsFalse(second.IsResolved,
                "Reusing a pooled projectile must reset its resolved state, not carry it over from the previous activation.");

            var targetObject = new GameObject("TestTarget");
            targetObject.transform.position = new Vector3(1f, 0f, 0f);
            var collider = targetObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.3f;
            var target = targetObject.AddComponent<TestDamageableTarget>();
            _spawnedObjects.Add(targetObject);

            yield return RunFixedSteps(20);

            Assert.AreEqual(1, target.HitCount,
                "Reused projectile should behave like a fresh instance and be able to resolve a new hit.");
            Assert.AreEqual(9, target.LastDamageAmount);
        }
    }
}
