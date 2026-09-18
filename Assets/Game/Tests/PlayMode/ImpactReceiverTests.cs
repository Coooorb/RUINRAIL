using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 050 — receivers in the scene: displacement, wall impact, boss immunity, stagger interruption, player hooks.</summary>
    public class ImpactReceiverTests
    {
        private readonly List<Object> _created = new();
        private StaggerConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = StaggerConfig.Create(threshold: 10f, recoveryPerSecond: 5f, staggerDuration: 0.3f, immunity: 0.5f, unitsPerPoint: 0.25f, maxDistance: 4f, knockbackDuration: 0.1f);
            _created.Add(_config);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private GameObject NewBody(string name, Vector2 position, bool kinematic = false)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = kinematic ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            rb.freezeRotation = true;
            go.AddComponent<CircleCollider2D>().radius = 0.25f;
            return go;
        }

        private ImpactReceiver NewReceiver(string id, Vector2 position, int staggerResist, int knockbackResist, bool displaceable, bool boss)
        {
            var go = NewBody(id, position);
            var receiver = go.AddComponent<ImpactReceiver>();
            receiver.SetConfig(_config);
            receiver.SetProfile(new ImpactProfile(id, staggerResist, knockbackResist, displaceable, boss));
            return receiver;
        }

        private GameObject NewWall(Vector2 position, Vector2 size)
        {
            var wall = new GameObject("Wall");
            _created.Add(wall);
            wall.transform.position = position;
            wall.AddComponent<BoxCollider2D>().size = size;
            wall.AddComponent<EnvironmentObstacle>();
            return wall;
        }

        private static IEnumerator FixedSteps(int steps)
        {
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        private sealed class RecordingFeedback : IImpactAttackerFeedback
        {
            public readonly List<string> Staggered = new();
            public readonly List<(string id, bool boss)> Walls = new();
            public WallImpactOutcome WallOutcome;
            public void OnTargetStaggered(string targetId) => Staggered.Add(targetId);
            public WallImpactOutcome OnTargetKnockedIntoWall(string targetId, bool isBoss) { Walls.Add((targetId, isBoss)); return WallOutcome; }
        }

        // ---- Acceptance 1: knockback moves valid targets predictably; resistance reduces within cap ----

        [UnityTest]
        public IEnumerator Knockback_DisplacesByComputedDistance_AndResistanceShortensIt()
        {
            var weak = NewReceiver("grunt", Vector2.zero, 0, 0, true, false);
            var tough = NewReceiver("brute", new Vector2(0f, 5f), 0, 50, true, false);
            yield return null;

            var push = new ImpactRequest(Vector2.right, 8f, 0f);
            var a = weak.ApplyKnockback(push);
            var b = tough.ApplyKnockback(push);
            Assert.AreEqual(2f, a.Distance, 1e-4f);
            Assert.AreEqual(1f, b.Distance, 1e-4f);
            Assert.IsTrue(weak.IsKnockbackActive);

            yield return FixedSteps(20);
            Assert.IsFalse(weak.IsKnockbackActive);
            Assert.AreEqual(2f, weak.transform.position.x, 0.15f, "Grunt travelled the full 2 units.");
            Assert.AreEqual(1f, tough.transform.position.x, 0.15f, "50% resistance: 1 unit.");
            Assert.AreEqual(0f, weak.transform.position.y, 0.05f);
            Assert.AreEqual(Vector2.zero, weak.GetComponent<Rigidbody2D>().linearVelocity, "Velocity cleared after the push.");
            Assert.AreEqual(1, weak.KnockbacksApplied);

            // A weaker push during flight does not stack; a stronger one replaces the remainder.
            weak.ApplyKnockback(new ImpactRequest(Vector2.up, 8f, 0f));
            Assert.AreEqual(0f, weak.ApplyKnockback(new ImpactRequest(Vector2.up, 1f, 0f)).Distance);
            Assert.AreEqual(4f, weak.ApplyKnockback(new ImpactRequest(Vector2.up, 40f, 0f)).Distance, 1e-4f, "Capped at max distance, replaces the remainder.");
        }

        // ---- Acceptance 3: bosses / immune targets are never wall-knocked ----

        [UnityTest]
        public IEnumerator Boss_IsNeverDisplaced_AndFullyResistantTargetsDoNotMove()
        {
            var boss = NewReceiver("conductor", Vector2.zero, 95, 100, false, true);
            var anchored = NewReceiver("anchor", new Vector2(0f, 5f), 0, 100, true, false);
            NewWall(new Vector2(1f, 0f), new Vector2(0.5f, 10f));
            var feedback = new RecordingFeedback { WallOutcome = new WallImpactOutcome(15, 20, true) };
            yield return null;

            var result = boss.ApplyKnockback(new ImpactRequest(Vector2.right, 100f, 0f, DamageKind.Normal, null, feedback));
            Assert.IsFalse(result.Moved);
            Assert.IsFalse(boss.IsKnockbackActive);
            Assert.IsFalse(anchored.ApplyKnockback(new ImpactRequest(Vector2.right, 100f, 0f)).Moved);
            yield return FixedSteps(10);
            Assert.AreEqual(0f, boss.transform.position.x, 1e-3f);
            Assert.AreEqual(0f, anchored.transform.position.x, 1e-3f);
            Assert.AreEqual(0, boss.WallImpacts);
            Assert.IsEmpty(feedback.Walls, "No wall-impact hook for an undisplaceable boss.");

            // Boss stagger: 95% resistance means 20 heavy hits, then a fixed immunity — never a lock.
            for (var i = 0; i < 19; i++) Assert.IsFalse(boss.ApplyStagger(new ImpactRequest(Vector2.zero, 0f, 10f)).Triggered);
            Assert.IsTrue(boss.ApplyStagger(new ImpactRequest(Vector2.zero, 0f, 10f)).Triggered);
            Assert.IsTrue(boss.IsStaggered);
        }

        // ---- Wall impact: attacker hook, bonus damage and high stagger through the receiver only ----

        [UnityTest]
        public IEnumerator KnockedIntoWall_StopsAtTheWall_AndAppliesTheAttackerHookOutcome()
        {
            var enemy = NewReceiver("grunt", Vector2.zero, 0, 0, true, false);
            var health = enemy.gameObject.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 17 });
            NewWall(new Vector2(1.5f, 0f), new Vector2(0.5f, 10f));
            var feedback = new RecordingFeedback { WallOutcome = new WallImpactOutcome(15, 20, true) };
            yield return null;

            var walls = 0;
            enemy.KnockedIntoWall += _ => walls++;
            Assert.AreEqual(4f, enemy.ApplyKnockback(new ImpactRequest(Vector2.right, 40f, 0f, DamageKind.Normal, null, feedback)).Distance, 1e-4f);
            yield return FixedSteps(20);

            Assert.AreEqual(1, walls);
            Assert.AreEqual(1, enemy.WallImpacts);
            Assert.Less(enemy.transform.position.x, 1.3f, "Stopped at the wall instead of travelling 4 units.");
            CollectionAssert.AreEqual(new[] { ("grunt", false) }, feedback.Walls);
            Assert.AreEqual(83, health.CurrentHealth, "15-20 bonus damage rolled (fixed 17) through IDamageable.");
            Assert.AreEqual(1, enemy.Meter.TriggerCount, "High stagger (10 = threshold) staggered it (the 0.3 s stagger may already have elapsed).");
            CollectionAssert.AreEqual(new[] { "grunt" }, feedback.Staggered);

            // Without a hook outcome nothing extra happens.
            var plain = NewReceiver("swarm", new Vector2(0f, -5f), 0, 0, true, false);
            var plainHealth = plain.gameObject.AddComponent<HealthComponent>();
            plainHealth.SetMaxHealth(50);
            NewWall(new Vector2(1.5f, -5f), new Vector2(0.5f, 2f));
            yield return null;
            plain.ApplyKnockback(new ImpactRequest(Vector2.right, 40f, 0f));
            yield return FixedSteps(20);
            Assert.AreEqual(1, plain.WallImpacts);
            Assert.AreEqual(50, plainHealth.CurrentHealth);
            Assert.IsFalse(plain.IsStaggered);
        }

        // ---- Acceptance 2 in the scene: a normal enemy's action is interrupted exactly at the threshold ----

        [UnityTest]
        public IEnumerator EnemyController_TelegraphIsInterruptedByStagger_ThenResumes()
        {
            var definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            _created.Add(definition);
            void Set(string f, object v) => typeof(EnemyDefinition).GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(definition, v);
            Set("_id", "grunt"); Set("_baseHealth", 30); Set("_damageMin", 6); Set("_damageMax", 8); Set("_moveSpeed", 3f); Set("_baseXp", 12);
            Set("_attackRange", 1f); Set("_attackTelegraphSeconds", 5f); Set("_attackCooldownSeconds", 0.05f); Set("_staggerResistancePercent", 20);

            var target = new GameObject("Target");
            _created.Add(target);
            target.transform.position = new Vector3(0.5f, 0f, 0f);
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            var targetHits = target.AddComponent<TestDamageableTarget>();

            var enemyObject = new GameObject("Grunt");
            _created.Add(enemyObject);
            var enemy = enemyObject.AddComponent<EnemyController>();
            enemy.SetDamageRoller(new FixedDamageRoller { FixedValue = 7 });
            enemy.SetTarget(target.transform);
            enemy.SetDefinition(definition);
            enemy.SetStaggerConfig(_config);
            yield return null; // Idle -> Chase
            yield return null; // Chase -> Telegraph
            Assert.AreEqual(EnemyState.Telegraph, enemy.State);
            Assert.IsNotNull(enemy.Impact);
            Assert.AreEqual(20, enemy.Impact.Profile.StaggerResistancePercent);

            var feedback = new RecordingFeedback();
            // 20% resistance: 10 -> 8 applied, second hit crosses 10 exactly at 12.5 -> use 2.5 to land on the threshold.
            Assert.IsFalse(ImpactApply(enemyObject, 10f, feedback).Triggered);
            Assert.AreEqual(EnemyState.Telegraph, enemy.State, "Below threshold: the swing continues.");
            Assert.IsTrue(ImpactApply(enemyObject, 2.5f, feedback).Triggered);
            Assert.AreEqual(EnemyState.Staggered, enemy.State, "Threshold crossed: the telegraph is interrupted.");
            Assert.IsTrue(enemy.IsStaggered);
            CollectionAssert.AreEqual(new[] { "grunt" }, feedback.Staggered);

            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(0, targetHits.HitCount, "The interrupted attack never lands.");
            Assert.AreNotEqual(EnemyState.Staggered, enemy.State);
            Assert.IsFalse(enemy.IsStaggered);
            yield return new WaitForSeconds(0.1f);
            Assert.That(enemy.State, Is.EqualTo(EnemyState.Chase).Or.EqualTo(EnemyState.Telegraph), "Resumes from recovery.");
        }

        private static StaggerResult ImpactApply(GameObject target, float stagger, IImpactAttackerFeedback feedback)
        {
            return target.GetComponent<IStaggerReceiver>().ApplyStagger(new ImpactRequest(Vector2.right, 0f, stagger, DamageKind.Normal, null, feedback));
        }

        // ---- Acceptance 4: player-side hooks intercept without any item class ----

        [UnityTest]
        public IEnumerator PlayerReceiver_UsesEventHooksAndStats_WithoutKnowingItems()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>(); // defaults = approved 50% caps
            _created.Add(caps);
            var stats = new PlayerStats(caps);
            var events = new PlayerCombatEvents();
            var player = NewBody("Player", Vector2.zero);
            var receiver = player.AddComponent<PlayerImpactReceiver>();
            receiver.SetConfig(_config);
            receiver.SetStats(stats);
            receiver.SetEvents(events);
            yield return null;

            // A test hook plays "Anchored": negate the next stagger once.
            var negateOnce = true;
            events.StaggerIncoming += r => { if (negateOnce) { negateOnce = false; r.Negate("test_anchored"); } };
            var first = receiver.ApplyStagger(new ImpactRequest(Vector2.right, 0f, 50f));
            Assert.IsTrue(first.Negated);
            Assert.IsFalse(receiver.IsStaggered);
            Assert.AreEqual(1, receiver.StaggersNegated);
            var second = receiver.ApplyStagger(new ImpactRequest(Vector2.right, 0f, 50f));
            Assert.IsTrue(second.Triggered);
            Assert.IsTrue(receiver.IsStaggered);
            Assert.IsTrue(receiver.IsActive, "Stagger owns movement while it lasts.");
            yield return new WaitForSeconds(0.35f);
            Assert.IsFalse(receiver.IsStaggered);

            // "Shock Absorber": explosion knockback negated, normal knockback still applies and is reduced by capped resistance.
            events.ExplosionKnockbackIncoming += r => r.Negate("test_shock_absorber");
            Assert.IsTrue(receiver.ApplyKnockback(new ImpactRequest(Vector2.right, 8f, 0f, DamageKind.Explosion)).Negated);
            Assert.AreEqual(1, receiver.KnockbacksNegated);
            stats.SetSource(new StatModifierSource("test_exo", StatModifier.Percent(StatId.KnockbackResistance, 80)));
            var normal = receiver.ApplyKnockback(new ImpactRequest(Vector2.right, 8f, 0f));
            Assert.AreEqual(1f, normal.Distance, 1e-4f, "8 x 0.25 = 2, halved by the 50% capped resistance.");
            yield return FixedSteps(15);
            Assert.AreEqual(1f, player.transform.position.x, 0.15f);
            Assert.IsFalse(receiver.IsActive);

            // Attacker-side: the receiver forwards to the hub; a test hook plays "Wallbreaker" / "Shock Charm".
            string staggeredId = null;
            events.EnemyStaggeredByWearer += id => staggeredId = id;
            events.EnemyKnockedIntoWall += r => { if (!r.IsBoss) { r.BonusDamageMin = 15; r.BonusDamageMax = 20; r.ApplyHighStagger = true; } };
            receiver.OnTargetStaggered("grunt");
            Assert.AreEqual("grunt", staggeredId);
            var outcome = receiver.OnTargetKnockedIntoWall("grunt", false);
            Assert.AreEqual((15, 20, true), (outcome.BonusDamageMin, outcome.BonusDamageMax, outcome.ApplyHighStagger));
            Assert.IsFalse(receiver.OnTargetKnockedIntoWall("conductor", true).HasBonus, "Bosses never trigger it.");
        }

        [UnityTest]
        public IEnumerator Shockwave_PushesEveryEnemyAwayFromTheCentre_NeverAllies()
        {
            var source = NewBody("Player", Vector2.zero);
            source.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var ally = NewBody("Ally", new Vector2(0.5f, 0f));
            ally.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var allyReceiver = ally.AddComponent<ImpactReceiver>();
            allyReceiver.SetConfig(_config);
            var east = NewReceiver("east", new Vector2(1f, 0f), 0, 0, true, false);
            var north = NewReceiver("north", new Vector2(0f, 1f), 0, 0, true, false);
            var far = NewReceiver("far", new Vector2(10f, 0f), 0, 0, true, false);
            yield return null;

            var affected = ShockwaveResolver.Emit(Vector2.zero, 2.5f, 8f, 8f, DamageTeam.Player, source);
            Assert.AreEqual(2, affected);
            Assert.AreEqual(8f, east.Meter.Pressure, 1e-4f, "Stagger pressure landed as well (decays afterwards).");
            Assert.AreEqual(0f, allyReceiver.Meter.Pressure);
            yield return FixedSteps(15);
            Assert.AreEqual(3f, east.transform.position.x, 0.2f);
            Assert.AreEqual(3f, north.transform.position.y, 0.2f);
            Assert.AreEqual(10f, far.transform.position.x, 1e-3f);
            Assert.AreEqual(0.5f, ally.transform.position.x, 1e-3f, "Friendly fire is off for impacts too.");
        }
    }
}
