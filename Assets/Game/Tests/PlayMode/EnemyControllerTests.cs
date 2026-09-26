using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Enemies;
using RuinRail.Presentation.Animation;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class EnemyControllerTests
    {
        private const float Telegraph = 0.1f;
        private const float Cooldown = 0.15f;

        private GameObject _enemyObject;
        private EnemyController _enemy;
        private HealthComponent _enemyHealth;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private EnemyDefinition _definition;
        private FixedDamageRoller _roller;

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            SetField("_id", "grunt");
            SetField("_baseHealth", 30);
            SetField("_damageMin", 6);
            SetField("_damageMax", 8);
            SetField("_moveSpeed", 3f);
            SetField("_baseXp", 12);
            SetField("_attackRange", 1f);
            SetField("_attackTelegraphSeconds", Telegraph);
            SetField("_attackCooldownSeconds", Cooldown);

            _targetObject = new GameObject("TestTarget");
            _targetObject.transform.position = new Vector3(10f, 0f, 0f); // out of range until a test positions it
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _roller = new FixedDamageRoller { FixedValue = 7 };

            _enemyObject = new GameObject("TestGrunt");
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemyHealth = _enemyObject.GetComponent<HealthComponent>();
            _enemy.SetDamageRoller(_roller);
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_enemyObject != null) Object.DestroyImmediate(_enemyObject);
            if (_targetObject != null) Object.DestroyImmediate(_targetObject);
            if (_definition != null) Object.DestroyImmediate(_definition);
        }

        private void SetField(string name, object value)
        {
            typeof(EnemyDefinition).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_definition, value);
        }

        [Test]
        public void Grunt_SpawnsWithApprovedHealthAndXp()
        {
            Assert.AreEqual(30, _enemyHealth.MaxHealth);
            Assert.AreEqual(30, _enemyHealth.CurrentHealth);
            Assert.AreEqual(12, _enemy.XpValue);
            Assert.IsTrue(_enemy.IsAlive);
        }

        [UnityTest]
        public IEnumerator Grunt_PursuesTargetAtConfiguredSpeed()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(10f, 0f, 0f);

            const int steps = 25;
            var elapsed = steps * Time.fixedDeltaTime;
            yield return null;
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            Assert.AreEqual(3f * elapsed, _enemyObject.transform.position.x, 0.25f, "Grunt should move toward the target at 3.0 units/s.");
            Assert.AreEqual(0f, _enemyObject.transform.position.y, 0.05f);
        }

        [UnityTest]
        public IEnumerator Grunt_TelegraphsThenAttacks_WithIntegerDamageThroughIDamageable()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(0.5f, 0f, 0f);

            yield return null; // Idle -> Chase
            yield return null; // Chase -> Telegraph (in range)
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(0, _target.HitCount, "No damage during telegraph.");

            yield return new WaitForSeconds(Telegraph + 0.05f);

            Assert.AreEqual(1, _target.HitCount);
            Assert.AreEqual(7, _target.LastDamageAmount);
            Assert.AreEqual(EnemyState.Recovery, _enemy.State);
        }

        [Test]
        public void GruntDamageRoll_IsIntegerWithinApprovedRange_WithRealRoller()
        {
            var roller = new UnityRandomDamageRoller();
            for (var i = 0; i < 200; i++)
            {
                var damage = roller.Roll(_definition.DamageMin, _definition.DamageMax);
                Assert.GreaterOrEqual(damage, 6);
                Assert.LessOrEqual(damage, 8);
            }
        }

        [UnityTest]
        public IEnumerator Grunt_RespectsAttackCooldown_BetweenAttacks()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(0.5f, 0f, 0f);

            yield return new WaitForSeconds(Telegraph + 0.08f);
            Assert.AreEqual(1, _target.HitCount);

            yield return new WaitForSeconds(Cooldown * 0.5f);
            Assert.AreEqual(1, _target.HitCount, "No second attack during recovery/cooldown.");

            yield return new WaitForSeconds(Cooldown * 0.6f + Telegraph + 0.08f);
            Assert.AreEqual(2, _target.HitCount, "A second attack should occur after cooldown and a new telegraph.");
        }

        [UnityTest]
        public IEnumerator Grunt_DeathFiresOnce_AndNoAttacksAfterDeath()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(0.5f, 0f, 0f);
            var deathCount = 0;
            EnemyController diedEnemy = null;
            _enemy.Died += e => { deathCount++; diedEnemy = e; };

            yield return null;

            Assert.IsTrue(_enemyHealth.TryApplyDamage(new DamageRequest(30)));
            Assert.AreEqual(1, deathCount);
            Assert.AreSame(_enemy, diedEnemy);
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            Assert.IsFalse(_enemy.IsAlive);

            _enemyHealth.TryApplyDamage(new DamageRequest(10));
            Assert.AreEqual(1, deathCount, "Death must not fire twice.");

            yield return new WaitForSeconds(Telegraph + Cooldown + 0.2f);
            Assert.AreEqual(0, _target.HitCount, "A dead Grunt must not attack.");
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
        }

        [UnityTest]
        public IEnumerator Grunt_Corpse_StaysWhereAndHowItDied_UnderBodyKnockbackBlastAndAiPressure()
        {
            var config = StaggerConfig.Create(threshold: 10f, recoveryPerSecond: 5f, staggerDuration: 0.3f, immunity: 0.5f, unitsPerPoint: 0.25f, maxDistance: 4f, knockbackDuration: 0.3f);
            var pusher = new GameObject("CorpsePusher");
            try
            {
                _enemyObject.AddComponent<CircleCollider2D>().radius = 0.35f;
                _enemy.SetStaggerConfig(config);
                var driver = _enemyObject.AddComponent<EnemyAnimationDriver>();
                var body = _enemyObject.GetComponent<Rigidbody2D>();
                _enemyObject.transform.position = Vector3.zero;
                _targetObject.transform.position = new Vector3(10f, 0f, 0f);

                // Alive and chasing east, then a knockback is still in flight at the moment of death.
                var chaseUntil = Time.time + 0.15f;
                while (Time.time < chaseUntil) yield return new WaitForFixedUpdate();
                Assert.Greater(_enemyObject.transform.position.x, 0.05f, "Precondition: the live Grunt moves.");
                ImpactDispatcher.Apply(_enemyObject.GetComponent<Collider2D>(), new ImpactRequest(Vector2.up, 8f, 0f, DamageKind.Normal, null, null));
                Assert.IsTrue(_enemy.GetComponent<ImpactReceiver>().IsKnockbackActive, "Precondition: knockback in flight.");
                Assert.IsTrue(_enemyHealth.TryApplyDamage(new DamageRequest(30)));
                yield return null;

                var deathPosition = _enemyObject.transform.position;
                var deathRotation = _enemyObject.transform.rotation;
                var deathFacing = driver.Facing;

                // A body walks straight through where the corpse lies; the target circles behind it (AI/facing pressure);
                // a blast and a direct knockback hit it.
                pusher.transform.position = deathPosition + new Vector3(-1.5f, 0f, 0f);
                var pusherBody = pusher.AddComponent<Rigidbody2D>();
                pusherBody.gravityScale = 0f;
                pusher.AddComponent<CircleCollider2D>().radius = 0.35f;
                _targetObject.transform.position = deathPosition + new Vector3(-3f, -3f, 0f);
                ShockwaveResolver.Emit((Vector2)deathPosition + new Vector2(0.3f, 0.3f), 3f, 12f, 30f, DamageTeam.Player);
                ImpactDispatcher.Apply(_enemyObject.GetComponent<Collider2D>(), new ImpactRequest(Vector2.left, 12f, 30f, DamageKind.Normal, null, null));

                var pressureUntil = Time.time + 0.6f;
                while (Time.time < pressureUntil)
                {
                    pusherBody.linearVelocity = ((Vector2)deathPosition + Vector2.right * 1.5f - pusherBody.position).normalized * 6f;
                    yield return new WaitForFixedUpdate();
                }

                Assert.AreEqual(0f, Vector3.Distance(deathPosition, _enemyObject.transform.position), 1e-5f, "The corpse must not move after death.");
                Assert.AreEqual(0f, Quaternion.Angle(deathRotation, _enemyObject.transform.rotation), 1e-3f, "The corpse must not turn after death.");
                Assert.AreEqual(deathFacing, driver.Facing, "The corpse keeps the facing it died with.");
                Assert.AreEqual(EnemyAnimState.Death, driver.State);
                Assert.AreEqual(Vector2.zero, body.linearVelocity);
                Assert.IsFalse(_enemy.GetComponent<ImpactReceiver>().IsKnockbackActive);
                Assert.Greater(pusherBody.position.x, deathPosition.x, "Precondition: the body really crossed the corpse.");
                Assert.IsTrue(_enemyObject.activeInHierarchy, "The corpse stays present under the existing lifetime rules.");
            }
            finally
            {
                Object.DestroyImmediate(pusher);
                Object.DestroyImmediate(config);
            }
        }

        [UnityTest]
        public IEnumerator Grunt_WithoutTarget_ReturnsToIdle_AndDoesNotCrash()
        {
            _enemy.SetTarget(null);
            yield return null;
            yield return null;

            Assert.AreEqual(EnemyState.Idle, _enemy.State);
            Assert.AreEqual(0, _target.HitCount);
        }
    }
}
