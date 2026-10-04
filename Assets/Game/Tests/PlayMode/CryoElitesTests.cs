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
    /// <summary>Cryo Vaults Elites on the shared Elite framework: Cryo Enforcer (heavy: cleave, frost slam, charge) and Vault Stalker (mobile: frost fan, shard burst, vault dash) — stats, range picks, the telegraphed dash, one resolution of death/XP, Cryo-only seeded pick.</summary>
    public class CryoElitesTests
    {
        private readonly List<Object> _created = new();
        private EliteDefinition _enforcer;
        private EliteDefinition _stalker;

        [SetUp]
        public void SetUp()
        {
            _enforcer = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_CryoEnforcer.asset");
            _stalker = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_VaultStalker.asset");
            Assert.IsNotNull(_enforcer);
            Assert.IsNotNull(_stalker);
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
        public void Enforcer_ExposesItsStats_AndSelectsAttacksByRange()
        {
            var elite = Spawn(_enforcer, Vector2.zero).Elite;
            Assert.AreEqual(500, elite.Health.MaxHealth);
            Assert.AreEqual(325, elite.XpValue);
            Assert.AreEqual(Biome.CryoVaults, _enforcer.Biome);
            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            elite.SetTarget(player.transform);
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 1.5f, "Broad Cleave", "Close: the broad cleave.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 1.5f, "Frost Slam", "Close: the frost slam.");
            BossSelectionAssert.OnlySelectableAt(elite, player.transform, Vector2.zero, 2.4f, "Frost Slam", "Just past the cleave: only the slam reaches.");
            BossSelectionAssert.OnlySelectableAt(elite, player.transform, Vector2.zero, 5f, "Enforcer Charge", "Mid: the telegraphed charge.");
            player.transform.position = new Vector2(30f, 0f);
            Assert.IsNull(elite.SelectAttack());
        }

        [Test]
        public void Stalker_ExposesItsStats_AndSelectsAttacksByRange()
        {
            var elite = Spawn(_stalker, Vector2.zero).Elite;
            Assert.AreEqual(375, elite.Health.MaxHealth);
            Assert.AreEqual(300, elite.XpValue);
            Assert.AreEqual(3.5f, elite.Definition.MoveSpeed, 0.001f);
            var (player, _) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            elite.SetTarget(player.transform);
            BossSelectionAssert.OnlySelectableAt(elite, player.transform, Vector2.zero, 1.5f, "Vault Dash", "Close: it dashes through and past the player (its reposition).");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 5f, "Frost Fan", "Mid: the fan.");
            BossSelectionAssert.CanSelectAt(elite, player.transform, Vector2.zero, 5f, "Shard Burst", "Mid: the burst.");
            BossSelectionAssert.OnlySelectableAt(elite, player.transform, Vector2.zero, 10.5f, "Shard Burst", "Far: only the burst reaches.");
        }

        [UnityTest]
        public IEnumerator VaultDash_Telegraphs_ThenCarriesTheStalkerPastThePlayer()
        {
            var elite = Spawn(_stalker, Vector2.zero, new FixedDamageRoller { FixedValue = 20 }).Elite;
            var (player, health) = SpawnPlayerDummy(new Vector2(1.5f, 0f));
            // A real player body: enemy bodies pass player bodies (layer rule), so the dash can carry through.
            CombatLayers.Apply();
            CombatLayers.TagPlayerBody(player);
            var hits = new List<int>();
            health.Damaged += d => hits.Add(d);
            elite.SetTarget(player.transform);
            yield return null;
            yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual("Vault Dash", elite.CurrentAttack.DisplayName);
            yield return new WaitForSeconds(0.4f);
            Assert.AreEqual(0, hits.Count, "Nothing during the 0.6 s telegraph.");
            var deadline = Time.time + 1.5f;
            while (Time.time < deadline && elite.transform.position.x < 2.5f) yield return null;
            Assert.Greater(elite.transform.position.x, 2.5f, "The dash repositions the Stalker beyond the player.");
            Assert.AreEqual(1, hits.Count, "The dash hits once on the way through.");
            Assert.AreEqual(20, hits[0]);
        }

        [UnityTest]
        public IEnumerator FrostFan_FiresPooledFrostProjectiles()
        {
            var encounter = Spawn(_stalker, Vector2.zero, new FixedDamageRoller { FixedValue = 9 });
            var elite = encounter.Elite;
            var (player, _) = SpawnPlayerDummy(new Vector2(8f, 0f));
            var telegraphs = new List<string>();
            elite.AttackTelegraphStarted += (_, a) => telegraphs.Add(a.DisplayName);
            elite.SetTarget(player.transform);
            var guard = 0;
            while (!telegraphs.Contains("Frost Fan") && guard++ < 120) yield return new WaitForSeconds(0.1f);
            Assert.Contains("Frost Fan", telegraphs);
            yield return new WaitForSeconds(1.0f);
            Assert.IsNotNull(elite.GetComponent<ProjectilePool>());
            var fired = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(fired.Length, 1, "Pooled projectiles are in flight or were fired.");
            Assert.AreEqual("proj_enemy_frost", _stalker.Moveset.First(a => a.DisplayName == "Frost Fan").ProjectileVisualId, "The fan fires the Cryo ice splinter.");
        }

        [UnityTest]
        public IEnumerator BothCryoElites_DieOnce_AwardXpOnce_AndCannotBeHitByClients()
        {
            foreach (var (definition, xpExpected) in new[] { (_enforcer, 325), (_stalker, 300) })
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
        public void CryoElitePick_IsSeededPerRoom_AmongBothCryoElites_NeverOtherBiomes()
        {
            var all = AssetDatabase.FindAssets("t:EliteDefinition").Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(e => e != null).ToList();
            var a = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, all, new DefaultEliteSpawner());
            var b = new DungeonRuntimeContext(5, 3, 1, System.Array.Empty<EnemyDefinition>(), new DefaultEnemySpawner(), null, all, new DefaultEliteSpawner());
            var picks = Enumerable.Range(0, 40).Select(i => a.PickElite(Biome.CryoVaults, i).Id).ToList();
            CollectionAssert.AreEqual(picks, Enumerable.Range(0, 40).Select(i => b.PickElite(Biome.CryoVaults, i).Id).ToList());
            CollectionAssert.AreEquivalent(new[] { "elite_cryo_enforcer", "elite_vault_stalker" }, picks.Distinct());
        }
    }
}
