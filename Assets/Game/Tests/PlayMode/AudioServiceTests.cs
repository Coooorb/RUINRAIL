using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Audio;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
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
    /// <summary>TASK 140 — pooled audio service with bus gains from Settings, fallback silence for missing clips, bounded sources, gameplay event hooks, blaster heat cues, loop cleanup on transitions.</summary>
    public class AudioServiceTests
    {
        private readonly List<Object> _created = new();
        private AudioEventCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.LoadAssetAtPath<AudioEventCatalog>("Assets/Game/ScriptableObjects/Audio/AudioEventCatalog.asset");
            Assert.IsNotNull(_catalog);
            AudioLevels.Reset();
            DamageAuthority.LocalIsAuthoritative = true;
        }

        [TearDown]
        public void TearDown()
        {
            AudioLevels.Reset();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var actor in Object.FindObjectsByType<EliteController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            _created.Clear();
        }

        private AudioClip Clip(float seconds = 0.5f)
        {
            var clip = AudioClip.Create("test", (int)(44100 * seconds), 1, 44100, false);
            clip.SetData(new float[(int)(44100 * seconds)], 0);
            _created.Add(clip);
            return clip;
        }

        private AudioEventCatalog TestCatalog(params (string id, AudioBus bus, bool loop, int max, int minMs, bool withClip)[] events)
        {
            var catalog = ScriptableObject.CreateInstance<AudioEventCatalog>();
            _created.Add(catalog);
            var definitions = new List<AudioEventDefinition>();
            foreach (var e in events)
            {
                var d = ScriptableObject.CreateInstance<AudioEventDefinition>();
                _created.Add(d);
                d.Configure(e.id, e.bus, e.loop, e.max, e.minMs);
                if (e.withClip) d.SetClips(Clip());
                definitions.Add(d);
            }

            catalog.Configure(definitions);
            return catalog;
        }

        private AudioService Service(AudioEventCatalog catalog, int sources = 8)
        {
            var go = new GameObject("Audio");
            _created.Add(go);
            var service = go.AddComponent<AudioService>();
            service.Configure(catalog, null, sources);
            return service;
        }

        [Test]
        public void Catalog_CoversEveryRequiredEvent_AndMissingClipsAreSilentNeverThrowing()
        {
            var missing = _catalog.MissingIds(AudioEventIds.RequiredIds).ToList();
            CollectionAssert.IsEmpty(missing, "Every required event id has a definition (contract complete).");
            CollectionAssert.IsEmpty(_catalog.DuplicateIds().ToList());
            Assert.AreEqual(AudioEventIds.Required.Count, _catalog.Events.Count);
            foreach (var (id, bus, loop) in AudioEventIds.Required)
            {
                Assert.IsTrue(_catalog.TryGet(id, out var d));
                Assert.AreEqual(bus, d.Bus, id);
                Assert.AreEqual(loop, d.Loop, id);
            }

            var service = Service(_catalog);
            foreach (var id in AudioEventIds.RequiredIds) Assert.DoesNotThrow(() => service.Play(id, Vector2.one), id);
            Assert.DoesNotThrow(() => service.Play("does.not.exist"));
            Assert.DoesNotThrow(() => service.PlayLoop(AudioEventIds.BlasterHeatRising));
            CollectionAssert.IsEmpty(service.SilentEvents.ToList(), "Every event resolves to a generated clip; none falls back to silence.");
            CollectionAssert.Contains(service.UnknownEvents, "does.not.exist");

            // Both of these were zero only because the project had no audio: nothing could actually start. Now that
            // every event resolves, playing 53 cues genuinely occupies the pool and the loop genuinely runs, so the
            // meaningful assertion is that the fixed pool is respected rather than that nothing happened.
            Assert.LessOrEqual(service.BusyOneShots, 8, "The pool is fixed; high-frequency events never allocate beyond it.");
            Assert.AreEqual(1, service.ActiveLoops, "The blaster heat loop is running.");
            service.StopAllLoops();
            Assert.AreEqual(0, service.ActiveLoops, "Loops stop cleanly.");

            Assert.AreEqual(AudioEventIds.Required.Count + 1, service.TotalPlayed, "Every required event plus the loop is counted.");
        }

        [Test]
        public void OneShots_ArePooledAndBounded_StealOldest_CapPerEvent_Throttle_AndFollowSettingsGains()
        {
            var catalog = TestCatalog(("a", AudioBus.Weapons, false, 100, 0, true), ("b", AudioBus.UI, false, 2, 0, true), ("c", AudioBus.Weapons, false, 100, 200, true));
            var service = Service(catalog, 4);
            var before = Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            for (var i = 0; i < 500; i++) service.Play("a");
            Assert.AreEqual(4, service.OneShotSources, "Never more AudioSources than the pool.");
            Assert.AreEqual(4, service.BusyOneShots);
            Assert.AreEqual(496, service.Stolen, "Oldest one-shot stolen when the pool is full.");
            Assert.AreEqual(before, Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "No sources allocated on demand.");

            Assert.IsTrue(service.Play("b"));
            Assert.IsTrue(service.Play("b"));
            Assert.IsFalse(service.Play("b"), "Per-event instance cap.");
            Assert.AreEqual(1, service.Capped);

            Assert.IsTrue(service.Play("c"));
            service.Tick(0.05f);
            Assert.IsFalse(service.Play("c"), "Throttled inside the minimum interval.");
            service.Tick(0.2f);
            Assert.IsTrue(service.Play("c"));
            Assert.AreEqual(1, service.Throttled);

            // Gains: master × sfx for SFX buses, master × music for Music; mute = 0. Changing Settings updates live sources.
            AudioLevels.Set(0.5f, 0.2f, 0.8f, false);
            Assert.AreEqual(0.4f, AudioService.GainFor(AudioBus.Weapons), 1e-5f);
            Assert.AreEqual(0.1f, AudioService.GainFor(AudioBus.Music), 1e-5f);
            var played = service.Play("a");
            Assert.IsTrue(played);
            var sources = service.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying).ToList();
            Assert.IsTrue(sources.All(s => Mathf.Approximately(s.volume, 0.4f)), "Live sources follow the new gain.");
            AudioLevels.Set(1f, 1f, 1f, muted: true);
            Assert.AreEqual(0f, AudioService.GainFor(AudioBus.UI));
            Assert.IsTrue(service.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying).All(s => s.volume == 0f), "Mute silences everything already playing.");
        }

        [Test]
        public void Loops_AreTracked_StoppedIdempotently_AndClearedOnDepthAndExpeditionTransitions()
        {
            var catalog = TestCatalog((AudioEventIds.BlasterHeatRising, AudioBus.Weapons, true, 1, 0, true), (AudioEventIds.TransitDepart, AudioBus.Loot, false, 1, 0, true));
            var service = Service(catalog);
            var follow = new GameObject("Follow").transform;
            _created.Add(follow.gameObject);
            var loop = service.PlayLoop(AudioEventIds.BlasterHeatRising, follow);
            Assert.IsNotNull(loop);
            Assert.AreEqual(1, service.ActiveLoops);
            follow.position = new Vector3(5f, 1f, 0f);
            service.Tick(0.016f);
            service.StopLoop(loop);
            service.StopLoop(loop);
            Assert.IsFalse(loop.IsPlaying);
            Assert.AreEqual(0, service.ActiveLoops);

            service.PlayLoop(AudioEventIds.BlasterHeatRising, follow);
            service.PlayLoop(AudioEventIds.BlasterHeatRising, follow);
            Assert.AreEqual(2, service.ActiveLoops);
            var binder = service.gameObject.AddComponent<GameplayAudioBinder>();
            binder.Configure(service);
            var expedition = new RuinRail.Gameplay.Expedition.ExpeditionService(_ => null, _ => null, null);
            binder.Attach(expedition);
            var depthField = typeof(RuinRail.Gameplay.Expedition.ExpeditionService).GetField("DepthEntered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            (depthField.GetValue(expedition) as System.MulticastDelegate).DynamicInvoke(new object[] { null });
            Assert.AreEqual(0, service.ActiveLoops, "Depth transition cleans every loop.");
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.TransitDepart));

            // A spatial loop whose follow target disappears ends on the next tick.
            var gone = new GameObject("Gone").transform;
            service.PlayLoop(AudioEventIds.BlasterHeatRising, gone);
            Object.DestroyImmediate(gone.gameObject);
            service.Tick(0.016f);
            Assert.AreEqual(0, service.ActiveLoops);
        }

        [Test]
        public void Hooks_PlayerDownedDeathRevive_Dash_HitsHeal_UiSounds_LootRarity_AreWired()
        {
            var service = Service(_catalog);
            var binder = service.gameObject.AddComponent<GameplayAudioBinder>();
            binder.Configure(service);
            var balance = AssetDatabase.LoadAssetAtPath<PlayerBalanceConfig>("Assets/Game/ScriptableObjects/Player/PlayerBalanceConfig.asset");
            var roster = new PartyLifeRoster();
            var reader = new FakePlayerInputReader();
            var local = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Local", IsLocal = true, InputReader = reader, BalanceConfig = balance, LifeRoster = roster, ParticipantId = "Local" });
            _created.Add(local);
            var mate = PlayerEntityBuilder.Build(new PlayerEntityBuilder.Options { Name = "Mate", IsLocal = true, InputReader = new FakePlayerInputReader(), BalanceConfig = balance, LifeRoster = roster, ParticipantId = "Mate", Position = new Vector2(0.5f, 0f) });
            _created.Add(mate);
            var health = local.GetComponent<HealthComponent>();
            var life = local.GetComponent<PlayerLifeStateComponent>();
            binder.Attach(roster).Attach(health, isPlayer: true).Attach(local.GetComponent<PlayerDash>());

            health.TryApplyDamage(new DamageRequest(5));
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.PlayerHit));
            Assert.IsTrue(health.Heal(3));
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.Heal));
            reader.Move = Vector2.right;
            reader.RaiseDash();
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.PlayerDash));

            var mateLife = mate.GetComponent<PlayerLifeStateComponent>();
            mate.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(999999));
            Assert.AreEqual(PlayerLifeState.Downed, mateLife.State);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.PlayerDowned));
            mateLife.ReturnToAlive(30);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.PlayerReviveComplete));
            life.MarkDeadByAuthority("test");
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.PlayerDeath));

            UiSoundBus.Raise(UiSound.Confirm);
            UiSoundBus.Raise(UiSound.Failure);
            UiSoundBus.Raise(UiSound.Purchase);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.UiConfirm));
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.UiFailure));
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.UiPurchase));

            Assert.AreEqual(AudioEventIds.DropLegendary, AudioEventIds.DropEventFor(Rarity.Legendary), "Distinctive reusable Legendary drop sound.");
            Assert.AreNotEqual(AudioEventIds.DropEventFor(Rarity.Epic), AudioEventIds.DropEventFor(Rarity.Legendary));
            Assert.AreEqual(AudioEventIds.FireShotgun, AudioEventIds.FireEventFor(WeaponClass.Shotgun));
            Assert.AreEqual(11, new[] { WeaponClass.Pistol, WeaponClass.Smg, WeaponClass.AssaultRifle, WeaponClass.BattleRifle, WeaponClass.Shotgun, WeaponClass.Sniper, WeaponClass.Bow, WeaponClass.RocketLauncher, WeaponClass.Blaster, WeaponClass.Knife, WeaponClass.Spear }.Select(AudioEventIds.FireEventFor).Distinct().Count(), "Every weapon class has its own identity.");
            Assert.Greater(binder.Hooks, 3);
        }

        [UnityTest]
        public IEnumerator Hooks_EliteTelegraphCue_FiresWithTheGameplayTelegraph_AndTheVisualMarkerNeedsNoSound()
        {
            var service = Service(_catalog);
            var binder = service.gameObject.AddComponent<GameplayAudioBinder>();
            binder.Configure(service);
            var definition = AssetDatabase.LoadAssetAtPath<EliteDefinition>("Assets/Game/ScriptableObjects/Enemies/Elites/Elite_ScrapExecutioner.asset");
            var encounter = new DefaultEliteSpawner().Spawn(definition, Vector2.zero, null, null);
            _created.Add(encounter.gameObject);
            var elite = encounter.Elite;
            binder.Attach(elite);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.EliteSpawn));
            var dummy = new GameObject("PlayerDummy");
            _created.Add(dummy);
            dummy.transform.position = new Vector2(2.2f, 0f);
            dummy.AddComponent<BoxCollider2D>().size = Vector2.one;
            dummy.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            dummy.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            dummy.AddComponent<HealthComponent>().SetMaxHealth(500);
            AudioLevels.Set(1f, 1f, 1f, muted: true);
            elite.SetTarget(dummy.transform);
            var deadline = Time.time + 3f;
            while (Time.time < deadline && elite.State != MovesetActorState.Telegraph) yield return null;
            Assert.AreEqual(MovesetActorState.Telegraph, elite.State);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.EliteTelegraph), "Cue at the gameplay telegraph start…");
            Assert.AreEqual(0f, AudioService.GainFor(AudioBus.Enemies), "…muted here — the visual telegraph (TelegraphIndicator / animation) carries the warning.");
            Assert.AreEqual(0, service.PlayedCount(AudioEventIds.BossTelegraph), "Elites use the Elite cue, bosses the Boss cue.");
        }

        [UnityTest]
        public IEnumerator Hooks_WeaponIdentity_ReloadAndMeleeSwing_FromWeaponState()
        {
            var service = Service(_catalog);
            var binder = service.gameObject.AddComponent<GameplayAudioBinder>();
            binder.Configure(service);
            var player = new GameObject("Player");
            _created.Add(player);
            var input = new FakePlayerInputReader();
            var aiming = player.AddComponent<PlayerAiming>();
            aiming.SetInputReader(input);
            var loadout = player.AddComponent<WeaponLoadout>();
            var ranged = player.AddComponent<RangedWeapon>();
            var reserve = new AmmoReserve();
            reserve.Add(AmmoType.Light, 36);
            ranged.SetAmmoReserve(reserve);
            ranged.SetDefinition(AssetDatabase.LoadAssetAtPath<RangedWeaponDefinition>("Assets/Game/ScriptableObjects/Items/P9Ranger.asset"));
            loadout.SetPrimary(ranged);
            loadout.Initialize();
            var visuals = player.AddComponent<WeaponVisualDriver>();
            visuals.Configure(loadout);
            binder.Attach(visuals);
            yield return null;

            ranged.ApplyAuthoritativeState(ranged.MagazineAmmo - 1, false);
            visuals.Tick(0.016f);
            binder.Tick(0.016f);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.FirePistol), "P9 Ranger fires with the Pistol identity.");
            Assert.AreEqual(0, service.PlayedCount(AudioEventIds.FireSmg));
            Assert.IsTrue(ranged.TryStartReload());
            visuals.Tick(0.016f);
            binder.Tick(0.016f);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.Reload));
            binder.Tick(0.016f);
            Assert.AreEqual(1, service.PlayedCount(AudioEventIds.Reload), "Reload cue once per reload, not per frame.");
        }
    }
}
