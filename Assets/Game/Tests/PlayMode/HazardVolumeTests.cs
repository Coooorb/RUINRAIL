using System.Linq;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Hazards;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 051 — authored hazard volumes and damage tags in the scene.</summary>
    public class HazardVolumeTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private HazardDefinition Def(DamageKind kind, int min, int max, float interval, float delay = 0f, bool players = true, bool enemies = true, float knockback = 0f)
        {
            var d = HazardDefinition.Create("test_hazard", kind, min, max, interval, delay, players, enemies, knockback);
            _created.Add(d);
            return d;
        }

        private HazardVolume Hazard(HazardDefinition definition, Vector2 position, Vector2 size)
        {
            var go = new GameObject("Hazard");
            _created.Add(go);
            go.transform.position = position;
            var box = go.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = size;
            var volume = go.AddComponent<HazardVolume>();
            volume.SetDefinition(definition);
            volume.SetDamageRoller(new FixedDamageRoller { FixedValue = 6 });
            return volume;
        }

        private (GameObject go, HealthComponent health) Actor(string name, Vector2 position, DamageTeam team, int extraColliders = 0)
        {
            var go = new GameObject(name);
            _created.Add(go);
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            go.AddComponent<CircleCollider2D>().radius = 0.3f;
            for (var i = 0; i < extraColliders; i++)
            {
                var child = new GameObject($"Hurtbox{i}");
                child.transform.SetParent(go.transform, false);
                child.AddComponent<CircleCollider2D>().radius = 0.2f;
            }

            go.AddComponent<TeamMember>().SetTeam(team);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(100);
            return (go, health);
        }

        private static IEnumerator FixedSeconds(float seconds)
        {
            var steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (var i = 0; i < steps; i++) yield return new WaitForFixedUpdate();
        }

        // ---- Acceptance 1 + 3: players are hurt at the authored interval, exactly once per tick, friendly fire irrelevant ----

        [UnityTest]
        public IEnumerator PlayerInsideHazard_TakesDamageAtTheInterval_OncePerTickDespiteSeveralColliders()
        {
            var hazard = Hazard(Def(DamageKind.Hazard, 5, 8, 0.5f), Vector2.zero, new Vector2(2f, 2f));
            var (player, health) = Actor("Player", Vector2.zero, DamageTeam.Player, extraColliders: 2);
            var ticks = new List<int>();
            hazard.Ticked += (_, dmg) => ticks.Add(dmg);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, hazard.OccupantCount, "Three colliders, one occupant.");
            Assert.AreEqual(1, ticks.Count, "Immediate first tick (no initial delay).");
            Assert.AreEqual(94, health.CurrentHealth);

            yield return FixedSeconds(0.5f);
            Assert.AreEqual(2, ticks.Count, "Second tick after exactly the interval.");
            yield return FixedSeconds(1.0f);
            Assert.AreEqual(4, ticks.Count);
            Assert.AreEqual(76, health.CurrentHealth);
            Assert.AreEqual(DamageKind.Hazard, hazard.Definition.Kind);

            // Leaving stops the ticks; re-entering restarts the timer from the initial delay.
            player.transform.position = new Vector2(10f, 0f);
            yield return FixedSeconds(1.0f);
            Assert.AreEqual(0, hazard.OccupantCount);
            Assert.AreEqual(4, ticks.Count);
            Assert.IsTrue(health.IsAlive);
        }

        /// <summary>
        /// Only an actor's own body in the hazard is an occupant. The player's bullets flown across the hazard, a trigger
        /// parented under the player reaching into it, an enemy's own bullets crossing it and a loose dropped-item body
        /// lying in it cause no damage to anyone; the player walking in is ticked as before and walking out stops it.
        /// </summary>
        [UnityTest]
        public IEnumerator ProjectilesAndOtherNonBodies_CrossingAHazard_NeverDamageTheirOwner_TheBodyStillDoes()
        {
            var hazard = Hazard(Def(DamageKind.Hazard, 5, 8, 0.5f), Vector2.zero, new Vector2(3f, 3f));
            var ticks = 0;
            hazard.Ticked += (_, _) => ticks++;
            var (player, playerHealth) = Actor("Player", new Vector2(-6f, 0f), DamageTeam.Player);
            var (enemy, enemyHealth) = Actor("Enemy", new Vector2(6f, 0f), DamageTeam.Enemy);
            var playerPool = player.AddComponent<RuinRail.Gameplay.Combat.Projectiles.ProjectilePool>();
            var enemyPool = enemy.AddComponent<RuinRail.Gameplay.Combat.Projectiles.ProjectilePool>();
            yield return new WaitForFixedUpdate();

            // 1. Both sides fire straight across the hazard (the shots travel the whole width, nothing to hit).
            var crossed = false;
            for (var volley = 0; volley < 3; volley++)
            {
                playerPool.Spawn(new Vector2(-4f, 0.2f), new RuinRail.Gameplay.Combat.Projectiles.ProjectileSpawnData(5, 16f, 5f, 0f, 0f, Vector2.right, player, null, 0f, DamageTeam.Player));
                enemyPool.Spawn(new Vector2(4f, -0.2f), new RuinRail.Gameplay.Combat.Projectiles.ProjectileSpawnData(5, 16f, 5f, 0f, 0f, Vector2.left, enemy, null, 0f, DamageTeam.Enemy));
                for (var i = 0; i < 10; i++)
                {
                    yield return new WaitForFixedUpdate();
                    crossed |= player.GetComponentsInChildren<RuinRail.Gameplay.Combat.Projectiles.Projectile>(false).Any(p => Mathf.Abs(p.transform.position.x) < 1.2f);
                }
            }

            yield return FixedSeconds(0.6f); // every shot has run its range out
            Assert.IsTrue(crossed, "a player bullet really flew through the hazard");
            Assert.AreEqual(0, ticks, "no hazard tick from bullets");
            Assert.AreEqual(100, playerHealth.CurrentHealth, "the player's own bullets never proxy hazard damage onto the player");
            Assert.AreEqual(100, enemyHealth.CurrentHealth, "nor an enemy's onto the enemy");

            // 2. A trigger parented under the player (a hurtbox, a pooled projectile) reaching into the hazard.
            var child = new GameObject("PlayerChildTrigger");
            child.transform.SetParent(player.transform, false);
            var childCollider = child.AddComponent<BoxCollider2D>();
            childCollider.isTrigger = true;
            child.transform.position = Vector2.zero;
            yield return FixedSeconds(0.6f);
            Assert.AreEqual(0, ticks, "a player-parented trigger is not the player's body");
            Assert.AreEqual(100, playerHealth.CurrentHealth, "no damage from it");
            Object.DestroyImmediate(child);

            // 3. A loose dropped-item body in the hazard: not damageable, nothing enrolled.
            var drop = new GameObject("LooseDrop");
            _created.Add(drop);
            var dropBody = drop.AddComponent<Rigidbody2D>();
            dropBody.gravityScale = 0f;
            drop.AddComponent<CircleCollider2D>().radius = 0.25f;
            yield return FixedSeconds(0.3f);
            Assert.AreEqual(0, hazard.OccupantCount, "no occupant from bullets, triggers or loose bodies");
            Assert.AreEqual(0, ticks, "no tick at all");

            // 4. The player's body walking in is ticked at the authored cadence; walking out stops it.
            player.GetComponent<Rigidbody2D>().position = Vector2.zero;
            player.transform.position = Vector2.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(1, hazard.OccupantCount);
            Assert.AreEqual(94, playerHealth.CurrentHealth, "immediate first tick on entry");
            yield return FixedSeconds(0.5f);
            Assert.AreEqual(88, playerHealth.CurrentHealth, "second tick after the interval");
            player.GetComponent<Rigidbody2D>().position = new Vector2(-6f, 0f);
            player.transform.position = new Vector2(-6f, 0f);
            yield return FixedSeconds(1.0f);
            Assert.AreEqual(0, hazard.OccupantCount, "leaving ends the occupancy");
            Assert.AreEqual(88, playerHealth.CurrentHealth, "and further damage");
        }

        [UnityTest]
        public IEnumerator Hazard_HurtsEnemiesToo_SkipsNeutrals_AndRespectsAuthoredTargetFlags()
        {
            var both = Hazard(Def(DamageKind.Hazard, 5, 8, 0.25f), Vector2.zero, new Vector2(3f, 3f));
            var (_, playerHealth) = Actor("Player", new Vector2(-0.8f, 0f), DamageTeam.Player);
            var (_, enemyHealth) = Actor("Enemy", new Vector2(0.8f, 0f), DamageTeam.Enemy);
            var (_, neutralHealth) = Actor("Prop", new Vector2(0f, 0.8f), DamageTeam.Neutral);
            yield return FixedSeconds(0.3f);
            Assert.Less(playerHealth.CurrentHealth, 100, "Friendly fire OFF does not protect players from the environment.");
            Assert.Less(enemyHealth.CurrentHealth, 100);
            Assert.AreEqual(100, neutralHealth.CurrentHealth);
            Assert.AreEqual(2, both.OccupantCount);

            var enemyOnly = Hazard(Def(DamageKind.Hazard, 5, 8, 0.25f, 0f, false), new Vector2(20f, 0f), new Vector2(3f, 3f));
            var (_, p2) = Actor("Player2", new Vector2(19.5f, 0f), DamageTeam.Player);
            var (_, e2) = Actor("Enemy2", new Vector2(20.5f, 0f), DamageTeam.Enemy);
            yield return FixedSeconds(0.3f);
            Assert.AreEqual(100, p2.CurrentHealth, "Authored enemy-only trap.");
            Assert.Less(e2.CurrentHealth, 100);
            Assert.AreEqual(1, enemyOnly.OccupantCount);
        }

        [UnityTest]
        public IEnumerator InitialDelay_HoldsTheFirstTick_Deterministically()
        {
            var hazard = Hazard(Def(DamageKind.Hazard, 5, 8, 1f, delay: 0.4f), Vector2.zero, new Vector2(2f, 2f));
            var (_, health) = Actor("Player", Vector2.zero, DamageTeam.Player);
            yield return FixedSeconds(0.3f);
            Assert.AreEqual(100, health.CurrentHealth, "Still inside the grace window.");
            yield return FixedSeconds(0.15f);
            Assert.AreEqual(94, health.CurrentHealth);
            Assert.AreEqual(1, hazard.TicksApplied);
        }

        // ---- Acceptance 2: explosion tag -> general DR, then Blast Suit's 30% on the remainder; Shock Absorber knockback ----

        [UnityTest]
        public IEnumerator ExplosionHazard_AppliesGeneralDrThenBlastSuitReduction_AndShockAbsorberIgnoresItsKnockback()
        {
            var caps = ScriptableObject.CreateInstance<GlobalStatCapsConfig>();
            _created.Add(caps);
            var hazard = Hazard(Def(DamageKind.Explosion, 100, 100, 5f, knockback: 8f), Vector2.zero, new Vector2(2f, 2f));
            hazard.SetDamageRoller(new FixedDamageRoller { FixedValue = 100 });

            var (player, health) = Actor("Player", new Vector2(0.3f, 0f), DamageTeam.Player);
            health.SetMaxHealth(500);
            var stats = new PlayerStats(caps, 500);
            // 16_GLOBAL_STAT_CAPS example: 40% general DR, then a separate 30% explosion reduction -> 42 of 100.
            stats.SetSource(new StatModifierSource("test_armor", StatModifier.Percent(StatId.GeneralDamageReduction, 40), StatModifier.Percent(StatId.ExplosionDamageReduction, 30)));
            health.SetIncomingDamageModifier(new StatsDamageModifier(stats));
            var events = new PlayerCombatEvents();
            var impact = player.AddComponent<PlayerImpactReceiver>();
            var config = StaggerConfig.Create();
            _created.Add(config);
            impact.SetConfig(config);
            impact.SetStats(stats);
            impact.SetEvents(events);
            events.ExplosionKnockbackIncoming += r => r.Negate("test_shock_absorber");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(458, health.CurrentHealth, "100 -> 60 after general DR -> 42 after the specialized 30%.");
            Assert.AreEqual(1, impact.KnockbacksNegated, "Explosion-tagged hazard knockback ignored by the Shock Absorber hook.");
            Assert.IsFalse(impact.IsKnockbackActive);
            Assert.AreEqual(0.3f, player.transform.position.x, 1e-3f);

            // The same request without the explosion tag would only see general DR.
            Assert.AreEqual(60, stats.ApplyDamageReduction(100, isExplosion: false));
            Assert.AreEqual(42, stats.ApplyDamageReduction(100, isExplosion: true));
        }

        private sealed class StatsDamageModifier : IIncomingDamageModifier
        {
            private readonly PlayerStats _stats;
            public StatsDamageModifier(PlayerStats stats) => _stats = stats;
            public int ModifyIncomingDamage(DamageRequest request) => _stats.ApplyDamageReduction(request.Amount, request.IsExplosion);
        }

        // ---- Acceptance 4: no elemental taxonomy ----

        [Test]
        public void DamageKinds_StayMinimal()
        {
            CollectionAssert.AreEquivalent(new[] { "Normal", "Explosion", "Burn", "Hazard" }, System.Enum.GetNames(typeof(DamageKind)));
            Assert.IsNull(typeof(HazardDefinition).GetProperty("Resistances"));
            Assert.IsTrue(new DamageRequest(1, DamageKind.Hazard).IsEnvironmental);
            Assert.IsFalse(new DamageRequest(1, DamageKind.Hazard).IsExplosion);
        }
    }
}
