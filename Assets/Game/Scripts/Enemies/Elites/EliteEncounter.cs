using System;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Elites
{
    /// <summary>
    /// One Elite encounter = exactly one Elite actor (45_ELITES: only one Elite is fought at a time). Relays the
    /// actor's start/death into encounter-level Started/Completed signals that the room lifecycle, audio and loot consume.
    /// Completes exactly once and exposes the XP the Elite awards.
    /// </summary>
    public sealed class EliteEncounter : MonoBehaviour
    {
        [SerializeField] private EliteController _elite;

        private bool _started;
        private bool _completed;

        public EliteController Elite => _elite;
        public bool IsStarted => _started;
        public bool IsCompleted => _completed;
        public int XpAwarded => _completed && _elite != null ? _elite.XpValue : 0;

        public event Action<EliteEncounter> Started;
        public event Action<EliteEncounter, int> Completed;

        public void Bind(EliteController elite)
        {
            if (elite == null) throw new ArgumentNullException(nameof(elite));
            if (_elite != null && _elite != elite) throw new InvalidOperationException("An Elite encounter holds exactly one Elite actor.");
            Unbind();
            _elite = elite;
            _elite.EncounterStartedEvent += HandleStarted;
            _elite.Died += HandleDied;
        }

        private void Awake()
        {
            if (_elite == null) _elite = GetComponentInChildren<EliteController>();
            if (_elite != null) Bind(_elite);
        }

        private void OnDestroy()
        {
            Unbind();
        }

        private void Unbind()
        {
            if (_elite == null) return;
            _elite.EncounterStartedEvent -= HandleStarted;
            _elite.Died -= HandleDied;
        }

        private void HandleStarted(MovesetActorController actor)
        {
            if (_started) return;
            _started = true;
            Started?.Invoke(this);
        }

        private void HandleDied(MovesetActorController actor)
        {
            if (_completed) return;
            _completed = true;
            Completed?.Invoke(this, actor.XpValue);
        }
    }
}
