using System.Collections;
using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class PlayerMovementAimingIndependenceTests
    {
        private GameObject _playerObject;
        private Rigidbody2D _rigidbody2D;
        private PlayerMovement _movement;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _rigidbody2D = _playerObject.AddComponent<Rigidbody2D>();
            _movement = _playerObject.AddComponent<PlayerMovement>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();

            _input = new FakePlayerInputReader();
            _config = ScriptableObject.CreateInstance<PlayerBalanceConfig>();

            _movement.SetInputReader(_input);
            _movement.SetBalanceConfig(_config);
            _aiming.SetInputReader(_input);
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

        [UnityTest]
        public IEnumerator MovementDirection_DoesNotAffectAimDirection()
        {
            _input.Move = Vector2.left;
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.up;

            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            yield return null;

            Assert.Less(_playerObject.transform.position.x, -0.1f, "Player should have moved left in response to Move input.");

            Assert.AreEqual(0f, _aiming.AimDirection.x, 0.01f);
            Assert.AreEqual(1f, _aiming.AimDirection.y, 0.01f);
            Assert.AreEqual(BodyFacing8.N, _aiming.BodyFacing,
                "Aim direction should reflect Aim input only, independent of Move input.");
        }
    }
}
