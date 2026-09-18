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
    /// TASK 054 — Longshot S1 reference (45–52, 1/s, 5, 2.4 s, 20, 34, Heavy): a visible pooled projectile even at 34 u/s,
    /// with a per-step sweep so thin walls and small targets are never tunnelled and never hit twice.
    /// </summary>
    public class SniperReferenceTests
    {
        private GameObject _playerObject;
        private RangedWeapon _sniper;
        private ProjectilePool _pool;
        private FakePlayerInputReader _input;
        private RangedWeaponDefinition _definition;
        private PlayerInventory _inventory;
        private readonly List<Object> _created = new();
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            var ammoBalance = Track(ScriptableObject.CreateInstance<AmmoBalanceConfig>());
            var heavyAmmo = Track(ScriptableObject.CreateInstance<AmmoItemDefinition>());
            Set(typeof(ItemDefinition), heavyAmmo, "_id", "ammo_heavy");
            Set(typeof(ItemDefinition), heavyAmmo, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), heavyAmmo, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), heavyAmmo, "_ammoType", AmmoType.Heavy);

            _definition = Track(ScriptableObject.CreateInstance<RangedWeaponDefinition>());
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_longshot_s1");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.Sniper);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 45);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 52);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 1f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 5);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 2.4f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 20f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 34f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Heavy);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 1);

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { heavyAmmo, _definition });
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            _inventory.Add(AmmoType.Heavy, 30);

            _playerObject = new GameObject("TestPlayer");
            _pool = _playerObject.AddComponent<ProjectilePool>();
            var aiming = _playerObject.AddComponent<PlayerAiming>();
            _sniper = _playerObject.AddComponent<RangedWeapon>();
            _input = new FakePlayerInputReader { Aim = Vector2.right };
            aiming.SetInputReader(_input);
            _sniper.SetInputReader(_input);
            _sniper.SetProjectilePool(_pool);
            _sniper.SetAiming(aiming);
            _sniper.SetAmmoReserve(_inventory);
            _sniper.SetDamageRoller(new UnityRandomDamageRoller());
            _sniper.SetDefinition(_definition);
            _previousCaptureDeltaTime = Time.captureDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
            if (_playerObject != null) Object.DestroyImmediate(_playerObject);
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
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

        private GameObject ThinWall(Vector2 position, float thickness)
        {
            var wall = new GameObject("ThinWall");
            _created.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(thickness, 3f);
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        private TestDamageableTarget SmallTarget(Vector2 position, float radius)
        {
            var target = new GameObject("SmallTarget");
            _created.Add(target);
            target.transform.position = position;
            target.AddComponent<CircleCollider2D>().radius = radius;
            return target.AddComponent<TestDamageableTarget>();
        }

        private static IEnumerator FixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        // ---- Acceptance 2 + 3: visible pooled projectile at 34/20; integer 45-52; 1/s; mag 5; reload 2.4 ----

        [UnityTest]
        public IEnumerator Fire_SpawnsVisiblePooledProjectile_ThatMovesAt34AndExpiresAt20()
        {
            Assert.AreEqual(5, _sniper.MagazineAmmo);
            Assert.IsTrue(_sniper.TryFire());
            var projectile = _sniper.LastSpawnedProjectile;
            Assert.IsNotNull(projectile);
            Assert.IsTrue(projectile.gameObject.activeSelf, "Visible moving object, not a hitscan ray.");
            Assert.AreEqual(34f, projectile.Data.Speed, 0.001f);
            Assert.AreEqual(20f, projectile.Data.MaxRange, 0.001f);
            Assert.AreEqual(4, _sniper.MagazineAmmo);
            Assert.AreEqual(30, _inventory.Get(AmmoType.Heavy));

            yield return FixedSteps(10); // 0.2 s -> ~6.8 units
            Assert.IsTrue(projectile.gameObject.activeSelf);
            Assert.AreEqual(34f * 10 * Time.fixedDeltaTime, projectile.transform.position.x, 0.75f, "Travels at 34 u/s.");
            yield return FixedSteps(25); // total 0.7 s -> 23.8 > 20
            Assert.IsFalse(projectile.gameObject.activeSelf, "Expired at range 20 and returned to the pool.");
            Assert.LessOrEqual(projectile.transform.position.x, 20.5f);
        }

        [Test]
        public void Damage_RollsOnlyIntegers45To52()
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 600; i++)
            {
                Set(typeof(RangedWeapon), _sniper, "_fireCooldownRemaining", 0f);
                typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_sniper, 5);
                Assert.IsTrue(_sniper.TryFire());
                seen.Add(_sniper.LastSpawnedProjectile.Data.Damage);
            }

            CollectionAssert.AreEquivalent(Enumerable.Range(45, 8), seen.OrderBy(v => v));
        }

        [UnityTest]
        public IEnumerator Cadence_IsOnePerSecond_AndReloadTakes2Point4SecondsOfHeavyAmmo()
        {
            Time.captureDeltaTime = 0.01f;
            _input.FireHeld = true;
            for (var frame = 0; frame < 100; frame++) yield return null;
            var shots = 5 - _sniper.MagazineAmmo;
            Assert.LessOrEqual(shots, 1, "At most one shot in the first second.");
            Assert.AreEqual(1, shots);
            // The fifth round empties the magazine; with Heavy reserve left the reload begins on its own at that moment.
            var framesSinceEmpty = -1;
            for (var frame = 0; frame < 350; frame++)
            {
                yield return null;
                if (framesSinceEmpty >= 0) framesSinceEmpty++;
                else if (_sniper.MagazineAmmo == 0) framesSinceEmpty = 0;
            }

            Assert.AreEqual(0, _sniper.MagazineAmmo, "Five rounds over ~4.5 s of held fire.");
            _input.FireHeld = false;
            Assert.GreaterOrEqual(framesSinceEmpty, 0, "the magazine ran dry inside the window");
            Assert.AreEqual(1, _sniper.AutoReloads, "the dry magazine started the reload automatically, once");
            Assert.IsTrue(_sniper.IsReloading);
            Assert.IsFalse(_sniper.TryStartReload(), "a manual reload during the auto reload is a no-op, not a second reload");
            Assert.AreEqual(2.4f, _sniper.CurrentReloadTime, 0.001f);
            Assert.AreEqual(30, _inventory.Get(AmmoType.Heavy), "Dry magazine never bypasses into the reserve; the reserve is consumed at reload completion.");

            while (framesSinceEmpty < 235) { yield return null; framesSinceEmpty++; }
            Assert.IsTrue(_sniper.IsReloading, "2.4 s: still reloading at 2.35 s");
            for (var frame = 0; frame < 10; frame++) yield return null;
            Assert.IsFalse(_sniper.IsReloading);
            Assert.AreEqual(5, _sniper.MagazineAmmo);
            Assert.AreEqual(25, _inventory.Get(AmmoType.Heavy), "Exactly five Heavy rounds.");
        }

        // ---- Acceptance 4: thin walls and small targets at 34 u/s — no tunnelling, no duplicate impacts ----

        [UnityTest]
        public IEnumerator ThinWall_StopsTheProjectile_AtReferenceSpeed()
        {
            // 0.68 units per physics step at 34 u/s; the wall is a tenth of that.
            ThinWall(new Vector2(5f, 0f), 0.06f);
            var behind = SmallTarget(new Vector2(7f, 0f), 0.4f);
            Assert.IsTrue(_sniper.TryFire());
            var projectile = _sniper.LastSpawnedProjectile;
            yield return FixedSteps(20);

            Assert.IsFalse(projectile.gameObject.activeSelf, "Terminated on the thin wall.");
            Assert.LessOrEqual(projectile.transform.position.x, 5.05f, "Stopped at the wall surface, never beyond it.");
            Assert.AreEqual(0, behind.HitCount, "Nothing behind a wall is ever hit.");
            Assert.GreaterOrEqual(projectile.SweepResolvedHits, 1, "The sweep, not a lucky overlap, caught it.");
        }

        [UnityTest]
        public IEnumerator SmallTarget_IsHitExactlyOnce_AtReferenceSpeed_AcrossManyOffsets()
        {
            // A 0.1-radius target with the 0.15 projectile radius: hittable band 0.25 wide, step 0.68 — discrete overlap
            // would miss most spawn offsets; the sweep catches every one exactly once.
            for (var offset = 0f; offset < 0.68f; offset += 0.0425f)
            {
                var target = SmallTarget(new Vector2(6f + offset, 0f), 0.1f);
                Set(typeof(RangedWeapon), _sniper, "_fireCooldownRemaining", 0f);
                typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_sniper, 5);
                Assert.IsTrue(_sniper.TryFire());
                var projectile = _sniper.LastSpawnedProjectile;
                yield return FixedSteps(15);
                Assert.AreEqual(1, target.HitCount, $"Offset {offset:F4}: hit exactly once.");
                Assert.IsFalse(projectile.gameObject.activeSelf);
                Assert.IsTrue(target.LastDamageAmount >= 45 && target.LastDamageAmount <= 52);
                Object.DestroyImmediate(target.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TwoTargetsInLine_OnlyTheFirstIsHit_AndThePooledObjectIsReusable()
        {
            var first = SmallTarget(new Vector2(4f, 0f), 0.2f);
            var second = SmallTarget(new Vector2(4.5f, 0f), 0.2f);
            Assert.IsTrue(_sniper.TryFire());
            var projectile = _sniper.LastSpawnedProjectile;
            yield return FixedSteps(15);
            Assert.AreEqual(1, first.HitCount);
            Assert.AreEqual(0, second.HitCount, "One impact per projectile; no duplicate resolution.");
            Assert.IsFalse(projectile.gameObject.activeSelf);

            Set(typeof(RangedWeapon), _sniper, "_fireCooldownRemaining", 0f);
            Object.DestroyImmediate(first.gameObject);
            Assert.IsTrue(_sniper.TryFire());
            var reused = _sniper.LastSpawnedProjectile;
            Assert.AreSame(projectile, reused, "Pool hands the same object back once returned.");
            yield return FixedSteps(15);
            Assert.AreEqual(1, second.HitCount);
        }
    }
}
