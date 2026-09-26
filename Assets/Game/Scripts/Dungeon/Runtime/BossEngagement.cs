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
        /// <summary>
        /// How long the boss holds back after the first player enters, so the room introduction can establish the arena
        /// and name the boss before the fight: the boss acquires no target (so it cannot attack) until the hold ends or
        /// the introduction is skipped. Presentation timing, not balance: nothing about the fight itself changes.
        /// </summary>
        public const float DefaultIntroHoldSeconds = 2.2f;

        private readonly BossEncounter _encounter;
        private readonly float _introHoldSeconds;
        private Transform _pendingTarget;
        private float _holdRemaining;
        private bool _raised;

        public BossEngagement(BossEncounter encounter, float introHoldSeconds = 0f)
        {
            _encounter = encounter != null ? encounter : throw new ArgumentNullException(nameof(encounter));
            _introHoldSeconds = Mathf.Max(0f, introHoldSeconds);
            _encounter.BossDefeated += OnDefeated;
            if (_encounter.IsDefeated) Raise();
        }

        public BossEncounter Encounter => _encounter;
        public bool IsComplete => _encounter.IsDefeated;
        public event Action<IRoomEngagement> Completed;

        /// <summary>True while the boss waits out the room introduction (it has no target yet).</summary>
        public bool IsHoldingForIntro => _pendingTarget != null;
        public float IntroHoldSeconds => _introHoldSeconds;

        /// <summary>Raised once when the entering player is known, before the hold (the room introduction starts here).</summary>
        public event Action<BossEngagement, GameObject> IntroStarted;

        public void Begin(RoomRuntime room, GameObject enteringPlayer)
        {
            if (_encounter.Boss == null || enteringPlayer == null) return;
            if (_introHoldSeconds <= 0f)
            {
                _encounter.Boss.SetTarget(enteringPlayer.transform);
                return;
            }

            _pendingTarget = enteringPlayer.transform;
            _holdRemaining = _introHoldSeconds;
            IntroStarted?.Invoke(this, enteringPlayer);
        }

        public void Tick(float deltaTime)
        {
            if (_pendingTarget == null) return;
            _holdRemaining -= deltaTime;
            if (_holdRemaining <= 0f) EndIntro();
        }

        /// <summary>Ends the hold now (the introduction finished or was skipped): the boss acquires the entering player.</summary>
        public void EndIntro()
        {
            if (_pendingTarget == null) return;
            var target = _pendingTarget;
            _pendingTarget = null;
            if (_encounter.Boss != null && _encounter.Boss.IsAlive && target != null) _encounter.Boss.SetTarget(target);
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
