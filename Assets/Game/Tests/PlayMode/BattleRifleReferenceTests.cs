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
    /// TASK 053 — Sentinel BR reference (18–22, 3/s, 12, 2.0 s, 15, 26, Medium): the slow/heavy end of the shared
    /// RangedWeapon with no battle-rifle code, full 360° aim and wall-blocked projectiles.
    /// </summary>
    public class BattleRifleReferenceTests
    {
        private GameObject _playerObject;
        private RangedWeapon _br;
        private MeleeWeapon _knife;
        private WeaponLoadout _loadout;
        private FakePlayerInputReader _input;
        private PlayerAiming _aiming;
        private RangedWeaponDefinition _definition;
        private PlayerInventory _inventory;
        private readonly List<Object> _created = new();
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            var ammoBalance = Track(ScriptableObject.CreateInstance<AmmoBalanceConfig>());
            var mediumAmmo = Track(ScriptableObject.CreateInstance<AmmoItemDefinition>());
            Set(typeof(ItemDefinition), mediumAmmo, "_id", "ammo_medium");
            Set(typeof(ItemDefinition), mediumAmmo, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), mediumAmmo, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), mediumAmmo, "_ammoType", AmmoType.Medium);

            _definition = Track(ScriptableObject.CreateInstance<RangedWeaponDefinition>());
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_sentinel_br");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.BattleRifle);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 18);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 22);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 3f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 12);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 2f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 15f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 26f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Medium);
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

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { mediumAmmo, _definition, knifeDefinition });
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            _inventory.Add(AmmoType.Medium, 60);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _br = _playerObject.AddComponent<RangedWeapon>();
            _knife = _playerObject.AddComponent<MeleeWeapon>();
            _loadout = _playerObject.AddComponent<WeaponLoadout>();
            _input = new FakePlayerInputReader();
            _aiming.SetInputReader(_input);

            _br.SetInputReader(_input);
            _br.SetProjectilePool(pool);
            _br.SetAiming(_aiming);
            _br.SetAmmoReserve(_inventory);
            _br.SetDamageRoller(new UnityRandomDamageRoller());
            _br.SetDefinition(_definition);

            _knife.SetInputReader(_input);
            _knife.SetAiming(_aiming);
            _knife.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            _knife.SetDefinition(knifeDefinition);
            _loadout.SetInputReader(_input);
            _loadout.SetPrimary(_br);
            _loadout.SetSecondary(_knife);
            _loadout.Initialize();
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

        // ---- Acceptance 1 + 3: catalog values at runtime, Medium ammo, 2.0 s reload ----

        [UnityTest]
        public IEnumerator Fire_UsesCatalogSpeedAndRange_AndReloadTakesTwoSecondsOfMediumAmmo()
        {
            Assert.AreEqual(12, _br.MagazineAmmo);
            Assert.IsTrue(_br.TryFire());
            Assert.AreEqual(11, _br.MagazineAmmo);
            Assert.AreEqual(26f, _br.LastSpawnedProjectile.Data.Speed, 0.001f);
            Assert.AreEqual(15f, _br.LastSpawnedProjectile.Data.MaxRange, 0.001f);
            Assert.AreEqual(60, _inventory.Get(AmmoType.Medium));

            Time.captureDeltaTime = 0.01f;
            for (var i = 0; i < 3; i++)
            {
                Set(typeof(RangedWeapon), _br, "_fireCooldownRemaining", 0f);
                Assert.IsTrue(_br.TryFire());
            }

            Assert.AreEqual(8, _br.MagazineAmmo);
            Assert.IsTrue(_br.TryStartReload());
            Assert.AreEqual(2.0f, _br.CurrentReloadTime, 0.001f);
            for (var frame = 0; frame < 195; frame++) yield return null;
            Assert.IsTrue(_br.IsReloading, "1.95 s: still reloading.");
            for (var frame = 0; frame < 10; frame++) yield return null;
            Assert.IsFalse(_br.IsReloading);
            Assert.AreEqual(12, _br.MagazineAmmo);
            Assert.AreEqual(56, _inventory.Get(AmmoType.Medium), "Exactly the 4 missing Medium rounds.");
        }

        // ---- Acceptance 2: integer 18-22 and 3/s cadence ----

        [Test]
        public void Damage_RollsOnlyIntegersEighteenToTwentyTwo()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 400; i++)
            {
                Set(typeof(RangedWeapon), _br, "_fireCooldownRemaining", 0f);
                typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_br, 12);
                Assert.IsTrue(_br.TryFire());
                seen.Add(_br.LastSpawnedProjectile.Data.Damage);
            }

            CollectionAssert.AreEquivalent(new[] { 18, 19, 20, 21, 22 }, seen.OrderBy(v => v));
        }

        [UnityTest]
        public IEnumerator HeldFire_IsGatedToThreePerSecond()
        {
            Time.captureDeltaTime = 0.01f;
            _input.FireHeld = true;
            for (var frame = 0; frame < 100; frame++) yield return null;
            var shots = 12 - _br.MagazineAmmo;
            Assert.LessOrEqual(shots, 3, "At most 3 shots in one second.");
            Assert.GreaterOrEqual(shots, 2);
            for (var frame = 0; frame < 100; frame++) yield return null;
            var twoSeconds = 12 - _br.MagazineAmmo;
            Assert.LessOrEqual(twoSeconds, 6);
            Assert.GreaterOrEqual(twoSeconds, 5);
            _input.FireHeld = false;

            Assert.IsFalse(_br.TryFire(), "Inside the 0.333 s window the trigger is dead.");
        }

        // ---- Requirement 4: 360-degree aim and wall blocking ----

        [UnityTest]
        public IEnumerator Aim_IsFull360_AndProjectilesStopAtWalls()
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = new Vector3(0f, 2f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(4f, 0.5f);
            wall.AddComponent<EnvironmentObstacle>();

            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = new Vector3(0f, 4f, 0f);
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            var hits = target.AddComponent<TestDamageableTarget>();

            foreach (var direction in new[] { new Vector2(-1f, -1f), new Vector2(0.3f, -1f), new Vector2(-1f, 0.2f), new Vector2(0.7f, 0.7f) })
            {
                _input.Aim = direction;
                yield return null;
                Set(typeof(RangedWeapon), _br, "_fireCooldownRemaining", 0f);
                Assert.IsTrue(_br.TryFire());
                Assert.AreEqual(direction.normalized.x, _br.LastSpawnedProjectile.Data.Direction.x, 0.01f);
                Assert.AreEqual(direction.normalized.y, _br.LastSpawnedProjectile.Data.Direction.y, 0.01f);
            }

            _input.Aim = Vector2.up;
            yield return null;
            Set(typeof(RangedWeapon), _br, "_fireCooldownRemaining", 0f);
            Assert.IsTrue(_br.TryFire());
            var projectile = _br.LastSpawnedProjectile;
            for (var i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(projectile.gameObject.activeSelf, "Terminated on the wall.");
            Assert.Less(projectile.transform.position.y, 2.4f);
            Assert.AreEqual(0, hits.HitCount, "The target behind the wall is never hit.");
        }
    }
}
