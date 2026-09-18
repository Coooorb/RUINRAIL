using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Tests
{
    public class HealthComponentTests
    {
        private GameObject _healthObject;
        private HealthComponent _health;

        [SetUp]
        public void SetUp()
        {
            _healthObject = new GameObject("TestHealth");
            _health = _healthObject.AddComponent<HealthComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_healthObject != null)
            {
                Object.Destroy(_healthObject);
            }
        }

        [Test]
        public void DefaultMaxHealth_Is100_AndInitializesToFull()
        {
            Assert.AreEqual(100, _health.MaxHealth);
            Assert.AreEqual(100, _health.CurrentHealth);
            Assert.IsTrue(_health.IsAlive);
        }

        [Test]
        public void TryApplyDamage_ReducesHealthByExactIntegerAmount()
        {
            var applied = _health.TryApplyDamage(new DamageRequest(30));

            Assert.IsTrue(applied);
            Assert.AreEqual(70, _health.CurrentHealth);
        }

        [Test]
        public void TryApplyDamage_ClampsAtZero_DoesNotGoNegative()
        {
            _health.TryApplyDamage(new DamageRequest(500));

            Assert.AreEqual(0, _health.CurrentHealth);
            Assert.IsFalse(_health.IsAlive);
        }

        [Test]
        public void Heal_CannotExceedMaxHealth()
        {
            _health.TryApplyDamage(new DamageRequest(10));

            var healed = _health.Heal(1000);

            Assert.IsTrue(healed);
            Assert.AreEqual(100, _health.CurrentHealth);
        }

        [Test]
        public void ZeroOrNegativeDamage_IsRejectedAndHasNoEffect()
        {
            var eventFired = false;
            _health.Damaged += _ => eventFired = true;

            var resultZero = _health.TryApplyDamage(new DamageRequest(0));
            var resultNegative = _health.TryApplyDamage(new DamageRequest(-5));

            Assert.IsFalse(resultZero);
            Assert.IsFalse(resultNegative);
            Assert.AreEqual(100, _health.CurrentHealth);
            Assert.IsFalse(eventFired);
        }

        [Test]
        public void Death_OccursExactlyWhenHealthReachesZero()
        {
            var died = false;
            _health.Died += () => died = true;

            _health.TryApplyDamage(new DamageRequest(50));
            Assert.IsFalse(died, "Should not be dead at 50/100 health.");

            _health.TryApplyDamage(new DamageRequest(50));
            Assert.IsTrue(died);
            Assert.IsFalse(_health.IsAlive);
        }

        [Test]
        public void Death_DoesNotTriggerMultipleTimes_FromRepeatedDamageAfterDeath()
        {
            var deathCount = 0;
            _health.Died += () => deathCount++;

            _health.TryApplyDamage(new DamageRequest(200));
            Assert.AreEqual(1, deathCount);

            var secondHitApplied = _health.TryApplyDamage(new DamageRequest(10));

            Assert.IsFalse(secondHitApplied, "Damage after death should not be applied.");
            Assert.AreEqual(1, deathCount, "Died must not fire again from damage received after death.");
        }

        [Test]
        public void TryApplyDamage_IsRejected_WhileInvulnerabilityStateIsActive()
        {
            var invulnerability = new FakeInvulnerabilityState { IsInvulnerable = true };
            _health.SetInvulnerabilityState(invulnerability);

            var applied = _health.TryApplyDamage(new DamageRequest(20));

            Assert.IsFalse(applied);
            Assert.AreEqual(100, _health.CurrentHealth);
        }

        [Test]
        public void TryApplyDamage_Succeeds_OnceInvulnerabilityEnds()
        {
            var invulnerability = new FakeInvulnerabilityState { IsInvulnerable = true };
            _health.SetInvulnerabilityState(invulnerability);

            _health.TryApplyDamage(new DamageRequest(20));
            Assert.AreEqual(100, _health.CurrentHealth, "Damage should be blocked while invulnerable.");

            invulnerability.IsInvulnerable = false;
            var applied = _health.TryApplyDamage(new DamageRequest(20));

            Assert.IsTrue(applied);
            Assert.AreEqual(80, _health.CurrentHealth);
        }
    }
}
