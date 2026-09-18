using UnityEngine;

namespace RuinRail.Audio
{
    /// <summary>
    /// The one AudioListener of the process. It lives on the persistent audio root (so the Main Menu and the Shelter —
    /// scenes without a camera object — are heard exactly like the dungeon) and follows the active camera each frame,
    /// so positioned gameplay sounds are judged from where the player looks. There is never a second listener: the
    /// scene composers add none, and <see cref="Ensure"/> is idempotent.
    /// </summary>
    [DefaultExecutionOrder(1000)] // after every camera rig's LateUpdate, so the listener sits where the frame is actually seen from
    public sealed class AudioListenerRig : MonoBehaviour
    {
        private AudioListener _listener;

        public static AudioListenerRig Current { get; private set; }

        /// <summary>World position of the listener (x/y matter for 2D attenuation; z is the camera's).</summary>
        public static Vector3 Position => Current != null ? Current.transform.position : Vector3.zero;

        public AudioListener Listener => _listener;

        public static AudioListenerRig Ensure(Transform parent)
        {
            if (Current != null) return Current;
            var go = new GameObject("AudioListenerRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<AudioListenerRig>();
            return rig;
        }

        private void Awake()
        {
            Current = this;
            _listener = GetComponent<AudioListener>();
            if (_listener == null) _listener = gameObject.AddComponent<AudioListener>();
            _listener.enabled = true;
            AudioListener.pause = false; // a persistent listener must never boot paused
            Follow();
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        private void LateUpdate() => Follow();

        /// <summary>Tracks Camera.main (the expedition's camera rig, tagged MainCamera); without a camera the listener sits at the origin.</summary>
        public void Follow()
        {
            var camera = Camera.main;
            transform.position = camera != null ? camera.transform.position : Vector3.zero;
        }
    }
}
