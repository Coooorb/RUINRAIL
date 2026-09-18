using RuinRail.Core.Input;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    public sealed class PlayerAiming : MonoBehaviour
    {
        private const float MinMeaningfulInputSqrMagnitude = 0.0001f;

        [SerializeField] private Transform _aimPivot;
        [SerializeField] private Camera _camera;

        private IPlayerInputReader _inputReader;

        public Vector2 AimDirection { get; private set; } = Vector2.right;
        public BodyFacing8 BodyFacing { get; private set; } = BodyFacing8.E;

        /// <summary>True when the last aim came from a pointer, so <see cref="AimWorldPoint"/> is a real crosshair position.</summary>
        public bool HasPointerAim { get; private set; }

        /// <summary>The crosshair in world space (pointer aim only; meaningless for stick aim).</summary>
        public Vector2 AimWorldPoint { get; private set; }

        /// <summary>
        /// Where the aim ray starts: the weapon pivot (the hands) when one is bound, else the body. A shot leaves the
        /// muzzle, which sits on the ray from the pivot — so measuring the pointer direction from the body instead put
        /// the bullet line a grip-height above the crosshair line and made a direct aim miss.
        /// </summary>
        public Vector2 AimOrigin => _aimPivot != null ? (Vector2)_aimPivot.position : (Vector2)transform.position;

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            _inputReader = inputReader;
        }

        public void SetCamera(Camera camera)
        {
            _camera = camera;
        }

        public void SetAimPivot(Transform aimPivot)
        {
            _aimPivot = aimPivot;
        }

        /// <summary>
        /// A pure network replica has no input to aim from: the host's replicated aim is applied here so the body facing
        /// and the weapon pivot follow the real owner. Ignored while a reader with actual aim input is bound.
        /// </summary>
        public void ApplyReplicatedAim(Vector2 aimDirection)
        {
            if (_inputReader != null && !(_inputReader is NullPlayerInputReader)) return;
            if (aimDirection.sqrMagnitude < MinMeaningfulInputSqrMagnitude) return;
            AimDirection = aimDirection.normalized;
            BodyFacing = BodyFacingResolver.Resolve(AimDirection);
            OrientPivot();
        }

        private void Awake()
        {
            if (_inputReader == null)
            {
                _inputReader = GetComponent<PlayerInput>()?.Reader;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
            }
        }

        private void Update()
        {
            UpdateAimDirection();
            OrientPivot();
        }

        private void UpdateAimDirection()
        {
            if (_inputReader == null)
            {
                return;
            }

            var rawAim = _inputReader.Aim;
            HasPointerAim = _inputReader.IsAimFromPointer && _camera != null;
            var candidateDirection = _inputReader.IsAimFromPointer
                ? ScreenPointToWorldDirection(rawAim)
                : rawAim;

            if (candidateDirection.sqrMagnitude < MinMeaningfulInputSqrMagnitude)
            {
                return;
            }

            AimDirection = candidateDirection.normalized;
            BodyFacing = BodyFacingResolver.Resolve(AimDirection);
        }

        private Vector2 ScreenPointToWorldDirection(Vector2 screenPosition)
        {
            if (_camera == null)
            {
                return Vector2.zero;
            }

            var distanceToPlayer = Mathf.Abs(_camera.transform.position.z - transform.position.z);
            var worldPoint = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, distanceToPlayer));
            AimWorldPoint = worldPoint;
            return (Vector2)worldPoint - AimOrigin;
        }

        private void OrientPivot()
        {
            if (_aimPivot == null)
            {
                return;
            }

            var angle = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg;
            _aimPivot.rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
