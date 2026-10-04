using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The large orange square was the effect pool's white placeholder quad, tinted, sized to a normal enemy's whole
    /// attack range (a Shooter: 14×14 tiles). These tests pin the replacement: every effect kind the runtime spawns
    /// resolves to catalog VFX frames (never the placeholder), and a normal enemy's telegraph is the shape of the
    /// mechanic — a lane for a shot, a ring of reach for contact, a ring at the landing point for a lob, a lane for a charge.
    /// </summary>
    public sealed class TelegraphArtTests
    {
        private static readonly string[] RuntimeKinds =
        {
            "muzzle", "impact", "explosion", "melee", "stagger", "heal", "status", "loot_glow",
            TelegraphIndicator.KindStationary, TelegraphIndicator.KindDash, TelegraphIndicator.KindProjectile, TelegraphIndicator.KindZone, TelegraphIndicator.KindSlam
        };

        private readonly List<Object> _created = new();
        private GameContentCatalog _catalog;
        private EffectPool _pool;

        [SetUp]
        public void SetUp()
        {
            DamageAuthority.LocalIsAuthoritative = true;
            _catalog = GameContentCatalog.Load();
            var go = new GameObject("Effects");
            _created.Add(go);
            _pool = go.AddComponent<EffectPool>();
            _pool.Configure(32);
            _pool.SetSpriteResolver(_catalog.VfxFramesFor); // exactly what ExpeditionScene binds
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var e in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (e != null) Object.DestroyImmediate(e.gameObject);
            _created.Clear();
        }

        [Test]
        public void EveryRuntimeEffectKind_HasCatalogFrames_AndSpawnsThemInsteadOfThePlaceholder()
        {
            foreach (var kind in RuntimeKinds)
            {
                Assert.IsTrue(_pool.HasArtFor(kind), kind + " has final art bound");
                var effect = _pool.Spawn(kind, Vector2.zero, 1f, Color.white);
                Assert.IsNotNull(effect.Frames, kind);
                Assert.Greater(effect.Frames.Count, 0, kind);
                Assert.IsTrue(effect.Renderer.sprite != null && effect.Renderer.sprite.name.StartsWith("vfx_" + kind + "_"), kind + " draws its own frames, got " + (effect.Renderer.sprite != null ? effect.Renderer.sprite.name : "null"));
                Assert.AreNotSame(_pool.Placeholder, effect.Renderer.sprite, kind + " is not the placeholder quad");
                Assert.IsFalse(effect.Frames.Contains(_pool.Placeholder), kind + " has no placeholder frame");
                _pool.Return(effect);
            }
        }

        [Test]
        public void EffectFrames_AdvanceOverTheLifetime()
        {
            var effect = _pool.Spawn("explosion", Vector2.zero, 1f, Color.white);
            Assume.That(effect.Frames.Count, Is.GreaterThan(1), "explosion is animated");
            var first = effect.Renderer.sprite;
            effect.Tick(0.95f);
            Assert.AreNotSame(first, effect.Renderer.sprite, "later frames play as the effect ages");
            Assert.AreEqual(effect.Frames.Count - 1, effect.CurrentFrame);
        }

        [Test]
        public void ShooterTelegraph_IsALaneAlongTheShot_NotASquareOfTheAttackRange()
        {
            var (enemy, target) = EnemyWithTarget("shooter", new Vector2(5f, 0f));
            var shape = TelegraphIndicator.ShapeOf(enemy, enemy.transform.position);
            Assert.AreEqual(TelegraphIndicator.KindProjectile, shape.Kind);
            var radius = AttackFootprint.EnemyProjectileRadius;
            Assert.AreEqual(enemy.Definition.ProjectileRange + radius * 2f, shape.Size.x, 0.001f, "lane length is the projectile range plus the shot's own radius at each end (never shorter than the real path)");
            Assert.LessOrEqual(shape.Size.y, 1f, "a narrow lane");
            Assert.Less(shape.Size.x * shape.Size.y, enemy.Definition.AttackRange * enemy.Definition.AttackRange, "far smaller than the old AttackRange² square (" + enemy.Definition.AttackRange * 2f + "² tiles)");
            Assert.AreEqual(0f, shape.AngleDegrees, 0.5f, "along the direction to the target");
            Assert.AreEqual(enemy.Definition.ProjectileRange * 0.5f, shape.Centre.x, 0.01f, "centred half way down the lane");
        }

        [Test]
        public void GruntBomberCharger_TelegraphShapes_FollowTheirMechanics()
        {
            var (grunt, _) = EnemyWithTarget("grunt", new Vector2(0.8f, 0f));
            var contact = TelegraphIndicator.ShapeOf(grunt, grunt.transform.position);
            Assert.AreEqual(TelegraphIndicator.KindStationary, contact.Kind);
            Assert.AreEqual(Vector2.one * (grunt.Definition.AttackRange * 2f), contact.Size, "a ring of the contact reach around the body");
            Assert.AreEqual((Vector2)grunt.transform.position, contact.Centre);

            var (bomber, _) = EnemyWithTarget("bomber", new Vector2(4f, 0f), new Vector2(20f, 0f));
            var lob = TelegraphIndicator.ShapeOf(bomber, bomber.transform.position);
            Assert.AreEqual(TelegraphIndicator.KindSlam, lob.Kind);
            Assert.AreEqual(Vector2.one * (bomber.Definition.BombRadiusTiles * 2f), lob.Size, "the blast ring, not the throw range");
            Assert.Greater(lob.Centre.x, bomber.transform.position.x + 1f, "at the landing point, away from the bomber");

            var (charger, _) = EnemyWithTarget("charger", new Vector2(4f, 0f), new Vector2(40f, 0f));
            var dash = TelegraphIndicator.ShapeOf(charger, charger.transform.position);
            Assert.AreEqual(TelegraphIndicator.KindDash, dash.Kind);
            Assert.Greater(dash.Size.x, dash.Size.y, "a lane along the charge");
            Assert.AreEqual(TelegraphIndicator.ShapeFor(charger.Definition.ChargeAttack, Vector2.right).size, dash.Size, "the dash lane of the charge move (distance plus hit radius)");
            Assert.GreaterOrEqual(dash.Size.x, charger.Definition.ChargeAttack.DashDistance);
        }

        /// <summary>
        /// The Elite marker is the hit area, not a decoration: for every shipped Elite ground attack (cleave, charge,
        /// zone, slam) a small target just inside each edge of the drawn footprint is struck by the real resolver and one
        /// just outside is not. (Volleys are lanes of real projectiles; their fans are pinned elsewhere.)
        /// </summary>
        [UnityTest]
        public IEnumerator EveryShippedEliteGroundAttack_MarkerFootprint_IsTheResolversHitArea()
        {
            Assert.AreEqual(8, _catalog.Elites.Count, "the eight shipped Elites");
            var attacks = _catalog.Elites.SelectMany(e => e.Moveset).Where(a => a != null && a.Motion != AttackMotion.Projectile).Distinct().ToList();
            Assert.Greater(attacks.Count, 10);
            var origin = new Vector2(800f, 800f);
            const float Edge = 0.12f;
            foreach (var attack in attacks)
            {
                var (size, offset) = TelegraphIndicator.ShapeFor(attack, Vector2.right);
                var centre = origin + offset;
                // A dash strikes once per physics step along its run, so its far end is honest to within one step.
                var step = attack.Motion == AttackMotion.Dash ? attack.DashSpeed * Time.fixedDeltaTime : 0f;
                var probes = new List<(Vector2 point, bool inside, string edge)>
                {
                    (centre + Vector2.right * (size.x * 0.5f - Edge - step), true, "front, inside"),
                    (centre + Vector2.right * (size.x * 0.5f + Edge), false, "front, outside"),
                    (centre - Vector2.right * (size.x * 0.5f - Edge), true, "back, inside"),
                    (centre - Vector2.right * (size.x * 0.5f + Edge), false, "back, outside"),
                    (centre + Vector2.up * (size.y * 0.5f - Edge), true, "side, inside"),
                    (centre + Vector2.up * (size.y * 0.5f + Edge), false, "side, outside"),
                };
                foreach (var (point, inside, edge) in probes)
                {
                    var attacker = new GameObject("Attacker");
                    _created.Add(attacker);
                    attacker.transform.position = origin;
                    attacker.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                    attacker.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
                    var target = new GameObject("Probe");
                    _created.Add(target);
                    target.transform.position = point;
                    var circle = target.AddComponent<CircleCollider2D>();
                    circle.radius = 0.02f;
                    circle.isTrigger = true;
                    target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
                    var probe = target.AddComponent<TestDamageableTarget>();
                    Physics2D.SyncTransforms();
                    var resolver = new AttackResolver(attacker.transform, attacker.GetComponent<Rigidbody2D>(), new FixedDamageRoller());
                    resolver.Begin(attack, Vector2.right);
                    for (var guard = 0; guard < 400 && resolver.IsRunning; guard++)
                    {
                        resolver.Tick(Time.fixedDeltaTime);
                        yield return new WaitForFixedUpdate();
                    }

                    Assert.IsFalse(resolver.IsRunning, attack.name + " resolved");
                    Assert.AreEqual(inside, probe.HitCount > 0, $"{attack.name} ({attack.Motion}) {edge}: marker {size} at {offset} — the probe at {point - origin} is {(inside ? "inside" : "outside")} the marker, so it must {(inside ? "" : "not ")}be struck");
                    Object.DestroyImmediate(attacker);
                    Object.DestroyImmediate(target);
                }
            }
        }

        [UnityTest]
        public IEnumerator LiveShooterTelegraph_DrawsCatalogArtAtTheLaneFootprint()
        {
            var (enemy, _) = EnemyWithTarget("shooter", new Vector2(5f, 0f));
            enemy.enabled = true;
            var indicator = enemy.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_catalog.Feedback, _pool, enemy, null);
            var deadline = Time.time + 4f;
            while (Time.time < deadline && enemy.State != EnemyState.Telegraph) yield return null;
            Assert.AreEqual(EnemyState.Telegraph, enemy.State, "the shooter telegraphs its shot");
            yield return null;
            Assert.IsTrue(indicator.IsShowing);
            Assert.AreEqual(TelegraphIndicator.KindProjectile, indicator.MarkerKind);

            var view = indicator.View;
            Assert.IsNotNull(view, "the lane marker is painted");
            Assert.IsTrue(view.IsVisible, "and shown");
            Assert.AreEqual(Quaternion.identity, view.transform.rotation, "painted on the world pixel grid, never rotated");
            Assert.AreEqual(Vector3.one, view.transform.localScale, "or stretched");
            var rect = view.WorldRect;
            Assert.LessOrEqual(Mathf.Max(rect.width, rect.height), enemy.Definition.ProjectileRange + 1f, "no larger than the lane length");
            Assert.LessOrEqual(Mathf.Min(rect.width, rect.height), 1f, "and narrow — nothing like a 14×14 tile square");
        }

        private (EnemyController enemy, GameObject target) EnemyWithTarget(string id, Vector2 targetAt, Vector2? enemyAt = null)
        {
            var definition = _catalog.Enemies.First(e => e.Id == id);
            var target = new GameObject("Target_" + id);
            _created.Add(target);
            target.transform.position = targetAt + (enemyAt ?? Vector2.zero);
            target.AddComponent<CircleCollider2D>().isTrigger = true;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            target.AddComponent<TestDamageableTarget>();
            var enemy = new DefaultEnemySpawner(_catalog.Stagger).Spawn(definition, enemyAt ?? Vector2.zero, target.transform);
            _created.Add(enemy.gameObject);
            enemy.enabled = false;
            if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
            return (enemy, target);
        }
    }
}
