using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
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
    /// Breacher-12 reference: 6 pellets × 5–7, 1.4/s, magazine 6, reload 2.2s, range 6, speed 16, Shells.
    /// Values mirror Breacher12.asset (validated in EditMode).
    /// </summary>
    public class ShotgunReferenceTests
    {
        private const float SpreadDegrees = 30f;

        private GameObject _playerObject;
        private RangedWeapon _shotgun;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private AmmoReserve _ammoReserve;
        private readonly List<Object> _created = new();
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            _definition = ScriptableObject.CreateInstance<RangedWeaponDefinition>();
            _created.Add(_definition);
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_breacher_12");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.Shotgun);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 5);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 7);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 1.4f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 6);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 2.2f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 6f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 16f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Shells);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 1);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectilesPerShot", 6);
            Set(typeof(RangedWeaponDefinition), _definition, "_spreadDegrees", SpreadDegrees);

            _playerObject = new GameObject("TestPlayer");
            var pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _shotgun = _playerObject.AddComponent<RangedWeapon>();

            _input = new FakePlayerInputReader { Aim = Vector2.right, IsAimFromPointer = false };
            _aiming.SetInputReader(_input);

            _ammoReserve = new AmmoReserve();
            _ammoReserve.Add(AmmoType.Shells, 20);

            _shotgun.SetInputReader(_input);
            _shotgun.SetProjectilePool(pool);
            _shotgun.SetAiming(_aiming);
            _shotgun.SetAmmoReserve(_ammoReserve);
            _shotgun.SetDamageRoller(new UnityRandomDamageRoller());
            _shotgun.SetSpreadRandom(new SeededRandom(1234));
            _shotgun.SetDefinition(_definition);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            if (_playerObject != null) Object.DestroyImmediate(_playerObject);
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(System.Type type, object target, string field, object value)
        {
            type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private TestDamageableTarget SpawnTarget(Vector2 position, Vector2 size)
        {
            var go = new GameObject("Target");
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = size;
            _spawned.Add(go);
            return go.AddComponent<TestDamageableTarget>();
        }

        private void ResetShot()
        {
            Set(typeof(RangedWeapon), _shotgun, "_fireCooldownRemaining", 0f);
            typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_shotgun, 6);
        }

        [Test]
        public void OneShot_EmitsExactlySixPooledProjectiles_AndConsumesOneRound()
        {
            var reserveBefore = _ammoReserve.Get(AmmoType.Shells);

            Assert.IsTrue(_shotgun.TryFire());

            Assert.AreEqual(6, _shotgun.LastSpawnedProjectiles.Count);
            Assert.AreEqual(6, _shotgun.LastSpawnedProjectiles.Distinct().Count(), "Six distinct projectile instances.");
            Assert.IsTrue(_shotgun.LastSpawnedProjectiles.All(p => p.gameObject.activeSelf));
            Assert.AreEqual(5, _shotgun.MagazineAmmo, "Exactly one magazine round per shot.");
            Assert.AreEqual(reserveBefore, _ammoReserve.Get(AmmoType.Shells), "Reserve untouched by firing.");
            Assert.IsFalse(_shotgun.TryFire(), "Fire-rate gate applies to the whole shot.");
        }

        [Test]
        public void Pellets_RollIntegerDamageFiveToSeven_IndependentlyPerPellet_AndFollowSpreadAroundAim()
        {
            var seen = new HashSet<int>();
            var distinctWithinShot = false;
            var maxAngle = 0f;

            for (var shot = 0; shot < 60; shot++)
            {
                ResetShot();
                Assert.IsTrue(_shotgun.TryFire());
                var damages = _shotgun.LastSpawnedProjectiles.Select(p => p.Data.Damage).ToArray();
                foreach (var d in damages) seen.Add(d);
                distinctWithinShot |= damages.Distinct().Count() > 1;
                foreach (var p in _shotgun.LastSpawnedProjectiles)
                {
                    var angle = Vector2.Angle(Vector2.right, p.Data.Direction);
                    maxAngle = Mathf.Max(maxAngle, angle);
                    Assert.LessOrEqual(angle, SpreadDegrees / 2f + 0.001f);
                    Assert.AreEqual(16f, p.Data.Speed, 0.001f);
                    Assert.AreEqual(6f, p.Data.MaxRange, 0.001f);
                }
            }

            CollectionAssert.AreEquivalent(new[] { 5, 6, 7 }, seen.OrderBy(v => v));
            Assert.IsTrue(distinctWithinShot, "Damage is rolled per pellet, not once per shot.");
            Assert.Greater(maxAngle, 10f, "Pellets spread across the cone.");
        }

        [Test]
        public void Spread_IsReproducibleFromInjectedSeed()
        {
            Assert.IsTrue(_shotgun.TryFire());
            var first = _shotgun.LastSpawnedProjectiles.Select(p => p.Data.Direction).ToArray();

            ResetShot();
            _shotgun.SetSpreadRandom(new SeededRandom(1234));
            Assert.IsTrue(_shotgun.TryFire());
            var second = _shotgun.LastSpawnedProjectiles.Select(p => p.Data.Direction).ToArray();

            CollectionAssert.AreEqual(first, second);
        }

        [UnityTest]
        public IEnumerator Reload_TakesApprovedTime_AndConsumesOneShellPerMissingRound()
        {
            Assert.IsTrue(_shotgun.TryFire());
            ResetShot();
            typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_shotgun, 4);

            Assert.IsTrue(_shotgun.TryStartReload());
            yield return new WaitForSeconds(2.0f);
            Assert.IsTrue(_shotgun.IsReloading, "Reload must not finish before 2.2s.");
            yield return new WaitForSeconds(0.35f);

            Assert.IsFalse(_shotgun.IsReloading);
            Assert.AreEqual(6, _shotgun.MagazineAmmo);
            Assert.AreEqual(18, _ammoReserve.Get(AmmoType.Shells), "Two missing rounds cost two Shells.");
        }

        [UnityTest]
        public IEnumerator FireRate_GatesAtOnePointFourPerSecond()
        {
            Assert.IsTrue(_shotgun.TryFire());
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(_shotgun.TryFire(), "Interval is 1/1.4 ≈ 0.714s.");
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(_shotgun.TryFire());
        }

        [UnityTest]
        public IEnumerator WideTarget_CanBeHitByMultiplePellets_EachPelletHitsAtMostOnce()
        {
            var target = SpawnTarget(new Vector2(2f, 0f), new Vector2(0.5f, 6f));
            yield return null;

            Assert.IsTrue(_shotgun.TryFire());
            var pellets = _shotgun.LastSpawnedProjectiles.ToArray();
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(6, target.HitCount, "Every pellet of the shot hits the wide target exactly once.");
            Assert.IsTrue(pellets.All(p => p.IsResolved || !p.gameObject.activeSelf), "Resolved pellets stop and return to the pool.");
            Assert.AreEqual(0, pellets.Count(p => p.gameObject.activeSelf), "Pooled pellets are inactive after resolving.");
        }

        [UnityTest]
        public IEnumerator NarrowTarget_IsHitOnlyByPelletsAimedAtIt_OthersPassAndExpireAtRange()
        {
            var target = SpawnTarget(new Vector2(4f, 0f), new Vector2(0.3f, 0.3f));
            yield return null;

            Assert.IsTrue(_shotgun.TryFire());
            var pellets = _shotgun.LastSpawnedProjectiles.ToArray();
            // Pellet path crosses x=4 at y = tan(angle) * 4; pellet radius 0.15 + target half-height 0.15.
            float OffsetAtTarget(Projectile p) => Mathf.Abs(Mathf.Tan(Vector2.SignedAngle(Vector2.right, p.Data.Direction) * Mathf.Deg2Rad) * 4f);
            var surelyHit = pellets.Count(p => OffsetAtTarget(p) <= 0.25f);
            var possiblyHit = pellets.Count(p => OffsetAtTarget(p) <= 0.35f);
            yield return new WaitForSeconds(0.8f);

            Assert.GreaterOrEqual(target.HitCount, surelyHit, "Every pellet whose path crosses the target registers.");
            Assert.LessOrEqual(target.HitCount, possiblyHit, "Pellets that miss never register; no pellet registers twice.");
            Assert.Less(target.HitCount, 6, "A small target is not hit by the whole spread.");
            Assert.IsTrue(pellets.All(p => !p.gameObject.activeSelf), "All pellets expired at range 6 or on hit.");
        }

        [UnityTest]
        public IEnumerator Pellets_TerminateOnEnvironmentObstacle_WithoutDamage()
        {
            var wall = new GameObject("Wall");
            wall.transform.position = new Vector3(1.5f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.3f, 8f);
            wall.AddComponent<EnvironmentObstacle>();
            _spawned.Add(wall);
            var target = SpawnTarget(new Vector2(3f, 0f), new Vector2(0.5f, 8f));
            yield return null;

            Assert.IsTrue(_shotgun.TryFire());
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(0, target.HitCount);
        }
    }
}
