using System;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// World coin pile. Collected exactly once; the amount is handed to the interactor's ICoinReceiver
    /// (even co-op distribution is applied by the receiving side per 58 "Shared Co-op Loot").
    /// Coins are always attraction-eligible (30 Magnetic Coil / 34 Room Sweep).
    /// </summary>
    public sealed class CoinPickup : MonoBehaviour, IInteractable, IAttractablePickup, IInteractionPrompt
    {
        [SerializeField, Min(0)] private int _amount;
        private bool _collected;
        private bool _resolving;

        public int Amount => _amount;
        public bool IsCollected => _collected;
        public bool IsAttractionEligible => !_collected && _amount > 0;

        public event Action<CoinPickup, int> Collected;

        public void SetAmount(int amount)
        {
            _amount = Mathf.Max(0, amount);
        }

        /// <summary>Host authority collected this pile through the party distribution (no local receiver involved).</summary>
        public void MarkCollectedByAuthority()
        {
            if (_collected) return;
            _collected = true;
            Collected?.Invoke(this, _amount);
            Destroy(gameObject);
        }

        public bool CanInteract(GameObject interactor) => !_collected && !_resolving && _amount > 0 && interactor != null && interactor.GetComponent<ICoinReceiver>() != null;

        public string PromptFor(GameObject interactor) => CanInteract(interactor) ? $"TAKE {_amount} COINS" : string.Empty;

        public bool Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return false;
            var receiver = interactor.GetComponent<ICoinReceiver>();

            // Re-entrant callbacks during the credit (or a duplicate same-frame interaction) see a resolving pile.
            _resolving = true;
            try
            {
                if (!receiver.TryReceiveCoins(_amount)) return false;

                _collected = true;
                Collected?.Invoke(this, _amount);
                Destroy(gameObject);
                return true;
            }
            finally
            {
                _resolving = false;
            }
        }
    }

    public interface ICoinReceiver
    {
        bool TryReceiveCoins(int amount);
    }
}
