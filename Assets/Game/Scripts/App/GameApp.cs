using System;
using System.IO;
using System.Linq;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using RuinRail.UI.Transitions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RuinRail.App
{
    /// <summary>
    /// The process-wide composition root (created once by AppRoot in the Bootstrap scene, DontDestroyOnLoad): content
    /// catalog, save slot, settings, the main-menu view model (which owns the Base session), networking bootstrap,
    /// audio and music. It composes each scene from code when it loads — the scenes themselves stay data-free — so the
    /// same view models the tests verify drive the built player. Not a service locator: screens receive it explicitly.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public const string SmokeArgument = "-smoke";
        public const string SaveDirArgument = "-savedir";
        public const string SmokeResultFile = "smoke_result.json";
        /// <summary>Optional deterministic run seed for proof/smoke runs (`-seed 1234`); the Shelter transit uses it instead of the clock.</summary>
        public const string SeedArgument = "-seed";

        public static GameApp Current { get; private set; }

        public GameContentCatalog Content { get; private set; }
        public ItemDefinitionRegistry Registry { get; private set; }
        public LegendarySpecialRegistry Specials { get; private set; }
        public BaseConfigs Configs { get; private set; }
        public SaveSlotService Saves { get; private set; }
        public UserSettingsService Settings { get; private set; }
        public SettingsViewModel SettingsScreen { get; private set; }
        /// <summary>The rebinder behind the Settings CONTROLS page (its own action instance, never a player's).</summary>
        public InputRebinder SettingsRebinder { get; private set; }
        private RuinRailInputActions _settingsActions;
        public MainMenuViewModel Menu { get; private set; }
        public NetworkBootstrap Network { get; private set; }
        /// <summary>
        /// The co-op session layer (81/82): lobby relay, the host's authoritative expedition start and the client's
        /// start from it. Process lifetime, like the session it serves; idle outside a live session.
        /// </summary>
        public CoopSessionService Coop { get; private set; }
        public AudioService Audio { get; private set; }
        public GameplayAudioBinder AudioBinder { get; private set; }
        public MusicDirector Music { get; private set; }
        public MusicBinder MusicBinder { get; private set; }
        public string SaveDirectory { get; private set; }
        /// <summary>Run seed override from the command line (null = the clock, the shipped default).</summary>
        public int? RunSeedOverride { get; private set; }
        /// <summary>The run seed the next expedition starts with.</summary>
        public int NextRunSeed() => RunSeedOverride ?? Environment.TickCount;
        /// <summary>Tests/proof runs pin the next run seed; null restores the clock.</summary>
        public void SetRunSeedOverride(int? seed) => RunSeedOverride = seed;
        public bool IsSmoke { get; private set; }
        public SmokeRunner Smoke { get; private set; }

        /// <summary>TASK 183: in-player profiling run, present only under -profile.</summary>
        public PlayerProfileRunner Profile { get; private set; }
        public string ComposedScene { get; private set; } = string.Empty;
        public int ComposeCount { get; private set; }

        /// <summary>TASK 178: covers the screen across a scene change and gates input while it runs.</summary>
        public SceneTransitionViewModel Transition { get; } = new();

        /// <summary>TASK 178: recoverable service/save/scene errors, presented with a safe way out.</summary>
        public RecoverableErrorViewModel Errors { get; } = new();

        /// <summary>TASK 180: whether this process composed the real Unity adapters or the development fakes.</summary>
        public LiveServiceConfiguration.Mode LiveMultiplayerMode { get; private set; }

        public event Action<string> SceneComposed;

        /// <summary>Creates (or returns) the app; safe to call from the Bootstrap scene and from tests.</summary>
        public static GameApp Ensure(GameContentCatalog content = null, string saveDirectory = null, bool smoke = false)
        {
            if (Current != null) return Current;
            var go = new GameObject("GameApp");
            DontDestroyOnLoad(go);
            var app = go.AddComponent<GameApp>();
            app.Initialize(content ?? GameContentCatalog.Load(), saveDirectory, smoke);
            return app;
        }

        private void Initialize(GameContentCatalog content, string saveDirectory, bool smoke)
        {
            Current = this;
            Content = content ?? throw new InvalidOperationException("GameContentCatalog missing from Resources: run RuinRail/Production/Build Game Content Catalog.");
            var problems = Content.Problems();
            if (problems.Count > 0) Debug.LogError("GameContentCatalog incomplete: " + string.Join("; ", problems));
            Registry = Content.BuildRegistry();
            Specials = Content.BuildSpecials();
            Configs = Content.BuildBaseConfigs();
            // Every replicated player object composes the same body + held-weapon presentation as the solo player.
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = go => PlayerVisualComposer.Compose(go, Content);
            // Every pooled projectile, player or enemy, draws its in-flight profile from the shipped catalog.
            RuinRail.Gameplay.Combat.Projectiles.ProjectileVisualCatalog.Active = Content.ProjectileVisuals;
            // Item descriptions read the shipped weapon catalog so an ammo type can say which classes consume it (ui/93).
            RuinRail.Gameplay.Items.ItemDescriptions.WeaponCatalog = Content.Items.OfType<RuinRail.Gameplay.Items.WeaponDefinition>().ToList();

            var args = Environment.GetCommandLineArgs();
            IsSmoke = smoke || args.Contains(SmokeArgument);
            var dirIndex = Array.IndexOf(args, SaveDirArgument);
            SaveDirectory = saveDirectory ?? (dirIndex >= 0 && dirIndex + 1 < args.Length ? args[dirIndex + 1] : Application.persistentDataPath);
            Directory.CreateDirectory(SaveDirectory);
            var seedIndex = Array.IndexOf(args, SeedArgument);
            if (seedIndex >= 0 && seedIndex + 1 < args.Length && int.TryParse(args[seedIndex + 1], out var seed)) RunSeedOverride = seed;

            Saves = new SaveSlotService(new FileSaveStore(Path.Combine(SaveDirectory, SaveSlotService.SlotFileName)), Configs.Resolve);
            Settings = new UserSettingsService(new FileSaveStore(Path.Combine(SaveDirectory, UserSettingsService.SettingsFileName)));
            SettingsViewModel.Bootstrap(Settings, new UnitySettingsApplier());
            // The CONTROLS page rebinds against a dedicated action instance carrying the persisted overrides; APPLY
            // publishes the result through ActiveBindingOverrides to every live and future input reader.
            _settingsActions = new RuinRailInputActions();
            ActiveBindingOverrides.ApplyTo(_settingsActions.asset);
            SettingsRebinder = new InputRebinder(_settingsActions.asset);
            SettingsScreen = new SettingsViewModel(Settings, SettingsRebinder, new UnitySettingsApplier());
            Menu = new MainMenuViewModel(Saves, Configs);
            Menu.SetSettings(SettingsScreen);

            // TASK 180: a release player build composes the real Unity adapters; the in-memory fakes are reachable
            // only through an explicit opt-out (-offline-multiplayer, -smoke, or the editor). A shipped build must
            // never present FakeMultiplayerServices to the player as online play.
            Network = gameObject.AddComponent<NetworkBootstrap>();
            var mode = LiveServiceConfiguration.Resolve();
            // Live composition builds the process NetworkManager itself (transport + the networked player prefab):
            // without one the session controller has no driver and online play silently degrades to the fake.
            var (services, driver) = LiveServiceConfiguration.ComposeForProcess(mode, Content.NetworkPlayerEntity, Content.CoopRunLink);
            LiveMultiplayerMode = mode;
            if (driver != null) Network.Configure(services, driver);
            else Network.Configure(new FakeMultiplayerServices(), new FakeNetworkDriver());
            Coop = new CoopSessionService();

            // The one listener of the process, on the persistent root: Main Menu and Shelter have no camera object, and
            // the dungeon camera must not add a second one. It follows Camera.main wherever one exists.
            // Before anything plays: keep the process attached to the output device the player is actually on. A
            // device change after launch otherwise silences the whole run while every in-engine check still passes.
            AudioOutputWatchdog.Ensure(transform);
            AudioListenerRig.Ensure(transform);
            Audio = gameObject.AddComponent<AudioService>();
            Audio.Configure(Content.AudioEvents);
            AudioBinder = gameObject.AddComponent<GameplayAudioBinder>();
            AudioBinder.Configure(Audio);
            Music = gameObject.AddComponent<MusicDirector>();
            Music.Configure(Content.Music);
            MusicBinder = gameObject.AddComponent<MusicBinder>();
            MusicBinder.Configure(Music);
            MusicBinder.EnterMainMenu();

            // TASK 178: the overlay performs the actual load once it has covered the screen, and lifts once the
            // destination has composed — so a transition never presents a raw or half-built frame.
            Transition.LoadRequested += SceneManager.LoadScene;

            // 113 autosave points (storage/loadout, trader, skill spending, base upgrades): the Shelter services mark the
            // slot dirty at each one and this end-of-frame flusher writes it — once per frame, and on pause/quit. It
            // existed with its tests but was never composed, so those safe points only reached disk at the next
            // explicit save (expedition start or leaving the Shelter), and a crash in between silently undid them.
            _autosaveFlusher = gameObject.AddComponent<RuinRail.Persistence.AutosaveFlusher>();

            SceneManager.sceneLoaded += OnSceneLoaded;
            if (IsSmoke)
            {
                Smoke = gameObject.AddComponent<SmokeRunner>();
                Smoke.Begin(this);
            }
            else if (CoopExpeditionProof.Requested(args))
            {
                // Two or three built processes play one real co-op expedition over a real socket: the completion proof.
                CoopExpeditionProof.Begin(this, args);
            }
            else if (CoopPeerRunner.Requested(args))
            {
                // Two built processes, one host and one client, over a real socket: the co-op runtime proof.
                CoopPeerRunner.Begin(this, args);
            }
            else if (PlayerProfileRunner.Requested(args))
            {
                // TASK 183: profile the real player. Transitions are left on — a fade is part of what ships.
                Profile = gameObject.AddComponent<PlayerProfileRunner>();
                Profile.Begin(this, PlayerProfileRunner.SecondsFrom(args));
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Coop?.Dispose();
            SettingsScreen?.Dispose();
            SettingsRebinder?.Dispose();
            _settingsActions?.Dispose();
            if (Current == this) Current = null;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Compose(scene.name);

        /// <summary>Builds the scene's screen/run from code; tests call it directly for a scene they loaded themselves.</summary>
        public void Compose(string sceneName)
        {
            switch (sceneName)
            {
                case SceneNames.MainMenu:
                    RuinRail.Core.Input.GameplayInputGate.Reset(); // no menu layer of a torn-down run may keep gameplay input held
                    MusicBinder.EnterMainMenu();
                    CursorService.SetBase(CursorKind.Pointer);
                    MainMenuScreen.Create(this);
                    break;
                case SceneNames.Base:
                    RuinRail.Core.Input.GameplayInputGate.Reset();
                    MusicBinder.EnterShelter();
                    CursorService.SetBase(CursorKind.Pointer);
                    BaseHubScreen.Create(this);
                    break;
                case SceneNames.Dungeon:
                    RuinRail.Core.Input.GameplayInputGate.Reset(); // a fresh run never starts with a menu hold left over
                    CursorService.SetBase(CursorKind.Aim);
                    ExpeditionScene.Create(this);
                    break;
                default:
                    return;
            }

            ComposedScene = sceneName;
            ComposeCount++;
            Transition.DestinationComposed(); // safe to start uncovering: the scene is built
            SceneComposed?.Invoke(sceneName);
        }

        /// <summary>
        /// TASK 178: scene changes go through the transition overlay, so the screen is covered before the load and
        /// uncovered only once the destination has composed. A second request while one is in flight is ignored —
        /// that is the double-submit guard (pressing START twice must not launch two expeditions).
        ///
        /// The smoke runner drives scene flow deterministically and must not wait on a timed fade, so it loads
        /// directly; the composition path either way is identical.
        /// </summary>
        public void LoadScene(string sceneName)
        {
            if (IsSmoke) { SceneManager.LoadScene(sceneName); return; }
            if (!Transition.Begin(SceneTransitionViewModel.KindFor(ComposedScene, sceneName), sceneName)) return;
        }

        private RuinRail.Persistence.AutosaveFlusher _autosaveFlusher;

        private void Update()
        {
            if (!IsSmoke) Transition.Tick(Time.unscaledDeltaTime);
            // The flusher follows whichever profile session is open (none at the Main Menu).
            _autosaveFlusher?.SetAutosave(Menu?.Session?.Autosave);
        }

        /// <summary>Focus loss hands the cursor back to the OS; regaining it restores the cursor the current screen owns.</summary>
        private void OnApplicationFocus(bool focus)
        {
            if (focus) CursorService.Reapply();
        }

        /// <summary>True while a transition owns the screen: screens must not accept input in this window.</summary>
        public bool InputBlocked => Transition.BlocksInput || Errors.IsShowing;

        /// <summary>Plain snapshot of the slot on disk (tests/smoke outside the persistence assembly).</summary>
        public sealed class SaveProbe
        {
            public bool Success;
            public int BankedCoins;
            public int TotalXp;
            public string DisplayName = "";
            public string[] EquippedInstanceIds = Array.Empty<string>();
            public bool ExpeditionMarkerOpen;

            /// <summary>Permanent attribute ranks as stored on disk, indexed by SkillId (player/13).</summary>
            public int[] SkillRanks = Array.Empty<int>();

            public int UnspentSkillPoints;

            /// <summary>Deepest depth ever reached, as stored on disk. 0 for a profile that has entered no depth yet.</summary>
            public int DeepestDepthReached;

            /// <summary>
            /// Every affix roll stored on an equipped item, as "definitionId:affixId=value". Affix rolls are the one part
            /// of an item that the run now composes into real stats, so the built-player smoke has to be able to see
            /// that a save round trip preserved them verbatim rather than dropping or re-rolling them.
            /// </summary>
            public string[] EquippedAffixRolls = Array.Empty<string>();
        }

        public SaveProbe ProbeSave()
        {
            var loaded = Saves.Load();
            if (!loaded.Success) return new SaveProbe();
            var slot = loaded.Slot;
            return new SaveProbe
            {
                Success = true,
                BankedCoins = slot.Profile.BankedCoins,
                TotalXp = slot.Profile.TotalXp,
                DisplayName = slot.Profile.DisplayName,
                EquippedInstanceIds = slot.Profile.SafeLoadout != null ? slot.Profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).ToArray() : Array.Empty<string>(),
                ExpeditionMarkerOpen = slot.ActiveExpedition.IsOpen,
                SkillRanks = ((RuinRail.Gameplay.Progression.SkillId[])Enum.GetValues(typeof(RuinRail.Gameplay.Progression.SkillId))).Select(slot.Profile.Skills.GetRank).ToArray(),
                UnspentSkillPoints = slot.Profile.UnspentSkillPoints,
                DeepestDepthReached = slot.Profile.DeepestDepthReached,
                EquippedAffixRolls = slot.Profile.SafeLoadout != null
                    ? slot.Profile.SafeLoadout.Equipped
                        .Where(e => e.Item?.AffixRolls != null)
                        .SelectMany(e => e.Item.AffixRolls.Select(r => $"{e.Item.DefinitionId}:{r.AffixId}={r.Value}"))
                        .ToArray()
                    : Array.Empty<string>()
            };
        }

        public void Quit()
        {
            Menu.LeaveBase();
            Application.Quit(0);
        }
    }
}
