using System;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>
    /// The armored transit in the Boss Room (60_EXTRACTION_TRANSIT steps 4–6). Inactive until the bound BossEncounter
    /// reports defeat; then boarding (Interact) surfaces the decision and choices are forwarded to the ExpeditionService.
    /// Repeated boarding/choices after resolution are no-ops.
    /// </summary>
    public sealed class TransitCar : MonoBehaviour, IInteractable, IInteractionPrompt
    {
        [SerializeField] private BossEncounter _bossEncounter;

        private ExpeditionService _service;
        private bool _boarded;

        public bool IsActivated { get; private set; }
        public bool IsBoarded => _boarded;
        public TransitDecision Decision => _service?.Transit;

        public event Action<TransitCar> Activated;
        public event Action<TransitCar> Boarded;

        public void Configure(ExpeditionService service, BossEncounter bossEncounter = null)
        {
            _service = service;
            if (bossEncounter != null)
            {
                if (_bossEncounter != null) _bossEncounter.BossDefeated -= HandleBossDefeated;
                _bossEncounter = bossEncounter;
                _bossEncounter.BossDefeated += HandleBossDefeated;
            }
        }

        private void Awake()
        {
            if (_bossEncounter != null) _bossEncounter.BossDefeated += HandleBossDefeated;
        }

        private void OnDestroy()
        {
            if (_bossEncounter != null) _bossEncounter.BossDefeated -= HandleBossDefeated;
        }

        private void HandleBossDefeated(BossEncounter encounter, int xp)
        {
            if (_service != null && _service.IsExpeditionActive)
            {
                _service.RecordBossDefeated(xp);
            }

            Activate();
        }

        public void Activate()
        {
            if (IsActivated) return;
            IsActivated = true;
            Activated?.Invoke(this);
        }

        public bool CanInteract(GameObject interactor) => IsActivated && !_boarded && _service != null && _service.Transit != null && _service.Transit.State == TransitDecisionState.Open;

        /// <summary>The HUD prompt for the one Interact action (ui/90): boarding is offered only while the decision is open.</summary>
        public string PromptFor(GameObject interactor) => CanInteract(interactor) ? "BOARD TRANSIT" : string.Empty;

        public bool Interact(GameObject interactor)
        {
            if (!CanInteract(interactor)) return false;
            _boarded = true;
            Boarded?.Invoke(this);
            return true;
        }

        /// <summary>Forwards the local player's choice; false when not boarded or already resolved.</summary>
        public bool Choose(TransitChoice choice)
        {
            if (!_boarded || _service == null) return false;
            return _service.ChooseTransit(choice);
        }
    }
}
