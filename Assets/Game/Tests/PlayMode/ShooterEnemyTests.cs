using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 063 — Shooter archetype: ranged telegraphed projectile attack with spacing on the shared enemy framework.</summary>
    public class ShooterEnemyTests
    {
        private EnemyDefinition _shooter;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private EnemyProjectileAttack _attack;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _shooter = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Shooter.asset");
            Assert.IsNotNull(_shooter);

            _targetObject = new GameObject("Target");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(20f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("Shooter");
            _created.Add(_enemyObject);
            _enemyObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            _enemyObject.AddComponent<ProjectilePool>();
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 6 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_shooter);
            _attack = _enemyObject.GetComponent<EnemyProjectileAttack>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static IEnumerator FixedSteps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        // ---- Acceptance 1 + 4: exact data and generic composition ----

        [Test]
        public void Shooter_DataMatchesCombat44_AndExposesEncounterDirectorData()
        {
            Assert.AreEqual("shooter", _shooter.Id);
            Assert.AreEqual(24, _shooter.BaseHealth);
            Assert.AreEqual((5, 7), (_shooter.DamageMin, _shooter.DamageMax));
            Assert.AreEqual(2.6f, _shooter.MoveSpeed, 0.001f);
            Assert.AreEqual(14, _shooter.BaseXp);
            Assert.AreEqual(1, _shooter.UnlockDepth);
            Assert.AreEqual(EnemyAttackKind.Projectile, _shooter.AttackKind);
            Assert.Greater(_shooter.ProjectileSpeed, 0f, "Visible projectile, never hitscan.");
            Assert.IsTrue(_shooter.KeepsDistance, "Maintains medium distance.");
            Assert.Less(_shooter.PreferredDistance, _shooter.AttackRange);
            Assert.GreaterOrEqual(_shooter.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.5f, "Pre-fire telegraph stays readable at the deepest attack-speed scale.");
            CollectionAssert.Contains(_shooter.SpawnTags, "ranged");
            Assert.AreEqual(1f, _shooter.ThreatCost, "47: Shooter threat 1.");

            // Generic composition: the controller picked the projectile behaviour from the definition, no shooter script exists.
            Assert.IsNotNull(_attack);
            Assert.AreSame(_attack, _enemy.Attack);
            Assert.AreEqual(24, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(14, _enemy.XpValue);
            Assert.IsFalse(typeof(EnemyController).Assembly.GetTypes().Any(t => t.Name.Contains("Shooter")), "No archetype-specific class.");
            var grunt = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Grunt.asset");
            Assert.AreEqual(EnemyAttackKind.MeleeContact, grunt.AttackKind);
            Assert.AreEqual(1, grunt.UnlockDepth);
            CollectionAssert.Contains(grunt.SpawnTags, "melee");
        }

        // ---- Acceptance 2 + 3: telegraph -> visible projectile through the shared damage path; deterministic; stops on death ----

        [UnityTest]
        public IEnumerator Shooter_TelegraphsThenFiresAVisibleProjectile_ThatDealsIntegerBandDamage()
        {
            _targetObject.transform.position = new Vector3(5f, 0f, 0f);
            yield return null; // Idle -> Chase
            yield return null; // Chase -> Telegraph (5 < range 7)
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(0, _attack.ShotsFired, "Nothing leaves during the telegraph.");
            Assert.AreEqual(0.6f, _enemy.CurrentTelegraphSeconds, 0.001f);

            yield return new WaitForSeconds(0.65f);
            Assert.AreEqual(EnemyState.Recovery, _enemy.State);
            Assert.AreEqual(1, _attack.ShotsFired);
            var projectile = _attack.SpawnedProjectiles.Single();
            Assert.IsTrue(projectile.gameObject.activeSelf, "Visible moving projectile.");
            Assert.AreEqual(12f, projectile.Data.Speed);
            Assert.AreEqual(DamageTeam.Enemy, projectile.Data.SourceTeam);
            Assert.AreEqual(6, projectile.Data.Damage, "Integer band roll (fixed 6) carried by the projectile.");
            Assert.AreEqual(0, _target.HitCount, "Not hit until the projectile actually arrives.");

            yield return FixedSteps(30); // 5 units at 12 u/s = 0.42 s
            Assert.AreEqual(1, _target.HitCount);
            Assert.AreEqual(6, _target.LastDamageAmount);
            Assert.IsFalse(projectile.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Shooter_KeepsMediumDistance_AndCannotFireThroughSmokeOrAfterDeath()
        {
            // Too close: backs off before attacking; at range: attacks; beyond range: approaches.
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(3f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Chase, _enemy.State, "Inside the preferred 4.5 tiles a shooter repositions instead of firing.");
            var rb = _enemyObject.GetComponent<Rigidbody2D>();
            yield return FixedSteps(10);
            Assert.Less(_enemyObject.transform.position.x, -0.2f, "Backs away from the target.");
            Assert.AreEqual(0, _attack.ShotsFired);
            yield return new WaitForSeconds(1.5f);
            Assert.LessOrEqual(_enemyObject.transform.position.x, -1.4f, "Stops retreating once the preferred distance is reached, then fires.");
            Assert.GreaterOrEqual(_attack.ShotsFired, 1);

            _targetObject.transform.position = new Vector3(_enemyObject.transform.position.x + 12f, 0f, 0f);
            yield return new WaitForSeconds(1.6f); // recovery (1.5 s cooldown) ends
            yield return FixedSteps(5);
            Assert.AreEqual(EnemyState.Chase, _enemy.State);
            Assert.Greater(rb.linearVelocity.x, 2f, "Beyond attack range it approaches at 2.6.");

            // Smoke between shooter and target: no line of sight, no shot.
            _targetObject.transform.position = new Vector3(_enemyObject.transform.position.x + 5f, 0f, 0f);
            var smoke = new GameObject("Smoke").AddComponent<RuinRail.Gameplay.Combat.Area.SmokeZone>();
            _created.Add(smoke.gameObject);
            smoke.transform.position = new Vector3(_enemyObject.transform.position.x + 2.5f, 0f, 0f);
            smoke.Configure(1.5f, 10f);
            var shotsBefore = _attack.ShotsFired;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(shotsBefore, _attack.ShotsFired, "Normal enemies cannot target through smoke.");
            Object.DestroyImmediate(smoke.gameObject);

            // Death stops everything.
            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            var shotsAtDeath = _attack.ShotsFired;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(shotsAtDeath, _attack.ShotsFired);
            Assert.AreEqual(Vector2.zero, rb.linearVelocity);
        }

        [UnityTest]
        public IEnumerator AttackSpeedScaling_IsCapped_AndKeepsTheTelegraphReadable()
        {
            _enemy.SetAttackSpeedMultiplier(5f);
            Assert.AreEqual(EnemyController.MaxAttackSpeedMultiplier, _enemy.AttackSpeedMultiplier);
            Assert.AreEqual(0.6f / 1.1f, _enemy.CurrentTelegraphSeconds, 0.001f);
            _targetObject.transform.position = new Vector3(5f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State, "0.4 s in: still telegraphing (0.545 s).");
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, _attack.ShotsFired);
        }
    }
}
