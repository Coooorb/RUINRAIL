using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Tunnel Stalker reference (350 HP, 24–30 strongest, 3.6 speed, XP 250) built from runtime definitions that mirror
    /// the authored assets (validated in EditMode).
    /// </summary>
    public class TunnelStalkerEliteTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _definition;
        private EnemyAttackDefinition _rush;
        private EnemyAttackDefinition _lunge;
        private EnemyAttackDefinition _claw;

        [SetUp]
        public void SetUp()
        {
            _rush = Attack("tunnel_stalker_tunnel_rush", AttackMotion.Dash, 24, 30, 3.5f, 9f, 0.9f, 1.4f, 1, 0f, 0.9f, 8f, 14f, 6f);
            _lunge = Attack("tunnel_stalker_lunge", AttackMotion.Dash, 14, 18, 1.8f, 3.5f, 0.5f, 0.6f, 1, 0f, 0.8f, 2.5f, 12f, 2.5f);
            _claw = Attack("tunnel_stalker_claw_combo", AttackMotion.Stationary, 8, 10, 0f, 1.8f, 0.35f, 0.7f, 3, 0.25f, 1.3f, 0f, 0f, 1.5f);

            _definition = ScriptableObject.CreateInstance<EliteDefinition>();
            _created.Add(_definition);
            Set(_definition, "_id", "elite_tunnel_stalker");
            Set(_definition, "_baseHealth", 350);
            Set(_definition, "_moveSpeed", 3.6f);
            Set(_definition, "_baseXp", 250);
            Set(_definition, "_strongestAttackDamageMin", 24);
            Set(_definition, "_strongestAttackDamageMax", 30);
            Set(_definition, "_moveset", new[] { _rush, _lunge, _claw });
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

        private EnemyAttackDefinition Attack(string id, AttackMotion motion, int dmin, int dmax, float minR, float maxR, float tele, float rec, int hits, float interval, float radius, float dashDist, float dashSpeed, float cooldown)
        {
            var a = ScriptableObject.CreateInstance<EnemyAttackDefinition>();
            _created.Add(a);
            Set(a, "_id", id);
            Set(a, "_displayName", id);
            Set(a, "_motion", motion);
            Set(a, "_damageMin", dmin);
            Set(a, "_damageMax", dmax);
            Set(a, "_minTriggerRange", minR);
            Set(a, "_maxTriggerRange", maxR);
            Set(a, "_telegraphSeconds", tele);
            Set(a, "_recoverySeconds", rec);
            Set(a, "_hitCount", hits);
            Set(a, "_hitIntervalSeconds", interval);
            Set(a, "_hitRadius", radius);
            Set(a, "_dashDistance", dashDist);
            Set(a, "_dashSpeed", dashSpeed);
            Set(a, "_cooldownSeconds", cooldown);
            return a;
        }

        private (EliteController elite, EliteEncounter encounter) SpawnStalker(Vector2 position, IDamageRoller roller = null)
        {
            var root = new GameObject("EliteEncounter");
            _created.Add(root);
            var encounter = root.AddComponent<EliteEncounter>();
            var go = new GameObject("TunnelStalker");
            go.transform.SetParent(root.transform, false);
            go.transform.position = position;
            go.AddComponent<CircleCollider2D>().radius = 0.5f;
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<HealthComponent>();
            var elite = go.AddComponent<EliteController>();
            elite.SetDamageRoller(roller ?? new UnityRandomDamageRoller());
            elite.SetDefinition(_definition);
            encounter.Bind(elite);
            return (elite, encounter);
        }

        private (GameObject go, HealthComponent health) SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);
            return (go, health);
        }

        [Test]
        public void Stalker_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var (elite, _) = SpawnStalker(Vector2.zero);
            Assert.AreEqual(350, elite.Health.MaxHealth);
            Assert.AreEqual(350, elite.Health.CurrentHealth);
            Assert.AreEqual(250, elite.XpValue);

            var (player, _) = SpawnPlayerDummy(new Vector2(6f, 0f));
            elite.SetTarget(player.transform);
            Assert.AreSame(_rush, elite.SelectAttack(), "Far target → Tunnel Rush.");
            player.transform.position = new Vector2(2.5f, 0f);
            Assert.AreSame(_lunge, elite.SelectAttack(), "Mid range → Lunge.");
            player.transform.position = new Vector2(1f, 0f);
            Assert.AreSame(_claw, elite.SelectAttack(), "Close → Claw Combo.");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack(), "Out of every range → keep chasing.");
        }

        [UnityTest]
        public IEnumerator ClawCombo_TelegraphsThenLandsThreeHits_EachThroughDamageBoundary()
        {
            var (elite, _) = SpawnStalker(Vector2.zero, new FixedDamageRoller { FixedValue = 9 });
            var (player, health) = SpawnPlayerDummy(new Vector2(1f, 0f));
            var telegraphs = new List<EnemyAttackDefinition>();
            var resolved = new List<EnemyAttackDefinition>();
            elite.AttackTelegraphStarted += (_, a) => telegraphs.Add(a);
            elite.AttackResolved += (_, a) => resolved.Add(a);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreSame(_claw, elite.CurrentAttack);
            Assert.AreEqual(500, health.CurrentHealth, "No damage during the telegraph.");
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(500, health.CurrentHealth, "Still telegraphing at 0.2s of 0.35s.");

            yield return new WaitForSeconds(0.9f);
            Assert.AreEqual(500 - 3 * 9, health.CurrentHealth, "Three claw hits of the fixed roll.");
            CollectionAssert.AreEqual(new[] { _claw }, telegraphs);
            CollectionAssert.AreEqual(new[] { _claw }, resolved);
            Assert.AreEqual(MovesetActorState.Recovery, elite.State);
        }

        [UnityTest]
        public IEnumerator TunnelRush_IsTelegraphed_DashesAlongLockedDirection_AndHitsOnceFor24To30()
        {
            var (elite, _) = SpawnStalker(Vector2.zero);
            var (player, health) = SpawnPlayerDummy(new Vector2(6f, 0f));
            var damages = new List<int>();
            health.Damaged += d => damages.Add(d);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreSame(_rush, elite.CurrentAttack);
            var startX = elite.transform.position.x;
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(startX, elite.transform.position.x, 0.05f, "Stalker holds still while telegraphing.");
            Assert.AreEqual(500, health.CurrentHealth);

            // Player side-steps after the telegraph locked the direction: the rush keeps its line.
            player.transform.position = new Vector2(6f, 4f);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(MovesetActorState.Attacking, elite.State);
            Assert.Less(Mathf.Abs(elite.transform.position.y), 0.2f, "Dash follows the locked direction, not the moved target.");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(500, health.CurrentHealth, "Dodged rush deals nothing.");
            Assert.AreEqual(MovesetActorState.Recovery, elite.State);
            Assert.Greater(elite.transform.position.x, 6f, "Rush travelled its full distance.");

            // Second encounter: stand in the line and take exactly one 24–30 hit.
            var (elite2, _) = SpawnStalker(new Vector2(0f, 20f));
            var (player2, health2) = SpawnPlayerDummy(new Vector2(6f, 20f));
            var hits2 = new List<int>();
            health2.Damaged += d => hits2.Add(d);
            elite2.SetTarget(player2.transform);
            yield return new WaitForSeconds(2.2f);

            Assert.AreEqual(1, hits2.Count, "One rush = one hit on the target.");
            Assert.That(hits2[0], Is.InRange(24, 30));
        }

        [UnityTest]
        public IEnumerator Elite_DiesOnce_AwardsXp250_AndSignalsEncounterStartAndCompletion()
        {
            var (elite, encounter) = SpawnStalker(Vector2.zero);
            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            var started = 0;
            var completed = new List<int>();
            var died = 0;
            encounter.Started += _ => started++;
            encounter.Completed += (_, xp) => completed.Add(xp);
            elite.Died += _ => died++;

            Assert.IsFalse(encounter.IsStarted);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.IsTrue(encounter.IsStarted);
            Assert.AreEqual(1, started);

            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(349)));
            Assert.IsTrue(elite.IsAlive);
            Assert.IsFalse(encounter.IsCompleted);
            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(1)));
            Assert.IsFalse(elite.IsAlive);
            Assert.AreEqual(MovesetActorState.Dead, elite.State);
            Assert.AreEqual(1, died);
            CollectionAssert.AreEqual(new[] { 250 }, completed);
            Assert.AreEqual(250, encounter.XpAwarded);

            Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(50)), "Dead elites take no further damage.");
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, died);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(MovesetActorState.Dead, elite.State, "No respawn, no phase.");
        }

        [Test]
        public void Encounter_HoldsExactlyOneEliteActor()
        {
            var (elite, encounter) = SpawnStalker(Vector2.zero);
            var (other, _) = SpawnStalker(new Vector2(5f, 0f));
            Assert.AreSame(elite, encounter.Elite);
            Assert.Throws<System.InvalidOperationException>(() => encounter.Bind(other));
        }

        [UnityTest]
        public IEnumerator DamageDuringAttack_DoesNotStopIt_ButDeathCancelsCleanly()
        {
            var (elite, _) = SpawnStalker(Vector2.zero);
            var (player, health) = SpawnPlayerDummy(new Vector2(1f, 0f));
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);

            elite.Health.TryApplyDamage(new DamageRequest(100));
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State, "Stagger interruption is TASK 050; plain damage never cancels the fixed moveset.");

            elite.Health.TryApplyDamage(new DamageRequest(250));
            Assert.AreEqual(MovesetActorState.Dead, elite.State);
            Assert.IsNull(elite.CurrentAttack);
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(500, health.CurrentHealth, "A dead stalker's pending attack never lands.");
        }
    }
}
