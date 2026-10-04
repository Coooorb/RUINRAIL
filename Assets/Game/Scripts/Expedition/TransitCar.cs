using System;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>
    /// The Boss Room's transit hook (60_EXTRACTION_TRANSIT steps 4–6). There is no in-world transit object to board:
    /// once the bound BossEncounter reports defeat, this records the defeat on the ExpeditionService (which opens the
    /// Return / Descend decision the post-boss panel shows) and marks itself activated, the state co-op mirrors.
    /// It is not an interactable, has no collider and no prompt; choices go through the decision panel.
    /// </summary>
    public sealed class TransitCar : MonoBehaviour
    {
        [SerializeField] private BossEncounter _bossEncounter;

        private ExpeditionService _service;

        public bool IsActivated { get; private set; }
        public TransitDecision Decision => _service?.Transit;

        public event Action<TransitCar> Activated;

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

    }
}
