using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 114: Scrap Executioner (500 HP, 28–34 strongest, 2.2 speed, XP 325) on the shared Elite framework — fixed
    /// range-based attack selection, telegraph then one hit, dies once with XP once, and the Rustworks Elite pick.
    /// </summary>
    public class ScrapExecutionerEliteTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _executioner;

        [SetUp]
        public void SetUp()
        {
            _executioner = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset");
            Assert.IsNotNull(_executioner);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            _created.Clear();
        }

        private (GameObject go, HealthComponent health) SpawnPlayerDummy(Vector2 position)
        {
            var go = new GameObject("PlayerDummy");
            _created.Add(go);
            go.transform.position = position;
            go.AddComponent<BoxCollider2D>().size = Vector2.one;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);
            return (go, health);
        }

        private EliteEncounter Spawn(Vector2 position, IDamageRoller roller = null)
        {
            var encounter = new DefaultEliteSpawner().Spawn(_executioner, position, null, null);
            _created.Add(encounter.gameObject);
            if (roller != null) encounter.Elite.SetDamageRoller(roller);
            return encounter;
        }

        [Test]
        public void Executioner_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var elite = Spawn(Vector2.zero).Elite;
            Assert.AreEqual(500, elite.Health.MaxHealth);
            Assert.AreEqual(325, elite.XpValue);
            Assert.AreEqual(2.2f, elite.Definition.MoveSpeed, 0.001f);

            var (player, _) = SpawnPlayerDummy(new Vector2(4.5f, 0f));
            elite.SetTarget(player.transform);
            Assert.AreEqual("Execution Charge", elite.SelectAttack().DisplayName, "Out of melee reach: the charge closes in.");
            player.transform.position = new Vector2(2.2f, 0f);
            Assert.AreEqual("Overhead Slam", elite.SelectAttack().DisplayName, "Just outside the cleave: the slam's radius reaches.");
            player.transform.position = new Vector2(1.2f, 0f);
            Assert.AreEqual("Heavy Cleave", elite.SelectAttack().DisplayName, "In melee reach: the cleave comes first.");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack(), "Out of every range: keep advancing.");
        }

        [UnityTest]
        public IEnumerator OverheadSlam_TelegraphsThenHitsOnce_InsideItsBand()
        {
            var elite = Spawn(Vector2.zero, new FixedDamageRoller { FixedValue = 27 }).Elite;
            var (player, health) = SpawnPlayerDummy(new Vector2(2.2f, 0f));
            var telegraphs = new List<string>();
            elite.AttackTelegraphStarted += (_, a) => telegraphs.Add(a.DisplayName);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual("Overhead Slam", elite.CurrentAttack.DisplayName);
            Assert.AreEqual(500, health.CurrentHealth, "Nothing lands during the telegraph.");
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(500, health.CurrentHealth, "Still telegraphing at 0.6 s of 1.1 s.");
            yield return new WaitForSeconds(0.8f);
            Assert.AreEqual(500 - 27, health.CurrentHealth, "One Overhead Slam hit of the fixed roll inside 26-30.");
            CollectionAssert.AreEqual(new[] { "Overhead Slam" }, telegraphs);
        }

        [UnityTest]
        public IEnumerator Executioner_DiesOnce_AwardsXp325_AndTheEncounterCompletesOnce()
        {
            var encounter = Spawn(Vector2.zero);
            var elite = encounter.Elite;
            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            var completed = new List<int>();
            var died = 0;
            encounter.Completed += (_, xp) => completed.Add(xp);
            elite.Died += _ => died++;
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.IsTrue(encounter.IsStarted);

            DamageAuthority.LocalIsAuthoritative = false;
            Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(100)), "A client never damages the Elite.");
            DamageAuthority.LocalIsAuthoritative = true;
            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(499)));
            Assert.IsTrue(elite.IsAlive);
            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(1)));
            Assert.IsFalse(elite.IsAlive);
            Assert.AreEqual(1, died);
            CollectionAssert.AreEqual(new[] { 325 }, completed);
            Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(50)));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(MovesetActorState.Dead, elite.State, "No phases, no respawn.");
        }

        [Test]
        public void RustworksElitePick_IsSeededPerRoom_AndNeverPicksAMetroElite()
        {
            var railguard = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_Railguard.asset");
            var elites = new[] { railguard, _executioner };
            var a = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var b = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var picks = Enumerable.Range(0, 20).Select(i => a.PickElite(Biome.Rustworks, i).Id).ToList();
            CollectionAssert.AreEqual(picks, Enumerable.Range(0, 20).Select(i => b.PickElite(Biome.Rustworks, i).Id).ToList());
            Assert.IsTrue(picks.All(id => id == "elite_scrap_executioner"), "Only Rustworks Elites fill Rustworks Elite rooms.");
            Assert.AreEqual("elite_railguard", a.PickElite(Biome.RuinedMetro, 0).Id);
        }
    }
}
