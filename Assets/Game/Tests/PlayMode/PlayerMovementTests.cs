using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class PlayerMovementTests
    {
        private const float DefaultMoveSpeed = 5f;
        private const float PositionTolerance = 0.15f;

        private GameObject _playerObject;
        private Rigidbody2D _rigidbody2D;
        private PlayerMovement _movement;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;
        private float _originalFixedDeltaTime;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;

            _playerObject = new GameObject("TestPlayer");
            _rigidbody2D = _playerObject.AddComponent<Rigidbody2D>();
            _movement = _playerObject.AddComponent<PlayerMovement>();

            _input = new FakePlayerInputReader();
            _config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();

            _movement.SetInputReader(_input);
            _movement.SetBalanceConfig(_config);
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;

            if (_playerObject != null)
            {
                Object.Destroy(_playerObject);
            }

            if (_config != null)
            {
                Object.Destroy(_config);
            }
        }

        private static void SetMoveSpeed(PlayerBalanceConfig config, float moveSpeed)
        {
            var field = typeof(PlayerBalanceConfig).GetField("_moveSpeed", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(config, moveSpeed);
        }

        private static IEnumerator RunFixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator ZeroInput_ProducesNoMovement()
        {
            var startPosition = _playerObject.transform.position;
            _input.Move = Vector2.zero;

            yield return RunFixedSteps(10);

            Assert.AreEqual(Vector2.zero, _rigidbody2D.linearVelocity);
            Assert.AreEqual(startPosition, _playerObject.transform.position);
        }

        [UnityTest]
        public IEnumerator CardinalInput_MovesRightAtConfiguredSpeed()
        {
            _input.Move = Vector2.right;

            const int steps = 25;
            var elapsedTime = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            var expectedX = DefaultMoveSpeed * elapsedTime;
            Assert.AreEqual(expectedX, _playerObject.transform.position.x, PositionTolerance);
            Assert.AreEqual(0f, _playerObject.transform.position.y, PositionTolerance);
        }

        [UnityTest]
        public IEnumerator CardinalInput_MovesUpAtConfiguredSpeed()
        {
            _input.Move = Vector2.up;

            const int steps = 25;
            var elapsedTime = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            var expectedY = DefaultMoveSpeed * elapsedTime;
            Assert.AreEqual(0f, _playerObject.transform.position.x, PositionTolerance);
            Assert.AreEqual(expectedY, _playerObject.transform.position.y, PositionTolerance);
        }

        [UnityTest]
        public IEnumerator DiagonalInput_IsNormalizedAndNotFaster()
        {
            _input.Move = new Vector2(1f, 1f);

            const int steps = 25;
            var elapsedTime = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            var travelled = ((Vector2)_playerObject.transform.position).magnitude;
            var expectedDistance = DefaultMoveSpeed * elapsedTime;

            Assert.AreEqual(expectedDistance, travelled, PositionTolerance,
                "Diagonal movement should travel at the configured speed, not faster.");
        }

        [UnityTest]
        public IEnumerator ConfiguredSpeed_IsRespected_ForDifferentValues()
        {
            SetMoveSpeed(_config, 2f);
            _input.Move = Vector2.right;

            const int steps = 25;
            var elapsedTime = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            Assert.AreEqual(2f * elapsedTime, _playerObject.transform.position.x, PositionTolerance);
        }

        [UnityTest]
        public IEnumerator ConfiguredSpeed_IsRespected_ForHigherValue()
        {
            SetMoveSpeed(_config, 8f);
            _input.Move = Vector2.right;

            const int steps = 25;
            var elapsedTime = steps * Time.fixedDeltaTime;
            yield return RunFixedSteps(steps);

            Assert.AreEqual(8f * elapsedTime, _playerObject.transform.position.x, PositionTolerance);
        }

        [UnityTest]
        public IEnumerator Movement_IsIndependentOfPhysicsStepSize()
        {
            const float targetSimulatedSeconds = 0.4f;

            Time.fixedDeltaTime = 0.02f;
            _input.Move = Vector2.right;
            yield return RunFixedSteps(Mathf.RoundToInt(targetSimulatedSeconds / Time.fixedDeltaTime));
            var distanceWithLargeStep = _playerObject.transform.position.x;

            _playerObject.transform.position = Vector3.zero;
            _rigidbody2D.linearVelocity = Vector2.zero;

            Time.fixedDeltaTime = 0.005f;
            yield return RunFixedSteps(Mathf.RoundToInt(targetSimulatedSeconds / Time.fixedDeltaTime));
            var distanceWithSmallStep = _playerObject.transform.position.x;

            Assert.AreEqual(distanceWithLargeStep, distanceWithSmallStep, PositionTolerance,
                "Total distance travelled over the same simulated time should not depend on the physics step size.");
        }

        [UnityTest]
        public IEnumerator Movement_IsBlockedByWallCollision()
        {
            var wallObject = new GameObject("TestWall");
            wallObject.transform.position = new Vector3(2f, 0f, 0f);
            var wallCollider = wallObject.AddComponent<BoxCollider2D>();
            wallCollider.size = new Vector2(0.5f, 3f);

            var playerCollider = _playerObject.AddComponent<BoxCollider2D>();
            playerCollider.size = new Vector2(0.8f, 0.8f);

            _input.Move = Vector2.right;

            yield return RunFixedSteps(60);

            Assert.Less(_playerObject.transform.position.x, 1.9f,
                "Player should be blocked by the wall well before the unobstructed travel distance.");
            Assert.Greater(_playerObject.transform.position.x, 0.2f,
                "Player should have moved from the start before being blocked.");

            Object.Destroy(wallObject);
        }
    }
}
