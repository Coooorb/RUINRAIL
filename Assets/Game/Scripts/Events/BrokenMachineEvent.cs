using System;
using RuinRail.Gameplay.Economy;

namespace RuinRail.Gameplay.Events
{
    /// <summary>
    /// 57.3 Broken Machine: pay Carried Coins (77: 100 + 10 × (Depth − 1), capped at 400) to attempt a repair. The
    /// seeded attempt either yields one item/ammo/consumable from the authored machine table or simply fails; the
    /// coins are spent either way (a small gamble), there is no hidden punishment and exactly one attempt per depth.
    /// The outcome is fixed by the event context, so host and tests agree before any coin moves.
    /// </summary>
    public sealed class BrokenMachineEvent : DungeonEventBase
    {
        private const int RepairSalt = 0x5250; // "RP"

        private readonly DungeonEventConfig _config;
        private readonly PriceService _prices;
        private readonly EventRewardRoller _rewards;
        private readonly IRewardDeliverer _deliverer;

        public BrokenMachineEvent(DungeonEventContext context, DungeonEventConfig config, PriceService prices, EventRewardRoller rewards, IRewardDeliverer deliverer)
            : base(DungeonEventKind.BrokenMachine, context)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _prices = prices ?? throw new ArgumentNullException(nameof(prices));
            _rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            _deliverer = deliverer ?? throw new ArgumentNullException(nameof(deliverer));
        }

        public override int CostCoins => _prices.EventPrice(DungeonEventPriceKind.BrokenMachine, Context.Depth);

        /// <summary>The attempt's outcome as fixed by the seed (pure; does not consume anything).</summary>
        public bool WillRepairSucceed() => Context.Random(RepairSalt).NextInt(100) < _config.BrokenMachineSuccessPercent;

        protected override DungeonEventResult OnActivate(EventActor actor)
        {
            var cost = CostCoins;
            if (actor.Wallet == null || actor.Wallet.Domain != CoinDomain.Carried)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.Unavailable, detail: "carried_wallet_required");
            }

            var repaired = WillRepairSucceed();
            var loot = repaired ? _rewards.Roll(Context, _config.BrokenMachineTable, _config.BrokenMachineQuality) : null;
            if (loot != null) loot.Coins = 0; // 57: item/ammo/consumable or nothing — never coins back.

            if (cost > 0 && !actor.Wallet.Debit(cost, $"event:broken_machine:d{Context.Depth}").Success)
            {
                return new DungeonEventResult(Kind, EventIndex, DungeonEventOutcome.InsufficientFunds);
            }

            if (!repaired) return Failed(cost, "repair_failed");
            _deliverer.Deliver(loot);
            return Success(cost, loot, "repaired");
        }
    }
}
