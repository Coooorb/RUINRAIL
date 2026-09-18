using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class RangedWeaponTests
    {
        private GameObject _playerObject;
        private RangedWeapon _weapon;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private AmmoReserve _ammoReserve;
        private FixedDamageRoller _roller;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _weapon = _playerObject.AddComponent<RangedWeapon>();

            _input = new FakePlayerInputReader();
            _aiming.SetInputReader(_input);
            _weapon.SetInputReader(_input);

            _definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            SetDefinition(_definition, 12, 14, 4.0f, 12, 1.2f, 10f, 20f, AmmoType.Light);

            _ammoReserve = new AmmoReserve();
            _roller = new FixedDamageRoller();

            _weapon.SetProjectilePool(pool);
            _weapon.SetAiming(_aiming);
            _weapon.SetAmmoReserve(_ammoReserve);
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
        }

        private static void SetDefinition(
            RangedWeaponDefinition definition, int damageMin, int damageMax, float fireRate,
            int magazineSize, float reloadTime, float range, float projectileSpeed, AmmoType ammoType)
        {
            SetField(definition, "_damageMin", damageMin);
            SetField(definition, "_damageMax", damageMax);
            SetField(definition, "_fireRate", fireRate);
            SetField(definition, "_magazineSize", magazineSize);
            SetField(definition, "_reloadTime", reloadTime);
            SetField(definition, "_range", range);
            SetField(definition, "_projectileSpeed", projectileSpeed);
            SetField(definition, "_ammoType", ammoType);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            typeof(RangedWeaponDefinition).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        [Test]
        public void TryFire_SpawnsExactlyOneProjectile_AndConsumesOneMagazineRound()
        {
            var magBefore = _weapon.MagazineAmmo;

            var fired = _weapon.TryFire();

            Assert.IsTrue(fired);
            Assert.IsNotNull(_weapon.LastSpawnedProjectile);
            Assert.IsTrue(_weapon.LastSpawnedProjectile.gameObject.activeSelf);
            Assert.AreEqual(magBefore - 1, _weapon.MagazineAmmo);
        }

        [UnityTest]
        public IEnumerator ProjectileDirection_FollowsAimDirection_OverAFrame()
        {
            const float angleDegrees = 20f;
            var direction = new Vector2(Mathf.Cos(angleDegrees * Mathf.Deg2Rad), Mathf.Sin(angleDegrees * Mathf.Deg2Rad));

            _input.IsAimFromPointer = false;
            _input.Aim = direction;

            yield return null;

            var fired = _weapon.TryFire();

            Assert.IsTrue(fired);
            var spawnedDirection = _weapon.LastSpawnedProjectile.Data.Direction;
            Assert.AreEqual(direction.x, spawnedDirection.x, 0.01f,
                "Projectile direction should follow the true 360-degree aim direction, not the quantized body facing.");
            Assert.AreEqual(direction.y, spawnedDirection.y, 0.01f);
        }

        [Test]
        public void DamageRoll_UsesInjectedRoller_AndIsAppliedToProjectile()
        {
            _roller.FixedValue = 13;

            _weapon.TryFire();

            Assert.AreEqual(13, _weapon.LastSpawnedProjectile.Data.Damage);
        }

        [UnityTest]
        public IEnumerator FireRate_PreventsSecondShotBeforeIntervalElapses_ThenAllowsAfter()
        {
            Assert.IsTrue(_weapon.TryFire());
            var magAfterFirst = _weapon.MagazineAmmo;

            Assert.IsFalse(_weapon.TryFire(), "A second shot within the fire-rate interval must not succeed.");
            Assert.AreEqual(magAfterFirst, _weapon.MagazineAmmo);

            yield return new WaitForSeconds(0.3f);

            Assert.IsTrue(_weapon.TryFire(), "A shot after the fire-rate interval has elapsed should succeed.");
            Assert.AreEqual(magAfterFirst - 1, _weapon.MagazineAmmo);
        }

        [Test]
        public void EmptyMagazine_CannotFire()
        {
            SetDefinition(_definition, 12, 14, 4.0f, 1, 1.2f, 10f, 20f, AmmoType.Light);
            _weapon.SetDefinition(_definition);

            Assert.IsTrue(_weapon.TryFire());
            Assert.AreEqual(0, _weapon.MagazineAmmo);

            var secondShot = _weapon.TryFire();

            Assert.IsFalse(secondShot, "Firing with an empty magazine must fail.");
        }

        [UnityTest]
        public IEnumerator Reload_TakesConfiguredDuration()
        {
            _ammoReserve.Add(AmmoType.Light, 20);
            _weapon.TryFire();
            var magBeforeReload = _weapon.MagazineAmmo;

            Assert.IsTrue(_weapon.TryStartReload());
            Assert.IsTrue(_weapon.IsReloading);
            Assert.AreEqual(magBeforeReload, _weapon.MagazineAmmo, "Ammo must not transfer before reload completes.");

            yield return new WaitForSeconds(0.9f);
            Assert.IsTrue(_weapon.IsReloading, "Reload should still be in progress before its configured 1.2s duration elapses.");
            Assert.AreEqual(magBeforeReload, _weapon.MagazineAmmo);

            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(_weapon.IsReloading, "Reload should have completed by 1.2s total.");
            Assert.Greater(_weapon.MagazineAmmo, magBeforeReload);
        }

        [UnityTest]
        public IEnumerator Reload_FillsOnlyMissingCapacity_AndConsumesExactReserve()
        {
            // Bring the magazine to 4/12 by firing 8 shots (waiting out the fire-rate interval each time).
            for (var i = 0; i < 8; i++)
            {
                Assert.IsTrue(_weapon.TryFire());
                yield return new WaitForSeconds(0.3f);
            }
            Assert.AreEqual(4, _weapon.MagazineAmmo);

            _ammoReserve.Add(AmmoType.Light, 5);

            Assert.IsTrue(_weapon.TryStartReload());
            yield return new WaitForSeconds(1.3f);

            Assert.AreEqual(9, _weapon.MagazineAmmo);
            Assert.AreEqual(0, _ammoReserve.Get(AmmoType.Light));
        }

        [Test]
        public void Reload_FullMagazine_DoesNothing()
        {
            _ammoReserve.Add(AmmoType.Light, 20);

            var started = _weapon.TryStartReload();

            Assert.IsFalse(started, "Reloading a full magazine should do nothing.");
            Assert.IsFalse(_weapon.IsReloading);
        }

        [Test]
        public void Reload_ZeroReserve_DoesNothing()
        {
            _weapon.TryFire();

            var started = _weapon.TryStartReload();

            Assert.IsFalse(started, "Reloading with zero reserve ammo should do nothing.");
            Assert.IsFalse(_weapon.IsReloading);
        }

        [Test]
        public void Weapon_CannotFireWhileReloading()
        {
            _ammoReserve.Add(AmmoType.Light, 20);
            _weapon.TryFire();
            Assert.IsTrue(_weapon.TryStartReload());

            var firedWhileReloading = _weapon.TryFire();

            Assert.IsFalse(firedWhileReloading, "The weapon must not fire while actively reloading.");
        }

        [Test]
        public void Reload_TriggeredByInputReaderReloadEvent()
        {
            _ammoReserve.Add(AmmoType.Light, 20);
            _weapon.TryFire();

            _input.RaiseReload();

            Assert.IsTrue(_weapon.IsReloading, "The Reload input event should start a reload.");
        }
    }
}
