using System;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Bosses
{
    /// <summary>
    /// Boss encounter boundary for the Boss Room: exactly one boss, with BossStarted / PhaseChanged / BossDefeated
    /// raised once each. Downstream systems (room lock, Boss Cache, Transit Car, music, UI) subscribe here rather than
    /// to the actor directly. The defeat signal is the single source of truth for "boss beaten".
    /// </summary>
    public sealed class BossEncounter : MonoBehaviour
    {
        [SerializeField] private BossController _boss;

        private bool _started;
        private bool _defeated;

        public BossController Boss => _boss;
        public bool IsStarted => _started;
        public bool IsDefeated => _defeated;
        public int XpAwarded => _defeated && _boss != null ? _boss.XpValue : 0;

        public event Action<BossEncounter> BossStarted;
        public event Action<BossEncounter, int> PhaseChanged;
        public event Action<BossEncounter, int> BossDefeated;

        public void Bind(BossController boss)
        {
            if (boss == null) throw new ArgumentNullException(nameof(boss));
            if (_boss != null && _boss != boss) throw new InvalidOperationException("A Boss encounter holds exactly one boss actor.");
            Unbind();
            _boss = boss;
            _boss.EncounterStartedEvent += HandleStarted;
            _boss.PhaseChanged += HandlePhaseChanged;
            _boss.Died += HandleDied;
        }

        private void Awake()
        {
            if (_boss == null) _boss = GetComponentInChildren<BossController>();
            if (_boss != null) Bind(_boss);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (_boss == null) return;
            _boss.EncounterStartedEvent -= HandleStarted;
            _boss.PhaseChanged -= HandlePhaseChanged;
            _boss.Died -= HandleDied;
        }

        private void HandleStarted(MovesetActorController actor)
        {
            if (_started) return;
            _started = true;
            BossStarted?.Invoke(this);
        }

        private void HandlePhaseChanged(BossController boss, int phase)
        {
            PhaseChanged?.Invoke(this, phase);
        }

        private void HandleDied(MovesetActorController actor)
        {
            if (_defeated) return;
            _defeated = true;
            BossDefeated?.Invoke(this, actor.XpValue);
        }
    }
}
