using System.Collections;
using NUnit.Framework;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class PlayerAimingTests
    {
        private const float DirectionTolerance = 0.01f;

        private GameObject _playerObject;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private GameObject _cameraObject;
        private Camera _camera;
        private Transform _aimPivot;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _aiming = _playerObject.AddComponent<PlayerAiming>();

            var pivotObject = new GameObject("TestAimPivot");
            pivotObject.transform.SetParent(_playerObject.transform, false);
            _aimPivot = pivotObject.transform;

            _cameraObject = new GameObject("TestCamera");
            _camera = _cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5f;
            _cameraObject.transform.position = new Vector3(0f, 0f, -10f);

            _input = new FakePlayerInputReader();

            _aiming.SetInputReader(_input);
            _aiming.SetCamera(_camera);
            _aiming.SetAimPivot(_aimPivot);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null)
            {
                Object.Destroy(_playerObject);
            }

            if (_cameraObject != null)
            {
                Object.Destroy(_cameraObject);
            }
        }

        [UnityTest]
        public IEnumerator GamepadDirection_ProducesMatchingAimDirectionAndFacing()
        {
            _input.IsAimFromPointer = false;
            _input.Aim = new Vector2(1f, 1f);

            yield return null;

            var expected = new Vector2(1f, 1f).normalized;
            Assert.AreEqual(expected.x, _aiming.AimDirection.x, DirectionTolerance);
            Assert.AreEqual(expected.y, _aiming.AimDirection.y, DirectionTolerance);
            Assert.AreEqual(BodyFacing8.NE, _aiming.BodyFacing);
        }

        [UnityTest]
        public IEnumerator ArbitraryNonCardinalDirection_RetainsTrue360AimWhileFacingIsQuantized()
        {
            const float angleDegrees = 20f;
            var direction = new Vector2(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), Mathf.Sin(angleDegrees * Mathf.Deg2Rad));

            _input.IsAimFromPointer = false;
            _input.Aim = direction;

            yield return null;

            Assert.AreEqual(direction.x, _aiming.AimDirection.x, DirectionTolerance,
                "Aim direction must retain the true 360-degree angle, not snap to an octant.");
            Assert.AreEqual(direction.y, _aiming.AimDirection.y, DirectionTolerance);
            Assert.AreEqual(BodyFacing8.E, _aiming.BodyFacing,
                "Body facing is expected to quantize the same direction to the nearest octant.");
        }

        [UnityTest]
        public IEnumerator MousePointerInput_ConvertsScreenPositionToWorldDirection()
        {
            _playerObject.transform.position = Vector3.zero;

            var targetWorldPoint = new Vector3(3f, 4f, 0f);
            var screenPoint = _camera.WorldToScreenPoint(targetWorldPoint);

            _input.IsAimFromPointer = true;
            _input.Aim = new Vector2(screenPoint.x, screenPoint.y);

            yield return null;

            var expectedDirection = new Vector2(3f, 4f).normalized;
            Assert.AreEqual(expectedDirection.x, _aiming.AimDirection.x, DirectionTolerance);
            Assert.AreEqual(expectedDirection.y, _aiming.AimDirection.y, DirectionTolerance);
            Assert.AreEqual(BodyFacing8.NE, _aiming.BodyFacing);
        }

        [UnityTest]
        public IEnumerator NoMeaningfulInput_PreservesLastValidDirection()
        {
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.up;
            yield return null;

            var previousDirection = _aiming.AimDirection;
            var previousFacing = _aiming.BodyFacing;

            _input.Aim = Vector2.zero;
            yield return null;

            Assert.AreEqual(previousDirection, _aiming.AimDirection,
                "Aim direction should be preserved when the stick reports no meaningful input.");
            Assert.AreEqual(previousFacing, _aiming.BodyFacing);
        }

        [UnityTest]
        public IEnumerator AimPivot_RotatesToMatchAimDirection()
        {
            _input.IsAimFromPointer = false;
            _input.Aim = Vector2.up;
            yield return null;

            Assert.AreEqual(90f, NormalizeAngle(_aimPivot.eulerAngles.z), 1f);

            _input.Aim = Vector2.down;
            yield return null;

            Assert.AreEqual(270f, NormalizeAngle(_aimPivot.eulerAngles.z), 1f);
        }

        private static float NormalizeAngle(float angle)
        {
            angle %= 360f;
            if (angle < 0f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
