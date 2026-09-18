using UnityEngine;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>Something the Interact input can target. Implementations must be idempotent per state (re-firing is harmless).</summary>
    public interface IInteractable
    {
        bool CanInteract(GameObject interactor);
        bool Interact(GameObject interactor);
    }

    /// <summary>
    /// The one interaction prompt (ui/90 "One consistent Interact action"): an interactable that can describe what
    /// the Interact press will do for a given player. The HUD shows the nearest usable target's text; nothing here
    /// performs the interaction.
    /// </summary>
    public interface IInteractionPrompt
    {
        /// <summary>Short upper-case verb phrase ("OPEN CHEST", "TAKE LIGHT AMMO x24"); empty when nothing can be done.</summary>
        string PromptFor(GameObject interactor);
    }
}
