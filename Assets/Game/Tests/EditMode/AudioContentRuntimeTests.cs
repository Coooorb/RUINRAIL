using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Persistence;
using RuinRail.UI.Settings;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// The 73 authored audio roles resolve on the release path (the shipped GameContentCatalog, not a search of the
    /// project) and each clip's source samples are non-trivial and non-silent; fresh-profile audio defaults are
    /// audible, an explicit user mute survives a reload, and a settings document without audio keys lands on the
    /// documented defaults instead of zero.
    /// </summary>
    public sealed class AudioContentRuntimeTests
    {
        public const int SfxRoles = 53;
        public const int MusicRoles = 11;
        public const int StingerRoles = 6;
        public const int AmbienceRoles = 3;
        public const int TotalRoles = SfxRoles + MusicRoles + StingerRoles + AmbienceRoles; // 73

        [TearDown]
        public void TearDown() => AudioLevels.Reset();

        private static IEnumerable<(string role, AudioClip clip)> ReleaseClips(GameContentCatalog catalog)
        {
            foreach (var (id, _, _) in AudioEventIds.Required)
            {
                Assert.IsTrue(catalog.AudioEvents.TryGet(id, out var definition), id);
                Assert.IsTrue(definition.HasClips, id + " has a clip");
                foreach (var clip in definition.Clips.Where(c => c != null)) yield return (id, clip);
            }

            foreach (MusicRole role in Enum.GetValues(typeof(MusicRole))) yield return ("music." + role, catalog.Music.TrackFor(role));
            foreach (StingerRole role in Enum.GetValues(typeof(StingerRole))) yield return ("stinger." + role, catalog.Music.StingerFor(role));
            foreach (Biome biome in Enum.GetValues(typeof(Biome))) yield return ("ambience." + biome, catalog.Music.AmbienceFor(biome));
        }

        [Test]
        public void All73Roles_ResolveOnTheReleasePath_ToDistinctClips()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog.AudioEvents, "AudioEventCatalog referenced by the shipped catalog");
            Assert.IsNotNull(catalog.Music, "MusicCatalog referenced by the shipped catalog");
            var clips = ReleaseClips(catalog).ToList();
            Assert.AreEqual(SfxRoles, AudioEventIds.Required.Count);
            Assert.AreEqual(TotalRoles, clips.Count, "53 SFX + 11 tracks + 6 stingers + 3 ambience loops");
            Assert.IsTrue(clips.All(c => c.clip != null), string.Join(", ", clips.Where(c => c.clip == null).Select(c => c.role)));
            Assert.AreEqual(TotalRoles, clips.Select(c => c.clip).Distinct().Count(), "no two roles share one clip");
            Assert.IsTrue(catalog.Music.IsContentComplete && catalog.Music.HasExactSlots);
        }

        [Test]
        public void All73Clips_ContainNonTrivialNonSilentSamples()
        {
            var catalog = GameContentCatalog.Load();
            var failures = new List<string>();
            var count = 0;
            foreach (var (role, clip) in ReleaseClips(catalog))
            {
                count++;
                var path = AssetDatabase.GetAssetPath(clip);
                Assert.IsTrue(File.Exists(path), role + " source file exists: " + path);
                var (seconds, peak, rms) = WavStats(path);
                if (seconds < 0.05f) failures.Add($"{role}: {seconds:0.00}s is trivial");
                if (peak < 0.05f) failures.Add($"{role}: peak {peak:0.000} is silent");
                if (rms < 0.005f) failures.Add($"{role}: rms {rms:0.0000} is effectively silent");
                Assert.Greater(clip.samples, 0, role);
                Assert.Greater(clip.length, 0.05f, role);
                Assert.IsTrue(clip.loadType != AudioClipLoadType.Streaming || clip.length > 0f, role);
            }

            Assert.AreEqual(TotalRoles, count);
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>PCM16 WAV statistics of the whole file (duration, absolute peak 0..1, RMS 0..1).</summary>
        private static (float seconds, float peak, float rms) WavStats(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var channels = BitConverter.ToInt16(bytes, 22);
            var sampleRate = BitConverter.ToInt32(bytes, 24);
            var bits = BitConverter.ToInt16(bytes, 34);
            Assert.AreEqual(16, bits, path + " is PCM16");
            var offset = 12;
            var dataOffset = -1;
            var dataLength = 0;
            while (offset + 8 <= bytes.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                var length = BitConverter.ToInt32(bytes, offset + 4);
                if (id == "data") { dataOffset = offset + 8; dataLength = Math.Min(length, bytes.Length - dataOffset); break; }
                offset += 8 + length;
            }

            Assert.GreaterOrEqual(dataOffset, 0, path + " has a data chunk");
            var samples = dataLength / 2;
            var peak = 0;
            double sum = 0;
            for (var i = 0; i < samples; i++)
            {
                var v = BitConverter.ToInt16(bytes, dataOffset + i * 2);
                var a = Math.Abs((int)v);
                if (a > peak) peak = a;
                sum += (double)v * v;
            }

            var rms = samples > 0 ? Math.Sqrt(sum / samples) / 32768.0 : 0.0;
            return (samples / (float)(sampleRate * channels), peak / 32768f, (float)rms);
        }

        [Test]
        public void FreshProfileAudioDefaults_AreAudible_AndTheGainsReachEveryBus()
        {
            var defaults = SettingsData.Defaults();
            Assert.Greater(defaults.Audio.MasterVolume, 0f);
            Assert.Greater(defaults.Audio.MusicVolume, 0f);
            Assert.Greater(defaults.Audio.SfxVolume, 0f);
            Assert.IsFalse(defaults.Audio.Mute);
            SettingsViewModel.PublishAudio(defaults);
            Assert.Greater(AudioService.GainFor(AudioBus.Music), 0f);
            Assert.Greater(AudioService.GainFor(AudioBus.Weapons), 0f);
            Assert.Greater(AudioService.GainFor(AudioBus.UI), 0f);
            Assert.Greater(MusicDirector.AmbienceGain(), 0f, "ambience is audible at the defaults (below the SFX gain)");
            Assert.LessOrEqual(MusicDirector.AmbienceGain(), AudioService.GainFor(AudioBus.Ambience) * MusicDirector.AmbienceCeiling + 1e-5f);
        }

        [Test]
        public void SettingsDocumentWithoutAudioKeys_LoadsTheDocumentedDefaults_NotZero()
        {
            var store = new MemorySaveStore { Document = "{\"SettingsVersion\":1,\"Video\":{\"Fullscreen\":false,\"VSync\":true,\"ResolutionWidth\":0,\"ResolutionHeight\":0}}" };
            var service = new UserSettingsService(store);
            var loaded = service.Load();
            Assert.AreEqual(1f, loaded.Audio.MasterVolume);
            Assert.AreEqual(1f, loaded.Audio.MusicVolume);
            Assert.AreEqual(1f, loaded.Audio.SfxVolume);
            Assert.IsFalse(loaded.Audio.Mute);
            Assert.IsFalse(loaded.Video.Fullscreen, "the keys that were present are kept");
        }

        [Test]
        public void ExplicitUserMute_AndLevels_ArePreservedThroughSaveAndReload()
        {
            var store = new MemorySaveStore();
            var service = new UserSettingsService(store);
            var data = service.Load();
            data.Audio.Mute = true;
            data.Audio.MasterVolume = 0.4f;
            data.Audio.MusicVolume = 0f; // an explicit zero is a user choice, never "uninitialised"
            Assert.AreEqual(SaveError.None, service.Save());
            var reloaded = new UserSettingsService(store).Load();
            Assert.IsTrue(reloaded.Audio.Mute);
            Assert.AreEqual(0.4f, reloaded.Audio.MasterVolume, 1e-5f);
            Assert.AreEqual(0f, reloaded.Audio.MusicVolume);
            SettingsViewModel.PublishAudio(reloaded);
            Assert.AreEqual(0f, AudioService.GainFor(AudioBus.Music), "muted stays muted");
            Assert.AreEqual(0f, AudioService.GainFor(AudioBus.Weapons));
        }

        [Test]
        public void MasterVolume_IsAppliedExactlyOnce_ThroughTheGains_NotAlsoOnTheListener()
        {
            var data = SettingsData.Defaults();
            data.Audio.MasterVolume = 0.5f;
            data.Audio.SfxVolume = 1f;
            SettingsViewModel.PublishAudio(data);
            new UnitySettingsApplier().Apply(data);
            Assert.AreEqual(0.5f, AudioService.GainFor(AudioBus.Weapons), 1e-5f);
            Assert.AreEqual(1f, AudioListener.volume, 1e-5f, "0.5 master must not play at 0.25");
        }

        [Test]
        public void NoReleaseScript_PausesTheListener_OrZeroesItsVolume_OrAddsASecondListener()
        {
            var offenders = new List<string>();
            foreach (var path in Directory.GetFiles("Assets/Game/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                if (path.Replace('\\', '/').Contains("/Editor/")) continue;
                var text = File.ReadAllText(path);
                if (text.Contains("AudioListener.pause = true")) offenders.Add(path + ": pauses the listener");
                if (text.Contains("AudioListener.volume = 0")) offenders.Add(path + ": zeroes the listener");
                if (text.Contains("AddComponent<AudioListener>") && !path.EndsWith("AudioListenerRig.cs")) offenders.Add(path + ": adds a listener outside the rig");
            }

            Assert.IsEmpty(offenders, string.Join("\n", offenders));
        }

        [Test]
        public void Spatializer_KeepsRoomScaleSoundsAudible_AndPansLightly()
        {
            var listener = Vector2.zero;
            Assert.AreEqual(1f, Spatializer.Gain(new Vector2(3f, 2f), listener));
            Assert.AreEqual(1f, Spatializer.Gain(new Vector2(Spatializer.FullRadius, 0f), listener), 1e-5f);
            Assert.That(Spatializer.Gain(new Vector2(12f, 0f), listener), Is.InRange(Spatializer.Floor, 1f).And.LessThan(1f));
            Assert.AreEqual(Spatializer.Floor, Spatializer.Gain(new Vector2(40f, 0f), listener), 1e-5f, "never below the floor: an event across the room is still heard");
            Assert.AreEqual(0f, Spatializer.Pan(new Vector2(0f, 5f), listener), 1e-5f);
            Assert.Greater(Spatializer.Pan(new Vector2(4f, 0f), listener), 0f);
            Assert.Less(Spatializer.Pan(new Vector2(-4f, 0f), listener), 0f);
            Assert.AreEqual(Spatializer.MaxPan, Spatializer.Pan(new Vector2(100f, 0f), listener), 1e-5f);
        }
    }
}
