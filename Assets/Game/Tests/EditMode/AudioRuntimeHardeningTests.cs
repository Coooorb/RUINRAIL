using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Audio;
using RuinRail.Core.Rendering;
using RuinRail.Persistence;
using RuinRail.UI.Settings;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The audio runtime's silence causes, as rules rather than as claims.
    ///
    /// The previous pass proved clips existed, sources reported <c>isPlaying</c> and mixer gains were right, and the
    /// player still heard nothing — so these tests deliberately cover the things those checks could not see: a
    /// settings document that deserializes a missing volume key as 0, a process that boots muted or paused, the
    /// ambience bed's gain (which has no slider of its own), and the output-device change that stops every source in
    /// the process. None of this can prove a speaker made a sound; that remains a human check, and the report says so.
    /// </summary>
    public sealed class AudioRuntimeHardeningTests
    {
        [TearDown]
        public void TearDown() => AudioLevels.Reset();

        // ---- Fresh / migrated settings ----

        [Test]
        public void AFreshProfile_StartsAudible_OnEveryChannel()
        {
            var defaults = SettingsData.Defaults();
            Assert.Greater(defaults.Audio.MasterVolume, 0f);
            Assert.Greater(defaults.Audio.MusicVolume, 0f);
            Assert.Greater(defaults.Audio.SfxVolume, 0f);
            Assert.IsFalse(defaults.Audio.Mute);
            SettingsViewModel.PublishAudio(defaults);
            Assert.Greater(AudioLevels.Master, 0f);
            Assert.Greater(AudioLevels.Music, 0f);
            Assert.Greater(AudioLevels.Sfx, 0f);
            Assert.Greater(AudioLevels.AmbienceGain, 0f, "a fresh profile is audible on the ambience bus too (its own AUDIO-page level defaults to 100%)");
            Assert.IsFalse(AudioLevels.Muted);
        }

        [Test]
        public void ASettingsDocumentMissingItsVolumeKeys_IsRestoredToTheDefaults_NotLeftAtZero()
        {
            // An older document, or a truncated one: JsonUtility leaves an absent float at 0, which is
            // indistinguishable from "turned all the way down" and boots the game silent.
            const string legacy = "{\"SettingsVersion\":1,\"Audio\":{\"Mute\":false},\"Video\":{},\"Controls\":{},\"Accessibility\":{},\"Tutorial\":{}}";
            var service = new UserSettingsService(new MemorySaveStore { Document = legacy });
            var loaded = service.Load();
            Assert.AreEqual(1f, loaded.Audio.MasterVolume, 0.0001f);
            Assert.AreEqual(1f, loaded.Audio.MusicVolume, 0.0001f);
            Assert.AreEqual(1f, loaded.Audio.SfxVolume, 0.0001f);
            Assert.IsTrue(service.LastDiagnostics.Entries.Any(e => e.Code == "settings.audio.migrated"), "the migration is reported, not silent");
        }

        [Test]
        public void AnExplicitZero_IsThePlayersOwnChoice_AndIsNeverOverridden()
        {
            const string muted = "{\"SettingsVersion\":1,\"Audio\":{\"MasterVolume\":0,\"MusicVolume\":0,\"SfxVolume\":0.5,\"Mute\":true}}";
            var service = new UserSettingsService(new MemorySaveStore { Document = muted });
            var loaded = service.Load();
            Assert.AreEqual(0f, loaded.Audio.MasterVolume, 0.0001f, "a value the player set is preserved");
            Assert.AreEqual(0f, loaded.Audio.MusicVolume, 0.0001f);
            Assert.AreEqual(0.5f, loaded.Audio.SfxVolume, 0.0001f);
            Assert.IsTrue(loaded.Audio.Mute);
            Assert.IsFalse(service.LastDiagnostics.Entries.Any(e => e.Code == "settings.audio.migrated"));
        }

        [Test]
        public void ADocumentWithNoAudioBlockAtAll_BootsAudible()
        {
            var service = new UserSettingsService(new MemorySaveStore { Document = "{\"SettingsVersion\":1}" });
            var loaded = service.Load();
            SettingsViewModel.PublishAudio(loaded);
            Assert.Greater(AudioLevels.GainFor(true), 0f, "music is audible");
            Assert.Greater(AudioLevels.GainFor(false), 0f, "sfx is audible");
            Assert.Greater(AudioLevels.AmbienceGain, 0f);
        }

        // ---- Gain rules ----

        [Test]
        public void AmbienceStaysBelowCombatReadability_AndFollowsMuteAndMaster()
        {
            AudioLevels.Set(1f, 1f, 1f, false);
            Assert.AreEqual(AudioLevels.AmbienceCeiling, AudioLevels.AmbienceGain, 0.0001f);
            Assert.Less(AudioLevels.AmbienceGain, AudioLevels.GainFor(false), "ambience sits under the SFX bed (art/105)");
            Assert.AreEqual(MusicDirector.AmbienceCeiling, AudioLevels.AmbienceCeiling, "one ceiling, one place");
            AudioLevels.Set(0.5f, 1f, 1f, false);
            Assert.AreEqual(0.5f * AudioLevels.AmbienceCeiling, AudioLevels.AmbienceGain, 0.0001f, "master scales it");
            AudioLevels.Set(1f, 1f, 1f, true);
            Assert.AreEqual(0f, AudioLevels.AmbienceGain, 0.0001f, "mute silences it");
        }

        // ---- The runtime picture ----

        [Test]
        public void TheDiagnosticSnapshot_NamesEveryInEngineCauseOfSilence()
        {
            AudioLevels.Set(1f, 1f, 1f, false);
            var healthy = new AudioRuntimeDiagnostics.Snapshot
            {
                ListenerCount = 1, EnabledListenerCount = 1, ListenerVolume = 1f, ListenerPaused = false,
                Master = 1f, Music = 1f, Sfx = 1f, Ambience = AudioLevels.AmbienceGain, Muted = false
            };
            CollectionAssert.IsEmpty(healthy.Problems());

            void Expect(System.Action<AudioRuntimeDiagnostics.Snapshot> break_, string fragment)
            {
                var broken = new AudioRuntimeDiagnostics.Snapshot
                {
                    ListenerCount = 1, EnabledListenerCount = 1, ListenerVolume = 1f,
                    Master = 1f, Music = 1f, Sfx = 1f, Ambience = 0.4f
                };
                break_(broken);
                var problems = broken.Problems();
                Assert.IsTrue(problems.Any(p => p.Contains(fragment)), $"expected a problem mentioning '{fragment}', got: {string.Join("; ", problems)}");
            }

            Expect(s => s.ListenerCount = 0, "no AudioListener");
            Expect(s => s.ListenerCount = 2, "AudioListeners");
            Expect(s => s.EnabledListenerCount = 0, "disabled");
            Expect(s => s.ListenerPaused = true, "AudioListener.pause");
            Expect(s => s.ListenerVolume = 0f, "AudioListener.volume");
            Expect(s => s.Muted = true, "muted");
            Expect(s => s.Master = 0f, "master volume");
            Expect(s => s.Music = 0f, "music volume");
            Expect(s => s.Sfx = 0f, "sfx volume");
            Expect(s => s.Ambience = 0f, "ambience gain");

            var text = healthy.ToText();
            StringAssert.Contains("AudioListener.volume", text);
            StringAssert.Contains("ambience", text);
            StringAssert.Contains("no in-engine cause of silence", text);
        }

        [Test]
        public void AnOutputDeviceChange_RestartsTheMusicAndAmbienceBeds()
        {
            // A device change tears the audio system down and stops every source. Without a restart the run goes
            // permanently silent even though the state machine still believes the bed is playing.
            var go = new GameObject("MusicDirectorTest");
            try
            {
                var catalog = ScriptableObject.CreateInstance<MusicCatalog>();
                catalog.EnsureSlots();
                var clip = AudioClip.Create("bed", 4410, 1, 44100, false);
                catalog.SetTrack(MusicRole.MainMenu, clip);
                catalog.SetAmbience(RuinRail.Core.Biome.RuinedMetro, clip);
                var director = go.AddComponent<MusicDirector>();
                director.Configure(catalog);
                director.SetRole(MusicRole.MainMenu);
                director.SetAmbience(RuinRail.Core.Biome.RuinedMetro);
                var sources = go.GetComponentsInChildren<AudioSource>(true);
                Assert.AreEqual(4, sources.Length, "two beds, a stinger and the ambience");
                // Exactly what AudioSettings.Reset does to every source in the process.
                foreach (var source in sources) { source.Stop(); source.volume = 0f; }

                Assert.AreEqual(0, director.OutputResets);
                director.OnOutputReset();
                Assert.AreEqual(1, director.OutputResets);
                var track = sources.First(s => s.name == "MusicA" || s.name == "MusicB");
                var ambience = sources.First(s => s.name == "Ambience");
                Assert.IsNotNull(track.clip);
                Assert.Greater(track.volume, 0f, "the bed came back at its proper gain");
                Assert.IsNotNull(ambience.clip);
                Assert.Greater(ambience.volume, 0f);
                Assert.AreEqual(MusicRole.MainMenu, director.ActiveRole, "the state machine never changed");
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(clip);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TheWatchdogIsPartOfTheShippedComposition_AndIsTheOnlyPlaceThatResetsTheAudioSystem()
        {
            var boot = File.ReadAllText("Assets/Game/Scripts/App/GameApp.cs");
            StringAssert.Contains("AudioOutputWatchdog.Ensure", boot, "the shipped player composes the output watchdog");
            var offenders = Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Editor/"))
                .Where(p => Path.GetFileName(p) != "AudioOutputWatchdog.cs")
                .Where(p => File.ReadAllText(p).Contains("AudioSettings.Reset"))
                .ToList();
            CollectionAssert.IsEmpty(offenders, "one seam owns re-initialising the audio device");
        }

        [Test]
        public void TheShippedPlayerKeepsRunning_WhenItsWindowLosesFocus()
        {
            // With Run In Background off, Unity pauses the player the moment the window is not focused and the whole
            // mix goes silent — a player who alt-tabs, or whose window never took focus on launch, simply hears
            // nothing, while every in-engine check taken from inside the process still passes.
            Assert.IsTrue(UnityEditor.PlayerSettings.runInBackground, "a windowed run must not go silent when it loses focus");
            Assert.IsTrue(UnityEditor.PlayerSettings.visibleInBackground);
        }

        [Test]
        public void NoReleaseScript_SilencesTheProcessGlobally()
        {
            // AudioListener.volume and AudioListener.pause are process-wide: setting them anywhere but the two seams
            // that deliberately restore them is how a build ends up silent with every other check passing.
            var allowed = new[] { "AudioOutputWatchdog.cs", "AudioListenerRig.cs", "SettingsViewModel.cs" };
            var offenders = Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Editor/"))
                .Where(p => !allowed.Contains(Path.GetFileName(p)))
                .Where(p =>
                {
                    var source = File.ReadAllText(p);
                    return source.Contains("AudioListener.volume =") || source.Contains("AudioListener.pause =");
                })
                .Select(p => p.Replace('\\', '/'))
                .ToList();
            CollectionAssert.IsEmpty(offenders);
        }
    }
}
