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
    /// TASK 052 — Rattler-9 SMG reference (6–8, 10/s, 32, 1.6 s, 8, 18, Light) on the shared RangedWeapon: held fire is
    /// gated to the configured cadence, every shot costs one magazine round, and the gun works in either slot.
    /// </summary>
    public class SmgReferenceTests
    {
        private GameObject _playerObject;
        private RangedWeapon _smg;
        private MeleeWeapon _knife;
        private WeaponLoadout _loadout;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private PlayerInventory _inventory;
        private readonly List<Object> _created = new();
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            var ammoBalance = Track(ScriptableObject.CreateInstance<AmmoBalanceConfig>());
            var lightAmmo = Track(ScriptableObject.CreateInstance<AmmoItemDefinition>());
            Set(typeof(ItemDefinition), lightAmmo, "_id", "ammo_light");
            Set(typeof(ItemDefinition), lightAmmo, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), lightAmmo, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), lightAmmo, "_ammoType", AmmoType.Light);

            _definition = Track(ScriptableObject.CreateInstance<RangedWeaponDefinition>());
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_rattler_9");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.Smg);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 6);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 8);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 10f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 32);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 1.6f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 8f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 18f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Light);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 1);

            var knifeDefinition = Track(ScriptableObject.CreateInstance<MeleeWeaponDefinition>());
            Set(typeof(ItemDefinition), knifeDefinition, "_id", "weapon_field_knife");
            Set(typeof(ItemDefinition), knifeDefinition, "_category", ItemCategory.Weapon);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_damageMin", 14);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_damageMax", 17);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_attackRate", 3.5f);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_attackRange", 1.2f);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_attackArcDegrees", 80f);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_windUpSeconds", 0.08f);
            Set(typeof(MeleeWeaponDefinition), knifeDefinition, "_recoverySeconds", 0.12f);

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { lightAmmo, _definition, knifeDefinition });
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            _inventory.Add(AmmoType.Light, 120);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            var aiming = _playerObject.AddComponent<PlayerAiming>();
            _smg = _playerObject.AddComponent<RangedWeapon>();
            _knife = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();
            _input = new FakePlayerInputReader();
            aiming.SetInputReader(_input);

            _smg.SetInputReader(_input);
            _smg.SetProjectilePool(pool);
            _smg.SetAiming(aiming);
            _smg.SetAmmoReserve(_inventory);
            _smg.SetDamageRoller(new UnityRandomDamageRoller());
            _smg.SetDefinition(_definition);

            _knife.SetInputReader(_input);
            _knife.SetAiming(aiming);
            _knife.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _knife.SetDefinition(knifeDefinition);
            _loadout.SetInputReader(_input);

            _previousCaptureDeltaTime = Time.captureDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
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

        private void ArmLoadout(bool smgInPrimary)
        {
            if (smgInPrimary)
            {
                _loadout.SetPrimary(_smg);
                _loadout.SetSecondary(_knife);
            }
            else
            {
                _loadout.SetPrimary(_knife);
                _loadout.SetSecondary(_smg);
            }

            _loadout.Initialize();
        }

        // ---- Acceptance 3: projectile speed / range / damage / ammo ----

        [Test]
        public void Fire_SpawnsPooledProjectile_WithCatalogSpeedRange_AndLightAmmoOnlyOnReload()
        {
            ArmLoadout(smgInPrimary: true);
            Assert.AreEqual(32, _smg.MagazineAmmo);
            Assert.IsTrue(_smg.TryFire());
            Assert.AreEqual(31, _smg.MagazineAmmo);
            var projectile = _smg.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            Assert.AreEqual(18f, projectile.Data.Speed, 0.001f);
            Assert.AreEqual(8f, projectile.Data.MaxRange, 0.001f);
            Assert.AreEqual(120, _inventory.Get(AmmoType.Light), "Firing never draws from the reserve directly.");
            Assert.AreEqual(AmmoType.Light, _definition.AmmoType);
        }

        [Test]
        public void Damage_RollsOnlySixSevenOrEight_AsIntegers()
        {
            ArmLoadout(smgInPrimary: true);
            var seen = new HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                Set(typeof(RangedWeapon), _smg, "_fireCooldownRemaining", 0f);
                typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_smg, 32);
                Assert.IsTrue(_smg.TryFire());
                seen.Add(_smg.LastSpawnedProjectile.Data.Damage);
            }

            CollectionAssert.AreEquivalent(new[] { 6, 7, 8 }, seen.OrderBy(v => v));
        }

        // ---- Acceptance 2: held fire is capped at 10/s under deterministic frame timing; one round per shot ----

        [UnityTest]
        public IEnumerator HeldFire_NeverExceedsTenShotsPerSecond_AndConsumesOneRoundPerShot()
        {
            ArmLoadout(smgInPrimary: true);
            Time.captureDeltaTime = 0.01f; // 100 frames = exactly one simulated second
            _input.FireHeld = true;
            var spawnedBefore = 0;
            var shots = 0;
            for (var frame = 0; frame < 100; frame++)
            {
                yield return null;
                var now = 32 - _smg.MagazineAmmo;
                Assert.LessOrEqual(now - spawnedBefore, 1, "Never two shots in one frame.");
                spawnedBefore = now;
                shots = now;
            }

            Assert.LessOrEqual(shots, 10, "At most 10 successful shots in one second.");
            Assert.GreaterOrEqual(shots, 9, "Held fire actually sustains the cadence.");
            Assert.AreEqual(32 - shots, _smg.MagazineAmmo, "Exactly one magazine round per shot.");
            Assert.AreEqual(120, _inventory.Get(AmmoType.Light));

            for (var frame = 0; frame < 100; frame++) yield return null;
            var twoSeconds = 32 - _smg.MagazineAmmo;
            Assert.LessOrEqual(twoSeconds, 20);
            Assert.GreaterOrEqual(twoSeconds, 19);

            // Emptying the magazine: 32 rounds take ~3.2 s; held fire cannot skip ammo or fire dry.
            for (var frame = 0; frame < 200; frame++) yield return null;
            Assert.AreEqual(0, _smg.MagazineAmmo);
            Assert.AreEqual(120, _inventory.Get(AmmoType.Light), "Dry magazine never bypasses into the reserve.");
            _input.FireHeld = false;
        }

        [UnityTest]
        public IEnumerator Reload_TakesOnePointSixSeconds_AndOnlyTheMissingLightRounds()
        {
            ArmLoadout(smgInPrimary: true);
            Time.captureDeltaTime = 0.01f;
            for (var i = 0; i < 12; i++)
            {
                Set(typeof(RangedWeapon), _smg, "_fireCooldownRemaining", 0f);
                Assert.IsTrue(_smg.TryFire());
            }

            Assert.AreEqual(20, _smg.MagazineAmmo);
            Assert.IsTrue(_smg.TryStartReload());
            Assert.AreEqual(1.6f, _smg.CurrentReloadTime, 0.001f);
            for (var frame = 0; frame < 155; frame++) yield return null;
            Assert.IsTrue(_smg.IsReloading, "1.55 s: still reloading.");
            for (var frame = 0; frame < 10; frame++) yield return null;
            Assert.IsFalse(_smg.IsReloading);
            Assert.AreEqual(32, _smg.MagazineAmmo);
            Assert.AreEqual(108, _inventory.Get(AmmoType.Light), "Exactly the 12 missing Light rounds.");
        }

        // ---- Acceptance 4: either slot ----

        [Test]
        public void Rattler9_WorksInEitherSlot_AndOnlyWhenActive()
        {
            ArmLoadout(smgInPrimary: false);
            Assert.IsFalse(_smg.IsEquipped);
            Assert.IsFalse(_smg.TryFire(), "Inactive SMG in Secondary cannot fire.");
            _input.RaiseWeapon2Selected();
            Assert.IsTrue(_smg.IsEquipped);
            Assert.IsTrue(_smg.TryFire());
            Assert.AreEqual(31, _smg.MagazineAmmo);

            _input.RaiseWeapon1Selected();
            Assert.IsFalse(_smg.IsEquipped);
            Set(typeof(RangedWeapon), _smg, "_fireCooldownRemaining", 0f);
            Assert.IsFalse(_smg.TryFire(), "Only the active weapon consumes attack input.");
            Assert.AreEqual(31, _smg.MagazineAmmo);
        }
    }
}
