using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 058 — the fixed RMB Legendary special contract: routing, cooldown-only gating, reusable primitives.</summary>
    public class LegendarySpecialTests
    {
        private GameObject _playerObject;
        private RangedWeapon _legendaryGun;
        private RangedWeapon _normalGun;
        private WeaponLoadout _loadout;
        private ProjectilePool _pool;
        private PlayerAiming _aiming;
        private FakePlayerInputReader _input;
        private PlayerInventory _inventory;
        private LegendarySpecialController _controller;
        private readonly List<Object> _created = new();
        private float _previousCaptureDeltaTime;

        [SetUp]
        public void SetUp()
        {
            var ammoBalance = Track(ScriptableObject.CreateInstance<AmmoBalanceConfig>());
            var light = Track(ScriptableObject.CreateInstance<AmmoItemDefinition>());
            Set(typeof(ItemDefinition), light, "_id", "ammo_light");
            Set(typeof(ItemDefinition), light, "_category", ItemCategory.Ammo);
            Set(typeof(ItemDefinition), light, "_isStackable", true);
            Set(typeof(AmmoItemDefinition), light, "_ammoType", AmmoType.Light);

            var legendaryDef = Pistol("weapon_test_legendary", "test_special");
            var normalDef = Pistol("weapon_test_normal", "");
            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { light, legendaryDef, normalDef });
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);
            _inventory.Add(AmmoType.Light, 50);

            _playerObject = new GameObject("TestPlayer");
            _created.Add(_playerObject);
            _playerObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var rb = _playerObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            _pool = _playerObject.AddComponent<ProjectilePool>();
            _aiming = _playerObject.AddComponent<PlayerAiming>();
            _input = new FakePlayerInputReader { Aim = Vector2.right };
            _aiming.SetInputReader(_input);

            _legendaryGun = Gun(legendaryDef);
            _normalGun = Gun(normalDef);
            _loadout = _playerObject.AddComponent<WeaponLoadout>();
            _loadout.SetInputReader(_input);
            _loadout.SetPrimary(_legendaryGun);
            _loadout.SetSecondary(_normalGun);
            _loadout.Initialize();

            _controller = _playerObject.AddComponent<LegendarySpecialController>();
            _controller.SetInputReader(_input);
            _previousCaptureDeltaTime = Time.captureDeltaTime;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = _previousCaptureDeltaTime;
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

        private RangedWeaponDefinition Pistol(string id, string mechanicId)
        {
            var d = Track(ScriptableObject.CreateInstance<RangedWeaponDefinition>());
            Set(typeof(ItemDefinition), d, "_id", id);
            Set(typeof(ItemDefinition), d, "_category", ItemCategory.Weapon);
            Set(typeof(EquipmentItemDefinition), d, "_legendaryMechanicId", mechanicId);
            Set(typeof(WeaponDefinition), d, "_weaponClass", WeaponClass.Pistol);
            Set(typeof(RangedWeaponDefinition), d, "_damageMin", 12);
            Set(typeof(RangedWeaponDefinition), d, "_damageMax", 14);
            Set(typeof(RangedWeaponDefinition), d, "_fireRate", 4f);
            Set(typeof(RangedWeaponDefinition), d, "_magazineSize", 12);
            Set(typeof(RangedWeaponDefinition), d, "_reloadTime", 1.2f);
            Set(typeof(RangedWeaponDefinition), d, "_range", 10f);
            Set(typeof(RangedWeaponDefinition), d, "_projectileSpeed", 20f);
            Set(typeof(RangedWeaponDefinition), d, "_ammoType", AmmoType.Light);
            Set(typeof(RangedWeaponDefinition), d, "_ammoCostPerShot", 1);
            return d;
        }

        private RangedWeapon Gun(RangedWeaponDefinition definition)
        {
            var gun = _playerObject.AddComponent<RangedWeapon>();
            gun.SetInputReader(_input);
            gun.SetProjectilePool(_pool);
            gun.SetAiming(_aiming);
            gun.SetAmmoReserve(_inventory);
            gun.SetDamageRoller(new UnityRandomDamageRoller());
            gun.SetDefinition(definition);
            return gun;
        }

        private SpecialContext Context() => new(_playerObject, _playerObject.GetComponent<Rigidbody2D>(), () => _aiming.AimDirection, () => _playerObject.transform.position, _pool, new FixedDamageRoller { FixedValue = 11 });

        private TestDamageableTarget Enemy(Vector2 position, float radius = 0.3f)
        {
            var go = new GameObject("Enemy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = radius;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            return go.AddComponent<TestDamageableTarget>();
        }

        private GameObject Wall(Vector2 position, Vector2 size)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = size;
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        private int ActiveProjectiles() => _pool.GetComponentsInChildren<Projectile>(true).Count(p => p.gameObject.activeSelf);

        private static IEnumerator FixedSteps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        // ---- Acceptance 1 + 2: routing to the active Legendary only; cooldown-only gating; no resource ----

        [UnityTest]
        public IEnumerator Special_FiresOnlyForTheActiveLegendaryWeapon_CooldownGates_NoResourceConsumed()
        {
            var special = new BurstSpecial("test_special", 10f, shots: 3, intervalSeconds: 0f, new DamageBand(10, 12), 20f, 10f);
            _controller.Configure(special, _legendaryGun, Context);
            Time.captureDeltaTime = 0.02f;
            var fired = 0;
            _controller.SpecialFired += _ => fired++;

            // Normal fire and special are independent inputs; special costs nothing.
            _input.SpecialHeld = true;
            yield return null;
            Assert.AreEqual(1, fired);
            Assert.AreEqual(1, _controller.Activations);
            Assert.AreEqual(12, _legendaryGun.MagazineAmmo, "The special never touches the magazine.");
            Assert.AreEqual(50, _inventory.Get(AmmoType.Light), "Nor the reserve.");
            Assert.AreEqual(3, ActiveProjectiles(), "Three special projectiles spawned.");
            Assert.IsTrue(_legendaryGun.TryFire(), "Normal LMB attack stays available while the special cools down.");
            Assert.AreEqual(11, _legendaryGun.MagazineAmmo);

            // Holding does not repeat; releasing and pressing again is blocked by the cooldown.
            yield return null;
            Assert.AreEqual(1, fired);
            _input.SpecialHeld = false;
            yield return null;
            _input.SpecialHeld = true;
            yield return null;
            Assert.AreEqual(1, fired, "Cooldown (10 s) blocks a repeat press.");
            Assert.IsFalse(_controller.State.IsReady);
            Assert.AreEqual(10f, _controller.State.CooldownRemaining, 0.1f);
            _input.SpecialHeld = false;

            // Switch to the normal gun: special input is ignored entirely (no controller listens for it).
            _input.RaiseWeapon2Selected();
            Assert.IsTrue(_normalGun.IsEquipped);
            _controller.State.Reset();
            _input.SpecialHeld = true;
            yield return null;
            Assert.AreEqual(1, fired, "Special never routes to a normal weapon.");
            Assert.IsFalse(_controller.TryActivate(), "Scripted activation is refused while the Legendary is holstered.");
            _input.SpecialHeld = false;
            yield return null; // the release must be observed before a new press counts

            // Back on the Legendary the (reset) special is ready again.
            _input.RaiseWeapon1Selected();
            _input.SpecialHeld = true;
            yield return null;
            Assert.AreEqual(2, fired);
        }

        [UnityTest]
        public IEnumerator Cooldown_KeepsTickingWhileHolstered_AndIsPerWeaponState()
        {
            var special = new FanSpecial("fan", 2f, 5, 40f, new DamageBand(1, 1), 20f, 10f);
            _controller.Configure(special, _legendaryGun, Context);
            Time.captureDeltaTime = 0.1f;
            Assert.IsTrue(_controller.TryActivate());
            Assert.IsFalse(_controller.TryActivate());

            _input.RaiseWeapon2Selected();
            for (var i = 0; i < 15; i++) yield return null; // 1.5 s holstered
            Assert.AreEqual(0.5f, _controller.State.CooldownRemaining, 0.15f, "Cooldown counts down while holstered.");
            _input.RaiseWeapon1Selected();
            for (var i = 0; i < 6; i++) yield return null;
            Assert.IsTrue(_controller.State.IsReady);
            Assert.IsTrue(_controller.TryActivate());
            Assert.AreEqual(2, _controller.State.Uses);

            // A second Legendary would carry its own state object.
            var other = new LegendarySpecialState(14f);
            Assert.IsTrue(other.IsReady);
            Assert.IsTrue(other.TryUse());
            Assert.AreEqual(14f, other.CooldownRemaining);
            Assert.AreNotSame(other, _controller.State);
        }

        // ---- Requirement 5: reusable primitives ----

        [UnityTest]
        public IEnumerator Burst_EmitsExactlyNShotsAtTheInterval_WithFixedDamageBand()
        {
            var special = new BurstSpecial("burst", 5f, shots: 8, intervalSeconds: 0.05f, new DamageBand(10, 12), 20f, 10f);
            Time.captureDeltaTime = 0.01f;
            var execution = special.Begin(Context());
            Assert.IsNotNull(execution);
            Assert.AreEqual(1, ActiveProjectiles(), "First shot leaves immediately.");
            for (var i = 0; i < 5; i++) execution.Tick(0.01f);
            Assert.AreEqual(2, ActiveProjectiles(), "Second shot after 0.05 s.");
            for (var i = 0; i < 40; i++) execution.Tick(0.01f);
            Assert.IsTrue(execution.IsComplete);
            Assert.AreEqual(8, ActiveProjectiles());
            foreach (var projectile in _pool.GetComponentsInChildren<Projectile>().Where(p => p.gameObject.activeSelf))
            {
                var data = projectile.Data;
                Assert.AreEqual(11, data.Damage, "Fixed band rolled through the roller (fixed 11).");
                Assert.AreEqual(DamageTeam.Player, data.SourceTeam);
            }

            yield return null;
        }

        [Test]
        public void Fan_SpreadsShotsEvenlyAcrossTheArc()
        {
            var directions = new List<Vector2>();
            FanSpecial.ResolveDirections(Vector2.right, 5, 40f, directions);
            var angles = directions.Select(d => Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg).ToList();
            CollectionAssert.AreEqual(new[] { -20f, -10f, 0f, 10f, 20f }, angles.Select(a => Mathf.Round(a)).ToList());
            FanSpecial.ResolveDirections(Vector2.up, 1, 90f, directions);
            Assert.AreEqual(1, directions.Count);
            Assert.AreEqual(Vector2.up, directions[0]);

            var special = new FanSpecial("fan", 14f, 24, 120f, new DamageBand(7, 9), 18f, 8f);
            special.Begin(Context());
            Assert.AreEqual(24, ActiveProjectiles(), "24 projectiles in one frame.");
        }

        [UnityTest]
        public IEnumerator PiercingShot_DamagesEveryTargetOnce_AndStopsAtWalls()
        {
            var a = Enemy(new Vector2(2f, 0f));
            var b = Enemy(new Vector2(2.4f, 0f));
            var c = Enemy(new Vector2(5f, 0f));
            Wall(new Vector2(7f, 0f), new Vector2(0.2f, 3f));
            var behind = Enemy(new Vector2(9f, 0f));
            var special = new PiercingShotSpecial("pierce", 12f, new DamageBand(55, 65), 34f, 20f);
            special.Begin(Context());
            var shot = _pool.GetComponentsInChildren<Projectile>().First(p => p.gameObject.activeSelf);
            Assert.IsTrue(shot.Data.Piercing);
            yield return FixedSteps(20);

            Assert.AreEqual(1, a.HitCount);
            Assert.AreEqual(1, b.HitCount, "Two targets inside one 0.68-unit step are both pierced.");
            Assert.AreEqual(1, c.HitCount);
            Assert.AreEqual(0, behind.HitCount, "Walls still stop the shot.");
            Assert.AreEqual(3, shot.PiercedTargets);
            Assert.IsFalse(shot.gameObject.activeSelf);
            Assert.AreEqual(11, a.LastDamageAmount);
        }

        [UnityTest]
        public IEnumerator Cone_HitsOnlyTargetsInRangeAndArc_OnceEach_WithImpact()
        {
            var inside = Enemy(new Vector2(2f, 0.5f));
            var edge = Enemy(new Vector2(1f, 1.5f)); // 56 degrees: outside a 90-degree cone
            var far = Enemy(new Vector2(4f, 0f));
            var behind = Enemy(new Vector2(-1f, 0f));
            var receiverGo = inside.gameObject;
            var rb = receiverGo.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            var receiver = receiverGo.AddComponent<ImpactReceiver>();
            receiver.SetConfig(Track(StaggerConfig.Create()));
            receiver.SetProfile(new ImpactProfile("inside", 0, 0, true, false));
            yield return null;

            var special = new ConeSpecial("cone", 10f, new DamageBand(50, 60), 3f, 90f, knockback: 8f, stagger: 10f);
            special.Begin(Context());
            Assert.AreEqual(1, inside.HitCount);
            Assert.AreEqual(0, edge.HitCount);
            Assert.AreEqual(0, far.HitCount);
            Assert.AreEqual(0, behind.HitCount);
            Assert.AreEqual(1, special.LastTargetsHit);
            Assert.IsTrue(receiver.IsKnockbackActive, "Massive knockback/stagger ride on the cone hit.");
            Assert.AreEqual(1, receiver.Meter.TriggerCount);
        }

        [Test]
        public void ExplosionSalvo_PlacesEachExplosionAlongTheAim_ThroughTheSharedExplosionPath()
        {
            var special = new ExplosionSalvoSpecial("salvo", 18f, 3, new DamageBand(45, 55), 1.5f, 3f, 2f);
            var t1 = Enemy(new Vector2(3f, 0f));
            var t2 = Enemy(new Vector2(5f, 0f));
            var t3 = Enemy(new Vector2(7f, 0f));
            var miss = Enemy(new Vector2(10f, 0f));
            Physics2D.SyncTransforms();
            special.Begin(Context());
            CollectionAssert.AreEqual(new[] { new Vector2(3f, 0f), new Vector2(5f, 0f), new Vector2(7f, 0f) }, special.LastCentres);
            Assert.AreEqual(1, t1.HitCount);
            Assert.AreEqual(1, t2.HitCount);
            Assert.AreEqual(1, t3.HitCount);
            Assert.AreEqual(0, miss.HitCount);
        }

        [UnityTest]
        public IEnumerator DashStrike_MovesTheWielderAndDamagesEnemiesCrossedOnce_StoppingAtWalls()
        {
            var crossed = Enemy(new Vector2(1.5f, 0f));
            var alsoCrossed = Enemy(new Vector2(3f, 0.2f));
            var beyond = Enemy(new Vector2(6f, 0f));
            var special = new DashStrikeSpecial("blink", 10f, distanceTiles: 4f, durationSeconds: 0.2f, new DamageBand(40, 50), 0.6f);
            _controller.Configure(special, _legendaryGun, Context);
            _playerObject.GetComponent<PlayerAiming>();
            Assert.IsTrue(_controller.TryActivate());
            Assert.IsTrue(_controller.IsActive, "A dash special owns movement while it runs.");
            yield return new WaitForSeconds(0.35f);
            Assert.IsFalse(_controller.IsRunning);
            Assert.AreEqual(4f, _playerObject.transform.position.x, 0.25f, "Dashed exactly four tiles.");
            Assert.AreEqual(1, crossed.HitCount);
            Assert.AreEqual(1, alsoCrossed.HitCount);
            Assert.AreEqual(0, beyond.HitCount);

            // Second dash into a wall stops short of it.
            Wall(new Vector2(6f, 0f), new Vector2(0.2f, 3f));
            _controller.State.Reset();
            Assert.IsTrue(_controller.TryActivate());
            yield return new WaitForSeconds(0.35f);
            Assert.LessOrEqual(_playerObject.transform.position.x, 5.95f, "Walls end the dash.");
        }

        // ---- Acceptance 4: no catalog switch in the framework ----

        [Test]
        public void Framework_HasNoWeaponIdSwitch()
        {
            var sources = System.IO.Directory.GetFiles("Assets/Game/Scripts/Combat/Weapons/Specials", "*.cs").Select(System.IO.File.ReadAllText).ToList();
            Assert.IsNotEmpty(sources);
            foreach (var source in sources)
            {
                Assert.IsFalse(source.Contains("weapon_"), "No weapon ids inside the special framework.");
                Assert.IsFalse(source.Contains("Quickfang") || source.Contains("Buzzsaw") || source.Contains("Farline"), "No catalog names inside the framework.");
            }
        }
    }
}
