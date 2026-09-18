using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The hit-registration invariant on the real runtime path (camera → pointer → PlayerAiming → pivot → muzzle →
    /// projectile → enemy hurtbox → HealthComponent): for an unobstructed enemy in range, a crosshair inside its
    /// hurtbox hits it with a normal direct projectile, with aim assist OFF. Plus the negative cases: just outside the
    /// hurtbox misses, a wall stops the shot, a close target is not spawned past, and damage stays host-authoritative.
    /// </summary>
    public sealed class DirectAimHitTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            _catalog = GameContentCatalog.Load();
            _registry = _catalog.BuildRegistry();
            var camGo = new GameObject("TestCamera");
            _created.Add(camGo);
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 5.625f;
            camGo.transform.position = new Vector3(0f, 0f, -10f);
        }

        [TearDown]
        public void TearDown()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            _created.Clear();
        }

        private (GameObject player, FakePlayerInputReader reader, PlayerAiming aiming, HeldWeaponVisual held) Player(Vector2 at)
        {
            var reader = new FakePlayerInputReader { IsAimFromPointer = true };
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Shooter", IsLocal = true, InputReader = reader, BalanceConfig = _catalog.PlayerBalance, Position = at });
            _created.Add(go);
            var aiming = go.GetComponent<PlayerAiming>();
            aiming.SetCamera(_camera);
            go.AddComponent<ProjectilePool>();
            go.AddComponent<WeaponLoadout>();
            var held = PlayerVisualComposer.Compose(go, _catalog, reader);
            return (go, reader, aiming, held);
        }

        private IEquippableWeapon Mount(GameObject player, string weaponId, bool assist)
        {
            Assert.IsTrue(_registry.TryGet(weaponId, out var definition), weaponId);
            var aiming = player.GetComponent<PlayerAiming>();
            var pool = player.GetComponent<ProjectilePool>();
            var reader = player.GetComponent<PlayerInput>() != null ? player.GetComponent<PlayerInput>().Reader : null;
            IEquippableWeapon weapon;
            switch (definition)
            {
                case BlasterWeaponDefinition blaster:
                    var b = player.AddComponent<BlasterWeapon>();
                    b.SetAiming(aiming); b.SetProjectilePool(pool); b.SetDamageRoller(new UnityRandomDamageRoller()); b.SetDefinition(blaster);
                    if (assist) b.SetAimAssist(_catalog.AimAssist);
                    weapon = b;
                    break;
                case RangedWeaponDefinition ranged:
                    var r = player.AddComponent<RangedWeapon>();
                    var reserve = new AmmoReserve();
                    reserve.Add(ranged.AmmoType, 200);
                    r.SetAiming(aiming); r.SetProjectilePool(pool); r.SetAmmoReserve(reserve); r.SetDamageRoller(new UnityRandomDamageRoller()); r.SetDefinition(ranged);
                    r.SetSpreadRandom(new SeededRandom(7));
                    if (assist) r.SetAimAssist(_catalog.AimAssist);
                    weapon = r;
                    break;
                default:
                    throw new AssertionException("not a projectile weapon: " + weaponId);
            }

            var loadout = player.GetComponent<WeaponLoadout>();
            loadout.SetPrimary(weapon);
            loadout.Initialize();
            player.GetComponent<HeldWeaponVisual>().Refresh();
            return weapon;
        }

        private EnemyController Enemy(Vector2 at, string id = "grunt")
        {
            var definition = _catalog.Enemies.First(e => e.Id == id);
            var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, at, null);
            _created.Add(enemy.gameObject);
            enemy.enabled = false; // a target dummy: no AI, no movement
            enemy.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            return enemy;
        }

        private static bool Fire(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r => r.TryFire(),
            BlasterWeapon b => b.TryFire(),
            _ => false
        };

        private static Projectile Last(IEquippableWeapon weapon) => weapon switch
        {
            RangedWeapon r => r.LastSpawnedProjectile,
            BlasterWeapon b => b.LastSpawnedProjectile,
            _ => null
        };

        /// <summary>Points the crosshair at a world point and lets PlayerAiming resolve it (one frame).</summary>
        private IEnumerator AimAt(FakePlayerInputReader reader, Vector2 world)
        {
            reader.Aim = _camera.WorldToScreenPoint(new Vector3(world.x, world.y, 0f));
            yield return null;
            yield return null;
        }

        private static IEnumerator Settle(float seconds)
        {
            var end = Time.time + seconds;
            while (Time.time < end) yield return new WaitForFixedUpdate();
        }

        private static readonly string[] DirectWeapons =
        {
            "weapon_p9_ranger", "weapon_rattler_9", "weapon_ar_17", "weapon_sentinel_br", "weapon_scatter_8", "weapon_longshot_s1", "weapon_pulse_carbine_b1", "weapon_pipe_launcher"
        };

        [UnityTest]
        public IEnumerator CrosshairInsideHurtbox_HitsWithEveryDirectProjectileClass_AimAssistOff()
        {
            foreach (var weaponId in DirectWeapons)
            {
                var (player, reader, aiming, _) = Player(Vector2.zero);
                var weapon = Mount(player, weaponId, assist: false);
                var enemy = Enemy(new Vector2(5f, 0.3f));
                var health = enemy.GetComponent<HealthComponent>();
                var hurtbox = enemy.GetComponent<CombatHurtbox>();
                Assert.IsNotNull(hurtbox, "enemies carry a hurtbox");
                var before = health.CurrentHealth;

                yield return AimAt(reader, hurtbox.AimPoint);
                Assert.IsTrue(hurtbox.Contains(aiming.AimWorldPoint), $"{weaponId}: crosshair resolved inside the hurtbox");
                Assert.IsTrue(Fire(weapon), $"{weaponId}: fired");
                yield return Settle(1.5f);
                Assert.Less(health.CurrentHealth, before, $"{weaponId}: a direct shot under the crosshair damages the enemy");

                Object.DestroyImmediate(enemy.gameObject);
                Object.DestroyImmediate(player);
            }
        }

        [UnityTest]
        public IEnumerator CrosshairNearHurtboxEdge_StillInside_Hits_AndJustOutside_MissesWithoutAssist()
        {
            var (player, reader, aiming, _) = Player(Vector2.zero);
            var weapon = Mount(player, "weapon_p9_ranger", assist: false);
            var enemy = Enemy(new Vector2(6f, 0f));
            var health = enemy.GetComponent<HealthComponent>();
            var hurtbox = enemy.GetComponent<CombatHurtbox>();
            var top = hurtbox.AimPoint + Vector2.up * (hurtbox.Size.y * 0.5f - 0.05f);
            yield return AimAt(reader, top);
            Assert.IsTrue(hurtbox.Contains(aiming.AimWorldPoint));
            var before = health.CurrentHealth;
            Assert.IsTrue(Fire(weapon));
            yield return Settle(1.5f);
            Assert.Less(health.CurrentHealth, before, "an edge shot inside the hurtbox still lands");

            var above = hurtbox.AimPoint + Vector2.up * (hurtbox.Size.y * 0.5f + 0.35f);
            yield return AimAt(reader, above);
            Assert.IsFalse(hurtbox.Contains(aiming.AimWorldPoint));
            before = health.CurrentHealth;
            yield return Settle(0.4f); // fire-rate cooldown
            Assert.IsTrue(Fire(weapon));
            yield return Settle(1.5f);
            Assert.AreEqual(before, health.CurrentHealth, "just outside the hurtbox with assist off: a miss, no hidden magnetism");
        }

        [UnityTest]
        public IEnumerator WallBetween_StopsTheShot_NoFalseHit()
        {
            var (player, reader, _, _) = Player(Vector2.zero);
            var weapon = Mount(player, "weapon_p9_ranger", assist: true);
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = new Vector3(3f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 6f);
            wall.AddComponent<EnvironmentObstacle>();
            var enemy = Enemy(new Vector2(6f, 0f));
            var health = enemy.GetComponent<HealthComponent>();
            var before = health.CurrentHealth;
            yield return AimAt(reader, enemy.GetComponent<CombatHurtbox>().AimPoint);
            Assert.IsTrue(Fire(weapon));
            var shot = ((RangedWeapon)weapon).LastShot;
            Assert.IsFalse(shot.Assisted, "assist never picks a target behind a wall");
            yield return Settle(1.5f);
            Assert.AreEqual(before, health.CurrentHealth, "the wall takes the shot");
        }

        [UnityTest]
        public IEnumerator CloseTarget_ProjectileIsNotSpawnedPastIt()
        {
            var (player, reader, aiming, held) = Player(Vector2.zero);
            var weapon = Mount(player, "weapon_p9_ranger", assist: false);
            var enemy = Enemy(new Vector2(0.55f, 0.1f)); // hurtbox overlaps the muzzle
            var health = enemy.GetComponent<HealthComponent>();
            var before = health.CurrentHealth;
            yield return AimAt(reader, enemy.GetComponent<CombatHurtbox>().AimPoint);
            Assert.IsTrue(Fire(weapon));
            var shot = ((RangedWeapon)weapon).LastShot;
            Assert.IsTrue(shot.SpawnPulledBack, "the muzzle is past the target: the shot leaves from the hand");
            Assert.Less(Vector2.Distance(shot.SpawnPosition, aiming.AimOrigin), 0.01f);
            yield return Settle(1f);
            Assert.Less(health.CurrentHealth, before, "point-blank still hits");
        }

        [UnityTest]
        public IEnumerator LongRangeWithinWeaponRange_Hits_AndEightDirectionsAllHit()
        {
            var (player, reader, _, _) = Player(Vector2.zero);
            var weapon = (RangedWeapon)Mount(player, "weapon_longshot_s1", assist: false);
            var far = Enemy(new Vector2(weapon.Definition.Range - 1.5f, 0f));
            var before = far.GetComponent<HealthComponent>().CurrentHealth;
            yield return AimAt(reader, far.GetComponent<CombatHurtbox>().AimPoint);
            Assert.IsTrue(weapon.TryFire());
            yield return Settle(2f);
            Assert.Less(far.GetComponent<HealthComponent>().CurrentHealth, before, "a target near the end of the weapon's range is hit");
            Object.DestroyImmediate(far.gameObject);
            Object.DestroyImmediate(player);

            foreach (var angle in new[] { 90f, 45f, 0f, -45f, -90f, -135f, 180f, 135f })
            {
                var (p, r, _, _) = Player(Vector2.zero);
                var pistol = (RangedWeapon)Mount(p, "weapon_p9_ranger", assist: false);
                var dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                var enemy = Enemy(dir * 4f);
                var health = enemy.GetComponent<HealthComponent>();
                var hp = health.CurrentHealth;
                yield return AimAt(r, enemy.GetComponent<CombatHurtbox>().AimPoint);
                Assert.IsTrue(pistol.TryFire(), $"{angle}°");
                yield return Settle(1.2f);
                Assert.Less(health.CurrentHealth, hp, $"{angle}°: hit");
                Object.DestroyImmediate(enemy.gameObject);
                Object.DestroyImmediate(p);
            }
        }

        [UnityTest]
        public IEnumerator Damage_StaysHostAuthoritative()
        {
            var (player, reader, _, _) = Player(Vector2.zero);
            var weapon = Mount(player, "weapon_p9_ranger", assist: true);
            var enemy = Enemy(new Vector2(5f, 0f));
            var health = enemy.GetComponent<HealthComponent>();
            var before = health.CurrentHealth;
            yield return AimAt(reader, enemy.GetComponent<CombatHurtbox>().AimPoint);
            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsTrue(Fire(weapon));
            yield return Settle(1.5f);
            Assert.AreEqual(before, health.CurrentHealth, "a client never applies damage locally (82)");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [Test]
        public void ProjectileBody_StartsAtTheMuzzle_NotAtTheLastPooledPosition()
        {
            var pool = new GameObject("Pool").AddComponent<ProjectilePool>();
            _created.Add(pool.gameObject);
            var data = new ProjectileSpawnData(1, 10f, 5f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player);
            var first = pool.Spawn(new Vector2(20f, 20f), data);
            pool.Return(first);
            var second = pool.Spawn(new Vector2(-7f, 3f), data);
            Assert.AreSame(first, second, "pooled instance reused");
            Assert.AreEqual(new Vector2(-7f, 3f), second.GetComponent<Rigidbody2D>().position, "the body is teleported with the transform, so the first sweep starts at the muzzle");
        }
    }
}
