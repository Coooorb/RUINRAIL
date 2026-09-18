using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>
    /// Pulse Carbine B1 reference: 7–9, 8/s, speed 22, heat 7/shot, cooling 35/s, max 100, delay 0.6s, lockout 2.2s.
    /// Values mirror PulseCarbineB1.asset (validated in EditMode).
    /// </summary>
    public class BlasterWeaponTests
    {
        private GameObject _playerObject;
        private BlasterWeapon _blaster;
        private MeleeWeapon _knife;
        private WeaponLoadout _loadout;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private BlasterWeaponDefinition _definition;
        private MeleeWeaponDefinition _knifeDefinition;
        private AmmoReserve _ammoReserve;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<BlasterWeaponDefinition>();
            _created.Add(_definition);
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_pulse_carbine_b1");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.Blaster);
            Set(typeof(BlasterWeaponDefinition), _definition, "_damageMin", 7);
            Set(typeof(BlasterWeaponDefinition), _definition, "_damageMax", 9);
            Set(typeof(BlasterWeaponDefinition), _definition, "_fireRate", 8f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_range", 12f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_projectileSpeed", 22f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_heatPerShot", 7f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_coolingRatePerSecond", 35f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_maxHeat", 100f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_coolingDelaySeconds", 0.6f);
            Set(typeof(BlasterWeaponDefinition), _definition, "_overheatLockoutSeconds", 2.2f);

            _knifeDefinition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            _created.Add(_knifeDefinition);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_damageMin", 14);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_damageMax", 17);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackRate", 3.5f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackRange", 1.2f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackArcDegrees", 80f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_windUpSeconds", 0.08f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_recoverySeconds", 0.12f);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _blaster = _playerObject.AddComponent<BlasterWeapon>();
            _knife = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();

            _input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            _aiming.SetInputReader(_input);

            _ammoReserve = new AmmoReserve();
            _ammoReserve.Add(AmmoType.Light, 10);
            _ammoReserve.Add(AmmoType.Medium, 10);
            _ammoReserve.Add(AmmoType.Heavy, 10);
            _ammoReserve.Add(AmmoType.Shells, 10);

            _blaster.SetInputReader(_input);
            _blaster.SetProjectilePool(pool);
            _blaster.SetAiming(_aiming);
            _blaster.SetDamageRoller(new UnityRandomDamageRoller());
            _blaster.SetDefinition(_definition);

            _knife.SetInputReader(_input);
            _knife.SetAiming(_aiming);
            _knife.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _knife.SetDefinition(_knifeDefinition);

            _loadout.SetInputReader(_input);
            _loadout.SetPrimary(_blaster);
            _loadout.SetSecondary(_knife);
            _loadout.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null) Object.DestroyImmediate(_playerObject);
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(System.Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private void ResetCooldown()
        {
            Set(typeof(BlasterWeapon), _blaster, "_fireCooldownRemaining", 0f);
        }

        private int TotalAmmo() =>
            _ammoReserve.Get(AmmoType.Light) + _ammoReserve.Get(AmmoType.Medium) + _ammoReserve.Get(AmmoType.Heavy) + _ammoReserve.Get(AmmoType.Shells);

        [Test]
        public void Fire_AddsExactHeat_SpawnsVisibleProjectile_AndConsumesNoAmmo()
        {
            var ammoBefore = TotalAmmo();

            Assert.IsTrue(_blaster.TryFire());

            Assert.AreEqual(7f, _blaster.Heat.Heat, 0.0001f);
            var projectile = _blaster.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            Assert.IsTrue(projectile.gameObject.activeSelf);
            Assert.AreEqual(22f, projectile.Data.Speed, 0.001f);
            Assert.AreEqual(ammoBefore, TotalAmmo(), "Blasters consume no Light/Medium/Heavy/Shell ammo.");
            Assert.IsFalse(_blaster.TryFire(), "Fire-rate gate blocks a second shot in the same frame.");
        }

        [Test]
        public void Damage_RollsOnlySevenEightOrNine()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                ResetCooldown();
                Assert.IsTrue(_blaster.TryFire());
                seen.Add(_blaster.LastSpawnedProjectile.Data.Damage);
                // Keep the weapon below overheat so the roll path stays open.
                if (_blaster.Heat.Heat >= 90f) Set(typeof(BlasterWeapon), _blaster, "_heat", null);
            }

            CollectionAssert.AreEquivalent(new[] { 7, 8, 9 }, seen.OrderBy(v => v));
        }

        [UnityTest]
        public IEnumerator FireRate_GatesAtEightPerSecond()
        {
            Assert.IsTrue(_blaster.TryFire());
            yield return new WaitForSeconds(0.06f);
            Assert.IsFalse(_blaster.TryFire(), "Interval is 0.125s.");
            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(_blaster.TryFire());
        }

        [Test]
        public void Reload_DoesNothing_ForABlaster()
        {
            _blaster.TryFire();
            var heatBefore = _blaster.Heat.Heat;

            _input.RaiseReload();

            Assert.AreEqual(heatBefore, _blaster.Heat.Heat, 0.0001f, "Reload must not touch heat.");
            Assert.IsNull(typeof(BlasterWeapon).GetMethod("TryStartReload"), "Blaster exposes no reload action.");
            Assert.IsNull(typeof(BlasterWeapon).GetProperty("MagazineAmmo"));
        }

        [UnityTest]
        public IEnumerator Overheat_LocksFiringForLockout_ThenAllowsFiring()
        {
            for (var i = 0; i < 15; i++)
            {
                ResetCooldown();
                Assert.IsTrue(_blaster.TryFire(), $"Shot {i + 1} should succeed below max heat.");
            }

            Assert.AreEqual(100f, _blaster.Heat.Heat, 0.0001f, "15 × 7 = 105 clamps to 100.");
            Assert.IsTrue(_blaster.Heat.IsOverheated);
            ResetCooldown();
            Assert.IsFalse(_blaster.TryFire(), "Overheated: cannot fire.");

            yield return new WaitForSeconds(2.0f);
            ResetCooldown();
            Assert.IsFalse(_blaster.TryFire(), "Still locked before 2.2s.");
            Assert.Less(_blaster.Heat.Heat, 100f, "Heat cools during lockout after the delay.");

            yield return new WaitForSeconds(0.35f);
            ResetCooldown();
            Assert.IsFalse(_blaster.Heat.IsOverheated);
            Assert.IsTrue(_blaster.TryFire(), "Lockout over: firing resumes.");
        }

        [UnityTest]
        public IEnumerator Cooling_StartsAfterDelay_AtConfiguredRate()
        {
            for (var i = 0; i < 5; i++)
            {
                ResetCooldown();
                _blaster.TryFire();
            }
            Assert.AreEqual(35f, _blaster.Heat.Heat, 0.0001f);

            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(35f, _blaster.Heat.Heat, 0.0001f, "Inside the 0.6s delay nothing cools.");

            yield return new WaitForSeconds(0.7f);
            var expected = 35f - 35f * 0.5f;
            Assert.AreEqual(expected, _blaster.Heat.Heat, 4f, "≈0.5s of cooling at 35/s (frame timing tolerance).");
            Assert.Less(_blaster.Heat.Heat, 35f);
        }

        [UnityTest]
        public IEnumerator HolsteredBlaster_KeepsHeat_AndKeepsCooling_NoResetOnReequip()
        {
            for (var i = 0; i < 8; i++)
            {
                ResetCooldown();
                _blaster.TryFire();
            }
            Assert.AreEqual(56f, _blaster.Heat.Heat, 0.0001f);

            _input.RaiseWeapon2Selected();
            Assert.IsFalse(_blaster.IsEquipped);
            Assert.AreEqual(56f, _blaster.Heat.Heat, 0.0001f, "Unequip does not reset heat.");
            ResetCooldown();
            Assert.IsFalse(_blaster.TryFire(), "Inactive blaster cannot fire.");

            yield return new WaitForSeconds(1.2f);
            var holsteredHeat = _blaster.Heat.Heat;
            Assert.Less(holsteredHeat, 56f - 10f, "A holstered blaster continues cooling in the background.");

            _input.RaiseWeapon1Selected();
            Assert.IsTrue(_blaster.IsEquipped);
            Assert.AreEqual(holsteredHeat, _blaster.Heat.Heat, 0.5f, "Re-equip neither resets nor duplicates heat.");
            ResetCooldown();
            Assert.IsTrue(_blaster.TryFire());
            Assert.AreEqual(holsteredHeat + 7f, _blaster.Heat.Heat, 0.5f);
        }

        [UnityTest]
        public IEnumerator HeldFire_WhileHolstered_DoesNotShoot()
        {
            _input.RaiseWeapon2Selected();
            _input.FireHeld = true;
            yield return new WaitForSeconds(0.3f);

            Assert.AreEqual(0f, _blaster.Heat.Heat, 0.0001f);
            Assert.IsNull(_blaster.LastSpawnedProjectile);
        }
    }
}
