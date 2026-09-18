using System.Collections;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    public class RangedWeaponAmmoCostTests
    {
        private const float ReloadTime = 0.1f;

        private GameObject _playerObject;
        private RangedWeapon _weapon;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private AmmoReserve _ammoReserve;

        [SetUp]
        public void SetUp()
        {
            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            var aiming = _playerObject.AddComponent<PlayerAiming>();
            _weapon = _playerObject.AddComponent<RangedWeapon>();

            _input = new FakePlayerInputReader();
            aiming.SetInputReader(_input);
            _weapon.SetInputReader(_input);

            _definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _ammoReserve = new AmmoReserve();

            _weapon.SetProjectilePool(pool);
            _weapon.SetAiming(aiming);
            _weapon.SetAmmoReserve(_ammoReserve);
            _weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 10 });
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

        private void Configure(AmmoType ammoType, int magazineSize, int ammoCostPerShot)
        {
            SetField("_damageMin", 10);
            SetField("_damageMax", 10);
            SetField("_fireRate", 100f);
            SetField("_magazineSize", magazineSize);
            SetField("_reloadTime", ReloadTime);
            SetField("_range", 10f);
            SetField("_projectileSpeed", 20f);
            SetField("_ammoType", ammoType);
            SetField("_ammoCostPerShot", ammoCostPerShot);
            _weapon.SetDefinition(_definition);
        }

        private void SetField(string fieldName, object value)
        {
            typeof(RangedWeaponDefinition).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(_definition, value);
        }

        private static IEnumerator WaitForReload()
        {
            yield return new WaitForSeconds(ReloadTime + 0.15f);
        }

        [Test]
        public void AmmoCostPerShot_DefaultsToOne_AndNeverBelowOne()
        {
            SetField("_ammoCostPerShot", 0);
            Assert.AreEqual(1, _definition.AmmoCostPerShot, "A cost below 1 must be clamped to 1.");
        }

        [Test]
        public void Firing_ConsumesOneMagazineShot_RegardlessOfAmmoCost()
        {
            Configure(AmmoType.Heavy, 2, 4);

            Assert.IsTrue(_weapon.TryFire());

            Assert.AreEqual(1, _weapon.MagazineAmmo, "Magazine counts loaded shots; one shot is consumed per fire.");
        }

        [UnityTest]
        public IEnumerator Reload_WithCostFour_ConsumesFourHeavyPerShotLoaded()
        {
            Configure(AmmoType.Heavy, 1, 4);
            Assert.IsTrue(_weapon.TryFire()); // magazine 0/1
            _ammoReserve.Add(AmmoType.Heavy, 12);

            Assert.IsTrue(_weapon.TryStartReload());
            yield return WaitForReload();

            Assert.AreEqual(1, _weapon.MagazineAmmo);
            Assert.AreEqual(8, _ammoReserve.Get(AmmoType.Heavy), "Loading one rocket must cost exactly 4 Heavy Ammo.");
        }

        [UnityTest]
        public IEnumerator Reload_PartialReserve_LoadsOnlyWholeAffordableShots()
        {
            Configure(AmmoType.Heavy, 3, 4);
            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(_weapon.TryFire());
                yield return new WaitForSeconds(0.05f); // let the fire-rate cooldown tick
            }
            Assert.AreEqual(0, _weapon.MagazineAmmo);
            _ammoReserve.Add(AmmoType.Heavy, 7); // affords 1 shot, leaves 3

            Assert.IsTrue(_weapon.TryStartReload());
            yield return WaitForReload();

            Assert.AreEqual(1, _weapon.MagazineAmmo, "Only whole shots may be loaded.");
            Assert.AreEqual(3, _ammoReserve.Get(AmmoType.Heavy), "Leftover reserve below one shot's cost must remain untouched.");
        }

        [Test]
        public void Reload_Refused_WhenReserveBelowOneShotCost()
        {
            Configure(AmmoType.Heavy, 1, 4);
            Assert.IsTrue(_weapon.TryFire());
            _ammoReserve.Add(AmmoType.Heavy, 3);

            Assert.IsFalse(_weapon.TryStartReload(), "Reload must not start when reserve cannot afford a single shot.");
            Assert.AreEqual(3, _ammoReserve.Get(AmmoType.Heavy));
            Assert.AreEqual(0, _weapon.MagazineAmmo);
        }

        [UnityTest]
        public IEnumerator Reload_NeverOverfillsMagazine_AndNeverMakesReserveNegative()
        {
            Configure(AmmoType.Medium, 5, 2);
            Assert.IsTrue(_weapon.TryFire()); // 4/5
            _ammoReserve.Add(AmmoType.Medium, 100);

            Assert.IsTrue(_weapon.TryStartReload());
            yield return WaitForReload();

            Assert.AreEqual(5, _weapon.MagazineAmmo);
            Assert.AreEqual(98, _ammoReserve.Get(AmmoType.Medium), "Exactly one shot's cost (2 Medium) should be consumed.");
            Assert.GreaterOrEqual(_ammoReserve.Get(AmmoType.Medium), 0);
        }

        [UnityTest]
        [TestCase(AmmoType.Light, ExpectedResult = null)]
        [TestCase(AmmoType.Medium, ExpectedResult = null)]
        [TestCase(AmmoType.Heavy, ExpectedResult = null)]
        [TestCase(AmmoType.Shells, ExpectedResult = null)]
        public IEnumerator Reload_ConsumesOnlyTheWeaponsOwnAmmoCategory(AmmoType ammoType)
        {
            Configure(ammoType, 2, 1);
            Assert.IsTrue(_weapon.TryFire()); // 1/2
            foreach (AmmoType type in System.Enum.GetValues(typeof(AmmoType)))
            {
                _ammoReserve.Add(type, 10);
            }

            Assert.IsTrue(_weapon.TryStartReload());
            yield return WaitForReload();

            Assert.AreEqual(2, _weapon.MagazineAmmo);
            foreach (AmmoType type in System.Enum.GetValues(typeof(AmmoType)))
            {
                var expected = type == ammoType ? 9 : 10;
                Assert.AreEqual(expected, _ammoReserve.Get(type), $"Reserve for {type} was affected unexpectedly.");
            }
        }
    }
}
