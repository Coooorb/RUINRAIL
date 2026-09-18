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
    /// AR-17 reference: proves the generalized RangedWeapon handles a second catalog gun (8–10, 7.5/s, 30, 1.8s, 12, 22, Medium)
    /// with no assault-rifle-specific behaviour. Values mirror AR17.asset (validated in EditMode).
    /// </summary>
    public class AssaultRifleReferenceTests
    {
        private GameObject _playerObject;
        private RangedWeapon _ar17;
        private MeleeWeapon _knife;
        private WeaponLoadout _loadout;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private MeleeWeaponDefinition _knifeDefinition;
        private AmmoItemDefinition _mediumAmmo;
        private AmmoBalanceConfig _ammoBalance;
        private PlayerInventory _inventory;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = Track(ScriptableObject.CreateInstance<AmmoBalanceConfig>());
            _mediumAmmo = Track(ScriptableObject.CreateInstance<AmmoItemDefinition>());
            Set(typeof(ItemDefinition), _mediumAmmo, "_id", "ammo_medium");
            Set(typeof(ItemDefinition), _mediumAmmo, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), _mediumAmmo, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), _mediumAmmo, "_ammoType", AmmoType.Medium);

            _definition = Track(ScriptableObject.CreateInstance<RangedWeaponDefinition>());
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_ar_17");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.AssaultRifle);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 8);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 10);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 7.5f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 30);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 1.8f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 12f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 22f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Medium);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 1);

            _knifeDefinition = Track(ScriptableObject.CreateInstance<MeleeWeaponDefinition>());
            Set(typeof(ItemDefinition), _knifeDefinition, "_id", "weapon_field_knife");
            Set(typeof(ItemDefinition), _knifeDefinition, "_category", ItemCategory.Weapon);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_damageMin", 14);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_damageMax", 17);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackRate", 3.5f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackRange", 1.2f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_attackArcDegrees", 80f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_windUpSeconds", 0.08f);
            Set(typeof(MeleeWeaponDefinition), _knifeDefinition, "_recoverySeconds", 0.12f);

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _mediumAmmo, _definition, _knifeDefinition });
            _inventory = PlayerInventory.FromRegistry(registry, _ammoBalance);
            _inventory.Add(AmmoType.Medium, 100);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _ar17 = _playerObject.AddComponent<RangedWeapon>();
            _knife = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();

            _input = new FakePlayerInputReader();
            _aiming.SetInputReader(_input);

            _ar17.SetInputReader(_input);
            _ar17.SetProjectilePool(pool);
            _ar17.SetAiming(_aiming);
            _ar17.SetAmmoReserve(_inventory);
            _ar17.SetDamageRoller(new UnityRandomDamageRoller());
            _ar17.SetDefinition(_definition);

            _knife.SetInputReader(_input);
            _knife.SetAiming(_aiming);
            _knife.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _knife.SetDefinition(_knifeDefinition);

            _loadout.SetInputReader(_input);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null) Object.DestroyImmediate(_playerObject);
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        private static void Set(System.Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private void ArmLoadout(bool arInPrimary)
        {
            if (arInPrimary)
            {
                _loadout.SetPrimary(_ar17);
                _loadout.SetSecondary(_knife);
            }
            else
            {
                _loadout.SetPrimary(_knife);
                _loadout.SetSecondary(_ar17);
            }

            _loadout.Initialize();
        }

        [Test]
        public void Fire_ConsumesMagazineAndSpawnsPooledVisibleProjectile_WithCatalogSpeedAndRange()
        {
            ArmLoadout(arInPrimary: true);
            Assert.AreEqual(30, _ar17.MagazineAmmo, "Magazine starts full at catalog size.");

            Assert.IsTrue(_ar17.TryFire());

            Assert.AreEqual(29, _ar17.MagazineAmmo);
            var projectile = _ar17.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            Assert.IsTrue(projectile.gameObject.activeSelf);
            Assert.AreEqual(22f, projectile.Data.Speed, 0.001f);
            Assert.AreEqual(12f, projectile.Data.MaxRange, 0.001f);
            Assert.AreEqual(100, _inventory.Get(AmmoType.Medium), "Firing draws from the magazine, never directly from reserve.");
        }

        [UnityTest]
        public IEnumerator Reload_ConsumesMediumAmmoFromReserve_ForMissingRoundsOnly()
        {
            ArmLoadout(arInPrimary: true);
            for (var i = 0; i < 5; i++)
            {
                Assert.IsTrue(_ar17.TryFire());
                yield return new WaitForSeconds(1f / 7.5f + 0.01f);
            }

            Assert.AreEqual(25, _ar17.MagazineAmmo);
            Assert.IsTrue(_ar17.TryStartReload());
            Assert.IsTrue(_ar17.IsReloading);
            yield return new WaitForSeconds(1.8f + 0.1f);

            Assert.IsFalse(_ar17.IsReloading);
            Assert.AreEqual(30, _ar17.MagazineAmmo);
            Assert.AreEqual(95, _inventory.Get(AmmoType.Medium), "Reload takes exactly the 5 missing Medium rounds.");
            Assert.AreEqual(95, _inventory.CountOf("ammo_medium"));
        }

        [UnityTest]
        public IEnumerator FireRate_GatesAtSevenPointFivePerSecond()
        {
            ArmLoadout(arInPrimary: true);
            Assert.IsTrue(_ar17.TryFire());
            Assert.IsFalse(_ar17.TryFire(), "Second shot in the same frame must be blocked.");

            yield return new WaitForSeconds(0.06f);
            Assert.IsFalse(_ar17.TryFire(), "Interval is 1/7.5 ≈ 0.133s; 0.06s is too early.");

            yield return new WaitForSeconds(0.1f);
            Assert.IsTrue(_ar17.TryFire());
        }

        [Test]
        public void Damage_RollsOnlyEightNineOrTen()
        {
            ArmLoadout(arInPrimary: true);
            var seen = new HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                // Reset cooldown/magazine directly so the roll path is exercised without waiting on fire rate.
                Set(typeof(RangedWeapon), _ar17, "_fireCooldownRemaining", 0f);
                typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_ar17, 30);
                Assert.IsTrue(_ar17.TryFire());
                seen.Add(_ar17.LastSpawnedProjectile.Data.Damage);
            }

            CollectionAssert.IsSubsetOf(seen, new[] { 8, 9, 10 });
            CollectionAssert.AreEquivalent(new[] { 8, 9, 10 }, seen.OrderBy(v => v), "All three whole values must be reachable.");
        }

        [Test]
        public void AR17_InSecondarySlot_FiresWhenActive_AndPrimaryKnifeDoesNot()
        {
            ArmLoadout(arInPrimary: false);
            Assert.AreEqual(WeaponSlot.Primary, _loadout.ActiveSlot);
            Assert.IsFalse(_ar17.IsEquipped);
            Assert.IsFalse(_ar17.TryFire(), "Inactive AR in Secondary cannot fire.");
            Assert.IsFalse(_ar17.TryStartReload(), "Inactive AR in Secondary cannot reload.");
            Assert.AreEqual(30, _ar17.MagazineAmmo);

            _input.RaiseWeapon2Selected();

            Assert.AreEqual(WeaponSlot.Secondary, _loadout.ActiveSlot);
            Assert.IsTrue(_ar17.IsEquipped);
            Assert.IsTrue(_ar17.TryFire());
            Assert.AreEqual(29, _ar17.MagazineAmmo);
        }

        [Test]
        public void AR17_InPrimarySlot_CannotFireOrReloadAfterSwitchingToSecondary()
        {
            ArmLoadout(arInPrimary: true);
            Assert.IsTrue(_ar17.TryFire());
            Assert.IsTrue(_ar17.TryStartReload());
            Assert.IsTrue(_ar17.IsReloading);

            _input.RaiseWeapon2Selected();

            Assert.IsFalse(_ar17.IsEquipped);
            Assert.IsFalse(_ar17.IsReloading, "Switching away cancels the reload.");
            Assert.AreEqual(100, _inventory.Get(AmmoType.Medium), "Cancelled reload consumed no reserve.");
            Assert.AreEqual(29, _ar17.MagazineAmmo);
            Assert.IsFalse(_ar17.TryFire());
            Assert.IsFalse(_ar17.TryStartReload());
        }

        [UnityTest]
        public IEnumerator Projectile_TerminatesOnEnvironmentObstacle_WithoutDamage()
        {
            ArmLoadout(arInPrimary: true);
            var wall = new GameObject("Wall");
            wall.transform.position = new Vector3(2f, 0f, 0f);
            var collider = wall.AddComponent<BoxCollider2D>();
            collider.size = new Vector2(0.5f, 4f);
            wall.AddComponent<EnvironmentObstacle>();

            var target = new GameObject("Target");
            target.transform.position = new Vector3(4f, 0f, 0f);
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            var damageable = target.AddComponent<TestDamageableTarget>();

            _input.Aim = Vector2.right;
            _input.IsAimFromPointer = false;
            yield return null;

            Assert.IsTrue(_ar17.TryFire());
            var projectile = _ar17.LastSpawnedProjectile;
            yield return new WaitForSeconds(0.5f);

            Assert.IsTrue(projectile.IsResolved || !projectile.gameObject.activeSelf, "Projectile must be stopped by the wall.");
            Assert.AreEqual(0, damageable.HitCount, "Target behind the wall must not be hit.");

            Object.DestroyImmediate(wall);
            Object.DestroyImmediate(target);
        }
    }
}
