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
            if (Profile.SafeLoadout != null) Loadout.RestoreFromSnapshot(Profile.SafeLoadout);
            Loadout.EquippedChanged += (_, _) => Autosave.MarkDirty("loadout");
            Loadout.BackpackChanged += () => Autosave.MarkDirty("loadout");

            Lobby = new PartyLobby(LocalClientId, configs.Resolve);
            Lobby.Join(LocalClientId, Profile.DisplayName ?? "Player 1");
            _loadoutBinder = new LobbyLoadoutBinder(Lobby, LocalClientId, Loadout);

            Expedition = new ExpeditionService(configs.Resolve, configs.ResolveAmmo, configs.AmmoBalance, null, Lobby.Get(LocalClientId).ParticipantId);
            // While a run is active the Base loadout is empty (the gear is at risk in the expedition); on return it is the
            // secured loadout. Subscribed BEFORE the recorder so the restore runs before the end-of-run save.
            Expedition.ExpeditionStarted += _ => { _loadoutSynced = false; Loadout.RestoreFromSnapshot(null); };
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

        /// <summary>Commits the edited loadout to the profile (what Start moves into the at-risk inventory).</summary>
        public void CommitLoadoutToProfile()
        {
            if (!Expedition.IsExpeditionActive) Profile.SafeLoadout = Loadout.ToSnapshot();
        }

        /// <summary>Writes the current Base state (storage, loadout, coins, progression) as one safe point.</summary>
        public SaveError SaveNow(string reason = "base") => Autosave.SaveNow(reason);

        public void Dispose()
        {
            _loadoutBinder.Dispose();
            _autosaveBinder.Dispose();
            Recorder.Dispose();
        }
    }
}
