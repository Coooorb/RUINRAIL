using System;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Networking;
using RuinRail.Persistence;

namespace RuinRail.UI.Base
{
    /// <summary>The approved balance/config assets the Base needs; owned by the composition, never by the UI.</summary>
    public sealed class BaseConfigs
    {
        public ItemDefinitionRegistry Registry;
        public AmmoBalanceConfig AmmoBalance;
        public EconomyConfig Economy;
        public TraderConfig Trader;
        public WorkshopConfig Workshop;

        /// <summary>
        /// Global stat caps, so the Shelter loadout applies the same equipment-derived Ammo Stack Capacity rule the run
        /// does. Null is safe: Ammo Stack Capacity carries no cap in player/16, and PlayerStats treats a missing cap as
        /// uncapped rather than as zero.
        /// </summary>
        public GlobalStatCapsConfig StatCaps;

        public ItemDefinition Resolve(string id) => Registry != null && Registry.TryGet(id, out var definition) ? definition : null;
        public AmmoItemDefinition ResolveAmmo(AmmoType type) => Registry?.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);
    }

    /// <summary>
    /// One loaded profile at the Shelter: the existing Base services (Storage, Trader, Workshop, Character Station,
    /// progression, autosave) composed over the save slot, plus the local loadout inventory, the party lobby that
    /// tracks Ready against loadout changes (81) and the expedition service. The UI panels bind to these; no business
    /// rule lives here or in them.
    /// </summary>
    public sealed class BaseSession : IDisposable
    {
        private readonly AutosaveBinder _autosaveBinder;
        private readonly LobbyLoadoutBinder _loadoutBinder;
        private readonly Gameplay.Stats.LoadoutStatRegistrar _loadoutStats;
        /// <summary>True while the Base loadout inventory is the authoritative safe loadout (false during a run).</summary>
        private bool _loadoutSynced = true;

        public const ulong LocalClientId = 0;

        private BaseSession(SaveSlot slot, SaveSlotService saves, BaseConfigs configs)
        {
            Slot = slot ?? throw new ArgumentNullException(nameof(slot));
            Saves = saves ?? throw new ArgumentNullException(nameof(saves));
            Configs = configs ?? throw new ArgumentNullException(nameof(configs));
            Profile = slot.Profile;
            Banked = new CoinWallet(CoinDomain.Banked, () => Profile.BankedCoins, v => Profile.BankedCoins = v);
            Storage = Gameplay.Base.Storage.FromSnapshot(slot.Storage, configs.Resolve, configs.AmmoBalance);
            Prices = new PriceService(configs.Economy);
            Trader = new TraderService(configs.Trader, Prices, Banked, Profile.Trader, Profile.ProfileSeed, configs.Registry.Definitions, configs.Resolve);
            Workshop = new WorkshopService(configs.Workshop, Banked, Profile.Workshop, Storage, Trader) { IsAtBase = true };
            Progression = new ProgressionService(Profile) { IsAtBase = true };
            Character = new CharacterStation(Progression, Banked, configs.Economy) { IsAtBase = true };
            StarterKit = new StarterKitService(configs.Resolve, configs.ResolveAmmo, configs.AmmoBalance);
            Autosave = new AutosaveService(slot, saves);
            // 113 anti-exploit: the safe loadout is only ever written while no expedition is active (Start clears it).
            Autosave.BeforeSave += s => { s.Storage = Storage.ToSnapshot(); if (_loadoutSynced && !Expedition.IsExpeditionActive) s.Profile.SafeLoadout = Loadout.ToSnapshot(); };
            _autosaveBinder = new AutosaveBinder(Autosave, Storage, Trader, Workshop, Progression, Banked);

            Loadout = new PlayerInventory(configs.Resolve, configs.ResolveAmmo, configs.AmmoBalance);
            // The Shelter loadout resolves ammo stack limits through the same pipeline the run uses, so an Ammo Pouch
            // raises capacity identically at the Shelter, in storage transfers, at the Trader and inside an expedition.
            LoadoutStats = new Gameplay.Stats.PlayerStats(configs.StatCaps);
            // The Shelter resolves affixes from the same registry the run does, so a rolled Ammo Stack Capacity affix
            // raises the stack limit identically whether the player is packing at the Shelter or already in the dungeon.
            var affixes = Gameplay.Items.AffixRegistry.FromDefinitions(configs.Registry?.Definitions);
            _loadoutStats = new Gameplay.Stats.LoadoutStatRegistrar(Loadout, LoadoutStats, configs.Resolve, affixes.Get);
            Loadout.SetAmmoCapacityBonusProvider(() => LoadoutStats.GetPercent(Gameplay.Stats.StatId.AmmoStackCapacity));
            if (Profile.SafeLoadout != null) Loadout.RestoreFromSnapshot(Profile.SafeLoadout);
            Loadout.EquippedChanged += (_, _) => Autosave.MarkDirty("loadout");
            Loadout.BackpackChanged += () => Autosave.MarkDirty("loadout");

            Lobby = new PartyLobby(LocalClientId, configs.Resolve);
            Lobby.Join(LocalClientId, Profile.DisplayName ?? "Player 1");
            _loadoutBinder = new LobbyLoadoutBinder(Lobby, LocalClientId, Loadout);
            // Spending at the Trader / Workshop / Character Station can lower the bank under the chosen amount: the
            // lobby always sees the clamped value.
            Banked.Changed += _ => SyncCoinsToCarry();

            // The economy config carries the bounded deep-depth reward curve, so XP earned past its start depth scales
            // through the one seam in ExpeditionService.AddXp.
            Expedition = new ExpeditionService(configs.Resolve, configs.ResolveAmmo, configs.AmmoBalance, null,
                Lobby.Get(LocalClientId).ParticipantId, configs.Economy);
            // While a run is active the Base loadout is empty (the gear is at risk in the expedition); on return it is the
            // secured loadout. Subscribed BEFORE the recorder so the restore runs before the end-of-run save.
            Expedition.ExpeditionStarted += _ => { _loadoutSynced = false; Loadout.RestoreFromSnapshot(null); };
            // The chosen coins were consumed by that start; the next preparation begins from nothing taken.
            Expedition.ExpeditionStarted += _ => { _coinsToCarry = 0; CoinsToCarryChanged?.Invoke(); };
            // The party reopens first so the restored Base loadout is re-submitted to the lobby (a closed party refuses
            // loadout updates), and the next run starts from the loadout the player actually has, never a stale copy.
            Expedition.ExpeditionEnded += _ => { Lobby.Reopen(); Loadout.RestoreFromSnapshot(Profile.SafeLoadout); _loadoutSynced = true; _loadoutBinder.Submit(); };
            Recorder = new ExpeditionTransactionRecorder(Expedition, slot, Autosave);
        }

        public SaveSlot Slot { get; }
        public SaveSlotService Saves { get; }
        public BaseConfigs Configs { get; }
        public PlayerProfile Profile { get; }
        public CoinWallet Banked { get; }
        public Storage Storage { get; }
        public PriceService Prices { get; }
        public TraderService Trader { get; }
        public WorkshopService Workshop { get; }
        public ProgressionService Progression { get; }
        public CharacterStation Character { get; }
        public StarterKitService StarterKit { get; }
        public AutosaveService Autosave { get; }
        public PlayerInventory Loadout { get; }

        /// <summary>Equipment-derived stats of the Shelter loadout (capacity rules only; combat stats belong to the run).</summary>
        public Gameplay.Stats.PlayerStats LoadoutStats { get; }
        public PartyLobby Lobby { get; }
        public ExpeditionService Expedition { get; }
        public ExpeditionTransactionRecorder Recorder { get; }
        public bool GrantedFirstKit { get; private set; }
        public bool GrantedRescueKit { get; private set; }

        /// <summary>Opens the profile at the Shelter: starter/rescue kits (75) are ensured through the existing service, then a safe point is written.</summary>
        public static BaseSession Open(SaveSlot slot, SaveSlotService saves, BaseConfigs configs)
        {
            var session = new BaseSession(slot, saves, configs);
            session.GrantedFirstKit = session.StarterKit.GrantFirstProfileKit(session.Profile);
            if (session.GrantedFirstKit) session.Loadout.RestoreFromSnapshot(session.Profile.SafeLoadout);
            session.GrantedRescueKit = session.StarterKit.EnsureStartableLoadout(session.Profile, session.Storage);
            if (session.GrantedRescueKit) session.Loadout.RestoreFromSnapshot(session.Profile.SafeLoadout);
            session.Autosave.MarkDirty("base_entered");
            session.Autosave.Flush();
            return session;
        }

        private int _coinsToCarry;

        /// <summary>
        /// Banked Coins the player chose to take into the next expedition (77: they become Carried Coins at Start). A
        /// preparation choice only — nothing moves until the start transaction, so changing or abandoning it can never
        /// lose or duplicate a coin, and it is never saved. Always within [0, banked balance].
        /// </summary>
        public int CoinsToCarry => Math.Min(_coinsToCarry, Banked.Balance);

        /// <summary>What stays banked once the chosen coins leave with the expedition.</summary>
        public int BankedAfterDeparture => Banked.Balance - CoinsToCarry;

        public event Action CoinsToCarryChanged;

        /// <summary>Chooses the amount to take (clamped to [0, banked]); returns the amount now chosen.</summary>
        public int SetCoinsToCarry(int coins)
        {
            if (Expedition.IsExpeditionActive) return CoinsToCarry;
            _coinsToCarry = Math.Max(0, Math.Min(coins, Banked.Balance));
            SyncCoinsToCarry();
            CoinsToCarryChanged?.Invoke();
            return CoinsToCarry;
        }

        /// <summary>Re-states the (clamped) choice to the lobby, which captures it at start.</summary>
        public void SyncCoinsToCarry()
        {
            if (Expedition.IsExpeditionActive) return;
            _coinsToCarry = Math.Min(_coinsToCarry, Banked.Balance);
            Lobby.SetCarriedCoins(LocalClientId, _coinsToCarry);
        }

        /// <summary>Commits the edited loadout to the profile (what Start moves into the at-risk inventory).</summary>
        public void CommitLoadoutToProfile()
        {
            if (!Expedition.IsExpeditionActive) Profile.SafeLoadout = Loadout.ToSnapshot();
        }

        /// <summary>How often the Starter Loadout fallback actually equipped the kit in this session (diagnostics/tests).</summary>
        public int StarterLoadoutFallbacks { get; private set; }

        /// <summary>
        /// Run before Ready/Start: a loadout with no weapon equipped gets the free Starter Loadout through the existing
        /// kit service (75); a loadout with a weapon equipped is preserved untouched. The lobby sees the change through
        /// the loadout binder like any other edit. Never runs on scene load, never during a run, never writes storage.
        /// </summary>
        public bool EnsureStarterLoadoutIfEmpty()
        {
            if (Expedition.IsExpeditionActive || !_loadoutSynced) return false;
            if (!StarterKit.EnsureEquippedLoadout(Loadout)) return false;
            StarterLoadoutFallbacks++;
            Autosave.MarkDirty("starter_loadout_fallback");
            return true;
        }

        /// <summary>
        /// The extraction whose loot the player has already looked at in the stash (UI state only, not saved): the
        /// Shelter stops pointing at Storage once the stash was opened for that run.
        /// </summary>
        public string StashAcknowledgedTransaction { get; set; }

        /// <summary>
        /// True while the last expedition was a successful extraction, some of what it secured is still in the survivor's
        /// backpack (where loot lands; the gear worn into the run is not "loot to stash") and the stash has not been
        /// opened since: the Shelter points the player at Storage.
        /// </summary>
        public bool HasLootToStash
        {
            get
            {
                var summary = Expedition.LastSummary;
                if (summary == null || !summary.IsSuccess || Expedition.IsExpeditionActive) return false;
                if (StashAcknowledgedTransaction == summary.TransactionId) return false;
                var secured = summary.ExtractedItemIds;
                return secured.Length > 0 && Loadout.BackpackSlots.Any(i => i != null && System.Array.IndexOf(secured, i.InstanceId) >= 0);
            }
        }

        /// <summary>Writes the current Base state (storage, loadout, coins, progression) as one safe point.</summary>
        public SaveError SaveNow(string reason = "base") => Autosave.SaveNow(reason);

        public void Dispose()
        {
            _loadoutBinder.Dispose();
            _autosaveBinder.Dispose();
            _loadoutStats.Dispose();
            Recorder.Dispose();
        }
    }
}
