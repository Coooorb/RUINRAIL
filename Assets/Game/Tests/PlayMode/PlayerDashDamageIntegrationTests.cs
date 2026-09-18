using System.Collections;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class PlayerDashDamageIntegrationTests
    {
        private GameObject _playerObject;
        private PlayerDash _dash;
        private HealthComponent _health;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _playerObject.AddComponent<Rigidbody2D>();
            _dash = _playerObject.AddComponent<PlayerDash>();
            _health = _playerObject.AddComponent<HealthComponent>();

            _input = new FakePlayerInputReader();
            _config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();

            _dash.SetInputReader(_input);
            _dash.SetBalanceConfig(_config);
            _health.SetInvulnerabilityState(_dash);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null)
            {
                Object.Destroy(_playerObject);
            }

            if (_config != null)
            {
                Object.Destroy(_config);
            }
        }

        private static IEnumerator RunFixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [Test]
        public void Damage_IsRejected_WhileDashIFrameWindowIsActive()
        {
            _input.Move = Vector2.right;
            _input.RaiseDash();
            Assert.IsTrue(_dash.IsInvulnerable);

            var applied = _health.TryApplyDamage(new DamageRequest(20));

            Assert.IsFalse(applied, "Damage should be rejected while the dash iFrame window is active.");
            Assert.AreEqual(_health.MaxHealth, _health.CurrentHealth);
        }

        [UnityTest]
        public IEnumerator Damage_Succeeds_AfterIFrameWindowEnds_WhileDashMovementStillActive()
        {
            // Uses the unmodified default config: DashDuration = 0.18s, DashIFrameDuration = 0.10s.
            _input.Move = Vector2.right;
            _input.RaiseDash();

            yield return RunFixedSteps(Mathf.RoundToInt(_config.DashIFrameDuration / Time.fixedDeltaTime) + 2);

            Assert.IsFalse(_dash.IsInvulnerable, "The 0.10s iFrame window should have ended.");
            Assert.IsTrue(_dash.IsDashing, "The 0.18s dash movement should still be active.");

            var applied = _health.TryApplyDamage(new DamageRequest(20));

            Assert.IsTrue(applied, "Damage should succeed once iFrames end, even while dash movement continues.");
            Assert.AreEqual(_health.MaxHealth - 20, _health.CurrentHealth);
        }
    }
}
