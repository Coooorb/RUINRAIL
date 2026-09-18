using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEngine;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// 57.4 Supply Signal: activate, then survive a ~30 s wave encounter (tunable). Waves are seeded encounter plans
    /// (one at activation, then one per interval) handed to the room runtime through WaveStarted; the room ticks the
    /// timer. Surviving the full duration delivers a Supply Chest reward into the shared world exactly once; a party
    /// wipe fails the signal. Enemies still alive at the end do not block the delivery — the condition is survival.
    /// </summary>
    public sealed class SupplySignalEvent : DungeonEventBase
    {
        private readonly DungeonEventConfig _config;
        private readonly EventRewardRoller _rewards;
        private readonly IRewardDeliverer _deliverer;
        private readonly IEnumerable<EnemyDefinition> _archetypes;
        private readonly IReadOnlyList<string> _roomTags;
        private readonly List<EncounterPlan> _waves = new();

        public SupplySignalEvent(DungeonEventContext context, DungeonEventConfig config, EventRewardRoller rewards, IRewardDeliverer deliverer, IEnumerable<EnemyDefinition> archetypes, IReadOnlyList<string> roomTags = null)
            : base(DungeonEventKind.SupplySignal, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _deliverer = deliverer ?? throw new ArgumentNullException(nameof(deliverer));
            _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
            _roomTags = roomTags;
        }

        public float DurationSeconds => _config.SupplySignalWaveSeconds;
        public float WaveInterval => _config.SupplySignalWaveInterval;
        public float Elapsed { get; private set; }
        public float Remaining => Mathf.Max(0f, DurationSeconds - Elapsed);
        public bool IsRunning => Phase == DungeonEventPhase.InProgress;
        public IReadOnlyList<EncounterPlan> Waves => _waves;

        /// <summary>Number of waves the signal will raise over its duration (activation wave + one per interval).</summary>
        public int PlannedWaveCount => 1 + Mathf.FloorToInt((DurationSeconds - 0.0001f) / WaveInterval);

        public event Action<SupplySignalEvent, EncounterPlan> WaveStarted;

        public EncounterPlan ComposeWave(int wave) => EncounterDirector.Compose(Context.ForEncounter(_roomTags, 0, wave), _archetypes);

        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            Elapsed = 0f;
            RaiseWave();
            return Started(detail: _waves[0].Signature);
        }

        /// <summary>Advances the signal timer (room runtime); raises due waves and resolves at the end of the duration.</summary>
        public void Tick(float deltaTime)
        {
            if (!IsRunning || deltaTime <= 0f) return;
            Elapsed += deltaTime;
            while (_waves.Count < PlannedWaveCount && Elapsed >= _waves.Count * WaveInterval)
            {
                RaiseWave();
            }

            if (Elapsed >= DurationSeconds) ReportSurvived();
        }

        private void RaiseWave()
        {
            var plan = ComposeWave(_waves.Count);
            _waves.Add(plan);
            WaveStarted?.Invoke(this, plan);
        }

        /// <summary>Terminal success: the party survived the duration. Pays out once.</summary>
        public bool ReportSurvived()
        {
            if (!IsRunning) return false;
            var loot = _rewards.Roll(Context, _config.SupplySignalLootSource, _config.SupplySignalQuality);
            _deliverer.Deliver(loot);
            return Finish(Success(0, loot, $"survived:{_waves.Count}"));
        }

        /// <summary>Terminal failure: the party was wiped during the signal. No reward.</summary>
        public bool ReportPartyWiped()
        {
            if (!IsRunning) return false;
            return Finish(Failed(0, "wiped"));
        }
    }
}
