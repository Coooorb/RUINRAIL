using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Persistence;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The real Settings panel (ui/90) over the real view model, opened from the pause menu: category controls, one
    /// page per category with adjustable rows that show their value (sliders with a fill bar), pointer drag on a
    /// slider, keyboard/controller stepping through the one focus stack, the back-stack (page → categories → pause),
    /// gameplay input held while it is up, pixel font on every label, and no dead focus.
    /// </summary>
    public sealed class SettingsPanelTests
    {
        private readonly List<Object> _created = new();
        private FakePlayerInputReader _reader;
        private TimeScalePause _world;
        private PauseMenuViewModel _pause;
        private MenuInput _input;
        private PauseMenuScreen _screen;
        private SettingsViewModel _settings;
        private UserSettingsService _service;
        private PlayerInputReader _actions;
        private InputRebinder _rebinder;

        private sealed class NoApplier : ISettingsApplier
        {
            public int Applies;
            public IReadOnlyList<Vector2Int> AvailableResolutions => new[] { new Vector2Int(1280, 720) };
            public void Apply(SettingsData settings) => Applies++;
        }

        [SetUp]
        public void SetUp()
        {
            CursorService.Reset();
            CursorService.SetApplier(_ => true);
            CursorService.SetBase(CursorKind.Aim);
            GameplayInputGate.Reset();
            AudioLevels.Reset();
            ActiveBindingOverrides.Clear();
            _service = new UserSettingsService(new MemorySaveStore());
            _service.Load();
            _actions = new PlayerInputReader();
            _rebinder = new InputRebinder(_actions.Asset);
            _settings = new SettingsViewModel(_service, _rebinder, new NoApplier());
            _reader = new FakePlayerInputReader();
            _world = new TimeScalePause();
            _pause = new PauseMenuViewModel(_reader, _world, isCoop: false, _settings, null, null, () => true);
            var canvas = UiKit.Canvas("RunUi", 20);
            _created.Add(canvas.gameObject);
            _input = canvas.gameObject.AddComponent<MenuInput>();
            _input.KeyboardBackEnabled = false;
            _input.Back += () => { if (_pause.IsOpen) _pause.Back(); };
            _screen = PauseMenuScreen.Create(canvas.transform, _input, _pause);
        }

        [TearDown]
        public void TearDown()
        {
            _pause.Dispose();
            _settings.Dispose();
            _rebinder.Dispose();
            _actions.Dispose();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            Time.timeScale = 1f;
            CursorService.Reset();
            GameplayInputGate.Reset();
            AudioLevels.Reset();
            ActiveBindingOverrides.Clear();
        }

        private UiControl Control(string id) => _screen.Controls.First(c => c != null && c.Id == id);
        private SettingsPanelDriver Driver => _screen.SettingsInstance.Driver;

        private IEnumerator OpenSettings()
        {
            _pause.Open();
            yield return null;
            Control("pause.Settings").SimulateClick();
            yield return null;
            Assert.IsTrue(_screen.SettingsShowing);
        }

        [UnityTest]
        public IEnumerator FromPause_SettingsShowsTheCategories_EachOpensItsPage_AndBackWalksBack()
        {
            yield return OpenSettings();
            Assert.IsTrue(Driver.IsOnCategories);
            Assert.AreEqual("SETTINGS", Driver.TitleText);
            CollectionAssert.AreEqual(new[] { "settings.category.Video", "settings.category.Audio", "settings.category.Controls", "settings.category.Gameplay", "settings.defaults", "settings.back" },
                _screen.Controls.Where(c => c.Id.StartsWith("settings.")).Select(c => c.Id));
            Assert.AreSame(_screen.SettingsList, _input.Stack.Current, "the category list owns the input");
            Assert.AreEqual(2, _input.Stack.Depth);
            Assert.IsTrue(GameplayInputGate.IsHeld, "gameplay input is held under the pause/settings layer");

            foreach (var tab in SettingsViewModel.Tabs)
            {
                Control("settings.category." + tab).SimulateClick();
                yield return null;
                Assert.AreEqual(tab, Driver.Page, tab + " page opened");
                Assert.AreEqual("SETTINGS  ·  " + SettingsViewModel.CategoryLabel(tab), Driver.TitleText);
                Assert.AreEqual(2, _input.Stack.Depth, "the page replaced the categories on the stack (no stacked duplicates)");
                Assert.AreSame(_screen.SettingsList, _input.Stack.Current);
                Assert.IsNotNull(_input.Stack.Focused, "no dead focus on the page");
                Assert.IsTrue(_input.Stack.Focused.IsEnabled);
                Assert.Greater(Driver.RowViews.Count, 0);
                foreach (var row in Driver.RowViews.Where(v => v.gameObject.activeInHierarchy))
                    Assert.IsFalse(string.IsNullOrEmpty(row.Control.Id));
                _pause.Back();
                yield return null;
                Assert.IsTrue(Driver.IsOnCategories, "Back returns to the categories");
                Assert.AreEqual(PauseScreen.Settings, _pause.Screen);
            }

            _pause.Back();
            yield return null;
            Assert.IsFalse(_screen.SettingsShowing, "Back on the categories returns to the pause root");
            Assert.AreEqual(PauseScreen.Root, _pause.Screen);
            Assert.AreSame(_screen.RootList, _input.Stack.Current);
            Assert.AreEqual(1, _input.Stack.Depth);
            Assert.IsTrue(_pause.IsOpen);
        }

        [UnityTest]
        public IEnumerator AudioPage_ShowsSlidersWithValues_PointerAndKeyboardChangeEachIndependently_AndPersist()
        {
            yield return OpenSettings();
            Control("settings.category.Audio").SimulateClick();
            yield return null;
            var rows = Driver.RowViews.ToDictionary(v => v.Control.Id);
            CollectionAssert.AreEquivalent(new[] { "settings.audio.master", "settings.audio.music", "settings.audio.sfx", "settings.audio.ambience", "settings.audio.mute", "settings.back" }, rows.Keys);
            foreach (var id in new[] { "settings.audio.master", "settings.audio.music", "settings.audio.sfx", "settings.audio.ambience" })
            {
                Assert.IsTrue(rows[id].ShowsBar, id + " is a slider");
                Assert.AreEqual("100%", rows[id].ValueText);
                Assert.AreEqual(1f, rows[id].Fill, 1e-4f);
            }

            Assert.AreEqual("OFF", rows["settings.audio.mute"].ValueText);

            // Pointer: a press along the bar sets the value by position.
            rows["settings.audio.music"].SimulateFill(0.5f);
            yield return null;
            Assert.AreEqual(0.5f, _settings.Draft.Audio.MusicVolume, 1e-4f);
            Assert.AreEqual("50%", rows["settings.audio.music"].ValueText);
            Assert.AreEqual(0.5f, rows["settings.audio.music"].Fill, 1e-4f);
            Assert.AreEqual("settings.audio.music", _input.Stack.Focused.Id, "the pointer also focused the row");
            Assert.AreEqual(1f, _settings.Draft.Audio.MasterVolume, "master untouched");

            // Keyboard / controller: left/right on the focused row through the one stack.
            _input.Stack.Move(+1); // sfx
            Assert.AreEqual("settings.audio.sfx", _input.Stack.Focused.Id);
            Assert.IsTrue(_input.Stack.Adjust(-1) && _input.Stack.Adjust(-1) && _input.Stack.Adjust(-1) && _input.Stack.Adjust(-1));
            yield return null;
            Assert.AreEqual(0.8f, _settings.Draft.Audio.SfxVolume, 1e-4f);
            Assert.AreEqual("80%", rows["settings.audio.sfx"].ValueText);
            _input.Stack.Move(+1); // ambience
            for (var i = 0; i < 30; i++) _input.Stack.Adjust(-1);
            yield return null;
            Assert.AreEqual(0f, _settings.Draft.Audio.AmbienceVolume);
            Assert.AreEqual("0%", rows["settings.audio.ambience"].ValueText, "an explicit 0 is a valid mute of that bus");
            Assert.AreEqual(0.5f, _settings.Draft.Audio.MusicVolume, 1e-4f, "music untouched by the other sliders");
            Assert.AreEqual(0.5f, AudioLevels.Music, 1e-4f, "previewed at once");
            Assert.AreEqual(0f, AudioLevels.Ambience);

            _input.Stack.Move(+1); // mute
            Assert.IsTrue(_input.Stack.Activate());
            yield return null;
            Assert.AreEqual("ON", rows["settings.audio.mute"].ValueText);
            Assert.IsTrue(AudioLevels.Muted);

            _pause.Back();
            yield return null;
            var saved = _service.Current;
            Assert.AreEqual(0.5f, saved.Audio.MusicVolume, 1e-4f, "leaving the page persisted the mix");
            Assert.AreEqual(0.8f, saved.Audio.SfxVolume, 1e-4f);
            Assert.AreEqual(0f, saved.Audio.AmbienceVolume);
            Assert.IsTrue(saved.Audio.Mute);
        }

        [UnityTest]
        public IEnumerator VideoPage_ShowsCurrentValues_SelectorsStep_AndApplyIsOnlyOfferedForAChange()
        {
            yield return OpenSettings();
            Control("settings.category.Video").SimulateClick();
            yield return null;
            var rows = Driver.RowViews.ToDictionary(v => v.Control.Id);
            Assert.AreEqual("Fullscreen", rows["settings.video.display_mode"].ValueText);
            Assert.AreEqual("Native", rows["settings.video.resolution"].ValueText);
            Assert.AreEqual("ON", rows["settings.video.vsync"].ValueText);
            Assert.AreEqual("Unlimited", rows["settings.video.framerate"].ValueText);
            Assert.IsFalse(rows["settings.video.apply"].Control.IsEnabled, "nothing to apply");
            _input.Stack.Move(+3); // framerate
            Assert.AreEqual("settings.video.framerate", _input.Stack.Focused.Id);
            _input.Stack.Adjust(+2);
            yield return null;
            Assert.AreEqual("60 fps", rows["settings.video.framerate"].ValueText);
            rows["settings.video.display_mode"].Control.SimulateClick();
            yield return null;
            Assert.AreEqual("Windowed", rows["settings.video.display_mode"].ValueText);
            Assert.IsTrue(rows["settings.video.apply"].Control.IsEnabled, "a display change offers APPLY");
            Assert.IsFalse(rows["settings.video.revert"].Control.IsEnabled);
            rows["settings.video.apply"].Control.SimulateClick();
            yield return null;
            Assert.IsTrue(_settings.VideoConfirmPending);
            Assert.IsTrue(rows["settings.video.revert"].Control.IsEnabled);
            StringAssert.Contains("Keep these display settings", Driver.MessageText);
            rows["settings.video.revert"].Control.SimulateClick();
            yield return null;
            Assert.IsFalse(_settings.VideoConfirmPending);
            Assert.AreEqual("Fullscreen", rows["settings.video.display_mode"].ValueText, "reverted");
            _pause.Back();
            yield return null;
            Assert.AreEqual(60, _service.Current.Video.FrameRateLimit, "the safe edit persisted on Back");
            Assert.IsTrue(_service.Current.Video.Fullscreen);
        }

        [UnityTest]
        public IEnumerator ControlsPage_ListsRebindRows_SchemeSelectorSwitchesThem_AndScrollsByFocus()
        {
            yield return OpenSettings();
            Control("settings.category.Controls").SimulateClick();
            yield return null;
            var visibleIds = Driver.RowViews.Where(v => v.gameObject.activeInHierarchy).Select(v => v.Control.Id).ToList();
            Assert.AreEqual("settings.controls.scheme", visibleIds[0]);
            Assert.AreEqual("Keyboard & Mouse", Driver.RowViews[0].ValueText);
            Assert.IsTrue(visibleIds.Any(id => id.StartsWith("settings.rebind.Keyboard&Mouse.")), "rebind rows are real controls");
            var list = _screen.SettingsList;
            Assert.Greater(list.Items.Count, Driver.RowViews.Count, "the list is longer than the window: it scrolls by focus");
            for (var i = 0; i < list.Items.Count; i++) _input.Stack.Move(+1);
            yield return null;
            Assert.IsTrue(Driver.RowViews.Any(v => v.gameObject.activeInHierarchy && v.Control.Id == _input.Stack.Focused.Id), "the focused row is always drawn");
            _input.Stack.Current.Focus("settings.controls.scheme");
            _input.Stack.Adjust(+1);
            yield return null;
            Assert.AreEqual("Controller", Driver.RowViews[0].ValueText);
            Assert.IsTrue(Driver.RowViews.Where(v => v.gameObject.activeInHierarchy).Any(v => v.Control.Id.StartsWith("settings.rebind.Gamepad.")), "the controller bindings are listed");
            var dashRow = _input.Stack.Current.Items.First(i => i.Id.StartsWith("settings.rebind.Gamepad.Dash."));
            _input.Stack.Current.Focus(dashRow.Id);
            Assert.IsTrue(_input.Stack.Activate(), "Enter on a rebind row starts listening");
            Assert.IsTrue(_settings.IsListening);
            _settings.CancelRebind();
            Assert.IsFalse(_settings.IsListening);
            _pause.Back();
            yield return null;
            Assert.IsTrue(Driver.IsOnCategories);
        }

        [UnityTest]
        public IEnumerator EveryLabel_UsesThePixelFont_AndValuesNeverOverlapLabels()
        {
            yield return OpenSettings();
            foreach (var tab in SettingsViewModel.Tabs)
            {
                Control("settings.category." + tab).SimulateClick();
                yield return null;
                foreach (var text in _screen.SettingsInstance.Panel.GetComponentsInChildren<Text>(true)) Assert.AreSame(UiFont.Font(), text.font, text.name);
                foreach (var view in Driver.RowViews.Where(v => v.gameObject.activeInHierarchy))
                {
                    var label = view.Control.GetComponentInChildren<Text>();
                    var labelRight = UiTheme.PadSmall + 2 + UiText.Width(label.text);
                    var valueLeft = ((RectTransform)view.Control.transform).sizeDelta.x - SettingsPanel.ValueWidth - UiTheme.PadSmall;
                    Assert.LessOrEqual(labelRight, valueLeft, $"{view.Control.Id}: label '{label.text}' runs into its value");
                }

                _pause.Back();
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator EscUnderSettings_StepsBackAPage_ThenLeavesSettings_NeverIntoGameplay()
        {
            yield return OpenSettings();
            Control("settings.category.Gameplay").SimulateClick();
            yield return null;
            _reader.RaisePause();
            yield return null;
            Assert.IsTrue(_screen.SettingsShowing);
            Assert.IsTrue(Driver.IsOnCategories, "Esc on a page returns to the categories");
            _reader.RaisePause();
            yield return null;
            Assert.IsFalse(_screen.SettingsShowing);
            Assert.AreEqual(PauseScreen.Root, _pause.Screen, "Esc on the categories returns to the pause root — never straight into gameplay");
            Assert.IsTrue(GameplayInputGate.IsHeld);
            _reader.RaisePause();
            yield return null;
            Assert.IsFalse(_pause.IsOpen);
            Assert.IsFalse(GameplayInputGate.IsHeld, "released only when the pause menu itself closes");
        }
    }
}
