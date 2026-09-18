using System;
using System.Collections.Generic;
using System.Text;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// Keeps the shipped player's audio attached to the output device the player is actually listening on, and makes
    /// the real output state readable.
    ///
    /// Unity opens one output device when the process starts and stays on it. If that device is not the one the player
    /// hears — because Windows' default endpoint changed after launch, because a headset was plugged in or removed, or
    /// because the device that was default at boot is not the one in use — the FMOD graph keeps mixing perfectly.
    /// Every in-engine check then passes: the listener exists, sources report <c>isPlaying</c>, mixer gains are right,
    /// and <c>AudioListener.GetOutputData</c> even carries signal, because that samples the mix and not the speakers.
    /// The player hears nothing. That is precisely the failure the previous audio pass could not see.
    ///
    /// Unity raises <see cref="AudioSettings.OnAudioConfigurationChanged"/> with <c>deviceWasChanged = true</c> for
    /// exactly this. Handling it is not optional: the engine tears the audio system down on a device change and every
    /// <see cref="AudioSource"/> stops, so without a handler the run goes permanently silent even once the right
    /// device is selected. This watchdog re-initialises the audio system onto the new configuration and asks the
    /// music/ambience beds to restart, which is the only way a persistent loop survives the event.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class AudioOutputWatchdog : MonoBehaviour
    {
        /// <summary>Raised after an output-device change has been absorbed: persistent beds must restart themselves.</summary>
        public static event Action OutputReset;

        public static AudioOutputWatchdog Current { get; private set; }

        /// <summary>How often Unity reported a configuration change since boot.</summary>
        public int ConfigurationChanges { get; private set; }
        /// <summary>How many of those were an actual output-device change.</summary>
        public int DeviceChanges { get; private set; }
        /// <summary>The last configuration Unity reported, as text (for the runtime evidence file).</summary>
        public string LastConfiguration { get; private set; } = string.Empty;

        public static AudioOutputWatchdog Ensure(Transform parent)
        {
            if (Current != null) return Current;
            var go = new GameObject("AudioOutputWatchdog");
            go.transform.SetParent(parent, false);
            return go.AddComponent<AudioOutputWatchdog>();
        }

        private void Awake()
        {
            Current = this;
            LastConfiguration = ConfigurationText();
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            // A process that booted muted or paused (a previous run's state, a headless harness) must not stay that way.
            AudioListener.pause = false;
            if (AudioListener.volume <= 0f) AudioListener.volume = 1f;
        }

        private void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (Current == this) Current = null;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            ConfigurationChanges++;
            LastConfiguration = ConfigurationText();
            if (!deviceWasChanged)
            {
                Debug.Log("[AUDIO] configuration changed without a device change: " + LastConfiguration);
                return;
            }

            DeviceChanges++;
            Debug.Log("[AUDIO] output device changed; re-initialising onto " + LastConfiguration);
            // Re-initialise onto whatever Unity now reports as the configuration; this reopens the output.
            if (!AudioSettings.Reset(AudioSettings.GetConfiguration()))
                Debug.LogWarning("[AUDIO] AudioSettings.Reset refused the new configuration; the player may stay silent until restart.");
            AudioListener.pause = false;
            if (AudioListener.volume <= 0f) AudioListener.volume = 1f;
            // Every AudioSource was stopped by the reset: the persistent beds have to be started again.
            OutputReset?.Invoke();
        }

        public static string ConfigurationText()
        {
            var config = AudioSettings.GetConfiguration();
            return $"sampleRate={config.sampleRate} speakerMode={config.speakerMode} dspBuffer={config.dspBufferSize} " +
                   $"realVoices={config.numRealVoices} virtualVoices={config.numVirtualVoices} " +
                   $"driverCapabilities={AudioSettings.driverCapabilities} outputSampleRate={AudioSettings.outputSampleRate}";
        }
    }

    /// <summary>
    /// The complete runtime audio picture, in one place, for the built player's evidence file and for the tests.
    ///
    /// The point of this type is that the previous pass proved the wrong things. Clip existence, <c>isPlaying</c> and
    /// mixer values were all true while the player heard silence, so every one of them is recorded here together with
    /// the facts that can actually explain silence: how many listeners exist and whether they are enabled, the global
    /// listener volume and pause flag, the process's focus state, the device configuration Unity opened, and the
    /// user's persisted levels. None of it proves the speakers made a sound — only a human can confirm that — but it
    /// makes every in-engine cause visible instead of assumed.
    /// </summary>
    public static class AudioRuntimeDiagnostics
    {
        public sealed class SourceLine
        {
            public string Name = string.Empty;
            public string Clip = "(none)";
            public bool IsPlaying;
            public float Volume;
            public float SpatialBlend;
            public string MixerGroup = "direct";
            public bool Mute;
            public bool Loop;

            public override string ToString() =>
                $"{Name} | clip={Clip} | isPlaying={IsPlaying} | volume={Volume:0.00} | spatialBlend={SpatialBlend:0.00} | group={MixerGroup} | mute={Mute} | loop={Loop}";
        }

        public sealed class Snapshot
        {
            public string Configuration = string.Empty;
            public int ListenerCount;
            public int EnabledListenerCount;
            public float ListenerVolume;
            public bool ListenerPaused;
            public bool ApplicationFocused;
            public bool RunInBackground;
            public bool BatchMode;
            public float Master, Music, Sfx, Ambience;
            public bool Muted;
            public int DeviceChanges;
            public readonly List<SourceLine> Sources = new();

            /// <summary>Every in-engine reason the player could be hearing nothing; empty is the healthy state.</summary>
            public List<string> Problems()
            {
                var problems = new List<string>();
                if (ListenerCount == 0) problems.Add("no AudioListener in the process");
                if (ListenerCount > 1) problems.Add($"{ListenerCount} AudioListeners (only one may exist)");
                if (EnabledListenerCount == 0 && ListenerCount > 0) problems.Add("the AudioListener is disabled");
                if (ListenerPaused) problems.Add("AudioListener.pause is on");
                if (ListenerVolume <= 0f) problems.Add("AudioListener.volume is 0");
                if (Muted) problems.Add("the user's audio settings are muted");
                if (Master <= 0f) problems.Add("master volume is 0");
                if (Music <= 0f) problems.Add("music volume is 0");
                if (Sfx <= 0f) problems.Add("sfx volume is 0");
                if (Ambience <= 0f) problems.Add("ambience gain is 0");
                return problems;
            }

            public string ToText()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# audio device / engine configuration");
                sb.AppendLine("  " + Configuration);
                sb.AppendLine($"  output-device changes handled since boot: {DeviceChanges}");
                sb.AppendLine($"  batchMode={BatchMode} applicationFocused={ApplicationFocused} runInBackground={RunInBackground}");
                sb.AppendLine("# listener");
                sb.AppendLine($"  listeners={ListenerCount} enabled={EnabledListenerCount} AudioListener.volume={ListenerVolume:0.00} AudioListener.pause={ListenerPaused}");
                sb.AppendLine("# user volume settings as the audio layer reads them");
                sb.AppendLine($"  master={Master:0.00} music={Music:0.00} sfx={Sfx:0.00} ambience={Ambience:0.00} muted={Muted}");
                sb.AppendLine($"# audio sources ({Sources.Count})");
                foreach (var source in Sources) sb.AppendLine("  " + source);
                var problems = Problems();
                sb.AppendLine(problems.Count == 0
                    ? "# no in-engine cause of silence found"
                    : "# PROBLEMS: " + string.Join("; ", problems));
                return sb.ToString();
            }
        }

        /// <summary>Reads the whole audio runtime right now. Scene search is deliberate: this is a diagnostic.</summary>
        public static Snapshot Capture()
        {
            var snapshot = new Snapshot
            {
                Configuration = AudioOutputWatchdog.ConfigurationText(),
                ListenerVolume = AudioListener.volume,
                ListenerPaused = AudioListener.pause,
                ApplicationFocused = Application.isFocused,
                RunInBackground = Application.runInBackground,
                BatchMode = Application.isBatchMode,
                Master = AudioLevels.Master,
                Music = AudioLevels.Music,
                Sfx = AudioLevels.Sfx,
                Ambience = AudioLevels.AmbienceGain,
                Muted = AudioLevels.Muted,
                DeviceChanges = AudioOutputWatchdog.Current != null ? AudioOutputWatchdog.Current.DeviceChanges : 0
            };

            var listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            snapshot.ListenerCount = listeners.Length;
            foreach (var listener in listeners) if (listener.enabled && listener.gameObject.activeInHierarchy) snapshot.EnabledListenerCount++;

            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                snapshot.Sources.Add(new SourceLine
                {
                    Name = source.name,
                    Clip = source.clip != null ? source.clip.name : "(none)",
                    IsPlaying = source.isPlaying,
                    Volume = source.volume,
                    SpatialBlend = source.spatialBlend,
                    MixerGroup = source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "direct",
                    Mute = source.mute,
                    Loop = source.loop
                });
            }

            return snapshot;
        }
    }
}
