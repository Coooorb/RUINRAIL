using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Expedition
{
    public enum ExpeditionOutcome
    {
        InProgress,
        Extracted,
        Failed
    }

    /// <summary>Immutable identity of one depth run: what the dungeon generator/assembler and loot contexts consume.</summary>
    public readonly struct DepthRunContext
    {
        public DepthRunContext(int runSeed, int depth, Biome biome)
        {
            RunSeed = runSeed;
            Depth = depth;
            Biome = biome;
        }

        public int RunSeed { get; }
        public int Depth { get; }
        public Biome Biome { get; }
    }

    /// <summary>Concise numbers for the expedition summary screen (base/76_EXPEDITION_SUMMARY).</summary>
    public sealed class ExpeditionStats
    {
        public int DepthReached = 1;
        public int RoomsCleared;
        public int EnemiesDefeated;
        public int ElitesDefeated;
        public int BossesDefeated;
        public int XpEarned;
    }

    /// <summary>
    /// Mutable per-expedition (at-risk) domain: the carried inventory, carried coins, current depth/biome and stats.
    /// Everything here is lost on failure and only becomes safe through ExpeditionService.Return.
    /// </summary>
    public sealed class ExpeditionState
    {
        public ExpeditionState(int runSeed, Biome firstBiome, PlayerInventory inventory, string transactionId = null, int startingPartySize = 1)
        {
            TransactionId = string.IsNullOrEmpty(transactionId) ? System.Guid.NewGuid().ToString("N") : transactionId;
            RunSeed = runSeed;
            StartingPartySize = System.Math.Clamp(startingPartySize, 1, 3);
            Depth = 1;
            Biome = firstBiome;
            Inventory = inventory;
            BiomeHistory.Add(firstBiome);
        }

        /// <summary>Identity of this at-risk transaction: Start opens it, exactly one Return or Fail closes it.</summary>
        public string TransactionId { get; }
        public int RunSeed { get; }
        /// <summary>83: co-op scaling is fixed to the party size at Start; deaths/disconnects never lower it.</summary>
        public int StartingPartySize { get; }
        public int Depth { get; private set; }
        public Biome Biome { get; private set; }
        public PlayerInventory Inventory { get; }
        /// <summary>Carried Coins (77_ECONOMY): the expedition's own wallet, lost on failure, banked only by Return.</summary>
        public CoinWallet CarriedWallet { get; } = new(CoinDomain.Carried);
        public int CarriedCoins => CarriedWallet.Balance;
        /// <summary>Banked Coins this player moved into the Carried wallet at Start (at risk like any carried coin).</summary>
        public int CoinsBroughtIn { get; internal set; }
        public ExpeditionOutcome Outcome { get; private set; } = ExpeditionOutcome.InProgress;
        public ExpeditionStats Stats { get; } = new();
        public List<Biome> BiomeHistory { get; } = new();
        public bool IsActive => Outcome == ExpeditionOutcome.InProgress;
        public bool BossDefeatedThisDepth { get; private set; }
        public DepthRunContext CurrentDepth => new(RunSeed, Depth, Biome);

        internal void AddCarriedCoins(int amount)
        {
            if (amount > 0) CarriedWallet.Credit(amount, "pickup");
        }

        internal void AddXp(int amount)
        {
            if (amount > 0) Stats.XpEarned += amount;
        }

        internal void MarkBossDefeated()
        {
            BossDefeatedThisDepth = true;
            Stats.BossesDefeated++;
        }

        internal void Descend(Biome nextBiome)
        {
            Depth++;
            Biome = nextBiome;
            BossDefeatedThisDepth = false;
            Stats.DepthReached = Depth;
            BiomeHistory.Add(nextBiome);
        }

        internal void Close(ExpeditionOutcome outcome)
        {
            Outcome = outcome;
        }
    }
}
