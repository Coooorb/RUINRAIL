using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class PlayerDashTests
    {
        private GameObject _playerObject;
        private Rigidbody2D _rigidbody2D;
        private PlayerMovement _movement;
        private PlayerDash _dash;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _rigidbody2D = _playerObject.AddComponent<Rigidbody2D>();
            _movement = _playerObject.AddComponent<PlayerMovement>();
            _dash = _playerObject.AddComponent<PlayerDash>();

            _input = new FakePlayerInputReader();
            _config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();

            _movement.SetInputReader(_input);
            _movement.SetBalanceConfig(_config);
            _movement.SetMovementOverride(_dash);

            _dash.SetInputReader(_input);
            _dash.SetBalanceConfig(_config);
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

        private static void SetFloat(PlayerBalanceConfig config, string fieldName, float value)
        {
            var field = typeof(PlayerBalanceConfig).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(config, value);
        }

        private static IEnumerator RunFixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [Test]
        public void Dash_StartsResponsively_FromValidMovementInput()
        {
            _input.Move = Vector2.right;

            _input.RaiseDash();

            Assert.IsTrue(_dash.IsDashing);
            Assert.AreEqual(Vector2.right, _dash.DashDirection);
        }

        [Test]
        public void DashDirection_FollowsMovementInput_NotAimInput()
        {
            _input.Move = Vector2.up;
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.right;

            _input.RaiseDash();

            Assert.AreEqual(Vector2.up, _dash.DashDirection);
        }

        [Test]
        public void DiagonalDashDirection_IsNormalized()
        {
            _input.Move = new Vector2(1f, 1f);

            _input.RaiseDash();

            Assert.AreEqual(1f, _dash.DashDirection.magnitude, 0.0001f);
            var expected = new Vector2(1f, 1f).normalized;
            Assert.AreEqual(expected.x, _dash.DashDirection.x, 0.0001f);
            Assert.AreEqual(expected.y, _dash.DashDirection.y, 0.0001f);
        }

        [Test]
        public void ZeroMovementInput_DoesNotStartDash()
        {
            _input.Move = Vector2.zero;

            _input.RaiseDash();

            Assert.IsFalse(_dash.IsDashing);
        }

        [Test]
        public void SecondDash_CannotStartWhileAlreadyDashing()
        {
            _input.Move = Vector2.right;
            _input.RaiseDash();
            Assert.IsTrue(_dash.IsDashing);

            _input.Move = Vector2.up;
            _input.RaiseDash();

            Assert.AreEqual(Vector2.right, _dash.DashDirection,
                "A dash attempt while already dashing must not redirect the active dash.");
        }

        [UnityTest]
        public IEnumerator Dash_LastsConfiguredDuration()
        {
            SetFloat(_config, "_dashDuration", 0.1f);

            _input.Move = Vector2.right;
            _input.RaiseDash();

            var totalSteps = Mathf.RoundToInt(0.1f / Time.fixedDeltaTime);
            yield return RunFixedSteps(totalSteps - 1);
            Assert.IsTrue(_dash.IsDashing, "Dash should still be active just before its configured duration elapses.");

            yield return RunFixedSteps(2);
            Assert.IsFalse(_dash.IsDashing, "Dash should have ended at its configured duration.");
        }

        [UnityTest]
        public IEnumerator SecondDash_CannotStartDuringCooldown()
        {
            SetFloat(_config, "_dashDuration", 0.06f);
            SetFloat(_config, "_dashCooldown", 0.5f);

            _input.Move = Vector2.right;
            _input.RaiseDash();
            yield return RunFixedSteps(Mathf.RoundToInt(0.06f / Time.fixedDeltaTime) + 2);
            Assert.IsFalse(_dash.IsDashing, "Precondition: first dash should have ended.");

            _input.Move = Vector2.up;
            _input.RaiseDash();

            Assert.IsFalse(_dash.IsDashing, "Dash should not restart while cooldown is still active.");
        }

        [UnityTest]
        public IEnumerator Dash_CanStartAgain_AfterCooldownExpires()
        {
            SetFloat(_config, "_dashDuration", 0.04f);
            SetFloat(_config, "_dashCooldown", 0.1f);

            _input.Move = Vector2.right;
            _input.RaiseDash();
            yield return RunFixedSteps(Mathf.RoundToInt(0.1f / Time.fixedDeltaTime) + 3);
            Assert.IsFalse(_dash.IsDashing, "Precondition: cooldown should have fully expired.");

            _input.Move = Vector2.up;
            _input.RaiseDash();

            Assert.IsTrue(_dash.IsDashing, "Dash should be able to start again once cooldown has expired.");
            Assert.AreEqual(Vector2.up, _dash.DashDirection);
        }

        [UnityTest]
        public IEnumerator IFrames_ActiveOnlyDuringConfiguredWindow_AndCanEndBeforeDash()
        {
            SetFloat(_config, "_dashDuration", 0.2f);
            SetFloat(_config, "_dashIFrameDuration", 0.04f);

            Assert.IsFalse(_dash.IsInvulnerable, "Precondition: not invulnerable before any dash.");

            _input.Move = Vector2.right;
            _input.RaiseDash();
            Assert.IsTrue(_dash.IsInvulnerable, "iFrames should activate immediately when the dash starts.");

            yield return RunFixedSteps(Mathf.RoundToInt(0.04f / Time.fixedDeltaTime) + 2);

            Assert.IsFalse(_dash.IsInvulnerable, "iFrame window should have ended.");
            Assert.IsTrue(_dash.IsDashing, "Dash should still be active after the shorter iFrame window ends.");
        }

        [UnityTest]
        public IEnumerator DefaultIFrameWindow_EndsBeforeDefaultDashMovementEnds()
        {
            // Uses the unmodified default config: DashDuration = 0.18s, DashIFrameDuration = 0.10s.
            Assert.Less(_config.DashIFrameDuration, _config.DashDuration);

            _input.Move = Vector2.right;
            _input.RaiseDash();
            Assert.IsTrue(_dash.IsInvulnerable, "iFrames should activate immediately when the dash starts.");
            Assert.IsTrue(_dash.IsDashing);

            yield return RunFixedSteps(Mathf.RoundToInt(_config.DashIFrameDuration / Time.fixedDeltaTime) + 2);

            Assert.IsFalse(_dash.IsInvulnerable, "Default 0.10s iFrame window should have ended.");
            Assert.IsTrue(_dash.IsDashing, "Default 0.18s dash movement should still be active after iFrames end.");

            yield return RunFixedSteps(Mathf.RoundToInt((_config.DashDuration - _config.DashIFrameDuration) / Time.fixedDeltaTime) + 2);

            Assert.IsFalse(_dash.IsDashing, "Dash movement should have ended at the default 0.18s duration.");
            Assert.IsFalse(_dash.IsInvulnerable);
        }

        [UnityTest]
        public IEnumerator IFrames_AreNotActiveAfterDashAndCooldownComplete()
        {
            SetFloat(_config, "_dashDuration", 0.04f);
            SetFloat(_config, "_dashCooldown", 0.06f);
            SetFloat(_config, "_dashIFrameDuration", 0.04f);

            _input.Move = Vector2.right;
            _input.RaiseDash();
            yield return RunFixedSteps(Mathf.RoundToInt(0.1f / Time.fixedDeltaTime) + 3);

            Assert.IsFalse(_dash.IsInvulnerable);
            Assert.IsFalse(_dash.IsDashing);
        }

        [UnityTest]
        public IEnumerator NormalMovement_ResumesAfterDashCompletes()
        {
            SetFloat(_config, "_dashDuration", 0.06f);
            SetFloat(_config, "_dashCooldown", 0.06f);

            _input.Move = Vector2.right;
            _input.RaiseDash();
            yield return RunFixedSteps(Mathf.RoundToInt(0.06f / Time.fixedDeltaTime) + 2);
            Assert.IsFalse(_dash.IsDashing);

            yield return RunFixedSteps(5);

            Assert.AreEqual(_config.MoveSpeed, _rigidbody2D.linearVelocity.magnitude, 0.25f,
                "Normal movement speed should resume once the dash has ended.");
        }

        [UnityTest]
        public IEnumerator WallCollision_StillAppliesDuringDash()
        {
            SetFloat(_config, "_dashDuration", 0.3f);
            SetFloat(_config, "_dashSpeed", 30f);

            var wallObject = new GameObject("TestWall");
            wallObject.transform.position = new Vector3(2f, 0f, 0f);
            var wallCollider = wallObject.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.5f, 3f);

            var playerCollider = _playerObject.AddComponent<BoxCollider2D>();
            playerCollider.size = new Vector2(0.8f, 0.8f);

            _input.Move = Vector2.right;
            _input.RaiseDash();

            yield return RunFixedSteps(Mathf.RoundToInt(0.3f / Time.fixedDeltaTime) + 2);

            Assert.Less(_playerObject.transform.position.x, 1.9f,
                "Even at high dash speed, the player should be blocked by wall collision rather than phasing through it.");

            Object.Destroy(wallObject);
        }
    }
}
