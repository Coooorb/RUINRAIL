using System;
using System.Collections.Generic;
using RuinRail.Core.Rendering;
using UnityEngine;
using UnityEngine.Audio;

namespace RuinRail.Audio
{
    /// <summary>Handle for a running loop; stopping is idempotent.</summary>
    public sealed class LoopHandle
    {
        internal LoopHandle(string id, AudioSource source, Transform follow, float baseVolume) { Id = id; Source = source; Follow = follow; Follows = follow != null; BaseVolume = baseVolume; }
        public string Id { get; }
        internal AudioSource Source { get; }
        internal Transform Follow { get; }
        /// <summary>True for a loop bound to a world object: it ends when that object disappears.</summary>
        internal bool Follows { get; }
        internal float BaseVolume { get; set; }
        public bool IsPlaying { get; internal set; } = true;
    }

    /// <summary>
    /// How a positioned sound is heard in a top-down 2D game: no 3D rolloff (the camera sits ten units behind the
    /// world plane, which would make every sound distant) — a flat, listener-relative attenuation in the world plane
    /// with a floor so room-scale events stay audible, plus a light stereo pan from the horizontal offset.
    /// </summary>
    public static class Spatializer
    {
        /// <summary>Within this many tiles of the listener a sound plays at full gain.</summary>
        public const float FullRadius = 6f;
        /// <summary>At this distance the gain has fallen to <see cref="Floor"/>; it never falls below it.</summary>
        public const float FadeRadius = 18f;
        public const float Floor = 0.35f;
        public const float PanRadius = 10f;
        public const float MaxPan = 0.5f;

        public static float Gain(Vector2 source, Vector2 listener)
        {
            var distance = Vector2.Distance(source, listener);
            if (distance <= FullRadius) return 1f;
            var t = Mathf.Clamp01((distance - FullRadius) / (FadeRadius - FullRadius));
            return Mathf.Lerp(1f, Floor, t);
        }

        public static float Pan(Vector2 source, Vector2 listener) => Mathf.Clamp((source.x - listener.x) / PanRadius, -MaxPan, MaxPan);
    }

    /// <summary>
    /// Pooled one-shots and tracked loops over an optional AudioMixer (groups looked up by bus name) with bus gains
    /// from <see cref="AudioLevels"/> (master/music/sfx/mute from Settings). Fixed pool: the oldest one-shot is
    /// stolen when every source is busy, so high-frequency events never allocate unbounded AudioSources. A missing
    /// event or a definition without clips plays fallback silence and is counted — never an exception. Every source
    /// is a 2D source; positioned events are attenuated and panned by <see cref="Spatializer"/> relative to the
    /// process listener (<see cref="AudioListenerRig"/>).
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        public const int DefaultOneShotSources = 24;

        [SerializeField] private AudioEventCatalog _catalog;
        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private int _oneShotSources = DefaultOneShotSources;

        private sealed class Voice
        {
            public AudioSource Source;
            public string Id;
            public float StartedAt;
            public float SpatialGain = 1f;
            public AudioBus Bus;
            public float Volume;
        }

        private readonly List<AudioSource> _oneShots = new();
        private readonly List<Voice> _busy = new();
        private readonly List<LoopHandle> _loops = new();
        private readonly Dictionary<string, float> _lastStart = new();
        private readonly Dictionary<string, int> _played = new();
        private readonly HashSet<string> _silent = new();
        private readonly HashSet<string> _unknown = new();
        private readonly Dictionary<AudioBus, AudioMixerGroup> _groups = new();
        private float _clock;

        public AudioEventCatalog Catalog => _catalog;
        public int OneShotSources => _oneShots.Count;
        public int BusyOneShots => _busy.Count;
        public int ActiveLoops => _loops.Count;
        public int Stolen { get; private set; }
        public int Throttled { get; private set; }
        public int Capped { get; private set; }
        public IReadOnlyCollection<string> SilentEvents => _silent;
        public IReadOnlyCollection<string> UnknownEvents => _unknown;
        public int PlayedCount(string id) => _played.TryGetValue(id, out var n) ? n : 0;
        public int TotalPlayed { get; private set; }

        /// <summary>The last voice that actually started (id, clip, source, volume) — runtime proof for the audio evidence.</summary>
        public AudioSource LastStartedSource { get; private set; }
        public string LastStartedId { get; private set; } = string.Empty;

        /// <summary>Raised whenever a voice actually starts playing (id, source): the evidence recorder listens.</summary>
        public event Action<string, AudioSource> VoiceStarted;

        public void Configure(AudioEventCatalog catalog, AudioMixer mixer = null, int oneShotSources = DefaultOneShotSources)
        {
            _catalog = catalog;
            _mixer = mixer;
            _oneShotSources = Mathf.Max(1, oneShotSources);
            EnsurePool();
            ResolveGroups();
        }

        private void Awake()
        {
            // The pool is built lazily (Configure or first Play) so a configured size is never pre-empted by the default.
            ResolveGroups();
            AudioLevels.Changed += OnLevelsChanged;
            AudioOutputWatchdog.OutputReset += OnOutputReset;
        }

        private void OnDestroy()
        {
            AudioLevels.Changed -= OnLevelsChanged;
            AudioOutputWatchdog.OutputReset -= OnOutputReset;
        }

        /// <summary>
        /// The audio system was re-initialised onto a new output device; every source was stopped by the reset. The
        /// mixer groups have to be looked up again and the tracked loops restarted, or a device change would leave
        /// every world loop permanently silent while the service still believed it was playing them.
        /// </summary>
        public void OnOutputReset()
        {
            OutputResets++;
            ResolveGroups();
            foreach (var loop in _loops)
            {
                if (loop.Source == null || loop.Source.clip == null || loop.Source.isPlaying) continue;
                loop.Source.outputAudioMixerGroup = _catalog != null && _catalog.TryGet(loop.Id, out var d) ? GroupFor(d.Bus) : loop.Source.outputAudioMixerGroup;
                loop.Source.Play();
            }

            ApplyLoopGains();
        }

        /// <summary>How often the service restarted its loops after an output-device change (runtime evidence).</summary>
        public int OutputResets { get; private set; }

        private void EnsurePool()
        {
            while (_oneShots.Count < Mathf.Max(1, _oneShotSources))
            {
                var go = new GameObject("OneShot" + _oneShots.Count);
                go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.Stop();
                _oneShots.Add(source);
            }
        }

        private void ResolveGroups()
        {
            _groups.Clear();
            if (_mixer == null) return;
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            {
                var found = _mixer.FindMatchingGroups(bus.ToString());
                if (found != null && found.Length > 0) _groups[bus] = found[0];
            }
        }

        public static float GainFor(AudioBus bus) => bus == AudioBus.Ambience ? AudioLevels.AmbienceBusGain : AudioLevels.GainFor(bus == AudioBus.Music);

        /// <summary>The mixer group a bus routes to (null without a mixer: the source outputs straight to the listener).</summary>
        public AudioMixerGroup GroupFor(AudioBus bus) => _groups.TryGetValue(bus, out var group) ? group : null;

        // ---- One-shots ----

        /// <summary>Plays the event once (2D, or attenuated/panned for a world position). Returns false for fallback silence (unknown id, no clips, cap, throttle).</summary>
        public bool Play(string id, Vector2? position = null)
        {
            if (!TryResolve(id, out var definition)) return false;
            if (definition.MinIntervalMs > 0 && _lastStart.TryGetValue(id, out var last) && (_clock - last) * 1000f < definition.MinIntervalMs)
            {
                Throttled++;
                return false;
            }

            if (CountBusy(id) >= definition.MaxInstances)
            {
                Capped++;
                return false;
            }

            var source = Acquire();
            var clip = Pick(definition);
            var listener = (Vector2)AudioListenerRig.Position;
            var spatial = position.HasValue ? Spatializer.Gain(position.Value, listener) : 1f;
            source.clip = clip;
            source.loop = false;
            source.volume = definition.Volume * GainFor(definition.Bus) * spatial;
            source.pitch = UnityEngine.Random.Range(definition.PitchMin, definition.PitchMax);
            source.outputAudioMixerGroup = GroupFor(definition.Bus);
            source.spatialBlend = 0f;
            source.panStereo = position.HasValue ? Spatializer.Pan(position.Value, listener) : 0f;
            source.transform.position = position.HasValue ? new Vector3(position.Value.x, position.Value.y, 0f) : transform.position;
            source.Play();
            _busy.Add(new Voice { Source = source, Id = id, StartedAt = _clock, SpatialGain = spatial, Bus = definition.Bus, Volume = definition.Volume });
            _lastStart[id] = _clock;
            Count(id);
            LastStartedSource = source;
            LastStartedId = id;
            VoiceStarted?.Invoke(id, source);
            return true;
        }

        // ---- Loops ----

        public LoopHandle PlayLoop(string id, Transform follow = null)
        {
            if (!TryResolve(id, out var definition)) return null;
            var go = new GameObject("Loop:" + id);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.Stop();
            source.clip = Pick(definition);
            source.loop = true;
            var baseVolume = definition.Volume * GainFor(definition.Bus);
            source.volume = baseVolume;
            source.outputAudioMixerGroup = GroupFor(definition.Bus);
            source.spatialBlend = 0f;
            if (follow != null) go.transform.position = follow.position;
            var handle = new LoopHandle(id, source, follow, baseVolume);
            ApplySpatial(handle);
            source.Play();
            _loops.Add(handle);
            Count(id);
            LastStartedSource = source;
            LastStartedId = id;
            VoiceStarted?.Invoke(id, source);
            return handle;
        }

        public void StopLoop(LoopHandle handle)
        {
            if (handle == null || !handle.IsPlaying) return;
            handle.IsPlaying = false;
            _loops.Remove(handle);
            if (handle.Source != null)
            {
                handle.Source.Stop();
                Destroy(handle.Source.gameObject);
            }
        }

        /// <summary>Despawn / scene / depth transition: every loop ends; one-shots finish naturally.</summary>
        public void StopAllLoops()
        {
            for (var i = _loops.Count - 1; i >= 0; i--) StopLoop(_loops[i]);
        }

        // ---- Housekeeping ----

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;
            for (var i = _busy.Count - 1; i >= 0; i--)
            {
                var voice = _busy[i];
                if (voice.Source == null || !voice.Source.isPlaying) _busy.RemoveAt(i);
            }

            for (var i = _loops.Count - 1; i >= 0; i--)
            {
                var loop = _loops[i];
                if (loop.Follows && loop.Follow == null) { StopLoop(loop); continue; } // its world object is gone
                if (loop.Follow != null && loop.Source != null)
                {
                    loop.Source.transform.position = loop.Follow.position;
                    ApplySpatial(loop);
                }
            }
        }

        private void ApplySpatial(LoopHandle loop)
        {
            if (loop.Source == null) return;
            if (loop.Follow == null) { loop.Source.volume = loop.BaseVolume; loop.Source.panStereo = 0f; return; }
            var listener = (Vector2)AudioListenerRig.Position;
            loop.Source.volume = loop.BaseVolume * Spatializer.Gain(loop.Follow.position, listener);
            loop.Source.panStereo = Spatializer.Pan(loop.Follow.position, listener);
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private void OnLevelsChanged()
        {
            foreach (var voice in _busy)
            {
                if (voice.Source != null) voice.Source.volume = voice.Volume * GainFor(voice.Bus) * voice.SpatialGain;
            }

            ApplyLoopGains();
        }

        private void ApplyLoopGains()
        {
            foreach (var loop in _loops)
            {
                if (loop.Source != null && _catalog != null && _catalog.TryGet(loop.Id, out var d))
                {
                    loop.BaseVolume = d.Volume * GainFor(d.Bus);
                    ApplySpatial(loop);
                }
            }
        }

        private bool TryResolve(string id, out AudioEventDefinition definition)
        {
            definition = null;
            if (string.IsNullOrEmpty(id)) return false;
            if (_catalog == null || !_catalog.TryGet(id, out definition))
            {
                _unknown.Add(id);
                return false;
            }

            if (!definition.HasClips)
            {
                _silent.Add(id);
                Count(id);
                return false;
            }

            return true;
        }

        private AudioSource Acquire()
        {
            EnsurePool();
            foreach (var s in _oneShots)
            {
                var busy = false;
                foreach (var b in _busy) if (b.Source == s) { busy = true; break; }
                if (!busy) return s;
            }

            // All busy: steal the oldest.
            var oldestIndex = 0;
            for (var i = 1; i < _busy.Count; i++) if (_busy[i].StartedAt < _busy[oldestIndex].StartedAt) oldestIndex = i;
            var stolen = _busy[oldestIndex].Source;
            _busy.RemoveAt(oldestIndex);
            stolen.Stop();
            Stolen++;
            return stolen;
        }

        private int CountBusy(string id)
        {
            var n = 0;
            foreach (var b in _busy) if (b.Id == id) n++;
            return n;
        }

        private static AudioClip Pick(AudioEventDefinition definition)
        {
            var clips = definition.Clips;
            if (clips.Length == 1) return clips[0];
            AudioClip pick = null;
            for (var attempts = 0; attempts < 8 && pick == null; attempts++) pick = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (pick == null) foreach (var c in clips) if (c != null) return c;
            return pick;
        }

        private void Count(string id)
        {
            _played[id] = PlayedCount(id) + 1;
            TotalPlayed++;
        }
    }
}
