using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Dungeon.Grid;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Core.Input;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using RuinRail.Presentation.World;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Pause;
using RuinRail.UI.RunEnd;
using RuinRail.UI.Theme;
using RuinRail.UI.WeaponCache;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The solo expedition run composed from code (host-authoritative locally): per depth the seeded dungeon is
    /// generated from the biome's pool, prefabs instantiated, room runtimes attached; the local player is built from the
    /// at-risk inventory; camera, HUD, inventory, pause, tutorial, VFX and audio binders observe. Descend rebuilds the
    /// depth; Return/Fail hand back to the Shelter scene. Every decision is the existing services'.
    /// </summary>
    public sealed partial class ExpeditionScene : MonoBehaviour
    {
        /// <summary>
        /// The contextual tutorial prompt's band on the 640×360 frame. It sits below the HUD's top band — the boss bar
        /// owns y 6..23 and the room-title reveal y 28..48 — so a prompt can never be drawn over the name of the room
        /// the player has just walked into (asserted by <c>RoomHudQolTests</c>).
        /// </summary>
        public static readonly UiRect TutorialPromptRect = new(120, 52, 400, 40);

        private GameApp _app;
        private ExpeditionService _expedition;
        private DungeonRuntimeServices _services;
        private GroundLootLifetime _groundLifetime;
        private PlayerRig _rig;
        private PartyLifeRoster _roster;
        private ExpeditionParty _party;
        private LootAuthorityService _lootAuthority;
        private GameObject _dungeonRoot;
        /// <summary>The depth's environmental underlay (presentation only); rebuilt with every depth.</summary>
        public WorldSubstrate Substrate { get; private set; }
        private CameraRig _camera;
        private DungeonHudViewModel _hud;
        private InventoryViewModel _inventory;
        private MerchantViewModel _merchant;
        private DungeonMerchantInteractable _openMerchant;
        private WeaponCacheViewModel _weaponCache;
        private DungeonEventInteractable _openCache;
        private MinimapModel _minimap;
        private int? _revealedRoom;
        private PauseMenuViewModel _pause;
        private RunFailedViewModel _runFailed;
        private TutorialPromptService _prompts;
        private ExpeditionTutorialBinder _tutorial;
        private TransitVoteViewModel _vote;
        private Text _promptText;
        private TransitDecisionView _voteView;
        /// <summary>The Transit decision panel owns keyboard/controller focus (engaged) or holds input because the pointer is on it.</summary>
        private bool _voteEngaged;
        private bool _voteHoverHeld;
        private Canvas _canvas;
        private FocusList _voteList;
        private MenuInput _menuInput;
        private readonly List<IDisposable> _disposables = new();
        private readonly HashSet<AmmoType> _usefulAmmo = new();
        private Text _interactText;
        private PlayerInteractor _interactor;
        private IInputGlyphs _glyphs;
        private bool _ended;

        public ExpeditionService Expedition => _expedition;
        public PlayerRig Rig => _rig;
        /// <summary>The run-level party composition: one player entity per expedition participant (1-3).</summary>
        public ExpeditionParty Party => _party;
        /// <summary>Host arbitration of every loot/economy request in this run (82).</summary>
        public LootAuthorityService LootAuthority => _lootAuthority;
        /// <summary>True when this run actually composed more than one player entity — the source of every co-op UI flag.</summary>
        public bool IsCoop => _party != null && _party.IsCoop;
        public IReadOnlyDictionary<int, RoomRuntime> Rooms { get; private set; }
        public DungeonGenerationResult Generation { get; private set; }
        public int DepthsBuilt { get; private set; }
        /// <summary>Scene-level exit validation of the depth that was built (empty on success; the build fails otherwise).</summary>
        public IReadOnlyList<string> ExitProblems { get; private set; } = System.Array.Empty<string>();
        public CameraRig Camera => _camera;
        public DungeonHudViewModel Hud => _hud;
        public DungeonHudView HudView { get; private set; }
        public TransitVoteViewModel Vote => _vote;
        public PauseMenuViewModel Pause => _pause;
        public PauseMenuScreen PauseScreen { get; private set; }
        /// <summary>The Death / Run Lost screen: shown once from the expedition-ended callback after a conclusive failure.</summary>
        public RunFailedViewModel RunFailed => _runFailed;
        public RunFailedScreen RunFailedScreen { get; private set; }
        public InventoryViewModel Inventory => _inventory;
        public InventoryView InventoryView { get; private set; }
        /// <summary>The merchant trade screen (dungeon/58); bound to the merchant the player opened, closed with the room left behind.</summary>
        public MerchantViewModel Merchant => _merchant;
        public MerchantView MerchantView { get; private set; }
        /// <summary>The Weapon Cache selection screen (57.6); bound to the cache the player pressed Interact on.</summary>
        public WeaponCacheViewModel WeaponCache => _weaponCache;
        public WeaponCacheView WeaponCacheView { get; private set; }
        /// <summary>The run's room-graph minimap model; the HUD renders it, the room-entry events feed it.</summary>
        public MinimapModel Minimap => _minimap;
        /// <summary>The room the local player is standing in, or null before the first entry of the depth.</summary>
        public RoomRuntime CurrentRoom { get; private set; }
        /// <summary>Set once RETURN TO MAIN MENU was confirmed: the expedition end hands over to the Main Menu instead of the Shelter.</summary>
        public bool LeavingToMainMenu { get; private set; }

        public static ExpeditionScene Create(GameApp app)
        {
            var go = new GameObject("ExpeditionScene");
            var scene = go.AddComponent<ExpeditionScene>();
            scene.Build(app);
            return scene;
        }

        private void Build(GameApp app)
        {
            _app = app;
            var session = app.Menu.Session;
            if (session == null || !session.Expedition.IsExpeditionActive)
            {
                Debug.LogWarning("Dungeon scene loaded without an active expedition: returning to the Shelter.");
                app.LoadScene(SceneNames.Base);
                return;
            }

            _expedition = session.Expedition;
            // Solo, co-op host or co-op client (82): decided once, from the live session, never from a flag of the run.
            Mode = ResolveRunMode(app);
            if (Mode == CoopRunMode.Client)
            {
                // A joining client composes nothing until the host's start, the host's depth and its own replicated
                // character exist: it never builds a player of its own and never rolls a dungeon of its own.
                StartCoroutine(BuildWhenClientReady(app));
                return;
            }

            BuildCore(app, null);
        }

        private void BuildCore(GameApp app, GameObject ownedNetworkPlayer)
        {
            var session = app.Menu.Session;
            _roster = new PartyLifeRoster();
            var content = app.Content;
            var state = _expedition.State;
            var spawner = new DefaultEnemySpawner(content.Stagger);
            _services = new DungeonRuntimeServices
            {
                LootCatalog = content.Loot,
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = app.Configs.Resolve,
                ItemCatalog = content.Items,
                Prices = new PriceService(content.Economy),
                MerchantConfig = content.Merchant,
                CarriedWallet = state.CarriedWallet,
                EventConfig = content.Events,
                // 82: bosses are the host's to spawn; a client shows the host's boss as a replica.
                BossSpawner = Mode == CoopRunMode.Client ? null : new RosterBossSpawner(new DefaultBossSpawner(content.Bosses, content.Stagger, spawner)),
                Expedition = _expedition,
                ReviveAuthority = new PartyReviveAuthority(_roster)
            };
            _groundLifetime = new GroundLootLifetime(_services.GroundLoot, _expedition);
            _disposables.Add(_groundLifetime);

            // Camera + lighting.
            var camGo = new GameObject("MainCamera") { tag = "MainCamera" };
            camGo.transform.position = new Vector3(0f, 0f, -10f);
            var cam = camGo.AddComponent<UnityEngine.Camera>();
            // Not raw black: the clear colour is the biome substrate's darkest note, so the few pixels the underlay
            // cannot reach (a window wider than the layout plus its margin) still read as the same dark ground.
            cam.backgroundColor = WorldSubstrate.ClearColorFor(state.Biome);
            cam.clearFlags = CameraClearFlags.SolidColor;
            // No AudioListener here: the process listener (AudioListenerRig on GameApp) follows this camera.
            _camera = camGo.AddComponent<CameraRig>();
            _camera.SetConfig(content.CameraRig);
            var shake = camGo.AddComponent<CameraShake>();
            shake.Configure(content.Feedback, null, _camera);
            var lighting = camGo.AddComponent<BiomeLightingApplier>();
            lighting.SetCamera(cam);

            // Combat doors draw in the biome's skin (open housing / locked shutter) from the content catalog.
            RoomDoorLock.SkinResolver = biome => { var skin = content.DoorSkinFor(biome); return skin != null ? new DoorSkinSprites(skin.Open, skin.Locked) : default; };
            // Chests, pickups, coins, the merchant, event objects and the transit car draw the final world art from the catalog.
            WorldObjectArt.Resolver = content.WorldSpriteFor;
            // 58/26 ammo usefulness: chest and event rolls prefer the ammo the carried firearms consume; refreshed on every loadout change.
            _services.UsefulAmmoTypes = _usefulAmmo;
            RefreshUsefulAmmo();
            state.Inventory.EquippedChanged += OnEquippedChangedForAmmo;
            // Every pickup that lands on the ground (chest loot, event rewards, drops) gets its sound, prompt hooks and stinger.
            _services.GroundLoot.PickupTracked += OnPickupTracked;

            // Player from the at-risk inventory plus the profile's permanent attribute ranks (player/13): the
            // progression source is registered inside the one player composition, so the run's stat pipeline —
            // and therefore the run-start full-HP fill below it — already carries Vitality and every other attribute.
            _rig = new PlayerRig(content, app.Registry, app.Specials)
            {
                ReviveRequester = RequestDefibrillatorRevive,
                // A co-op client's body is simulated by the host, which runs its incoming-impact passives (Anchored,
                // Shock Absorber, Exo Lock) on its copy; the client's own rig runs every other passive.
                PassiveAdmit = Mode == CoopRunMode.Client ? RuinRail.Gameplay.Items.Passives.EquipmentPassiveRegistrar.IsMemberRigMechanic : null,
                GroundPickups = () => _services.GroundLoot.Tracked
            };
            _disposables.Add(_rig);

            // ---- Run-level party composition (80/82/83) ----
            // 82: the process only applies damage locally when it is the authority. This was never set from the real
            // role, so a client build would have applied damage itself; solo and host stay authoritative as before.
            DamageAuthority.LocalIsAuthoritative = Mode != CoopRunMode.Client && (app.Network == null || app.Network.Controller == null || app.Network.Controller.IsHostAuthority);
            _lootAuthority = new LootAuthorityService(Mode == CoopRunMode.Solo ? (app.Network?.Controller?.Authority ?? LocalAuthorityContext.Instance) : CoopAuthority());
            var drops = new ItemDropService(_services.CreateLootSpawner(gameObject));
            _lootAuthority.SetDropService(drops);
            GameObject player;
            switch (Mode)
            {
                case CoopRunMode.Host:
                    // Every member — this host included — is a real network player object (82); the host's own run
                    // player is composed onto the one it owns, so the clients see the host like any other member.
                    // The run player is composed onto the host's own object inside the spawn (before the loot
                    // authority registers it), so the party's local entity already is the rig's player.
                    _party = ComposeHostParty(app, session);
                    player = _party != null && _party.LocalEntity == _rig.Player ? _rig.Player : null;
                    break;
                case CoopRunMode.Client:
                    // The character the host spawned for this peer, never a second one (82).
                    player = _rig.Attach(ownedNetworkPlayer, state, _roster, _expedition.State.TransactionId, session.Profile.Skills);
                    _party = ComposeClientParty(app, session, player);
                    break;
                default:
                    player = _rig.Build(state, _roster, _expedition.State.TransactionId, Vector2.zero, null, session.Profile.Skills);
                    _party = ComposeParty(app, session, player);
                    break;
            }

            if (_party == null || player == null)
            {
                Debug.LogError($"Expedition composition failed in {Mode} mode: no party or no local player.");
                _expedition.Fail();
                return;
            }

            _disposables.Add(_party);
            player.GetComponent<PlayerAiming>().SetCamera(cam);
            _camera.SetFollow(() => player != null ? player.GetComponent<DeadSpectatorFollow>().FollowPosition : (Vector2)_camera.transform.position);
            _camera.SetAim(() => player != null ? (Vector2)player.transform.position + player.GetComponent<PlayerAiming>().AimDirection * 3f : (Vector2)_camera.transform.position);
            var binding = new PartyExpeditionBinding(_expedition, _roster, player.GetComponent<PlayerLifeStateComponent>())
            {
                // 84: the wipe is the host's decision; a client fails with the host's run end, never on its own view.
                FailOnWipe = Mode != CoopRunMode.Client
            };
            _disposables.Add(binding);

            // Player presentation: body (sprite + animator the animation driver draws into) and the held weapon on the
            // 360° pivot — the same composition every networked player object receives.
            PlayerVisualComposer.Compose(player, content, _rig.Reader);

            // Presentation + audio observers.
            var effects = new GameObject("Effects").AddComponent<EffectPool>();
            effects.Configure(64);
            effects.SetSpriteResolver(content.VfxFramesFor); // the accepted effect art, never the placeholder quad
            var feedback = effects.gameObject.AddComponent<CombatFeedback>();
            feedback.Configure(content.Feedback, effects, shake);
            var numbers = effects.gameObject.AddComponent<DamageNumberPool>();
            numbers.Configure(content.Feedback);
            numbers.Bind(player.GetComponent<HealthComponent>());
            feedback.Attach(player.GetComponent<HealthComponent>());
            feedback.Attach(player.GetComponent<WeaponVisualDriver>());
            app.AudioBinder.Attach(_rig.Loadout.GetComponent<WeaponVisualDriver>()).Attach(player.GetComponent<HealthComponent>(), true).Attach(player.GetComponent<PlayerDash>()).Attach(_roster).Attach(_expedition).Attach(_rig.Special);
            AttachFirearmAudio();
            state.Inventory.EquippedChanged += (_, _) => AttachFirearmAudio();
            // The expedition started in the Shelter, before this scene existed: enter the biome bed explicitly (track + ambience).
            app.MusicBinder.Attach(_expedition);
            app.MusicBinder.EnterExpedition(state);

            // HUD, inventory, pause, tutorial.
            _canvas = UiKit.Canvas("RunUi", 20);
            _hud = new DungeonHudViewModel();
            _hud.BindPlayer(player.GetComponent<HealthComponent>(), player.GetComponent<PlayerDash>(), player.GetComponent<PlayerLifeStateComponent>());
            _hud.BindWeapons(_rig.Loadout, _rig.Loadout.GetSlot(WeaponSlot.Primary), _rig.Loadout.GetSlot(WeaponSlot.Secondary), t => state.Inventory.Get(t), _rig.Special);
            _hud.BindInventory(state.Inventory);
            // Timed effects: the HUD reads the runner that owns their countdowns, so a chip cannot outlive its buff.
            _hud.BindStatusEffects(_rig.Consumables != null ? _rig.Consumables.Effects : null);
            _hud.BindConsumableUse(_rig.Consumables != null ? _rig.Consumables.UseAction : null);
            _hud.BindExpedition(_expedition);
            _hud.BindParty(_roster);
            _hud.SetDisplayName(_expedition.State.TransactionId, session.Profile.DisplayName);
            // Every composed member's sanitized name reaches the party rows, and a member held in reconnect grace (85)
            // shows as DISCONNECTED rather than silently vanishing from the party block.
            foreach (var member in _party.Members)
            {
                var life = member.GameObject != null ? member.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
                if (life != null) _hud.SetDisplayName(life.ParticipantId, member.Identity.DisplayName);
            }

            _party.Presence.Despawned += OnPartyMemberLeft;
            _party.Presence.Reconnected += OnPartyMemberReconnected;
            _disposables.Add(new ActionDisposable(() =>
            {
                if (_party == null) return;
                _party.Presence.Despawned -= OnPartyMemberLeft;
                _party.Presence.Reconnected -= OnPartyMemberReconnected;
            }));
            // 91 enemy-remaining readout: the local player's current room is the one authority — its lifecycle decides
            // whether anything is counted (active standard combat only) and its encounter membership gives the number.
            _hud.BindEnemyCount(() => CurrentRoom != null ? (CurrentRoom.ShowsEnemyCount, CurrentRoom.EnemiesRemaining) : (false, 0));
            HudView = DungeonHudView.Create(_hud);
            // One map model for the run; BuildDepth fills it from the generated layout and the room-entry events feed it.
            _minimap = new MinimapModel();
            HudView.BindMinimap(_minimap);
            // 32 dropping: solo and the host drop into the same tracked ground loot the loot authority drops into (the
            // host's co-op sync replicates it); a co-op member asks the host, which drops from its copy and revokes.
            var lootReceiver = player.GetComponent<PlayerLootReceiver>();
            if (Mode == CoopRunMode.Client) lootReceiver?.SetHostDrop(RequestHostDrop);
            else lootReceiver?.SetDropService(drops);
            _inventory = new InventoryViewModel();
            _inventory.Bind(state.Inventory, lootReceiver, () => state.CarriedCoins, app.Specials);
            // 84/86: in co-op the shared world must keep running while one player is in a menu — a client can never
            // stop the host simulation, and the host opening Pause is local UI too. Solo keeps the accepted
            // time-scale pause. The flag is the actually composed party size, never a literal.
            var worldPause = new TimeScalePause();
            var isCoop = _party.IsCoop;
            InstallLootArbiter(isCoop);
            _inventory.ConfigurePause(worldPause, isCoop);
            _disposables.Add(_inventory);
            _inventory.Changed += RefreshInventoryUi;
            InventoryView = InventoryView.Create(_inventory);
            // The survivor portrait is the player's own idle sprite, handed over by the composition root (the view never loads art).
            var playerSet = content.AnimationSetFor(CharacterVisual.PlayerActorId);
            if (playerSet != null && playerSet.TryGet("Idle", BodyFacing8.S, out var idle) && idle.Frames.Length > 0) InventoryView.SetPortrait(idle.Frames[0]);
            _merchant = new MerchantViewModel();
            _merchant.ConfigurePause(worldPause, isCoop);
            _disposables.Add(_merchant);
            _merchant.Changed += RefreshMerchantUi;
            MerchantView = MerchantView.Create(_merchant);
            _weaponCache = new WeaponCacheViewModel();
            _weaponCache.ConfigurePause(worldPause, isCoop);
            _disposables.Add(_weaponCache);
            _weaponCache.Changed += RefreshWeaponCacheUi;
            WeaponCacheView = WeaponCacheView.Create(_weaponCache);
            _pause = new PauseMenuViewModel(_rig.Reader, worldPause, isCoop, app.SettingsScreen, app.Quit, ReturnToMainMenu, () => _expedition != null && _expedition.IsExpeditionActive);
            _disposables.Add(_pause);
            // The Run Lost screen: the existing failure transaction decides the loss; this only shows its summary and offers the two exits.
            _runFailed = new RunFailedViewModel(LeaveForShelterAfterRunLost, ReturnToMainMenu);
            _runFailed.ConfigurePause(worldPause, isCoop);
            _disposables.Add(_runFailed);
            _runFailed.Changed += RefreshRunFailedUi;
            // Tab never opens the inventory under the pause menu or the Run Lost screen; Esc with the inventory open closes the inventory instead of pausing.
            _rig.Reader.InventoryToggled += () => { if (!_pause.IsOpen && !_merchant.IsOpen && !_weaponCache.IsOpen && !_runFailed.IsOpen) _inventory.Toggle(); };
            _pause.BeforePauseToggle = () =>
            {
                if (_runFailed != null && _runFailed.IsOpen) return true; // Esc never opens the pause menu over the Run Lost screen
                if (_voteEngaged) { RequestVoteRelease(); return true; } // Esc hands focus back from the Transit panel first
                if (_weaponCache.IsOpen) { _weaponCache.Close(); return true; }
                if (_merchant.IsOpen) { _merchant.Close(); return true; }
                if (!_inventory.IsOpen) return false;
                _inventory.Close();
                return true;
            };
            _menuInput = _canvas.gameObject.AddComponent<MenuInput>();
            // Esc is the Pause action here (owned by the player input reader); the menu input keeps B / pad Back only.
            _menuInput.KeyboardBackEnabled = false;
            _menuInput.Back += () =>
            {
                if (_runFailed.IsOpen) return; // the Run Lost screen has no back: one of its two exits must be chosen
                if (_pause.IsOpen) { _pause.Back(); return; }
                if (_voteEngaged) { if (_vote != null && _vote.AwaitingReturnConfirmation) _vote.CancelReturn(); else RequestVoteRelease(); return; }
                if (_weaponCache.IsOpen) { _weaponCache.Close(); return; }
                if (_merchant.IsOpen) { _merchant.Close(); return; }
                if (_inventory.IsOpen) { if (_inventory.Selected.HasValue) _inventory.CancelSelection(); else _inventory.Close(); }
            };
            _menuInput.InputBlocked = () => app.InputBlocked;
            PauseScreen = PauseMenuScreen.Create(_canvas.transform, _menuInput, _pause);
            _pause.Changed += RefreshPauseUi;
            RunFailedScreen = RunFailedScreen.Create(_canvas.transform, _menuInput, _runFailed);
            _prompts = new TutorialPromptService(new SaveSlotTutorialProgress(session.Slot, session.Autosave, () => app.Settings.Current.Tutorial.ShowPrompts), new SchemeGlyphs(InputScheme.KeyboardMouse));
            _tutorial = new ExpeditionTutorialBinder(_prompts, _rig.Reader).Attach(_expedition).Attach(player.GetComponent<HealthComponent>(), state.Inventory).Attach(_rig.Loadout);
            _disposables.Add(_tutorial);
            _promptText = UiKit.Label(_canvas.transform, string.Empty, TutorialPromptRect, 1, TextAnchor.UpperCenter, wrap: true);
            _prompts.Changed += () => _promptText.text = _prompts.ActiveText;
            _tutorial.ObserveExpeditionStarted();
            // The one interaction prompt (ui/90): what the Interact press would do to the nearest usable world object.
            _glyphs = new SchemeGlyphs(InputScheme.KeyboardMouse);
            _interactor = player.GetComponent<PlayerInteractor>();
            // Wide enough for an event prompt with its cost and refusal reason ("[E] REPAIR BROKEN MACHINE (100 COINS) — NEED 63 MORE COINS").
            _interactText = UiKit.Label(_canvas.transform, string.Empty, new UiRect(120, 250, 400, 24), 1, TextAnchor.MiddleCenter);

            _expedition.DepthEntered += OnDepthEntered;
            _expedition.TransitOpened += OnTransitOpened;
            _expedition.ExpeditionEnded += OnExpeditionEnded;
            ComposeCoopRuntime(app, player);
            BuildDepth();
        }

        private void BuildDepth()
        {
            var content = _app.Content;
            var state = _expedition.State;
            // 82: a client never rolls a depth. It waits for the host's payload for exactly this depth and rebuilds
            // from its seed, biome and generation round; anything else is a desync and fails loudly.
            var hostPayload = default(DungeonSyncPayload);
            if (Mode == CoopRunMode.Client && !TryGetHostDepthPayload(state, out hostPayload))
            {
                _awaitingHostDepth = true;
                return;
            }

            _awaitingHostDepth = false;
            if (_dungeonRoot != null) Destroy(_dungeonRoot);
            var pools = BiomeRoomPools.Build(content.Rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            var pool = pools.PoolFor(state.Biome);
            Dictionary<int, RoomRoot> rooms = null;
            var exitProblems = new List<string>();
            // 53 "Generation Failure": a layout that instantiates with an open doorway onto the void is discarded and
            // the next deterministic round is rolled; it is never patched in place. A client starts at the round the
            // host kept, so it lands on the same layout without re-deciding anything.
            var startRound = Mode == CoopRunMode.Client ? Mathf.Max(1, hostPayload.Rounds) : 1;
            for (var firstRound = startRound; firstRound <= DungeonGenerationPipeline.DefaultMaxRounds; firstRound = Generation.Rounds + 1)
            {
                Generation = DungeonGenerationPipeline.Generate(generator, pool, state.RunSeed, state.Depth, firstRound: firstRound);
                if (!Generation.Success) break;
                _dungeonRoot = new GameObject($"Dungeon_D{state.Depth}_{state.Biome}");
                rooms = DungeonLayoutInstantiator.Instantiate(Generation.Layout, _dungeonRoot.transform);
                exitProblems = DungeonExitValidator.Validate(Generation.Layout, rooms);
                if (exitProblems.Count == 0 || Mode == CoopRunMode.Client) break;
                Debug.LogWarning($"Dungeon round {Generation.Rounds} discarded: " + string.Join(" | ", exitProblems));
                Destroy(_dungeonRoot);
                _dungeonRoot = null;
                rooms = null;
            }

            var poolFingerprint = DungeonFingerprints.RoomPool(pool);
            Destroy(rules);
            if (Mode == CoopRunMode.Client)
            {
                var desync = ClientDepthDesync(hostPayload, poolFingerprint, rooms != null && exitProblems.Count == 0);
                if (desync != null)
                {
                    Debug.LogError("COOP-CLIENT depth rebuild refused: " + desync);
                    _coopClient?.ReportDepth(state.Depth, false, Generation != null && Generation.Success ? DungeonFingerprints.Layout(Generation.Layout) : string.Empty, desync);
                    LastDepthDesync = desync;
                    if (_dungeonRoot != null) Destroy(_dungeonRoot);
                    _expedition.Fail();
                    return;
                }
            }

            if (!Generation.Success || rooms == null)
            {
                Debug.LogError("Dungeon generation failed: " + (Generation.Error ?? "no round produced a dungeon without an open exit into the void"));
                _expedition.Fail();
                return;
            }

            ExitProblems = exitProblems;
            // 83: the dungeon scales for the party that actually exists. StartingPartySize is what the lobby promised;
            // ScalingPartySize is what was composed. They are equal for a correctly started run — ComposeParty refuses
            // to start otherwise — and this makes the guarantee structural rather than a comment.
            var partySize = _party != null ? _party.ScalingPartySize : state.StartingPartySize;
            // 82: enemy spawning is a host decision; a client's rooms get a spawner that refuses every spawn (and
            // never activate anyway — they only mirror the host's lifecycle).
            IEnemySpawner roomSpawner = new DefaultEnemySpawner(content.Stagger);
            IEliteSpawner eliteSpawner = new DefaultEliteSpawner(content.Stagger);
            if (Mode == CoopRunMode.Client)
            {
                roomSpawner = new AuthoritativeEnemySpawner(roomSpawner, new CoopClientAuthority());
                eliteSpawner = null;
            }

            var context = new DungeonRuntimeContext(state.RunSeed, state.Depth, partySize, content.Enemies, roomSpawner, content.DepthScaling, content.Elites, eliteSpawner)
            {
                IsAuthoritative = Mode != CoopRunMode.Client
            };
            Rooms = DungeonRoomRuntimeComposer.Attach(Generation.Layout, rooms, context, _services);
            if (Mode == CoopRunMode.Client) foreach (var runtime in Rooms.Values) runtime.SetAuthoritative(false);
            foreach (var runtime in Rooms.Values)
            {
                runtime.EnemySpawned += (_, enemy) => BindEnemyPresentation(enemy);
                var isBossRoom = runtime.State.RoomType == RoomType.Boss;
                runtime.Activated += _ => { if (isBossRoom) _app.MusicBinder.ObserveBossRoomEntered(); else _app.MusicBinder.ObserveCombatStarted(); };
                runtime.Cleared += (_, _) => { if (!isBossRoom) _app.MusicBinder.ObserveCombatEnded(); if (_expedition.IsExpeditionActive) _expedition.RecordRoomCleared(); };
                var content2 = runtime.GetComponent<RoomContentBinding>();
                if (content2 != null && content2.Boss != null)
                {
                    _app.MusicBinder.Attach(content2.Boss);
                    if (content2.Boss.Boss != null)
                    {
                        var boss = content2.Boss;
                        BindActorPresentation(boss.Boss, boss.Boss.Definition != null ? boss.Boss.Definition.Id : null, isElite: false);
                        // Boss health is a dedicated screen bar (no small world bar): name + authoritative HP while the fight is on.
                        // "Active encounter" is the boss room being entered and unresolved (the boss acquires its target from BossEngagement.Begin on that entry).
                        var bossRoom = runtime;
                        _hud.BindBoss(boss.Boss.Definition != null ? boss.Boss.Definition.DisplayName : "BOSS", boss.Boss.Health, () => bossRoom.Lifecycle == RoomLifecycleState.Active && !boss.IsDefeated);
                        // The room introduction plays for this peer's own survivor while the engagement holds the boss back.
                        if (runtime.Engagement is BossEngagement bossEngagement)
                            bossEngagement.IntroStarted += (engagement, entering) =>
                            {
                                if (_rig?.Player == null || entering != _rig.Player) return;
                                BossIntroSequence.Play(_camera, boss.Boss, entering.transform, engagement,
                                    RoomDisplayNames.BiomeName(_expedition.State.Biome) + "  -  BOSS");
                            };
                    }
                }

                if (runtime.Engagement is EliteEngagement elite) elite.Spawned += (_, encounter) => { if (encounter.Elite != null) BindActorPresentation(encounter.Elite, encounter.Elite.Definition != null ? encounter.Elite.Definition.Id : null, isElite: true); };
                AttachRoomAudio(runtime);
                AttachMerchant(runtime);
                AttachWeaponCache(runtime);
                AttachEventNotices(runtime);
                AttachRoomPresence(runtime);
            }

            BuildMinimapForDepth();

            var startRoot = rooms[Generation.Graph.StartId];
            var spawn = startRoot.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            var position = spawn != null ? (Vector2)startRoot.transform.TransformPoint(spawn.WorldCenter) : (Vector2)startRoot.transform.position;
            if (Mode == CoopRunMode.Solo)
            {
                _rig.Player.transform.position = position;
                _rig.Player.GetComponent<Rigidbody2D>().position = position;
            }
            else
            {
                // Every member starts on the Start room's validated spawn markers, in the run's member order, on
                // every peer (the host places them authoritatively; a client places its own the same way so the
                // first reconciliation has nothing to correct).
                position = PlaceCoopMembersAtStart(Generation.Layout, rooms);
            }
            _camera.SetFollow(() => _rig.Player != null ? _rig.Player.GetComponent<DeadSpectatorFollow>().FollowPosition : (Vector2)_camera.transform.position);
            var bounds = Generation.Layout.Placements.Aggregate(new Rect(position, Vector2.zero), (r, p) => { var b = p.Bounds; var pr = new Rect((Vector2)b.min * GridConstants.TileWorldSize, (Vector2)b.size * GridConstants.TileWorldSize); return Rect.MinMaxRect(Mathf.Min(r.xMin, pr.xMin), Mathf.Min(r.yMin, pr.yMin), Mathf.Max(r.xMax, pr.xMax), Mathf.Max(r.yMax, pr.yMax)); });
            _camera.SetVisibleBounds(bounds);
            // The dark environmental underlay the depth sits in, under the dungeon root so it dies with the depth.
            // Presentation only: Ground layer at a large negative order, no collider, no tile occupancy, no room
            // membership — pathing, sealing, encounter bounds, doors and the minimap never see it.
            Substrate = WorldSubstrate.Create(_dungeonRoot.transform, state.Biome, bounds);
            _camera.GetComponent<UnityEngine.Camera>().backgroundColor = WorldSubstrate.ClearColorFor(state.Biome);
            _camera.GetComponent<BiomeLightingApplier>().Apply(content.LightingFor(state.Biome));
            // The depth objective is contextual, not a permanent text block: it rides the room-title reveal of the
            // Start room the player is standing in, and disappears with it.
            var startPlacement = Generation.Layout.GetPlacement(Generation.Graph.StartId);
            var startName = startPlacement != null ? RoomDisplayNames.NameOf(startPlacement.Definition) : RoomDisplayNames.FallbackName(RoomType.Start);
            HudView?.RoomTitle?.Reveal(startName, $"DEPTH {state.Depth} — REACH THE BOSS");
            _revealedRoom = Generation.Graph.StartId;
            _minimap?.MarkEntered(Generation.Graph.StartId);
            CurrentRoom = Rooms != null && Rooms.TryGetValue(Generation.Graph.StartId, out var startRuntime) ? startRuntime : null;
            DepthsBuilt++;
            // The personal-best record is written here and nowhere else: the depth has generated, validated, composed
            // and placed the player. Every earlier return in this method is a failed arrival, so a descend that could
            // not build a dungeon never advances the record.
            _expedition.RecordDepthArrival(state.Depth);
            BindCoopDepth(pool, poolFingerprint);
        }

        /// <summary>Elite / Boss body: the same seam as every other character, driven by the moveset actor state.</summary>
        public void BindActorPresentation(MovesetActorController actor, string actorId, bool isElite)
        {
            var body = CharacterVisual.Attach(actor.gameObject, _app.Content.AnimationSetFor(actorId));
            if (actor.GetComponent<EnemyAnimationDriver>() == null) actor.gameObject.AddComponent<EnemyAnimationDriver>().Configure(body, null, actor, actor.GetComponent<Rigidbody2D>());
            // Elites carry the stronger world bar in the Elite accent; bosses use the screen bar instead.
            if (isElite && actor.GetComponent<WorldHealthBar>() == null)
                actor.gameObject.AddComponent<WorldHealthBar>().Configure(actor.Health, WorldHealthBar.Style.Elite, 1.7f, _app.Content.Feedback != null ? _app.Content.Feedback.EliteBossTelegraphColor : (Color?)null);
            // Bosses get the same combat read as every normal enemy: the ground danger marker for each telegraphed move
            // (without it a boss's slams, zones and dashes had no marker at all outside a co-op client), the hit flash,
            // damage numbers and impact feedback. Presentation only: it reads the actor's state, never drives it.
            if (actor is BossController && actor.GetComponent<TelegraphIndicator>() == null)
            {
                var effects = FindFirstObjectByType<EffectPool>();
                actor.gameObject.AddComponent<TelegraphIndicator>().Configure(_app.Content.Feedback, effects, null, actor);
                var bossFlash = actor.gameObject.AddComponent<HitFlash>();
                bossFlash.Configure(_app.Content.Feedback, actor.Health, actor.Impact, body != null ? body.Renderer : null);
                bossFlash.UseBossProfile(); // the tint's strength follows each hit's effective damage
                effects?.GetComponent<DamageNumberPool>()?.Bind(actor.Health);
                effects?.GetComponent<CombatFeedback>()?.Attach(actor.Impact);
            }

            // Elites flash red on an applied hit like every normal enemy (they had no hit flash at all).
            if (isElite && actor.GetComponent<HitFlash>() == null)
                actor.gameObject.AddComponent<HitFlash>().Configure(_app.Content.Feedback, actor.Health, actor.Impact, body != null ? body.Renderer : null);

            // Audio: telegraph / death / phase cues and the hit cue for Elites and Bosses; the Elite encounter stinger.
            _app.AudioBinder.Attach(actor).Attach(actor.Health, false);
            if (actor is EliteController eliteActor) _app.MusicBinder.Attach(eliteActor);
        }

        /// <summary>The presentation of one normal enemy (body, bar, flash, telegraph marker, animation, audio); public for the proof tests that spawn extra enemies into a live run.</summary>
        public void BindEnemyPresentation(EnemyController enemy)
        {
            if (enemy == null) return;
            var effects = FindFirstObjectByType<EffectPool>();
            var feedback = effects != null ? effects.GetComponent<CombatFeedback>() : null;
            var numbers = effects != null ? effects.GetComponent<DamageNumberPool>() : null;
            numbers?.Bind(enemy.GetComponent<HealthComponent>());
            var body = CharacterVisual.Attach(enemy.gameObject, _app.Content.AnimationSetFor(enemy.Definition != null ? enemy.Definition.Id : null));
            enemy.gameObject.AddComponent<WorldHealthBar>().Configure(enemy.GetComponent<HealthComponent>(), WorldHealthBar.Style.Normal);
            var flash = enemy.gameObject.AddComponent<HitFlash>();
            flash.Configure(_app.Content.Feedback, enemy.GetComponent<HealthComponent>(), enemy.GetComponent<RuinRail.Gameplay.Combat.Impact.ImpactReceiver>(), body != null ? body.Renderer : null);
            var indicator = enemy.gameObject.AddComponent<TelegraphIndicator>();
            indicator.Configure(_app.Content.Feedback, effects, enemy, null);
            enemy.gameObject.AddComponent<EnemyAnimationDriver>().Configure(body, enemy, null, enemy.GetComponent<Rigidbody2D>());
            _app.AudioBinder.Attach(enemy).Attach(enemy.GetComponent<HealthComponent>(), false);
            feedback?.Attach(enemy.GetComponent<RuinRail.Gameplay.Combat.Impact.ImpactReceiver>());
            _tutorial.ObserveEnemySpawned();
        }

        /// <summary>The ammo types the carried firearms consume (Primary + Secondary), for the 70/30 usefulness rule; melee/heat/charge weapons add none.</summary>
        public IReadOnlyCollection<AmmoType> UsefulAmmoTypes => _usefulAmmo;

        private void RefreshUsefulAmmo()
        {
            _usefulAmmo.Clear();
            var inventory = _expedition?.State?.Inventory;
            if (inventory == null) return;
            foreach (var slot in new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon })
            {
                var item = inventory.GetEquipped(slot);
                if (item != null && _app.Configs.Resolve(item.DefinitionId) is RangedWeaponDefinition ranged) _usefulAmmo.Add(ranged.AmmoType);
            }
        }

        private void OnEquippedChangedForAmmo(EquippedSlot slot, ItemInstance item)
        {
            if (slot == EquippedSlot.PrimaryWeapon || slot == EquippedSlot.SecondaryWeapon) RefreshUsefulAmmo();
        }

        /// <summary>Dry-fire cue for every mounted firearm (re-run after a remount; stale subscriptions die with their components).</summary>
        private void AttachFirearmAudio()
        {
            if (_rig?.Player == null) return;
            foreach (var weapon in _rig.Player.GetComponents<RangedWeapon>()) _app.AudioBinder.Attach(weapon);
        }

        /// <summary>A pickup landed on the ground: drop/pickup sounds, the Legendary stinger and the tutorial pickup prompt.</summary>
        private void OnPickupTracked(GameObject pickup)
        {
            if (pickup == null) return;
            var item = pickup.GetComponent<WorldItemPickup>();
            if (item != null)
            {
                _app.AudioBinder.Attach(item);
                _app.MusicBinder.Attach(item);
                _tutorial?.Attach(item);
                _tutorial?.ObserveLootSpawned();
            }

            var coins = pickup.GetComponent<CoinPickup>();
            if (coins != null) _app.AudioBinder.Attach(coins);
        }

        /// <summary>Per-room audio hooks the binders cannot reach on their own: chests, the merchant and the combat doors.</summary>
        private void AttachRoomAudio(RoomRuntime runtime)
        {
            var binding = runtime.GetComponent<RoomContentBinding>();
            if (binding != null)
            {
                foreach (var chest in binding.Chests) _app.AudioBinder.Attach(chest);
                // An Elite's or boss's reward chest appears only when the encounter is won.
                binding.RewardChestSpawned += chest => _app.AudioBinder.Attach(chest);
                _app.AudioBinder.Attach(binding.Merchant);
            }

            foreach (var door in runtime.Doors)
            {
                if (door == null) continue;
                door.LockChanged += (d, _) => _app.AudioBinder.PlayDoor(d.BlockerArea().center);
            }
        }

        /// <summary>
        /// The merchant interactable's Opened event is the one seam from the world into the trade screen: E on the
        /// prompt opens the window exactly once (the input gate swallows repeats while it is up), bound to that room's
        /// merchant service and the run's inventory/wallet.
        /// </summary>
        private void AttachMerchant(RoomRuntime runtime)
        {
            var binding = runtime.GetComponent<RoomContentBinding>();
            if (binding == null || binding.Merchant == null) return;
            binding.Merchant.Opened += OnMerchantOpened;
        }

        /// <summary>
        /// The Weapon Cache's one seam into its selection screen. The event answers the Interact press with
        /// "a choice is required" and hands over the acting player; the screen opens once, bound to that cache, and
        /// the event's own Choose stays the authority for the reward.
        /// </summary>
        private void AttachWeaponCache(RoomRuntime runtime)
        {
            var binding = runtime.GetComponent<RoomContentBinding>();
            if (binding == null || binding.Event == null || binding.EventInstance is not WeaponCacheEvent) return;
            binding.Event.ChoiceRequested += OnWeaponCacheChoiceRequested;
        }

        /// <summary>
        /// Every event object (57) reports what a press did through the HUD notice: the reward that dropped, a failed
        /// repair, a started wave, a purchased heal — and a refused press says why (not enough coins, HP full, used).
        /// Async events (Cursed Chest, Supply Signal) announce their resolution from the event's own Completed.
        /// </summary>
        private void AttachEventNotices(RoomRuntime runtime)
        {
            var binding = runtime.GetComponent<RoomContentBinding>();
            if (binding != null && binding.Transit != null)
            {
                // Boarding the transit (60 step 5) restates the open decision on the notice line; the vote panel itself is already up.
                binding.Transit.Boarded += _ => Notify("TRANSIT BOARDED: RETURN TO SHELTER (1) OR DESCEND DEEPER (2)", false);
            }

            if (binding == null || binding.Event == null || binding.EventInstance == null) return;
            var instance = binding.EventInstance;
            binding.Event.Activated += (_, result) => { if (result.Outcome != DungeonEventOutcome.Success && result.Outcome != DungeonEventOutcome.Failed || !result.IsTerminal) Notify(EventOutcomeText.For(instance, result, _app.Configs.Resolve), false); };
            binding.Event.Refused += (_, _, result) => Notify(EventOutcomeText.For(instance, result, _app.Configs.Resolve), true);
            instance.Completed += (e, result) => Notify(EventOutcomeText.For(e, result, _app.Configs.Resolve), result.Outcome == DungeonEventOutcome.Failed);
            if (instance is SupplySignalEvent signal) _signals.Add(signal);
        }

        private readonly List<SupplySignalEvent> _signals = new();
        private string _heldNotice;

        /// <summary>The last notice the run announced (proof/diagnostics) and how many there were.</summary>
        public string LastNotice { get; private set; } = string.Empty;
        public int Notices { get; private set; }

        private void Notify(string text, bool isProblem)
        {
            if (string.IsNullOrEmpty(text)) return;
            LastNotice = text;
            Notices++;
            HudView?.Notice?.Show(text, isProblem);
        }

        /// <summary>A running Supply Signal keeps its countdown on the notice line; nothing else is held.</summary>
        private void RefreshHeldNotice()
        {
            var running = _signals.FirstOrDefault(s => s.IsRunning);
            var text = running != null ? $"SUPPLY SIGNAL: SURVIVE {Mathf.CeilToInt(running.Remaining)} S" : null;
            if (text == _heldNotice) return;
            _heldNotice = text;
            if (text != null) HudView?.Notice?.Hold(text);
            else HudView?.Notice?.Clear();
        }

        private void OnWeaponCacheChoiceRequested(DungeonEventInteractable interactable, EventActor actor)
        {
            if (_weaponCache == null || interactable == null || actor == null) return;
            if (interactable.Event is not WeaponCacheEvent cache) return;
            // The screen belongs to whoever pressed Interact on their own machine: a joined member's press replayed on
            // the host never opens the host's screen (that member opened its own).
            if (Mode != CoopRunMode.Solo && actor.GameObject != null && _rig?.Player != null && actor.GameObject != _rig.Player) return;
            if (_weaponCache.IsOpen || (_pause?.IsOpen ?? false) || (_inventory?.IsOpen ?? false) || (_merchant?.IsOpen ?? false)) return;
            if (_openCache != interactable)
            {
                _openCache = interactable;
                _weaponCache.Bind(cache, actor, _expedition?.State?.Inventory, _app.Specials);
            }

            if (Mode == CoopRunMode.Client)
            {
                var node = NodeOf(interactable);
                _weaponCache.ChooseRequest = index => { _coopClient?.SendCacheChoice(node, index); return _coopClient != null; };
            }

            _weaponCache.Open();
        }

        /// <summary>
        /// Composes the run's party from the lobby's start snapshot (81/83).
        ///
        /// The snapshot is the single source of who is in the expedition — the same object the host used to decide the
        /// party size the dungeon is about to be scaled for. Composing from it is what closes the latent scaling bug:
        /// if the snapshot promises three players and only one entity can be built, this returns null and the run fails
        /// with a multiplayer error instead of silently generating a trio-scaled dungeon for a solo player.
        /// </summary>
        private ExpeditionParty ComposeParty(GameApp app, BaseSession session, GameObject localPlayer)
        {
            var content = app.Content;
            var snapshot = session.Lobby?.StartSnapshot;
            var local = session.Lobby?.Get(BaseSession.LocalClientId);
            var members = new List<PartyMemberDescriptor>();
            if (snapshot != null)
            {
                foreach (var member in snapshot.Members)
                {
                    // The lobby carries identity and at-risk loadout, not display names; the local player's own profile
                    // name is used for this peer and the roster's sanitizer supplies "Player N" for the rest (10).
                    var name = member.ClientId == BaseSession.LocalClientId ? session.Profile.DisplayName : null;
                    members.Add(new PartyMemberDescriptor(member.ClientId, name, member.ParticipantId, member.ClientId == BaseSession.LocalClientId));
                }
            }

            // 85: "Initial reconnect grace target: ~60 s" — a disconnected character stays represented and at risk.
            var grace = new ReconnectGraceService(ReconnectGraceService.DefaultGraceSeconds,
                () => _expedition != null && _expedition.IsExpeditionActive);
            var request = new PartyCompositionRequest
            {
                Members = members,
                LocalClientId = BaseSession.LocalClientId,
                LocalDisplayName = session.Profile.DisplayName ?? "Player 1",
                LocalParticipantId = local?.ParticipantId ?? _expedition.State.TransactionId,
                LocalEntity = localPlayer,
                IsHost = app.Network?.Controller == null || app.Network.Controller.IsHostAuthority,
                Balance = content.PlayerBalance,
                Caps = content.StatCaps,
                LifeRoster = _roster,
                Loot = _lootAuthority,
                // On a live host session every member is spawned as the release network player object, so the other
                // peers receive it; offline the same composition is built locally. Either way it is one entity per
                // member through the same presence service.
                RemoteFactory = LiveRemoteFactory(app, localPlayer),
                LiveConnection = (app.Network?.Driver as NgoNetworkDriver)?.Connections,
                Items = app.Registry,
                Ammo = content.AmmoBalance,
                Grace = grace,
                DisplayNamePolicy = content.DisplayNamePolicy,
                // Remote members spawn a ring around the start marker; the local player keeps the spawn the run placed it at.
                SpawnPosition = identity => (Vector2)localPlayer.transform.position + RemoteSpawnOffset(identity.ClientId),
                // Presentation only: the same accepted visual composition every player gets. Never local input, camera,
                // HUD, cursor or audio listener — those belong to the local presentation composed once below.
                Decorate = (go, isLocalOwner) => { if (!isLocalOwner) PlayerVisualComposer.Compose(go, content); }
            };

            var party = ExpeditionParty.Compose(request, out var error);
            if (party == null)
            {
                Debug.LogError($"Co-op composition failed ({error}): the expedition expected {members.Count} participant(s). The run is not started underfilled (83).");
                return null;
            }

            return party;
        }

        /// <summary>
        /// Routes world pickups through the host arbiter for a co-op party: one shared object, one winner, one
        /// transaction, and a coin pile split across the party (58/82). Solo installs nothing, so the accepted solo
        /// pickup path is untouched. Cleared in OnDestroy — the next run must never inherit this run's arbiter.
        /// </summary>
        private void InstallLootArbiter(bool isCoop)
        {
            PickupArbiter.Clear();
            if (Mode == CoopRunMode.Client)
            {
                // 82: a client never resolves ground loot. Its pickups are presentation of the host's; the host resolves
                // the same press (or the same pull) against its own objects and grants the result to this player.
                PickupArbiter.Items = (_, _) => false;
                PickupArbiter.Coins = (_, _) => false;
                return;
            }

            if (!isCoop || _lootAuthority == null) return;
            PickupArbiter.Items = (pickup, interactor) =>
            {
                var clientId = ClientIdOf(interactor);
                if (clientId == null) return null;
                var granted = pickup.Item != null ? pickup.Item.ToSnapshot() : null;
                var transaction = NewTransactionId();
                var accepted = _lootAuthority.RequestPickup(transaction, clientId.Value, pickup).Verdict == LootVerdict.Accepted;
                // A joined member's pickup lands in the host's mirror of its backpack; the member's own inventory gets it here.
                if (accepted && _coopHost != null) _coopHost.SendGrant(clientId.Value, granted, transaction, "pickup");
                return accepted;
            };
            PickupArbiter.Coins = (pile, interactor) =>
            {
                var clientId = ClientIdOf(interactor);
                if (clientId == null) return null;
                return _lootAuthority.RequestCoins(NewTransactionId(), clientId.Value, pile).Verdict == LootVerdict.Accepted;
            };
        }

        /// <summary>Which party member an interacting GameObject is; null for anything that is not a composed member.</summary>
        private ulong? ClientIdOf(GameObject interactor)
        {
            if (_party == null || interactor == null) return null;
            foreach (var member in _party.Members)
            {
                if (member.GameObject == interactor) return member.OwnerClientId;
            }

            return null;
        }

        private static string NewTransactionId() => System.Guid.NewGuid().ToString("N");

        /// <summary>The live NGO spawn factory when this process hosts a real session; null for solo/offline runs.</summary>
        private static IPlayerEntityFactory LiveRemoteFactory(GameApp app, GameObject localPlayer)
        {
            var driver = app.Network?.Driver as NgoNetworkDriver;
            var controller = app.Network?.Controller;
            if (driver == null || !driver.IsListening || controller == null || !controller.IsHostAuthority) return null;
            return driver.CreatePlayerFactory(app.Content.NetworkPlayerEntity,
                identity => (Vector3)((Vector2)localPlayer.transform.position + RemoteSpawnOffset(identity.ClientId)));
        }

        private void OnPartyMemberLeft(NetworkPlayerEntity entity)
        {
            var life = entity?.GameObject != null ? entity.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
            if (life == null) return;
            _roster?.Unregister(life);
            _hud?.SetMemberConnected(life.ParticipantId, false);
        }

        private void OnPartyMemberReconnected(NetworkPlayerEntity entity, ulong newClientId)
        {
            var life = entity?.GameObject != null ? entity.GameObject.GetComponent<PlayerLifeStateComponent>() : null;
            if (life == null) return;
            _hud?.SetMemberConnected(life.ParticipantId, true);
        }

        /// <summary>A deterministic ring offset so party members never spawn inside one another.</summary>
        private static Vector2 RemoteSpawnOffset(ulong clientId)
        {
            if (clientId == BaseSession.LocalClientId) return Vector2.zero;
            var angle = (clientId % ExpeditionParty.MaxPartySize) * (Mathf.PI * 2f / ExpeditionParty.MaxPartySize);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 1.5f;
        }

        /// <summary>
        /// The one room-entry seam (the same inset trigger that activates a combat room) feeding both presentations:
        /// the minimap's current room and discovery, and the room-title reveal. There is no second room detector.
        /// </summary>
        private void AttachRoomPresence(RoomRuntime runtime)
        {
            runtime.PlayerEntered += OnRoomEntered;
        }

        private void OnRoomEntered(RoomRuntime room, GameObject player)
        {
            if (room == null || player == null) return;
            var nodeId = room.State.NodeId;
            // Map discovery is the party's: the docs are silent on shared maps, and the smallest multiplayer-safe
            // behaviour for one dungeon the whole party is inside is that any member walking into a room discovers it
            // for everyone. Room activation itself is decided once by RoomRuntime under host authority (82).
            _minimap?.SetKind(nodeId, MinimapKindOf(room));
            _minimap?.MarkEntered(nodeId);

            // Everything below is this client's own camera context and must stay local to the owned player.
            if (_rig?.Player == null || player != _rig.Player) return;
            CurrentRoom = room;
            if (_revealedRoom == nodeId) return; // still the same room: the reveal never repeats
            _revealedRoom = nodeId;
            var definition = room.Root != null ? room.Root.Definition : null;
            var type = definition != null ? definition.RoomType : room.State.RoomType;
            // A boss room is named by its introduction card; the room-title banner would say it a second time.
            if (type == RoomType.Boss && room.Engagement is BossEngagement introduced && introduced.IntroHoldSeconds > 0f) return;
            HudView?.RoomTitle?.Reveal(RoomDisplayNames.NameOf(room.State.RoomId, type), RoleLineOf(room, type));
        }

        /// <summary>The role line under a room's name: the actual event kind for an Event room, the room type otherwise.</summary>
        private static string RoleLineOf(RoomRuntime room, RoomType type)
        {
            var binding = room.GetComponent<RoomContentBinding>();
            if (type == RoomType.Event && binding != null && binding.EventInstance != null)
                return EventPromptBuilder.TitleOf(binding.EventInstance.Kind).ToUpperInvariant();
            return RoomDisplayNames.RoleOf(type);
        }

        /// <summary>The minimap symbol a room earns once the player has been inside it.</summary>
        private static MinimapRoomKind MinimapKindOf(RoomRuntime room)
        {
            var type = room.Root != null && room.Root.Definition != null ? room.Root.Definition.RoomType : room.State.RoomType;
            if (type == RoomType.Event)
            {
                var binding = room.GetComponent<RoomContentBinding>();
                var kind = binding != null && binding.EventInstance != null ? binding.EventInstance.Kind : (DungeonEventKind?)null;
                return kind == DungeonEventKind.WeaponCache ? MinimapRoomKind.WeaponCache
                    : kind == DungeonEventKind.MedicalStation ? MinimapRoomKind.Medical
                    : MinimapRoomKind.Event;
            }

            return MinimapKindOf(type);
        }

        /// <summary>Room type to map symbol; the UI layer never sees the dungeon's own enum.</summary>
        public static MinimapRoomKind MinimapKindOf(RoomType type) => type switch
        {
            RoomType.Start => MinimapRoomKind.Start,
            RoomType.Loot => MinimapRoomKind.Loot,
            RoomType.Treasure => MinimapRoomKind.Treasure,
            RoomType.Merchant => MinimapRoomKind.Merchant,
            RoomType.Event => MinimapRoomKind.Event,
            RoomType.MedicalRecovery => MinimapRoomKind.Medical,
            RoomType.Boss => MinimapRoomKind.Boss,
            _ => MinimapRoomKind.Normal
        };

        /// <summary>
        /// Loads the depth's room graph into the map: every room's centre in layout tiles and every realised door
        /// connection. Nothing is discovered yet — the entry events decide what the player gets to see.
        /// </summary>
        private void BuildMinimapForDepth()
        {
            if (_minimap == null || Generation?.Layout == null) return;
            var state = _expedition.State;
            _minimap.BeginDepth(state.Depth, RoomDisplayNames.BiomeName(state.Biome));
            foreach (var placement in Generation.Layout.Placements.OrderBy(p => p.NodeId))
            {
                var bounds = placement.Bounds;
                var centre = new Vector2(bounds.xMin + bounds.width * 0.5f, bounds.yMin + bounds.height * 0.5f);
                // Before a room is entered the map shows only its outline, so the kind it carries here is the room's
                // architecture — the event a room actually holds is revealed when the player walks into it.
                _minimap.AddRoom(placement.NodeId, centre, MinimapKindOf(placement.Definition.RoomType));
            }

            foreach (var connection in Generation.Layout.Connections) _minimap.AddLink(connection.NodeA, connection.NodeB);
        }

        private void OnMerchantOpened(DungeonMerchantInteractable merchant, GameObject interactor)
        {
            if (_merchant == null || merchant == null || merchant.Merchant == null || _expedition?.State == null) return;
            if (_merchant.IsOpen || (_pause?.IsOpen ?? false) || (_inventory?.IsOpen ?? false)) return;
            // A joined member's press replayed on the host never opens the host's trade screen.
            if (Mode != CoopRunMode.Solo && interactor != null && _rig?.Player != null && interactor != _rig.Player) return;
            var state = _expedition.State;
            if (_openMerchant != merchant)
            {
                _openMerchant = merchant;
                _merchant.Bind(merchant.Merchant, state.Inventory, () => state.CarriedCoins, _app.Specials);
            }

            if (Mode == CoopRunMode.Client)
            {
                // 82: the host's merchant, this member's own wallet and backpack decide; the screen only asks.
                var node = NodeOf(merchant);
                _merchant.BuyRequest = index => { _coopClient?.SendBuy(node, index); return _coopClient != null ? TradeError.None : TradeError.NoSuchOffer; };
                _merchant.SellRequest = instanceId => { _coopClient?.SendSell(node, instanceId); return _coopClient != null ? TradeError.None : TradeError.SourceMissingItem; };
            }

            _merchant.Open();
        }

        /// <summary>The nearest usable interactable's prompt, or nothing (also nothing while a menu layer is open).</summary>
        public string CurrentInteractionPrompt { get; private set; } = string.Empty;

        private void RefreshInteractionPrompt()
        {
            if (_interactText == null) return;
            var text = string.Empty;
            if (_interactor != null && !(_pause?.IsOpen ?? false) && !(_inventory?.IsOpen ?? false) && !(_merchant?.IsOpen ?? false) && !(_weaponCache?.IsOpen ?? false))
            {
                var target = _interactor.FindNearestInteractable();
                if (target is IInteractionPrompt prompt)
                {
                    var line = prompt.PromptFor(_interactor.gameObject);
                    if (!string.IsNullOrEmpty(line)) text = "[" + _glyphs.For("Interact") + "] " + line;
                }
            }

            if (text != CurrentInteractionPrompt)
            {
                CurrentInteractionPrompt = text;
                _interactText.text = text;
            }
        }

        private void OnDepthEntered(ExpeditionState state)
        {
            // The merchant and the cache of the depth left behind are gone with their rooms: close their windows and
            // forget the bindings, and clear the room-title reveal so it never carries over into the new depth.
            if (_merchant != null && _merchant.IsOpen) _merchant.Close();
            _openMerchant = null;
            if (_weaponCache != null && _weaponCache.IsOpen) _weaponCache.Close();
            _openCache = null;
            CurrentRoom = null;
            _revealedRoom = null;
            _signals.Clear();
            _heldNotice = null;
            HudView?.Notice?.Clear();
            HudView?.RoomTitle?.Clear();
            HudView?.Vignette?.Reset();
            CloseVotePanel();
            BuildDepth();
            // The one place the new-depth heal runs (dungeon/60): after the next depth exists, every living participant
            // starts it at their own effective maximum. A room entry, a revisit, the Transit vote, a rebuild inside the
            // same depth, an equipment change or a menu never reaches this callback, so the heal cannot repeat.
            // A client's members are healed by the host (its own heal would be a second one, 82).
            if (Mode != CoopRunMode.Client && _expedition != null && _expedition.IsExpeditionActive && state != null && state.Depth > 1)
            {
                LastDepthArrivalHeals = DepthArrivalHeal.ApplyToParty(_roster);
                DepthArrivalHeals++;
            }
        }

        /// <summary>How often the new-depth heal ran (exactly once per successful Descend) and what it did last time.</summary>
        public int DepthArrivalHeals { get; private set; }
        public IReadOnlyList<DepthArrivalHeal.Outcome> LastDepthArrivalHeals { get; private set; } = System.Array.Empty<DepthArrivalHeal.Outcome>();

        private void OnTransitOpened(TransitDecision decision)
        {
            var vote = new TransitVoteViewModel(decision, _expedition.State.TransactionId);
            _vote = vote;
            _voteList = ScreenNavigation.TransitVote(_vote);
            if (_voteView != null) Destroy(_voteView.gameObject);
            // The decision as a panel at the top of the screen (ui/91). Its buttons activate the decision's existing
            // focus list, so the vote, its authority and its outcome are unchanged; the world keeps running (the Boss
            // Cache is still to be opened) and the panel only owns input when the player takes it (F / D-pad up, or
            // the pointer on it). Factual context only (Phase 5): no recommendation, the choice stays the player's.
            _voteView = TransitDecisionView.Create(_canvas.transform, vote, _voteList, TransitPanelContext);
            _voteView.transform.SetSiblingIndex(_promptText.transform.GetSiblingIndex() + 1); // under the pause / Run Lost screens
            // A choice made (or the Return confirmation asked) hands focus back to the game; a resolution closes the panel.
            vote.Changed += _ => { if (_vote == vote) OnVoteChanged(vote); };
            _promptText.enabled = false; // the panel explains itself; the tutorial line would sit under it
            _tutorial.ObserveTransitOpened(decision);
        }

        /// <summary>The panel's two context lines: where the party is and where descending leads; the facts at stake.</summary>
        private (string depth, string detail) TransitPanelContext()
        {
            var state = _expedition?.State;
            if (state == null) return (string.Empty, string.Empty);
            var best = _expedition.DeepestDepthReached > 0 ? $"BEST DEPTH {_expedition.DeepestDepthReached}" : "NO BEST YET";
            var detail = $"{best}  ·  {state.CarriedCoins} C AT RISK";
            var bonus = TransitContext.Lines(_expedition).Skip(4).FirstOrDefault(); // the live deep-depth bonus, when in force
            if (!string.IsNullOrEmpty(bonus)) detail += "  ·  " + bonus.ToUpperInvariant();
            return ($"DEPTH {state.Depth}  >  {state.Depth + 1}", detail);
        }

        private void OnVoteChanged(TransitVoteViewModel vote)
        {
            if (vote.IsResolved) { CloseVotePanel(keepVote: true); return; }
            if (vote.AwaitingReturnConfirmation) { EngageVotePanel(); return; } // the warning needs an answer
            if (_voteEngaged && vote.LocalVote.HasValue) RequestVoteRelease();
        }

        private int _voteReleaseFrame = -1;

        /// <summary>
        /// Focus goes back to the game on the NEXT frame: the press that confirmed or backed out (Space / pad A / pad B)
        /// is still "pressed this frame" and would otherwise also reach gameplay as a dash or an interaction.
        /// </summary>
        private void RequestVoteRelease()
        {
            if (_voteEngaged) _voteReleaseFrame = Time.frameCount + 1;
        }

        /// <summary>F / D-pad up (or the Return warning): the panel takes keyboard/controller focus and gameplay input is held.</summary>
        private void EngageVotePanel()
        {
            if (_voteView == null || !_voteView.IsVisible || _voteEngaged || _vote == null || !_vote.HasVoteControls) return;
            _voteEngaged = true;
            _voteReleaseFrame = -1;
            GameplayInputGate.Hold();
            if (!_menuInput.Stack.Contains(_voteList)) _menuInput.Stack.Push(_voteList);
            _voteView.SetEngaged(true);
        }

        private void ReleaseVotePanel()
        {
            _voteReleaseFrame = -1;
            if (!_voteEngaged) return;
            _voteEngaged = false;
            GameplayInputGate.Release();
            _menuInput?.Stack.Remove(_voteList);
            _voteView?.SetEngaged(false);
        }

        /// <summary>The pointer on the panel: gameplay input is held (a click never also fires) and the pointer cursor shows.</summary>
        private void SyncVoteHover()
        {
            var over = _voteView != null && _voteView.IsPointerOver && !_pause.IsOpen;
            if (over == _voteHoverHeld) return;
            _voteHoverHeld = over;
            if (over) { GameplayInputGate.Hold(); CursorService.PushOverlay(); }
            else { GameplayInputGate.Release(); CursorService.PopOverlay(); }
        }

        /// <summary>Resolution or a new depth: the panel goes and every hold it took is returned.</summary>
        private void CloseVotePanel(bool keepVote = false)
        {
            ReleaseVotePanel();
            if (_voteHoverHeld) { _voteHoverHeld = false; GameplayInputGate.Release(); CursorService.PopOverlay(); }
            if (_voteView != null) { _voteView.Hide(); Destroy(_voteView.gameObject); _voteView = null; }
            if (_promptText != null) _promptText.enabled = !(_pauseOverlay || _inventoryOverlay || _merchantOverlay || _weaponCacheOverlay || _runFailedOverlay);
            if (keepVote) return;
            _vote?.Dispose();
            _vote = null;
        }

        /// <summary>The Transit panel as seen by tests: visible, engaged, and its buttons.</summary>
        public TransitDecisionView TransitDecisionView => _voteView;
        public bool TransitPanelEngaged => _voteEngaged;

        private bool _pauseOverlay;
        private bool _inventoryOverlay;
        private bool _merchantOverlay;
        private bool _weaponCacheOverlay;
        private bool _runFailedOverlay;

        private void RefreshPauseUi()
        {
            SyncOverlay(ref _pauseOverlay, _pause.IsOpen);
        }

        private void RefreshRunFailedUi()
        {
            SyncOverlay(ref _runFailedOverlay, _runFailed.IsOpen && !_runFailed.IsResolved);
        }

        private void RefreshInventoryUi()
        {
            SyncOverlay(ref _inventoryOverlay, _inventory.IsOpen);
            // The inventory's focus list owns keyboard/controller navigation while the window is up, exactly like a pause panel.
            if (_menuInput == null || InventoryView == null) return;
            var list = InventoryView.FocusList;
            if (_inventory.IsOpen) { if (!_menuInput.Stack.Contains(list)) _menuInput.Stack.Push(list); }
            else _menuInput.Stack.Remove(list);
        }

        private void RefreshMerchantUi()
        {
            SyncOverlay(ref _merchantOverlay, _merchant.IsOpen);
            if (_menuInput == null || MerchantView == null) return;
            var list = MerchantView.FocusList;
            if (_merchant.IsOpen) { if (!_menuInput.Stack.Contains(list)) _menuInput.Stack.Push(list); }
            else _menuInput.Stack.Remove(list);
        }

        private void RefreshWeaponCacheUi()
        {
            SyncOverlay(ref _weaponCacheOverlay, _weaponCache.IsOpen);
            if (_menuInput == null || WeaponCacheView == null) return;
            var list = WeaponCacheView.FocusList;
            if (_weaponCache.IsOpen) { if (!_menuInput.Stack.Contains(list)) _menuInput.Stack.Push(list); }
            else _menuInput.Stack.Remove(list);
        }

        /// <summary>A menu layer over gameplay owns the pointer cursor while it is open; the aim cursor returns when the last one closes.</summary>
        private void SyncOverlay(ref bool tracked, bool open)
        {
            if (tracked == open) return;
            tracked = open;
            if (open) CursorService.PushOverlay(); else CursorService.PopOverlay();
            // While any window owns the screen the low-HP frame stands down (it must never tint a menu), and the
            // contextual tutorial line hides too (it shares the run canvas and would otherwise draw through a panel).
            var anyOverlay = _pauseOverlay || _inventoryOverlay || _merchantOverlay || _weaponCacheOverlay || _runFailedOverlay;
            _hud?.SetOverlayOpen(anyOverlay);
            if (_promptText != null) _promptText.enabled = !anyOverlay;
        }

        private void Update()
        {
            if (_voteReleaseFrame >= 0 && Time.frameCount >= _voteReleaseFrame) ReleaseVotePanel();
            if (_voteView != null && _vote != null && !_vote.IsResolved)
            {
                // Taking the decision: F or D-pad up — keys no gameplay action uses (1/2 are the weapon slots, Space
                // dashes, E / A interact), and only while no menu owns the screen.
                var kb = UnityEngine.InputSystem.Keyboard.current;
                var pad = UnityEngine.InputSystem.Gamepad.current;
                var take = (kb != null && kb.fKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame);
                if (take && !_voteEngaged && !(_pause.IsOpen || _inventory.IsOpen || _merchant.IsOpen || _weaponCache.IsOpen || _runFailed.IsOpen))
                {
                    if (pad != null && pad.dpad.up.wasPressedThisFrame) RuinRail.Core.Input.ActiveInputDevice.Set(RuinRail.Core.Input.InputDeviceKind.Gamepad);
                    EngageVotePanel();
                }

                SyncVoteHover();
            }

            _tutorial?.Tick();
            RefreshInteractionPrompt();
            RefreshHeldNotice();
            TickCoop(Time.deltaTime);
        }

        /// <summary>
        /// Pause → RETURN TO MAIN MENU, confirmed. An active expedition ends through the one failure transaction
        /// (85 Solo Quit: carried loot and coins are lost, XP commits, the run is not resumable); the expedition-ended
        /// handler then saves and, because of the flag, leaves the session for the Main Menu rather than the Shelter.
        /// Nothing about the loss is decided here.
        /// </summary>
        public void ReturnToMainMenu()
        {
            if (LeavingToMainMenu) return;
            LeavingToMainMenu = true;
            if (_expedition != null && _expedition.IsExpeditionActive)
            {
                _expedition.Fail();
                return;
            }

            LeaveForMainMenu();
        }

        private void LeaveForMainMenu()
        {
            _app.Audio.StopAllLoops();
            _app.Menu.Session?.SaveNow("return_to_menu");
            var controller = _app.Network != null ? _app.Network.Controller : null;
            if (controller != null && !controller.IsOffline) _ = controller.LeaveAsync();
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
        }

        private void OnExpeditionEnded(ExpeditionSummary summary)
        {
            if (_ended) return;
            _ended = true;
            // No danger frame and no room banner survive the end of the run.
            HudView?.Vignette?.Reset();
            HudView?.RoomTitle?.Clear();
            _app.Audio.StopAllLoops();
            _app.Menu.Session?.SaveNow("expedition_end");
            // The party's run is over on this peer; the next start (host) or the next received start (client) opens a new one.
            _app.Coop?.ClearRun();
            if (LeavingToMainMenu) { LeaveForMainMenu(); return; }
            // A conclusive failure of the party (solo death / co-op wipe) shows the Run Lost screen over the closed
            // transaction and waits for RETURN TO SHELTER / MAIN MENU; every other end hands back to the Shelter as before.
            if (IsConclusiveRunLoss(summary) && _runFailed != null && _runFailed.Show(summary)) return;
            _app.LoadScene(SceneNames.Base);
        }

        /// <summary>The run failed and the party is wiped (or the local player is dead): the loss is final, not a downed-and-revivable state.</summary>
        private bool IsConclusiveRunLoss(ExpeditionSummary summary)
        {
            if (summary == null || summary.IsSuccess) return false;
            var local = _rig?.Player != null ? _rig.Player.GetComponent<PlayerLifeStateComponent>() : null;
            return (_roster != null && _roster.IsWiped) || (local != null && local.IsDead);
        }

        /// <summary>RETURN TO SHELTER on the Run Lost screen: the transaction is already closed and saved; only the scene changes.</summary>
        private void LeaveForShelterAfterRunLost()
        {
            _app.LoadScene(SceneNames.Base);
        }

        private void OnDestroy()
        {
            if (_expedition != null)
            {
                _expedition.DepthEntered -= OnDepthEntered;
                _expedition.TransitOpened -= OnTransitOpened;
                _expedition.ExpeditionEnded -= OnExpeditionEnded;
                if (_expedition.State?.Inventory != null) _expedition.State.Inventory.EquippedChanged -= OnEquippedChangedForAmmo;
            }

            if (_services?.GroundLoot != null) _services.GroundLoot.PickupTracked -= OnPickupTracked;

            _vote?.Dispose();
            DisposeCoop();
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            if (_rig?.Reader is IDisposable reader) reader.Dispose();

            // Per-run process globals get explicit reset ownership here rather than being left pointing at a torn-down
            // run's content: the next expedition (or the menu) must never read this one's resolvers, and a client
            // process must not stay damage-authoritative after leaving a session (82).
            RoomDoorLock.SkinResolver = null;
            WorldObjectArt.Resolver = null;
            PickupArbiter.Clear();
            DamageAuthority.LocalIsAuthoritative = true;
        }
    }
}
