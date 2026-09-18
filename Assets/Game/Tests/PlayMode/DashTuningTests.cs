using System.Collections;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Dash tuning (design update 2026-09-17): ~15 % nerf of recharge rate and distance from the shipped config
    /// (20 u/s × 0.18 s = 3.6 tiles at a 1.25 s cooldown) to 17 u/s × 0.18 s = 3.06 tiles at 1.25 / 0.85 = 1.4706 s.
    /// The i-frame window, the direction rules and the wall collision are untouched, and the HUD's cooldown fraction
    /// comes from the same authoritative cooldown the dash enforces.
    /// </summary>
    public class DashTuningTests
    {
        private const float OldCooldown = 1.25f;
        private const float OldSpeed = 20f;
        private const float NewCooldown = OldCooldown / 0.85f;
        private const float NewSpeed = OldSpeed * 0.85f;

        private GameObject _playerObject;
        private Rigidbody2D _body;
        private PlayerDash _dash;
        private FakePlayerInputReader _input;
        private PlayerBalanceConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            Assert.IsNotNull(_config, "the shipped PlayerBalanceConfig");
            _playerObject = new GameObject("TestPlayer");
            _body = _playerObject.AddComponent<Rigidbody2D>();
            _body.gravityScale = 0f;
            _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var movement = _playerObject.AddComponent<PlayerMovement>();
            _dash = _playerObject.AddComponent<PlayerDash>();
            _input = new FakePlayerInputReader();
            movement.SetInputReader(_input);
            movement.SetBalanceConfig(_config);
            movement.SetMovementOverride(_dash);
            _dash.SetInputReader(_input);
            _dash.SetBalanceConfig(_config);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null) Object.Destroy(_playerObject);
        }

        private static IEnumerator FixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        [Test]
        public void ShippedConfig_CarriesTheExactBeforeAfterValues()
        {
            Assert.AreEqual(NewCooldown, _config.DashCooldown, 0.0005f, "cooldown = 1.25 / 0.85 = 1.4706 s (recharge rate × 0.85)");
            Assert.AreEqual(NewSpeed, _config.DashSpeed, 0.0005f, "speed = 20 × 0.85 = 17 u/s (distance × 0.85 over the unchanged duration)");
            Assert.AreEqual(0.18f, _config.DashDuration, 0.0005f, "duration unchanged");
            Assert.AreEqual(0.10f, _config.DashIFrameDuration, 0.0005f, "i-frame window unchanged");
            Assert.AreEqual(3.06f, _config.DashSpeed * _config.DashDuration, 0.001f, "3.06 tiles, from 3.6");
            Assert.AreEqual(0.85f, (_config.DashSpeed * _config.DashDuration) / (OldSpeed * 0.18f), 0.001f);
            Assert.AreEqual(0.85f, OldCooldown / _config.DashCooldown, 0.001f);
        }

        [UnityTest]
        public IEnumerator Recharge_TakesFifteenPercentLonger_NoEarlySecondDash()
        {
            _input.Move = Vector2.right;
            Assert.IsTrue(_dash.TryStartDash(Vector2.right));
            Assert.AreEqual(NewCooldown, _dash.CooldownRemaining, 0.001f, "the cooldown starts at the new value");
            var step = Time.fixedDeltaTime;
            // Under the old 1.25 s the dash would be back by now; under the new tuning it is still recharging.
            yield return FixedSteps(Mathf.CeilToInt(OldCooldown / step) + 1);
            Assert.IsFalse(_dash.CanDash, "still on cooldown after 1.25 s");
            Assert.IsFalse(_dash.TryStartDash(Vector2.up), "no early second dash");
            Assert.Greater(_dash.CooldownRemaining, 0.15f, "≈ 0.22 s of the recharge left");
            yield return FixedSteps(Mathf.CeilToInt((NewCooldown - OldCooldown) / step) + 2);
            Assert.IsTrue(_dash.CanDash, "recharged after 1.4706 s");
            Assert.IsTrue(_dash.TryStartDash(Vector2.up));
        }

        [UnityTest]
        public IEnumerator Displacement_IsFifteenPercentShorter_ThreePointZeroSixTiles()
        {
            var start = (Vector2)_playerObject.transform.position;
            _input.Move = Vector2.right;
            Assert.IsTrue(_dash.TryStartDash(Vector2.right));
            Assert.AreEqual(NewSpeed, _dash.CurrentDashSpeed, 0.001f);
            var steps = 0;
            while (_dash.IsDashing && steps++ < 60) yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            var travelled = ((Vector2)_playerObject.transform.position - start).x;
            var expected = NewSpeed * _config.DashDuration;
            Assert.AreEqual(expected, travelled, NewSpeed * Time.fixedDeltaTime * 1.5f, $"≈ {expected:0.00} tiles (one physics step of slack)");
            Assert.Less(travelled, OldSpeed * 0.18f * 0.92f, "clearly shorter than the old 3.6 tiles");
        }

        [UnityTest]
        public IEnumerator IFrames_Unchanged_TenthOfASecond_AndEndBeforeTheDashDoes()
        {
            _input.Move = Vector2.right;
            Assert.IsTrue(_dash.TryStartDash(Vector2.right));
            var step = Time.fixedDeltaTime;
            var iFrames = 0;
            var dashSteps = 0;
            for (var i = 0; i < 60 && _dash.IsDashing; i++)
            {
                if (_dash.IsInvulnerable) iFrames++;
                dashSteps++;
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(Mathf.CeilToInt(0.10f / step), iFrames, 1, "i-frames ≈ 0.10 s");
            Assert.AreEqual(Mathf.CeilToInt(0.18f / step), dashSteps, 1, "movement ≈ 0.18 s");
            Assert.Less(iFrames, dashSteps, "the i-frame window closes before the dash ends");
            Assert.IsFalse(_dash.IsInvulnerable);
        }

        [UnityTest]
        public IEnumerator Dash_StopsAtAWall_NoTunneling()
        {
            var wall = new GameObject("TestWall");
            wall.transform.position = new Vector3(1.5f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 3f);
            _playerObject.AddComponent<BoxCollider2D>().size = new Vector2(0.8f, 0.8f);

            _input.Move = Vector2.right;
            Assert.IsTrue(_dash.TryStartDash(Vector2.right));
            yield return FixedSteps(Mathf.RoundToInt(_config.DashDuration / Time.fixedDeltaTime) + 3);
            Assert.Less(_playerObject.transform.position.x, 1.3f, "blocked by the wall (the unblocked dash would reach 3.06)");
            Assert.Greater(_playerObject.transform.position.x, 0.2f, "but it did move up to it");
            Object.Destroy(wall);
        }

        [UnityTest]
        public IEnumerator HudCooldownFraction_TracksTheAuthoritativeCooldown()
        {
            var vm = new DungeonHudViewModel();
            vm.BindPlayer(_playerObject.AddComponent<HealthComponent>(), _dash);
            vm.Tick();
            Assert.IsTrue(vm.Snapshot.DashReady);
            Assert.AreEqual(0f, vm.Snapshot.DashCooldown01, 0.001f);

            _input.Move = Vector2.right;
            Assert.IsTrue(_dash.TryStartDash(Vector2.right));
            vm.Tick();
            Assert.IsFalse(vm.Snapshot.DashReady);
            Assert.AreEqual(1f, vm.Snapshot.DashCooldown01, 0.01f, "just used: the whole sweep");
            yield return FixedSteps(Mathf.CeilToInt(0.5f / Time.fixedDeltaTime));
            vm.Tick();
            Assert.AreEqual(_dash.CooldownRemaining / _dash.CurrentDashCooldown, vm.Snapshot.DashCooldown01, 0.01f, "the fraction is remaining / 1.4706");
            Assert.Greater(vm.Snapshot.DashCooldown01, 0.55f, "after 0.5 s of a 1.4706 s cooldown, > 55 % remains (under 1.25 s it would be 60 %)");
            yield return FixedSteps(Mathf.CeilToInt(1.05f / Time.fixedDeltaTime));
            vm.Tick();
            Assert.IsTrue(vm.Snapshot.DashReady);
            Assert.AreEqual(0f, vm.Snapshot.DashCooldown01, 0.001f);
            vm.Dispose();
        }
    }
}
