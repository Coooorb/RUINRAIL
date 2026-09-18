using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class MeleeWeaponTests
    {
        private const float WindUp = 0.08f;
        private const float Recovery = 0.12f;
        private const float AttackRate = 3.5f;
        private const float AttackInterval = 1f / AttackRate; // ~0.2857s, longer than WindUp + Recovery (0.2s)

        private GameObject _playerObject;
        private MeleeWeapon _weapon;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private MeleeWeaponDefinition _definition;
        private FixedDamageRoller _roller;
        private readonly List<GameObject> _spawnedObjects = new();

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _weapon = _playerObject.AddComponent<MeleeWeapon>();

            _input = new FakePlayerInputReader();
            _aiming.SetInputReader(_input);
            _weapon.SetInputReader(_input);

            _definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            SetDefinition(_definition, 14, 17, AttackRate, 1.2f, 80f, WindUp, Recovery, 0f, 0f);

            _roller = new FixedDamageRoller { FixedValue = 15 };

            _weapon.SetAiming(_aiming);
            _weapon.SetDamageRoller(_roller);
            _weapon.SetDefinition(_definition);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null)
            {
                Object.DestroyImmediate(_playerObject);
            }

            if (_definition != null)
            {
                Object.DestroyImmediate(_definition);
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

        private static void SetDefinition(
            MeleeWeaponDefinition definition, int damageMin, int damageMax, float attackRate,
            float attackRange, float attackArcDegrees, float windUp, float recovery, float knockback, float staggerPower)
        {
            SetField(definition, "_damageMin", damageMin);
            SetField(definition, "_damageMax", damageMax);
            SetField(definition, "_attackRate", attackRate);
            SetField(definition, "_attackRange", attackRange);
            SetField(definition, "_attackArcDegrees", attackArcDegrees);
            SetField(definition, "_windUpSeconds", windUp);
            SetField(definition, "_recoverySeconds", recovery);
            SetField(definition, "_knockback", knockback);
            SetField(definition, "_staggerPower", staggerPower);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            // Knockback/StaggerPower live on the WeaponDefinition base since TASK 050: walk the hierarchy.
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new System.MissingFieldException(target.GetType().Name, fieldName);
        }

        private TestDamageableTarget CreateTarget(Vector2 position)
        {
            var targetObject = new GameObject("TestTarget");
            targetObject.transform.position = position;
            var collider = targetObject.AddComponent<CircleCollider2D>();
            collider.isTrigger = true;
            collider.radius = 0.2f;
            _spawnedObjects.Add(targetObject);
            return targetObject.AddComponent<TestDamageableTarget>();
        }

        private static Vector2 PositionAtAngle(float angleDegrees, float distance)
        {
            var radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
        }

        private void AimRight()
        {
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.right;
        }

        private static IEnumerator RunAttackToCompletion()
        {
            yield return new WaitForSeconds(WindUp + 0.05f);
        }

        [UnityTest]
        public IEnumerator AttackDirection_FollowsFullAimDirection_NotQuantized()
        {
            const float angleDegrees = 20f;
            var direction = new Vector2(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), Mathf.Sin(angleDegrees * Mathf.Deg2Rad));
            _input.IsAimFromPointer = false;
            _input.Aim = direction;
            yield return null; // let PlayerAiming.Update() pick up the new aim direction

            var target = CreateTarget(PositionAtAngle(angleDegrees, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, target.HitCount,
                "A target placed exactly along a non-cardinal 20-degree aim direction should be hit.");
        }

        [UnityTest]
        public IEnumerator MovementDirection_DoesNotAffectMeleeAttackDirection()
        {
            _input.Move = Vector2.up;
            AimRight();
            yield return null;

            var targetAlongAim = CreateTarget(PositionAtAngle(0f, 0.6f));
            var targetAlongMovement = CreateTarget(PositionAtAngle(90f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, targetAlongAim.HitCount, "Attack should hit along the aim direction.");
            Assert.AreEqual(0, targetAlongMovement.HitCount, "Attack must not follow movement direction.");
        }

        [UnityTest]
        public IEnumerator TargetCenteredInArc_WithinRange_IsHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(0f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, target.HitCount);
        }

        [UnityTest]
        public IEnumerator TargetJustInsideArcBoundary_39Degrees_IsHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(39f, 1.0f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, target.HitCount, "A target at 39 degrees (inside the 40-degree half-angle) should be hit.");
        }

        [UnityTest]
        public IEnumerator TargetJustOutsideArcBoundary_41Degrees_IsNotHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(41f, 1.0f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(0, target.HitCount, "A target at 41 degrees (outside the 40-degree half-angle) should not be hit.");
        }

        [UnityTest]
        public IEnumerator TargetFarOutsideArc_IsNotHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(90f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(0, target.HitCount);
        }

        [UnityTest]
        public IEnumerator TargetInsideArc_ButBeyondRange_IsNotHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(0f, 1.5f)); // beyond 1.2 range

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(0, target.HitCount);
        }

        [UnityTest]
        public IEnumerator TargetBehindPlayer_WithinRange_IsNotHit()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(180f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(0, target.HitCount, "A target behind the player is outside the arc and must not be hit.");
        }

        [UnityTest]
        public IEnumerator Attacker_CannotHitItself()
        {
            AimRight();
            yield return null;

            var selfCollider = _playerObject.AddComponent<CircleCollider2D>();
            selfCollider.isTrigger = true;
            selfCollider.radius = 0.3f;
            var selfTarget = _playerObject.AddComponent<TestDamageableTarget>();

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(0, selfTarget.HitCount, "The attacker must never damage its own IDamageable.");
        }

        [UnityTest]
        public IEnumerator Damage_GoesThroughIDamageable_WithConfiguredRoll()
        {
            AimRight();
            yield return null;
            _roller.FixedValue = 16;
            var target = CreateTarget(PositionAtAngle(0f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, target.HitCount);
            Assert.AreEqual(16, target.LastDamageAmount);
        }

        [UnityTest]
        public IEnumerator NoDamage_DuringWindUp_BeforeActiveHitPoint()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(0f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            Assert.AreEqual(MeleeAttackState.WindUp, _weapon.State);
            Assert.AreEqual(0, target.HitCount, "No damage should be applied while still in wind-up.");

            yield return new WaitForSeconds(WindUp * 0.5f);
            Assert.AreEqual(0, target.HitCount, "No damage part-way through wind-up either.");

            yield return RunAttackToCompletion();
            Assert.AreEqual(1, target.HitCount, "Damage should be applied once the active hit point is reached.");
        }

        [UnityTest]
        public IEnumerator SameTarget_WithMultipleColliders_IsNotHitTwiceInOneSwing()
        {
            AimRight();
            yield return null;

            var targetObject = new GameObject("MultiColliderTarget");
            targetObject.transform.position = PositionAtAngle(0f, 0.6f);
            var colliderA = targetObject.AddComponent<CircleCollider2D>();
            colliderA.isTrigger = true;
            colliderA.radius = 0.2f;
            var colliderB = targetObject.AddComponent<BoxCollider2D>();
            colliderB.isTrigger = true;
            colliderB.size = new Vector2(0.3f, 0.3f);
            var target = targetObject.AddComponent<TestDamageableTarget>();
            _spawnedObjects.Add(targetObject);

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, target.HitCount,
                "A target with multiple overlapping colliders must only be damaged once per swing.");
        }

        [UnityTest]
        public IEnumerator TwoDifferentTargets_CanEachBeHitOnce_BySameSwing()
        {
            AimRight();
            yield return null;
            var targetA = CreateTarget(PositionAtAngle(10f, 0.5f));
            var targetB = CreateTarget(PositionAtAngle(-10f, 0.9f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();

            Assert.AreEqual(1, targetA.HitCount);
            Assert.AreEqual(1, targetB.HitCount);
        }

        [UnityTest]
        public IEnumerator SameTarget_CanBeHitAgain_ByLaterSeparateSwing()
        {
            AimRight();
            yield return null;
            var target = CreateTarget(PositionAtAngle(0f, 0.6f));

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();
            Assert.AreEqual(1, target.HitCount);

            yield return new WaitForSeconds(AttackInterval + 0.05f);

            Assert.IsTrue(_weapon.TryAttack());
            yield return RunAttackToCompletion();
            Assert.AreEqual(2, target.HitCount, "A later, separate swing should be able to hit the same target again.");
        }

        [UnityTest]
        public IEnumerator AttackRate_PreventsSecondAttackBeforeIntervalElapses_ThenAllowsAfter()
        {
            Assert.IsTrue(_weapon.TryAttack());

            Assert.IsFalse(_weapon.TryAttack(), "A second attack within the fire-rate interval must not succeed.");

            yield return new WaitForSeconds(AttackInterval + 0.05f);

            Assert.IsTrue(_weapon.TryAttack(), "An attack after the interval has elapsed should succeed.");
        }

        [UnityTest]
        public IEnumerator WindUpPlusRecovery_ShorterThanAttackInterval_CannotBypassAttackRate()
        {
            // WindUp (0.08s) + Recovery (0.12s) = 0.20s, which is shorter than the 3.5/s
            // attack interval (~0.2857s). The rate cap, not the lifecycle, must be binding.
            Assert.IsTrue(_weapon.TryAttack());

            yield return new WaitForSeconds(WindUp + Recovery + 0.02f); // lifecycle finished, back to Idle

            Assert.AreEqual(MeleeAttackState.Idle, _weapon.State,
                "Precondition: the wind-up/recovery lifecycle should have finished by now.");
            Assert.IsFalse(_weapon.TryAttack(),
                "Even though the lifecycle reached Idle, the attack-rate cooldown must still block a new attack.");

            yield return new WaitForSeconds(AttackInterval - (WindUp + Recovery + 0.02f) + 0.05f);

            Assert.IsTrue(_weapon.TryAttack(), "Once the full attack-rate interval has elapsed, a new attack should succeed.");
        }
    }
}
