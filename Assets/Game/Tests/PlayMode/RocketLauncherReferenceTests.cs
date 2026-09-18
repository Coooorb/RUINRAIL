using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 055 — Pipe Launcher reference (65–80, 0.5/s, 1, 2.2 s, 14, 10, 4 Heavy/shot) and the reusable explosion path:
    /// one detonation per rocket, each target once, Explosion tag, team filter, knockback/stagger hooks.
    /// </summary>
    public class RocketLauncherReferenceTests
    {
        private GameObject _playerObject;
        private RangedWeapon _launcher;
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
            Set(typeof(ItemDefinition), _definition, "_id", "weapon_pipe_launcher");
            Set(typeof(ItemDefinition), _definition, "_category", ItemCategory.Weapon);
            Set(typeof(WeaponDefinition), _definition, "_weaponClass", WeaponClass.RocketLauncher);
            Set(typeof(WeaponDefinition), _definition, "_knockback", 8f);
            Set(typeof(WeaponDefinition), _definition, "_staggerPower", 6f);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMin", 65);
            Set(typeof(RangedWeaponDefinition), _definition, "_damageMax", 80);
            Set(typeof(RangedWeaponDefinition), _definition, "_fireRate", 0.5f);
            Set(typeof(RangedWeaponDefinition), _definition, "_magazineSize", 1);
            Set(typeof(RangedWeaponDefinition), _definition, "_reloadTime", 2.2f);
            Set(typeof(RangedWeaponDefinition), _definition, "_range", 14f);
            Set(typeof(RangedWeaponDefinition), _definition, "_projectileSpeed", 10f);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoType", AmmoType.Heavy);
            Set(typeof(RangedWeaponDefinition), _definition, "_ammoCostPerShot", 4);
            Set(typeof(RangedWeaponDefinition), _definition, "_explosionRadiusTiles", 2f);

            var registry = ItemDefinitionRegistry.Build(new ItemDefinition[] { heavyAmmo, _definition });
            _inventory = PlayerInventory.FromRegistry(registry, ammoBalance);

            _playerObject = new GameObject("TestPlayer");
            _playerObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _pool = _playerObject.AddComponent<ProjectilePool>();
            var aiming = _playerObject.AddComponent<PlayerAiming>();
            _launcher = _playerObject.AddComponent<RangedWeapon>();
            _input = new FakePlayerInputReader { Aim = Vector2.right };
            aiming.SetInputReader(_input);
            _launcher.SetInputReader(_input);
            _launcher.SetProjectilePool(_pool);
            _launcher.SetAiming(aiming);
            _launcher.SetAmmoReserve(_inventory);
            _launcher.SetDamageRoller(new FixedDamageRoller { FixedValue = 70 });
            _launcher.SetDefinition(_definition);
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

        private (GameObject go, HealthComponent health, ImpactReceiver impact) Enemy(string name, Vector2 position, int colliders = 1)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            for (var i = 0; i < colliders; i++)
            {
                var host = i == 0 ? go : new GameObject($"Hurtbox{i}");
                if (i > 0) host.transform.SetParent(go.transform, false);
                host.AddComponent<CircleCollider2D>().radius = 0.3f + 0.1f * i;
            }

            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(300);
            var impact = go.AddComponent<ImpactReceiver>();
            var config = Track(StaggerConfig.Create());
            impact.SetConfig(config);
            impact.SetProfile(new ImpactProfile(name, 0, 0, true, false));
            return (go, health, impact);
        }

        private static IEnumerator FixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        // ---- Acceptance 1: 4 Heavy per shot, never a free shot ----

        [UnityTest]
        public IEnumerator FourHeavyPerRocket_AndNoShotWithoutFourUnits()
        {
            // The magazine starts full (one rocket); reloading needs four Heavy units.
            Assert.AreEqual(1, _launcher.MagazineAmmo);
            Assert.IsTrue(_launcher.TryFire());
            Assert.AreEqual(0, _launcher.MagazineAmmo);
            Assert.IsFalse(_launcher.TryFire(), "Empty tube: no free shot.");

            _inventory.Add(AmmoType.Heavy, 3);
            Assert.IsFalse(_launcher.TryStartReload(), "Three Heavy units cannot load a rocket.");
            _inventory.Add(AmmoType.Heavy, 1);
            Assert.IsTrue(_launcher.TryStartReload());
            Time.captureDeltaTime = 0.01f;
            for (var frame = 0; frame < 215; frame++) yield return null;
            Assert.IsTrue(_launcher.IsReloading, "2.15 s: still reloading (2.2 s).");
            for (var frame = 0; frame < 10; frame++) yield return null;
            Assert.IsFalse(_launcher.IsReloading);
            Assert.AreEqual(1, _launcher.MagazineAmmo);
            Assert.AreEqual(0, _inventory.Get(AmmoType.Heavy), "Exactly four Heavy consumed for one rocket.");

            // Cadence 0.5/s: after firing, the trigger is dead for two seconds.
            Set(typeof(RangedWeapon), _launcher, "_fireCooldownRemaining", 0f);
            Assert.IsTrue(_launcher.TryFire());
            _inventory.Add(AmmoType.Heavy, 40);
            typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_launcher, 1);
            for (var frame = 0; frame < 190; frame++) yield return null;
            Assert.IsFalse(_launcher.TryFire(), "1.9 s < 2.0 s interval.");
            for (var frame = 0; frame < 15; frame++) yield return null;
            Assert.IsTrue(_launcher.TryFire());
        }

        // ---- Acceptance 2 + 4: one explosion, each target once, explosion tag, team filter, hooks ----

        [UnityTest]
        public IEnumerator Explosion_HitsEachTargetOnce_WithExplosionTag_SkipsOwnTeam_AndPushesOutward()
        {
            var (_, nearHealth, nearImpact) = Enemy("near", new Vector2(5.2f, 0f), colliders: 3);
            var (_, sideHealth, _) = Enemy("side", new Vector2(5f, 1.5f));
            var (_, farHealth, _) = Enemy("far", new Vector2(9f, 0f));
            var ally = new GameObject("Ally");
            _created.Add(ally);
            ally.transform.position = new Vector2(5f, -1f);
            ally.AddComponent<CircleCollider2D>().isTrigger = true;
            ally.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var allyHealth = ally.AddComponent<HealthComponent>();
            allyHealth.SetMaxHealth(100);

            var kinds = new List<DamageKind>();
            var wrapper = nearHealth.gameObject.AddComponent<DamageKindRecorder>();
            wrapper.Health = nearHealth;
            wrapper.Kinds = kinds;
            var pushed = 0f;
            nearImpact.KnockedBack += (_, d) => pushed = d;

            Assert.IsTrue(_launcher.TryFire());
            var rocket = _launcher.LastSpawnedProjectile;
            Assert.IsTrue(rocket.Data.IsExplosive);
            Assert.AreEqual(DamageTeam.Player, rocket.Data.SourceTeam);
            Vector2 blast = default;
            var explosions = 0;
            rocket.Exploded += (_, at) => { explosions++; blast = at; };
            yield return FixedSteps(40); // 10 u/s: reaches x~4.9 within 0.5 s

            Assert.AreEqual(1, explosions, "Exactly one detonation per rocket.");
            Assert.IsFalse(rocket.gameObject.activeSelf, "Returned to the pool.");
            Assert.AreEqual(230, nearHealth.CurrentHealth, "70 once, despite three colliders (no direct hit + splash double-dip).");
            Assert.AreEqual(230, sideHealth.CurrentHealth, "1.5 tiles away: inside the 2-tile radius.");
            Assert.AreEqual(300, farHealth.CurrentHealth, "Outside the radius.");
            Assert.AreEqual(100, allyHealth.CurrentHealth, "Player-team target inside the blast: friendly fire OFF.");
            Assert.AreEqual(2, rocket.LastExplosion.TargetsHit);
            CollectionAssert.AreEqual(new[] { DamageKind.Explosion }, kinds, "One application, tagged Explosion.");
            Assert.AreEqual(2f, pushed, 1e-3f, "Knockback 8 x 0.25 radiates from the blast.");
            Assert.Greater(nearImpact.Meter.Pressure, 0f, "Stagger pressure landed through the receiver (decaying since).");
        }

        private sealed class DamageKindRecorder : MonoBehaviour, IIncomingDamageModifier
        {
            public HealthComponent Health;
            public List<DamageKind> Kinds;
            private void Start() => Health.SetIncomingDamageModifier(this);
            public int ModifyIncomingDamage(DamageRequest request) { Kinds.Add(request.Kind); return request.Amount; }
        }

        [UnityTest]
        public IEnumerator Explosion_CarriesTheExplosionTag_SoBlastSuitAndShockAbsorberApply()
        {
            // An enemy-team rocket hitting the player: general DR, then the specialized 30%, and explosion knockback negated.
            var caps = Track(ScriptableObject.CreateInstance<GlobalStatCapsConfig>());
            var player = new GameObject("Victim");
            _created.Add(player);
            player.transform.position = new Vector2(4f, 0f);
            var rb = player.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            player.AddComponent<CircleCollider2D>().radius = 0.3f;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = player.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);
            var stats = new PlayerStats(caps, 500);
            stats.SetSource(new StatModifierSource("blast_suit", StatModifier.Percent(StatId.GeneralDamageReduction, 40), StatModifier.Percent(StatId.ExplosionDamageReduction, 30)));
            health.SetIncomingDamageModifier(new StatsModifier(stats));
            var receiver = player.AddComponent<PlayerImpactReceiver>();
            receiver.SetConfig(Track(StaggerConfig.Create()));
            receiver.SetStats(stats);
            var events = new PlayerCombatEvents();
            receiver.SetEvents(events);
            events.ExplosionKnockbackIncoming += r => r.Negate("shock_absorber");

            var data = new ProjectileSpawnData(100, 10f, 14f, 8f, 0f, Vector2.right, null, null, explosionRadius: 2f, sourceTeam: DamageTeam.Enemy);
            var rocket = _pool.Spawn(Vector2.zero, data);
            yield return FixedSteps(30);

            Assert.IsFalse(rocket.gameObject.activeSelf);
            Assert.AreEqual(458, health.CurrentHealth, "100 -> 60 -> 42 through the explosion mitigation path.");
            Assert.AreEqual(1, receiver.KnockbacksNegated, "Explosion knockback ignored by the Shock Absorber hook.");
            Assert.AreEqual(4f, player.transform.position.x, 1e-3f);
        }

        private sealed class StatsModifier : IIncomingDamageModifier
        {
            private readonly PlayerStats _stats;
            public StatsModifier(PlayerStats stats) => _stats = stats;
            public int ModifyIncomingDamage(DamageRequest request) => _stats.ApplyDamageReduction(request.Amount, request.IsExplosion);
        }

        // ---- Acceptance 3: visible pooled rocket, blocked by walls (detonates there), detonates at range end ----

        [UnityTest]
        public IEnumerator Rocket_DetonatesOnWalls_NotBehindThem_AndAtRangeEnd()
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = new Vector2(3f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.1f, 4f);
            wall.AddComponent<EnvironmentObstacle>();
            var (_, behindHealth, _) = Enemy("behind", new Vector2(6f, 0f));
            var (_, nearWallHealth, _) = Enemy("nearWall", new Vector2(2.2f, 1f));

            Assert.IsTrue(_launcher.TryFire());
            var rocket = _launcher.LastSpawnedProjectile;
            Assert.IsTrue(rocket.gameObject.activeSelf, "Visible moving projectile.");
            Vector2 blast = default;
            rocket.Exploded += (_, at) => blast = at;
            yield return FixedSteps(25);
            Assert.IsFalse(rocket.gameObject.activeSelf);
            Assert.LessOrEqual(blast.x, 3.0f, "Detonated at the wall face, not beyond it.");
            Assert.AreEqual(230, nearWallHealth.CurrentHealth, "Splash on the near side.");
            Assert.AreEqual(300, behindHealth.CurrentHealth, "Blast centre is in front of the wall; 3 tiles away is outside the radius.");

            // Range end: a rocket that hits nothing still goes off at 14 tiles.
            typeof(RangedWeapon).GetProperty("MagazineAmmo").SetValue(_launcher, 1);
            Set(typeof(RangedWeapon), _launcher, "_fireCooldownRemaining", 0f);
            _input.Aim = Vector2.down;
            yield return null;
            Assert.IsTrue(_launcher.TryFire());
            var second = _launcher.LastSpawnedProjectile;
            var (_, endHealth, _) = Enemy("atRangeEnd", new Vector2(0f, -14.5f));
            var fired = 0;
            second.Exploded += (_, at) => { fired++; blast = at; };
            yield return FixedSteps(80); // 1.6 s at 10 u/s > 14 tiles
            Assert.AreEqual(1, fired);
            Assert.AreEqual(-14f, blast.y, 0.2f);
            Assert.AreEqual(230, endHealth.CurrentHealth, "Detonation at the end of the range still splashes.");
        }
    }
}
