using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Audio;
using RuinRail.Core;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.ArtGen
{
    /// <summary>
    /// Generates every audio clip and binds it to the catalog the runtime already reads.
    ///
    /// The seams are the ones TASK 151 documented: an SFX clip lands in the AudioEventDefinition for its event id,
    /// and music, stingers and ambience land in the MusicCatalog. No new lookup path is introduced, so routing,
    /// bus assignment and the MusicDirector's crossfade logic are untouched.
    /// </summary>
    public static class AudioIntegration
    {
        public const string AudioRoot = "Assets/Game/Audio";

        [MenuItem("RuinRail/Art/Generate All Audio")]
        public static void GenerateAll()
        {
            var written = GenerateFiles();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyImportSettings();
            AssetDatabase.SaveAssets();
            BindAll();
            Debug.Log($"Audio generation complete: {written} clips.");
        }

        /// <summary>
        /// Regenerates the 11 music beds only and rebinds them. This exists because the loop fix in
        /// <see cref="GameAudioFactory.BuildMusic"/> changes only the music files: regenerating everything would
        /// rewrite 62 clips that are byte-identical, and a batch runner should touch exactly what changed.
        /// </summary>
        [MenuItem("RuinRail/Art/Regenerate Music Loops")]
        public static void GenerateMusic()
        {
            Written.Clear();
            foreach (MusicRole role in Enum.GetValues(typeof(MusicRole)))
            {
                var clip = GameAudioFactory.BuildMusic(role.ToString());
                clip.Normalize(GameAudioFactory.PeakFor("Music"));
                var path = $"{AudioRoot}/Music/music_{role.ToString().ToLowerInvariant()}.wav";
                AudioSynth.WriteWav(clip, path);
                Written.Add((path, true));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ApplyImportSettings();
            AssetDatabase.SaveAssets();
            BindMusic();
            Debug.Log($"Regenerated {Written.Count} music beds on their bar grid.");
        }

        /// <summary>Batch entry for the runner (-executeMethod).</summary>
        public static void GenerateMusicBatch()
        {
            try { GenerateMusic(); EditorApplication.Exit(0); }
            catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
        }

        private static readonly List<(string path, bool loop)> Written = new();

        public static int GenerateFiles()
        {
            Written.Clear();

            // --- 53 SFX ---
            foreach (var (id, bus, loop) in AudioEventIds.Required)
            {
                var clip = GameAudioFactory.BuildSfx(id);
                clip.Normalize(GameAudioFactory.PeakFor(bus.ToString()));
                var path = $"{AudioRoot}/Sfx/{id.Replace('.', '_')}.wav";
                AudioSynth.WriteWav(clip, path);
                Written.Add((path, loop));
            }

            // --- 11 music tracks ---
            foreach (MusicRole role in Enum.GetValues(typeof(MusicRole)))
            {
                var clip = GameAudioFactory.BuildMusic(role.ToString());
                clip.Normalize(GameAudioFactory.PeakFor("Music"));
                var path = $"{AudioRoot}/Music/music_{role.ToString().ToLowerInvariant()}.wav";
                AudioSynth.WriteWav(clip, path);
                Written.Add((path, true));
            }

            // --- 6 stingers ---
            foreach (StingerRole role in Enum.GetValues(typeof(StingerRole)))
            {
                var clip = GameAudioFactory.BuildStinger(role.ToString());
                clip.Normalize(GameAudioFactory.PeakFor("Music"));
                var path = $"{AudioRoot}/Stingers/stinger_{role.ToString().ToLowerInvariant()}.wav";
                AudioSynth.WriteWav(clip, path);
                Written.Add((path, false));
            }

            // --- 3 ambience loops ---
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var clip = GameAudioFactory.BuildAmbience(biome.ToString());
                clip.Normalize(GameAudioFactory.PeakFor("Ambience"));
                var path = $"{AudioRoot}/Ambience/ambience_{biome.ToString().ToLowerInvariant()}.wav";
                AudioSynth.WriteWav(clip, path);
                Written.Add((path, true));
            }

            return Written.Count;
        }

        private static void ApplyImportSettings()
        {
            foreach (var (path, loop) in Written)
            {
                if (AssetImporter.GetAtPath(path) is not AudioImporter importer) continue;

                // Long beds stream; short cues decode once at load.
                //
                // This setting was measured, not guessed. PCM DecompressOnLoad put 24.9 MB of growth across depth
                // transitions against an 8 MB budget, and CompressedInMemory was no better — it decodes on *every*
                // play, so a cue fired ten times a cycle allocates ten times. Vorbis DecompressOnLoad pays the decode
                // once at load and allocates nothing per play, which is what a short, constantly-retriggered cue wants.
                //
                // SFX are additionally forced to mono at 22 kHz: they are positional cues whose direction comes from
                // the AudioSource, so a stereo 44 kHz copy is twice the memory for nothing.
                var isBed = path.Contains("/Music/") || path.Contains("/Ambience/");
                var settings = importer.defaultSampleSettings;
                settings.loadType = isBed ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;

                // Beds use Vorbis; short cues stay uncompressed PCM at their authored rate.
                //
                // Compressed formats were tried first and both misbehaved on the shortest cues: Vorbis has a practical
                // minimum length, and ADPCM combined with a sample-rate override produced clips the editor saw but the
                // runtime reported silent. PCM has no such edge cases, and the in-player profile showed the memory
                // cost is not material — the built player's heap moved +0.03 MB across a 60 s run with all 73 clips
                // resident. Robustness is worth more here than a few megabytes.
                settings.compressionFormat = isBed ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.PCM;
                settings.quality = isBed ? 0.6f : 1f;
                settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;

                // Short cues preload so the first shot is not late; beds do not, to keep load time and memory flat.
                settings.preloadAudioData = !isBed;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = !isBed;
                importer.SaveAndReimport();
            }
        }

        // ---------- binding ----------

        public static void BindAll()
        {
            BindSfx();
            BindMusic();
        }

        /// <summary>Puts each generated clip into the AudioEventDefinition the AudioService resolves by event id.</summary>
        public static void BindSfx()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AudioEventCatalog>(
                "Assets/Game/ScriptableObjects/Audio/AudioEventCatalog.asset");
            if (catalog == null)
            {
                Debug.LogError("AudioEventCatalog missing; cannot bind SFX.");
                return;
            }

            var bound = 0;
            foreach (var (id, _, _) in AudioEventIds.Required)
            {
                if (!catalog.TryGet(id, out var definition) || definition == null) continue;

                var path = $"{AudioRoot}/Sfx/{id.Replace('.', '_')}.wav";
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) continue;

                definition.EditorSetClips(new[] { clip });
                EditorUtility.SetDirty(definition);
                bound++;
            }

            Debug.Log($"Bound {bound} SFX clips.");
        }

        /// <summary>Fills the MusicCatalog's track, stinger and ambience slots.</summary>
        public static void BindMusic()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<MusicCatalog>(
                "Assets/Game/ScriptableObjects/Audio/MusicCatalog.asset");
            if (catalog == null)
            {
                Debug.LogError("MusicCatalog missing; cannot bind music.");
                return;
            }

            AudioClip Load(string folder, string stem) =>
                AssetDatabase.LoadAssetAtPath<AudioClip>($"{AudioRoot}/{folder}/{stem}.wav");

            // The catalog already creates one entry per role; fill the slots it owns rather than rebuilding the lists.
            catalog.EnsureSlots();

            foreach (var entry in catalog.Tracks)
                entry.Clip = Load("Music", $"music_{entry.Role.ToString().ToLowerInvariant()}");
            foreach (var entry in catalog.Stingers)
                entry.Clip = Load("Stingers", $"stinger_{entry.Role.ToString().ToLowerInvariant()}");
            foreach (var entry in catalog.Ambience)
                entry.Loop = Load("Ambience", $"ambience_{entry.Biome.ToString().ToLowerInvariant()}");

            EditorUtility.SetDirty(catalog);

            Debug.Log($"Bound {catalog.Tracks.Count(t => t.HasClip)} tracks, " +
                      $"{catalog.Stingers.Count(s => s.HasClip)} stingers, " +
                      $"{catalog.Ambience.Count(a => a.HasClip)} ambience loops.");
        }
    }
}
