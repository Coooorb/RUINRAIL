using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 141 — exactly 11 track roles + 6 stingers, deterministic role selection, one music state at a time with crossfade cleanup, ambience below readability, stingers from gameplay events, missing content reported.</summary>
    public class MusicRoutingTests
    {
        private readonly List<UnityEngine.Object> _created = new();

        [SetUp]
        public void SetUp() => AudioLevels.Reset();

        [TearDown]
        public void TearDown()
        {
            AudioLevels.Reset();
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private AudioClip Clip(string name)
        {
            var clip = AudioClip.Create(name, 44100, 1, 44100, false);
            _created.Add(clip);
            return clip;
        }

        private MusicCatalog FullCatalog()
        {
            var catalog = ScriptableObject.CreateInstance<MusicCatalog>();
            _created.Add(catalog);
            catalog.EnsureSlots();
            foreach (MusicRole role in Enum.GetValues(typeof(MusicRole))) catalog.SetTrack(role, Clip(role.ToString()));
            foreach (StingerRole role in Enum.GetValues(typeof(StingerRole))) catalog.SetStinger(role, Clip(role.ToString()));
            foreach (Biome biome in Enum.GetValues(typeof(Biome))) catalog.SetAmbience(biome, Clip(biome + "_amb"));
            return catalog;
        }

        private (MusicDirector director, MusicBinder binder) Rig(MusicCatalog catalog)
        {
            var go = new GameObject("Music");
            _created.Add(go);
            var director = go.AddComponent<MusicDirector>();
            director.Configure(catalog);
            var binder = go.AddComponent<MusicBinder>();
            binder.Configure(director);
            return (director, binder);
        }

        [Test]
        public void ExactlyElevenTrackRoles_SixStingers_ThreeAmbience_AndDeterministicSelection()
        {
            Assert.AreEqual(11, Enum.GetValues(typeof(MusicRole)).Length);
            Assert.AreEqual(6, Enum.GetValues(typeof(StingerRole)).Length);
            CollectionAssert.AreEqual(new[] { "Main Menu", "The Shelter", "Ruined Metro — Exploration", "Ruined Metro — Combat", "Ruined Metro — Boss", "Rustworks — Exploration", "Rustworks — Combat", "Rustworks — Boss", "Overgrown Labs — Exploration", "Overgrown Labs — Combat", "Overgrown Labs — Boss" },
                Enum.GetValues(typeof(MusicRole)).Cast<MusicRole>().Select(MusicStateResolver.DisplayName), "art/105 list, in order.");
            CollectionAssert.AreEquivalent(new[] { StingerRole.LegendaryDrop, StingerRole.EliteEncounter, StingerRole.BossDefeated, StingerRole.ExtractionSuccess, StingerRole.ExpeditionFailed, StingerRole.LevelUp }, Enum.GetValues(typeof(StingerRole)).Cast<StingerRole>());

            Assert.AreEqual(MusicRole.MainMenu, MusicStateResolver.Resolve(MusicScreen.MainMenu, Biome.Rustworks, CombatIntensity.Boss), "Screen wins over biome/intensity.");
            Assert.AreEqual(MusicRole.Shelter, MusicStateResolver.Resolve(MusicScreen.Shelter, Biome.OvergrownLabs, CombatIntensity.Combat));
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var e = MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration);
                var c = MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Combat);
                var b = MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Boss);
                Assert.AreNotEqual(e, c); Assert.AreNotEqual(c, b); Assert.AreNotEqual(e, b);
                StringAssert.StartsWith(biome == Biome.RuinedMetro ? "Ruined Metro" : biome == Biome.Rustworks ? "Rustworks" : "Overgrown Labs", MusicStateResolver.DisplayName(e));
                Assert.AreEqual(b, MusicStateResolver.BossRoleFor(biome), "Both bosses of the biome share its Boss track.");
                Assert.AreEqual(e, MusicStateResolver.Resolve(MusicScreen.Expedition, biome, CombatIntensity.Exploration), "Deterministic.");
            }

            var catalog = AssetDatabase.LoadAssetAtPath<MusicCatalog>("Assets/Game/ScriptableObjects/Audio/MusicCatalog.asset");
            Assert.IsNotNull(catalog);
            Assert.IsTrue(catalog.HasExactSlots, "11 + 6 + 3 slots, no invented count.");
            Assert.AreEqual(0, catalog.MissingTracks.Count(), "Every music role is bound to a generated track.");
            Assert.AreEqual(0, catalog.MissingStingers.Count());
            Assert.AreEqual(0, catalog.MissingAmbience.Count());
            Assert.IsTrue(catalog.IsContentComplete);
            StringAssert.Contains("machinery, steam", catalog.Ambience.First(a => a.Biome == Biome.Rustworks).Character);
        }

        [Test]
        public void Director_OneStateAtATime_CrossfadesWithoutDuplicates_AndCleansUp()
        {
            var (director, _) = Rig(FullCatalog());
            director.SetState(MusicScreen.MainMenu, Biome.RuinedMetro, CombatIntensity.Exploration);
            Assert.AreEqual(MusicRole.MainMenu, director.ActiveRole);
            Assert.IsFalse(director.IsSilent);
            Assert.AreEqual(1, director.PlayingTrackSources, "Playing: " + string.Join(", ", director.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying).Select(s => s.name + ":" + (s.clip != null ? s.clip.name : "null") + ":" + s.loop)));
            director.SetState(MusicScreen.MainMenu, Biome.OvergrownLabs, CombatIntensity.Boss);
            Assert.AreEqual(1, director.Transitions, "Same role again: no restart, no duplicate.");

            director.SetRole(MusicRole.Shelter);
            Assert.IsTrue(director.IsCrossfading);
            Assert.LessOrEqual(director.PlayingTrackSources, 2, "At most the outgoing and incoming track during a crossfade.");
            director.SetRole(MusicRole.RustworksCombat);
            director.SetRole(MusicRole.RustworksBoss);
            Assert.LessOrEqual(director.PlayingTrackSources, 2, "Rapid transitions cut the previous fade: never three tracks.");
            director.Tick(MusicDirector.CrossfadeSeconds + 0.1f);
            Assert.IsFalse(director.IsCrossfading);
            Assert.AreEqual(1, director.PlayingTrackSources, "Clean after the fade: exactly one track.");
            Assert.AreEqual(MusicRole.RustworksBoss, director.ActiveRole);

            AudioLevels.Set(0.5f, 0.5f, 1f, false);
            var playing = director.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying && s.clip != null && s.name.StartsWith("Music")).ToList();
            Assert.AreEqual(1, playing.Count);
            Assert.IsTrue(playing.All(s => Mathf.Approximately(s.volume, 0.25f)), "Music follows master × music.");
            AudioLevels.Set(1f, 1f, 1f, muted: true);
            Assert.IsTrue(director.GetComponentsInChildren<AudioSource>().Where(s => s.isPlaying && s.clip != null).All(s => s.volume == 0f), "Mute silences music and ambience.");

            director.StopAll();
            Assert.AreEqual(0, director.PlayingTrackSources);
            Assert.IsNull(director.ActiveRole);
        }

        [Test]
        public void Ambience_PerBiome_BelowCombatReadability_AndOffOutsideExpeditions()
        {
            var (director, binder) = Rig(FullCatalog());
            binder.EnterShelter();
            Assert.IsNull(director.ActiveAmbience, "No ambience in the Shelter.");
            var expedition = new ExpeditionService(_ => null, _ => null, null);
            binder.Attach(expedition);
            var profile = new PlayerProfile { SafeLoadout = new InventorySnapshot { Equipped = Array.Empty<InventorySnapshot.Entry>(), Backpack = Array.Empty<InventorySnapshot.Entry>() } };
            Assert.IsNotNull(expedition.Start(profile, 7, Biome.Rustworks));
            Assert.AreEqual(MusicRole.RustworksExploration, director.ActiveRole);
            Assert.AreEqual(Biome.Rustworks, director.ActiveAmbience);
            var ambience = director.GetComponentsInChildren<AudioSource>().First(s => s.name == "Ambience");
            Assert.IsTrue(ambience.isPlaying);
            Assert.LessOrEqual(ambience.volume, AudioService.GainFor(AudioBus.Ambience) * MusicDirector.AmbienceCeiling + 1e-5f, "Ambience stays below combat readability.");
            Assert.Less(ambience.volume, AudioService.GainFor(AudioBus.Weapons));

            binder.ObserveCombatStarted();
            Assert.AreEqual(MusicRole.RustworksCombat, director.ActiveRole);
            Assert.AreEqual(Biome.Rustworks, director.ActiveAmbience, "Ambience keeps looping through combat (not restarted).");
            binder.ObserveCombatStarted();
            binder.ObserveCombatEnded();
            Assert.AreEqual(MusicRole.RustworksCombat, director.ActiveRole, "A second active room keeps combat.");
            binder.ObserveCombatEnded();
            Assert.AreEqual(MusicRole.RustworksExploration, director.ActiveRole);

            // Depth change to another biome re-routes track and ambience; ending the run returns to the Shelter and stops ambience.
            var labsState = new ExpeditionState(7, Biome.OvergrownLabs, new PlayerInventory(_ => null, _ => null, null));
            var depthField = typeof(ExpeditionService).GetField("DepthEntered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            (depthField.GetValue(expedition) as MulticastDelegate).DynamicInvoke(labsState);
            Assert.AreEqual(MusicRole.OvergrownLabsExploration, director.ActiveRole);
            Assert.AreEqual(Biome.OvergrownLabs, director.ActiveAmbience);
            expedition.Fail();
            Assert.AreEqual(MusicRole.Shelter, director.ActiveRole);
            Assert.IsNull(director.ActiveAmbience);
            Assert.AreEqual(StingerRole.ExpeditionFailed, director.LastStinger);
        }

        [Test]
        public void Stingers_FromLevelUpBossDefeatExtractionAndLegendaryDrop_NeverChangeTheTrackState()
        {
            var (director, binder) = Rig(FullCatalog());
            var profile = new PlayerProfile();
            var progression = new ProgressionService(profile);
            binder.Attach(progression);
            progression.AddXp(100000);
            Assert.AreEqual(StingerRole.LevelUp, director.LastStinger);
            Assert.Greater(director.StingersPlayed, 0);

            var expedition = new ExpeditionService(_ => null, _ => null, null);
            binder.Attach(expedition);
            var runProfile = new PlayerProfile { SafeLoadout = new InventorySnapshot { Equipped = Array.Empty<InventorySnapshot.Entry>(), Backpack = Array.Empty<InventorySnapshot.Entry>() } };
            Assert.IsNotNull(expedition.Start(runProfile, 3, Biome.RuinedMetro));
            var before = director.Transitions;
            director.PlayStinger(StingerRole.EliteEncounter);
            director.PlayStinger(StingerRole.BossDefeated);
            Assert.AreEqual(before, director.Transitions, "Stingers never change the music state.");
            Assert.AreEqual(MusicRole.RuinedMetroExploration, director.ActiveRole);

            var pickupGo = new GameObject("Drop");
            _created.Add(pickupGo);
            var pickup = pickupGo.AddComponent<RuinRail.Gameplay.Loot.WorldItemPickup>();
            var stingers = director.StingersPlayed;
            binder.Attach(pickup);
            Assert.AreEqual(stingers, director.StingersPlayed, "No item / no rarity: no stinger.");

            expedition.Return();
            Assert.AreEqual(StingerRole.ExtractionSuccess, director.LastStinger);
            Assert.AreEqual(MusicRole.Shelter, director.ActiveRole);

            // Missing clips: the state machine keeps routing and reports silence rather than failing.
            var empty = ScriptableObject.CreateInstance<MusicCatalog>();
            _created.Add(empty);
            empty.EnsureSlots();
            var (silent, _) = Rig(empty);
            silent.SetRole(MusicRole.OvergrownLabsBoss);
            Assert.AreEqual(MusicRole.OvergrownLabsBoss, silent.ActiveRole);
            Assert.IsTrue(silent.IsSilent);
            Assert.AreEqual(0, silent.PlayingTrackSources);
            Assert.DoesNotThrow(() => silent.PlayStinger(StingerRole.LegendaryDrop));
            Assert.DoesNotThrow(() => silent.SetAmbience(Biome.Rustworks));
        }
    }
}
