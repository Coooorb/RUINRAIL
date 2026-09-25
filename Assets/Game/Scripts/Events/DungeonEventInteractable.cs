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
            // The actor is the interacting member (84/co-op): per-participant limits (Medical Station heals) and the
            // Weapon Cache's chooser must name who acted, not one shared "local" for the whole party.
            var life = interactor.GetComponent<PlayerLifeStateComponent>();
            var participant = life != null && !string.IsNullOrEmpty(life.ParticipantId) ? life.ParticipantId : "local";
            return new EventActor(receiver.Wallet, receiver.Backpack, participant, interactor, health != null ? new HealthComponentPatient(health) : null);
        }

        public EventPrompt PromptFor(GameObject interactor)
        {
            var actor = ActorFor(interactor);
            return EventPromptBuilder.Build(_event, actor?.Wallet?.Balance ?? 0);
        }

        /// <summary>
        /// The object answers the Interact press while its event is still open (Available, or a multi-use service
        /// between uses), whether or not this player can afford or use it right now: the prompt then says why the
        /// press will be refused instead of the object silently showing nothing (57: a prompt that exists but does
        /// nothing, and an object that does nothing without a prompt, are both failures). Resolved events are inert.
        /// </summary>
        public bool CanInteract(GameObject interactor) => _event != null && interactor != null && _event.Phase == DungeonEventPhase.Available && ActorFor(interactor) != null;

        /// <summary>True when the press would actually activate the event for this player (affordable, usable).</summary>
        public bool CanActivate(GameObject interactor) => _event != null && _event.CanActivate(ActorFor(interactor));

        /// <summary>Why the press would be refused for this player right now, or empty when it would go through.</summary>
        public string RefusalFor(GameObject interactor)
        {
            if (_event == null) return string.Empty;
            var actor = ActorFor(interactor);
            if (actor == null) return string.Empty;
            if (_event.Phase != DungeonEventPhase.Available) return "USED";
            if (_event.CanActivate(actor)) return string.Empty;
            return EventPromptBuilder.RefusalReason(_event, actor);
        }

        /// <summary>The HUD line for the one Interact prompt: "&lt;ACTION&gt; &lt;TITLE&gt;" plus the Carried-coin cost when the event charges one, and the refusal reason when the press would be refused.</summary>
        string IInteractionPrompt.PromptFor(GameObject interactor)
        {
            if (_event == null || !CanInteract(interactor)) return string.Empty;
            var prompt = PromptFor(interactor);
            var text = prompt.ActionLabel + " " + prompt.Title;
            if (prompt.HasCost) text += $" ({prompt.CostCoins} COINS)";
            var refusal = RefusalFor(interactor);
            if (!string.IsNullOrEmpty(refusal)) text += " — " + refusal;
            return text.ToUpperInvariant();
        }

        /// <summary>Raised when a press reached the event and was refused (not enough coins, nothing to heal, no uses left): the HUD tells the player why.</summary>
        public event Action<DungeonEventInteractable, EventActor, DungeonEventResult> Refused;
        public int Refusals { get; private set; }

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

            if (result.Outcome == DungeonEventOutcome.InsufficientFunds || result.Outcome == DungeonEventOutcome.Unavailable)
            {
                Refusals++;
                Refused?.Invoke(this, actor, result);
                return false;
            }

            Activated?.Invoke(this, result);
            return result.Outcome == DungeonEventOutcome.Started || result.Outcome == DungeonEventOutcome.Success || result.Outcome == DungeonEventOutcome.Failed;
        }
    }
}
