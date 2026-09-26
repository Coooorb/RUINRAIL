using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Gameplay.Progression;
using RuinRail.UI.Base;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.App
{
    /// <summary>
    /// Main menu (94): PLAY / SETTINGS / QUIT over MainMenuViewModel; PLAY enters the Base scene.
    ///
    /// The screen is composed as a front-end rather than a list of buttons on black: the approach to the Shelter is
    /// behind it, the wordmark and the primary action own the left third, and the right card reports what is actually
    /// on disk so the player knows what PLAY is about to do before pressing it. Everything on it comes from the save
    /// probe and the menu view model; nothing is invented to fill the composition.
    /// </summary>
    public sealed class MainMenuScreen : MonoBehaviour
    {
        private GameApp _app;
        private MenuInput _input;
        private RectTransform _root;
        private Text _status;
        private Text _footer;
        private Text _playHint;
        private FocusList _menuList;
        private SettingsPanel.Instance _settings;
        private CodexPanel.Instance _codex;
        private RuinRail.UI.Codex.CodexViewModel _codexModel;
        private UiPrompts _prompts;
        private readonly List<UiControl> _controls = new();

        public MainMenuViewModel Menu => _app.Menu;
        public MenuInput Input => _input;
        public FocusList MenuList => _menuList;
        /// <summary>The Settings panel while it is open (its list owns the input), else null.</summary>
        public SettingsPanel.Instance SettingsInstance => _settings;
        public bool SettingsShowing => _settings != null && _settings.Panel != null;
        /// <summary>The Help / Codex page over the menu.</summary>
        public bool CodexShowing => _codex != null && _codex.Panel != null;
        public CodexPanel.Instance CodexInstance => _codex;
        public FocusList CodexList => _codex?.List;
        public FocusList SettingsList => _settings?.List;
        /// <summary>Every interactive control on the screen, in build order. The interaction tests walk this.</summary>
        public IReadOnlyList<UiControl> Controls => _controls;

        public static MainMenuScreen Create(GameApp app)
        {
            var canvas = UiKit.Canvas("MainMenuScreen");
            var screen = canvas.gameObject.AddComponent<MainMenuScreen>();
            screen.Build(app);
            return screen;
        }

        private void Build(GameApp app)
        {
            _app = app;
            _prompts = new UiPrompts();
            _input = gameObject.AddComponent<MenuInput>();
            _input.Back += OnBack;
            _input.InputBlocked = () => app.InputBlocked;

            _root = UiKit.ReferenceRoot(transform);
            UiKit.Backdrop(_root, UiSkin.Load()?.MenuBackdrop);

            BuildIdentity();
            BuildActions();
            BuildProfileCard();

            // Below the action column, which is four controls deep now that HELP sits between SETTINGS and QUIT:
            // PLAY 132..162, SETTINGS 168..192, HELP 198..222, QUIT 228..252. The status starts clear of all of them.
            _status = UiKit.Label(_root, string.Empty, new UiRect(36, 260, 300, UiText.Height(3)), 1,
                TextAnchor.UpperLeft, UiTheme.InkMuted, wrap: true);

            UiKit.ChromeBar(_root, ScreenLayout.Footer, "Footer", ruleAtBottom: false);
            _footer = UiKit.Label(_root, _prompts.Footer(),
                new UiRect(UiTheme.ScreenMargin, ScreenLayout.Footer.Y + 5, ScreenLayout.Footer.Width - UiTheme.ScreenMargin * 2, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.InkMuted);

            _input.Stack.Push(_menuList);
            Menu.Changed += OnMenuChanged;
        }

        /// <summary>The wordmark block: the title at an integer scale of the pixel face, over a rule and a subtitle.</summary>
        private void BuildIdentity()
        {
            const int x = 36;
            const int titleScale = 4;

            // A plate behind the wordmark so it reads against the tunnel whatever the backdrop does there.
            UiKit.Plate(_root, new UiRect(x - 10, 40, UiText.Width("RUINRAIL", titleScale) + 20, UiText.Height(1, titleScale) + 34),
                UiTheme.WithAlpha(UiTheme.NearBlack, 0.62f), "TitlePlate");

            UiKit.Label(_root, "RUINRAIL", new UiRect(x, 48, UiText.Width("RUINRAIL", titleScale), UiText.Height(1, titleScale)),
                titleScale, TextAnchor.UpperLeft, UiTheme.Ink);

            UiKit.Plate(_root, new UiRect(x, 48 + UiText.Height(1, titleScale) + 5, UiText.Width("RUINRAIL", titleScale), 1), UiTheme.Amber, "TitleRule");

            UiKit.Label(_root, "SHELTER  ·  EXPEDITION  ·  EXTRACTION",
                new UiRect(x, 48 + UiText.Height(1, titleScale) + 10, 280, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.InkMuted);
        }

        /// <summary>
        /// PLAY, SETTINGS, HELP and QUIT in a narrow column on the left, so the approach to the Shelter stays the
        /// picture. PLAY is the heaviest control and carries its own one-word state (NEW / CONTINUE) inside it.
        /// </summary>
        private void BuildActions()
        {
            _menuList = ScreenNavigation.MainMenu(Menu);

            const int x = 36;
            const int width = 150;
            var y = 132;

            foreach (var item in _menuList.Items)
            {
                var primary = item.Id == "menu." + MainMenuEntry.Play;
                var height = primary ? 30 : 24;
                var control = UiKit.Control(_root, _menuList, item, new UiRect(x, y, width, height),
                    primary ? ControlRole.Primary : ControlRole.Button,
                    i => { _menuList.Focus(i.Id); _menuList.ActivateFocused(); },
                    labelAnchor: TextAnchor.MiddleLeft,
                    labelScale: primary ? 2 : 1);
                _controls.Add(control);

                if (primary)
                {
                    // What PLAY will do with the save on disk, as a tag inside the button rather than a sentence beside it.
                    _playHint = UiKit.Label(control.transform, string.Empty,
                        new UiRect(width - 70, (height - UiText.Height()) / 2, 62, UiText.Height()), 1, TextAnchor.UpperRight, UiTheme.Amber);
                    _playHint.raycastTarget = false;
                }

                y += height + 6;
            }
        }

        /// <summary>
        /// The profile badge, bottom right, clear of the Shelter door the backdrop is built around: the survivor's
        /// name and level, then banked coins, deepest depth and equipped gear as icon/number pairs.
        ///
        /// It reads through <see cref="GameApp.ProbeSave"/>, the same snapshot the smoke run uses, so it can never
        /// disagree with what PLAY is about to load. With no save it is one tag, NEW SURVIVOR, instead of zeroes.
        /// </summary>
        private void BuildProfileCard()
        {
            var probe = _app.ProbeSave();
            const int right = 640 - 24;

            if (!probe.Success)
            {
                const string tag = "NEW SURVIVOR";
                var w = UiText.Width(tag) + 16;
                var badge = new UiRect(right - w, 306, w, UiText.Height() + 10);
                UiKit.Panel(_root, badge, "ProfileCard", UiTheme.WithAlpha(UiTheme.NearBlack, 0.8f), UiTheme.AmberDim);
                UiKit.Label(_root, tag, new UiRect(badge.X + 8, badge.Y + 5, w - 16, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Amber);
                return;
            }

            var open = probe.ExpeditionMarkerOpen;
            var card = new UiRect(right - 176, open ? 282 : 292, 176, open ? 54 : 44);
            UiKit.Panel(_root, card, "ProfileCard", UiTheme.WithAlpha(UiTheme.NearBlack, 0.8f), open ? UiTheme.Danger : UiTheme.PanelEdge);
            var inner = card.Inset(UiTheme.PadSmall + 2);

            // Line 1: who, and the level as a badge.
            var levelText = "LV " + LevelCurve.LevelForTotalXp(probe.TotalXp);
            var levelWidth = UiText.Width(levelText) + 8;
            var badgeRect = new UiRect(inner.Right - levelWidth, inner.Y - 1, levelWidth, UiText.Height() + 2);
            UiKit.Plate(_root, badgeRect, UiTheme.AmberDim, "LevelBadge");
            UiKit.Label(_root, levelText, new UiRect(badgeRect.X + 4, badgeRect.Y + 1, levelWidth - 8, UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
            var name = string.IsNullOrEmpty(probe.DisplayName) ? "unnamed" : probe.DisplayName;
            UiKit.Label(_root, UiText.Fit(name, inner.Width - levelWidth - 4), new UiRect(inner.X, inner.Y, inner.Width - levelWidth - 4, UiText.Height()),
                1, TextAnchor.UpperLeft, UiTheme.Ink);

            // Line 2: banked coins (coin icon), deepest depth, equipped gear — compact pairs, no sentences.
            var y = inner.Y + UiText.LineHeight + 6;
            var x = inner.X;
            var coin = UiSkin.Load()?.CoinIcon;
            if (coin != null)
            {
                var icon = UiKit.Plate(_root, new UiRect(x, y, 8, 8), Color.white, "CoinIcon");
                icon.sprite = coin;
                icon.preserveAspect = true;
                x += 11;
            }
            else
            {
                x = Pair(x, y, "C", string.Empty);
            }

            x = Pair(x, y, probe.BankedCoins.ToString(), string.Empty) + 8;
            x = Pair(x, y, "DEPTH ", probe.DeepestDepthReached > 0 ? probe.DeepestDepthReached.ToString() : "-") + 8;
            Pair(x, y, "GEAR ", probe.EquippedInstanceIds.Length.ToString());

            if (open)
                UiKit.Label(_root, "LAST EXPEDITION LOST", new UiRect(inner.X, y + UiText.LineHeight + 2, inner.Width, UiText.Height()),
                    1, TextAnchor.UpperLeft, UiTheme.Danger);
        }

        /// <summary>A muted key followed by an ink value on one line; returns the x after it.</summary>
        private int Pair(int x, int y, string key, string value)
        {
            if (key.Length > 0)
            {
                UiKit.Label(_root, key, new UiRect(x, y, UiText.Width(key), UiText.Height()), 1, TextAnchor.UpperLeft,
                    value.Length > 0 ? UiTheme.InkMuted : UiTheme.Ink);
                x += UiText.Width(key);
            }

            if (value.Length > 0)
            {
                UiKit.Label(_root, value, new UiRect(x, y, UiText.Width(value), UiText.Height()), 1, TextAnchor.UpperLeft, UiTheme.Ink);
                x += UiText.Width(value);
            }

            return x;
        }

        private void OnDestroy()
        {
            if (Menu != null) Menu.Changed -= OnMenuChanged;
        }

        private void Update()
        {
            _footer.text = _prompts.Footer();
            if (_playHint != null)
                _playHint.text = Menu.HasSave ? "CONTINUE" : "NEW";
        }

        private void OnMenuChanged(MainMenuViewModel menu)
        {
            _status.text = menu.Message;
            switch (menu.State)
            {
                case MainMenuState.Base:
                    _app.LoadScene(SceneNames.Base);
                    break;
                case MainMenuState.Settings:
                    ShowSettings();
                    break;
                case MainMenuState.Help:
                    ShowCodex();
                    break;
                case MainMenuState.Quitting:
                    _app.Quit();
                    break;
                case MainMenuState.SaveError:
                    _status.text = menu.Message + "  (Enter on PLAY again keeps the file; RESET requires confirmation)";
                    break;
            }
        }

        /// <summary>
        /// The Settings panel over the menu: the category root first, then one page per category (ui/90). The panel
        /// manages its own focus list on the shared stack; Back walks page → categories → menu.
        /// </summary>
        private void ShowSettings()
        {
            if (SettingsShowing) return;
            var settings = _app.SettingsScreen;
            settings.ResetToCategories();
            settings.CloseRequested = CloseSettings;
            _settings = SettingsPanel.Build(_root, settings, _input);
        }

        private void CloseSettings()
        {
            if (!SettingsShowing) return;
            var settings = _app.SettingsScreen;
            settings.BackFromPage(); // applies any page still open
            settings.ResetToCategories();
            foreach (var control in _settings.Controls) _controls.Remove(control);
            SettingsPanel.Close(_settings);
            _settings = null;
            Menu.BackToMenu();
        }

        /// <summary>
        /// The Help / Codex page over the menu. It is built over the live rebinder's glyphs, so the control names in
        /// the text are the ones actually bound right now rather than the defaults.
        /// </summary>
        private void ShowCodex()
        {
            if (CodexShowing) return;
            _codexModel ??= new RuinRail.UI.Codex.CodexViewModel(
                new RuinRail.UI.Onboarding.SchemeGlyphs(
                    RuinRail.Core.Input.ActiveInputDevice.Current == RuinRail.Core.Input.InputDeviceKind.Gamepad
                        ? RuinRail.UI.Onboarding.InputScheme.Gamepad
                        : RuinRail.UI.Onboarding.InputScheme.KeyboardMouse,
                    _app.SettingsRebinder),
                _app.Content != null ? _app.Content.AmmoBalance : null);
            _codexModel.CloseRequested = CloseCodex;
            _codexModel.Open();
            _codex = CodexPanel.Build(_root, _codexModel, _input);
        }

        private void CloseCodex()
        {
            if (!CodexShowing) return;
            foreach (var control in _codex.Controls) _controls.Remove(control);
            CodexPanel.Close(_codex);
            _codex = null;
            Menu.BackToMenu();
        }

        private void OnBack()
        {
            if (CodexShowing) { _codexModel.Close(); return; }
            if (!SettingsShowing) return;
            if (_app.SettingsScreen.BackFromPage()) return; // page -> categories
            CloseSettings();
        }

        /// <summary>Every interactive control, including the open Settings panel's live rows.</summary>
        public IReadOnlyList<UiControl> AllControls => _controls
            .Concat(_settings != null ? _settings.Controls : new List<UiControl>())
            .Concat(_codex != null ? _codex.Controls : new List<UiControl>())
            .Where(c => c != null).Distinct().ToList();
    }
}
