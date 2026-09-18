using System;
using RuinRail.Gameplay.Economy;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// 57.2 Locked Vault: pay Carried Coins (77: 250 + 25 × (Depth − 1), capped at 1,000) to open a vault with
    /// guaranteed good loot. The debit and the payout are one transaction: the reward is rolled before the debit,
    /// nothing is delivered unless the debit succeeded, and a failed payment leaves the vault locked and untouched.
    /// Pays out exactly once for the depth.
    /// </summary>
    public sealed class LockedVaultEvent : DungeonEventBase
    {
        private readonly DungeonEventConfig _config;
        private readonly PriceService _prices;
        private readonly EventRewardRoller _rewards;
        private readonly IRewardDeliverer _deliverer;

        public LockedVaultEvent(DungeonEventContext context, DungeonEventConfig config, PriceService prices, EventRewardRoller rewards, IRewardDeliverer deliverer)
            : base(DungeonEventKind.LockedVault, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _deliverer = deliverer ?? throw new ArgumentNullException(nameof(deliverer));
        }

        public override int CostCoins => _prices.EventPrice(DungeonEventPriceKind.LockedVault, Context.Depth);
        public bool IsLocked => Phase != DungeonEventPhase.Completed;
        public string PaidBy { get; private set; }

        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            var cost = CostCoins;
            var loot = _rewards.Roll(Context, _config.VaultLootSource, _config.VaultQuality);
            if (actor.Wallet == null || actor.Wallet.Domain != CoinDomain.Carried)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "carried_wallet_required");
            }

            if (cost > 0 && !actor.Wallet.Debit(cost, $"event:locked_vault:d{Context.Depth}").Success)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            PaidBy = actor.ParticipantId;
            _deliverer.Deliver(loot);
            return Success(cost, loot);
        }
    }
}
