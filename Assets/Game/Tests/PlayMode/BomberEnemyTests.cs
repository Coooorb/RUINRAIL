using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Area;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 067 — Bomber archetype: telegraphed lobbed bomb resolving through the shared explosion path.</summary>
    public class BomberEnemyTests
    {
        private EnemyDefinition _bomber;
        private GameObject _enemyObject;
        private EnemyController _enemy;
        private EnemyLobAttack _attack;
        private GameObject _targetObject;
        private HealthComponent _targetHealth;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _bomber = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/Game/ScriptableObjects/Enemies/Bomber.asset");
            Assert.IsNotNull(_bomber);
            _targetObject = new GameObject("Player");
            _created.Add(_targetObject);
            _targetObject.transform.position = new Vector3(30f, 0f, 0f);
            _targetObject.AddComponent<CircleCollider2D>().isTrigger = true;
            _targetObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            _targetHealth = _targetObject.AddComponent<HealthComponent>();
            _targetHealth.SetMaxHealth(200);

            _enemyObject = new GameObject("Bomber");
            _created.Add(_enemyObject);
            _enemyObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            _enemy = _enemyObject.AddComponent<EnemyController>();
            _enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 18 });
            _enemy.SetTarget(_targetObject.transform);
            _enemy.SetDefinition(_bomber);
            _attack = _enemyObject.GetComponent<EnemyLobAttack>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var grenade in Object.FindObjectsByType<ThrownGrenade>(FindObjectsSortMode.None)) Object.DestroyImmediate(grenade.gameObject);
            _created.Clear();
        }

        [Test]
        public void Bomber_DataMatchesCombat44_AndComposesTheLobBehaviour()
        {
            Assert.AreEqual("bomber", _bomber.Id);
            Assert.AreEqual(40, _bomber.BaseHealth);
            Assert.AreEqual((16, 20), (_bomber.DamageMin, _bomber.DamageMax));
            Assert.AreEqual(2.5f, _bomber.MoveSpeed, 0.001f);
            Assert.AreEqual(28, _bomber.BaseXp);
            Assert.AreEqual(7, _bomber.UnlockDepth, "Unlocks from Depth 7.");
            Assert.AreEqual(EnemyAttackKind.Lob, _bomber.AttackKind);
            Assert.Greater(_bomber.BombRadiusTiles, 0f);
            Assert.GreaterOrEqual(_bomber.AttackTelegraphSeconds / EnemyController.MaxAttackSpeedMultiplier, 0.45f);
            var flight = _bomber.AttackRange / _bomber.ProjectileSpeed;
            Assert.GreaterOrEqual(flight, 0.8f, "Bomb flight at max range gives a readable pre-detonation window.");
            CollectionAssert.Contains(_bomber.SpawnTags, "explosive");
            Assert.IsNotNull(_attack);
            Assert.AreSame(_attack, _enemy.Attack);
            Assert.AreEqual(GrenadeEffectKind.Frag, _attack.BombData.Kind);
            Assert.AreEqual((16, 20, 2f), (_attack.BombData.DamageMin, _attack.BombData.DamageMax, _attack.BombData.RadiusTiles));
            Assert.AreEqual(40, _enemyObject.GetComponent<HealthComponent>().MaxHealth);
            Assert.AreEqual(28, _enemy.XpValue);
        }

        [UnityTest]
        public IEnumerator Bomber_LocksLandingAtTelegraph_ThrowsAVisibleBomb_ThatExplodesThroughTheSharedPath()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(6f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Telegraph, _enemy.State);
            Assert.AreEqual(new Vector2(6f, 0f), _attack.LandingPoint, "Landing point locked at telegraph start (the player's approximate position).");
            Assert.AreEqual(0, _attack.BombsThrown);

            // The player moves during the telegraph: the bomb still flies to the locked zone.
            _targetObject.transform.position = new Vector3(6f, 3.5f, 0f);
            yield return new WaitForSeconds(0.55f);
            Assert.AreEqual(1, _attack.BombsThrown);
            var bomb = _attack.LastGrenade;
            Assert.IsNotNull(bomb);
            Assert.IsFalse(bomb.IsResolved, "In flight: the zone is telegraphed, nothing has exploded.");
            Assert.AreEqual(new Vector2(6f, 0f), bomb.LandingPoint);
            AreaDamageResolver.Result result = default;
            bomb.Resolved += (_, r) => result = r;

            yield return new WaitForSeconds(1.1f); // 6 tiles at 6 u/s = 1.0 s
            Assert.AreEqual(200, _targetHealth.CurrentHealth, "Dodged: 3.5 tiles from the zone (radius 2).");
            Assert.AreEqual(0, result.TargetsHit);

            // Standing in the zone: explosion damage once, with the Explosion tag (Blast Suit style reduction applies).
            _targetObject.transform.position = new Vector3(6f, 0.5f, 0f);
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var stats = new PlayerStats(caps, 200);
            stats.SetSource(new StatModifierSource("blast_suit", StatModifier.Percent(StatId.ExplosionDamageReduction, 30)));
            _targetHealth.SetIncomingDamageModifier(new StatsModifier(stats));
            _enemyObject.transform.position = new Vector3(2f, 0f, 0f);
            Physics2D.SyncTransforms();
            yield return new WaitForSeconds(2.6f); // recovery ends
            yield return new WaitForSeconds(0.6f); // telegraph
            Assert.AreEqual(2, _attack.BombsThrown);
            yield return new WaitForSeconds(0.9f); // ~4 tiles at 6 u/s
            Assert.AreEqual(200 - 13, _targetHealth.CurrentHealth, "18 -> 13 after the 30% explosion reduction, applied once.");
        }

        private sealed class StatsModifier : IIncomingDamageModifier
        {
            private readonly PlayerStats _stats;
            public StatsModifier(PlayerStats stats) => _stats = stats;
            public int ModifyIncomingDamage(DamageRequest request) => _stats.ApplyDamageReduction(request.Amount, request.IsExplosion);
        }

        [UnityTest]
        public IEnumerator Bomber_KeepsDistance_AndDeathStopsEverything()
        {
            _enemyObject.transform.position = Vector3.zero;
            _targetObject.transform.position = new Vector3(2f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(EnemyState.Chase, _enemy.State, "Inside the preferred distance it repositions first.");
            for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
            Assert.Less(_enemyObject.transform.position.x, -0.2f, "Backs away at 2.5 u/s.");

            _enemyObject.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999));
            Assert.AreEqual(EnemyState.Dead, _enemy.State);
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(0, _attack.BombsThrown, "A dead bomber throws nothing.");
            Assert.AreEqual(Vector2.zero, _enemyObject.GetComponent<Rigidbody2D>().linearVelocity);
        }
    }
}
