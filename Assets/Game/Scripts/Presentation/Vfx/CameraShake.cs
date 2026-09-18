using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation.Vfx
{
    public enum ShakeKind
    {
        SmallGun,
        Shotgun,
        Explosion,
        BossSlam
    }

    /// <summary>
    /// Controlled screen shake (art/104): an offset applied after the rig framed the world, in whole pixels so the pixel
    /// grid holds, scaled by the accessibility intensity (0 = off). It never moves the rig's framed position and never
    /// reaches gameplay.
    /// </summary>
    public sealed class CameraShake : MonoBehaviour
    {
        [SerializeField] private FeedbackConfig _config;
        [SerializeField] private Transform _shaken;
        [SerializeField] private CameraRig _rig;

        private float _remaining;
        private float _duration;
        private float _pixels;
        private Vector2 _appliedOffset;
        private int _seed;

        public float Remaining => _remaining;
        public Vector2 CurrentOffset { get; private set; }
        public int Requests { get; private set; }
        public int Suppressed { get; private set; }

        public void Configure(FeedbackConfig config, Transform shaken = null, CameraRig rig = null)
        {
            _config = config;
            _shaken = shaken;
            _rig = rig;
        }

        private void Awake()
        {
            if (_rig == null) _rig = GetComponent<CameraRig>();
        }

        public static (float pixels, float seconds) AmountFor(FeedbackConfig config, ShakeKind kind) => kind switch
        {
            ShakeKind.SmallGun => (config.SmallGunShakePixels, config.SmallGunShakeSeconds),
            ShakeKind.Shotgun => (config.ShotgunShakePixels, config.ShotgunShakeSeconds),
            ShakeKind.Explosion => (config.ExplosionShakePixels, config.ExplosionShakeSeconds),
            _ => (config.BossSlamShakePixels, config.BossSlamShakeSeconds)
        };

        public void Request(ShakeKind kind)
        {
            if (_config == null) return;
            var (pixels, seconds) = AmountFor(_config, kind);
            Request(pixels, seconds);
        }

        /// <summary>Stronger requests replace weaker ones; the accessibility intensity scales the amplitude and 0 suppresses it entirely.</summary>
        public void Request(float pixels, float seconds)
        {
            Requests++;
            var scaled = pixels * FeedbackPreferences.ScreenShakeIntensity;
            if (scaled < 0.5f || seconds <= 0f)
            {
                Suppressed++;
                return;
            }

            if (scaled >= _pixels || _remaining <= 0f)
            {
                _pixels = scaled;
                _duration = seconds;
                _remaining = seconds;
            }
        }

        public void Tick(float deltaTime)
        {
            var target = _rig == null ? (_shaken != null ? _shaken : transform) : null;
            if (target != null && _appliedOffset != Vector2.zero)
            {
                target.position -= (Vector3)_appliedOffset;
                _appliedOffset = Vector2.zero;
            }

            if (_remaining <= 0f)
            {
                CurrentOffset = Vector2.zero;
                if (_rig != null) _rig.ShakeOffset = Vector2.zero;
                return;
            }

            _remaining -= deltaTime;
            var envelope = _duration <= 0f ? 0f : Mathf.Clamp01(_remaining / _duration);
            var amplitude = _pixels * envelope;
            _seed++;
            var x = Mathf.Round(Mathf.Sin(_seed * 12.9898f) * amplitude);
            var y = Mathf.Round(Mathf.Cos(_seed * 78.233f) * amplitude);
            CurrentOffset = new Vector2(x, y) / SortingConvention.PixelsPerUnit;
            if (_rig != null) _rig.ShakeOffset = CurrentOffset;
            else
            {
                _appliedOffset = CurrentOffset;
                target.position += (Vector3)_appliedOffset;
            }
            if (_remaining <= 0f) { _pixels = 0f; }
        }

        private void LateUpdate() => Tick(Time.deltaTime);
    }
}
