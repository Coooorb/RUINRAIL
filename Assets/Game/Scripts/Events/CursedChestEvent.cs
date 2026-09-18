using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// 57.1 Cursed Chest: the player chooses to open it; the doors lock, a harder encounter (normal budget × threat
    /// scale, same seeded composition path) spawns, and clearing it awards high-quality loot into the shared world.
    /// The room runtime hosts the encounter: it receives the plan through EncounterStarted and reports the outcome
    /// back. There is no lingering curse — the only effect is the documented encounter and reward.
    /// </summary>
    public sealed class CursedChestEvent : DungeonEventBase
    {
        private readonly DungeonEventConfig _config;
        private readonly EventRewardRoller _rewards;
        private readonly IRewardDeliverer _deliverer;
        private readonly IEnumerable<EnemyDefinition> _archetypes;
        private readonly IReadOnlyList<string> _roomTags;

        public CursedChestEvent(DungeonEventContext context, DungeonEventConfig config, EventRewardRoller rewards, IRewardDeliverer deliverer, IEnumerable<EnemyDefinition> archetypes, IReadOnlyList<string> roomTags = null)
            : base(DungeonEventKind.CursedChest, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _deliverer = deliverer ?? throw new ArgumentNullException(nameof(deliverer));
            _archetypes = archetypes ?? throw new ArgumentNullException(nameof(archetypes));
            _roomTags = roomTags;
        }

        /// <summary>True while the cursed encounter is unresolved (room doors stay locked).</summary>
        public bool DoorsLocked => Phase == DungeonEventPhase.InProgress;
        public EncounterPlan Plan { get; private set; }

        public event Action<CursedChestEvent, EncounterPlan> EncounterStarted;

        /// <summary>The harder encounter this chest would spawn (pure, for previews/tests).</summary>
        public EncounterPlan ComposePlan() => EncounterDirector.Compose(Context.ForEncounter(_roomTags), _archetypes, _config.CursedChestThreatScale);

        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            Plan = ComposePlan();
            EncounterStarted?.Invoke(this, Plan);
            return Started(detail: Plan.Signature);
        }

        /// <summary>Room runtime hook: the cursed encounter was cleared. Pays out once; ignored in any other phase.</summary>
        public bool ReportEncounterCleared()
        {
            if (Phase != DungeonEventPhase.InProgress) return false;
            var loot = _rewards.Roll(Context, _config.CursedChestLootSource, _config.CursedChestQuality);
            _deliverer.Deliver(loot);
            return Finish(Success(0, loot, Plan?.Signature));
        }

        /// <summary>Room runtime hook: the party lost the encounter (wipe). No reward; the chest stays cursed-and-spent.</summary>
        public bool ReportEncounterFailed()
        {
            if (Phase != DungeonEventPhase.InProgress) return false;
            return Finish(Failed(0, Plan?.Signature));
        }
    }
}
