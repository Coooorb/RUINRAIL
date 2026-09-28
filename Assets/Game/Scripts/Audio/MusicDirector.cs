using System;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// One music state at a time (art/105): two alternating sources crossfade between roles, a stinger source plays
    /// the six stingers on top, and one ambience loop per biome sits below combat readability. Selecting the role that
    /// is already active is a no-op, so transitions never stack duplicate tracks. Missing clips keep the state machine
    /// honest (the role is active and reported silent) without playing anything.
    /// </summary>
    public sealed class MusicDirector : MonoBehaviour
    {
        /// <summary>V1 FINAL (TASK 179) crossfade length.</summary>
        public const float CrossfadeSeconds = 1.5f;
        /// <summary>V1 FINAL (TASK 179): ambience never exceeds this fraction of the SFX gain (art/105: below combat readability).</summary>
        public const float AmbienceCeiling = AudioLevels.AmbienceCeiling;

        [SerializeField] private MusicCatalog _catalog;

        private AudioSource _a;
        private AudioSource _b;
        private AudioSource _stinger;
        private AudioSource _ambience;
        private AudioSource _current;
        private AudioSource _fading;
        private float _fade;
        // Level (0..1 of the music gain) each source fades from: a crossfade interrupted by another change hands over
        // from where the tracks actually are, never from full or from silence.
        private float _fadingFrom = 1f;
        private float _currentFrom;
        private bool _built;

        public MusicRole? ActiveRole { get; private set; }
        public Biome? ActiveAmbience { get; private set; }
        public bool IsSilent { get; private set; }
        public bool IsCrossfading => _fading != null && _fade < 1f;
        public int Transitions { get; private set; }
        public int StingersPlayed { get; private set; }
        public StingerRole? LastStinger { get; private set; }
        public MusicCatalog Catalog => _catalog;

        public void Configure(MusicCatalog catalog)
        {
            _catalog = catalog;
            Build();
        }

        private void Awake()
        {
            Build();
            AudioLevels.Changed += ApplyGains;
            AudioOutputWatchdog.OutputReset += OnOutputReset;
        }

        private void OnDestroy()
        {
            AudioLevels.Changed -= ApplyGains;
            AudioOutputWatchdog.OutputReset -= OnOutputReset;
        }

        /// <summary>
        /// The audio system was re-initialised onto a new output device, which stops every source. The music state
        /// machine is unchanged, so the beds simply start again on the role and biome that are already active — the
        /// player hears the track continue instead of falling permanently silent after a device switch.
        /// </summary>
        public void OnOutputReset()
        {
            OutputResets++;
            Build();
            if (_fading != null) { _fading.Stop(); _fading = null; _fade = 1f; }
            if (_current != null && _current.clip != null && !_current.isPlaying)
            {
                _current.volume = Gain(AudioBus.Music);
                _current.Play();
            }

            if (_ambience != null && _ambience.clip != null && !_ambience.isPlaying)
            {
                _ambience.volume = AmbienceGain();
                _ambience.Play();
            }

            ApplyGains();
        }

        /// <summary>How often the beds were restarted after an output-device change (runtime evidence).</summary>
        public int OutputResets { get; private set; }

        private void Build()
        {
            if (_built) return;
            _built = true;
            _a = Source("MusicA", true);
            _b = Source("MusicB", true);
            _stinger = Source("Stinger", false);
            _ambience = Source("Ambience", true);
        }

        private AudioSource Source(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.Stop(); // AddComponent already ran the source's Awake with playOnAwake on; make sure it is idle.
            return s;
        }

        /// <summary>The number of music sources that are playing a track right now (never more than two during a crossfade, one otherwise).</summary>
        public int PlayingTrackSources => (_a != null && _a.isPlaying && _a.clip != null ? 1 : 0) + (_b != null && _b.isPlaying && _b.clip != null ? 1 : 0);

        public void SetState(MusicScreen screen, Biome biome, CombatIntensity intensity) => SetRole(MusicStateResolver.Resolve(screen, biome, intensity));

        public void SetRole(MusicRole role)
        {
            Build();
            if (ActiveRole == role) return;
            ActiveRole = role;
            Transitions++;
            var clip = _catalog != null ? _catalog.TrackFor(role) : null;
            IsSilent = clip == null;

            // Roles that share one track (the Main Menu and the Shelter): the bed that is already playing simply takes
            // the new role — no restart, no crossfade into itself.
            // A fade-out still in progress just finishes as it was.
            if (clip != null && _current != null && _current.isPlaying && _current.clip == clip) return;

            // At most two sources ever play, and no track ever jumps up while it leaves. Mid-crossfade, the louder track
            // keeps fading out from the level it has now and the quieter one (at or below half level, already leaving or
            // barely in) hands its source to the new track. Going back to the track that is still fading out reverses
            // that fade instead of starting a second copy of it from the top.
            var fadingLevel = _fading != null ? _fadingFrom * (1f - _fade) : 0f;
            var currentLevel = _fading != null ? _currentFrom + (1f - _currentFrom) * _fade : 1f;
            if (_fading != null && clip != null && _fading.clip == clip)
            {
                (_current, _fading) = (_fading, _current);
                _currentFrom = fadingLevel;
                _fadingFrom = currentLevel;
                _fade = 0f;
                return;
            }

            AudioSource outgoing = null;
            var outgoingLevel = 0f;
            if (_fading != null)
            {
                if (currentLevel >= fadingLevel) { _fading.Stop(); outgoing = _current; outgoingLevel = currentLevel; }
                else { _current?.Stop(); outgoing = _fading; outgoingLevel = fadingLevel; }
            }
            else if (_current != null && _current.isPlaying)
            {
                outgoing = _current;
                outgoingLevel = 1f;
            }

            _fading = outgoing;
            _fadingFrom = outgoingLevel;
            _currentFrom = 0f;
            _fade = 0f;
            var next = outgoing == _a ? _b : _a;
            _current = next;
            _current.Stop();
            _current.clip = clip;
            if (clip != null)
            {
                _current.volume = _fading != null ? 0f : Gain(AudioBus.Music);
                _current.Play();
            }

            if (_fading == null) ApplyGains();
        }

        public void PlayStinger(StingerRole role)
        {
            Build();
            LastStinger = role;
            StingersPlayed++;
            var clip = _catalog != null ? _catalog.StingerFor(role) : null;
            if (clip == null) return;
            _stinger.volume = Gain(AudioBus.Music);
            _stinger.PlayOneShot(clip);
        }

        /// <summary>Biome ambience loop (null = none, e.g. menu/Shelter). Re-selecting the same biome never restarts it.</summary>
        public void SetAmbience(Biome? biome)
        {
            Build();
            if (ActiveAmbience == biome) return;
            ActiveAmbience = biome;
            _ambience.Stop();
            var clip = biome.HasValue && _catalog != null ? _catalog.AmbienceFor(biome.Value) : null;
            _ambience.clip = clip;
            if (clip == null) return;
            _ambience.volume = AmbienceGain();
            _ambience.Play();
        }

        /// <summary>Everything off (scene teardown, quit).</summary>
        public void StopAll()
        {
            Build();
            _a.Stop(); _b.Stop(); _stinger.Stop(); _ambience.Stop();
            _current = null; _fading = null;
            ActiveRole = null;
            ActiveAmbience = null;
        }

        public void Tick(float deltaTime)
        {
            if (_fading == null) return;
            _fade = Mathf.Clamp01(_fade + (CrossfadeSeconds <= 0f ? 1f : deltaTime / CrossfadeSeconds));
            var target = Gain(AudioBus.Music);
            _fading.volume = target * _fadingFrom * (1f - _fade);
            if (_current != null) _current.volume = target * (_currentFrom + (1f - _currentFrom) * _fade);
            if (_fade >= 1f)
            {
                _fading.Stop();
                _fading = null;
            }
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private static float Gain(AudioBus bus) => AudioService.GainFor(bus);
        public static float AmbienceGain() => Mathf.Min(Gain(AudioBus.Ambience), Gain(AudioBus.Ambience) * AmbienceCeiling);

        private void ApplyGains()
        {
            if (_fading != null) return;
            if (_current != null) _current.volume = Gain(AudioBus.Music);
            if (_ambience != null) _ambience.volume = AmbienceGain();
        }
    }
}
