using System;
using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// World handle of the Dungeon Merchant (58 Merchant Room): interacting opens the bound per-depth stock for the
    /// UI. Opening changes nothing — the stock and its sold state live in the DungeonMerchantService for the depth.
    /// </summary>
    public sealed class DungeonMerchantInteractable : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        private DungeonMerchantService _merchant;

        public DungeonMerchantService Merchant => _merchant;
        public int OpenCount { get; private set; }

        public event Action<DungeonMerchantInteractable, GameObject> Opened;

        public void Bind(DungeonMerchantService merchant) => _merchant = merchant;

        public bool CanInteract(GameObject interactor) => _merchant != null && interactor != null;

        public string PromptFor(GameObject interactor) => CanInteract(interactor) ? "TRADE WITH MERCHANT" : string.Empty;

        public bool Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return false;
            OpenCount++;
            Opened?.Invoke(this, interactor);
            return true;
        }
    }
}
