using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>The Conductor reference (1050 HP, 30–36 strongest, XP 650, phase 2 at 50%), runtime definitions mirroring the assets.</summary>
    public class ConductorBossTests
    {
        private readonly List<Object> _created = new();
        private BossDefinition _definition;
        private EnemyAttackDefinition _rail, _burst, _sweep, _dash, _hazard;

        [SetUp]
        public void SetUp()
        {
            _rail = Attack("conductor_marked_rail_strike", AttackMotion.Zone, 30, 36, 0f, 12f, 1.1f, 1.2f, 1, 0f, 7f);
            Set(_rail, "_zoneLength", 10f); Set(_rail, "_zoneWidth", 1.6f);
            _burst = Attack("conductor_burst_cannon", AttackMotion.Projectile, 10, 12, 4f, 14f, 0.6f, 0.8f, 3, 0.15f, 3f);
            Set(_burst, "_projectileCount", 1); Set(_burst, "_projectileSpeed", 16f); Set(_burst, "_projectileRange", 14f);
            _sweep = Attack("conductor_projectile_sweep", AttackMotion.Projectile, 12, 14, 2f, 10f, 0.8f, 1.0f, 1, 0f, 5f);
            Set(_sweep, "_projectileCount", 7); Set(_sweep, "_spreadDegrees", 90f); Set(_sweep, "_projectileSpeed", 12f); Set(_sweep, "_projectileRange", 12f);
            _dash = Attack("conductor_emergency_dash", AttackMotion.Dash, 8, 10, 0f, 4f, 0.4f, 0.5f, 1, 0f, 6f);
            Set(_dash, "_hitRadius", 0.9f); Set(_dash, "_dashDistance", 5f); Set(_dash, "_dashSpeed", 16f);
            _hazard = Attack("conductor_rail_line_hazard", AttackMotion.Zone, 20, 24, 0f, 30f, 1.4f, 0.6f, 1, 0f, 9f);
            Set(_hazard, "_zoneLength", 30f); Set(_hazard, "_zoneWidth", 1.4f);

            _definition = ScriptableObject.CreateInstance<BossDefinition>();
            _created.Add(_definition);
            Set(_definition, "_id", "boss_the_conductor");
            Set(_definition, "_baseHealth", 1050);
            Set(_definition, "_moveSpeed", 2f);
            Set(_definition, "_baseXp", 650);
            Set(_definition, "_strongestAttackDamageMin", 30);
            Set(_definition, "_strongestAttackDamageMax", 36);
            Set(_definition, "_phaseTwoHealthFraction", 0.5f);
            Set(_definition, "_phaseTwoTimingMultiplier", 0.8f);
            Set(_definition, "_moveset", new[] { _dash, _sweep, _burst, _rail });
            Set(_definition, "_phaseTwoArenaHazards", new[] { _hazard });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private EnemyAttackDefinition Attack(string id, AttackMotion motion, int dmin, int dmax, float minR, float maxR, float tele, float rec, int hits, float interval, float cooldown)
        {
            var a = ScriptableObject.CreateInstance<EnemyAttackDefinition>();
            _created.Add(a);
            Set(a, "_id", id); Set(a, "_displayName", id); Set(a, "_motion", motion);
            Set(a, "_damageMin", dmin); Set(a, "_damageMax", dmax);
            Set(a, "_minTriggerRange", minR); Set(a, "_maxTriggerRange", maxR);
            Set(a, "_telegraphSeconds", tele); Set(a, "_recoverySeconds", rec);
            Set(a, "_hitCount", hits); Set(a, "_hitIntervalSeconds", interval); Set(a, "_cooldownSeconds", cooldown);
            return a;
        }

        private (BossController boss, BossEncounter encounter, ProjectilePool pool) SpawnConductor(Vector2 position, BossDefinition definition = null)
        {
            var root = new GameObject("BossEncounter");
            _created.Add(root);
            var encounter = root.AddComponent<BossEncounter>();
            var go = new GameObject("TheConductor");
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.7f;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<HealthComponent>();
            var pool = go.AddComponent<ProjectilePool>();
            var boss = go.AddComponent<BossController>();
            boss.SetProjectilePool(pool);
            boss.SetDefinition(definition != null ? definition : _definition);
            encounter.Bind(boss);
            return (boss, encounter, pool);
        }

        private (GameObject go, HealthComponent health) SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(1000);
            return (go, health);
        }

        private BossDefinition SingleAttackBoss(params EnemyAttackDefinition[] moveset)
        {
            var d = ScriptableObject.CreateInstance<BossDefinition>();
            _created.Add(d);
            Set(d, "_id", "boss_test");
            Set(d, "_baseHealth", 1050);
            Set(d, "_baseXp", 650);
            Set(d, "_moveset", moveset);
            Set(d, "_phaseTwoArenaHazards", new EnemyAttackDefinition[0]);
            return d;
        }

        [Test]
        public void Conductor_ExposesApprovedStats_AndStartsInPhaseOne()
        {
            var (boss, encounter, _) = SpawnConductor(Vector2.zero);
            Assert.AreEqual(1050, boss.Health.MaxHealth);
            Assert.AreEqual(650, boss.XpValue);
            Assert.AreEqual(1, boss.Phase);
            Assert.IsFalse(boss.IsPhaseTwo);
            Assert.IsFalse(encounter.IsStarted);
            Assert.IsFalse(encounter.IsDefeated);
            Assert.AreEqual(1f, boss.TimingMultiplier, 0.0001f);
        }

        [Test]
        public void PhaseTwo_TriggersExactlyOnceAtHalfHealth_AndNeverAThirdTime()
        {
            var (boss, encounter, _) = SpawnConductor(Vector2.zero);
            var changes = new List<int>();
            boss.PhaseChanged += (_, p) => changes.Add(p);
            var encounterChanges = new List<int>();
            encounter.PhaseChanged += (_, p) => encounterChanges.Add(p);

            boss.Health.TryApplyDamage(new DamageRequest(524));
            Assert.AreEqual(526, boss.Health.CurrentHealth);
            Assert.AreEqual(1, boss.Phase, "526/1050 > 50%: still phase 1.");

            boss.Health.TryApplyDamage(new DamageRequest(1));
            Assert.AreEqual(2, boss.Phase, "525/1050 = 50%: phase 2.");
            Assert.AreEqual(0.8f, boss.TimingMultiplier, 0.0001f);
            CollectionAssert.AreEqual(new[] { 2 }, changes);
            CollectionAssert.AreEqual(new[] { 2 }, encounterChanges);

            boss.Health.TryApplyDamage(new DamageRequest(400));
            boss.Health.TryApplyDamage(new DamageRequest(100));
            Assert.AreEqual(25, boss.Health.CurrentHealth);
            Assert.AreEqual(2, boss.Phase, "No third phase at low health.");
            CollectionAssert.AreEqual(new[] { 2 }, changes);
        }

        [Test]
        public void PhaseTwo_AddsArenaHazardToRotation_AndKeepsFamiliarAttacks()
        {
            var (boss, _, _) = SpawnConductor(Vector2.zero);
            var (player, _) = SpawnPlayerDummy(new Vector2(20f, 0f));
            boss.SetTarget(player.transform);
            Assert.IsNull(boss.SelectAttack(), "20 tiles: no phase-1 attack in range.");

            boss.Health.TryApplyDamage(new DamageRequest(600));
            Assert.AreSame(_hazard, boss.SelectAttack(), "Phase 2: rail-line hazard covers the whole arena.");
            player.transform.position = new Vector2(6f, 0f);
            var selected = boss.SelectAttack();
            Assert.IsTrue(selected == _hazard || selected == _sweep || selected == _burst || selected == _rail, "Familiar mechanics stay in rotation.");
        }

        [UnityTest]
        public IEnumerator MarkedRailStrike_TelegraphsThenHitsInLineFor30To36_OnlyInsideTheZone()
        {
            var (boss, _, _) = SpawnConductor(Vector2.zero, SingleAttackBoss(_rail));
            var (player, health) = SpawnPlayerDummy(new Vector2(6f, 0f));
            var (bystander, bystanderHealth) = SpawnPlayerDummy(new Vector2(6f, 4f));
            var hits = new List<int>();
            health.Damaged += d => hits.Add(d);
            boss.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, boss.State);
            Assert.AreSame(_rail, boss.CurrentAttack);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(1000, health.CurrentHealth, "No damage during the 1.1s telegraph.");
            yield return new WaitForSeconds(0.5f);

            Assert.AreEqual(1, hits.Count, "One marked strike = one hit.");
            Assert.That(hits[0], Is.InRange(30, 36));
            Assert.AreEqual(1000, bystanderHealth.CurrentHealth, "Off the rail line: untouched.");
        }

        [UnityTest]
        public IEnumerator BurstCannonAndSweep_FireVisiblePooledProjectiles_ThatDamageThroughTheProjectileBoundary()
        {
            var (boss, _, pool) = SpawnConductor(Vector2.zero, SingleAttackBoss(_burst));
            var (player, health) = SpawnPlayerDummy(new Vector2(6f, 0f));
            boss.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreSame(_burst, boss.CurrentAttack);
            yield return new WaitForSeconds(0.6f + 0.15f * 3 + 0.2f);

            var spawned = boss.Resolver.SpawnedProjectiles;
            Assert.AreEqual(3, spawned.Count, "Burst Cannon: three volleys of one projectile.");
            yield return new WaitForSeconds(0.8f);
            Assert.Less(health.CurrentHealth, 1000, "Projectiles reached the target.");
            Assert.GreaterOrEqual(health.CurrentHealth, 1000 - 3 * 12);
            Assert.LessOrEqual(health.CurrentHealth, 1000 - 10, "At least one 10–12 projectile hit.");

            var (sweeper, _, _) = SpawnConductor(new Vector2(0f, 30f), SingleAttackBoss(_sweep));
            var (target2, _) = SpawnPlayerDummy(new Vector2(6f, 30f));
            sweeper.SetTarget(target2.transform);
            yield return null;
            yield return null;
            yield return new WaitForSeconds(1.0f);
            var fan = sweeper.Resolver.SpawnedProjectiles;
            Assert.AreEqual(7, fan.Count, "Projectile Sweep fans seven projectiles.");
            var angles = fan.Select(p => Vector2.SignedAngle(Vector2.right, p.Data.Direction)).OrderBy(a => a).ToArray();
            Assert.AreEqual(-45f, angles[0], 0.5f);
            Assert.AreEqual(45f, angles[^1], 0.5f);
            Assert.IsTrue(fan.All(p => p.Data.Source == sweeper.gameObject));
        }

        [UnityTest]
        public IEnumerator EmergencyDash_MovesTheBoss_AndDeathStopsAnyRunningAttack()
        {
            var (boss, encounter, _) = SpawnConductor(Vector2.zero, SingleAttackBoss(_dash));
            var (player, health) = SpawnPlayerDummy(new Vector2(3f, 0f));
            boss.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreSame(_dash, boss.CurrentAttack);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(MovesetActorState.Attacking, boss.State);
            var xDuringDash = boss.transform.position.x;
            Assert.Greater(xDuringDash, 0.2f, "Dash is moving the boss.");

            var defeats = 0;
            encounter.BossDefeated += (_, _) => defeats++;
            boss.Health.TryApplyDamage(new DamageRequest(1050));
            Assert.AreEqual(MovesetActorState.Dead, boss.State);
            Assert.IsNull(boss.CurrentAttack);
            yield return new WaitForFixedUpdate();
            var xAfterDeath = boss.transform.position.x;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(xAfterDeath, boss.transform.position.x, 0.05f, "Dead boss stops moving.");
            Assert.AreEqual(1, defeats);
        }

        [UnityTest]
        public IEnumerator BossDefeat_SignalsExactlyOnce_WithXp650_AndStartSignalledOnce()
        {
            var (boss, encounter, _) = SpawnConductor(Vector2.zero);
            var (player, _) = SpawnPlayerDummy(new Vector2(20f, 0f));
            var started = 0;
            var defeated = new List<int>();
            encounter.BossStarted += _ => started++;
            encounter.BossDefeated += (_, xp) => defeated.Add(xp);
            boss.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual(1, started);

            boss.Health.TryApplyDamage(new DamageRequest(1049));
            Assert.IsTrue(boss.IsAlive);
            Assert.AreEqual(2, boss.Phase);
            boss.Health.TryApplyDamage(new DamageRequest(1));
            Assert.IsFalse(boss.IsAlive);
            CollectionAssert.AreEqual(new[] { 650 }, defeated);
            Assert.IsTrue(encounter.IsDefeated);
            Assert.AreEqual(650, encounter.XpAwarded);

            Assert.IsFalse(boss.Health.TryApplyDamage(new DamageRequest(5)));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, defeated.Count);
            Assert.AreEqual(1, started);
        }

        [Test]
        public void Encounter_HoldsExactlyOneBoss()
        {
            var (boss, encounter, _) = SpawnConductor(Vector2.zero);
            var (other, _, _) = SpawnConductor(new Vector2(10f, 0f));
            Assert.AreSame(boss, encounter.Boss);
            Assert.Throws<System.InvalidOperationException>(() => encounter.Bind(other));
        }
    }
}
