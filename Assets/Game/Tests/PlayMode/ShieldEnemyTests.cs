using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 068 — Shield Enemy: orientation-aware 80% frontal projectile reduction, no shield HP.</summary>
    public class ShieldEnemyTests
    {
        private EnemyDefinition _definition;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private HealthComponent _health;
        private GameObject _targetObject;
        private TestDamageableTarget _target;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/ShieldEnemy.asset");
            Assert.IsNotNull(_definition);
            _targetObject = new GameObject("Player");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(6f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _target = _targetObject.AddComponent<TestDamageableTarget>();

            _enemyObject = new GameObject("ShieldEnemy");
            _created.Add(_enemyObject);
            _enemyObject.AddComponent<CircleCollider2D>().radius = 0.4f;
            _enemyObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _health = _enemyObject.GetComponent<HealthComponent>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 12 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_definition);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        [Test]
        public void ShieldEnemy_DataMatchesCombat44_AndComposesTheShield()
        {
            Assert.AreEqual("shield_enemy", _definition.Id);
            Assert.AreEqual(55, _definition.BaseHealth);
            Assert.AreEqual((10, 14), (_definition.DamageMin, _definition.DamageMax));
            Assert.AreEqual(2.2f, _definition.MoveSpeed, 0.001f);
            Assert.AreEqual(30, _definition.BaseXp);
            Assert.AreEqual(9, _definition.UnlockDepth, "Unlocks from Depth 9.");
            Assert.AreEqual(80, _definition.FrontalShieldPercent, "44: 80% frontal projectile reduction.");
            Assert.IsTrue(_definition.HasFrontalShield);
            Assert.AreEqual(EnemyAttackKind.MeleeContact, _definition.AttackKind, "Shield Bash is the melee contact attack.");
            Assert.GreaterOrEqual(_definition.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.45f);
            CollectionAssert.Contains(_definition.SpawnTags, "shield");
            Assert.IsNotNull(_enemy.Shield);
            Assert.AreEqual(80, _enemy.Shield.ReductionPercent);
            Assert.AreEqual(55, _health.MaxHealth);
            Assert.AreEqual(30, _enemy.XpValue);
            Assert.IsNull(typeof(FrontalShield).GetProperty("ShieldHealth"), "No shield-HP subsystem.");
        }

        [UnityTest]
        public IEnumerator FrontalProjectiles_AreReduced80Percent_FlankRearAreaAndMeleePassInFull()
        {
            yield return null; // facing follows the target at +x
            Assert.AreEqual(Vector2.right, _enemy.Shield.Facing);

            // Frontal projectile (travelling -x into the face): 20 -> 4.
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(20, DamageKind.Normal, 0f, Vector2.left)));
            Assert.AreEqual(51, _health.CurrentHealth);
            // Rear projectile (travelling +x, from behind): full 20.
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(20, DamageKind.Normal, 0f, Vector2.right)));
            Assert.AreEqual(31, _health.CurrentHealth);
            // Side projectile (travelling -y, from above): outside the 120-degree frontal arc -> full.
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(10, DamageKind.Normal, 0f, Vector2.down)));
            Assert.AreEqual(21, _health.CurrentHealth);
            // Just inside the arc (50 degrees off the face): reduced.
            var inside = Quaternion.Euler(0f, 0f, 50f) * Vector2.left;
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(10, DamageKind.Normal, 0f, inside)));
            Assert.AreEqual(19, _health.CurrentHealth);
            // Explosion from the front: bypasses.
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(10, DamageKind.Explosion, 0f, Vector2.left)));
            Assert.AreEqual(9, _health.CurrentHealth);
            // Melee (no direction): bypasses.
            Assert.IsTrue(_health.TryApplyDamage(new DamageRequest(5)));
            Assert.AreEqual(4, _health.CurrentHealth);
            Assert.AreEqual(2, _enemy.Shield.BlockedHits);
            Assert.AreEqual(4, _enemy.Shield.PassedHits);

            // Facing follows the target: move the player behind, the same -x projectile is now a rear hit.
            _health.SetMaxHealth(55);
            _targetObject.transform.position = new Vector3(-6f, 0f, 0f);
            yield return null;
            Assert.AreEqual(Vector2.left, _enemy.Shield.Facing);
            _health.TryApplyDamage(new DamageRequest(20, DamageKind.Normal, 0f, Vector2.left));
            Assert.AreEqual(35, _health.CurrentHealth, "From behind now: full damage.");
        }

        [UnityTest]
        public IEnumerator RealProjectiles_CarryTheirDirection_SoAFrontalShotIsBlockedAndARearShotIsNot()
        {
            var pool = new GameObject("Pool").AddComponent<ProjectilePool>();
            _created.Add(pool.gameObject);
            yield return null;
            // Player at +6 aims a shot travelling -x: frontal.
            var frontal = pool.Spawn(new Vector2(3f, 0f), new ProjectileSpawnData(20, 20f, 10f, 0f, 0f, Vector2.left, _targetObject, null, 0f, DamageTeam.Player));
            for (var i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(frontal.gameObject.activeSelf);
            Assert.AreEqual(51, _health.CurrentHealth, "20 -> 4 through the projectile's own direction.");

            var rear = pool.Spawn(new Vector2(-3f, 0f), new ProjectileSpawnData(20, 20f, 10f, 0f, 0f, Vector2.right, null, null, 0f, DamageTeam.Player));
            for (var i = 0; i < 15; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(rear.gameObject.activeSelf);
            Assert.AreEqual(31, _health.CurrentHealth, "Flanked: full 20.");

            // An explosion centred in front: full damage through the shared area path.
            AreaDamageResolver.Apply(new Vector2(1f, 0f), 2f, 10, 10, DamageKind.Explosion, 0f, DamageTeam.Player, new FixedDamageRoller { FixedValue = 10 });
            Assert.AreEqual(21, _health.CurrentHealth);
        }

        [UnityTest]
        public IEnumerator ShieldBash_IsATelegraphedMeleeHit_AndDeathStopsIt()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(0.8f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(0, _target.HitCount);
            yield return new WaitForSeconds(0.55f);
            Assert.AreEqual(1, _target.HitCount);
            Assert.AreEqual(12, _target.LastDamageAmount, "Integer band (fixed 12) through IDamageable.");
            _health.TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(1, _target.HitCount);
        }
    }
}
