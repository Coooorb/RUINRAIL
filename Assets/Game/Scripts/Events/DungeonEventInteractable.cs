using System;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// World-side handle of a dungeon event: the Interact input activates the bound event with the interacting player's
    /// Carried wallet and backpack (57 Co-op: the triggering player pays). Repeated interaction on a used event is a
    /// no-op; the event's own phase guard makes duplicate callbacks harmless.
    /// </summary>
    public sealed class DungeonEventInteractable : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        private IDungeonEvent _event;

        public IDungeonEvent Event => _event;
        public DungeonEventResult LastResult { get; private set; }
        public EventActor LastActor { get; private set; }

        public event Action<DungeonEventInteractable, DungeonEventResult> Activated;

        /// <summary>
        /// The one seam from the world into a choice screen (57.6 Weapon Cache), the counterpart of the merchant's
        /// <c>Opened</c>: the bound event answered the Interact press with
        /// <see cref="DungeonEventDetails.ChoiceRequired"/>, so the press opened a selection rather than resolving the
        /// event. The event is untouched and still Available; the screen calls its Choose when the player picks.
        /// Without a subscriber the press does nothing at all — which is exactly what it did before this existed.
        /// </summary>
        public event Action<DungeonEventInteractable, EventActor> ChoiceRequested;

        /// <summary>How often the interact press asked for a choice screen (proof/diagnostics).</summary>
        public int ChoiceRequests { get; private set; }

        public void Bind(IDungeonEvent dungeonEvent) => _event = dungeonEvent;

        public static EventActor ActorFor(GameObject interactor)
        {
            if (interactor == null) return null;
            var receiver = interactor.GetComponent<PlayerLootReceiver>();
            if (receiver == null) return null;
            var health = interactor.GetComponent<HealthComponent>();
            return new EventActor(receiver.Wallet, receiver.Backpack, "local", interactor, health != null ? new HealthComponentPatient(health) : null);
        }

        public EventPrompt PromptFor(GameObject interactor)
        {
            var actor = ActorFor(interactor);
            return EventPromptBuilder.Build(_event, actor?.Wallet?.Balance ?? 0);
        }

        public bool CanInteract(GameObject interactor) => _event != null && _event.CanActivate(ActorFor(interactor));

        /// <summary>The HUD line for the one Interact prompt: "&lt;ACTION&gt; &lt;TITLE&gt;" plus the Carried-coin cost when the event charges one.</summary>
        string IInteractionPrompt.PromptFor(GameObject interactor)
        {
            if (_event == null || !CanInteract(interactor)) return string.Empty;
            var prompt = PromptFor(interactor);
            var text = prompt.ActionLabel + " " + prompt.Title;
            if (prompt.HasCost) text += $" ({prompt.CostCoins} COINS)";
            return text.ToUpperInvariant();
        }

        public bool Interact(GameObject interactor)
        {
            if (_event == null) return false;
            var actor = ActorFor(interactor);
            if (actor == null) return false;
            LastActor = actor;
            var result = _event.Activate(actor);
            LastResult = result;
            if (result.Outcome == DungeonEventOutcome.None) return false;
            if (result.Detail == DungeonEventDetails.ChoiceRequired)
            {
                // A choice event answers the press by asking for its screen; the press succeeded even though the
                // event itself resolved nothing yet.
                ChoiceRequests++;
                Activated?.Invoke(this, result);
                ChoiceRequested?.Invoke(this, actor);
                return true;
            }

            Activated?.Invoke(this, result);
            return result.Outcome == DungeonEventOutcome.Started || result.Outcome == DungeonEventOutcome.Success || result.Outcome == DungeonEventOutcome.Failed;
        }
    }
}
