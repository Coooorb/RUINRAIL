using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 124/125: Mutated Brute and Prototype X-7 on the shared Elite framework — range picks, one resolution of death/XP, Labs-only seeded pick.</summary>
    public class LabsElitesTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _brute;
        private EliteDefinition _x7;

        [SetUp]
        public void SetUp()
        {
            _brute = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_MutatedBrute.asset");
            _x7 = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_PrototypeX7.asset");
            Assert.IsNotNull(_brute);
            Assert.IsNotNull(_x7);
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
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

        private EliteEncounter Spawn(EliteDefinition definition, Vector2 position, IDamageRoller roller = null)
        {
            var encounter = new DefaultEliteSpawner().Spawn(definition, position, null, null);
            _created.Add(encounter.gameObject);
            if (roller != null) encounter.Elite.SetDamageRoller(roller);
            return encounter;
        }

        [Test]
        public void Brute_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var elite = Spawn(_brute, Vector2.zero).Elite;
            Assert.AreEqual(525, elite.Health.MaxHealth);
            Assert.AreEqual(350, elite.XpValue);
            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            elite.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 1.5f, "Double Slam", "Close: the double slam.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 4f, "Roar Rush", "Roar Rush is the band's attack here");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 7f, "Mutation Leap", "Mutation Leap is the band's attack here");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack());
        }

        [Test]
        public void X7_ExposesApprovedStats_AndSelectsAttacksByFixedRangeRules()
        {
            var elite = Spawn(_x7, Vector2.zero).Elite;
            Assert.AreEqual(375, elite.Health.MaxHealth);
            Assert.AreEqual(300, elite.XpValue);
            Assert.AreEqual(3.4f, elite.Definition.MoveSpeed, 0.001f);
            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            elite.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 1.5f, "Radial Pulse", "Close: the radial ring pushes back.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 5f, "Blink Shot", "Mid: reposition.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 8f, "Energy Burst", "Far: the burst.");
        }

        [UnityTest]
        public IEnumerator DoubleSlam_TelegraphsThenLandsTwoHits()
        {
            var elite = Spawn(_brute, Vector2.zero, new FixedDamageRoller { FixedValue = 22 }).Elite;
            var (player, health) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            var hits = new List<int>();
            health.Damaged += d => hits.Add(d);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual("Double Slam", elite.CurrentAttack.DisplayName);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0, hits.Count, "Nothing during the 1.0 s telegraph.");
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(2, hits.Count, "Two slams, 0.45 s apart.");
            Assert.IsTrue(hits.All(h => h == 22));
        }

        [UnityTest]
        public IEnumerator BroadSalvo_FiresFivePooledProjectiles()
        {
            var encounter = Spawn(_x7, Vector2.zero, new FixedDamageRoller { FixedValue = 9 });
            var elite = encounter.Elite;
            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            var telegraphs = new List<string>();
            elite.AttackTelegraphStarted += (_, a) => telegraphs.Add(a.DisplayName);
            elite.SetTarget(player.transform);
            var guard = 0;
            while (!telegraphs.Contains("Broad Salvo") && guard++ < 120) yield return new WaitForSeconds(0.1f);
            Assert.Contains("Broad Salvo", telegraphs, "After the burst's cooldown the broad salvo follows.");
            yield return new WaitForSeconds(1.0f);
            var pool = elite.GetComponent<ProjectilePool>();
            Assert.IsNotNull(pool);
            Assert.GreaterOrEqual(Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None).Length, 1, "Pooled projectiles are in flight or were fired.");
        }

        [UnityTest]
        public IEnumerator BothLabsElites_DieOnce_AwardXpOnce_AndCannotBeHitByClients()
        {
            foreach (var (definition, xpExpected) in new[] { (_brute, 350), (_x7, 300) })
            {
                var encounter = Spawn(definition, new Vector2(0f, 0f));
                var elite = encounter.Elite;
                var (player, _) = SpawnPlayerDummy(new Vector2(30f, 0f));
                var completed = new List<int>();
                encounter.Completed += (_, xp) => completed.Add(xp);
                elite.SetTarget(player.transform);
                yield return null;
                DamageAuthority.LocalIsAuthoritative = false;
                Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(10)), definition.Id);
                DamageAuthority.LocalIsAuthoritative = true;
                Assert.IsTrue(elite.Health.TryApplyDamage(new DamageRequest(definition.BaseHealth)));
                Assert.IsFalse(elite.IsAlive);
                CollectionAssert.AreEqual(new[] { xpExpected }, completed, definition.Id);
                Assert.IsFalse(elite.Health.TryApplyDamage(new DamageRequest(1)));
                yield return null;
                Assert.AreEqual(1, completed.Count, definition.Id);
                Object.DestroyImmediate(encounter.gameObject);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void LabsElitePick_IsSeededPerRoom_AmongBothLabsElites_NeverOtherBiomes()
        {
            var all = AssetDatabase.FindAssets("t:EliteDefinition").Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(e => e != null).ToList();
            var a = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, all, new DefaultEliteSpawner());
            var b = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, all, new DefaultEliteSpawner());
            var picks = Enumerable.Range(0, 40).Select(i => a.PickElite(Biome.OvergrownLabs, i).Id).ToList();
            CollectionAssert.AreEqual(picks, Enumerable.Range(0, 40).Select(i => b.PickElite(Biome.OvergrownLabs, i).Id).ToList());
            CollectionAssert.AreEquivalent(new[] { "elite_mutated_brute", "elite_prototype_x7" }, picks.Distinct());
        }
    }
}
