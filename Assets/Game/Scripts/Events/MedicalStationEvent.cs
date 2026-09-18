using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;

namespace RuinRail.Gameplay.Events
{
    /// <summary>Someone the Medical Station can heal: current/max HP and a clamped heal.</summary>
    public interface IMedicalPatient
    {
        int CurrentHealth { get; }
        int MaxHealth { get; }
        bool IsAlive { get; }
        bool Heal(int amount);
    }

    /// <summary>Adapter over the player's HealthComponent.</summary>
    public sealed class HealthComponentPatient : IMedicalPatient
    {
        private readonly HealthComponent _health;

        public HealthComponentPatient(HealthComponent health)
        {
            _health = health != null ? health : throw new ArgumentNullException(nameof(health));
        }

        public int CurrentHealth => _health.CurrentHealth;
        public int MaxHealth => _health.MaxHealth;
        public bool IsAlive => _health.IsAlive;
        public bool Heal(int amount) => _health.Heal(amount);
    }

    /// <summary>
    /// The authoritative Dead-player bookkeeping the station asks before charging a revive (84). Implemented by the
    /// party/downed systems (TASKS 102–105); absent in Solo, where a revive can never be bought.
    /// </summary>
    public interface IReviveAuthority
    {
        bool IsDead(string participantId);

        /// <summary>Returns the participant at the given percent of Max HP; false when nothing changed.</summary>
        bool Revive(string participantId, int healthPercent);
    }

    /// <summary>
    /// 57.5 Medical Station: pay Carried Coins to heal (77: 150 + 15 × (Depth − 1), cap 600) or, in co-op, to revive a
    /// fully Dead teammate (500 + 30 × (Depth − 1), cap 1,500; back at ~30% HP per 84). A heal restores the buyer to
    /// Max HP and is only charged when there is something to heal; a revive is only charged when the authority confirms
    /// a Dead target and the revive actually happens (a refused revive refunds nothing because it was never debited).
    /// Uses are counted per depth (heal per participant, revives per station; V1 FINAL (TASK 179) counts in config).
    /// </summary>
    public sealed class MedicalStationEvent : DungeonEventBase
    {
        private readonly DungeonEventConfig _config;
        private readonly PriceService _prices;
        private readonly Dictionary<string, int> _healsByParticipant = new();
        private IReviveAuthority _reviveAuthority;

        public MedicalStationEvent(DungeonEventContext context, DungeonEventConfig config, PriceService prices, IReviveAuthority reviveAuthority = null)
            : base(DungeonEventKind.MedicalStation, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _reviveAuthority = reviveAuthority;
        }

        public int HealCost => _prices.EventPrice(DungeonEventPriceKind.MedicalStationHeal, Context.Depth);
        public int ReviveCost => _prices.EventPrice(DungeonEventPriceKind.MedicalStationRevive, Context.Depth);

        /// <summary>The default interaction is the heal purchase.</summary>
        public override int CostCoins => HealCost;

        public int RevivesUsed { get; private set; }
        public int RevivesRemaining => Math.Max(0, _config.MedicalReviveUses - RevivesUsed);
        public int TotalHeals { get; private set; }

        public event Action<MedicalStationEvent, string, int> Healed;
        public event Action<MedicalStationEvent, string, string, int> Revived;

        public void SetReviveAuthority(IReviveAuthority authority) => _reviveAuthority = authority;

        public int HealsUsedBy(string participantId) => _healsByParticipant.TryGetValue(participantId ?? string.Empty, out var n) ? n : 0;
        public int HealsRemainingFor(string participantId) => Math.Max(0, _config.MedicalHealUsesPerParticipant - HealsUsedBy(participantId));

        public override bool CanActivate(EventActor actor)
        {
            return actor?.Patient != null && actor.Patient.IsAlive && actor.Patient.CurrentHealth < actor.Patient.MaxHealth
                   && HealsRemainingFor(actor.ParticipantId) > 0 && actor.Wallet != null && actor.Wallet.Domain == CoinDomain.Carried && actor.Wallet.CanAfford(HealCost)
                   && Phase != DungeonEventPhase.Completed && Phase != DungeonEventPhase.Failed;
        }

        // The station is a multi-use service: activation is a heal purchase and never leaves the Available phase.
        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            var patient = actor.Patient;
            if (patient == null || !patient.IsAlive || actor.Wallet == null || actor.Wallet.Domain != CoinDomain.Carried)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "no_patient");
            }

            if (HealsRemainingFor(actor.ParticipantId) <= 0)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "heal_uses_spent");
            }

            var missing = patient.MaxHealth - patient.CurrentHealth;
            if (missing <= 0)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "already_full");
            }

            var cost = HealCost;
            if (cost > 0 && !actor.Wallet.Debit(cost, $"event:medical_heal:d{Context.Depth}").Success)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            if (!patient.Heal(missing))
            {
                if (cost > 0) actor.Wallet.Credit(cost, "event:medical_heal_refund");
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "heal_rejected");
            }

            _healsByParticipant[actor.ParticipantId ?? string.Empty] = HealsUsedBy(actor.ParticipantId) + 1;
            TotalHeals++;
            Healed?.Invoke(this, actor.ParticipantId, missing);
            // Not terminal: the station stays available for other participants/revives (returns a non-Started
            // non-terminal outcome so the base machine restores Available).
            return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Success, cost, null, $"healed:{missing}").AsNonTerminal();
        }

        /// <summary>
        /// Authoritative revive request hook (84): charges the requester's Carried Coins only when the target is
        /// confirmed Dead by the authority and the revive succeeds. Solo (no authority) can never buy one.
        /// </summary>
        public DungeonEventResult RequestRevive(EventActor requester, string targetParticipantId)
        {
            if (requester?.Wallet == null || requester.Wallet.Domain != CoinDomain.Carried)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "no_requester");
            }

            if (_reviveAuthority == null || string.IsNullOrEmpty(targetParticipantId) || !_reviveAuthority.IsDead(targetParticipantId))
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "target_not_dead");
            }

            if (RevivesRemaining <= 0)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "revive_uses_spent");
            }

            var cost = ReviveCost;
            if (!requester.Wallet.CanAfford(cost))
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            if (cost > 0 && !requester.Wallet.Debit(cost, $"event:medical_revive:d{Context.Depth}").Success)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            if (!_reviveAuthority.Revive(targetParticipantId, _config.ReviveHealthPercent))
            {
                if (cost > 0) requester.Wallet.Credit(cost, "event:medical_revive_refund");
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "revive_rejected");
            }

            RevivesUsed++;
            Revived?.Invoke(this, requester.ParticipantId, targetParticipantId, cost);
            return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Success, cost, null, $"revived:{targetParticipantId}").AsNonTerminal();
        }
    }
}
