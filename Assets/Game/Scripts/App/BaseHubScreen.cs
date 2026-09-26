using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;
using RuinRail.UI.Onboarding;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// The Shelter (94): the hub the player operates their base from.
    ///
    /// Composed as a front-end with four zones — identity and profile across the top, the station tabs under it, the
    /// Shelter itself behind the content, and three content columns over that: what the survivor has on the left, the
    /// open station in the middle, and the state of the next expedition on the right. The station panel is the only
    /// part that changes between tabs; the rest is the room the player is standing in and stays put, which is what
    /// makes the screen read as a place rather than as a tool window.
    ///
    /// Every number on it comes from a view model that already owns it. TRANSIT still starts the run and LEAVE still
    /// saves and returns to the menu; none of the hub's rules changed with its presentation.
    /// </summary>
    public sealed class BaseHubScreen : MonoBehaviour
    {
        private GameApp _app;
        private BaseHubViewModel _hub;
        private ShelterOnboardingViewModel _onboarding;
        private DisplayNameEntry _nameEntry;
        private DisplayNameEntryView _nameEntryView;
        private TerminalViewModel _terminal;
        private SessionPartyBridge _partyBridge;
        private SessionRoster _approvalRoster;
        private MenuInput _input;
        private RectTransform _root;
        private FocusList _stationList;
        private FocusList _panelList;
        private GameObject _panel;
        /// <summary>The open station's data column (state rows), rebuilt when that state changes.</summary>
        private GameObject _panelData;
        private BaseStation? _panelStation;
        private Text _panelDescription;
        private string _panelDescriptionDefault = string.Empty;
        private int _panelDescriptionWidth;
        private UiRect _panelDataRegion;
        private Text _feedback;
        private Text _footer;
        private Text _onboardingText;
        private string _selectedInstance;
        private readonly UiPrompts _prompts = new();
        private readonly List<UiControl> _controls = new();
        private readonly List<UiControl> _panelControls = new();

        // The Trader counter's graphical rows and details pane (the merchant's primitives, reused verbatim).
        private readonly List<MerchantRowView> _traderRows = new();
        private readonly List<Text> _traderDetailLines = new();
        private readonly DetailPager _traderDetails = new(TraderDetailLines);
        private Text _traderDetailTitle;
        private Text _traderDetailSubtitle;
        private string _traderDetailKey;
        private FocusWindow _traderWindow;

        // Header and left-column labels are rebuilt in place rather than recreated, so nothing can accumulate.
        private Text _profileName;
        private Text _profileStats;
        private Text _survivorName;
        private Text _levelBadge;
        private Image _xpFill;
        private Text _depthLabel;
        private Text _pointsBadge;
        private Text _coinsLabel;
        private Text _storageLabel;
        private Image _storageFill;
        private readonly List<Image> _partyPips = new();
        private Text _partySummary;
        private GameObject _onboardingCard;
        private Image _expeditionPlate;
        private readonly List<Image> _readyPips = new();
        private Text _expeditionState;
        private Text _expeditionHint;

        public BaseHubViewModel Hub => _hub;
        /// <summary>The Multiplayer Terminal view model the MULTIPLAYER station shows (READY lives here).</summary>
        public TerminalViewModel Terminal => _terminal;
        public ShelterOnboardingViewModel Onboarding => _onboarding;
        /// <summary>The display-name field the Character station's CHANGE NAME opens.</summary>
        public DisplayNameEntry NameEntry => _nameEntry;
        public DisplayNameEntryView NameEntryView => _nameEntryView;
        public MenuInput Input => _input;
        public FocusList StationList => _stationList;
        public FocusList PanelList => _panelList;
        public BaseSession Session => _app.Menu.Session;
        /// <summary>Every control currently on the screen: tab bar plus whatever the open station contributes.</summary>
        public IReadOnlyList<UiControl> Controls => _controls.Concat(_panelControls).Where(c => c != null).ToList();

        public static BaseHubScreen Create(GameApp app)
        {
            var canvas = UiKit.Canvas("BaseHubScreen");
            var screen = canvas.gameObject.AddComponent<BaseHubScreen>();
            screen.Build(app);
            return screen;
        }

        private void Build(GameApp app)
        {
            _app = app;
            if (app.Menu.Session == null) app.Menu.Play();
            var session = app.Menu.Session;
            if (session == null) { app.LoadScene(SceneNames.MainMenu); return; }

            // The session roster names the party (the local line included), so the terminal shows the saved profile name
            // rather than the participant id the lobby captured before the name was chosen; a rename updates it.
            _approvalRoster = new SessionRoster(app.Content.DisplayNamePolicy);
            _approvalRoster.Add(BaseSession.LocalClientId, session.Profile.DisplayName, true);
            _terminal = new TerminalViewModel(new MultiplayerTerminalService(app.Network.Controller), session.Lobby, BaseSession.LocalClientId, _approvalRoster)
            {
                // READY in the terminal: a player with nothing equipped gets the free Starter Loadout first (base/75).
                PrepareLoadout = session.EnsureStarterLoadoutIfEmpty,
                // Host: a joined member is shown by the name it reported with its Ready/loadout (the same name the lobby
                // state and the run start carry), sanitized through the profile rules before it is drawn.
                MemberName = clientId => clientId != BaseSession.LocalClientId && app.Coop != null
                    && app.Coop.MemberProfiles.TryGetValue(clientId, out var member) && !string.IsNullOrEmpty(member.DisplayName)
                        ? _approvalRoster.Sanitize(member.DisplayName, clientId)
                        : null
            };
            _hub = new BaseHubViewModel(session, new MultiplayerTerminalService(app.Network.Controller), app.NextRunSeed);
            // 81: a player who joins the hosted session becomes a party member here, so READY/START and the expedition's
            // start snapshot describe the real party. Without this the terminal could show a full lobby while the run
            // still started for one participant.
            if (app.Network?.Driver is NgoNetworkDriver ngo)
            {
                ngo.ConfigureApproval(_approvalRoster);
                ngo.SetConnectionPayload(session.Profile.DisplayName);
                _partyBridge = new SessionPartyBridge(ngo.Connections, session.Lobby,
                    clientId => _approvalRoster.Get(clientId)?.DisplayName ?? $"Player {clientId + 1}");
            }

            // 81/82 co-op: a joined client's Ready/loadout reaches the host's lobby, and the host's start starts this
            // peer's own expedition (never a seed of its own). Each role only acts while this process has that role.
            if (app.Coop != null)
            {
                app.Coop.BindHost(session.Lobby, session.Expedition, () => session.Profile.DisplayName);
                app.Coop.BindClient(session.Lobby, BaseSession.LocalClientId, () => new LobbyMemberMessage
                {
                    DisplayName = session.Profile.DisplayName,
                    Loadout = session.Loadout.ToSnapshot(),
                    SkillRanks = CoopMemberProfile.RanksOf(session.Profile.Skills)
                }, session.Expedition, session.Profile);
                app.Coop.RunStartReceived += OnCoopRunStart;
                // A joined client cannot start an expedition of its own: only the host starts the party (81).
                _hub.Transit.StartGate = () => app.Coop.IsClient ? "Only the host can start." : null;
            }
            _onboarding = new ShelterOnboardingViewModel(session, app.Content.DisplayNamePolicy);
            _onboarding.DisplayNameChanged += OnDisplayNameChanged;
            // A live session fixed every member's name when it connected (the host sanitized it into its roster), so
            // the name is changed only while offline: the next host/join then carries the new one.
            _nameEntry = new DisplayNameEntry(_onboarding,
                () => app.Network?.Driver != null && app.Network.Driver.IsListening ? "Leave the party to change your name." : null);
            app.SettingsScreen.SetTutorialProgress(new SaveSlotTutorialProgress(session.Slot, session.Autosave, () => app.Settings.Current.Tutorial.ShowPrompts));
            app.MusicBinder.Attach(session.Progression);

            _input = gameObject.AddComponent<MenuInput>();
            _input.Back += OnBack;
            _input.Horizontal += StepSection;
            _input.InputBlocked = () => app.InputBlocked || (_nameEntryView != null && _nameEntryView.OwnsInput);

            _root = UiKit.ReferenceRoot(transform);
            UiKit.Backdrop(_root, UiSkin.Load()?.ShelterBackdrop);

            BuildHeader();
            BuildTabs();
            BuildLeftColumn();
            BuildRightColumn();
            BuildFooter();
            _nameEntryView = gameObject.AddComponent<DisplayNameEntryView>();
            _nameEntryView.Bind(_nameEntry, _root);

            _input.Stack.Push(_stationList);
            _hub.StationChanged += OnStationChanged;
            _terminal.Changed += OnTerminalChanged;
            // A skill purchase or a respec changes the Character panel's whole state (rank, effect, next-rank preview,
            // remaining points). The mouse path already refreshed the panel from the control it clicked; keyboard and
            // controller activation go straight through the focus stack, so without this the panel kept showing the
            // ranks it was built with until the player left and re-entered the station.
            session.Character.Changed += OnCharacterSheetChanged;
            _hub.SummaryReady += OnSummary;
            _onboarding.Changed += RefreshTexts;
            session.Expedition.ExpeditionStarted += OnExpeditionStarted;
            foreach (var panel in new[] { _hub.Storage.Feedback, _hub.Loadout.Feedback, _hub.Trader.Feedback, _hub.Character.Feedback, _hub.Workshop.Feedback, _hub.Transit.Feedback })
                panel.Changed += OnFeedback;

            // The hub opens with no station selected, and StationChanged only fires on a change — so the middle
            // column is composed once here rather than being left as a hole until the player picks something.
            OnStationChanged(_hub.Current);
            RefreshTexts();
        }

        // ---------------- zone 1: header ----------------

        /// <summary>
        /// Identity on the left, profile on the right, both from <see cref="ScreenLayout.BuildHeader"/>.
        ///
        /// The layout measures the profile strings and reserves their width before the identity block is placed, which
        /// is the fix for the overlapping top-right text: the two blocks are laid out against each other rather than
        /// dropped at fixed coordinates and hoped for.
        /// </summary>
        private void BuildHeader()
        {
            UiKit.ChromeBar(_root, ScreenLayout.Header, "Header");
            var header = CurrentHeader();

            UiKit.Label(_root, header.Title, TextAnchor.UpperLeft, UiTheme.Ink);
            UiKit.Label(_root, header.Subtitle, TextAnchor.UpperLeft, UiTheme.Amber);

            _profileName = UiKit.Label(_root, header.ProfileName, TextAnchor.UpperRight, UiTheme.Ink);
            _profileStats = UiKit.Label(_root, header.ProfileStats, TextAnchor.UpperRight, UiTheme.InkMuted);

            // A hairline between the identity and the profile, so the two blocks read as separate fields.
            UiKit.Plate(_root, new UiRect(header.ProfileBlock.X - 6, 5, 1, UiTheme.HeaderHeight - 11), UiTheme.PanelEdge, "HeaderDivider");
        }

        private ScreenLayout.HeaderLayout CurrentHeader()
        {
            var session = Session;
            return ScreenLayout.BuildHeader(
                "RUINRAIL", "THE SHELTER",
                session?.Profile.DisplayName ?? string.Empty,
                session?.Progression.Level ?? 0,
                session?.Banked.Balance ?? 0);
        }

        // ---------------- zone 2: primary navigation ----------------

        private void BuildTabs()
        {
            UiKit.ChromeBar(_root, ScreenLayout.TabBar, "TabBar");
            _stationList = ScreenNavigation.BaseHub(_hub);

            var contentTabs = BaseHubViewModel.Stations
                .Select(s => ("station." + s, BaseHubViewModel.Label(s)))
                .ToList();
            var slots = ScreenLayout.BuildTabs(contentTabs, ("station.close", "LEAVE"));

            foreach (var slot in slots)
            {
                var item = _stationList.Find(slot.Id);
                if (item == null) continue;

                var station = StationOf(slot.Id);
                var role = slot.IsExit ? ControlRole.Exit
                    : station == BaseStation.Transit ? ControlRole.Primary
                    : ControlRole.Tab;

                var control = UiKit.Control(_root, _stationList, item, slot.Bounds, role,
                    i => { _stationList.Focus(i.Id); _stationList.ActivateFocused(); },
                    // The selected tab is whichever station the hub says is open, not whatever was clicked last.
                    isActive: station.HasValue ? () => _hub.Current == station : () => false,
                    labelAnchor: TextAnchor.MiddleCenter,
                    labelOverride: slot.Label);
                _controls.Add(control);

                // Loot brought home and not yet looked at: a pip on the STORAGE tab points at the stash.
                if (station == BaseStation.Storage)
                {
                    var b = slot.Bounds;
                    _storageTabPip = UiKit.Plate(_root, new UiRect(b.Right - 7, b.Y + 2, 5, 5), UiTheme.Terminal, "LootPip");
                    _storageTabPip.enabled = false;
                }
            }
        }

        // ---------------- the stash (Storage station) ----------------

        private StashViewModel _stash;
        private RuinRail.UI.Inventory.StashView _stashView;
        private Image _storageTabPip;

        public StashViewModel Stash => _stash;
        public RuinRail.UI.Inventory.StashView StashView => _stashView;
        public bool StashOpen => _stashView != null;
        /// <summary>The post-run pointer at Storage is showing (tab pip; the NEXT card says why).</summary>
        public bool LootCueVisible => _storageTabPip != null && _storageTabPip.enabled;
        public string NextCardText => _onboardingCard != null && _onboardingCard.activeSelf ? _onboardingText.text : string.Empty;

        /// <summary>OPEN STASH: the graphical survivor ↔ Storage window over the Shelter; Back / CLOSE returns to the station.</summary>
        public void OpenStash()
        {
            if (StashOpen || Session == null) return;
            _stash = new StashViewModel(Session, _hub.Storage, _hub.Loadout);
            _stashView = RuinRail.UI.Inventory.StashView.Create(_stash, CloseStash);
            _input.Stack.Push(_stashView.FocusList);
            var summary = Session.Expedition.LastSummary;
            if (summary != null) Session.StashAcknowledgedTransaction = summary.TransactionId;
            RefreshTexts();
        }

        public void CloseStash()
        {
            if (!StashOpen) return;
            _input.Stack.Remove(_stashView.FocusList);
            Destroy(_stashView.gameObject);
            _stashView = null;
            _stash.Dispose();
            _stash = null;
            RefreshStationData();
            RefreshTexts();
        }

        /// <summary>STORE WHOLE BACKPACK from the station itself: the same stash rule, reported in the footer.</summary>
        public void StoreWholeBackpack()
        {
            using var stash = new StashViewModel(Session, _hub.Storage, _hub.Loadout);
            stash.StoreBackpack();
            if (stash.MessageIsError) _hub.Storage.Feedback.Error(stash.Message); else _hub.Storage.Feedback.Ok(stash.Message);
            RefreshStationData();
            RefreshTexts();
        }

        private static BaseStation? StationOf(string focusId)
        {
            if (focusId == null || !focusId.StartsWith("station.", StringComparison.Ordinal)) return null;
            var name = focusId.Substring("station.".Length);
            return Enum.TryParse<BaseStation>(name, out var station) ? station : null;
        }

        /// <summary>
        /// Left/right steps the tab bar, from anywhere on the screen.
        ///
        /// Deliberately not gated on the tab bar owning the focus. Once a station is open its panel owns the vertical
        /// focus, and if horizontal steps were gated the same way the tab bar would go keyboard-inert exactly when the
        /// player is moving between sections — they would have to press Back first every time. So a horizontal step
        /// switches section directly when one is open, which is what a tab bar is for.
        ///
        /// Stepping onto LEAVE only moves the focus there. Leaving the Shelter stays a deliberate confirm, never
        /// something a stray arrow key can do.
        /// </summary>
        public void StepSection(int delta)
        {
            if (!_stationList.Move(delta)) return;

            var station = StationOf(_stationList.Focused?.Id);
            if (station.HasValue && _hub.Current != null && _hub.Current != station) _hub.Open(station.Value);
        }

        // ---------------- zone 4a: the survivor card ----------------

        // The side columns are compact cards, not full-height text panels: the Shelter behind them stays the picture,
        // and help text appears only while the first-launch steps are still open.
        private const int BadgeWidth = 34;

        /// <summary>
        /// Who the survivor is and what they own, as shapes rather than a key/value list: name and level badge, an XP
        /// bar, depth and unspent points, banked coins, a storage bar and one lit pip per party member.
        /// </summary>
        private void BuildLeftColumn()
        {
            var column = ScreenLayout.LeftColumn;
            var inner = column.Inset(UiTheme.Pad);
            const int cardHeight = 106;
            UiKit.Panel(_root, new UiRect(column.X, column.Y, column.Width, cardHeight), "SurvivorPanel");
            var y = inner.Y;

            // Name and level badge.
            _survivorName = UiKit.Label(_root, string.Empty, new UiRect(inner.X, y, inner.Width - BadgeWidth - 4, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            UiKit.Plate(_root, new UiRect(inner.Right - BadgeWidth, y - 1, BadgeWidth, UiText.Height() + 2), UiTheme.AmberDim, "LevelBadge");
            _levelBadge = UiKit.Label(_root, string.Empty, new UiRect(inner.Right - BadgeWidth, y, BadgeWidth, UiText.Height()), 1, TextAnchor.UpperCenter, UiTheme.Ink);
            _levelBadge.alignment = TextAnchor.UpperCenter;
            y += UiText.LineHeight + 3;

            // XP toward the next level.
            _xpFill = Bar(_root, new UiRect(inner.X, y, inner.Width, 3), UiTheme.Amber);
            y += 7;

            // Deepest depth, and unspent points as an amber tag only when there are any.
            _depthLabel = UiKit.Label(_root, string.Empty, new UiRect(inner.X, y, 80, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            _pointsBadge = UiKit.Label(_root, string.Empty, new UiRect(inner.Right - 54, y, 54, UiText.Height()), 1, TextAnchor.UpperRight, UiTheme.Amber);
            _pointsBadge.alignment = TextAnchor.UpperRight;
            y += UiText.LineHeight + 4;
            UiKit.Plate(_root, new UiRect(inner.X, y, inner.Width, 1), UiTheme.PanelEdgeSoft, "Rule");
            y += 5;

            // Banked coins.
            var coin = UiSkin.Load()?.CoinIcon;
            var coinX = inner.X;
            if (coin != null)
            {
                var icon = UiKit.Plate(_root, new UiRect(inner.X, y, 8, 8), Color.white, "CoinIcon");
                icon.sprite = coin;
                icon.preserveAspect = true;
                coinX += 12;
            }

            _coinsLabel = UiKit.Label(_root, string.Empty, new UiRect(coinX, y, inner.Right - coinX, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            y += UiText.LineHeight + 3;

            // Storage use as a bar.
            UiKit.Label(_root, "STORAGE", new UiRect(inner.X, y, 50, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            _storageLabel = UiKit.Label(_root, string.Empty, new UiRect(inner.X + 50, y, inner.Width - 50, UiText.Height()), 1, TextAnchor.UpperRight, UiTheme.Ink);
            _storageLabel.alignment = TextAnchor.UpperRight;
            y += UiText.LineHeight + 1;
            _storageFill = Bar(_root, new UiRect(inner.X, y, inner.Width, 3), UiTheme.InkMuted);
            y += 8;

            // Party: one pip per slot, lit per member, green when that member is Ready.
            UiKit.Label(_root, "PARTY", new UiRect(inner.X, y, 40, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            _partyPips.Clear();
            for (var i = 0; i < RuinRail.Networking.SessionRequest.MaxPartySize; i++)
                _partyPips.Add(UiKit.Plate(_root, new UiRect(inner.X + 44 + i * 10, y + 1, 7, 7), UiTheme.PanelEdgeSoft, "PartyPip"));
            _partySummary = UiKit.Label(_root, string.Empty, new UiRect(inner.X + 78, y, inner.Width - 78, UiText.Height()), 1, TextAnchor.UpperRight, UiTheme.InkMuted);
            _partySummary.alignment = TextAnchor.UpperRight;

            // First-launch guidance: an amber card under the survivor card, only while a step is still open.
            var callout = new UiRect(column.X, column.Y + cardHeight + 6, column.Width, 62);
            _onboardingCard = UiKit.Panel(_root, callout, "OnboardingCard", UiTheme.WithAlpha(UiTheme.NearBlack, 0.85f), UiTheme.AmberDim);
            var calloutInner = callout.Inset(UiTheme.PadSmall + 2);
            UiKit.Label(_onboardingCard.transform, "NEXT", new UiRect(UiTheme.PadSmall + 2, UiTheme.PadSmall + 2, calloutInner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            _onboardingText = UiKit.Label(_onboardingCard.transform, string.Empty,
                new UiRect(UiTheme.PadSmall + 2, UiTheme.PadSmall + 2 + UiText.LineHeight + 2, calloutInner.Width, UiText.Height(4)), 1,
                TextAnchor.UpperLeft, UiTheme.Ink, wrap: true);
        }

        /// <summary>A thin progress bar: a dark track and a fill whose width follows <see cref="SetBar"/>.</summary>
        private static Image Bar(Transform parent, UiRect bounds, Color color)
        {
            UiKit.Plate(parent, bounds, UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "BarTrack");
            var fill = UiKit.Plate(parent, bounds, color, "BarFill");
            fill.rectTransform.pivot = new Vector2(0f, 1f);
            return fill;
        }

        private static void SetBar(Image fill, float fraction, float fullWidth)
        {
            if (fill == null) return;
            var size = fill.rectTransform.sizeDelta;
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Round(Mathf.Clamp01(fraction) * fullWidth), size.y);
        }

        private int SectionHeading(Transform parent, UiRect inner, int y, string text)
        {
            UiKit.Label(parent, text, new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(parent, new UiRect(inner.X, y + UiText.Height() + 1, inner.Width, 1), UiTheme.AmberDim, "HeadingRule");
            return y + UiText.LineHeight + 3;
        }

        // ---------------- zone 4b: the expedition card ----------------

        /// <summary>
        /// The next expedition at a glance: a READY / HELD plate, one pip per party member lit when Ready, a one-line
        /// next step, and what a failed run costs as two short lines (lost / kept) instead of a paragraph.
        ///
        /// It carries no controls of its own on purpose: TRANSIT is the primary action in the tab bar.
        /// </summary>
        private void BuildRightColumn()
        {
            var column = ScreenLayout.RightColumn;
            const int cardHeight = 146;
            UiKit.Panel(_root, new UiRect(column.X, column.Y, column.Width, cardHeight), "ExpeditionPanel");
            var inner = column.Inset(UiTheme.Pad);
            var y = inner.Y;

            UiKit.Label(_root, "EXPEDITION", new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            y += UiText.LineHeight + 2;

            _expeditionPlate = UiKit.Plate(_root, new UiRect(inner.X, y, inner.Width, UiText.Height(1, 2) + 8), UiTheme.AmberDim, "ExpeditionPlate");
            _expeditionState = UiKit.Label(_root, string.Empty, new UiRect(inner.X, y + 4, inner.Width, UiText.Height(1, 2)), 2, TextAnchor.UpperCenter, UiTheme.Ink);
            _expeditionState.alignment = TextAnchor.UpperCenter;
            y += UiText.Height(1, 2) + 12;

            UiKit.Label(_root, "READY", new UiRect(inner.X, y, 36, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            _readyPips.Clear();
            for (var i = 0; i < RuinRail.Networking.SessionRequest.MaxPartySize; i++)
                _readyPips.Add(UiKit.Plate(_root, new UiRect(inner.X + 40 + i * 10, y + 1, 7, 7), UiTheme.PanelEdgeSoft, "ReadyPip"));
            y += UiText.LineHeight + 2;

            _expeditionHint = UiKit.Label(_root, string.Empty, new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            y += UiText.LineHeight + 4;
            UiKit.Plate(_root, new UiRect(inner.X, y, inner.Width, 1), UiTheme.PanelEdgeSoft, "Rule");
            y += 5;

            // What a failed run costs: two marked lines instead of a paragraph.
            RiskLine(inner, ref y, UiTheme.Danger, "LOST ON FAIL", "gear, backpack");
            RiskLine(inner, ref y, UiTheme.Terminal, "ALWAYS KEPT", "XP, coins, storage");
        }

        private void RiskLine(UiRect inner, ref int y, Color tone, string heading, string detail)
        {
            UiKit.Plate(_root, new UiRect(inner.X, y + 1, 5, 5), tone, "RiskMarker");
            UiKit.Label(_root, heading, new UiRect(inner.X + 9, y, inner.Width - 9, UiText.Height()), 1, TextAnchor.UpperLeft, tone);
            y += UiText.LineHeight;
            UiKit.Label(_root, detail, new UiRect(inner.X + 9, y, inner.Width - 9, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            y += UiText.LineHeight + 2;
        }

        // ---------------- zone 4c: the station panel ----------------

        private void OnStationChanged(BaseStation? station)
        {
            if (StashOpen && station != BaseStation.Storage) CloseStash(); // the stash belongs to the Storage station
            if (_panel != null)
            {
                if (_panelList != null) _panelList.FocusChanged -= OnPanelFocusChanged;
                _panelDescription = null;
                _input.Stack.Remove(_panelList);
                Destroy(_panel);
                _panel = null;
                _panelData = null;
                _panelStation = null;
                _panelList = null;
                _panelControls.Clear();
                _traderRows.Clear();
                _traderDetailLines.Clear();
                _traderDetailTitle = null;
                _traderDetailSubtitle = null;
                _traderDetailKey = null;
                _traderWindow = null;
                _traderDetails.SetRows(null, false);
                _loadoutView = null;
                _coinAmount = null;
            }

            // Removing the old panel pops it off the focus stack, and popping restores the tab the panel was pushed
            // with. That restoration is exactly right for Back — it puts the cursor back where the player left it —
            // and exactly wrong when the player has just switched section, because it drags the tab cursor back to
            // the section they were leaving. So the tab bar is re-pointed at whatever is actually open, which keeps
            // the click, the confirm and the horizontal-step paths agreeing with the panel on screen.
            if (station.HasValue) _stationList.Focus("station." + station.Value);

            if (station == null)
            {
                BuildIdlePanel();
                RefreshTexts();
                return;
            }

            BuildStationPanel(station.Value);
            RefreshTexts();
        }

        /// <summary>
        /// With no station open the middle column steps out of the way so the Shelter is simply visible.
        ///
        /// It is not left as a blank black rectangle: the frame is gone entirely and only a short line of guidance
        /// sits at the bottom of the column, so the backdrop reads as the room it is.
        /// </summary>
        private void BuildIdlePanel()
        {
            var column = ScreenLayout.MainColumn;
            _panel = new GameObject("StationIdle");
            _panel.transform.SetParent(_root, false);
            var rect = _panel.AddComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(ScreenLayout.Width, ScreenLayout.Height);

            var strip = new UiRect(column.X, column.Bottom - 40, column.Width, 40);
            UiKit.Plate(_panel.transform, strip, UiTheme.WithAlpha(UiTheme.NearBlack, 0.72f), "IdleStrip");
            UiKit.Label(_panel.transform, "THE SHELTER",
                new UiRect(strip.X + UiTheme.Pad, strip.Y + 6, strip.Width - UiTheme.Pad * 2, UiText.Height()), 1,
                TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Label(_panel.transform, "Pick a station above. TRANSIT boards the Transit Car.",
                new UiRect(strip.X + UiTheme.Pad, strip.Y + 6 + UiText.LineHeight + 2, strip.Width - UiTheme.Pad * 2, UiText.Height(2)), 1,
                TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);
        }

        private void BuildStationPanel(BaseStation station)
        {
            var column = ScreenLayout.MainColumn;
            _panel = UiKit.Panel(_root, column, "StationPanel:" + station);
            var inner = new UiRect(UiTheme.Pad, UiTheme.Pad, column.Width - UiTheme.Pad * 2, column.Height - UiTheme.Pad * 2);

            var view = StationPresentation.For(station, _hub, _terminal);

            // Panel header: the station icon beside its name, then what the station is for.
            var icon = UiSkin.Load()?.StationIcon((int)station);
            var textX = inner.X;
            if (icon != null)
            {
                var image = UiKit.Plate(_panel.transform, new UiRect(inner.X, inner.Y - 3, 16, 16), Color.white, "StationIcon");
                image.sprite = icon;
                image.preserveAspect = true;
                textX = inner.X + 20;
            }

            UiKit.Label(_panel.transform, view.Title, new UiRect(textX, inner.Y, inner.Width - (textX - inner.X), UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.Ink);
            _panelDescriptionWidth = inner.Width - (textX - inner.X);
            _panelDescriptionDefault = view.Description;
            _panelDescription = UiKit.Label(_panel.transform, UiText.Fit(view.Description, _panelDescriptionWidth),
                new UiRect(textX, inner.Y + UiText.LineHeight, _panelDescriptionWidth, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.InkMuted);

            var bodyY = inner.Y + UiText.LineHeight * 2 + 4;
            UiKit.Plate(_panel.transform, new UiRect(inner.X, bodyY - 3, inner.Width, 1), UiTheme.PanelEdge, "HeaderRule");

            // Two columns: the station's controls on the left, the state it holds on the right. The Character Station
            // is the one station whose data side carries sentences (an attribute's effect and next-rank preview) while
            // its controls are only a name and a rank, so it gets the narrower control column and the wider data one.
            // The Trader is the one station whose controls ARE its data: an offer row has to carry the icon, rarity,
            // category and price, which does not fit a 147 px half column. It gets the full width for a merchant-style
            // list with the details pane under it instead of beside it.
            if (station == BaseStation.Trader)
            {
                BuildTraderPanel(new UiRect(inner.X, bodyY, inner.Width, inner.Bottom - bodyY));
                _panelStation = station;
                return;
            }

            // LOADOUT speaks the stash's graphical language over the full width: worn slots, the backpack grid and one
            // details strip, instead of a column of slot-name buttons beside a text list of the same slots.
            if (station == BaseStation.Loadout)
            {
                BuildLoadoutPanel(new UiRect(inner.X, bodyY, inner.Width, inner.Bottom - bodyY));
                _panelStation = station;
                return;
            }

            var gap = UiTheme.Pad;
            var controlWidth = station == BaseStation.Character
                ? Mathf.RoundToInt((inner.Width - gap) * 0.31f)
                : (inner.Width - gap) / 2;
            var controls = new UiRect(inner.X, bodyY, controlWidth, inner.Bottom - bodyY);
            var data = new UiRect(inner.X + controlWidth + gap, bodyY, inner.Width - controlWidth - gap, inner.Bottom - bodyY);

            UiKit.Plate(_panel.transform, new UiRect(data.X - gap / 2, bodyY, 1, data.Height), UiTheme.PanelEdgeSoft, "ColumnRule");

            BuildStationControls(station, controls);
            _panelStation = station;
            // The header's second line is contextual: it explains the focused control where that says more than the
            // station's generic purpose (an attribute's effect, what an upgrade buys), so no panel repeats it all.
            _panelList.FocusChanged += OnPanelFocusChanged;
            OnPanelFocusChanged(_panelList.Focused);
            _panelDataRegion = data;
            BuildStationData(view, data);
        }

        /// <summary>
        /// Re-reads the open station's state rows (READY / LOADOUT status, the terminal's status and notice, a trader's
        /// stock) so what a control just did is visible without leaving and reopening the station.
        /// </summary>
        private void RefreshStationData()
        {
            if (_panel == null || !_panelStation.HasValue || _hub == null) return;
            // The Trader has no key/value data column: its state lives on the offer rows and in the details pane.
            if (_panelStation.Value == BaseStation.Trader) { RenderTrader(); return; }
            if (_panelStation.Value == BaseStation.Loadout) return; // the loadout body redraws itself from its view model
            if (_panelStation.Value == BaseStation.Transit) RefreshCoinSelector();
            BuildStationData(StationPresentation.For(_panelStation.Value, _hub, _terminal), _panelDataRegion);
        }

        private void OnTerminalChanged(TerminalViewModel _) => RefreshStationData();

        private void OnPanelFocusChanged(FocusItem item)
        {
            if (_panelDescription == null) return;
            var context = ContextLine(item?.Id);
            _panelDescription.text = UiText.Fit(context ?? _panelDescriptionDefault, _panelDescriptionWidth);
            _panelDescription.color = context != null ? UiTheme.Ink : UiTheme.InkMuted;
        }

        /// <summary>The focused control's own one-line explanation, or null to show the station's purpose.</summary>
        private string ContextLine(string focusId)
        {
            if (string.IsNullOrEmpty(focusId)) return null;
            const string allocate = "character.allocate.";
            if (focusId.StartsWith(allocate, StringComparison.Ordinal)
                && Enum.TryParse<RuinRail.Gameplay.Progression.SkillId>(focusId.Substring(allocate.Length), out var skill))
                return _hub.Character.DescriptionOf(skill);
            var workshop = _hub.Workshop;
            return focusId switch
            {
                "workshop.storage" => workshop.StorageTier >= workshop.MaxStorageTier
                    ? "Storage is at its highest tier."
                    : $"{workshop.StorageCapacity} -> {workshop.NextStorageCapacity} slots for {workshop.NextStorageUpgradeCost} C",
                "workshop.trader" => workshop.NextTraderUpgradeCost <= 0
                    ? "The Trader is at its highest level."
                    : $"Trader level {workshop.TraderLevel + 1}: more and better stock, {workshop.NextTraderUpgradeCost} C",
                _ => null
            };
        }

        private void OnCharacterSheetChanged(RuinRail.Gameplay.Base.CharacterSheet _)
        {
            if (_panelStation == BaseStation.Character) RefreshStationData();
        }

        // ---------------- the Trader counter ----------------

        /// <summary>Visible offer rows before the list scrolls (the focus window pages the rest).</summary>
        public const int TraderVisibleRows = 4;
        /// <summary>Detail lines under the counter (the pager adds its own hint line when they overflow).</summary>
        public const int TraderDetailLines = 6;

        public IReadOnlyList<MerchantRowView> TraderRows => _traderRows;
        public string TraderDetailTitleText => _traderDetailTitle != null ? _traderDetailTitle.text : string.Empty;
        public string TraderDetailSubtitleText => _traderDetailSubtitle != null ? _traderDetailSubtitle.text : string.Empty;
        public IReadOnlyList<string> TraderDetailTexts => _traderDetailLines.Select(l => l.text).ToList();
        /// <summary>The details pager for the counter; the inputs step it, the tests read it.</summary>
        public DetailPager TraderDetails => _traderDetails;

        /// <summary>
        /// The counter: graphical offer rows over a details pane, then SELL.
        ///
        /// The rows are <see cref="MerchantRowView"/> — the same primitive the Dungeon Merchant uses — so an offer
        /// carries its icon, rarity frame, rarity/category subtitle and price, and two rolls of one definition are
        /// told apart at a glance. The focus list is unchanged (<c>trader.buy.N</c> plus <c>trader.sell</c>), so the
        /// transaction path, the navigation contract and the keyboard/controller/pointer behaviour are all the ones
        /// that were already accepted.
        /// </summary>
        private void BuildTraderPanel(UiRect region)
        {
            _panelList = ScreenNavigation.Trader(_hub.Trader, () => _selectedInstance);
            _selectedInstance = FirstSelectable(BaseStation.Trader);

            var rowPitch = MerchantRowView.Height + 2;
            var listHeight = TraderVisibleRows * rowPitch;
            var sellY = region.Y + listHeight + 2;
            const int sellHeight = 16;
            var detailTop = sellY + sellHeight + 6;

            var skin = UiSkin.Load();
            var offerItems = _panelList.Items.Where(i => ShelterTraderPresentation.OfferIndexOf(i.Id) >= 0).ToList();
            var window = _traderWindow = new FocusWindow(_panelList, TraderVisibleRows, offerItems.Count);
            for (var slot = 0; slot < TraderVisibleRows; slot++)
            {
                var bounds = new UiRect(region.X, region.Y + slot * rowPitch, region.Width, MerchantRowView.Height);
                var row = MerchantRowView.Create(_panel.transform, bounds, skin != null ? skin.InventorySlot : null, "TraderRow" + slot);
                row.gameObject.SetActive(slot < offerItems.Count);
                row.Bind(_panelList, slot < offerItems.Count ? offerItems[slot] : null,
                    i => { _panelList.Focus(i.Id); _panelList.ActivateFocused(); RefreshTexts(); RefreshStationData(); });
                _traderRows.Add(row);
                window.Register(slot, row.Control);
                _panelControls.Add(row.Control);
            }

            var sellItem = _panelList.Find("trader.sell");
            if (sellItem != null)
            {
                var sell = UiKit.Control(_panel.transform, _panelList, sellItem,
                    new UiRect(region.X, sellY, region.Width, sellHeight), ControlRole.Button,
                    i => { _panelList.Focus(i.Id); _panelList.ActivateFocused(); RefreshTexts(); RefreshStationData(); },
                    labelAnchor: TextAnchor.MiddleLeft);
                _panelControls.Add(sell);
            }

            UiKit.Plate(_panel.transform, new UiRect(region.X, detailTop - 4, region.Width, 1), UiTheme.PanelEdgeSoft, "TraderRule");
            _traderDetailTitle = UiKit.Label(_panel.transform, string.Empty,
                new UiRect(region.X, detailTop, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            _traderDetailSubtitle = UiKit.Label(_panel.transform, string.Empty,
                new UiRect(region.X, detailTop + UiText.LineHeight, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            for (var i = 0; i < TraderDetailLines; i++)
            {
                var y = detailTop + UiText.LineHeight * 2 + 3 + i * UiText.LineHeight;
                if (y + UiText.Height() > region.Bottom) break;
                _traderDetailLines.Add(UiKit.Label(_panel.transform, string.Empty,
                    new UiRect(region.X, y, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink));
            }

            _panel.AddComponent<FocusWindowDriver>().Bind(window);
            _panelList.FocusChanged += OnTraderFocusChanged;
            _input.Stack.Push(_panelList);
            RenderTrader();
        }

        private void OnTraderFocusChanged(FocusItem _) => RenderTrader();

        private RuinRail.UI.Inventory.LoadoutPanelView _loadoutView;
        /// <summary>The graphical LOADOUT body while that station is open (null otherwise).</summary>
        public RuinRail.UI.Inventory.LoadoutPanelView LoadoutView => _loadoutView;

        private void BuildLoadoutPanel(UiRect region)
        {
            _panelList = ScreenNavigation.Inventory(_hub.Loadout.Inventory);
            _loadoutView = RuinRail.UI.Inventory.LoadoutPanelView.Create(_panel.transform, region, _hub.Loadout.Inventory, _panelList);
            _panelControls.Add(_loadoutView.ActionButton);
            _input.Stack.Push(_panelList);
        }

        /// <summary>Re-reads the counter: stock, sold state, affordability, and the focused offer's details.</summary>
        private void RenderTrader()
        {
            if (_traderRows.Count == 0 || _hub == null) return;
            var rows = ShelterTraderPresentation.Rows(_hub.Trader.Offers);
            var banked = _hub.Trader.Banked;
            var loadout = Session?.Loadout;
            var skin = UiSkin.Load();
            Func<int, Sprite> rarityFrame = skin != null ? skin.RarityFrame : null;
            var rowWidth = Mathf.RoundToInt(((RectTransform)_traderRows[0].transform).sizeDelta.x);

            // The focus window owns which offers are on screen; reading its offset (rather than recomputing one) is
            // what keeps the row art and the focused control looking at the same entry on the frame focus moves.
            _traderWindow?.Refresh();
            var focusedIndex = ShelterTraderPresentation.OfferIndexOf(_panelList?.Focused?.Id);
            var first = _traderWindow != null ? _traderWindow.Offset : 0;

            for (var slot = 0; slot < _traderRows.Count; slot++)
            {
                var at = first + slot;
                var row = at < rows.Count ? rows[at] : null;
                _traderRows[slot].Show(row, rarityFrame, rowWidth);
                if (row == null) continue;
                // The counter says what the Dungeon Merchant's subtitle cannot: whether the coins are there and
                // whether this definition is already worn, so a duplicate buy is a decision rather than a surprise.
                if (!row.IsSold && row.Price > banked) _traderRows[slot].Annotate("NO COINS", UiTheme.Danger);
                else if (ShelterTraderPresentation.IsEquippedAlready(row, loadout)) _traderRows[slot].Annotate("EQUIPPED", UiTheme.Cyan);
            }

            RenderTraderDetails(rows, focusedIndex, banked, loadout);
        }

        private void RenderTraderDetails(List<MerchantRow> rows, int focusedIndex, int banked, RuinRail.Gameplay.Items.PlayerInventory loadout)
        {
            if (_traderDetailTitle == null) return;
            foreach (var line in _traderDetailLines) line.text = string.Empty;
            var row = focusedIndex >= 0 ? rows.FirstOrDefault(r => r.Index == focusedIndex) : null;
            var width = _traderDetailTitle != null ? Mathf.RoundToInt(((RectTransform)_traderDetailTitle.transform).sizeDelta.x) : 0;
            var tooltip = ShelterTraderPresentation.TooltipFor(row, loadout, _app != null ? _app.Specials : null);
            if (tooltip == null)
            {
                _traderDetails.SetRows(null, false);
                _traderDetailKey = null;
                _traderDetailTitle.text = rows.Count == 0 ? "THE COUNTER IS BARE" : "SELECT AN OFFER";
                _traderDetailTitle.color = UiTheme.InkMuted;
                _traderDetailSubtitle.text = rows.Count == 0
                    ? "Stock rotates as the Trader is upgraded."
                    : "Its stats, affixes and comparison show here.";
                return;
            }

            var affordable = row.IsSold || row.Price <= banked;
            _traderDetailTitle.text = UiText.Fit(tooltip.Name, width);
            _traderDetailTitle.color = UiTheme.Ink;
            _traderDetailSubtitle.text = UiText.Fit(ShelterTraderPresentation.DetailSubtitle(row, tooltip, affordable), width);
            _traderDetailSubtitle.color = affordable ? UiTheme.InkMuted : UiTheme.Danger;

            var key = row.Item != null ? row.Item.InstanceId : row.Index.ToString();
            var sameItem = key == _traderDetailKey;
            _traderDetailKey = key;
            _traderDetails.SetRows(ItemDetailLayout.Compose(tooltip, ShelterTraderPresentation.CompareFor(row, loadout, _app != null ? _app.Specials : null), width), sameItem);
            var visible = _traderDetails.Visible(DetailPagingInput.Hint());
            for (var i = 0; i < _traderDetailLines.Count && i < visible.Count; i++)
            {
                _traderDetailLines[i].text = ItemDetailLayout.Render(visible[i], width);
                _traderDetailLines[i].color = visible[i].Color;
            }
        }

        private void BuildStationControls(BaseStation station, UiRect region)
        {
            _panelList = station switch
            {
                BaseStation.Storage => ScreenNavigation.Storage(OpenStash, StoreWholeBackpack, () => Session != null && Session.Loadout.BackpackSlots.Any(i => i != null)),
                BaseStation.Loadout => ScreenNavigation.Inventory(_hub.Loadout.Inventory),
                BaseStation.Trader => ScreenNavigation.Trader(_hub.Trader, () => _selectedInstance),
                BaseStation.Character => ScreenNavigation.Character(_hub.Character, OpenNameEntry),
                BaseStation.Workshop => ScreenNavigation.Workshop(_hub.Workshop),
                BaseStation.Multiplayer => ScreenNavigation.Multiplayer(_terminal),
                _ => ScreenNavigation.Transit(_hub.Transit, _hub.Multiplayer)
            };

            _selectedInstance = FirstSelectable(station);

            // Transit's and Storage's controls are the primary actions of their stations (depart; open the stash).
            var prominent = station == BaseStation.Transit || station == BaseStation.Storage;
            var role = prominent ? ControlRole.Primary : ControlRole.Button;
            var rowHeight = prominent ? 22 : 14;
            const int gap = 2;

            var capacity = ScreenLayout.RowCapacity(region, rowHeight, gap);
            var rows = ScreenLayout.Rows(region, rowHeight, gap, capacity);
            var window = new FocusWindow(_panelList, capacity);

            for (var slot = 0; slot < rows.Count && slot < _panelList.Items.Count; slot++)
            {
                if (IsCoinControl(_panelList.Items[slot].Id)) continue; // laid out as the coin selector below
                var control = UiKit.Control(_panel.transform, _panelList, _panelList.Items[slot], rows[slot], role,
                    i => { _panelList.Focus(i.Id); _panelList.ActivateFocused(); RefreshTexts(); RefreshStationData(); },
                    labelAnchor: role == ControlRole.Primary ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                window.Register(slot, control);
                _panelControls.Add(control);
            }

            if (station == BaseStation.Transit && rows.Count >= 2) BuildCoinSelector(new UiRect(region.X, rows[1].Bottom + 10, region.Width, region.Bottom - rows[1].Bottom - 10), window);

            _panel.AddComponent<FocusWindowDriver>().Bind(window);
            _input.Stack.Push(_panelList);
        }

        private static bool IsCoinControl(string id) => id != null && id.StartsWith("transit.coins.", StringComparison.Ordinal);

        private Text _coinAmount;
        /// <summary>The coin selector's amount readout (what the next Start takes from the bank).</summary>
        public string CoinSelectorText => _coinAmount != null ? _coinAmount.text : string.Empty;

        /// <summary>
        /// Coins for the run (77): [-] amount [+] with NONE / ALL under it — the choice only; the Start transaction
        /// moves it. The data column beside it shows banked now, taking, and what stays banked.
        /// </summary>
        private void BuildCoinSelector(UiRect area, FocusWindow window)
        {
            UiKit.Label(_panel.transform, "COINS FOR THE RUN", new UiRect(area.X, area.Y, area.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            const int stepWidth = 30;
            const int selectorHeight = 18;
            var y = area.Y + UiText.LineHeight + 2;
            var plate = new UiRect(area.X + stepWidth + 2, y, area.Width - (stepWidth + 2) * 2, selectorHeight);
            UiKit.Plate(_panel.transform, plate, UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "CoinAmountPlate");
            _coinAmount = UiKit.Label(_panel.transform, string.Empty, new UiRect(plate.X, plate.Y + (selectorHeight - UiText.LineHeight) / 2, plate.Width, UiText.Height()), 1, TextAnchor.UpperCenter, UiTheme.Ink);
            _coinAmount.alignment = TextAnchor.UpperCenter;
            var half = (area.Width - 2) / 2;
            var bounds = new Dictionary<string, UiRect>
            {
                [ScreenNavigation.CoinsLessId] = new UiRect(area.X, y, stepWidth, selectorHeight),
                [ScreenNavigation.CoinsMoreId] = new UiRect(area.Right - stepWidth, y, stepWidth, selectorHeight),
                [ScreenNavigation.CoinsNoneId] = new UiRect(area.X, y + selectorHeight + 3, half, 14),
                [ScreenNavigation.CoinsAllId] = new UiRect(area.Right - half, y + selectorHeight + 3, half, 14)
            };
            for (var index = 0; index < _panelList.Items.Count; index++)
            {
                var item = _panelList.Items[index];
                if (!bounds.TryGetValue(item.Id, out var rect)) continue;
                var control = UiKit.Control(_panel.transform, _panelList, item, rect, ControlRole.Button,
                    i => { _panelList.Focus(i.Id); _panelList.ActivateFocused(); RefreshTexts(); RefreshStationData(); },
                    labelAnchor: TextAnchor.MiddleCenter);
                window.Register(index, control);
                _panelControls.Add(control);
            }

            UiKit.Label(_panel.transform, "At risk until you return", new UiRect(area.X, y + selectorHeight + 21, area.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            RefreshCoinSelector();
        }

        private void RefreshCoinSelector()
        {
            if (_coinAmount == null || _hub == null) return;
            var taking = _hub.Transit.CoinsToCarry;
            _coinAmount.text = taking + " C";
            _coinAmount.color = taking > 0 ? UiTheme.Amber : UiTheme.InkMuted;
        }

        /// <summary>The station data column: headings, key/value rows, or an honest sentence when there is nothing.</summary>
        private void BuildStationData(StationView view, UiRect region)
        {
            if (_panelData != null) Destroy(_panelData);
            _panelData = new GameObject("StationData");
            _panelData.transform.SetParent(_panel.transform, false);
            var dataRect = _panelData.AddComponent<RectTransform>();
            dataRect.anchorMin = dataRect.anchorMax = new Vector2(0f, 1f);
            dataRect.pivot = new Vector2(0f, 1f);
            dataRect.anchoredPosition = Vector2.zero;
            dataRect.sizeDelta = Vector2.zero;
            var parent = _panelData.transform;
            if (view.IsEmpty)
            {
                UiKit.Label(parent, view.EmptyText,
                    new UiRect(region.X, region.Y, region.Width, UiText.Height(6)), 1,
                    TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);
                return;
            }

            var y = region.Y;
            foreach (var row in view.Rows)
            {
                if (y + UiText.Height() > region.Bottom) break;

                if (row.IsHeading)
                {
                    y += 3;
                    if (y + UiText.Height() > region.Bottom) break;
                    UiKit.Label(parent, UiText.Fit(row.Key, region.Width),
                        new UiRect(region.X, y, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
                    UiKit.Plate(parent, new UiRect(region.X, y + UiText.Height() + 1, region.Width, 1), UiTheme.AmberDim, "Rule");
                    y += UiText.LineHeight + 3;
                    continue;
                }

                if (row.IsText)
                {
                    // A sentence needs the whole column: the key/value split would clip it at 56 %.
                    UiKit.Label(parent, UiText.Fit(row.Key, region.Width),
                        new UiRect(region.X, y, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
                    y += UiText.LineHeight;
                    continue;
                }

                var height = RowHeight(row);
                if (y + height > region.Bottom) break;
                switch (row.Kind)
                {
                    case RowKind.Bar: DrawBarRow(parent, region, y, row); break;
                    case RowKind.Item: DrawItemRow(parent, region, y, row); break;
                    case RowKind.Badge: DrawBadgeRow(parent, region, y, row); break;
                    case RowKind.Pips: DrawPipsRow(parent, region, y, row); break;
                    default: UiKit.StatRow(parent, new UiRect(region.X, y, region.Width, UiText.Height()), row.Key, row.Value); break;
                }

                y += height;
            }
        }

        // ---------------- visual station rows ----------------

        private static int RowHeight(StationRow row) => row.Kind switch
        {
            RowKind.Bar => UiText.LineHeight + 8,
            RowKind.Item => 15,
            RowKind.Badge => UiText.Width(row.Key) > BadgeTextWidth ? UiText.Height(2) + 11 : UiText.Height() + 11,
            RowKind.Pips => UiText.LineHeight + 3,
            _ => UiText.LineHeight
        };

        private static Color ToneColor(RowTone tone) => tone switch
        {
            RowTone.Good => UiTheme.Terminal,
            RowTone.Warn => UiTheme.Amber,
            RowTone.Bad => UiTheme.Danger,
            RowTone.Muted => UiTheme.InkMuted,
            _ => UiTheme.Ink
        };

        /// <summary>Key left, value right, a 3 px bar under both filled to the row's fraction in its tone.</summary>
        private static void DrawBarRow(Transform parent, UiRect region, int y, StationRow row)
        {
            y += 2; // air above each bar row, so a bar never touches the line before it
            var valueWidth = Mathf.Min(UiText.Width(row.Value), region.Width / 2);
            UiKit.Label(parent, UiText.Fit(row.Key, region.Width - valueWidth - 4), new UiRect(region.X, y, region.Width - valueWidth - 4, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.Ink);
            if (valueWidth > 0)
            {
                var value = UiKit.Label(parent, UiText.Fit(row.Value, valueWidth), new UiRect(region.Right - valueWidth, y, valueWidth, UiText.Height()),
                    1, TextAnchor.UpperRight, ToneColor(row.Tone == RowTone.Warn ? RowTone.Neutral : row.Tone));
                value.alignment = TextAnchor.UpperRight;
            }

            var bar = new UiRect(region.X, y + UiText.LineHeight + 1, region.Width, 3);
            UiKit.Plate(parent, bar, UiTheme.WithAlpha(UiTheme.NearBlack, 0.9f), "BarTrack");
            var fill = Mathf.RoundToInt(row.Fraction * bar.Width);
            if (fill > 0) UiKit.Plate(parent, new UiRect(bar.X, bar.Y, fill, bar.Height), ToneColor(row.Tone), "BarFill");
        }

        /// <summary>Slot key (muted), a rarity chip, the item's icon and its name in its rarity colour.</summary>
        private void DrawItemRow(Transform parent, UiRect region, int y, StationRow row)
        {
            var x = region.X;
            if (row.Key.Length > 0)
            {
                const int keyWidth = 60;
                UiKit.Label(parent, UiText.Fit(row.Key, keyWidth), new UiRect(x, y + 3, keyWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
                x += keyWidth;
            }

            var empty = string.IsNullOrEmpty(row.ItemId);
            var rarity = RarityStyle.For(row.Rarity);
            UiKit.Plate(parent, new UiRect(x, y, 13, 13), UiTheme.WithAlpha(UiTheme.NearBlack, 0.85f), "IconFrame");
            if (!empty)
            {
                UiKit.Plate(parent, new UiRect(x, y + 12, 13, 1), rarity.Color, "RarityChip");
                var icon = Session?.Configs.Resolve(row.ItemId)?.Icon;
                if (icon != null)
                {
                    var image = UiKit.Plate(parent, new UiRect(x + 1, y + 1, 11, 11), Color.white, "ItemIcon");
                    image.sprite = icon;
                    image.preserveAspect = true;
                }
            }

            x += 17;
            var nameColor = empty ? UiTheme.InkMuted : row.Rarity == Rarity.Common ? UiTheme.Ink : rarity.Color;
            UiKit.Label(parent, UiText.Fit(row.Value, region.Right - x), new UiRect(x, y + 3, region.Right - x, UiText.Height()), 1, TextAnchor.UpperLeft, nameColor);
        }

        /// <summary>Text width a status plate holds on one line; longer statuses wrap onto a second line.</summary>
        private const int BadgeTextWidth = 132;

        /// <summary>A full-width status plate in the row's tone (two lines when the status needs them).</summary>
        private static void DrawBadgeRow(Transform parent, UiRect region, int y, StationRow row)
        {
            var twoLines = UiText.Width(row.Key) > BadgeTextWidth;
            var plate = new UiRect(region.X, y, region.Width, (twoLines ? UiText.Height(2) : UiText.Height()) + 8);
            UiKit.Plate(parent, plate, UiTheme.Darken(ToneColor(row.Tone), 0.55f), "StatusPlate");
            UiKit.Plate(parent, new UiRect(plate.X, plate.Y, 2, plate.Height), ToneColor(row.Tone), "StatusEdge");
            var label = UiKit.Label(parent, twoLines ? row.Key : UiText.Fit(row.Key, plate.Width - 8),
                new UiRect(plate.X + 4, plate.Y + 4, plate.Width - 8, twoLines ? UiText.Height(2) : UiText.Height()),
                1, TextAnchor.UpperCenter, UiTheme.Ink, wrap: twoLines);
            label.alignment = TextAnchor.UpperCenter;
        }

        /// <summary>Key, one square per slot (lit up to Filled), and an optional value on the right.</summary>
        private static void DrawPipsRow(Transform parent, UiRect region, int y, StationRow row)
        {
            const int keyWidth = 60;
            UiKit.Label(parent, UiText.Fit(row.Key, keyWidth), new UiRect(region.X, y, keyWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
            var step = row.Total > 8 ? 8 : 10;
            for (var i = 0; i < row.Total; i++)
                UiKit.Plate(parent, new UiRect(region.X + keyWidth + i * step, y + 1, step - 3, 7),
                    i < row.Filled ? ToneColor(row.Tone) : UiTheme.PanelEdgeSoft, "Pip");
            var valueX = region.X + keyWidth + row.Total * step + 4;
            if (row.Value.Length > 0 && valueX < region.Right)
            {
                var value = UiKit.Label(parent, UiText.Fit(row.Value, region.Right - valueX), new UiRect(valueX, y, region.Right - valueX, UiText.Height()),
                    1, TextAnchor.UpperRight, UiTheme.Ink);
                value.alignment = TextAnchor.UpperRight;
            }
        }

        // ---------------- footer ----------------

        private void BuildFooter()
        {
            var footer = ScreenLayout.Footer;
            UiKit.ChromeBar(_root, footer, "Footer", ruleAtBottom: false);

            var hintWidth = UiText.Width(_prompts.Footer()) + 8;
            _footer = UiKit.Label(_root, _prompts.Footer(),
                new UiRect(UiTheme.ScreenMargin, footer.Y + 5, hintWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);

            // Feedback gets its own reserved strip to the right of the hints, so the two can never run together.
            var feedbackX = UiTheme.ScreenMargin + hintWidth + UiTheme.Pad;
            _feedback = UiKit.Label(_root, string.Empty,
                new UiRect(feedbackX, footer.Y + 5, ScreenLayout.Width - UiTheme.ScreenMargin - feedbackX, UiText.Height()),
                1, TextAnchor.UpperRight, UiTheme.Ink);
        }

        private void OnFeedback(StationFeedback feedback)
        {
            var width = ScreenLayout.Width - UiTheme.ScreenMargin - (UiTheme.ScreenMargin + UiText.Width(_prompts.Footer()) + 8 + UiTheme.Pad);
            _feedback.text = UiText.Fit(feedback.Text, width);
            _feedback.color = feedback.IsError ? UiTheme.Danger : UiTheme.Terminal;
        }

        // ---------------- refresh ----------------

        private void RefreshTexts()
        {
            var session = Session;
            if (session == null) return;

            var header = CurrentHeader();
            _profileName.text = header.ProfileName.Text;
            _profileStats.text = header.ProfileStats.Text;

            var sheet = _hub.Character.Sheet;
            var inner = ScreenLayout.LeftColumn.Inset(UiTheme.Pad);
            _survivorName.text = UiText.Fit(session.Profile.DisplayName, inner.Width - BadgeWidth - 4);
            _levelBadge.text = "LV " + sheet.Level;
            SetBar(_xpFill, sheet.IsMaxLevel ? 1f : sheet.XpToNextLevel <= 0 ? 0f : sheet.XpIntoLevel / (float)sheet.XpToNextLevel, inner.Width);
            _depthLabel.text = session.Profile.DeepestDepthReached > 0 ? "DEPTH " + session.Profile.DeepestDepthReached : "NO DEPTH YET";
            _pointsBadge.text = sheet.UnspentPoints > 0 ? "+" + sheet.UnspentPoints + " PTS" : string.Empty;
            _coinsLabel.text = session.Banked.Balance + " C";
            var stored = session.Storage.Items.Count();
            _storageLabel.text = $"{stored}/{session.Storage.Capacity}";
            SetBar(_storageFill, session.Storage.Capacity <= 0 ? 0f : stored / (float)session.Storage.Capacity, inner.Width);

            var members = _hub.Multiplayer.Members;
            var ready = members.Count(m => m.IsReady);
            for (var i = 0; i < _partyPips.Count; i++)
                _partyPips[i].color = i < members.Count ? (members[i].IsReady ? UiTheme.Terminal : UiTheme.Amber) : UiTheme.PanelEdgeSoft;
            _partySummary.text = members.Count <= 1 ? "SOLO" : $"{members.Count}/{RuinRail.Networking.SessionRequest.MaxPartySize}";

            var canStart = _hub.Transit.CanStart;
            _expeditionState.text = canStart ? "READY" : "HELD";
            _expeditionPlate.color = canStart ? UiTheme.Darken(UiTheme.Terminal, 0.45f) : UiTheme.AmberDim;
            for (var i = 0; i < _readyPips.Count; i++)
                _readyPips[i].color = i < ready ? UiTheme.Terminal : i < members.Count ? UiTheme.Amber : UiTheme.PanelEdgeSoft;
            _expeditionHint.text = canStart ? "Start at TRANSIT." : _hub.Multiplayer.LocalReady ? "Waiting for the party." : "Ready up at TRANSIT.";

            var guiding = !_onboarding.IsComplete && !string.IsNullOrEmpty(_onboarding.PromptText);
            var loot = !guiding && session.HasLootToStash;
            _onboardingCard.SetActive(guiding || loot);
            if (guiding) _onboardingText.text = _onboarding.PromptText;
            else if (loot) _onboardingText.text = "Your loot is home. Open STORAGE, then OPEN STASH to put it away.";
            if (_storageTabPip != null) _storageTabPip.enabled = loot;
        }

        private void Update()
        {
            _footer.text = _prompts.Footer();
            RefreshTexts();
            // The counter's details page with the same wheel / PageUp-Down / right-stick input the merchant uses.
            if (_panelStation == BaseStation.Trader && _traderDetails.Overflows)
            {
                var step = DetailPagingInput.Poll();
                if (step > 0 && _traderDetails.PageDown()) RenderTrader();
                else if (step < 0 && _traderDetails.PageUp()) RenderTrader();
            }
        }

        private void OnDestroy()
        {
            if (_hub != null)
            {
                _hub.StationChanged -= OnStationChanged;
                _hub.SummaryReady -= OnSummary;
                foreach (var panel in new[] { _hub.Storage.Feedback, _hub.Loadout.Feedback, _hub.Trader.Feedback, _hub.Character.Feedback, _hub.Workshop.Feedback, _hub.Transit.Feedback })
                    panel.Changed -= OnFeedback;
                _hub.Dispose();
            }

            if (_stashView != null) { Destroy(_stashView.gameObject); _stashView = null; }
            _stash?.Dispose();
            _stash = null;
            if (_onboarding != null) { _onboarding.Changed -= RefreshTexts; _onboarding.DisplayNameChanged -= OnDisplayNameChanged; _onboarding.Dispose(); }
            _partyBridge?.Dispose();
            _partyBridge = null;
            if (_app?.Coop != null) _app.Coop.RunStartReceived -= OnCoopRunStart;
            if (Session != null)
            {
                Session.Expedition.ExpeditionStarted -= OnExpeditionStarted;
                Session.Character.Changed -= OnCharacterSheetChanged;
            }
            if (_terminal != null) _terminal.Changed -= OnTerminalChanged;
            _terminal?.Dispose();
        }

        private string FirstSelectable(BaseStation station) => station switch
        {
            BaseStation.Storage => Session.Loadout.BackpackSlots.FirstOrDefault(i => i != null)?.InstanceId ?? Session.Storage.Items.FirstOrDefault()?.InstanceId,
            BaseStation.Trader => Session.Loadout.BackpackSlots.FirstOrDefault(i => i != null)?.InstanceId,
            _ => null
        };

        private void OnExpeditionStarted(ExpeditionState state) => _app.LoadScene(SceneNames.Dungeon);

        /// <summary>
        /// Client: the host started the party's expedition. This peer commits its loadout and starts its own at-risk
        /// transaction from the host's start (seed, biome, party size, participant id) — the same Start transaction
        /// the host ran — and the expedition-started handler above takes it into the Dungeon.
        /// </summary>
        private void OnCoopRunStart(RunStartMessage run)
        {
            var session = Session;
            if (session == null || session.Expedition.IsExpeditionActive || _app.Coop == null || !_app.Coop.IsClient) return;
            session.EnsureStarterLoadoutIfEmpty();
            session.CommitLoadoutToProfile();
            var state = _app.Coop.ApplyRunStart(run);
            if (state == null) Debug.LogError($"COOP-CLIENT could not start the host's run {run?.StartId}.");
        }

        private void OnSummary(ExpeditionSummaryViewModel summary)
        {
            _feedback.text = UiText.Fit(summary.Title, 40);
            _feedback.color = summary.Summary.IsSuccess ? UiTheme.Terminal : UiTheme.Danger;
        }

        /// <summary>CHANGE NAME: opens the field, or says why the name cannot change right now.</summary>
        public void OpenNameEntry()
        {
            if (_nameEntryView != null && _nameEntryView.Open()) return;
            _hub.Character.Feedback.Error(_nameEntry.BlockedReason ?? "The name cannot be changed here.");
        }

        /// <summary>
        /// A confirmed or changed name reaches every place that captured the old one when the Shelter was built: the
        /// connection payload the next host/join sends and the host's own approval-roster line. Lobby lines, run
        /// starts and the expedition read the profile live.
        /// </summary>
        private void OnDisplayNameChanged(string name)
        {
            if (_app.Network?.Driver is NgoNetworkDriver ngo) ngo.SetConnectionPayload(name);
            _approvalRoster?.Rename(BaseSession.LocalClientId, name);
            if (_app.Coop != null && _app.Coop.IsClient) _app.Coop.SendClientLobbyMember();
            _hub.Character.Feedback.Ok("Name saved: " + name);
            RefreshTexts();
        }

        private void OnBack()
        {
            if (StashOpen) { CloseStash(); return; }
            // A picked-up loadout item is put down first; the next Back leaves the station.
            if (_hub.Current == BaseStation.Loadout && _hub.Loadout.Inventory.Selected.HasValue) { _hub.Loadout.Inventory.CancelSelection(); return; }
            if (_hub.Current != null) { _hub.Close(); return; }
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
        }
    }
}
