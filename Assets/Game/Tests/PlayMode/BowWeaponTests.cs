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
    /// Recurve Bow reference: quick 10–12, full draw 25–30, full charge 0.65s, no ammo. Values mirror RecurveBow.asset.
    /// </summary>
    public class BowWeaponTests
    {
        private GameObject _playerObject;
        private BowWeapon _bow;
        private MeleeWeapon _knife;
        private WeaponLoadout _loadout;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private BowWeaponDefinition _definition;
        private MeleeWeaponDefinition _knifeDefinition;
        private AmmoReserve _ammoReserve;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<BowWeaponDefinition>();
            _created.Add(_definition);
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_recurve_bow");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.Bow);
            Set(typeof(BowWeaponDefinition), _definition, "_quickDamageMin", 10);
            Set(typeof(BowWeaponDefinition), _definition, "_quickDamageMax", 12);
            Set(typeof(BowWeaponDefinition), _definition, "_fullDrawDamageMin", 25);
            Set(typeof(BowWeaponDefinition), _definition, "_fullDrawDamageMax", 30);
            Set(typeof(BowWeaponDefinition), _definition, "_fullChargeSeconds", 0.65f);
            Set(typeof(BowWeaponDefinition), _definition, "_quickProjectileSpeed", 16f);
            Set(typeof(BowWeaponDefinition), _definition, "_fullProjectileSpeed", 24f);
            Set(typeof(BowWeaponDefinition), _definition, "_quickRange", 8f);
            Set(typeof(BowWeaponDefinition), _definition, "_fullRange", 12f);

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
            _bow = _playerObject.AddComponent<BowWeapon>();
            _knife = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();

            _input = new FakePlayerInputReader { Aim = new Vector2(0f, 1f), IsAimFromPointer = false };
            _aiming.SetInputReader(_input);

            _ammoReserve = new AmmoReserve();
            foreach (AmmoType t in System.Enum.GetValues(typeof(AmmoType))) _ammoReserve.Add(t, 10);

            _bow.SetInputReader(_input);
            _bow.SetProjectilePool(pool);
            _bow.SetAiming(_aiming);
            _bow.SetDamageRoller(new UnityRandomDamageRoller());
            _bow.SetDefinition(_definition);

            _knife.SetInputReader(_input);
            _knife.SetAiming(_aiming);
            _knife.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _knife.SetDefinition(_knifeDefinition);

            _loadout.SetInputReader(_input);
            _loadout.SetPrimary(_bow);
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

        private int TotalAmmo() => System.Enum.GetValues(typeof(AmmoType)).Cast<AmmoType>().Sum(t => _ammoReserve.Get(t));

        [Test]
        public void QuickRelease_ProducesOnlyQuickDamage_OneProjectile_AndResetsCharge()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 200; i++)
            {
                Assert.IsTrue(_bow.TryStartCharge());
                Assert.IsTrue(_bow.IsCharging);
                Assert.IsTrue(_bow.TryRelease());
                Assert.AreEqual(1, _bow.LastSpawnedProjectiles.Count, "One release = one projectile.");
                Assert.IsFalse(_bow.IsCharging);
                Assert.AreEqual(0f, _bow.ChargeSeconds, 0.0001f, "Charge resets after the shot.");
                seen.Add(_bow.LastSpawnedProjectile.Data.Damage);
                Assert.AreEqual(16f, _bow.LastSpawnedProjectile.Data.Speed, 0.001f);
                Assert.AreEqual(8f, _bow.LastSpawnedProjectile.Data.MaxRange, 0.001f);
            }

            CollectionAssert.AreEquivalent(new[] { 10, 11, 12 }, seen.OrderBy(v => v));
        }

        [Test]
        public void FullCharge_ProducesOnlyFullDrawDamage_AndOverchargeDoesNotExceedIt()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 200; i++)
            {
                Assert.IsTrue(_bow.TryStartCharge());
                _bow.AdvanceCharge(i % 2 == 0 ? 0.65f : 3f);
                Assert.IsTrue(_bow.IsFullyCharged);
                Assert.AreEqual(1f, _bow.ChargeFraction, 0.0001f, "Charge is clamped at full.");
                Assert.IsTrue(_bow.TryRelease());
                seen.Add(_bow.LastSpawnedProjectile.Data.Damage);
                Assert.AreEqual(24f, _bow.LastSpawnedProjectile.Data.Speed, 0.001f);
                Assert.AreEqual(12f, _bow.LastSpawnedProjectile.Data.MaxRange, 0.001f);
            }

            Assert.IsTrue(seen.All(d => d >= 25 && d <= 30), string.Join(",", seen));
            Assert.Contains(25, seen.ToList());
            Assert.Contains(30, seen.ToList());
        }

        [Test]
        public void PartialCharge_LandsStrictlyBetweenQuickAndFull()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 100; i++)
            {
                _bow.TryStartCharge();
                _bow.AdvanceCharge(0.325f);
                _bow.TryRelease();
                seen.Add(_bow.LastSpawnedProjectile.Data.Damage);
            }

            Assert.IsTrue(seen.All(d => d > 12 && d < 25), string.Join(",", seen));
        }

        [UnityTest]
        public IEnumerator HoldingFire_ChargesOverRealTime_AndReachesFullAtApprovedTime()
        {
            _input.FireHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(_bow.IsCharging);

            yield return new WaitForSeconds(0.4f);
            Assert.IsFalse(_bow.IsFullyCharged, "0.4s < 0.65s");
            Assert.Greater(_bow.ChargeFraction, 0.4f);

            yield return new WaitForSeconds(0.35f);
            Assert.IsTrue(_bow.IsFullyCharged, "≥0.65s held reaches full draw.");

            _input.FireHeld = false;
            yield return null;

            Assert.IsFalse(_bow.IsCharging);
            Assert.IsNotNull(_bow.LastSpawnedProjectile);
            Assert.IsTrue(_bow.LastSpawnedProjectile.gameObject.activeSelf, "Visible pooled projectile in flight.");
            Assert.GreaterOrEqual(_bow.LastSpawnedProjectile.Data.Damage, 25);
            Assert.LessOrEqual(_bow.LastSpawnedProjectile.Data.Damage, 30);
            Assert.AreEqual(1f, Vector2.Dot(_bow.LastSpawnedProjectile.Data.Direction.normalized, Vector2.up), 0.01f, "Follows AimDirection.");
        }

        [Test]
        public void Bow_ConsumesNoAmmo_AndHasNoReload()
        {
            var before = TotalAmmo();
            _bow.TryStartCharge();
            _bow.TryRelease();
            _input.RaiseReload();

            Assert.AreEqual(before, TotalAmmo());
            Assert.IsNull(typeof(BowWeapon).GetMethod("TryStartReload"));
            Assert.IsNull(typeof(BowWeapon).GetProperty("MagazineAmmo"));
            Assert.IsNull(typeof(BowWeapon).GetMethod("SetAmmoReserve"));
        }

        [UnityTest]
        public IEnumerator SwitchingAway_CancelsPendingCharge_WithoutDelayedShot()
        {
            _input.FireHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(_bow.IsCharging);

            _input.RaiseWeapon2Selected();

            Assert.IsFalse(_bow.IsEquipped);
            Assert.IsFalse(_bow.IsCharging);
            Assert.AreEqual(0f, _bow.ChargeSeconds, 0.0001f);

            yield return new WaitForSeconds(0.3f);
            _input.FireHeld = false;
            yield return new WaitForSeconds(0.2f);

            Assert.IsNull(_bow.LastSpawnedProjectile, "No shot may fire after switching away.");
            Assert.IsFalse(_bow.TryStartCharge(), "Inactive bow cannot start a draw.");
        }

        [UnityTest]
        public IEnumerator SwitchingBack_WhileFireStillHeld_DoesNotStartANewDrawUntilRepressed()
        {
            _input.RaiseWeapon2Selected();
            _input.FireHeld = true;
            yield return null;
            _input.RaiseWeapon1Selected();
            yield return null;
            yield return null;

            Assert.IsFalse(_bow.IsCharging, "A held Fire carried over from the other weapon is not a draw.");

            _input.FireHeld = false;
            yield return null;
            Assert.IsNull(_bow.LastSpawnedProjectile);

            _input.FireHeld = true;
            yield return null;
            yield return null;
            Assert.IsTrue(_bow.IsCharging);
        }
    }
}
