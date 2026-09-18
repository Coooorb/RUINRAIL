using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.Presentation.Animation;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 138 — animation is derived from gameplay state (never the other way round): 8-way body + 360° weapon, 8–12 fps stepping, explicit placeholder fallback, enemy telegraph/strike timing from gameplay, weapon visuals from weapon state.</summary>
    public class AnimationIntegrationTests
    {
        private readonly List<Object> _created = new();
        private PlayerBalanceConfig _balance;

        [SetUp]
        public void SetUp()
        {
            _balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            _created.Clear();
        }

        private Sprite MakeSprite()
        {
            var tex = new Texture2D(4, 4);
            _created.Add(tex);
            var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 32f);
            _created.Add(sprite);
            return sprite;
        }

        private CharacterAnimationSet Set(string actorId, params (string key, BodyFacing8 facing, int frames, int fps, bool loop)[] clips)
        {
            var set = ScriptableObject.CreateInstance<CharacterAnimationSet>();
            _created.Add(set);
            var list = new List<SpriteAnimationClip>();
            foreach (var (key, facing, frames, fps, loop) in clips)
            {
                var sprites = new Sprite[frames];
                for (var i = 0; i < frames; i++) sprites[i] = MakeSprite();
                list.Add(new SpriteAnimationClip { Key = key, Facing = facing, Frames = sprites, FramesPerSecond = fps, Loop = loop });
            }

            set.Configure(actorId, list);
            return set;
        }

        [Test]
        public void SpriteAnimator_StepsAt8To12Fps_LoopsOrHolds_AndFallsBackExplicitlyWhenClipsAreMissing()
        {
            var set = Set("player", ("Idle", BodyFacing8.S, 4, 30, true), ("Death", BodyFacing8.S, 3, 4, false), ("Idle", BodyFacing8.E, 4, 10, true));
            Assert.AreEqual(8 * 6 - 3, set.Missing(AnimationRules.PlayerClipKeys).Count, "Every unauthored key/facing is listed.");

            var go = new GameObject("Body");
            _created.Add(go);
            var renderer = go.AddComponent<SpriteRenderer>();
            var animator = go.AddComponent<SpriteAnimator>();
            animator.Configure(renderer, set);

            animator.Play("Idle", BodyFacing8.S);
            Assert.IsFalse(animator.IsPlaceholder);
            Assert.AreEqual(12, animator.EffectiveFps, "30 fps authored → clamped to the 12 fps ceiling.");
            Assert.AreEqual(0, animator.CurrentFrame);
            animator.Tick(1f / 12f + 0.001f);
            Assert.AreEqual(1, animator.CurrentFrame);
            animator.Tick(0.5f);
            Assert.AreEqual((1 + 6) % 4, animator.CurrentFrame, "Loops.");
            Assert.AreSame(renderer.sprite, set.Clips[0].Frames[animator.CurrentFrame]);
            animator.Play("Idle", BodyFacing8.E);
            Assert.AreEqual((1 + 6) % 4, animator.CurrentFrame, "Turning keeps the frame phase.");

            animator.Play("Death", BodyFacing8.S);
            Assert.AreEqual(8, animator.EffectiveFps, "4 fps authored → raised to the 8 fps floor.");
            animator.Tick(5f);
            Assert.AreEqual(2, animator.CurrentFrame, "Non-looping clips hold their last frame.");
            Assert.IsTrue(animator.IsFinished);

            // Missing clip: explicit placeholder, no exception, recorded for the gate, last sprite kept.
            var last = renderer.sprite;
            Assert.DoesNotThrow(() => animator.Play("Dash", BodyFacing8.NW));
            Assert.IsTrue(animator.IsPlaceholder);
            CollectionAssert.Contains(animator.MissingClips, "Dash/NW");
            Assert.AreSame(last, renderer.sprite);
            Assert.DoesNotThrow(() => animator.Tick(1f));

            var noSet = go.AddComponent<SpriteAnimator>();
            noSet.Configure(renderer, null);
            Assert.DoesNotThrow(() => { noSet.Play("Idle", BodyFacing8.S); noSet.Tick(0.2f); });
            Assert.IsTrue(noSet.IsPlaceholder);
        }

        [Test]
        public void PlayerDriver_MapsLifeDashMoveToStates_AndKeeps8WayBodyWith360Aim()
        {
            var reader = new FakePlayerInputReader();
            var roster = new PartyLifeRoster();
            var go = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Local", IsLocal = true, InputReader = reader, BalanceConfig = _balance, LifeRoster = roster, ParticipantId = "Local" });
            _created.Add(go);
            var mate = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Mate", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = _balance, LifeRoster = roster, ParticipantId = "Mate", Position = new Vector2(0.5f, 0f) });
            _created.Add(mate);
            var aiming = go.GetComponent<PlayerAiming>();
            var pivot = new GameObject("WeaponPivot").transform;
            pivot.SetParent(go.transform, false);
            aiming.SetAimPivot(pivot);
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, false);
            var animator = body.AddComponent<SpriteAnimator>();
            animator.Configure(body.AddComponent<SpriteRenderer>(), Set("player"));
            var driver = go.AddComponent<PlayerAnimationDriver>();
            driver.Configure(animator, go.GetComponent<PlayerLifeStateComponent>(), go.GetComponent<PlayerDash>(), aiming, reader, go.GetComponent<Rigidbody2D>());

            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.Idle, driver.State);

            // 37° aim: the body snaps to NE while the pivot and AimDirection keep the exact angle.
            reader.IsAimFromPointer = false;
            reader.Aim = new Vector2(Mathf.Cos(37f * Mathf.Deg2Rad), Mathf.Sin(37f * Mathf.Deg2Rad));
            aiming.SendMessage("Update");
            driver.Tick(0.016f);
            Assert.AreEqual(BodyFacing8.NE, driver.Facing);
            Assert.AreEqual(37f, Vector2.SignedAngle(Vector2.right, aiming.AimDirection), 0.01f, "Gameplay aim is not quantised.");
            Assert.AreEqual(37f, pivot.rotation.eulerAngles.z, 0.01f, "Weapon pivot rotates mathematically.");
            reader.Aim = new Vector2(Mathf.Cos(200f * Mathf.Deg2Rad), Mathf.Sin(200f * Mathf.Deg2Rad));
            aiming.SendMessage("Update");
            driver.Tick(0.016f);
            Assert.AreEqual(BodyFacing8.W, driver.Facing);
            Assert.AreEqual(200f, pivot.rotation.eulerAngles.z, 0.01f);

            // Moving left while aiming west: Walk; animation never touches movement or aim.
            reader.Move = Vector2.left;
            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.Walk, driver.State);
            Assert.AreEqual(Vector2.left, reader.Move);
            reader.Move = Vector2.zero;

            var health = go.GetComponent<HealthComponent>();
            var life = go.GetComponent<PlayerLifeStateComponent>();
            health.TryApplyDamage(new DamageRequest(999999));
            Assert.AreEqual(PlayerLifeState.Downed, life.State);
            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.Downed, driver.State);

            life.ReturnToAlive(30);
            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.GetUp, driver.State, "Revive/Get Up right after returning to Alive.");
            driver.Tick(PlayerAnimationDriver.GetUpSeconds + 0.01f);
            Assert.AreEqual(PlayerAnimState.Idle, driver.State);

            reader.Move = Vector2.left;
            reader.RaiseDash();
            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.Dash, driver.State, "Dash wins over Walk.");
            Assert.IsTrue(go.GetComponent<PlayerDash>().IsDashing, "Dash timing is gameplay's.");

            life.MarkDeadByAuthority("test");
            Assert.AreEqual(PlayerLifeState.Dead, life.State);
            driver.Tick(0.016f);
            Assert.AreEqual(PlayerAnimState.Death, driver.State);
            Assert.IsTrue(animator.IsPlaceholder, "No clips authored: explicit placeholder, no exception.");
            CollectionAssert.Contains(animator.MissingClips, "Death/W");
        }

        [Test]
        public void EnemyDriver_PureMapping_FollowsGameplayStates()
        {
            Assert.AreEqual(EnemyAnimState.Telegraph, EnemyAnimationDriver.Resolve(EnemyState.Telegraph, false, false));
            Assert.AreEqual(EnemyAnimState.Recover, EnemyAnimationDriver.Resolve(EnemyState.Recovery, false, false));
            Assert.AreEqual(EnemyAnimState.Recover, EnemyAnimationDriver.Resolve(EnemyState.Staggered, true, false));
            Assert.AreEqual(EnemyAnimState.Move, EnemyAnimationDriver.Resolve(EnemyState.Chase, true, false));
            Assert.AreEqual(EnemyAnimState.Idle, EnemyAnimationDriver.Resolve(EnemyState.Chase, false, false));
            Assert.AreEqual(EnemyAnimState.Attack, EnemyAnimationDriver.Resolve(EnemyState.Recovery, false, true), "Strike frame at the resolved attack.");
            Assert.AreEqual(EnemyAnimState.Death, EnemyAnimationDriver.Resolve(EnemyState.Dead, false, true), "Death wins over everything.");
            Assert.AreEqual(EnemyAnimState.Telegraph, EnemyAnimationDriver.Resolve(MovesetActorState.Telegraph, true, false));
            Assert.AreEqual(EnemyAnimState.Attack, EnemyAnimationDriver.Resolve(MovesetActorState.Attacking, false, false));
            Assert.AreEqual(EnemyAnimState.Death, EnemyAnimationDriver.Resolve(MovesetActorState.Dead, false, false));
        }

        [UnityTest]
        public IEnumerator EliteDriver_TelegraphLastsExactlyTheGameplayTelegraph_StrikeAtResolution_DamageUnchanged()
        {
            var definition = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset");
            var encounter = new DefaultEliteSpawner().Spawn(definition, Vector2.zero, null, null);
            _created.Add(encounter.gameObject);
            var elite = encounter.Elite;
            elite.SetDamageRoller(new FixedDamageRoller { FixedValue = 27 });
            var dummy = new GameObject("PlayerDummy");
            _created.Add(dummy);
            dummy.transform.position = new Vector2(2.2f, 0f);
            dummy.AddComponent<BoxCollider2D>().size = Vector2.one;
            dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            dummy.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var health = dummy.AddComponent<HealthComponent>();
            health.SetMaxHealth(500);

            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(elite.transform, false);
            var animator = bodyGo.AddComponent<SpriteAnimator>();
            animator.Configure(bodyGo.AddComponent<SpriteRenderer>(), Set(definition.Id));
            var driver = elite.gameObject.AddComponent<EnemyAnimationDriver>();
            driver.Configure(animator, null, elite, elite.GetComponent<Rigidbody2D>());

            var states = new List<(float t, EnemyAnimState s)>();
            EnemyAttackDefinition telegraphed = null;
            float telegraphStart = -1f, strikeAt = -1f;
            elite.AttackTelegraphStarted += (_, a) => { telegraphed = a; telegraphStart = Time.time; };
            elite.AttackResolved += (_, _) => strikeAt = Time.time;
            elite.SetTarget(dummy.transform);
            var deadline = Time.time + 4f;
            while (Time.time < deadline && strikeAt < 0f)
            {
                states.Add((Time.time, driver.State));
                yield return null;
            }

            yield return null;
            states.Add((Time.time, driver.State));
            Assert.IsNotNull(telegraphed);
            Assert.AreEqual("Overhead Slam", telegraphed.DisplayName);
            Assert.AreEqual(500 - 27, health.CurrentHealth, "Damage and its timing are untouched by the animation layer.");
            Assert.AreEqual(1, driver.StrikesShown);
            Assert.AreEqual(EnemyAnimState.Attack, driver.State, "Strike frame right after gameplay resolved the hit.");
            var telegraphFrames = states.FindAll(x => x.s == EnemyAnimState.Telegraph);
            Assert.IsTrue(telegraphFrames.Count > 0, "Telegraph was shown.");
            var firstTelegraph = telegraphFrames[0].t;
            var lastTelegraph = telegraphFrames[telegraphFrames.Count - 1].t;
            Assert.LessOrEqual(Mathf.Abs(firstTelegraph - telegraphStart), 0.1f, "Telegraph animation starts with the gameplay telegraph.");
            Assert.AreEqual(telegraphed.TelegraphSeconds, lastTelegraph - firstTelegraph, 0.15f, "…and lasts exactly as long as the gameplay telegraph.");
            Assert.IsFalse(states.Exists(x => x.s == EnemyAnimState.Attack && x.t < telegraphStart + telegraphed.TelegraphSeconds - 0.05f), "No attack frame while gameplay is still telegraphing.");
            Assert.AreEqual(BodyFacing8.E, driver.Facing, "Faces the target.");
            CollectionAssert.Contains(animator.MissingClips, "Telegraph/E");
        }

        [UnityTest]
        public IEnumerator WeaponVisuals_MeleeSwingMatchesTheHitboxArcAndReach_RangedRecoilAndReload_OwnNoLogic()
        {
            var player = new GameObject("Player");
            _created.Add(player);
            var input = new FakePlayerInputReader();
            var aiming = player.AddComponent<PlayerAiming>();
            aiming.SetInputReader(input);
            var melee = player.AddComponent<MeleeWeapon>();
            var definition = ScriptableObject.CreateInstance<MeleeWeaponDefinition>();
            _created.Add(definition);
            SetField(definition, "_damageMin", 14); SetField(definition, "_damageMax", 17); SetField(definition, "_attackRate", 2f);
            SetField(definition, "_attackRange", 1.2f); SetField(definition, "_attackArcDegrees", 80f); SetField(definition, "_windUpSeconds", 0.12f); SetField(definition, "_recoverySeconds", 0.18f);
            melee.SetInputReader(input);
            melee.SetAiming(aiming);
            melee.SetDamageRoller(new FixedDamageRoller { FixedValue = 15 });
            melee.SetDefinition(definition);
            var loadout = player.AddComponent<WeaponLoadout>();
            loadout.SetPrimary(melee);
            loadout.Initialize();
            var sprite = new GameObject("WeaponSprite").transform;
            sprite.SetParent(player.transform, false);
            var visuals = player.AddComponent<WeaponVisualDriver>();
            visuals.Configure(loadout, sprite);
            yield return null;

            Assert.AreEqual(WeaponVisualState.Idle, visuals.State);
            Assert.IsTrue(melee.TryAttack());
            var angles = new List<float>();
            var states = new List<WeaponVisualState>();
            var deadline = Time.time + 2f;
            while (Time.time < deadline && melee.State != MeleeAttackState.Idle)
            {
                yield return null;
                if (visuals.State == WeaponVisualState.Idle) continue;
                angles.Add(visuals.SwingAngleDegrees);
                states.Add(visuals.State);
            }

            yield return null;
            Assert.AreEqual(1, visuals.SwingsShown);
            Assert.AreEqual(80f, visuals.SwingArcDegrees, "The visible arc is the definition's hit arc.");
            Assert.AreEqual(1.2f, visuals.SwingReachTiles, "The visible reach is the definition's hit range.");
            Assert.IsTrue(states.Contains(WeaponVisualState.SwingWindUp));
            Assert.IsTrue(states.Contains(WeaponVisualState.SwingRecovery));
            Assert.AreEqual(WeaponVisualState.Idle, visuals.State, "Ends with the gameplay recovery, never on its own clock.");
            Assert.AreEqual(0f, visuals.SwingAngleDegrees);
            for (var i = 1; i < angles.Count; i++) Assert.GreaterOrEqual(angles[i], angles[i - 1], "The blade sweeps monotonically across the arc.");
            Assert.GreaterOrEqual(angles[0], -40f);
            Assert.LessOrEqual(angles[angles.Count - 1], 40f);
            Assert.AreEqual(-40f, WeaponVisualDriver.SwingAngleFor(0f, 80f));
            Assert.AreEqual(40f, WeaponVisualDriver.SwingAngleFor(1f, 80f));

            // Ranged: recoil on a magazine decrease, reload state from IsReloading; the driver never changes ammo.
            var ranged = player.AddComponent<RangedWeapon>();
            var rangedDefinition = AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/P9Ranger.asset");
            Assert.IsNotNull(rangedDefinition);
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 36);
            ranged.SetAmmoReserve(reserve);
            ranged.SetDefinition(rangedDefinition);
            loadout.SetSecondary(ranged);
            loadout.SelectSlot(WeaponSlot.Secondary);
            visuals.Tick(0.016f);
            Assert.AreEqual(WeaponVisualState.Idle, visuals.State);
            var magazine = ranged.MagazineAmmo;
            ranged.ApplyAuthoritativeState(magazine - 1, false);
            visuals.Tick(0.016f);
            Assert.AreEqual(WeaponVisualState.Firing, visuals.State);
            Assert.AreEqual(1, visuals.ShotsShown);
            Assert.Greater(visuals.Recoil01, 0f);
            Assert.AreEqual(magazine - 1, ranged.MagazineAmmo, "Visuals never touch the magazine.");
            visuals.Tick(WeaponVisualDriver.RecoilSeconds + 0.01f);
            Assert.AreEqual(0f, visuals.Recoil01);
            Assert.IsTrue(ranged.TryStartReload());
            visuals.Tick(0.016f);
            Assert.AreEqual(WeaponVisualState.Reloading, visuals.State);
            Assert.AreEqual(1, visuals.ShotsShown, "A magazine change during reload is not a shot.");
        }

        private static void SetField(object target, string fieldName, object value)
        {
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null) continue;
                field.SetValue(target, value);
                return;
            }

            throw new System.MissingFieldException(target.GetType().Name, fieldName);
        }
    }
}
