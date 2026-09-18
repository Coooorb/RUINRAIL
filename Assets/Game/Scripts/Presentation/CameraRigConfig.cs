using RuinRail.Core.Rendering;
using UnityEngine;

namespace RuinRail.Presentation
{
    /// <summary>
    /// art/101 + art/102 camera data: 640×360 reference at PPU 32, orthographic, pixel perfect, fixed gameplay zoom,
    /// soft follow and a subtle aim offset. The follow/offset amounts are V1 FINAL (TASK 179) tunables (the spec gives
    /// qualities, not numbers); the reference resolution and PPU are approved values.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Presentation/Camera Rig Config", fileName = "CameraRigConfig")]
    public sealed class CameraRigConfig : ScriptableObject
    {
        public const int ApprovedReferenceWidth = 640;
        public const int ApprovedReferenceHeight = 360;
        public const int ApprovedPixelsPerUnit = SortingConvention.PixelsPerUnit;

        [SerializeField] private int _referenceWidth = ApprovedReferenceWidth;
        [SerializeField] private int _referenceHeight = ApprovedReferenceHeight;
        [SerializeField] private int _pixelsPerUnit = ApprovedPixelsPerUnit;
        [Tooltip("V1 FINAL (TASK 179): exponential follow sharpness per second (higher = tighter). Soft follow without excessive lag.")]
        [SerializeField] private float _followSharpness = 10f;
        [Tooltip("V1 FINAL (TASK 179): maximum aim-direction offset in tiles. Keep it subtle (art/102).")]
        [SerializeField] private float _aimOffsetMaxTiles = 1f;
        [Tooltip("V1 FINAL (TASK 179): fraction of the aim vector (in tiles, pointer) or full stick deflection applied before clamping.")]
        [SerializeField] private float _aimOffsetFraction = 0.15f;

        public int ReferenceWidth => _referenceWidth;
        public int ReferenceHeight => _referenceHeight;
        public int PixelsPerUnit => _pixelsPerUnit;
        public float FollowSharpness => _followSharpness;
        public float AimOffsetMaxTiles => _aimOffsetMaxTiles;
        public float AimOffsetFraction => _aimOffsetFraction;

        /// <summary>Orthographic half-height for the reference resolution: 360 px / 32 PPU / 2.</summary>
        public float OrthographicSize => _referenceHeight / (float)_pixelsPerUnit * 0.5f;
        public bool IsApproved => _referenceWidth == ApprovedReferenceWidth && _referenceHeight == ApprovedReferenceHeight && _pixelsPerUnit == ApprovedPixelsPerUnit;
    }
}
