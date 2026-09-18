using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;

namespace RuinRail.Gameplay.Expedition
{
    /// <summary>One item as it appeared in an extraction/failure transaction (read-only line for the summary screen).</summary>
    public readonly struct ItemSummaryLine
    {
        public ItemSummaryLine(string instanceId, string definitionId, Rarity rarity, int quantity)
        {
            InstanceId = instanceId;
            DefinitionId = definitionId;
            Rarity = rarity;
            Quantity = quantity;
        }

        public string InstanceId { get; }
        public string DefinitionId { get; }
        public Rarity Rarity { get; }
        public int Quantity { get; }
    }

    /// <summary>
    /// Read-only result of one closed expedition (base/76_EXPEDITION_SUMMARY), built from the transaction results
    /// themselves (coin transfer receipt, secured/lost item lines) at the moment Return/Fail commits; never re-derived
    /// later, never able to re-apply anything. Safe to hold across scene transitions and to show more than once.
    /// </summary>
    public sealed class ExpeditionSummary
    {
        public ExpeditionSummary(int expeditionIndex, string transactionId, ExpeditionOutcome outcome, int runSeed, IReadOnlyList<Biome> biomes, ExpeditionStats stats,
            int levelBefore, int levelAfter, CoinReceipt coinResult, int coinsLost, IReadOnlyList<ItemSummaryLine> securedItems, IReadOnlyList<ItemSummaryLine> lostItems)
        {
            ExpeditionIndex = expeditionIndex;
            TransactionId = transactionId;
            Outcome = outcome;
            RunSeed = runSeed;
            Biomes = biomes;
            DepthReached = stats.DepthReached;
            RoomsCleared = stats.RoomsCleared;
            EnemiesDefeated = stats.EnemiesDefeated;
            ElitesDefeated = stats.ElitesDefeated;
            BossesDefeated = stats.BossesDefeated;
            XpEarned = stats.XpEarned;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            CoinResult = coinResult;
            CoinsLost = coinsLost;
            SecuredItems = securedItems;
            LostItems = lostItems;
        }

        /// <summary>Sequence number of this ended expedition on the profile (trader refresh / tutorial hooks).</summary>
        public int ExpeditionIndex { get; }

        /// <summary>The closed transaction this summary belongs to (ExpeditionState.TransactionId).</summary>
        public string TransactionId { get; }
        public ExpeditionOutcome Outcome { get; }
        public bool IsSuccess => Outcome == ExpeditionOutcome.Extracted;
        public int RunSeed { get; }
        public IReadOnlyList<Biome> Biomes { get; }
        public int DepthReached { get; }
        public int RoomsCleared { get; }
        public int EnemiesDefeated { get; }
        public int ElitesDefeated { get; }
        public int BossesDefeated { get; }
        public int XpEarned { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }

        /// <summary>The Carried-to-Banked transfer receipt (amount 0 on failure).</summary>
        public CoinReceipt CoinResult { get; }
        public int CoinsExtracted => CoinResult.Amount;
        public int CoinsLost { get; }
        public IReadOnlyList<ItemSummaryLine> SecuredItems { get; }
        public IReadOnlyList<ItemSummaryLine> LostItems { get; }
        public string[] ExtractedItemIds => SecuredItems.Select(i => i.InstanceId).ToArray();
    }

    /// <summary>
    /// Owns the solo expedition lifecycle and the two explicit transaction boundaries between the at-risk expedition
    /// domain and the permanent profile (113_SAVE_PERSISTENCE):
    ///   Start   — safe loadout leaves the profile and becomes at-risk carried state (no snapshot kept: anti-exploit).
    ///   Return  — carried items become safe (profile.SafeLoadout), carried coins bank, XP commits. Exactly once.
    ///   Fail    — carried items and coins are destroyed, XP still commits. Exactly once.
    /// Descend never touches the profile. Transit choice arrives through TransitDecision so co-op voting can replace
    /// the solo policy without changing these transactions.
    /// Each expedition is one transaction (ExpeditionState.TransactionId). Once closed, replayed Return/Fail callbacks
    /// are idempotent: they return the recorded summary and touch nothing.
    /// </summary>
    public sealed class ExpeditionService
    {
        private readonly Func<string, ItemDefinition> _resolveDefinition;
        private readonly Func<AmmoType, AmmoItemDefinition> _resolveAmmo;
        private readonly AmmoBalanceConfig _ammoBalance;
        private readonly ITransitResolutionPolicy _transitPolicy;
        private readonly string _localPlayerId;
        private Func<bool> _isLocalPlayerDead;
        private Func<IEnumerable<string>> _livingPlayerIds;
        private Func<IEnumerable<string>> _deadPlayerIds;
        private ITransitResolutionPolicy _partyPolicy;

        public ExpeditionService(
            Func<string, ItemDefinition> resolveDefinition,
            Func<AmmoType, AmmoItemDefinition> resolveAmmo,
            AmmoBalanceConfig ammoBalance,
            ITransitResolutionPolicy transitPolicy = null,
            string localPlayerId = "local")
        {
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
            _resolveAmmo = resolveAmmo ?? throw new ArgumentNullException(nameof(resolveAmmo));
            _ammoBalance = ammoBalance;
            _transitPolicy = transitPolicy ?? new SoloTransitPolicy();
            _localPlayerId = localPlayerId;
        }

        /// <summary>84/85: the party may Return while this peer's player is still Dead; then the local at-risk state is lost, not secured.</summary>
        public void SetLocalLifeCheck(Func<bool> isLocalPlayerDead) => _isLocalPlayerDead = isLocalPlayerDead;

        public bool IsLocalPlayerDead => _isLocalPlayerDead != null && _isLocalPlayerDead();

        /// <summary>
        /// 86: co-op voting. The living provider supplies the voters when the boss falls (Dead players are never voters),
        /// the dead provider feeds the Return warning, and the party policy replaces the solo one. Null = solo.
        /// </summary>
        public void SetPartyTransit(Func<IEnumerable<string>> livingPlayerIds, Func<IEnumerable<string>> deadPlayerIds, ITransitResolutionPolicy policy)
        {
            _livingPlayerIds = livingPlayerIds;
            _deadPlayerIds = deadPlayerIds;
            _partyPolicy = policy;
        }

        public PlayerProfile Profile { get; private set; }

        /// <summary>Banked-domain wallet over the profile's serialized balance (null until an expedition profile is bound).</summary>
        public CoinWallet BankedWallet { get; private set; }

        /// <summary>Permanent XP/level/skill-point progression; XP is committed the moment it is earned (13: permanent even on failure).</summary>
        public ProgressionService Progression { get; private set; }
        public ExpeditionState State { get; private set; }
        public TransitDecision Transit { get; private set; }
        public ExpeditionSummary LastSummary { get; private set; }
        public bool IsExpeditionActive => State != null && State.IsActive;
        public string LocalPlayerId => _localPlayerId;

        public event Action<ExpeditionState> ExpeditionStarted;
        public event Action<ExpeditionState> DepthEntered;
        public event Action<TransitDecision> TransitOpened;
        public event Action<ExpeditionSummary> ExpeditionEnded;

        // ---- Start transaction ----

        private int _levelAtStart;

        public ExpeditionState Start(PlayerProfile profile, int runSeed, Biome firstBiome) => Start(profile, runSeed, firstBiome, 1);

        /// <summary>partySize is the party at expedition start (83): captured once here, never re-read from the live roster.</summary>
        public ExpeditionState Start(PlayerProfile profile, int runSeed, Biome firstBiome, int partySize)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (IsExpeditionActive) throw new InvalidOperationException("An expedition is already active.");

            Profile = profile;
            _levelAtStart = profile.Level;
            BankedWallet = new CoinWallet(CoinDomain.Banked, () => profile.BankedCoins, v => profile.BankedCoins = v);
            Progression = new ProgressionService(profile) { IsAtBase = false };
            var inventory = new PlayerInventory(_resolveDefinition, _resolveAmmo, _ammoBalance);
            if (profile.SafeLoadout != null)
            {
                inventory.RestoreFromSnapshot(profile.SafeLoadout);
            }

            // The safe copy is gone the moment the run begins: closing the game cannot restore pre-run gear.
            profile.SafeLoadout = null;
            foreach (var item in AllItems(inventory)) item.IsAtRisk = true;
            inventory.MarksIncomingAtRisk = true;

            State = new ExpeditionState(runSeed, firstBiome, inventory, null, partySize);
            Transit = null;
            LastSummary = null;
            ExpeditionStarted?.Invoke(State);
            DepthEntered?.Invoke(State);
            return State;
        }

        // ---- In-run bookkeeping ----

        public void AddCarriedCoins(int amount)
        {
            EnsureActive();
            State.AddCarriedCoins(amount);
        }

        /// <summary>Records expedition XP for the summary and commits it permanently to the profile at once.</summary>
        public void AddXp(int amount)
        {
            EnsureActive();
            if (amount <= 0) return;
            State.AddXp(amount);
            Progression.AddXp(amount);
        }

        public void RecordEnemyDefeated(int xp) { EnsureActive(); State.Stats.EnemiesDefeated++; AddXp(xp); }
        public void RecordEliteDefeated(int xp) { EnsureActive(); State.Stats.ElitesDefeated++; AddXp(xp); }
        public void RecordRoomCleared() { EnsureActive(); State.Stats.RoomsCleared++; }

        /// <summary>Boss down: records XP and opens the transit decision for this depth (once).</summary>
        public TransitDecision RecordBossDefeated(int xp)
        {
            EnsureActive();
            if (State.BossDefeatedThisDepth)
            {
                return Transit;
            }

            State.MarkBossDefeated();
            AddXp(xp);
            var living = _livingPlayerIds != null ? _livingPlayerIds().ToList() : new List<string> { _localPlayerId };
            if (living.Count == 0) living.Add(_localPlayerId);
            Transit = new TransitDecision(_partyPolicy ?? _transitPolicy, living, _deadPlayerIds?.Invoke());
            Transit.Resolved += HandleTransitResolved;
            Transit.Open();
            TransitOpened?.Invoke(Transit);
            return Transit;
        }

        /// <summary>Convenience for the local player; duplicates after resolution are ignored by TransitDecision.</summary>
        public bool ChooseTransit(TransitChoice choice)
        {
            return Transit != null && Transit.Submit(_localPlayerId, choice);
        }

        private void HandleTransitResolved(TransitDecision decision, TransitChoice choice)
        {
            decision.Resolved -= HandleTransitResolved;
            if (!IsExpeditionActive || decision != Transit)
            {
                return;
            }

            if (choice == TransitChoice.ReturnToShelter)
            {
                ReturnWithParty();
            }
            else
            {
                Descend();
            }
        }

        // ---- Descend (no profile access) ----

        public void Descend()
        {
            EnsureActive();
            if (!State.BossDefeatedThisDepth) throw new InvalidOperationException("Descend requires the current depth's boss to be defeated.");
            var next = BiomeSelector.SelectNext(State.Biome, State.RunSeed, State.Depth + 1);
            State.Descend(next);
            Transit = null;
            DepthEntered?.Invoke(State);
        }

        /// <summary>
        /// The party Returns to Shelter (84 Extraction): a living local player secures its carried state; a local player
        /// who is still Dead loses all at-risk carried gear/loot/coins (the failure transaction), XP stays committed.
        /// </summary>
        public ExpeditionSummary ReturnWithParty()
        {
            return IsLocalPlayerDead ? Fail() : Return();
        }

        // ---- Return transaction (exactly once) ----

        public ExpeditionSummary Return()
        {
            if (TryGetClosedSummary(out var replayed)) return replayed;
            EnsureActive();
            var state = State;
            var transfer = CoinTransfer.MoveAll(state.CarriedWallet, BankedWallet, "extraction");
            var secured = AllItems(state.Inventory).Select(Line).ToList();
            foreach (var item in AllItems(state.Inventory)) item.IsAtRisk = false;

            Profile.SafeLoadout = state.Inventory.ToSnapshot();
            // The carried (risk) domain is emptied the moment the safe copy exists: one domain owns an instance at a time.
            state.Inventory.MarksIncomingAtRisk = false;
            state.Inventory.RestoreFromSnapshot(null);
            state.Close(ExpeditionOutcome.Extracted);

            var summary = BuildSummary(state, transfer.Receipt, 0, secured, Array.Empty<ItemSummaryLine>());
            Finish(summary);
            return summary;
        }

        // ---- Failure transaction (exactly once) ----

        public ExpeditionSummary Fail()
        {
            if (TryGetClosedSummary(out var replayed)) return replayed;
            EnsureActive();
            var state = State;
            var lost = AllItems(state.Inventory).Select(Line).ToList();
            var coinsLost = state.CarriedWallet.TakeAll("expedition_failed");
            state.Inventory.RestoreFromSnapshot(null); // at-risk carried assets are destroyed
            state.Close(ExpeditionOutcome.Failed);

            var noTransfer = new CoinReceipt(CoinTransactionKind.Transfer, CoinDomain.Banked, 0, BankedWallet.Balance, "expedition_failed");
            var summary = BuildSummary(state, noTransfer, coinsLost, Array.Empty<ItemSummaryLine>(), lost);
            Finish(summary);
            return summary;
        }

        private static ItemSummaryLine Line(ItemInstance item) => new(item.InstanceId, item.DefinitionId, item.Rarity, item.Quantity);

        /// <summary>Replayed success/failure callbacks on an already-closed transaction: same summary, no second commit or loss.</summary>
        private bool TryGetClosedSummary(out ExpeditionSummary summary)
        {
            summary = null;
            if (State == null || State.IsActive || LastSummary == null) return false;
            summary = LastSummary;
            return true;
        }

        private void Finish(ExpeditionSummary summary)
        {
            LastSummary = summary;
            Transit = null;
            ExpeditionEnded?.Invoke(summary);
        }

        private ExpeditionSummary BuildSummary(ExpeditionState state, CoinReceipt coinResult, int coinsLost, IReadOnlyList<ItemSummaryLine> secured, IReadOnlyList<ItemSummaryLine> lost)
        {
            Profile.ExpeditionsEnded++;
            return new ExpeditionSummary(Profile.ExpeditionsEnded, state.TransactionId, state.Outcome, state.RunSeed, state.BiomeHistory.ToArray(), state.Stats,
                _levelAtStart, Profile.Level, coinResult, coinsLost, secured, lost);
        }

        private void EnsureActive()
        {
            if (!IsExpeditionActive) throw new InvalidOperationException("No active expedition.");
        }

        private static System.Collections.Generic.IEnumerable<ItemInstance> AllItems(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var equipped = inventory.GetEquipped(slot);
                if (equipped != null) yield return equipped;
            }

            foreach (var item in inventory.BackpackSlots)
            {
                if (item != null) yield return item;
            }
        }
    }
}
