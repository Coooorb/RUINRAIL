using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Presentation;
using RuinRail.Presentation.Vfx;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 139 — pooled VFX, exact damage numbers, hit flash / shake behind settings, sound-independent telegraph markers, readability caps; none of it touches gameplay.</summary>
    public class CombatFeedbackTests
    {
        private readonly List<Object> _created = new();
        private FeedbackConfig _config;

        [SetUp]
        public void SetUp()
        {
            _config = AssetDatabase.LoadAssetAtPath<FeedbackConfig>("Assets/Game/ScriptableObjects/Presentation/FeedbackConfig.asset");
            Assert.IsNotNull(_config);
            FeedbackPreferences.Reset();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackPreferences.Reset();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            _created.Clear();
        }

        private T New<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void EffectPool_IsCappedRecyclesOldest_AndLeaksNothing_OverThousandsOfSpawns()
        {
            var pool = New<EffectPool>("Pool");
            pool.Configure(32);
            var before = Object.FindObjectsByType<PooledEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            for (var i = 0; i < 5000; i++)
            {
                pool.Spawn("impact", new Vector2(i % 10, i % 7), 0.1f, Color.white);
                if (i % 50 == 0) pool.TickAll(0.05f);
            }

            Assert.AreEqual(32, pool.Created, "Never more instances than the cap.");
            Assert.AreEqual(32, pool.Live);
            Assert.Greater(pool.Recycled, 0, "The oldest live effect is recycled at the cap.");
            Assert.AreEqual(5000, pool.Spawned);
            pool.TickAll(1f);
            Assert.AreEqual(0, pool.Live, "Everything returns after its lifetime.");
            Assert.AreEqual(32, pool.Created);
            var after = Object.FindObjectsByType<PooledEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Assert.AreEqual(before + 32, after, "No leaked objects.");
            var effect = pool.Spawn("muzzle", Vector2.zero, 0.05f, Color.yellow);
            Assert.AreEqual("WorldVFX", effect.Renderer.sortingLayerName);
            Assert.IsNotNull(effect.Renderer.sprite, "Explicit placeholder sprite.");
        }

        [Test]
        public void DamageNumbers_ShowExactAppliedIntegers_HealsAsPlus_PooledAndSettingControlled()
        {
            var numbers = New<DamageNumberPool>("Numbers");
            numbers.Configure(_config);
            var target = New<HealthComponent>("Target");
            target.SetMaxHealth(100);
            target.transform.position = new Vector3(3f, 2f, 0f);
            numbers.Bind(target);

            // Mitigation: the number is what HealthComponent actually applied, never the request.
            target.SetIncomingDamageModifier(new HalvingModifier());
            Assert.IsTrue(target.TryApplyDamage(new DamageRequest(31)));
            Assert.AreEqual(1, numbers.Live);
            Assert.AreEqual(15, numbers.LiveNumbers[0].Value, "31 halved (floored) → 15 applied → 15 shown.");
            Assert.AreEqual("15", numbers.LiveNumbers[0].Text.text);
            Assert.IsFalse(numbers.LiveNumbers[0].Text.text.Contains("!"), "No crit styling.");
            Assert.AreEqual(85, target.CurrentHealth, "Showing a number changed nothing.");
            Assert.AreEqual(3f, numbers.LiveNumbers[0].transform.position.x, 0.001f, "Placed at the target.");

            target.SetIncomingDamageModifier(null);
            target.TryApplyDamage(new DamageRequest(1000));
            Assert.AreEqual(85, numbers.LiveNumbers[1].Value, "Overkill shows the applied 85, not 1000.");
            target.Revive(50);
            Assert.AreEqual(3, numbers.Live);
            Assert.IsTrue(numbers.LiveNumbers[2].IsHeal);
            Assert.AreEqual("+50", numbers.LiveNumbers[2].Text.text);

            // Pool cap with recycling; lifetime returns them.
            for (var i = 0; i < 200; i++) numbers.Show(7, false, Vector2.zero);
            Assert.AreEqual(_config.DamageNumberCapacity, numbers.Created);
            Assert.AreEqual(_config.DamageNumberCapacity, numbers.Live);
            numbers.TickAll(_config.DamageNumberSeconds + 0.1f);
            Assert.AreEqual(0, numbers.Live);

            // Setting OFF: nothing shown, damage still applied.
            FeedbackPreferences.Set(1f, damageNumbers: false, hitFlash: true);
            var hp = target.CurrentHealth;
            target.TryApplyDamage(new DamageRequest(5));
            Assert.AreEqual(hp - 5, target.CurrentHealth);
            Assert.AreEqual(0, numbers.Live);
            Assert.Greater(numbers.Suppressed, 0);
            numbers.Unbind(target);
        }

        [Test]
        public void HitFlashAndShake_FollowSettings_AndNeverAlterGameplay()
        {
            var body = New<SpriteRenderer>("Body");
            body.color = new Color(0.2f, 0.4f, 0.6f);
            var health = body.gameObject.AddComponent<HealthComponent>();
            health.SetMaxHealth(50);
            var flash = body.gameObject.AddComponent<HitFlash>();
            flash.Configure(_config, health, null, body);
            health.TryApplyDamage(new DamageRequest(3));
            Assert.IsTrue(flash.IsFlashing);
            Assert.AreEqual(_config.HitFlashColor, body.color);
            flash.Tick(_config.HitFlashSeconds + 0.01f);
            Assert.AreEqual(new Color(0.2f, 0.4f, 0.6f), body.color, "Restored to the original tint.");
            Assert.AreEqual(47, health.CurrentHealth);
            FeedbackPreferences.Set(1f, true, hitFlash: false);
            health.TryApplyDamage(new DamageRequest(3));
            Assert.IsFalse(flash.IsFlashing, "Hit flash off: no tint…");
            Assert.AreEqual(44, health.CurrentHealth, "…same damage.");
            Assert.AreEqual(1, flash.Suppressed);

            var camGo = new GameObject("Camera");
            _created.Add(camGo);
            camGo.AddComponent<Camera>();
            var rig = camGo.AddComponent<CameraRig>();
            rig.SetConfig(AssetDatabase.LoadAssetAtPath<CameraRigConfig>("Assets/Game/ScriptableObjects/Presentation/CameraRigConfig.asset"));
            var target = new GameObject("T").transform;
            _created.Add(target.gameObject);
            target.position = new Vector3(4f, 4f, 0f);
            rig.SetFollow(target);
            rig.Step(0.016f);
            var shake = camGo.AddComponent<CameraShake>();
            shake.Configure(_config, null, rig);
            Assert.IsTrue(_config.IsOrdered, "art/104: small gun < shotgun < explosion ≤ boss slam.");

            FeedbackPreferences.Set(1f, true, true);
            shake.Request(ShakeKind.BossSlam);
            shake.Tick(0.016f);
            rig.Step(0.016f);
            Assert.AreNotEqual(Vector2.zero, shake.CurrentOffset);
            Assert.IsTrue(CameraFraming.IsOnPixelGrid(rig.Position, 32), "Shake keeps the pixel grid.");
            Assert.AreEqual(rig.ShakeOffset, shake.CurrentOffset);
            var framed = rig.Position - rig.ShakeOffset;
            Assert.AreEqual(new Vector2(4f, 4f), framed, "The framed position is untouched by the shake.");
            shake.Tick(1f);
            rig.Step(0.016f);
            Assert.AreEqual(Vector2.zero, rig.ShakeOffset);

            FeedbackPreferences.Set(0.5f, true, true);
            shake.Request(ShakeKind.SmallGun);
            Assert.AreEqual(1, shake.Suppressed, "0.5 px × 0.5 rounds below a pixel: nothing to show.");
            FeedbackPreferences.Set(0f, true, true);
            shake.Request(ShakeKind.BossSlam);
            shake.Tick(0.016f);
            Assert.AreEqual(Vector2.zero, shake.CurrentOffset, "Shake off in Settings.");
            Assert.AreEqual(2, shake.Suppressed);
        }

        [Test]
        public void CombatFeedback_ProjectileImpactExplosionStaggerHealLegendaryGlow_AreReadOnlyHooks()
        {
            var pool = New<EffectPool>("Pool");
            pool.Configure(64);
            var feedback = New<CombatFeedback>("Feedback");
            feedback.Configure(_config, pool, null);

            var projectileGo = new GameObject("Projectile");
            _created.Add(projectileGo);
            projectileGo.AddComponent<Rigidbody2D>();
            var projectile = projectileGo.AddComponent<Projectile>();
            feedback.Attach(projectile);
            var impactedField = typeof(Projectile).GetField("Impacted", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var impacted = impactedField?.GetValue(projectile) as System.MulticastDelegate;
            Assert.IsNotNull(impacted, "Subscribed to the projectile's presentation event.");
            impacted.DynamicInvoke(projectile, new Vector2(1f, 1f), true);
            Assert.AreEqual(1, feedback.CountOf("impact"));
            Assert.AreEqual(1, pool.Live);
            Assert.IsFalse(projectile.IsResolved, "The hook resolved nothing.");

            var receiver = New<ImpactReceiver>("Enemy");
            feedback.Attach(receiver);
            var staggeredField = typeof(ImpactReceiver).GetField("Staggered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            (staggeredField.GetValue(receiver) as System.MulticastDelegate).DynamicInvoke(receiver);
            Assert.AreEqual(1, feedback.CountOf("stagger"));

            var health = New<HealthComponent>("Player");
            health.SetMaxHealth(100);
            feedback.Attach(health);
            health.TryApplyDamage(new DamageRequest(40));
            Assert.IsTrue(health.Heal(20));
            Assert.AreEqual(1, feedback.CountOf("heal"));
            Assert.AreEqual(0, feedback.CountOf("impact") - 1, "Damage on a player is not an impact effect (that is the projectile's).");

            Assert.AreEqual(_config.LegendaryGlowScale, feedback.GlowScaleFor(Rarity.Legendary));
            Assert.Greater(feedback.GlowScaleFor(Rarity.Legendary), feedback.GlowScaleFor(Rarity.Rare), "Legendary glow clearly stronger.");
            Assert.AreEqual(0f, feedback.GlowScaleFor(Rarity.Common));
            Assert.LessOrEqual(_config.LegendaryGlowScale, 3f, "…but not overwhelming (≤ 3 tiles).");
            Assert.AreEqual(ShakeKind.SmallGun, CombatFeedback.ShakeFor(null));
            Assert.LessOrEqual(_config.ExplosionSeconds, 1f, "Explosions never hide hazards for seconds.");
        }

        [UnityTest]
        public IEnumerator TelegraphMarker_ShowsTheRealAttackShape_ForExactlyTheGameplayTelegraph_WithTheEliteColour()
        {
            var pool = New<EffectPool>("Pool");
            pool.Configure(16);
            var definition = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset");
            var encounter = new DefaultEliteSpawner().Spawn(definition, Vector2.zero, null, null);
            _created.Add(encounter.gameObject);
            var elite = encounter.Elite;
            var dummy = new GameObject("PlayerDummy");
            _created.Add(dummy);
            dummy.transform.position = new Vector2(2.2f, 0f);
            dummy.AddComponent<BoxCollider2D>().size = Vector2.one;
            dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            dummy.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            dummy.AddComponent<HealthComponent>().SetMaxHealth(500);
            var indicator = elite.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_config, pool, null, elite);
            Assert.IsTrue(indicator.IsEliteOrBoss);

            EnemyAttackDefinition attack = null;
            elite.AttackTelegraphStarted += (_, a) => attack = a;
            elite.SetTarget(dummy.transform);
            var deadline = Time.time + 3f;
            while (Time.time < deadline && elite.State != MovesetActorState.Telegraph) yield return null;
            yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.IsTrue(indicator.IsShowing, "Marker visible while gameplay telegraphs — no sound needed.");
            Assert.AreEqual(_config.EliteBossTelegraphColor, indicator.MarkerColor);
            Assert.IsNotNull(attack);
            var (size, _) = TelegraphIndicator.ShapeFor(attack, elite.LockedDirection);
            Assert.AreEqual(size, indicator.MarkerScale, "The marker is the attack's real hit shape.");
            Assert.Greater(size.x, 0f);
            var fillEarly = indicator.Fill01;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(indicator.Fill01, fillEarly, "Fills over the telegraph.");
            while (Time.time < deadline + 3f && elite.State == MovesetActorState.Telegraph) yield return null;
            yield return null;
            Assert.AreNotEqual(MovesetActorState.Telegraph, elite.State);
            Assert.IsFalse(indicator.IsShowing, "Gone the moment the telegraph ends.");
            Assert.AreEqual(1, indicator.Shown);

            // Zone / slam / dash shapes follow the definition's numbers.
            var zone = ScriptableObject.CreateInstance<EnemyAttackDefinition>();
            _created.Add(zone);
            var so = new SerializedObject(zone);
            so.FindProperty("_motion").enumValueIndex = (int)AttackMotion.Zone;
            so.FindProperty("_zoneLength").floatValue = 6f;
            so.FindProperty("_zoneWidth").floatValue = 2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            var (zoneSize, zoneOffset) = TelegraphIndicator.ShapeFor(zone, Vector2.right);
            Assert.AreEqual(new Vector2(6f, 2f), zoneSize);
            Assert.AreEqual(new Vector2(3f, 0f), zoneOffset);
        }

        [Test]
        public void Readability_AtActiveCountCaps_EffectsAndNumbersStayBounded()
        {
            var pool = New<EffectPool>("Pool");
            pool.Configure(_config.DamageNumberCapacity);
            var numbers = New<DamageNumberPool>("Numbers");
            numbers.Configure(_config);
            // Trio + a full room: 3 players and 12 enemies each hit 10 times in one second.
            var targets = new List<HealthComponent>();
            for (var i = 0; i < 15; i++)
            {
                var h = New<HealthComponent>("T" + i);
                h.SetMaxHealth(1000);
                h.transform.position = new Vector3(i, 0f, 0f);
                numbers.Bind(h);
                targets.Add(h);
            }

            for (var round = 0; round < 10; round++)
            {
                foreach (var t in targets) { t.TryApplyDamage(new DamageRequest(3)); pool.Spawn("impact", t.transform.position, _config.ImpactSeconds, Color.white); }
                pool.TickAll(0.1f);
                numbers.TickAll(0.1f);
            }

            Assert.LessOrEqual(numbers.Live, _config.DamageNumberCapacity);
            Assert.LessOrEqual(pool.Live, pool.Capacity);
            Assert.AreEqual(150, numbers.Shown);
            Assert.IsTrue(targets.All(t => t.CurrentHealth == 970), "Every hit applied exactly once regardless of feedback volume.");
        }

        private sealed class HalvingModifier : IIncomingDamageModifier
        {
            public int ModifyIncomingDamage(DamageRequest request) => request.Amount / 2;
        }
    }
}
