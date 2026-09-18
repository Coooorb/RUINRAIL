using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Routes the Interact input to the nearest IInteractable within reach. One press → at most one interaction.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _reach = 1.5f;

        private IPlayerInputReader _inputReader;
        private readonly Collider2D[] _hits = new Collider2D[16];
        private readonly ActionGateLookup _actionGate = new();

        public float Reach => _reach;

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            Attach(inputReader);
        }

        private void Awake()
        {
            if (_inputReader == null)
            {
                Attach(GetComponent<PlayerInput>()?.Reader);
            }
        }

        private void OnDestroy()
        {
            Attach(null);
        }

        private void Attach(IPlayerInputReader reader)
        {
            if (_inputReader != null) _inputReader.Interact -= HandleInteract;
            _inputReader = reader;
            if (_inputReader != null) _inputReader.Interact += HandleInteract;
        }

        private void HandleInteract()
        {
            TryInteract();
        }

        /// <summary>Interacts with the closest usable target in reach; returns whether anything responded.</summary>
        public bool TryInteract()
        {
            // 84: Downed/Dead players perform no normal interactions.
            if (!_actionGate.CanAct(this)) return false;
            var target = FindNearestInteractable();
            return target != null && target.Interact(gameObject);
        }

        public IInteractable FindNearestInteractable()
        {
            var count = Physics2D.OverlapCircle(transform.position, _reach, Physics2DQueries.LegacyQueryFilter(), _hits);
            IInteractable best = null;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var interactable = _hits[i].GetComponentInParent<IInteractable>();
                if (interactable == null || !interactable.CanInteract(gameObject))
                {
                    continue;
                }

                var distance = ((Vector2)_hits[i].transform.position - (Vector2)transform.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = interactable;
                }
            }

            return best;
        }
    }
}
