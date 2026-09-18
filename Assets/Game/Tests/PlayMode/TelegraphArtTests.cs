using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
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
            Assert.AreEqual(enemy.Definition.ProjectileRange, shape.Size.x, 0.001f, "lane length is the projectile range");
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

            var marker = Object.FindObjectsByType<PooledEffect>(FindObjectsSortMode.None).FirstOrDefault(e => e.IsActive && e.Kind == TelegraphIndicator.KindProjectile);
            Assert.IsNotNull(marker, "the lane marker is a live pooled effect");
            Assert.IsTrue(marker.Renderer.sprite.name.StartsWith("vfx_telegraph_projectile_"), "final telegraph art, not the placeholder quad: " + marker.Renderer.sprite.name);
            var bounds = marker.Renderer.bounds.size;
            Assert.LessOrEqual(Mathf.Max(bounds.x, bounds.y), enemy.Definition.ProjectileRange + 0.25f, "no larger than the lane length");
            Assert.LessOrEqual(Mathf.Min(bounds.x, bounds.y), 1f, "and narrow — nothing like a 14×14 tile square");
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
