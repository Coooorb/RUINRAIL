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
    public class RangedWeaponInventoryAmmoTests
    {
        private GameObject _playerObject;
        private RangedWeaponDefinition _definition;
        private AmmoItemDefinition _lightAmmo;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _lightAmmo = ScriptableObject.CreateInstance<AmmoItemDefinition>();
            Set(typeof(ItemDefinition), _lightAmmo, "_id", "ammo_light");
            Set(typeof(ItemDefinition), _lightAmmo, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), _lightAmmo, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), _lightAmmo, "_ammoType", AmmoType.Light);

            _definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 12);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 14);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 4f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 12);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 0.1f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 10f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 20f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Light);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 1);
        }

        [TearDown]
        public void TearDown()
        {
            if (_playerObject != null) Object.DestroyImmediate(_playerObject);
            Object.DestroyImmediate(_definition);
            Object.DestroyImmediate(_lightAmmo);
            Object.DestroyImmediate(_ammoBalance);
        }

        private static void Set(System.Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        [UnityTest]
        public IEnumerator Reload_ConsumesAmmoFromBackpackStacks_ThroughInventoryReserveSeam()
        {
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { _lightAmmo });
            var inventory = PlayerInventory.FromRegistry(registry, _ammoBalance);
            inventory.Add(AmmoType.Light, 20);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            var aiming = _playerObject.AddComponent<PlayerAiming>();
            var weapon = _playerObject.AddComponent<RangedWeapon>();
            var input = new FakePlayerInputReader();
            aiming.SetInputReader(input);
            weapon.SetInputReader(input);
            weapon.SetProjectilePool(pool);
            weapon.SetAiming(aiming);
            weapon.SetAmmoReserve(inventory);
            weapon.SetDamageRoller(new FixedDamageRoller { FixedValue = 13 });
            weapon.SetDefinition(_definition);

            Assert.IsTrue(weapon.TryFire());
            Assert.AreEqual(11, weapon.MagazineAmmo);

            Assert.IsTrue(weapon.TryStartReload());
            yield return new WaitForSeconds(0.25f);

            Assert.AreEqual(12, weapon.MagazineAmmo);
            Assert.AreEqual(19, inventory.Get(AmmoType.Light), "Reload must draw from the backpack ammo stack.");
            Assert.AreEqual(19, inventory.CountOf("ammo_light"));
        }
    }
}
