using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// The category-based Settings (ui/90): VIDEO / AUDIO / CONTROLS / GAMEPLAY pages over one view model — every
    /// page's rows are real adjustable controls; audio levels change independently, preview at once and persist
    /// (an explicit 0 stays a mute); display-mode / resolution changes apply provisionally and KEEP or revert; the
    /// frame-rate limit and VSync persist; CONTROLS switches scheme and rebinds; Back walks page → categories → owner.
    /// </summary>
    public sealed class SettingsPagesTests
    {
        private sealed class RecordingApplier : ISettingsApplier
        {
            public readonly List<SettingsData> Applied = new();
            public IReadOnlyList<Vector2Int> AvailableResolutions { get; } = new[] { new Vector2Int(1280, 720), new Vector2Int(1920, 1080) };
            public void Apply(SettingsData settings) => Applied.Add(JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(settings)));
            public SettingsData Last => Applied.Count > 0 ? Applied[^1] : null;
        }

        private MemorySaveStore _store;
        private UserSettingsService _service;
        private RecordingApplier _applier;
        private SettingsViewModel _vm;
        private PlayerInputReader _reader;
        private InputRebinder _rebinder;

        [SetUp]
        public void SetUp()
        {
            ActiveBindingOverrides.Clear();
            AudioLevels.Reset();
            _store = new MemorySaveStore();
            _service = new UserSettingsService(_store);
            _service.Load();
            _applier = new RecordingApplier();
            _reader = new PlayerInputReader();
            _rebinder = new InputRebinder(_reader.Asset);
            _vm = new SettingsViewModel(_service, _rebinder, _applier);
        }

        [TearDown]
        public void TearDown()
        {
            _vm.Dispose();
            _rebinder.Dispose();
            _reader.Dispose();
            ActiveBindingOverrides.Clear();
            AudioLevels.Reset();
        }

        [Test]
        public void Root_ListsTheFourRealCategories_AndEachOpensItsOwnPage()
        {
            Assert.IsTrue(_vm.IsOnCategories);
            CollectionAssert.AreEqual(new[] { SettingsTab.Video, SettingsTab.Audio, SettingsTab.Controls, SettingsTab.Gameplay }, SettingsViewModel.Tabs);
            var root = ScreenNavigation.Settings(_vm);
            CollectionAssert.AreEqual(new[] { "settings.category.Video", "settings.category.Audio", "settings.category.Controls", "settings.category.Gameplay", "settings.defaults", "settings.back" }, root.Items.Select(i => i.Id));
            foreach (var tab in SettingsViewModel.Tabs)
            {
                root.Focus("settings.category." + tab);
                Assert.IsTrue(root.ActivateFocused());
                Assert.AreEqual(tab, _vm.Page, "selecting a category opens its page");
                Assert.IsFalse(_vm.IsOnCategories);
                Assert.Greater(_vm.RowsFor(tab).Count, 0, tab + " has rows");
                Assert.IsTrue(_vm.RowsFor(tab).All(r => r.Activate != null || r.Adjust != null), tab + ": every row can be operated");
                Assert.IsTrue(_vm.BackFromPage(), "Back returns to the categories");
                Assert.IsTrue(_vm.IsOnCategories);
            }

            Assert.IsFalse(_vm.BackFromPage(), "Back on the categories is the owner's to handle (close Settings)");
            Assert.AreEqual(4, _vm.PageOpens);
        }

        [Test]
        public void AudioPage_HasFourVolumeSliders_EachChangesIndependently_PreviewsAtOnce_AndPersistsOnBack()
        {
            _vm.OpenPage(SettingsTab.Audio);
            var rows = _vm.RowsFor(SettingsTab.Audio);
            CollectionAssert.AreEqual(new[] { "settings.audio.master", "settings.audio.music", "settings.audio.sfx", "settings.audio.ambience", "settings.audio.mute" }, rows.Select(r => r.Id));
            Assert.AreEqual(4, rows.Count(r => r.Kind == SettingsControlKind.Slider));
            rows[0].Adjust(-4); // master 100 -> 80
            rows[1].Adjust(-10); // music 100 -> 50
            rows[2].SetFill(0.3f); // sfx by pointer
            rows[3].Adjust(-20); // ambience 100 -> 0 (explicit mute of that bus)
            Assert.AreEqual(0.8f, _vm.Draft.Audio.MasterVolume, 1e-4f);
            Assert.AreEqual(0.5f, _vm.Draft.Audio.MusicVolume, 1e-4f);
            Assert.AreEqual(0.3f, _vm.Draft.Audio.SfxVolume, 1e-4f);
            Assert.AreEqual(0f, _vm.Draft.Audio.AmbienceVolume, 1e-4f);
            Assert.AreEqual("80%", rows[0].ValueText());
            Assert.AreEqual("50%", rows[1].ValueText());
            Assert.AreEqual("30%", rows[2].ValueText());
            Assert.AreEqual("0%", rows[3].ValueText());
            Assert.AreEqual(0.8f, rows[0].Fill(), 1e-4f);
            // Preview: the audio layer hears the draft before anything is saved.
            Assert.AreEqual(0.8f, AudioLevels.Master, 1e-4f);
            Assert.AreEqual(0.5f, AudioLevels.Music, 1e-4f);
            Assert.AreEqual(0.3f, AudioLevels.Sfx, 1e-4f);
            Assert.AreEqual(0f, AudioLevels.Ambience, 1e-4f);
            Assert.AreEqual(0f, AudioLevels.AmbienceGain, 1e-4f, "ambience at 0 is silent on its own bus");
            Assert.AreEqual(0.8f * 0.3f, AudioLevels.GainFor(false), 1e-4f, "sfx unaffected by the ambience level");
            Assert.AreEqual(1f, _service.Current.Audio.MasterVolume, "not persisted until Back/Apply");

            Assert.IsTrue(_vm.BackFromPage());
            var reloaded = new UserSettingsService(_store).Load();
            Assert.AreEqual(0.8f, reloaded.Audio.MasterVolume, 1e-4f);
            Assert.AreEqual(0.5f, reloaded.Audio.MusicVolume, 1e-4f);
            Assert.AreEqual(0.3f, reloaded.Audio.SfxVolume, 1e-4f);
            Assert.AreEqual(0f, reloaded.Audio.AmbienceVolume, 1e-4f, "an explicit 0 survives restart as a mute");
            Assert.IsFalse(reloaded.Audio.Mute);
        }

        [Test]
        public void ExplicitZeroMaster_PersistsAsMute_AndFreshDefaultsStayAudible()
        {
            Assert.AreEqual(1f, SettingsData.Defaults().Audio.MasterVolume);
            Assert.AreEqual(1f, SettingsData.Defaults().Audio.AmbienceVolume);
            _vm.OpenPage(SettingsTab.Audio);
            _vm.AdjustMasterVolume(-25);
            Assert.AreEqual(0f, _vm.Draft.Audio.MasterVolume);
            _vm.SetMute(true);
            _vm.BackFromPage();
            var reloaded = new UserSettingsService(_store).Load();
            Assert.AreEqual(0f, reloaded.Audio.MasterVolume);
            Assert.IsTrue(reloaded.Audio.Mute);
            Assert.IsTrue(AudioLevels.Muted);
            Assert.AreEqual(0f, AudioLevels.GainFor(true));
            _vm.ResetToDefaults();
            Assert.AreEqual(1f, AudioLevels.Master);
            Assert.IsFalse(AudioLevels.Muted);
            Assert.AreEqual(1f, AudioLevels.Ambience);
        }

        [Test]
        public void Discard_RestoresThePreviewedMixToThePersistedOne()
        {
            _vm.OpenPage(SettingsTab.Audio);
            _vm.SetMusicVolume(0.2f);
            Assert.AreEqual(0.2f, AudioLevels.Music, 1e-4f);
            _vm.Discard();
            Assert.AreEqual(1f, AudioLevels.Music, 1e-4f);
            Assert.AreEqual(1f, _vm.Draft.Audio.MusicVolume);
        }

        [Test]
        public void VideoPage_VSyncAndFrameRate_PersistOnBack_WithoutAConfirmation()
        {
            _vm.OpenPage(SettingsTab.Video);
            var rows = _vm.RowsFor(SettingsTab.Video).ToDictionary(r => r.Id);
            Assert.AreEqual("ON", rows["settings.video.vsync"].ValueText());
            rows["settings.video.vsync"].Activate();
            Assert.AreEqual("OFF", rows["settings.video.vsync"].ValueText());
            Assert.AreEqual("Unlimited", rows["settings.video.framerate"].ValueText());
            rows["settings.video.framerate"].Adjust(+2);
            Assert.AreEqual(60, _vm.Draft.Video.FrameRateLimit);
            Assert.AreEqual("60 fps", rows["settings.video.framerate"].ValueText());
            rows["settings.video.framerate"].Adjust(-3);
            Assert.AreEqual(240, _vm.Draft.Video.FrameRateLimit, "the selector wraps");
            Assert.IsFalse(_vm.VideoConfirmPending);
            Assert.IsTrue(_vm.BackFromPage());
            var reloaded = new UserSettingsService(_store).Load();
            Assert.IsFalse(reloaded.Video.VSync);
            Assert.AreEqual(240, reloaded.Video.FrameRateLimit);
            Assert.AreEqual(240, _applier.Last.Video.FrameRateLimit, "applied to the engine");
        }

        [Test]
        public void DisplayModeAndResolution_ApplyProvisionally_KeepPersists_TimeoutOrRevertRestores()
        {
            _vm.OpenPage(SettingsTab.Video);
            var rows = _vm.RowsFor(SettingsTab.Video).ToDictionary(r => r.Id);
            Assert.AreEqual("Fullscreen", rows["settings.video.display_mode"].ValueText());
            Assert.AreEqual("Native", rows["settings.video.resolution"].ValueText());
            Assert.IsFalse(rows["settings.video.apply"].IsEnabled, "nothing to apply yet");
            rows["settings.video.display_mode"].Activate();
            rows["settings.video.resolution"].Adjust(+1);
            Assert.AreEqual("Windowed", rows["settings.video.display_mode"].ValueText());
            Assert.AreEqual("1280 x 720", rows["settings.video.resolution"].ValueText());
            Assert.IsTrue(rows["settings.video.apply"].IsEnabled);
            Assert.AreEqual(0, _applier.Applied.Count, "a display change never reaches the engine until APPLY");

            // Apply → provisional: the engine runs it, the file does not have it yet, the timer runs.
            rows["settings.video.apply"].Activate();
            Assert.IsTrue(_vm.VideoConfirmPending);
            Assert.AreEqual(1, _applier.Applied.Count);
            Assert.IsFalse(_applier.Last.Video.Fullscreen);
            Assert.AreEqual(1280, _applier.Last.Video.ResolutionWidth);
            Assert.IsTrue(_service.Current.Video.Fullscreen, "not persisted while pending");
            StringAssert.Contains("Keep these display settings", _vm.Message);
            Assert.IsTrue(rows["settings.video.revert"].IsEnabled);

            // Timeout → revert to the previous values, engine and draft alike.
            _vm.Tick(SettingsViewModel.VideoConfirmSeconds + 0.1f);
            Assert.IsFalse(_vm.VideoConfirmPending);
            Assert.AreEqual(1, _vm.VideoReverts);
            Assert.IsTrue(_applier.Last.Video.Fullscreen, "engine back to fullscreen");
            Assert.AreEqual(0, _applier.Last.Video.ResolutionWidth);
            Assert.IsTrue(_vm.Draft.Video.Fullscreen);
            Assert.AreEqual("Native", _vm.ResolutionText);

            // Apply again and KEEP → persisted.
            _vm.SetFullscreen(false);
            _vm.ApplyVideo();
            Assert.IsTrue(_vm.VideoConfirmPending);
            _vm.Tick(3f);
            StringAssert.Contains("Reverting in", _vm.Message);
            _vm.ConfirmVideo();
            Assert.IsFalse(_vm.VideoConfirmPending);
            Assert.IsFalse(new UserSettingsService(_store).Load().Video.Fullscreen, "KEEP persisted the windowed mode");
            Assert.IsFalse(_applier.Last.Video.Fullscreen);

            // REVERT row while pending; and leaving the page with a pending change reverts too.
            _vm.SetFullscreen(true);
            _vm.ApplyVideo();
            rows = _vm.RowsFor(SettingsTab.Video).ToDictionary(r => r.Id);
            rows["settings.video.revert"].Activate();
            Assert.IsFalse(_vm.VideoConfirmPending);
            Assert.IsFalse(_applier.Last.Video.Fullscreen);
            _vm.SetFullscreen(true);
            _vm.ApplyVideo();
            Assert.IsTrue(_vm.BackFromPage());
            Assert.IsFalse(_vm.VideoConfirmPending);
            Assert.IsFalse(new UserSettingsService(_store).Load().Video.Fullscreen, "Back never persists an unconfirmed display change");
            Assert.IsFalse(_applier.Last.Video.Fullscreen);
        }

        [Test]
        public void LeavingTheVideoPage_WithAnUnappliedDisplayEdit_DropsIt()
        {
            _vm.OpenPage(SettingsTab.Video);
            _vm.SetFullscreen(false);
            _vm.SetVSync(false);
            Assert.IsTrue(_vm.BackFromPage());
            var reloaded = new UserSettingsService(_store).Load();
            Assert.IsTrue(reloaded.Video.Fullscreen, "an unapplied display-mode edit is dropped, not silently applied");
            Assert.IsFalse(reloaded.Video.VSync, "the safe edit persists");
        }

        [Test]
        public void ControlsPage_SwitchesScheme_ListsRebindRows_RebindsThroughTheExistingRebinder_AndResets()
        {
            _vm.OpenPage(SettingsTab.Controls);
            var rows = _vm.RowsFor(SettingsTab.Controls);
            Assert.AreEqual("settings.controls.scheme", rows[0].Id);
            Assert.AreEqual("Keyboard & Mouse", rows[0].ValueText());
            var keyboardRows = rows.Count(r => r.Id.StartsWith("settings.rebind.Keyboard&Mouse."));
            Assert.Greater(keyboardRows, 5, "the keyboard/mouse bindings are listed");
            Assert.IsTrue(rows.Any(r => r.Id.StartsWith("settings.rebind.Keyboard&Mouse.Dash.") && r.IsEnabled));
            Assert.IsTrue(rows.Any(r => r.Id.StartsWith("settings.rebind.Keyboard&Mouse.Pause.") && !r.IsEnabled), "Pause is fixed");
            rows[0].Adjust(+1);
            Assert.AreEqual(InputRebinder.GamepadScheme, _vm.Scheme);
            rows = _vm.RowsFor(SettingsTab.Controls);
            Assert.AreEqual("Controller", rows[0].ValueText());
            Assert.Greater(rows.Count(r => r.Id.StartsWith("settings.rebind.Gamepad.")), 5, "the controller bindings are listed");
            Assert.AreEqual(0, rows.Count(r => r.Id.StartsWith("settings.rebind.Keyboard&Mouse.")));
            rows[0].Adjust(+1);
            Assert.AreEqual(InputRebinder.KeyboardMouseScheme, _vm.Scheme, "the selector wraps");

            var dash = _rebinder.Find("Dash", InputRebinder.KeyboardMouseScheme);
            Assert.IsTrue(_vm.TryBind(dash, "<Keyboard>/leftShift").Changed);
            Assert.IsTrue(_vm.IsDirty);
            Assert.IsTrue(_vm.BackFromPage(), "Back applies the rebind");
            StringAssert.Contains("leftShift", ActiveBindingOverrides.Json);
            StringAssert.Contains("leftShift", new UserSettingsService(_store).Load().Controls.BindingOverridesJson);
            _vm.OpenPage(SettingsTab.Controls);
            _vm.RowsFor(SettingsTab.Controls).First(r => r.Id == "settings.reset_bindings").Activate();
            Assert.IsFalse(_rebinder.HasOverrides);
            _vm.BackFromPage();
            Assert.AreEqual(string.Empty, ActiveBindingOverrides.Json);
        }

        [Test]
        public void GameplayPage_TogglesPersist_AndShakeIntensitySlidesOnlyWhileShakeIsOn()
        {
            _vm.OpenPage(SettingsTab.Gameplay);
            var rows = _vm.RowsFor(SettingsTab.Gameplay).ToDictionary(r => r.Id);
            // AIM ASSIST joined the GAMEPLAY page (default ON); every existing row is still here.
            CollectionAssert.AreEquivalent(new[] { "settings.aim_assist", "settings.shake", "settings.shake.intensity", "settings.damage_numbers", "settings.hit_flash", "settings.tutorials", "settings.tutorials.reset" }, rows.Keys);
            rows["settings.shake.intensity"].Adjust(-3);
            Assert.AreEqual(0.7f, _vm.Draft.Accessibility.ScreenShakeIntensity, 1e-4f);
            Assert.AreEqual("70%", rows["settings.shake.intensity"].ValueText());
            rows["settings.shake"].Activate();
            Assert.IsFalse(_vm.Draft.Accessibility.ScreenShake);
            Assert.IsFalse(rows["settings.shake.intensity"].IsEnabled, "intensity is inert while shake is off");
            rows["settings.damage_numbers"].Activate();
            rows["settings.hit_flash"].Adjust(+1);
            rows["settings.tutorials"].Activate();
            Assert.IsFalse(rows["settings.tutorials.reset"].IsEnabled, "no profile open");
            _vm.BackFromPage();
            var reloaded = new UserSettingsService(_store).Load();
            Assert.IsFalse(reloaded.Accessibility.ScreenShake);
            Assert.AreEqual(0.7f, reloaded.Accessibility.ScreenShakeIntensity, 1e-4f);
            Assert.IsFalse(reloaded.Accessibility.DamageNumbers);
            Assert.IsFalse(reloaded.Accessibility.HitFlash);
            Assert.IsFalse(reloaded.Tutorial.ShowPrompts);
            Assert.AreEqual(0f, FeedbackPreferences.ScreenShakeIntensity);
            Assert.IsFalse(FeedbackPreferences.DamageNumbers);
        }

        [Test]
        public void FocusList_RoutesLeftRightToTheFocusedRow_AndOnlyToAdjustableRows()
        {
            _vm.OpenPage(SettingsTab.Audio);
            var stack = new FocusStack();
            stack.Push(ScreenNavigation.SettingsPage(_vm, SettingsTab.Audio));
            Assert.AreEqual("settings.audio.master", stack.Focused.Id);
            Assert.IsTrue(stack.Adjust(-1), "left steps the focused slider");
            Assert.AreEqual(0.95f, _vm.Draft.Audio.MasterVolume, 1e-4f);
            Assert.IsTrue(stack.Adjust(+1));
            Assert.AreEqual(1f, _vm.Draft.Audio.MasterVolume, 1e-4f);
            stack.Move(+4); // mute
            Assert.AreEqual("settings.audio.mute", stack.Focused.Id);
            Assert.IsTrue(stack.Adjust(+1), "a toggle also answers left/right");
            Assert.IsTrue(_vm.Draft.Audio.Mute);
            stack.Move(+1); // back
            Assert.AreEqual("settings.back", stack.Focused.Id);
            Assert.IsFalse(stack.Adjust(+1), "a plain button is not adjustable: the step falls through to the screen");
            Assert.IsTrue(stack.Activate());
            Assert.IsTrue(_vm.IsOnCategories, "BACK on a page returns to the categories");
        }

        [Test]
        public void PauseMenu_BackWalksPageThenCategoriesThenPauseRoot_AndAppliesOnTheWay()
        {
            using var pause = new PauseMenuViewModel(null, new TimeScalePause(), isCoop: true, _vm);
            pause.Open();
            pause.Activate(PauseMenuItem.Settings);
            Assert.AreEqual(PauseScreen.Settings, pause.Screen);
            Assert.IsTrue(_vm.IsOnCategories, "Settings opens on its categories");
            _vm.OpenPage(SettingsTab.Audio);
            _vm.SetSfxVolume(0.4f);
            pause.Back();
            Assert.AreEqual(PauseScreen.Settings, pause.Screen, "page -> categories keeps Settings open");
            Assert.IsTrue(_vm.IsOnCategories);
            Assert.AreEqual(0.4f, _service.Current.Audio.SfxVolume, 1e-4f, "leaving the page applied it");
            pause.Back();
            Assert.AreEqual(PauseScreen.Root, pause.Screen, "categories -> pause root");
            pause.Back();
            Assert.IsFalse(pause.IsOpen, "root -> resume");

            // The root's BACK control asks the owner to close: the pause menu returns to its root.
            pause.Open();
            pause.Activate(PauseMenuItem.Settings);
            _vm.RequestClose();
            Assert.AreEqual(PauseScreen.Root, pause.Screen);
            // Esc (the Pause action) while on a page also steps back a page first.
            pause.Activate(PauseMenuItem.Settings);
            _vm.OpenPage(SettingsTab.Gameplay);
            pause.HandlePauseInput();
            Assert.AreEqual(PauseScreen.Settings, pause.Screen);
            Assert.IsTrue(_vm.IsOnCategories);
            pause.HandlePauseInput();
            Assert.AreEqual(PauseScreen.Root, pause.Screen);
        }

        [Test]
        public void SettingsDocument_RoundTripsAmbienceAndFrameRate_AndOldDocumentsGetDefaults()
        {
            var data = SettingsData.Defaults();
            data.Audio.AmbienceVolume = 0.35f;
            data.Video.FrameRateLimit = 120;
            var json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<SettingsData>(json);
            Assert.AreEqual(0.35f, back.Audio.AmbienceVolume, 1e-4f);
            Assert.AreEqual(120, back.Video.FrameRateLimit);
            var old = JsonUtility.FromJson<SettingsData>("{\"SettingsVersion\":1,\"Audio\":{\"MasterVolume\":0.5,\"MusicVolume\":1,\"SfxVolume\":1,\"Mute\":false}}");
            Assert.AreEqual(1f, old.Audio.AmbienceVolume, "a document written before the ambience level exists stays audible");
            Assert.AreEqual(0, old.Video.FrameRateLimit, "and unlimited");
        }
    }
}
