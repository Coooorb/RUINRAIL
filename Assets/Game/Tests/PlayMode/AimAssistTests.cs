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
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The soft aim assist (§4): cone by device and weapon class, candidate rules (alive, in range, in front, in cone,
    /// clear line of sight), scoring (crosshair proximity first, angle, distance), direct-hover priority, and the
    /// guarantee that no valid target leaves the raw aim untouched. Damage stays exactly where it was.
    /// </summary>
    public sealed class AimAssistTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private AimAssistConfig _config;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            _catalog = GameContentCatalog.Load();
            _config = _catalog.AimAssist;
            Assert.IsNotNull(_config, "the catalog carries the aim assist tuning");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            _created.Clear();
        }

        private GameObject Shooter(Vector2 at)
        {
            var go = new GameObject("Shooter");
            _created.Add(go);
            go.transform.position = at;
            go.AddComponent<CircleCollider2D>().radius = 0.4f;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            return go;
        }

        private EnemyController Enemy(Vector2 at)
        {
            var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(_catalog.Enemies.First(e => e.Id == "grunt"), at, null);
            _created.Add(enemy.gameObject);
            enemy.enabled = false;
            enemy.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            return enemy;
        }

        private static Vector2 Dir(float degrees) => new(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        private ShotSolver.Candidate Select(GameObject shooter, Vector2 rawDir, float range, WeaponClass cls, bool pointer, Vector2? crosshair = null)
        {
            var origin = (Vector2)shooter.transform.position;
            var halfAngle = _config.HalfAngleFor(cls, pointer);
            return ShotSolver.SelectTarget(origin, rawDir, range, halfAngle, crosshair ?? origin + rawDir * range, pointer, shooter, DamageTeam.Player, _config);
        }

        [Test]
        public void Defaults_AreTheApprovedValues()
        {
            Assert.AreEqual(18f, _config.MouseHalfAngleDegrees, 0.001f);
            Assert.AreEqual(24f, _config.ControllerHalfAngleDegrees, 0.001f);
            Assert.AreEqual(28f, _config.CrosshairProximityPixels, 0.001f);
            Assert.AreEqual(1f, _config.MultiplierFor(WeaponClass.Pistol), 0.001f);
            Assert.AreEqual(0.75f, _config.MultiplierFor(WeaponClass.Sniper), 0.001f);
            Assert.AreEqual(0.65f, _config.MultiplierFor(WeaponClass.RocketLauncher), 0.001f);
            Assert.AreEqual(0f, _config.MultiplierFor(WeaponClass.Knife), 0.001f, "melee has no projectile assist");
            Assert.AreEqual(0f, _config.MultiplierFor(WeaponClass.Spear), 0.001f);
        }

        [UnityTest]
        public IEnumerator ConeByAngle_0_10_17SelectedAt18Degrees_And25NotSelected()
        {
            var shooter = Shooter(Vector2.zero);
            foreach (var (angle, expected) in new[] { (0f, true), (10f, true), (17f, true), (25f, false) })
            {
                var enemy = Enemy(Dir(angle) * 6f + Vector2.down * CombatHurtbox.NormalOffset.y); // hurtbox centre on the angle
                yield return new WaitForFixedUpdate();
                var picked = Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true, crosshair: (Vector2)shooter.transform.position + Vector2.right * 6f);
                Assert.AreEqual(expected, picked != null && ReferenceEquals(picked.Target, enemy.GetComponent<HealthComponent>()), $"{angle}° with the 18° mouse cone");
                Object.DestroyImmediate(enemy.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Controller_UsesTheWiderCone_SniperAndRocketNarrowIt()
        {
            var shooter = Shooter(Vector2.zero);
            var enemy = Enemy(Dir(21f) * 6f + Vector2.down * CombatHurtbox.NormalOffset.y);
            yield return new WaitForFixedUpdate();
            Assert.IsNull(Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true), "21° is outside the 18° mouse cone");
            Assert.IsNotNull(Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: false), "…but inside the 24° controller cone");
            Object.DestroyImmediate(enemy.gameObject);

            var near = Enemy(Dir(15f) * 6f + Vector2.down * CombatHurtbox.NormalOffset.y);
            yield return new WaitForFixedUpdate();
            Assert.IsNotNull(Select(shooter, Vector2.right, 20f, WeaponClass.AssaultRifle, pointer: true), "15° fits the AR cone");
            Assert.IsNull(Select(shooter, Vector2.right, 20f, WeaponClass.Sniper, pointer: true), "sniper: 18° × 0.75 = 13.5° — 15° is out");
            Assert.IsNull(Select(shooter, Vector2.right, 20f, WeaponClass.RocketLauncher, pointer: true), "rocket: 18° × 0.65 = 11.7° — 15° is out");
        }

        [UnityTest]
        public IEnumerator CrosshairOverATarget_WinsOverANearerCandidate()
        {
            var shooter = Shooter(Vector2.zero);
            var nearOnAxis = Enemy(new Vector2(4f, -CombatHurtbox.NormalOffset.y));
            var hovered = Enemy(Dir(12f) * 8f + Vector2.down * CombatHurtbox.NormalOffset.y);
            yield return new WaitForFixedUpdate();
            var crosshair = hovered.GetComponent<CombatHurtbox>().AimPoint;
            var picked = Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true, crosshair: crosshair);
            Assert.IsNotNull(picked);
            Assert.AreSame(hovered.GetComponent<HealthComponent>(), picked.Target, "the target under the crosshair has overwhelming priority");
            Assert.IsTrue(picked.CrosshairInside);
        }

        [UnityTest]
        public IEnumerator BehindWall_Dead_AndBehindPlayer_AreRejected()
        {
            var shooter = Shooter(Vector2.zero);
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = new Vector3(3f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(0.5f, 8f);
            wall.AddComponent<EnvironmentObstacle>();
            var behindWall = Enemy(new Vector2(6f, -CombatHurtbox.NormalOffset.y));
            yield return new WaitForFixedUpdate();
            Assert.IsNull(Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true), "no line of sight through a wall");
            Object.DestroyImmediate(wall);

            yield return new WaitForFixedUpdate();
            var dead = behindWall;
            dead.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(100000));
            Assert.IsFalse(dead.GetComponent<HealthComponent>().IsAlive);
            Assert.IsNull(Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true), "a dead target is not a candidate");
            Object.DestroyImmediate(dead.gameObject);

            var behind = Enemy(new Vector2(-1.2f, -CombatHurtbox.NormalOffset.y));
            yield return new WaitForFixedUpdate();
            Assert.IsNull(Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true), "a target behind the player is never pulled in, however close");
        }

        [UnityTest]
        public IEnumerator TwoTargets_BestScoreWins_CrosshairProximityBeforeAngle()
        {
            var shooter = Shooter(Vector2.zero);
            var a = Enemy(Dir(6f) * 7f + Vector2.down * CombatHurtbox.NormalOffset.y);
            var b = Enemy(Dir(-14f) * 5f + Vector2.down * CombatHurtbox.NormalOffset.y);
            yield return new WaitForFixedUpdate();
            // Crosshair near B (but not on it): B wins although A is closer to the raw aim axis.
            var nearB = b.GetComponent<CombatHurtbox>().AimPoint + Vector2.down * 0.9f;
            var picked = Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true, crosshair: nearB);
            Assert.AreSame(b.GetComponent<HealthComponent>(), picked.Target);
            // Crosshair far from both on the axis: the smaller angle wins.
            picked = Select(shooter, Vector2.right, 12f, WeaponClass.Pistol, pointer: true, crosshair: (Vector2)shooter.transform.position + Vector2.right * 11f);
            Assert.AreSame(a.GetComponent<HealthComponent>(), picked.Target);
        }

        [UnityTest]
        public IEnumerator NoValidTarget_LeavesTheRawAimUnchanged_AndShotgunSpreadStaysAroundTheAssistedCentre()
        {
            var shooter = Shooter(Vector2.zero);
            var solution = ShotSolver.Solve(Vector2.zero, Vector2.right * 0.8f, Dir(30f), 12f, WeaponClass.Pistol, shooter, DamageTeam.Player, _config, null);
            Assert.IsFalse(solution.Assisted);
            Assert.AreEqual(0f, Vector2.Angle(solution.Direction, Dir(30f)), 0.01f, "raw aim untouched");
            Assert.AreEqual(Vector2.right * 0.8f, solution.SpawnPosition);

            var enemy = Enemy(Dir(8f) * 6f + Vector2.down * CombatHurtbox.NormalOffset.y);
            yield return new WaitForFixedUpdate();
            var assisted = ShotSolver.Solve(Vector2.zero, Vector2.right * 0.8f, Vector2.right, 12f, WeaponClass.Shotgun, shooter, DamageTeam.Player, _config, null);
            Assert.IsTrue(assisted.Assisted);
            var expected = (enemy.GetComponent<CombatHurtbox>().AimPoint - assisted.SpawnPosition).normalized;
            Assert.Less(Vector2.Angle(assisted.Direction, expected), 0.5f, "the shot is bent toward the hurtbox centre, no teleport, no curve");

            // Pellets spread around the assisted centre: the pattern's mean direction is the assisted direction.
            var directions = new List<Vector2>();
            new SpreadFiringPattern(8, 16f, new SeededRandom(3)).ResolveDirections(assisted.Direction, directions);
            Assert.AreEqual(8, directions.Count);
            var mean = directions.Aggregate(Vector2.zero, (acc, d) => acc + d).normalized;
            Assert.Less(Vector2.Angle(mean, assisted.Direction), 6f, "spread preserved around the assisted centre");
            Assert.IsTrue(directions.Any(d => Vector2.Angle(d, assisted.Direction) > 1f), "spread is still a spread");
        }
    }
}
