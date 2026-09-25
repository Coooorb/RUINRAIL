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
    /// TASK 115: Crusher Unit (475 HP, 26–32 strongest, 2.5 speed, XP 325) — range-based selection, rectangular
    /// Hydraulic Slam telegraph → one hit inside the zone only, dies once with XP once, seeded Rustworks pick across
    /// both Rustworks Elites.
    /// </summary>
    public class CrusherUnitEliteTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _crusher;

        [SetUp]
        public void SetUp()
        {
            _crusher = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_CrusherUnit.asset");
            Assert.IsNotNull(_crusher);
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
            var encounter = new DefaultEliteSpawner().Spawn(_crusher, position, null, null);
            _created.Add(encounter.gameObject);
            if (roller != null) encounter.Elite.SetDamageRoller(roller);
            return encounter;
        }

        [Test]
        public void Crusher_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var elite = Spawn(Vector2.zero).Elite;
            Assert.AreEqual(475, elite.Health.MaxHealth);
            Assert.AreEqual(325, elite.XpValue);
            Assert.AreEqual(2.5f, elite.Definition.MoveSpeed, 0.001f);

            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            elite.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 1.5f, "Hydraulic Slam", "Close: the rectangular slam.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 2.8f, "Crusher Charge", "Mid range: charge first in the moveset order.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 8.5f, "Scrap Barrage", "Beyond charge range: the barrage.");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack());
        }

        [UnityTest]
        public IEnumerator HydraulicSlam_TelegraphsThenHitsOnce_OnlyInsideTheRectangle()
        {
            var elite = Spawn(Vector2.zero, new FixedDamageRoller { FixedValue = 24 }).Elite;
            var (player, health) = SpawnPlayerDummy(new Vector2(1.8f, 0f));
            var (bystander, bystanderHealth) = SpawnPlayerDummy(new Vector2(1.8f, 4f));
            var hits = new List<int>();
            health.Damaged += d => hits.Add(d);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;

            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual("Hydraulic Slam", elite.CurrentAttack.DisplayName);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(500, health.CurrentHealth, "No damage during the 1.0 s telegraph.");
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(1, hits.Count, "One slam = one hit.");
            Assert.AreEqual(24, hits[0]);
            Assert.AreEqual(500, bystanderHealth.CurrentHealth, "Outside the rectangle: untouched.");
        }

        [UnityTest]
        public IEnumerator Crusher_DiesOnce_AwardsXp325_AndTheEncounterCompletesOnce()
        {
            var encounter = Spawn(Vector2.zero);
            var elite = encounter.Elite;
            var (player, _) = SpawnPlayerDummy(new Vector2(12f, 0f));
            var completed = new List<int>();
            encounter.Completed += (_, xp) => completed.Add(xp);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.IsTrue(encounter.IsStarted);

            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(474)));
            Assert.IsTrue(elite.IsAlive);
            Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(1)));
            Assert.IsFalse(elite.IsAlive);
            CollectionAssert.AreEqual(new[] { 325 }, completed);
            Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(50)));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(1, completed.Count);
            Assert.AreEqual(MovesetActorState.Dead, elite.State);
        }

        [Test]
        public void RustworksElitePick_IsSeededPerRoom_AmongBothRustworksElites()
        {
            var executioner = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset");
            var railguard = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_Railguard.asset");
            var elites = new[] { railguard, executioner, _crusher };
            var a = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var b = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, elites, new DefaultEliteSpawner());
            var picks = Enumerable.Range(0, 40).Select(i => a.PickElite(Biome.Rustworks, i).Id).ToList();
            CollectionAssert.AreEqual(picks, Enumerable.Range(0, 40).Select(i => b.PickElite(Biome.Rustworks, i).Id).ToList());
            CollectionAssert.Contains(picks, "elite_scrap_executioner");
            CollectionAssert.Contains(picks, "elite_crusher_unit");
            Assert.IsFalse(picks.Contains("elite_railguard"), "Never a Metro Elite in Rustworks.");
        }
    }
}
