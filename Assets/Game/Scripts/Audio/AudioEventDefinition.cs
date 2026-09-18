using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// One authored audio event: id from <see cref="AudioEventIds"/>, its bus, clip variations (repetition control),
    /// gain/pitch, loop flag and a per-event cap on simultaneous instances. No clips = fallback silence (never an error):
    /// the audit reports it as BLOCKED_EXTERNAL_ASSET.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Audio/Audio Event", fileName = "Sfx_")]
    public sealed class AudioEventDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "";
        [SerializeField] private AudioBus _bus = AudioBus.Weapons;
        [SerializeField] private AudioClip[] _clips = System.Array.Empty<AudioClip>();
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField, Range(0.5f, 1.5f)] private float _pitchMin = 1f;
        [SerializeField, Range(0.5f, 1.5f)] private float _pitchMax = 1f;
        [SerializeField] private bool _loop;
        [Tooltip("Cap on simultaneous one-shot instances of this event (readability under fire).")]
        [SerializeField, Min(1)] private int _maxInstances = 4;
        [Tooltip("Minimum milliseconds between two starts of this event (anti-machine-gun for very frequent events).")]
        [SerializeField, Min(0)] private int _minIntervalMs;

        public string Id => _id;
        public AudioBus Bus => _bus;
        public AudioClip[] Clips => _clips;
        public float Volume => _volume;
        public float PitchMin => Mathf.Min(_pitchMin, _pitchMax);
        public float PitchMax => Mathf.Max(_pitchMin, _pitchMax);
        public bool Loop => _loop;
        public int MaxInstances => Mathf.Max(1, _maxInstances);
        public int MinIntervalMs => _minIntervalMs;
        public bool HasClips => _clips != null && _clips.Length > 0 && System.Array.Exists(_clips, c => c != null);

#if UNITY_EDITOR
        /// <summary>Editor-only binding used by the audio pipeline to attach generated clips. Never called at runtime.</summary>
        public void EditorSetClips(AudioClip[] clips)
        {
            _clips = clips ?? System.Array.Empty<AudioClip>();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif

        public void Configure(string id, AudioBus bus, bool loop, int maxInstances = 4, int minIntervalMs = 0)
        {
            _id = id;
            _bus = bus;
            _loop = loop;
            _maxInstances = maxInstances;
            _minIntervalMs = minIntervalMs;
        }

        public void SetClips(params AudioClip[] clips) => _clips = clips ?? System.Array.Empty<AudioClip>();
    }
}
