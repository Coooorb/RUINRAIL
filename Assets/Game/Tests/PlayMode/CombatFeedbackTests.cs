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

        private sealed class Guard : IInvulnerabilityState
        {
            public bool IsInvulnerable { get; set; }
        }

        /// <summary>
        /// The survivor's own damage read, on a player composed exactly as the game composes one: every applied hit tints
        /// the body red for the configured beat, a hit that deals nothing (zero, invulnerable / i-frames) never does,
        /// repeated hits restart the beat without stacking, the killing hit still reads, and the body always returns to
        /// its exact original colour.
        /// </summary>
        [Test]
        public void PlayerBody_FlashesRedOnRealDamageOnly_RetriggersCleanly_AndRestoresExactly()
        {
            FeedbackPreferences.Set(1f, true, true);
            var content = RuinRail.App.GameContentCatalog.Load();
            var player = RuinRail.Gameplay.Player.PlayerEntityBuilder.Build(new RuinRail.Gameplay.Player.PlayerEntityBuilder.Options
            {
                Name = "FlashPlayer", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = content.PlayerBalance, Caps = content.StatCaps
            });
            _created.Add(player);
            RuinRail.App.PlayerVisualComposer.Compose(player, content);
            RuinRail.App.PlayerVisualComposer.Compose(player, content); // idempotent: never a second flash
            var flashes = player.GetComponents<HitFlash>();
            Assert.AreEqual(1, flashes.Length, "one hit flash on the composed player");
            var flash = flashes[0];
            var body = RuinRail.Presentation.Animation.CharacterVisual.RendererOf(player);
            Assert.IsNotNull(body);
            var original = body.color;
            var health = player.GetComponent<HealthComponent>();
            var config = content.Feedback;
            Assert.AreNotEqual(original, config.PlayerHitFlashColor, "the damage tint is visibly different from the body");
            Assert.Less(config.PlayerHitFlashColor.g, 0.5f, "and it reads red");

            // Real damage: immediate red tint, HP unchanged by the presentation.
            var hp = health.CurrentHealth;
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(5)));
            Assert.IsTrue(flash.IsFlashing);
            Assert.AreEqual(config.PlayerHitFlashColor, body.color, "tinted on the very hit");
            Assert.AreEqual(hp - 5, health.CurrentHealth);
            flash.Tick(config.PlayerHitFlashSeconds + 0.01f);
            Assert.AreEqual(original, body.color, "restored exactly");
            Assert.IsFalse(flash.IsFlashing);

            // Nothing applied, nothing shown: zero damage, and a hit into invulnerability (dash i-frames, revive protection).
            var count = flash.Flashes;
            Assert.IsFalse(health.TryApplyDamage(new DamageRequest(0)));
            var guard = new Guard { IsInvulnerable = true };
            health.SetInvulnerabilityState(guard);
            Assert.IsFalse(health.TryApplyDamage(new DamageRequest(7)));
            Assert.AreEqual(count, flash.Flashes, "blocked or empty hits never flash");
            Assert.AreEqual(original, body.color);
            guard.IsInvulnerable = false;

            // Rapid hits restart the beat (no stacking, no permanent tint).
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(2)));
            flash.Tick(config.PlayerHitFlashSeconds * 0.7f);
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(2)));
            flash.Tick(config.PlayerHitFlashSeconds * 0.7f);
            Assert.IsTrue(flash.IsFlashing, "the second hit restarted the beat");
            Assert.AreEqual(config.PlayerHitFlashColor, body.color);
            flash.Tick(config.PlayerHitFlashSeconds);
            Assert.AreEqual(original, body.color, "one beat after the last hit the body is back to normal");
            Assert.AreEqual(count + 2, flash.Flashes);

            // The killing hit reads too, then the body returns to normal; a dead body takes no further flash.
            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(health.CurrentHealth)));
            Assert.IsFalse(health.IsAlive);
            Assert.IsTrue(flash.IsFlashing);
            flash.Tick(config.PlayerHitFlashSeconds + 0.01f);
            Assert.AreEqual(original, body.color);
            Assert.IsFalse(health.TryApplyDamage(new DamageRequest(3)));
            Assert.IsFalse(flash.IsFlashing);

            // Disabled mid-flash (despawn, scene change): the colour is put back.
            Assert.IsTrue(health.Revive(40));
            var afterRevive = flash.Flashes;
            Assert.AreEqual(original, body.color, "a revive is not a hit");

            // Co-op: a peer's body is driven by the host's replicated HP; a drop reads as a hit, an unchanged value does not.
            health.ApplyReplicatedHealth(34, health.MaxHealth);
            Assert.IsTrue(flash.IsFlashing, "replicated damage flashes the replica");
            flash.Tick(config.PlayerHitFlashSeconds + 0.01f);
            health.ApplyReplicatedHealth(34, health.MaxHealth);
            Assert.IsFalse(flash.IsFlashing, "an unchanged replicated value is not a hit");
            Assert.AreEqual(afterRevive + 1, flash.Flashes);

            Assert.IsTrue(health.TryApplyDamage(new DamageRequest(3)));
            flash.enabled = false;
            Assert.AreEqual(original, body.color);
        }

        /// <summary>
        /// Enemies, elites and every shipped boss read an applied hit on the body. A normal enemy / elite flashes the
        /// config's red; a boss's red scales with the effective damage of that hit (clamped at the strong tint). Zero and
        /// invulnerable hits never flash; rapid hits restart the beat; the killing hit leaves the death state intact and
        /// the body returns to its exact colour.
        /// </summary>
        [Test]
        public void EnemyEliteAndBossBodies_FlashRedOnRealDamage_BossIntensityFollowsTheHit_AndRestoreExactly()
        {
            FeedbackPreferences.Set(1f, true, true);
            var content = RuinRail.App.GameContentCatalog.Load();
            Assert.Less(_config.HitFlashColor.g, 0.6f, "the enemy flash reads red (a white multiply was invisible)");

            // ---- a normal enemy and an elite: the config's red on every applied hit ----
            var grunt = new RuinRail.Gameplay.Enemies.DefaultEnemySpawner().Spawn(content.Enemies.First(e => e.Id == "grunt"), new Vector2(300f, 0f), null);
            _created.Add(grunt.gameObject);
            var elite = new DefaultEliteSpawner().Spawn(content.Elites.First(), new Vector2(310f, 0f), null, null).Elite;
            _created.Add(elite.gameObject);
            foreach (var actor in new GameObject[] { grunt.gameObject, elite.gameObject })
            {
                var body = actor.AddComponent<SpriteRenderer>();
                var health = actor.GetComponent<HealthComponent>();
                var flash = actor.AddComponent<HitFlash>();
                flash.Configure(_config, health, null, body);
                var original = body.color;

                Assert.IsFalse(health.TryApplyDamage(new DamageRequest(0)));
                Assert.AreEqual(0, flash.Flashes, $"{actor.name}: zero damage never flashes");
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(3)));
                Assert.AreEqual(_config.HitFlashColor, body.color, $"{actor.name}: red on the applied hit");
                flash.Tick(_config.HitFlashSeconds * 0.7f);
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(3)));
                flash.Tick(_config.HitFlashSeconds * 0.7f);
                Assert.IsTrue(flash.IsFlashing, $"{actor.name}: a second hit restarted the beat");
                flash.Tick(_config.HitFlashSeconds);
                Assert.AreEqual(original, body.color, $"{actor.name}: restored exactly");

                var guard = new Guard { IsInvulnerable = true };
                health.SetInvulnerabilityState(guard);
                Assert.IsFalse(health.TryApplyDamage(new DamageRequest(9)));
                Assert.AreEqual(2, flash.Flashes, $"{actor.name}: an invulnerable hit never flashes");
            }

            // ---- every shipped boss: the tint's strength follows the effective damage of each hit ----
            var lane = 0;
            foreach (var definition in content.Bosses.Where(b => b != null))
            {
                var encounter = new RuinRail.Gameplay.Enemies.Bosses.DefaultBossSpawner(new[] { definition }, content.Stagger).Spawn(definition, new Vector2(400f + lane++ * 40f, 0f), null);
                _created.Add(encounter.gameObject);
                var boss = encounter.Boss;
                var body = boss.gameObject.AddComponent<SpriteRenderer>();
                var health = boss.Health;
                var flash = boss.gameObject.AddComponent<HitFlash>();
                flash.Configure(_config, health, boss.Impact, body);
                flash.UseBossProfile();
                var original = body.color;
                var max = health.MaxHealth;

                // Weak, medium and heavy hits: strictly stronger red for a larger applied hit (lower green/blue).
                var weak = Mathf.Max(1, Mathf.RoundToInt(max * 0.004f));
                var medium = Mathf.RoundToInt(max * 0.02f);
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(weak)));
                var weakIntensity = flash.LastIntensity;
                var weakColor = body.color;
                flash.Tick(_config.BossFlashSeconds + 0.01f);
                Assert.AreEqual(original, body.color, $"{definition.Id}: restored after the weak hit");
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(medium)));
                var mediumIntensity = flash.LastIntensity;
                var mediumColor = body.color;
                Assert.Greater(mediumIntensity, weakIntensity, $"{definition.Id}: a larger hit flashes stronger");
                Assert.Less(mediumColor.g, weakColor.g, $"{definition.Id}: and redder on screen");
                Assert.Greater(weakColor.g, _config.BossFlashStrongColor.g, $"{definition.Id}: a weak hit is subtle, not the full tint");

                // A huge hit is clamped at the strong tint: readable, never beyond.
                flash.Tick(_config.BossFlashSeconds + 0.01f);
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(Mathf.RoundToInt(max * 0.3f))));
                Assert.AreEqual(1f, flash.LastIntensity, 1e-4f);
                Assert.AreEqual(_config.BossFlashStrongColor, body.color, $"{definition.Id}: clamped at the strong tint");

                // Mitigated to nothing / invulnerable: no flash.
                var flashes = flash.Flashes;
                var guard = new Guard { IsInvulnerable = true };
                health.SetInvulnerabilityState(guard);
                Assert.IsFalse(health.TryApplyDamage(new DamageRequest(500)));
                Assert.AreEqual(flashes, flash.Flashes, $"{definition.Id}: an invulnerable hit never flashes");
                guard.IsInvulnerable = false;

                // The killing hit: a flash, the death state untouched, then the exact colour back.
                Assert.IsTrue(health.TryApplyDamage(new DamageRequest(health.CurrentHealth)));
                Assert.IsFalse(boss.IsAlive);
                Assert.AreEqual(RuinRail.Gameplay.Enemies.Attacks.MovesetActorState.Dead, boss.State, $"{definition.Id}: dead as before");
                flash.Tick(_config.BossFlashSeconds + 0.01f);
                Assert.AreEqual(original, body.color, $"{definition.Id}: no tint left on the corpse");
                Assert.IsFalse(health.TryApplyDamage(new DamageRequest(5)));
                Assert.IsFalse(flash.IsFlashing);
            }

            // The mapping itself: non-decreasing in the applied damage, 0 for nothing, clamped at 1.
            var previous = 0f;
            for (var damage = 0; damage <= 400; damage += 5)
            {
                var intensity = _config.BossFlashIntensity(damage, 1700);
                Assert.GreaterOrEqual(intensity, previous);
                Assert.LessOrEqual(intensity, 1f);
                previous = intensity;
            }

            Assert.AreEqual(0f, _config.BossFlashIntensity(0, 1700));
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
