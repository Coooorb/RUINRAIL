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
    /// <summary>
    /// TASK 056 — Scrap Spear reference (24–28, 1.8/s, 2.6 tiles, 20°): the narrow long-reach shape comes purely from the
    /// shared MeleeWeaponDefinition data; lifecycle, per-swing dedupe and cadence are the common melee rules.
    /// </summary>
    public class SpearReferenceTests
    {
        private const float WindUp = 0.15f;
        private const float Recovery = 0.25f;

        private GameObject _playerObject;
        private MeleeWeapon _spear;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private MeleeWeaponDefinition _definition;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            _created.Add(_playerObject);
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _spear = _playerObject.AddComponent<MeleeWeapon>();
            _input = new FakePlayerInputReader { Aim = Vector2.right };
            _aiming.SetInputReader(_input);
            _spear.SetInputReader(_input);

            _definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            _created.Add(_definition);
            Set(_definition, "_id", "weapon_scrap_spear");
            Set(_definition, "_category", ItemCategory.Weapon);
            Set(_definition, "_weaponClass", WeaponClass.Spear);
            Set(_definition, "_damageMin", 24);
            Set(_definition, "_damageMax", 28);
            Set(_definition, "_attackRate", 1.8f);
            Set(_definition, "_attackRange", 2.6f);
            Set(_definition, "_attackArcDegrees", 20f);
            Set(_definition, "_windUpSeconds", WindUp);
            Set(_definition, "_recoverySeconds", Recovery);

            _spear.SetAiming(_aiming);
            _spear.SetDamageRoller(new FixedDamageRoller { FixedValue = 26 });
            _spear.SetDefinition(_definition);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string fieldName, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new System.MissingFieldException(target.GetType().Name, fieldName);
        }

        private TestDamageableTarget Target(float angleDegrees, float distance)
        {
            var radians = angleDegrees * Mathf.Deg2Rad;
            var go = new GameObject($"Target_{angleDegrees}_{distance}");
            _created.Add(go);
            go.transform.position = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * distance;
            go.AddComponent<CircleCollider2D>().isTrigger = true;
            return go.AddComponent<TestDamageableTarget>();
        }

        private IEnumerator Swing()
        {
            yield return null; // aim update
            Assert.IsTrue(_spear.TryAttack());
            yield return new WaitForSeconds(WindUp + 0.05f);
        }

        // ---- Acceptance 2: +/-10 degrees around AimDirection at 2.6 tiles ----

        [UnityTest]
        public IEnumerator Reach_Is2Point6Tiles_AndArcIsPlusMinusTenDegrees()
        {
            var straightFar = Target(0f, 2.5f);
            var tooFar = Target(0f, 2.75f);
            var insideArc = Target(8f, 2f);
            var insideArcNeg = Target(-9f, 1.5f);
            var outsideArc = Target(12f, 2f);
            var outsideArcNeg = Target(-11f, 1f);
            var behind = Target(180f, 1f);

            yield return Swing();

            Assert.AreEqual(1, straightFar.HitCount, "2.5 tiles straight ahead: in reach.");
            Assert.AreEqual(0, tooFar.HitCount, "2.75 > 2.6: out of reach.");
            Assert.AreEqual(1, insideArc.HitCount, "+8 degrees is inside the +/-10 cone.");
            Assert.AreEqual(1, insideArcNeg.HitCount, "-9 degrees is inside.");
            Assert.AreEqual(0, outsideArc.HitCount, "+12 degrees is outside a 20-degree total arc.");
            Assert.AreEqual(0, outsideArcNeg.HitCount, "-11 degrees is outside.");
            Assert.AreEqual(0, behind.HitCount);
            Assert.AreEqual(26, straightFar.LastDamageAmount, "Integer damage from the shared roller.");
        }

        [UnityTest]
        public IEnumerator Arc_FollowsAimDirection_Full360()
        {
            var up = Target(90f, 2f);
            var right = Target(0f, 2f);
            _input.Aim = Vector2.up;
            yield return Swing();
            Assert.AreEqual(1, up.HitCount);
            Assert.AreEqual(0, right.HitCount, "A narrow spear aimed up cannot touch a target to the right.");
        }

        // ---- Acceptance 3: dedupe per swing, several targets in the narrow lane, cadence gate ----

        [UnityTest]
        public IEnumerator Swing_HitsEachTargetInTheLaneOnce_AndCadenceIs1Point8PerSecond()
        {
            var near = Target(0f, 1f);
            var mid = Target(3f, 1.8f);
            var far = Target(-4f, 2.5f);

            yield return Swing();
            Assert.AreEqual(1, near.HitCount);
            Assert.AreEqual(1, mid.HitCount, "Multiple targets inside the lane are all hit.");
            Assert.AreEqual(1, far.HitCount);

            yield return new WaitForSeconds(Recovery + 0.05f);
            Assert.IsFalse(_spear.TryAttack(), "Wind-up + recovery (0.4 s) is over, but 1/1.8 = 0.556 s has not elapsed.");
            Assert.AreEqual(1, near.HitCount, "No second hit before the cadence allows a new swing.");

            yield return new WaitForSeconds(0.2f);
            Assert.IsTrue(_spear.TryAttack(), "After the 0.556 s interval the next thrust is allowed.");
            yield return new WaitForSeconds(WindUp + 0.05f);
            Assert.AreEqual(2, near.HitCount, "Second swing hits again exactly once.");
            Assert.AreEqual(2, mid.HitCount);
        }

        [Test]
        public void Spear_HasNoAmmoStaminaOrChargeState()
        {
            Assert.IsNull(typeof(MeleeWeapon).GetProperty("ChargeFraction"));
            Assert.IsNull(typeof(MeleeWeapon).GetProperty("MagazineAmmo"));
            Assert.IsNull(typeof(MeleeWeaponDefinition).GetProperty("AmmoType"));
            Assert.IsNull(typeof(MeleeWeaponDefinition).GetProperty("ComboCount"));
            Assert.IsNull(typeof(MeleeWeaponDefinition).GetProperty("StaminaCost"));
        }
    }
}
