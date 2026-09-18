using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.UI.Base;
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
        private TerminalViewModel _terminal;
        private MenuInput _input;
        private RectTransform _root;
        private FocusList _stationList;
        private FocusList _panelList;
        private GameObject _panel;
        private Text _feedback;
        private Text _footer;
        private Text _onboardingText;
        private string _selectedInstance;
        private readonly UiPrompts _prompts = new();
        private readonly List<UiControl> _controls = new();
        private readonly List<UiControl> _panelControls = new();

        // Header and left-column labels are rebuilt in place rather than recreated, so nothing can accumulate.
        private Text _profileName;
        private Text _profileStats;
        private readonly Dictionary<string, Text> _leftValues = new();
        private Text _partySummary;
        private Text _expeditionState;
        private Text _expeditionHint;

        public BaseHubViewModel Hub => _hub;
        public ShelterOnboardingViewModel Onboarding => _onboarding;
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

            _terminal = new TerminalViewModel(new MultiplayerTerminalService(app.Network.Controller), session.Lobby, BaseSession.LocalClientId);
            _hub = new BaseHubViewModel(session, new MultiplayerTerminalService(app.Network.Controller), app.NextRunSeed);
            _onboarding = new ShelterOnboardingViewModel(session, app.Content.DisplayNamePolicy);
            app.SettingsScreen.SetTutorialProgress(new SaveSlotTutorialProgress(session.Slot, session.Autosave, () => app.Settings.Current.Tutorial.ShowPrompts));
            app.MusicBinder.Attach(session.Progression);

            _input = gameObject.AddComponent<MenuInput>();
            _input.Back += OnBack;
            _input.Horizontal += StepSection;
            _input.InputBlocked = () => app.InputBlocked;

            _root = UiKit.ReferenceRoot(transform);
            UiKit.Backdrop(_root, UiSkin.Load()?.ShelterBackdrop);

            BuildHeader();
            BuildTabs();
            BuildLeftColumn();
            BuildRightColumn();
            BuildFooter();

            _input.Stack.Push(_stationList);
            _hub.StationChanged += OnStationChanged;
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
            }
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

        // ---------------- zone 4a: the survivor column ----------------

        private void BuildLeftColumn()
        {
            var column = ScreenLayout.LeftColumn;
            UiKit.Panel(_root, column, "SurvivorPanel");
            var inner = column.Inset(UiTheme.Pad);
            var y = inner.Y;

            y = SectionHeading(_root, inner, y, "SURVIVOR");
            y = ValueRow(inner, y, "NAME", "name");
            y = ValueRow(inner, y, "LEVEL", "level");
            y = ValueRow(inner, y, "XP", "xp");
            y = ValueRow(inner, y, "POINTS", "points");

            y += 4;
            y = SectionHeading(_root, inner, y, "SHELTER");
            y = ValueRow(inner, y, "BANKED", "banked");
            y = ValueRow(inner, y, "STORAGE", "storage");
            y = ValueRow(inner, y, "TRADER", "trader");

            y += 4;
            y = SectionHeading(_root, inner, y, "PARTY");
            _partySummary = UiKit.Label(_root, string.Empty,
                new UiRect(inner.X, y, inner.Width, UiText.Height(4)), 1, TextAnchor.UpperLeft, UiTheme.Ink, wrap: true);
            y += UiText.LineHeight * 4;

            y += 4;
            UiKit.Plate(_root, new UiRect(inner.X, y, inner.Width, 1), UiTheme.PanelEdgeSoft, "Rule");
            y += 5;

            // The contextual help line: the onboarding step, or the section the player is standing in.
            _onboardingText = UiKit.Label(_root, string.Empty,
                new UiRect(inner.X, y, inner.Width, Mathf.Max(UiText.Height(2), inner.Bottom - y)), 1,
                TextAnchor.UpperLeft, UiTheme.InkFaint, wrap: true);
        }

        private int SectionHeading(Transform parent, UiRect inner, int y, string text)
        {
            UiKit.Label(parent, text, new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            UiKit.Plate(parent, new UiRect(inner.X, y + UiText.Height() + 1, inner.Width, 1), UiTheme.AmberDim, "HeadingRule");
            return y + UiText.LineHeight + 3;
        }

        private int ValueRow(UiRect inner, int y, string key, string id)
        {
            var (_, value) = UiKit.StatRow(_root, new UiRect(inner.X, y, inner.Width, UiText.Height()), key, string.Empty);
            _leftValues[id] = value;
            return y + UiText.LineHeight;
        }

        // ---------------- zone 4b: the expedition column ----------------

        /// <summary>
        /// The right card: the state of the next expedition.
        ///
        /// It carries no controls of its own on purpose. TRANSIT is already a control in the tab bar — styled as the
        /// primary action there — and a second button firing the same action would give the screen two places to
        /// start a run and two things for focus to sit on. The card reports; the tab acts.
        /// </summary>
        private void BuildRightColumn()
        {
            var column = ScreenLayout.RightColumn;
            UiKit.Panel(_root, column, "ExpeditionPanel");
            var inner = column.Inset(UiTheme.Pad);
            var y = inner.Y;

            y = SectionHeading(_root, inner, y, "EXPEDITION");

            _expeditionState = UiKit.Label(_root, string.Empty,
                new UiRect(inner.X, y, inner.Width, UiText.Height(1, 2)), 2, TextAnchor.UpperLeft, UiTheme.Ink);
            y += UiText.Height(1, 2) + 6;

            _expeditionHint = UiKit.Label(_root, string.Empty,
                new UiRect(inner.X, y, inner.Width, UiText.Height(4)), 1, TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);
            y += UiText.LineHeight * 4 + 4;

            UiKit.Plate(_root, new UiRect(inner.X, y, inner.Width, 1), UiTheme.PanelEdgeSoft, "Rule");
            y += 6;

            UiKit.Label(_root, "AT RISK", new UiRect(inner.X, y, inner.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
            y += UiText.LineHeight + 2;
            UiKit.Label(_root,
                "Everything in the loadout and backpack is lost if the expedition fails. XP, Banked Coins and Storage are not.",
                new UiRect(inner.X, y, inner.Width, Mathf.Max(UiText.Height(6), inner.Bottom - y)), 1,
                TextAnchor.UpperLeft, UiTheme.InkFaint, wrap: true);
        }

        // ---------------- zone 4c: the station panel ----------------

        private void OnStationChanged(BaseStation? station)
        {
            if (_panel != null)
            {
                _input.Stack.Remove(_panelList);
                Destroy(_panel);
                _panel = null;
                _panelList = null;
                _panelControls.Clear();
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
            UiKit.Label(_panel.transform, UiText.Fit(view.Description, inner.Width - (textX - inner.X)),
                new UiRect(textX, inner.Y + UiText.LineHeight, inner.Width - (textX - inner.X), UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.InkMuted);

            var bodyY = inner.Y + UiText.LineHeight * 2 + 4;
            UiKit.Plate(_panel.transform, new UiRect(inner.X, bodyY - 3, inner.Width, 1), UiTheme.PanelEdge, "HeaderRule");

            // Two columns: the station's controls on the left, the state it holds on the right.
            var gap = UiTheme.Pad;
            var controlWidth = (inner.Width - gap) / 2;
            var controls = new UiRect(inner.X, bodyY, controlWidth, inner.Bottom - bodyY);
            var data = new UiRect(inner.X + controlWidth + gap, bodyY, inner.Width - controlWidth - gap, inner.Bottom - bodyY);

            UiKit.Plate(_panel.transform, new UiRect(data.X - gap / 2, bodyY, 1, data.Height), UiTheme.PanelEdgeSoft, "ColumnRule");

            BuildStationControls(station, controls);
            BuildStationData(view, data);
        }

        private void BuildStationControls(BaseStation station, UiRect region)
        {
            _panelList = station switch
            {
                BaseStation.Storage => ScreenNavigation.Storage(_hub.Storage, () => _selectedInstance),
                BaseStation.Loadout => ScreenNavigation.Inventory(_hub.Loadout.Inventory),
                BaseStation.Trader => ScreenNavigation.Trader(_hub.Trader, () => _selectedInstance),
                BaseStation.Character => ScreenNavigation.Character(_hub.Character),
                BaseStation.Workshop => ScreenNavigation.Workshop(_hub.Workshop),
                BaseStation.Multiplayer => ScreenNavigation.Multiplayer(_terminal),
                _ => ScreenNavigation.Transit(_hub.Transit, _hub.Multiplayer)
            };

            _selectedInstance = FirstSelectable(station);

            // Transit is the one station whose controls are the primary action of the whole screen.
            var role = station == BaseStation.Transit ? ControlRole.Primary : ControlRole.Button;
            var rowHeight = station == BaseStation.Transit ? 22 : 14;
            const int gap = 2;

            var capacity = ScreenLayout.RowCapacity(region, rowHeight, gap);
            var rows = ScreenLayout.Rows(region, rowHeight, gap, capacity);
            var window = new FocusWindow(_panelList, capacity);

            for (var slot = 0; slot < rows.Count && slot < _panelList.Items.Count; slot++)
            {
                var control = UiKit.Control(_panel.transform, _panelList, _panelList.Items[slot], rows[slot], role,
                    i => { _panelList.Focus(i.Id); _panelList.ActivateFocused(); RefreshTexts(); },
                    labelAnchor: role == ControlRole.Primary ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
                window.Register(slot, control);
                _panelControls.Add(control);
            }

            _panel.AddComponent<FocusWindowDriver>().Bind(window);
            _input.Stack.Push(_panelList);
        }

        /// <summary>The station data column: headings, key/value rows, or an honest sentence when there is nothing.</summary>
        private void BuildStationData(StationView view, UiRect region)
        {
            if (view.IsEmpty)
            {
                UiKit.Label(_panel.transform, view.EmptyText,
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
                    UiKit.Label(_panel.transform, UiText.Fit(row.Key, region.Width),
                        new UiRect(region.X, y, region.Width, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
                    UiKit.Plate(_panel.transform, new UiRect(region.X, y + UiText.Height() + 1, region.Width, 1), UiTheme.AmberDim, "Rule");
                    y += UiText.LineHeight + 3;
                    continue;
                }

                UiKit.StatRow(_panel.transform, new UiRect(region.X, y, region.Width, UiText.Height()), row.Key, row.Value);
                y += UiText.LineHeight;
            }
        }

        // ---------------- footer ----------------

        private void BuildFooter()
        {
            var footer = ScreenLayout.Footer;
            UiKit.ChromeBar(_root, footer, "Footer", ruleAtBottom: false);

            var hintWidth = UiText.Width(_prompts.Footer()) + 8;
            _footer = UiKit.Label(_root, _prompts.Footer(),
                new UiRect(UiTheme.ScreenMargin, footer.Y + 5, hintWidth, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkFaint);

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
            Set("name", session.Profile.DisplayName);
            Set("level", sheet.Level.ToString());
            Set("xp", sheet.IsMaxLevel ? "max" : $"{sheet.XpIntoLevel}/{sheet.XpToNextLevel}");
            Set("points", sheet.UnspentPoints.ToString());
            Set("banked", session.Banked.Balance + " C");
            Set("storage", $"{session.Storage.Items.Count()}/{session.Storage.Capacity}");
            Set("trader", "Lv " + _hub.Workshop.TraderLevel);

            var members = _hub.Multiplayer.Members;
            var ready = members.Count(m => m.IsReady);
            _partySummary.text = members.Count <= 1
                ? "Solo. The Multiplayer Terminal hosts or joins a party."
                : $"{members.Count} survivors, {ready} ready.";

            var canStart = _hub.Transit.CanStart;
            _expeditionState.text = canStart ? "READY" : "HELD";
            _expeditionState.color = canStart ? UiTheme.Terminal : UiTheme.Amber;
            _expeditionHint.text = canStart
                ? "The Transit Car is cleared to depart. Open TRANSIT and start the expedition."
                : _hub.Multiplayer.LocalReady
                    ? "Waiting for the rest of the party to report Ready."
                    : "Mark yourself Ready at the Multiplayer Terminal or at TRANSIT.";

            _onboardingText.text = !_onboarding.IsComplete && !string.IsNullOrEmpty(_onboarding.PromptText)
                ? _onboarding.PromptText
                : _hub.Current.HasValue
                    ? StationPresentation.DescriptionOf(_hub.Current.Value)
                    : "Arrows or the mouse pick a station. Esc leaves the current station.";
            _onboardingText.color = !_onboarding.IsComplete ? UiTheme.Amber : UiTheme.InkFaint;
        }

        private void Set(string id, string value)
        {
            if (!_leftValues.TryGetValue(id, out var label) || label == null) return;
            label.text = UiText.Fit(value, ScreenLayout.LeftColumn.Width - UiTheme.Pad * 2 - UiText.Width("STORAGE "));
        }

        private void Update()
        {
            _footer.text = _prompts.Footer();
            RefreshTexts();
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

            if (_onboarding != null) { _onboarding.Changed -= RefreshTexts; _onboarding.Dispose(); }
            if (Session != null) Session.Expedition.ExpeditionStarted -= OnExpeditionStarted;
            _terminal?.Dispose();
        }

        private string FirstSelectable(BaseStation station) => station switch
        {
            BaseStation.Storage => Session.Loadout.BackpackSlots.FirstOrDefault(i => i != null)?.InstanceId ?? Session.Storage.Items.FirstOrDefault()?.InstanceId,
            BaseStation.Trader => Session.Loadout.BackpackSlots.FirstOrDefault(i => i != null)?.InstanceId,
            _ => null
        };

        private void OnExpeditionStarted(ExpeditionState state) => _app.LoadScene(SceneNames.Dungeon);

        private void OnSummary(ExpeditionSummaryViewModel summary)
        {
            _feedback.text = UiText.Fit(summary.Title, 40);
            _feedback.color = summary.Summary.IsSuccess ? UiTheme.Terminal : UiTheme.Danger;
        }

        private void OnBack()
        {
            if (_hub.Current != null) { _hub.Close(); return; }
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
        }
    }
}
