using System;
using RuinRail.Core.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace RuinRail.Presentation
{
    /// <summary>Pure framing math (testable without a scene): soft follow, subtle aim offset, bounds clamp, pixel snap.</summary>
    public static class CameraFraming
    {
        /// <summary>Rounds a world position to the PPU pixel grid so sprites never land on sub-pixel offsets (no shimmer).</summary>
        public static Vector2 SnapToPixelGrid(Vector2 position, int pixelsPerUnit)
        {
            var ppu = Mathf.Max(1, pixelsPerUnit);
            return new Vector2(Mathf.Round(position.x * ppu) / ppu, Mathf.Round(position.y * ppu) / ppu);
        }

        /// <summary>Integer upscale the pixel perfect camera picks for a screen (art/101: 1920×1080 is a clean 3×); 0 when the screen is smaller than the reference.</summary>
        public static int IntegerScaleFor(int screenWidth, int screenHeight, int referenceWidth, int referenceHeight)
        {
            if (referenceWidth <= 0 || referenceHeight <= 0) return 0;
            return Mathf.Min(screenWidth / referenceWidth, screenHeight / referenceHeight);
        }

        public static bool IsOnPixelGrid(Vector2 position, int pixelsPerUnit, float tolerance = 1e-4f)
        {
            var snapped = SnapToPixelGrid(position, pixelsPerUnit);
            return (snapped - position).sqrMagnitude <= tolerance * tolerance;
        }

        /// <summary>Subtle offset toward the aim: a fraction of the aim vector, clamped to the configured maximum.</summary>
        public static Vector2 AimOffset(Vector2 aimFromTarget, float fraction, float maxTiles)
        {
            if (maxTiles <= 0f || fraction <= 0f) return Vector2.zero;
            return Vector2.ClampMagnitude(aimFromTarget * fraction, maxTiles);
        }

        /// <summary>Exponential soft follow: frame-rate independent, never overshoots, converges to the desired point.</summary>
        public static Vector2 Follow(Vector2 current, Vector2 desired, float sharpness, float deltaTime)
        {
            if (sharpness <= 0f || deltaTime <= 0f) return desired;
            var t = 1f - Mathf.Exp(-sharpness * deltaTime);
            return Vector2.Lerp(current, desired, t);
        }

        /// <summary>Keeps the view inside the visible-world bounds (spectators never see undiscovered space); a view larger than the bounds centres on them.</summary>
        public static Vector2 ClampToBounds(Vector2 center, Vector2 halfExtents, Rect? bounds)
        {
            if (!bounds.HasValue) return center;
            var b = bounds.Value;
            var x = b.width <= halfExtents.x * 2f ? b.center.x : Mathf.Clamp(center.x, b.xMin + halfExtents.x, b.xMax - halfExtents.x);
            var y = b.height <= halfExtents.y * 2f ? b.center.y : Mathf.Clamp(center.y, b.yMin + halfExtents.y, b.yMax - halfExtents.y);
            return new Vector2(x, y);
        }

        /// <summary>One full step: desired = target + aim offset, soft follow, clamp, snap.</summary>
        public static Vector2 Step(Vector2 current, Vector2 target, Vector2 aimFromTarget, Rect? bounds, Vector2 halfExtents, CameraRigConfig config, float deltaTime)
        {
            var desired = target + AimOffset(aimFromTarget, config.AimOffsetFraction, config.AimOffsetMaxTiles);
            var followed = Follow(current, desired, config.FollowSharpness, deltaTime);
            var clamped = ClampToBounds(followed, halfExtents, bounds);
            return SnapToPixelGrid(clamped, config.PixelsPerUnit);
        }
    }

    /// <summary>
    /// The local player's camera (art/102: every online player has their own). Configures the URP Pixel Perfect Camera
    /// from <see cref="CameraRigConfig"/>, follows a position provider (the local player, or a Dead player's spectator
    /// target through DeadSpectatorFollow.FollowPosition — never a free position), applies the subtle aim offset and
    /// clamps to the visible-world bounds the dungeon runtime publishes. Input never moves this camera directly.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        [SerializeField] private CameraRigConfig _config;

        private Camera _camera;
        private PixelPerfectCamera _pixelPerfect;
        private Func<Vector2> _followPosition;
        private Func<Vector2> _aimPosition;
        private Rect? _bounds;
        private bool _snapNext = true;
        private Vector2 _virtual;

        public CameraRigConfig Config => _config;
        public Camera Camera => _camera;
        public PixelPerfectCamera PixelPerfect => _pixelPerfect;
        public Rect? VisibleBounds => _bounds;
        public Vector2 Position => transform.position;
        public bool HasTarget => _followPosition != null;
        public int Steps { get; private set; }
        /// <summary>Whole-pixel screen-shake offset (art/104) applied after framing; set by CameraShake, never part of the framed position.</summary>
        public Vector2 ShakeOffset { get; set; }

        public Vector2 HalfExtents
        {
            get
            {
                var size = _config != null ? _config.OrthographicSize : (_camera != null ? _camera.orthographicSize : 1f);
                var aspect = _config != null ? _config.ReferenceWidth / (float)_config.ReferenceHeight : (_camera != null ? _camera.aspect : 16f / 9f);
                return new Vector2(size * aspect, size);
            }
        }

        public void SetConfig(CameraRigConfig config)
        {
            _config = config;
            ApplyConfig();
        }

        /// <summary>The position to frame — a player or a spectator target. Passing null keeps the last framed position (the camera never roams).</summary>
        public void SetFollow(Func<Vector2> followPosition)
        {
            _followPosition = followPosition;
            _snapNext = true;
        }

        public void SetFollow(Transform target) => SetFollow(target != null ? () => (Vector2)target.position : null);

        /// <summary>World-space aim point (pointer) or target + stick vector; null disables the aim offset.</summary>
        public void SetAim(Func<Vector2> aimPosition) => _aimPosition = aimPosition;

        /// <summary>Visible/discovered world bounds; the view never leaves them.</summary>
        public void SetVisibleBounds(Rect? bounds) => _bounds = bounds;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _pixelPerfect = GetComponent<PixelPerfectCamera>();
            if (_pixelPerfect == null) _pixelPerfect = gameObject.AddComponent<PixelPerfectCamera>();
            ApplyConfig();
        }

        /// <summary>Pixel Perfect Camera: reference 640×360, PPU 32, pixel snapping (no sub-pixel movement), no crop (integer upscale; 1920×1080 = 3×).</summary>
        public void ApplyConfig()
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_config == null || _camera == null) return;
            _camera.orthographic = true;
            _camera.orthographicSize = _config.OrthographicSize;
            if (_pixelPerfect != null)
            {
                _pixelPerfect.assetsPPU = _config.PixelsPerUnit;
                _pixelPerfect.refResolutionX = _config.ReferenceWidth;
                _pixelPerfect.refResolutionY = _config.ReferenceHeight;
                _pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.PixelSnapping;
                _pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.None;
            }
        }

        private void LateUpdate() => Step(Time.deltaTime);

        /// <summary>One framing step (LateUpdate; tests drive it directly).</summary>
        public void Step(float deltaTime)
        {
            if (_followPosition == null || _config == null) return;
            var target = _followPosition();
            var aim = _aimPosition != null ? _aimPosition() - target : Vector2.zero;
            var desired = target + CameraFraming.AimOffset(aim, _config.AimOffsetFraction, _config.AimOffsetMaxTiles);
            // The smoothed position stays unsnapped so the follow converges fully; only what is applied to the transform is on the pixel grid.
            if (_snapNext)
            {
                _virtual = desired;
                _snapNext = false;
            }
            else
            {
                _virtual = CameraFraming.Follow(_virtual, desired, _config.FollowSharpness, deltaTime);
            }

            _virtual = CameraFraming.ClampToBounds(_virtual, HalfExtents, _bounds);
            var next = CameraFraming.SnapToPixelGrid(_virtual, _config.PixelsPerUnit);

            transform.position = new Vector3(next.x + ShakeOffset.x, next.y + ShakeOffset.y, transform.position.z);
            Steps++;
        }
    }
}
