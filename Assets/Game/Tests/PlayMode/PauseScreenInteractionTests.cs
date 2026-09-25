using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Persistence;
using RuinRail.UI.Inventory;
using RuinRail.UI.Pause;
using RuinRail.UI.Settings;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The real pause screen over the real view model: four controls, mouse hover/click and pressed states, keyboard
    /// and controller stepping through one focus stack, Settings as a nested panel with Back to the pause root, the
    /// leave confirmations with CANCEL focused first, no stacked duplicate pause layers, and the cursor switching to
    /// the pointer while paused.
    /// </summary>
    public sealed class PauseScreenInteractionTests
    {
        private readonly List<Object> _created = new();
        private FakePlayerInputReader _reader;
        private TimeScalePause _world;
        private PauseMenuViewModel _pause;
        private MenuInput _input;
        private PauseMenuScreen _screen;
        private int _returns;
        private int _quits;

        private sealed class NoApplier : ISettingsApplier
        {
            public IReadOnlyList<Vector2Int> AvailableResolutions => new[] { new Vector2Int(640, 360) };
            public void Apply(SettingsData settings) { }
        }

        [SetUp]
        public void SetUp()
        {
            CursorService.Reset();
            CursorService.SetApplier(_ => true);
            CursorService.SetBase(CursorKind.Aim);
            var service = new UserSettingsService(new MemorySaveStore());
            service.Load();
            var settings = new SettingsViewModel(service, null, new NoApplier());
            _reader = new FakePlayerInputReader();
            _world = new TimeScalePause();
            _pause = new PauseMenuViewModel(_reader, _world, isCoop: false, settings, () => _quits++, () => _returns++, () => true);
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
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            Time.timeScale = 1f;
            CursorService.Reset();
        }

        private UiControl Control(string id) => _screen.Controls.First(c => c.Id == id);

        [Test]
        public void Opening_ShowsEveryRealControl_PutsOneListOnTheStack_AndTakesThePointerCursor()
        {
            Assert.IsFalse(_screen.IsShowing);
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);
            _reader.RaisePause();
            Assert.IsTrue(_screen.IsShowing);
            Assert.AreEqual("PAUSED", _screen.TitleText);
            // HELP joined the root between SETTINGS and the two destructive entries.
            CollectionAssert.AreEqual(new[] { "pause.Resume", "pause.Settings", "pause.Help", "pause.ReturnToMainMenu", "pause.QuitGame" },
                _screen.Controls.Where(c => c.Id.StartsWith("pause.") && !c.Id.StartsWith("pause.confirm")).Select(c => c.Id));
            Assert.AreSame(_screen.RootList, _input.Stack.Current);
            Assert.AreEqual(1, _input.Stack.Depth, "one pause layer");
            Assert.AreEqual("pause.Resume", _input.Stack.Focused.Id, "RESUME has the focus first");
            Assert.IsTrue(Control("pause.Resume").ShowsFocusBrackets, "focus is visible by shape");
            Assert.AreEqual(0f, Time.timeScale, "solo: the world is held");

            // The scene owner pushes the pointer while a menu layer is over gameplay.
            CursorService.PushOverlay();
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);

            _reader.RaisePause();
            Assert.IsFalse(_screen.IsShowing);
            Assert.AreEqual(0, _input.Stack.Depth, "the layer left the stack: no stale pause list");
            Assert.AreEqual(1f, Time.timeScale);
            CursorService.PopOverlay();
            Assert.AreEqual(CursorKind.Aim, CursorService.Current, "the aim cursor comes back");

            _reader.RaisePause();
            _reader.RaisePause();
            _reader.RaisePause();
            Assert.AreEqual(1, _input.Stack.Depth, "reopening never stacks a duplicate pause screen");
        }

        [Test]
        public void Mouse_HoverIsDistinct_PressIsInset_AndClickResumes()
        {
            _pause.Open();
            var resume = Control("pause.Resume");
            resume.SimulateHover(true);
            Assert.IsTrue(resume.IsHovered);
            Assert.IsTrue(CursorService.Hovering, "the hover cursor variant is requested over a control");
            resume.SimulatePress(true);
            Assert.IsTrue(resume.IsPressed);
            Assert.AreEqual(ControlState.Pressed, resume.State);
            resume.SimulatePress(false);
            resume.SimulateHover(false);
            Assert.IsFalse(CursorService.Hovering);

            resume.SimulateClick();
            Assert.IsFalse(_pause.IsOpen, "a click on RESUME resumes");
            Assert.AreEqual(1, resume.PointerActivations);
        }

        [Test]
        public void Keyboard_StepsAndActivates_ThroughTheFocusStack()
        {
            _pause.Open();
            _input.Stack.Move(+1);
            Assert.AreEqual("pause.Settings", _input.Stack.Focused.Id);
            _input.Stack.Move(+1);
            Assert.AreEqual("pause.Help", _input.Stack.Focused.Id);
            _input.Stack.Move(+1);
            _input.Stack.Move(+1);
            Assert.AreEqual("pause.QuitGame", _input.Stack.Focused.Id);
            _input.Stack.Move(-4);
            Assert.AreEqual("pause.Resume", _input.Stack.Focused.Id);
            _input.Stack.Activate();
            Assert.IsFalse(_pause.IsOpen, "Enter / A on RESUME resumes");
        }

        [Test]
        public void Settings_IsANestedPanel_BackReturnsToThePauseRoot()
        {
            _pause.Open();
            Control("pause.Settings").SimulateClick();
            Assert.AreEqual(PauseScreen.Settings, _pause.Screen);
            Assert.IsTrue(_screen.SettingsShowing);
            Assert.AreSame(_screen.SettingsList, _input.Stack.Current, "the settings list owns the input");
            Assert.AreEqual(2, _input.Stack.Depth);
            Assert.Greater(_screen.Controls.Count(c => c.Id.StartsWith("settings.")), 5, "settings rows are real controls");

            _pause.Back();
            Assert.AreEqual(PauseScreen.Root, _pause.Screen);
            Assert.IsFalse(_screen.SettingsShowing, "the panel is gone");
            Assert.AreSame(_screen.RootList, _input.Stack.Current);
            Assert.AreEqual(1, _input.Stack.Depth);
            Assert.IsTrue(_pause.IsOpen, "back from settings lands on pause, not gameplay");
        }

        [Test]
        public void ReturnToMainMenu_ShowsTheConfirmation_WithCancelFocused_AndConfirmRunsTheLeavePathOnce()
        {
            _pause.Open();
            Control("pause.ReturnToMainMenu").SimulateClick();
            Assert.IsTrue(_screen.ConfirmationShowing);
            Assert.AreSame(_screen.ConfirmList, _input.Stack.Current);
            Assert.AreEqual("pause.confirm.cancel", _input.Stack.Focused.Id, "the safe choice has the focus");
            StringAssert.Contains("counts as failed", _screen.ConfirmationText);
            Assert.AreEqual(0, _returns);

            // Controller: step to CONFIRM and press A.
            _input.Stack.Move(+1);
            Assert.AreEqual("pause.confirm.yes", _input.Stack.Focused.Id);
            _input.Stack.Activate();
            Assert.AreEqual(1, _returns);
            Assert.IsFalse(_pause.IsOpen);
            Assert.AreEqual(0, _input.Stack.Depth);
            Assert.AreEqual(1f, Time.timeScale, "the world pause is released before leaving");
        }

        [Test]
        public void QuitGame_ConfirmationCancelsWithPauseOrBack_AndConfirmsByClick()
        {
            _pause.Open();
            Control("pause.QuitGame").SimulateClick();
            Assert.IsTrue(_screen.ConfirmationShowing);
            _reader.RaisePause();
            Assert.IsFalse(_screen.ConfirmationShowing, "Pause while confirming backs out");
            Assert.IsTrue(_pause.IsOpen);
            Assert.AreSame(_screen.RootList, _input.Stack.Current);

            Control("pause.QuitGame").SimulateClick();
            Control("pause.confirm.yes").SimulateClick();
            Assert.AreEqual(1, _quits);
        }

        [UnityTest]
        public IEnumerator EveryPauseLabel_FitsItsControl_InsideTheReferenceFrame()
        {
            _pause.Open();
            yield return null;
            foreach (var control in _screen.Controls.Where(c => c.gameObject.activeInHierarchy))
            {
                var label = control.GetComponentInChildren<Text>();
                Assert.IsNotNull(label, control.Id);
                Assert.IsFalse(label.text.EndsWith("…"), $"{control.Id}: label '{label.text}' is not truncated");
                var rect = (RectTransform)control.transform;
                Assert.GreaterOrEqual(rect.sizeDelta.y, 24f, $"{control.Id}: button height at least 24 px (spec 18.4)");
            }
        }
    }
}
