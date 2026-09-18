using System;
using RuinRail.Gameplay.Enemies.Bosses;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Boss Room engagement (55/46): the fight starts once when the first player enters (the boss acquires its
    /// target), doors stay locked until BossDefeated, which is the single downstream trigger for the Boss Cache and
    /// Transit Car hooks. Completed fires exactly once.
    /// </summary>
    public sealed class BossEngagement : IRoomEngagement
    {
        private readonly BossEncounter _encounter;
        private bool _raised;

        public BossEngagement(BossEncounter encounter)
        {
            _encounter = encounter != null ? encounter : throw new ArgumentNullException(nameof(encounter));
            _encounter.BossDefeated += OnDefeated;
            if (_encounter.IsDefeated) Raise();
        }

        public BossEncounter Encounter => _encounter;
        public bool IsComplete => _encounter.IsDefeated;
        public event Action<IRoomEngagement> Completed;

        public void Begin(RoomRuntime room, GameObject enteringPlayer)
        {
            if (_encounter.Boss != null && enteringPlayer != null) _encounter.Boss.SetTarget(enteringPlayer.transform);
        }

        public void Tick(float deltaTime)
        {
        }

        private void OnDefeated(BossEncounter encounter, int xp) => Raise();

        private void Raise()
        {
            if (_raised) return;
            _raised = true;
            _encounter.BossDefeated -= OnDefeated;
            Completed?.Invoke(this);
        }
    }
}
