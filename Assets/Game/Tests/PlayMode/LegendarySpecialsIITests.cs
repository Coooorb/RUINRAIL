using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 060 — Concussion Blast, Rail Shot, Arrow Storm, Meteor Salvo from their authored assets.</summary>
    public class LegendarySpecialsIITests
    {
        private GameObject _playerObject;
        private ProjectilePool _pool;
        private LegendarySpecialRegistry _registry;
        private readonly List<Object> _created = new();

        [SetUp]
        public void SetUp()
        {
            _registry = new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition").Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))));
            _playerObject = new GameObject("TestPlayer");
            _created.Add(_playerObject);
            _playerObject.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var rb = _playerObject.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            _pool = _playerObject.AddComponent<ProjectilePool>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private SpecialContext Context(Vector2 aim) => new(_playerObject, _playerObject.GetComponent<Rigidbody2D>(), () => aim, () => _playerObject.transform.position, _pool, new UnityRandomDamageRoller());

        private (HealthComponent health, ImpactReceiver impact) Enemy(string id, Vector2 position, bool boss = false, int colliders = 1)
        {
            var go = new GameObject(id);
            _created.Add(go);
            go.transform.position = position;
            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            for (var i = 0; i < colliders; i++)
            {
                var host = i == 0 ? go : new GameObject($"Hurtbox{i}");
                if (i > 0) host.transform.SetParent(go.transform, false);
                host.AddComponent<CircleCollider2D>().radius = 0.3f + 0.1f * i;
            }

            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            var health = go.AddComponent<HealthComponent>();
            health.SetMaxHealth(1000);
            var impact = go.AddComponent<ImpactReceiver>();
            var config = StaggerConfig.Create();
            _created.Add(config);
            impact.SetConfig(config);
            impact.SetProfile(boss ? new ImpactProfile(id, 95, 100, false, true) : new ImpactProfile(id, 0, 0, true, false));
            return (health, impact);
        }

        private static IEnumerator FixedSteps(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForFixedUpdate();
        }

        [Test]
        public void SpecialAndWeaponData_MatchTheCatalog()
        {
            Assert.IsTrue(_registry.TryGet("concussion_blast", out var cone));
            Assert.AreEqual((SpecialKind.Cone, 10f, 50, 60), (cone.Kind, cone.CooldownSeconds, cone.DamageMin, cone.DamageMax));
            Assert.Greater(cone.Knockback, 8f, "Massive knockback (PROTOTYPE 12).");
            Assert.GreaterOrEqual(cone.StaggerPower, 10f, "Massive stagger (PROTOTYPE 20 = two thresholds).");
            Assert.IsTrue(_registry.TryGet("rail_shot", out var rail));
            Assert.AreEqual((SpecialKind.PiercingShot, 14f, 85, 95), (rail.Kind, rail.CooldownSeconds, rail.DamageMin, rail.DamageMax));
            Assert.Greater(rail.ProjectileSpeed, 34f, "Extremely fast: faster than the sniper's own 34.");
            Assert.IsTrue(_registry.TryGet("arrow_storm", out var storm));
            Assert.AreEqual((SpecialKind.Fan, 12f, 9, 14, 17), (storm.Kind, storm.CooldownSeconds, storm.Shots, storm.DamageMin, storm.DamageMax));
            Assert.IsTrue(_registry.TryGet("meteor_salvo", out var salvo));
            Assert.AreEqual((SpecialKind.ExplosionSalvo, 18f, 3, 45, 55), (salvo.Kind, salvo.CooldownSeconds, salvo.Shots, salvo.DamageMin, salvo.DamageMax));

            var crowdbreaker = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Crowdbreaker.asset");
            Assert.AreEqual((WeaponClass.Shotgun, 7, 5, 7, 1.5f, 6, 2.2f, 6f, AmmoType.Shells, "concussion_blast"), (crowdbreaker.WeaponClass, crowdbreaker.ProjectilesPerShot, crowdbreaker.DamageMin, crowdbreaker.DamageMax, crowdbreaker.FireRate, crowdbreaker.MagazineSize, crowdbreaker.ReloadTime, crowdbreaker.Range, crowdbreaker.AmmoType, crowdbreaker.LegendaryMechanicId));
            var farline = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Farline.asset");
            Assert.AreEqual((WeaponClass.Sniper, 48, 55, 0.9f, 5, 2.4f, 21f, 34f, AmmoType.Heavy, "rail_shot"), (farline.WeaponClass, farline.DamageMin, farline.DamageMax, farline.FireRate, farline.MagazineSize, farline.ReloadTime, farline.Range, farline.ProjectileSpeed, farline.AmmoType, farline.LegendaryMechanicId));
            var stormstring = AssetDatabase.LoadAssetAtPath<BowWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Stormstring.asset");
            Assert.AreEqual((WeaponClass.Bow, 12, 15, 30, 36, 0.8f, "arrow_storm", "pool_bow"), (stormstring.WeaponClass, stormstring.QuickDamageMin, stormstring.QuickDamageMax, stormstring.FullDrawDamageMin, stormstring.FullDrawDamageMax, stormstring.FullChargeSeconds, stormstring.LegendaryMechanicId, stormstring.AffixPool.Id));
            var sunbreaker = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/Sunbreaker.asset");
            Assert.AreEqual((WeaponClass.RocketLauncher, 70, 85, 0.5f, 1, 2.3f, 14f, 10f, AmmoType.Heavy, 4, true, "meteor_salvo"), (sunbreaker.WeaponClass, sunbreaker.DamageMin, sunbreaker.DamageMax, sunbreaker.FireRate, sunbreaker.MagazineSize, sunbreaker.ReloadTime, sunbreaker.Range, sunbreaker.ProjectileSpeed, sunbreaker.AmmoType, sunbreaker.AmmoCostPerShot, sunbreaker.IsExplosive, sunbreaker.LegendaryMechanicId));
        }

        // ---- Acceptance 2: Concussion Blast uses shared knockback/stagger; boss immune to displacement ----

        [UnityTest]
        public IEnumerator ConcussionBlast_DamagesTheCone_PushesAndStaggersNormals_NeverDisplacesBosses()
        {
            var (grunt, gruntImpact) = Enemy("grunt", new Vector2(1.5f, 0.3f));
            var (boss, bossImpact) = Enemy("boss", new Vector2(2f, -0.5f), boss: true);
            var (outside, _) = Enemy("outside", new Vector2(-1.5f, 0f));
            yield return null;

            var special = (ConeSpecial)_registry.CreateFor("concussion_blast");
            special.Begin(Context(Vector2.right));
            Assert.AreEqual(2, special.LastTargetsHit);
            Assert.IsTrue(grunt.CurrentHealth is >= 940 and <= 950, "50-60 once.");
            Assert.IsTrue(boss.CurrentHealth is >= 940 and <= 950, "Bosses still take the damage.");
            Assert.AreEqual(1000, outside.CurrentHealth);
            Assert.IsTrue(gruntImpact.IsKnockbackActive, "Shared knockback resolution.");
            Assert.AreEqual(1, gruntImpact.Meter.TriggerCount, "Massive stagger triggers at once.");
            Assert.IsFalse(bossImpact.IsKnockbackActive, "Boss displacement immunity.");
            Assert.AreEqual(0, bossImpact.Meter.TriggerCount, "95% stagger resistance: one blast cannot stagger a boss.");
            yield return FixedSteps(12);
            Assert.Greater(grunt.transform.position.x, 1.5f + 1.5f, "Pushed 12 x 0.25 = 3 units.");
            Assert.AreEqual(2f, boss.transform.position.x, 1e-3f);
        }

        // ---- Acceptance 3: Rail Shot never double-hits ----

        [UnityTest]
        public IEnumerator RailShot_PenetratesEveryEnemyExactlyOnce_EvenWithMultipleColliders()
        {
            var (a, _) = Enemy("a", new Vector2(3f, 0f), colliders: 3);
            var (b, _) = Enemy("b", new Vector2(3.6f, 0f));
            var (c, _) = Enemy("c", new Vector2(9f, 0.1f));
            var hits = new Dictionary<string, int>();
            foreach (var h in new[] { a, b, c }) h.Damaged += d => { hits[h.name] = hits.TryGetValue(h.name, out var n) ? n + 1 : 1; };

            var special = (PiercingShotSpecial)_registry.CreateFor("rail_shot");
            special.Begin(Context(Vector2.right));
            var shot = _pool.GetComponentsInChildren<Projectile>().First(p => p.gameObject.activeSelf);
            Assert.AreEqual(60f, shot.Data.Speed, "Extremely fast (PROTOTYPE 60): 1.2 units per physics step.");
            yield return FixedSteps(25);

            Assert.AreEqual(1, hits["a"], "Three colliders, one hit.");
            Assert.AreEqual(1, hits["b"]);
            Assert.AreEqual(1, hits["c"]);
            Assert.AreEqual(3, shot.PiercedTargets);
            Assert.IsTrue(a.CurrentHealth is >= 905 and <= 915);
            Assert.IsFalse(shot.gameObject.activeSelf, "Ended at range 21.");
        }

        // ---- Arrow Storm ----

        [Test]
        public void ArrowStorm_FiresNineArrowsOf14To17_InAFan()
        {
            var special = (FanSpecial)_registry.CreateFor("arrow_storm");
            special.Begin(Context(Vector2.up));
            var arrows = _pool.GetComponentsInChildren<Projectile>(true).Where(p => p.gameObject.activeSelf).ToList();
            Assert.AreEqual(9, arrows.Count);
            Assert.IsTrue(arrows.All(p => p.Data.Damage >= 14 && p.Data.Damage <= 17));
            var angles = arrows.Select(p => Mathf.Atan2(p.Data.Direction.y, p.Data.Direction.x) * Mathf.Rad2Deg).OrderBy(x => x).ToList();
            Assert.AreEqual(60f, angles.First(), 0.5f, "Fan centred on the aim (up = 90): 60..120.");
            Assert.AreEqual(120f, angles.Last(), 0.5f);
            Assert.AreEqual(12f, special.CooldownSeconds);
        }

        // ---- Acceptance 4: Meteor Salvo — exactly three explosions, no duplicate targets inside one explosion ----

        [UnityTest]
        public IEnumerator MeteorSalvo_ThreeExplosions_EachTargetOncePerExplosion()
        {
            var (multi, _) = Enemy("multi", new Vector2(4f, 0.5f), colliders: 3); // inside blasts 1 (3,0) and 2 (5,0)
            var (edge, _) = Enemy("edge", new Vector2(8.5f, 0f));                 // inside blast 3 (7,0) only
            var (far, _) = Enemy("far", new Vector2(12f, 0f));
            var ally = new GameObject("Ally");
            _created.Add(ally);
            ally.transform.position = new Vector2(3f, -0.5f);
            ally.AddComponent<CircleCollider2D>().isTrigger = true;
            ally.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var allyHealth = ally.AddComponent<HealthComponent>();
            allyHealth.SetMaxHealth(100);
            var multiHits = 0;
            multi.Damaged += _ => multiHits++;
            yield return null;

            var special = (ExplosionSalvoSpecial)_registry.CreateFor("meteor_salvo");
            special.Begin(Context(Vector2.right));
            Assert.AreEqual(3, special.LastCentres.Count, "Exactly three explosion resolutions.");
            CollectionAssert.AreEqual(new[] { new Vector2(3f, 0f), new Vector2(5f, 0f), new Vector2(7f, 0f) }, special.LastCentres);
            Assert.AreEqual(2, multiHits, "Once per explosion it stands in (two), never once per collider.");
            Assert.IsTrue(edge.CurrentHealth is >= 945 and <= 955, "One explosion, 45-55.");
            Assert.AreEqual(1000, far.CurrentHealth);
            Assert.AreEqual(100, allyHealth.CurrentHealth, "Friendly fire OFF inside the salvo.");
            Assert.AreEqual(18f, special.CooldownSeconds);
        }
    }
}
