using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Networking;
using RuinRail.UI.Inventory;

namespace RuinRail.UI.Base
{
    public enum BaseStation
    {
        Storage,
        Loadout,
        Trader,
        Character,
        Workshop,
        Multiplayer,
        Transit
    }

    /// <summary>Shared feedback line for every station: the service result rendered as English text.</summary>
    public sealed class StationFeedback
    {
        public string Text { get; private set; } = string.Empty;
        public bool IsError { get; private set; }
        public event Action<StationFeedback> Changed;

        public void Ok(string text) { Text = text; IsError = false; RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm); Changed?.Invoke(this); }
        public void Error(string text) { Text = text; IsError = true; RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Failure); Changed?.Invoke(this); }
        public void Clear() { Text = string.Empty; IsError = false; Changed?.Invoke(this); }
    }

    /// <summary>Storage station (94): item grid with category filter / sort, deposit from the loadout, withdraw to it.</summary>
    public sealed class StoragePanelViewModel
    {
        private readonly BaseSession _session;
        private readonly StorageService _storage;
        private readonly BackpackContainer _backpack;

        public StoragePanelViewModel(BaseSession session)
        {
            _session = session;
            _storage = new StorageService(session.Storage);
            _backpack = new BackpackContainer(session.Loadout);
            Feedback = new StationFeedback();
        }

        public StationFeedback Feedback { get; }
        public ItemCategory? Filter { get; set; }
        public bool SortByRarity { get; set; }
        public int Capacity => _session.Storage.Capacity;
        public int Count => _session.Storage.Items.Count();

        public IReadOnlyList<ItemInstance> Items
        {
            get
            {
                var items = _session.Storage.Items.Where(i => Filter == null || _session.Configs.Resolve(i.DefinitionId)?.Category == Filter);
                items = SortByRarity ? items.OrderByDescending(i => i.Rarity).ThenBy(Name) : items.OrderBy(Name);
                return items.ToList();
            }
        }

        private string Name(ItemInstance item) => _session.Configs.Resolve(item.DefinitionId)?.DisplayName ?? item.DefinitionId;

        public bool Deposit(string instanceId)
        {
            var equipped = _session.Loadout.Contains(instanceId) && _backpack.Find(instanceId) == null;
            IItemContainer source = equipped ? SlotContainerOf(instanceId) : _backpack;
            var result = _storage.Deposit(source, instanceId);
            return Report(result, "Stored.");
        }

        public bool Withdraw(string instanceId)
        {
            var result = _storage.Withdraw(instanceId, _backpack);
            return Report(result, "Taken into the backpack.");
        }

        private IItemContainer SlotContainerOf(string instanceId)
        {
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var item = _session.Loadout.GetEquipped(slot);
                if (item != null && item.InstanceId == instanceId) return new EquippedSlotContainer(_session.Loadout, slot);
            }

            return _backpack;
        }

        private bool Report(TransferResult result, string ok)
        {
            if (result.Success) { Feedback.Ok(ok); return true; }
            Feedback.Error(result.Error switch
            {
                TransferError.DestinationRejected => "No room there.",
                TransferError.SourceMissingItem => "That item is no longer here.",
                TransferError.DuplicateOwnership => "That item is already there.",
                _ => "That move is not possible."
            });
            return false;
        }
    }

    /// <summary>Loadout station (94): the inventory view model plus Storage as a source; changes flow into the lobby's Ready state through the session binder.</summary>
    public sealed class LoadoutPanelViewModel
    {
        private readonly BaseSession _session;
        private readonly StorageService _storage;

        public LoadoutPanelViewModel(BaseSession session)
        {
            _session = session;
            _storage = new StorageService(session.Storage);
            Inventory = new InventoryViewModel();
            Inventory.Bind(session.Loadout, null, () => session.Banked.Balance);
            Feedback = new StationFeedback();
        }

        public InventoryViewModel Inventory { get; }
        public StationFeedback Feedback { get; }
        public IReadOnlyList<ItemInstance> StorageItems => _session.Storage.Items.ToList();

        /// <summary>Equip straight from Storage into the slot the item belongs to (withdraw through the service).</summary>
        public bool EquipFromStorage(string instanceId, EquippedSlot slot)
        {
            var result = _storage.Withdraw(instanceId, new EquippedSlotContainer(_session.Loadout, slot));
            if (result.Success) { Feedback.Ok("Equipped."); return true; }
            Feedback.Error(result.Error == TransferError.DestinationRejected ? "That item does not fit this slot (or the slot is taken)." : "That move is not possible.");
            return false;
        }

        public bool StoreFromLoadout(EquippedSlot slot)
        {
            var item = _session.Loadout.GetEquipped(slot);
            if (item == null) { Feedback.Error("Nothing equipped there."); return false; }
            var result = _storage.Deposit(new EquippedSlotContainer(_session.Loadout, slot), item.InstanceId);
            if (result.Success) { Feedback.Ok("Stored."); return true; }
            Feedback.Error("Storage is full.");
            return false;
        }
    }

    /// <summary>Trader station (94): Buy / Sell / upgrade over TraderService; the destination is the loadout backpack.</summary>
    public sealed class TraderPanelViewModel
    {
        private readonly BaseSession _session;
        private readonly BackpackContainer _backpack;

        public TraderPanelViewModel(BaseSession session)
        {
            _session = session;
            _backpack = new BackpackContainer(session.Loadout);
            Feedback = new StationFeedback();
        }

        public StationFeedback Feedback { get; }
        public IReadOnlyList<TraderOffer> Offers => _session.Trader.Offers;
        public int Level => _session.Trader.Level;
        public int NextUpgradeCost => _session.Trader.NextUpgradeCost;
        public int Banked => _session.Banked.Balance;

        public bool Buy(int offerIndex)
        {
            var offer = Offers.FirstOrDefault(o => o.Index == offerIndex);
            var error = _session.Trader.Buy(offerIndex, _backpack);
            var bought = Report(error, offer != null ? $"Bought {offer.Definition.DisplayName} for {offer.Price} Coins." : "Bought.");
            if (bought) RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Purchase);
            return bought;
        }

        public int QuoteSell(string instanceId)
        {
            var item = _backpack.Find(instanceId) ?? _session.Storage.Find(instanceId);
            return item == null ? 0 : _session.Trader.QuoteSellValue(item);
        }

        public bool Sell(string instanceId)
        {
            var source = _backpack.Find(instanceId) != null ? (IItemContainer)_backpack : _session.Storage;
            var quote = QuoteSell(instanceId);
            var error = _session.Trader.Sell(source, instanceId);
            return Report(error, $"Sold for {quote} Coins.");
        }

        public bool Upgrade() => Report(_session.Trader.TryUpgrade(), $"Trader upgraded to level {_session.Trader.Level}.");

        private bool Report(TradeError error, string ok)
        {
            if (error == TradeError.None) { Feedback.Ok(ok); return true; }
            Feedback.Error(error switch
            {
                TradeError.InsufficientFunds => "Not enough Coins.",
                TradeError.AlreadySold => "Already sold.",
                TradeError.NoSuchOffer => "No such offer.",
                TradeError.DestinationRejected => "BACKPACK FULL",
                TradeError.Unsellable => "Starter gear cannot be sold.",
                TradeError.NoValue => "That item has no sale value.",
                TradeError.AlreadyMaxLevel => "The Trader is already at its highest level.",
                TradeError.SourceMissingItem => "That item is no longer here.",
                _ => "The trade is not possible."
            });
            return false;
        }
    }

    /// <summary>Character station (94): level, XP, skill points, six attributes, respec — over CharacterStation.</summary>
    public sealed class CharacterPanelViewModel
    {
        private readonly BaseSession _session;

        public CharacterPanelViewModel(BaseSession session)
        {
            _session = session;
            Feedback = new StationFeedback();
        }

        public StationFeedback Feedback { get; }
        public CharacterSheet Sheet => _session.Character.GetSheet();
        public static readonly SkillId[] Attributes = (SkillId[])Enum.GetValues(typeof(SkillId));

        // ---- live per-attribute presentation (ui/94): every number comes from SkillCatalog / SkillRules, never from
        // text authored beside the rule, so the screen cannot show a value the run does not produce.

        public int RankOf(SkillId skill) => _session.Progression.Profile.Skills.GetRank(skill);
        public int MaxRank => SkillRules.MaxRank;
        public bool IsMaxed(SkillId skill) => RankOf(skill) >= SkillRules.MaxRank;
        public int UnspentPoints => _session.Progression.UnspentSkillPoints;

        /// <summary>The Skill Point price of the next rank, or 0 at the cap (nothing is ever deducted there).</summary>
        public int CostOf(SkillId skill) => IsMaxed(skill) ? 0 : SkillCatalog.PointCostPerRank;

        public bool CanAfford(SkillId skill) => !IsMaxed(skill) && UnspentPoints >= CostOf(skill);

        /// <summary>The purchase is offered only at the Shelter, below the cap, and with the points in hand.</summary>
        public bool CanAllocate(SkillId skill) => _session.Character.IsAtBase && CanAfford(skill);

        public string NameOf(SkillId skill) => SkillCatalog.DisplayName(skill);
        public string DescriptionOf(SkillId skill) => SkillCatalog.Description(skill);
        public string RankTextOf(SkillId skill) => SkillCatalog.RankText(RankOf(skill));
        public string EffectNow(SkillId skill) => SkillCatalog.EffectText(skill, RankOf(skill));

        /// <summary>The effect one more point buys, or null at the cap.</summary>
        public string EffectNext(SkillId skill) => SkillCatalog.NextRankEffectText(skill, RankOf(skill));

        /// <summary>One panel line per affected stat: the value now and what one more point buys, or MAX at the cap.</summary>
        public IReadOnlyList<string> EffectRows(SkillId skill) => SkillCatalog.EffectRows(skill, RankOf(skill));


        /// <summary>The respec control's caption: the Banked Coin price the Character Station actually charges.</summary>
        public string RespecLabel() => $"RESPEC {_session.Character.RespecPrice} C";

        /// <summary>True while the Character Station would actually accept a respec (Shelter, points spent, coins in hand).</summary>
        public bool CanRespec() => _session.Character.CanRespec(out _);

        /// <summary>The panel subtitle: what the station is for, or why nothing can be bought right now.</summary>
        public string StatusLine() =>
            UnspentPoints > 0
                ? "Spend Skill Points on the six attributes."
                : "No Skill Points. Earn XP on an expedition.";

        public bool Allocate(SkillId skill)
        {
            var error = _session.Character.Allocate(skill);
            if (error == SkillSpendError.None) { Feedback.Ok($"{skill} raised to rank {Sheet.Ranks[skill]}."); return true; }
            Feedback.Error(error switch
            {
                SkillSpendError.NoUnspentPoints => "No skill points to spend.",
                SkillSpendError.RankAtMax => $"{skill} is at its maximum rank.",
                _ => "That allocation is not possible."
            });
            return false;
        }

        public bool Respec()
        {
            var error = _session.Character.Respec();
            if (error == RespecError.None) { Feedback.Ok("Skill points refunded."); return true; }
            Feedback.Error(error switch
            {
                RespecError.InsufficientFunds => "Not enough Coins for a respec.",
                RespecError.NothingToRefund => "No skill points are allocated.",
                _ => "Respec is not possible here."
            });
            return false;
        }
    }

    /// <summary>Workshop station (94): the approved base upgrades over WorkshopService.</summary>
    public sealed class WorkshopPanelViewModel
    {
        private readonly BaseSession _session;

        public WorkshopPanelViewModel(BaseSession session)
        {
            _session = session;
            Feedback = new StationFeedback();
        }

        public StationFeedback Feedback { get; }
        public int StorageTier => _session.Workshop.StorageTier;
        public int StorageCapacity => _session.Workshop.StorageCapacity;
        public int NextStorageCapacity => _session.Workshop.NextStorageCapacity;
        public int NextStorageUpgradeCost => _session.Workshop.NextStorageUpgradeCost;
        public int TraderLevel => _session.Trader.Level;
        public int NextTraderUpgradeCost => _session.Trader.NextUpgradeCost;

        public bool BuyStorageUpgrade() => Report(_session.Workshop.BuyStorageUpgrade(), $"Storage expanded to {_session.Workshop.StorageCapacity} slots.");
        public bool BuyTraderUpgrade() => Report(_session.Workshop.BuyTraderUpgrade(), $"Trader upgraded to level {_session.Trader.Level}.");

        private bool Report(UpgradeError error, string ok)
        {
            if (error == UpgradeError.None) { Feedback.Ok(ok); return true; }
            Feedback.Error(error switch
            {
                UpgradeError.InsufficientFunds => "Not enough Coins.",
                UpgradeError.AlreadyMaxTier => "Already at the highest tier.",
                _ => "Upgrades are only available at the Shelter."
            });
            return false;
        }
    }

    /// <summary>Multiplayer terminal (94): Solo / Host / Join, join code, Ready states of the current party.</summary>
    public sealed class MultiplayerPanelViewModel
    {
        private readonly BaseSession _session;

        public MultiplayerPanelViewModel(BaseSession session, MultiplayerTerminalService terminal)
        {
            _session = session;
            Terminal = terminal;
            Feedback = new StationFeedback();
        }

        public MultiplayerTerminalService Terminal { get; }
        public StationFeedback Feedback { get; }
        public PartyLobby Lobby => _session.Lobby;
        public IReadOnlyList<LobbyMember> Members => _session.Lobby.Members;
        public string JoinCode => Terminal?.JoinCodeToShare;
        public bool LocalReady => _session.Lobby.Get(BaseSession.LocalClientId)?.IsReady ?? false;
        public bool AllReady => _session.Lobby.AllReady;

        public bool SetReady(bool ready)
        {
            var member = _session.Lobby.Get(BaseSession.LocalClientId);
            if (member == null) return false;
            // No weapon equipped is not a refusal: the free Starter Loadout is equipped first (75), then the lobby re-validates.
            var starter = ready && _session.EnsureStarterLoadoutIfEmpty();
            if (ready && !member.HasValidLoadout) { Feedback.Error($"Loadout not ready: {member.InvalidReason}."); return false; }
            var ok = _session.Lobby.SetReady(BaseSession.LocalClientId, ready);
            Feedback.Ok(ready ? (starter ? StarterLoadoutEquippedMessage + " Ready." : "Ready.") : "Not ready.");
            return ok;
        }

        /// <summary>The small non-blocking notice shown when the fallback equipped the kit.</summary>
        public const string StarterLoadoutEquippedMessage = "STARTER LOADOUT EQUIPPED.";
    }

    /// <summary>Transit (94): Start Expedition — the host starts the party through the lobby, exactly once.</summary>
    public sealed class TransitPanelViewModel
    {
        private readonly BaseSession _session;
        private readonly ExpeditionStartCoordinator _coordinator = new();
        private readonly Func<int> _runSeed;

        public TransitPanelViewModel(BaseSession session, Func<int> runSeed = null)
        {
            _session = session;
            _runSeed = runSeed ?? (() => Environment.TickCount);
            Feedback = new StationFeedback();
        }

        public StationFeedback Feedback { get; }
        public bool CanStart => _session.Lobby.AllReady && !_session.Expedition.IsExpeditionActive && StartGate?.Invoke() == null;
        public ExpeditionStartSnapshot Started => _session.Lobby.StartSnapshot;

        /// <summary>
        /// Set by the Shelter composition in a live session: a non-null reason refuses Start (81 — a joined client's
        /// local lobby must never start an expedition of its own; the host's start reaches it instead).
        /// </summary>
        public Func<string> StartGate { get; set; }

        public bool StartExpedition()
        {
            if (_session.Expedition.IsExpeditionActive) { Feedback.Error("An expedition is already running."); return false; }
            var gate = StartGate?.Invoke();
            if (gate != null) { Feedback.Error(gate); return false; }
            // Resolve → validate → preserve or fall back: a loadout with nothing equipped gets the Starter Loadout before launch.
            var starter = _session.EnsureStarterLoadoutIfEmpty();
            _session.CommitLoadoutToProfile();
            var error = _session.Lobby.TryStart(BaseSession.LocalClientId, _runSeed(), out var snapshot);
            if (error != LobbyStartError.None && error != LobbyStartError.AlreadyStarted)
            {
                Feedback.Error(error switch
                {
                    LobbyStartError.NotAllReady => "Everyone must be Ready.",
                    LobbyStartError.InvalidLoadout => "A loadout is not valid (a weapon must be equipped).",
                    LobbyStartError.NotHost => "Only the host can start.",
                    _ => "The expedition cannot start."
                });
                return false;
            }

            var state = _coordinator.Apply(snapshot, _session.Expedition, _session.Profile);
            if (state == null) { Feedback.Error("The expedition cannot start."); return false; }
            Feedback.Ok((starter ? MultiplayerPanelViewModel.StarterLoadoutEquippedMessage + " " : string.Empty) + $"Expedition started: Depth 1, {HudBiomeName((Biome)snapshot.Biome)}.");
            return true;
        }

        private static string HudBiomeName(Biome biome) => Hud.HudSnapshot.BiomeName(biome);
    }

    /// <summary>Expedition summary (76): the closed transaction's numbers, never re-derived.</summary>
    public sealed class ExpeditionSummaryViewModel
    {
        public ExpeditionSummaryViewModel(ExpeditionSummary summary, Func<string, ItemDefinition> resolve)
        {
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Title = summary.IsSuccess ? "EXTRACTION SUCCESSFUL" : "EXPEDITION FAILED";
            var lines = new List<string>
            {
                $"Depth Reached: {summary.DepthReached}",
                $"Rooms Cleared: {summary.RoomsCleared}",
                $"Enemies Defeated: {summary.EnemiesDefeated}",
                $"Elites Defeated: {summary.ElitesDefeated}",
                $"Bosses Defeated: {summary.BossesDefeated}",
                $"XP Earned: {summary.XpEarned} (kept)",
                summary.IsNewPersonalBest
                    ? $"Deepest Depth: {summary.DeepestDepthReached} — NEW PERSONAL BEST (was {summary.DeepestDepthBefore})"
                    : $"Deepest Depth: {summary.DeepestDepthReached}"
            };
            if (summary.IsSuccess)
            {
                lines.Add($"Coins Extracted: {summary.CoinsExtracted}");
                lines.Add("Extracted items:");
                lines.AddRange(summary.SecuredItems.Select(i => $"  {resolve?.Invoke(i.DefinitionId)?.DisplayName ?? i.DefinitionId} [{i.Rarity.ToString().ToUpperInvariant()}]{(i.Quantity > 1 ? $" x{i.Quantity}" : string.Empty)}"));
            }
            else
            {
                lines.Add($"Coins Lost: {summary.CoinsLost}");
                lines.Add("All carried items, backpack, ammo, consumables and carried Coins were lost.");
            }

            Lines = lines;
        }

        public ExpeditionSummary Summary { get; }
        public string Title { get; }
        public IReadOnlyList<string> Lines { get; }
    }

    /// <summary>
    /// The Shelter hub (94): every station reachable as a panel over the BaseSession; opening/closing panels never
    /// runs a transaction, so reopening can never duplicate a purchase, upgrade or reward.
    /// </summary>
    public sealed class BaseHubViewModel : IDisposable
    {
        public BaseHubViewModel(BaseSession session, MultiplayerTerminalService terminal = null, Func<int> runSeed = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Storage = new StoragePanelViewModel(session);
            Loadout = new LoadoutPanelViewModel(session);
            Trader = new TraderPanelViewModel(session);
            Character = new CharacterPanelViewModel(session);
            Workshop = new WorkshopPanelViewModel(session);
            Multiplayer = new MultiplayerPanelViewModel(session, terminal);
            Transit = new TransitPanelViewModel(session, runSeed);
            session.Expedition.ExpeditionEnded += OnExpeditionEnded;
        }

        public static readonly BaseStation[] Stations = (BaseStation[])Enum.GetValues(typeof(BaseStation));
        public BaseSession Session { get; }
        public BaseStation? Current { get; private set; }
        public StoragePanelViewModel Storage { get; }
        public LoadoutPanelViewModel Loadout { get; }
        public TraderPanelViewModel Trader { get; }
        public CharacterPanelViewModel Character { get; }
        public WorkshopPanelViewModel Workshop { get; }
        public MultiplayerPanelViewModel Multiplayer { get; }
        public TransitPanelViewModel Transit { get; }
        public ExpeditionSummaryViewModel LastSummary { get; private set; }
        public int Opens { get; private set; }

        public event Action<BaseStation?> StationChanged;
        public event Action<ExpeditionSummaryViewModel> SummaryReady;

        public static string Label(BaseStation station) => station switch
        {
            BaseStation.Storage => "STORAGE",
            BaseStation.Loadout => "LOADOUT",
            BaseStation.Trader => "TRADER",
            BaseStation.Character => "CHARACTER",
            BaseStation.Workshop => "WORKSHOP",
            BaseStation.Multiplayer => "MULTIPLAYER",
            BaseStation.Transit => "TRANSIT",
            _ => station.ToString().ToUpperInvariant()
        };

        public void Open(BaseStation station)
        {
            Current = station;
            Opens++;
            if (station == BaseStation.Loadout) Loadout.Inventory.Open();
            StationChanged?.Invoke(Current);
        }

        public void Close()
        {
            if (Current == BaseStation.Loadout) Loadout.Inventory.Close();
            Current = null;
            StationChanged?.Invoke(Current);
        }

        private void OnExpeditionEnded(ExpeditionSummary summary)
        {
            LastSummary = new ExpeditionSummaryViewModel(summary, Session.Configs.Resolve);
            SummaryReady?.Invoke(LastSummary);
        }

        public void Dispose()
        {
            Session.Expedition.ExpeditionEnded -= OnExpeditionEnded;
            Loadout.Inventory.Dispose();
        }
    }
}
