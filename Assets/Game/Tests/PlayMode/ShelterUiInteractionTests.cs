using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// UI polish pass, section A9 — the front-end is driven the way a player drives it.
    ///
    /// These tests exist because the previous menus were mouse-dead in a way no unit test could have noticed: the
    /// controls carried uGUI Buttons, the Buttons carried click listeners, and the project had no EventSystem
    /// anywhere, so not one of those listeners could ever fire. "A button technically receives click events" is
    /// exactly the self-deception the polish pass warns about, so the assertions below go through the real pointer
    /// handlers on the real screens and check the view models actually moved.
    /// </summary>
    public sealed class ShelterUiInteractionTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_ui_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var stash in Object.FindObjectsByType<RuinRail.UI.Inventory.StashView>(FindObjectsSortMode.None)) Object.DestroyImmediate(stash.gameObject);
            foreach (var run in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(run.gameObject);
            Time.timeScale = 1f;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private IEnumerator OpenShelter()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            yield return null;
        }

        private static UiControl Control(BaseHubScreen hub, string id) =>
            hub.Controls.FirstOrDefault(c => c.Id == id);

        // ---------------- the pointer works at all ----------------

        [UnityTest]
        public IEnumerator EventSystem_Exists_SoPointerInputIsPossible()
        {
            yield return OpenShelter();

            Assert.IsNotNull(EventSystem.current, "Without an EventSystem uGUI performs no raycasts and no menu accepts a click.");
            Assert.AreEqual(1, Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length,
                "Exactly one EventSystem; a second would double every pointer event.");
            Assert.IsNotNull(EventSystem.current.currentInputModule, "The event system needs an input module to read the pointer.");

            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                Assert.IsNotNull(canvas.GetComponent<GraphicRaycaster>(), $"{canvas.name} cannot be clicked without a raycaster.");
            Assert.Greater(hub.Controls.Count, 0);
        }

        [UnityTest]
        public IEnumerator MouseClick_DoesExactlyWhatConfirmDoes()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // Pointer: click the CHARACTER tab.
            var character = Control(hub, "station." + BaseStation.Character);
            Assert.IsNotNull(character, "The CHARACTER tab must exist as a clickable control.");
            character.SimulateClick();
            Assert.AreEqual(BaseStation.Character, hub.Hub.Current, "A click opens the station.");
            Assert.AreEqual(1, character.PointerActivations, "One click is one activation, never two.");

            // Keyboard: the same control through the focus list. The open panel owns the focus stack while it is up,
            // so the tab bar gets it back the way the player would — by backing out of the station first.
            hub.Hub.Close();
            yield return null;
            Assert.AreSame(hub.StationList, hub.Input.Stack.Current, "Closing a station returns focus to the tab bar.");

            var opensAfterClick = hub.Hub.Opens;
            hub.StationList.Focus("station." + BaseStation.Workshop);
            hub.Input.Stack.Activate();
            Assert.AreEqual(BaseStation.Workshop, hub.Hub.Current, "Confirm opens the station the focus is on.");
            Assert.AreEqual(opensAfterClick + 1, hub.Hub.Opens, "Confirm performs one activation, the same as a click.");

            // And back again with the pointer, proving the two coexist rather than fighting.
            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            Assert.AreEqual(BaseStation.Storage, hub.Hub.Current);
        }

        [UnityTest]
        public IEnumerator HorizontalSteps_SwitchSection_EvenWhileAStationPanelOwnsTheFocus()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Assert.AreEqual(BaseStation.Storage, hub.Hub.Current);
            Assert.AreNotSame(hub.StationList, hub.Input.Stack.Current, "The open station panel owns the vertical focus.");

            // A horizontal step must still move along the tab bar; otherwise the bar is keyboard-inert exactly when
            // the player is moving between sections.
            hub.StepSection(+1);
            yield return null;

            Assert.AreNotEqual(BaseStation.Storage, hub.Hub.Current, "A horizontal step switched section without a Back first.");
            Assert.AreEqual(StationOf(hub.StationList.Focused.Id), hub.Hub.Current, "The focused tab and the open station agree.");
        }

        private static BaseStation? StationOf(string id)
        {
            const string prefix = "station.";
            if (id == null || !id.StartsWith(prefix)) return null;
            return System.Enum.TryParse<BaseStation>(id.Substring(prefix.Length), out var station) ? station : null;
        }

        [UnityTest]
        public IEnumerator ClickingAControl_AlsoTakesFocus_SoTheKeyboardContinuesFromThere()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            Control(hub, "station." + BaseStation.Trader).SimulateClick();
            Assert.AreEqual("station." + BaseStation.Trader, hub.StationList.Focused.Id,
                "After a click the keyboard cursor sits on what was clicked; the player must not have to hunt for it.");
        }

        [UnityTest]
        public IEnumerator SwitchingSectionWhileOneIsOpen_LeavesTheTabCursorOnTheSectionThatOpened()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // Closing a panel restores the tab it was opened from, which is right for Back and wrong for a switch.
            // Clicking a second tab while the first is open used to leave the cursor on the section being left.
            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Control(hub, "station." + BaseStation.Workshop).SimulateClick();
            yield return null;

            Assert.AreEqual(BaseStation.Workshop, hub.Hub.Current);
            Assert.AreEqual("station." + BaseStation.Workshop, hub.StationList.Focused.Id,
                "The tab cursor follows the section that opened, not the one that closed.");
            Assert.IsTrue(Control(hub, "station." + BaseStation.Workshop).IsActiveSection);
            Assert.IsFalse(Control(hub, "station." + BaseStation.Storage).IsActiveSection);
        }

        // ---------------- the six visual states ----------------

        [UnityTest]
        public IEnumerator Hover_IsVisible_AndIsNotTheSameThingAsFocus()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // Focus sits on the first station, so pick a different one to hover.
            var hovered = Control(hub, "station." + BaseStation.Workshop);
            Assert.AreNotEqual(ControlState.Hover, hovered.State);

            hovered.SimulateHover(true);
            Assert.IsTrue(hovered.IsHovered);
            Assert.AreEqual(ControlState.Hover, hovered.State, "Hover feedback appears as soon as the pointer enters.");
            Assert.IsFalse(hovered.ShowsFocusBrackets, "Hovering must not steal the keyboard focus.");

            var normal = UiTheme.Visual(ControlRole.Tab, ControlState.Normal);
            var hover = UiTheme.Visual(ControlRole.Tab, ControlState.Hover);
            Assert.AreNotEqual(normal.Fill, hover.Fill, "Hover must be visibly different from normal.");

            hovered.SimulateHover(false);
            Assert.IsFalse(hovered.IsHovered);
            Assert.AreNotEqual(ControlState.Hover, hovered.State, "Leaving the control clears the hover.");
        }

        [UnityTest]
        public IEnumerator Focus_IsObvious_AndReadableWithoutColour()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            hub.StationList.Focus("station." + BaseStation.Loadout);
            var focused = Control(hub, "station." + BaseStation.Loadout);
            focused.Refresh();

            Assert.AreEqual(ControlState.Focused, focused.State);
            Assert.IsTrue(focused.ShowsFocusBrackets,
                "Focus is marked by corner brackets, so it survives a grayscale or colour-impaired read (spec 18.4).");

            var other = Control(hub, "station." + BaseStation.Trader);
            other.Refresh();
            Assert.IsFalse(other.ShowsFocusBrackets, "Only one control carries focus at a time.");
        }

        [UnityTest]
        public IEnumerator ActiveTab_StaysMarked_AfterThePointerAndTheFocusHaveMovedOn()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            Control(hub, "station." + BaseStation.Workshop).SimulateClick();
            var workshop = Control(hub, "station." + BaseStation.Workshop);
            Assert.IsTrue(workshop.IsActiveSection);

            // Move both the pointer and the keyboard cursor away.
            workshop.SimulateHover(false);
            hub.StationList.Focus("station." + BaseStation.Storage);
            workshop.Refresh();

            Assert.IsTrue(workshop.IsActiveSection, "The open station is still the open station.");
            Assert.AreEqual(ControlState.Active, workshop.State, "It renders its selected state, not its normal one.");
            Assert.IsTrue(workshop.ShowsSelectedMarker, "The selected marker persists — this was the tab-bar bug.");

            var storage = Control(hub, "station." + BaseStation.Storage);
            storage.Refresh();
            Assert.IsFalse(storage.IsActiveSection, "Exactly one primary section is active at a time.");
        }

        [UnityTest]
        public IEnumerator PressedState_IsVisiblyDepressed_AndReleases()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var control = Control(hub, "station." + BaseStation.Trader);

            control.SimulatePress(true);
            Assert.IsTrue(control.IsPressed);
            Assert.AreEqual(ControlState.Pressed, control.State);
            Assert.AreEqual(1, UiTheme.Visual(ControlRole.Tab, ControlState.Pressed).Inset,
                "Pressed shifts the label by a pixel, so the state reads without relying on colour.");

            control.SimulatePress(false);
            Assert.IsFalse(control.IsPressed);
            Assert.AreNotEqual(ControlState.Pressed, control.State);
        }

        [UnityTest]
        public IEnumerator DisabledControl_IgnoresHoverAndClick()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // Which station carries an unavailable action depends on what the fresh profile happens to hold, so the
            // test looks for a real one rather than assuming where it will be.
            UiControl disabled = null;
            foreach (var station in BaseHubViewModel.Stations)
            {
                Control(hub, "station." + station).SimulateClick();
                yield return null;
                disabled = hub.Controls.FirstOrDefault(c => !c.IsEnabled);
                if (disabled != null) break;
            }

            Assert.IsNotNull(disabled,
                "No station presented an unavailable action, so the disabled state cannot be exercised. " +
                "A fresh profile should at least have transfers or terminal actions it cannot perform yet.");

            var before = disabled.Item.Activations;
            disabled.SimulateHover(true);
            Assert.IsFalse(disabled.IsHovered, "A disabled control does not light up under the pointer.");
            Assert.AreEqual(ControlState.Disabled, disabled.State);

            disabled.SimulateClick();
            Assert.AreEqual(before, disabled.Item.Activations, "A disabled control swallows the click instead of acting on it.");
            Assert.AreEqual(0, disabled.PointerActivations, "And it does not pass the click through to anything underneath.");
        }

        // ---------------- keyboard and controller still work ----------------

        [UnityTest]
        public IEnumerator KeyboardNavigation_StillReachesEveryStation_AndTabsStepHorizontally()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            var visited = new List<string>();
            var start = hub.StationList.Focused.Id;
            visited.Add(start);
            for (var i = 0; i < hub.StationList.Items.Count - 1; i++)
            {
                Assert.IsTrue(hub.StationList.Move(+1), "Every control in the bar is reachable by stepping.");
                visited.Add(hub.StationList.Focused.Id);
            }

            CollectionAssert.AreEquivalent(hub.StationList.Items.Select(i => i.Id), visited,
                "Stepping visits every station and LEAVE exactly once.");

            // A horizontal step is what a tab bar should answer to, and the hub wires it to the station list.
            hub.StationList.Focus(start);
            var horizontalSteps = 0;
            hub.Input.Horizontal += _ => horizontalSteps++;
            hub.StationList.Move(+1);
            Assert.AreNotEqual(start, hub.StationList.Focused.Id, "Horizontal steps move along the tab bar.");
            Assert.AreEqual(0, horizontalSteps, "Moving the list directly must not raise the input event that drives it.");
        }

        [UnityTest]
        public IEnumerator SwitchingFromMouseToKeyboardAndBack_NeedsNoClickFirst()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // Pointer activity marks the device.
            Control(hub, "station." + BaseStation.Character).SimulateHover(true);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, ActiveInputDevice.Current);

            // A controller takes over without anything being clicked first.
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            Assert.IsTrue(hub.StationList.Move(+1), "Controller navigation works straight away.");

            // And the pointer takes over again just as directly.
            Control(hub, "station." + BaseStation.Trader).SimulateHover(true);
            Assert.AreEqual(InputDeviceKind.KeyboardMouse, ActiveInputDevice.Current);
            Assert.IsTrue(Control(hub, "station." + BaseStation.Trader).IsHovered);
        }

        // ---------------- no duplicates, no overflow ----------------

        [UnityTest]
        public IEnumerator Shelter_ShowsOneCanvasAndOneStationPanelAtATime()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            Assert.AreEqual(1, Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length,
                "A second canvas would mean a stale screen was left rendering underneath.");

            foreach (var station in BaseHubViewModel.Stations)
            {
                Control(hub, "station." + station).SimulateClick();
                yield return null;

                var panels = AllChildren(hub.transform).Count(t => t.name.StartsWith("StationPanel:"));
                Assert.AreEqual(1, panels, $"Opening {station} must leave exactly one station panel, not {panels}.");
            }
        }

        [UnityTest]
        public IEnumerator EveryLabel_StaysInsideItsBoxAndInsideTheScreen()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var root = ReferenceRoot(hub);

            Assert.AreEqual(ScreenLayout.Width, root.rect.width, 0.5f, "The reference frame is exactly 640 wide at any window size.");
            Assert.AreEqual(ScreenLayout.Height, root.rect.height, 0.5f, "The reference frame is exactly 360 tall at any window size.");

            foreach (var station in BaseHubViewModel.Stations)
            {
                Control(hub, "station." + station).SimulateClick();
                yield return null;
                Canvas.ForceUpdateCanvases();

                foreach (var text in hub.GetComponentsInChildren<Text>(true))
                {
                    if (string.IsNullOrEmpty(text.text)) continue;

                    // A single-line label must fit the box the layout gave it. Wrapping labels are allowed to use
                    // several lines, so they are not measured horizontally.
                    if (text.horizontalOverflow == HorizontalWrapMode.Overflow)
                        Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + 0.5f,
                            $"[{station}] '{text.text}' needs {text.preferredWidth:F0} px in a {text.rectTransform.rect.width:F0} px box.");

                    var box = CanvasRect(root, text.rectTransform);
                    var frame = root.rect;
                    Assert.GreaterOrEqual(box.xMin, frame.xMin - 0.5f, $"[{station}] '{text.text}' starts left of the 640x360 frame.");
                    Assert.LessOrEqual(box.xMax, frame.xMax + 0.5f, $"[{station}] '{text.text}' runs past the right edge of the frame.");
                    Assert.GreaterOrEqual(box.yMin, frame.yMin - 0.5f, $"[{station}] '{text.text}' falls below the frame.");
                    Assert.LessOrEqual(box.yMax, frame.yMax + 0.5f, $"[{station}] '{text.text}' runs past the top of the frame.");
                }
            }
        }

        /// <summary>The fixed 640x360 frame every screen is authored inside.</summary>
        private static RectTransform ReferenceRoot(Component screen) =>
            screen.GetComponentsInChildren<RectTransform>(true).First(r => r.name == "ReferenceRoot");

        [UnityTest]
        public IEnumerator EveryTabShowsItsWholeLabel_NotATruncationOfIt()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();

            // The tab bar sizes each tab from its label, and the control insets its own text inside that tab. If the
            // two disagree by even a pixel the bar reads "STORA…", "MULTIPLAY…" — sized for a label it then cuts.
            foreach (var station in BaseHubViewModel.Stations)
            {
                var control = Control(hub, "station." + station);
                var label = control.GetComponentInChildren<Text>(true);
                var expected = BaseHubViewModel.Label(station);

                Assert.AreEqual(expected, label.text,
                    $"The {station} tab shows '{label.text}' where its label is '{expected}'.");
                Assert.LessOrEqual(label.preferredWidth, label.rectTransform.rect.width + 0.5f,
                    $"'{label.text}' does not fit the {station} tab.");
            }

            var leave = Control(hub, "station.close");
            Assert.AreEqual("LEAVE", leave.GetComponentInChildren<Text>(true).text);
        }

        [UnityTest]
        public IEnumerator Header_DoesNotOverlap_EvenWithAMaximumLengthNameAndALargeProfile()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var root = ReferenceRoot(hub);

            hub.Onboarding.SubmitDisplayName("WWWWWWWWWWWWWWWW");
            hub.Session.Banked.Credit(999999, "layout_test");
            yield return null;
            Canvas.ForceUpdateCanvases();

            // The header band inside the fixed reference frame: its top UiTheme.HeaderHeight pixels.
            var headerFloor = root.rect.yMax - UiTheme.HeaderHeight;

            var header = hub.GetComponentsInChildren<Text>(true)
                .Where(t => !string.IsNullOrEmpty(t.text))
                .Select(t => (t, Rect: CanvasRect(root, t.rectTransform)))
                .Where(e => e.Rect.center.y > headerFloor)
                .ToList();

            Assert.Greater(header.Count, 1, "The header carries several strings; the test is meaningless with one.");

            for (var i = 0; i < header.Count; i++)
            for (var j = i + 1; j < header.Count; j++)
                Assert.IsFalse(header[i].Rect.Overlaps(header[j].Rect),
                    $"'{header[i].t.text}' overlaps '{header[j].t.text}' in the header.");
        }

        // ---------------- the Character Station reflects a purchase made from any device ----------------

        /// <summary>
        /// A skill purchase made with the keyboard/controller goes straight through the focus stack, not through the
        /// control's pointer handler. The station's data column must still show the new rank, effect and next-rank
        /// preview immediately — it used to keep the values it was built with until the player left and re-entered.
        /// </summary>
        [UnityTest]
        public IEnumerator CharacterStation_ShowsTheNewRank_AfterAKeyboardPurchase()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var session = hub.Session;
            session.Progression.AddXp(RuinRail.Gameplay.Progression.LevelCurve.TotalXpForLevel(4));

            hub.Hub.Open(BaseStation.Character);
            yield return null;
            CollectionAssert.Contains(StationRows(hub), "0 / 10", "the station opens showing rank 0 of the cap");

            // The keyboard/controller path: focus the control and confirm through the focus stack itself.
            Assert.IsTrue(hub.Input.Stack.Current.Focus("character.allocate." + RuinRail.Gameplay.Progression.SkillId.Vitality));
            Assert.IsTrue(hub.Input.Stack.Activate(), "the attribute control is enabled with a Skill Point in hand");
            yield return null;

            Assert.AreEqual(1, hub.Hub.Character.RankOf(RuinRail.Gameplay.Progression.SkillId.Vitality));
            var rows = StationRows(hub);
            CollectionAssert.Contains(rows, "1 / 10", "the panel shows the rank the purchase produced");
            CollectionAssert.Contains(rows, "Max HP +2->+4", "and the effect now / next-rank preview that goes with it");
            CollectionAssert.Contains(rows, "2", "the remaining Skill Points are re-read too (3 earned - 1 spent)");
        }

        // ---------------- the display name is changed from the Character station ----------------

        /// <summary>
        /// CHANGE NAME in the Character station opens the name field over the Shelter; while it is open the menu input
        /// is suspended (WASD/Space are menu keys), an empty save is refused on screen, CANCEL keeps the old name, and
        /// SAVE updates the header, the survivor column and the terminal's party line at once. Captures the field and
        /// the Shelter with a normal and a maximum-length name for visual review.
        /// </summary>
        [UnityTest]
        public IEnumerator CharacterStation_ChangeName_SavesThroughTheField_AndEveryShelterNameFollows()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            Assert.IsTrue(hub.Onboarding.SubmitDisplayName("Rail Ghost"));
            hub.Hub.Open(BaseStation.Character);
            yield return null;

            // Keyboard/controller path: the control sits in the station's own focus list.
            Assert.IsTrue(hub.Input.Stack.Current.Focus("character.name"), "CHANGE NAME is reachable by focus navigation");
            Assert.IsTrue(hub.Input.Stack.Activate());
            yield return null;
            Assert.IsTrue(hub.NameEntry.IsOpen);
            Assert.IsTrue(hub.NameEntryView.IsVisible);
            Assert.IsTrue(hub.Input.InputBlocked(), "the Shelter menu does not navigate while the player types");
            var focusedBefore = hub.Input.Stack.Current.Focused?.Id;
            hub.Input.Poll();
            Assert.AreEqual(focusedBefore, hub.Input.Stack.Current.Focused?.Id);

            UiControl Button(string id) => hub.GetComponentsInChildren<UiControl>(true).First(c => c.Id == id);

            // Empty input: refused, reason on screen, name unchanged, field still open.
            while (hub.NameEntry.Text.Length > 0) hub.NameEntry.Backspace();
            Button("name.save").SimulateClick();
            yield return null;
            Assert.IsTrue(hub.NameEntry.IsOpen);
            Assert.AreEqual("Enter a name.", hub.NameEntryView.ErrorText);
            Assert.AreEqual("Rail Ghost", hub.Session.Profile.DisplayName);

            // CANCEL keeps the saved name.
            hub.NameEntry.Type("Nope");
            Button("name.cancel").SimulateClick();
            yield return null;
            Assert.IsFalse(hub.NameEntry.IsOpen);
            Assert.IsFalse(hub.NameEntryView.IsVisible);
            Assert.AreEqual("Rail Ghost", hub.Session.Profile.DisplayName);
            yield return null;
            Assert.IsFalse(hub.Input.InputBlocked(), "the menu owns the input again once the field is closed");

            // SAVE with a normal name: every Shelter name follows immediately, no new slot, no reload.
            hub.OpenNameEntry();
            while (hub.NameEntry.Text.Length > 0) hub.NameEntry.Backspace();
            hub.NameEntry.Type("Iron Wolf");
            yield return null;
            Assert.IsTrue(hub.NameEntryView.IsVisible);
            UiScreenCapture.Capture("name_entry_open_normal");
            Button("name.save").SimulateClick();
            yield return null;
            Assert.AreEqual("Iron Wolf", hub.Session.Profile.DisplayName);
            AssertShelterShows(hub, "Iron Wolf");
            UiScreenCapture.Capture("name_saved_normal");

            // A maximum-length name fits the field and every place the Shelter draws it.
            hub.OpenNameEntry();
            while (hub.NameEntry.Text.Length > 0) hub.NameEntry.Backspace();
            hub.NameEntry.Type("WWWWWWWWWWWWWWWWWWWW");
            yield return null;
            Assert.AreEqual(16, hub.NameEntry.Text.Length);
            UiScreenCapture.Capture("name_entry_open_max");
            var field = hub.NameEntryView.GetComponentsInChildren<Text>(true).First(t => t.text.StartsWith("WWWW"));
            Assert.LessOrEqual(field.preferredWidth, field.rectTransform.rect.width + 0.5f, "a 16-character name fits the field");
            Assert.IsTrue(hub.NameEntry.Submit());
            yield return null;
            AssertShelterShows(hub, "WWWWWWWWWWWWWWWW");
            hub.Hub.Open(BaseStation.Multiplayer);
            yield return null;
            UiScreenCapture.Capture("name_saved_max_terminal");
        }

        private static void AssertShelterShows(BaseHubScreen hub, string name)
        {
            var texts = hub.GetComponentsInChildren<Text>(true).Where(t => t.gameObject.activeInHierarchy).Select(t => t.text).ToList();
            Assert.GreaterOrEqual(texts.Count(t => t == name), 2, $"header and survivor column both show '{name}': {string.Join(" | ", texts.Where(t => t.Length > 0).Take(40))}");
            Assert.AreEqual(name, hub.Terminal.Roster.Single(l => l.IsLocal).Name, "the terminal's party line follows the rename");
        }

        // ---------------- post-run: bring the loot home and put it away ----------------

        /// <summary>
        /// The real flow: a depth-1 run (seed 11) picks up loot, the boss falls, the party returns alive and the Shelter
        /// composes. The Shelter points at Storage; OPEN STASH (mouse) shows the survivor beside Storage; a backpack item
        /// is stored by click, a worn weapon by keyboard/controller confirm, one is dragged back, the whole backpack is
        /// stored by its button; a full Storage refuses with its reason on screen; CLOSE returns to the station; and
        /// after leaving the Shelter and continuing the profile, Storage and the survivor are exactly as left.
        /// Captures: TestResults/PolishPreview/stash_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator Stash_MovesItemsBetweenBackpackAndStorage_ByMouseKeyboardAndDrag_PartialStacksAndFullSwaps_AndItPersists()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Stash Mover");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var loadout = session.Loadout;
            for (var i = 0; i < RuinRail.Gameplay.Items.PlayerInventory.BackpackCapacity; i++) loadout.RemoveFromBackpack(i);
            var smg = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var stim = new RuinRail.Gameplay.Items.ItemInstance("consumable_combat_stim", 2);
            Assert.IsTrue(loadout.TryAddToBackpack(smg) && loadout.TryAddToBackpack(stim));
            var stimInBag = loadout.BackpackSlots.First(i => i?.DefinitionId == "consumable_combat_stim");
            var harness = new RuinRail.Gameplay.Items.ItemInstance("armor_combat_harness", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            Assert.IsTrue(session.Storage.TryAdd(harness));
            List<string> Ids() => loadout.BackpackSlots.Where(i => i != null).Concat(session.Storage.Items).Select(i => i.InstanceId).OrderBy(x => x).ToList();

            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Control(hub, "storage.open").SimulateClick();
            yield return null;
            var view = hub.StashView;
            var stash = hub.Stash;
            RuinRail.UI.Inventory.InventorySlotRef Cell(string instanceId)
            {
                while (stash.PrevPage()) { }
                for (var page = 0; page < stash.PageCount; page++)
                {
                    foreach (var slot in view.BackpackSlots.Concat(view.StorageSlots))
                        if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                    if (!stash.NextPage()) break;
                }

                Assert.Fail(instanceId + " is not on screen");
                return default;
            }

            RuinRail.UI.Inventory.InventorySlotRef Bag(int i) => new(RuinRail.UI.Inventory.InventorySlotKind.Backpack, i);

            // ---- mouse: click a backpack item → Storage; click it in Storage → back; the slots redraw at once ----
            var smgCell = Cell(smg.InstanceId);
            view.SlotFor(smgCell).SimulateHover(true);
            yield return null;
            StringAssert.Contains("STORE  >>  STORAGE", view.ActionText);
            view.SlotFor(smgCell).SimulateClick();
            yield return null;
            Assert.IsNotNull(session.Storage.Find(smg.InstanceId));
            Assert.IsFalse(view.SlotFor(smgCell).IsOccupied, "the backpack cell is empty on screen at once");
            var storedCell = Cell(smg.InstanceId);
            Assert.AreEqual(stash.DefinitionOf(smg).Icon, view.SlotFor(storedCell).IconSprite, "and the Storage cell shows it");
            view.SlotFor(storedCell).SimulateClick();
            yield return null;
            Assert.IsTrue(loadout.BackpackSlots.Contains(smg), "clicked back into the backpack");

            // ---- keyboard / controller: Enter / A on the focused cell does the same, both ways ----
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(stimInBag.InstanceId)));
            Assert.IsTrue(hub.Input.Stack.Activate());
            yield return null;
            Assert.AreEqual(2, session.Storage.Items.Where(i => i.DefinitionId == "consumable_combat_stim").Sum(i => i.Quantity), "the whole stack went in");
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(harness.InstanceId)));
            Assert.IsTrue(hub.Input.Stack.Activate());
            yield return null;
            Assert.IsTrue(loadout.BackpackSlots.Contains(harness));

            // ---- a stack that only partly fits: the strip says how much, and exactly that much moves ----
            while (loadout.BackpackSlots.Any(i => i == null)) Assert.IsTrue(loadout.TryAddToBackpack(new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12")));
            loadout.RemoveFromBackpack(7);
            Assert.IsTrue(loadout.TryAddToBackpack(new RuinRail.Gameplay.Items.ItemInstance("ammo_light", 170)));
            Assert.IsTrue(session.Storage.TryAdd(new RuinRail.Gameplay.Items.ItemInstance("ammo_light", 120)));
            var storedAmmo = session.Storage.Items.First(i => i.DefinitionId == "ammo_light");
            yield return null;
            var room = loadout.BackpackRoomFor(storedAmmo);
            Assert.Greater(room, 0);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(storedAmmo.InstanceId)));
            yield return null;
            StringAssert.Contains($"TAKE {room} OF 120", view.ActionText);
            UiScreenCapture.Capture("stash_move_01_partial_offer");
            Assert.IsTrue(hub.Input.Stack.Activate());
            yield return null;
            Assert.AreEqual(120 - room, storedAmmo.Quantity);
            StringAssert.Contains($"Took {room}/120", view.MessageText);

            // ---- both sides full: a drag onto an occupied cell trades places, in both directions ----
            while (session.Storage.FreeSlots > 0) Assert.IsTrue(session.Storage.TryAdd(new RuinRail.Gameplay.Items.ItemInstance("weapon_wasp_45")));
            yield return null;
            var ids = Ids();
            var target = loadout.BackpackSlots.ToList().FindIndex(i => i?.DefinitionId == "weapon_kestrel_12");
            var outgoing = loadout.BackpackSlots[target];
            var incoming = session.Storage.Items.First(i => i.DefinitionId == "weapon_wasp_45");
            view.SlotFor(Bag(target)).SimulateDrop(view.SlotFor(Cell(incoming.InstanceId)));
            yield return null;
            Assert.AreSame(incoming, loadout.BackpackSlots[target], "the stored item took the backpack cell");
            Assert.IsNotNull(session.Storage.Find(outgoing.InstanceId), "the carried one took its Storage cell");
            CollectionAssert.AreEqual(ids, Ids(), "nothing lost or duplicated");
            StringAssert.Contains("Stored ", view.MessageText);
            UiScreenCapture.Capture("stash_move_02_full_swap");
            var back = loadout.BackpackSlots[target];
            view.SlotFor(Cell(outgoing.InstanceId)).SimulateDrop(view.SlotFor(Bag(target)));
            yield return null;
            Assert.AreSame(outgoing, loadout.BackpackSlots[target], "dragged back the other way");
            Assert.IsNotNull(session.Storage.Find(back.InstanceId));
            CollectionAssert.AreEqual(ids, Ids());

            // ---- save / reload ----
            var expectedBag = loadout.BackpackSlots.Select(i => i == null ? "-" : i.InstanceId + "x" + i.Quantity).ToList();
            var expectedStorage = session.Storage.Items.Select(i => i.InstanceId + "x" + i.Quantity).OrderBy(x => x).ToList();
            view.Buttons[RuinRail.UI.Inventory.StashView.CloseId].SimulateClick();
            yield return null;
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            Assert.AreEqual(RuinRail.UI.Base.PlayOutcome.Continued, _app.Menu.Play());
            yield return WaitComposed(SceneNames.Base);
            var again = _app.Menu.Session;
            CollectionAssert.AreEqual(expectedBag, again.Loadout.BackpackSlots.Select(i => i == null ? "-" : i.InstanceId + "x" + i.Quantity).ToList(), "the backpack came back as left");
            CollectionAssert.AreEqual(expectedStorage, again.Storage.Items.Select(i => i.InstanceId + "x" + i.Quantity).OrderBy(x => x).ToList(), "Storage came back as left");
        }

        [UnityTest]
        public IEnumerator Shelter_ItemInspection_OpensAfterADelay_ByHoverOrFocus_ForEveryItemKind_AndStaysOnScreen()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Inspector");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var loadout = session.Loadout;
            for (var i = 0; i < RuinRail.Gameplay.Items.PlayerInventory.BackpackCapacity; i++) loadout.RemoveFromBackpack(i);
            var rifle = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var legendary = new RuinRail.Gameplay.Items.ItemInstance("weapon_quickfang", 1, RuinRail.Gameplay.Items.Rarity.Legendary);
            var medkit = new RuinRail.Gameplay.Items.ItemInstance("consumable_medkit", 2);
            Assert.IsTrue(loadout.TryAddToBackpack(rifle) && loadout.TryAddToBackpack(legendary) && loadout.TryAddToBackpack(medkit));
            medkit = loadout.BackpackSlots.First(i => i?.DefinitionId == "consumable_medkit"); // stacks are re-homed on add
            var harness = new RuinRail.Gameplay.Items.ItemInstance("armor_combat_harness", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            var pendant = new RuinRail.Gameplay.Items.ItemInstance("accessory_trauma_pendant", 1, RuinRail.Gameplay.Items.Rarity.Epic);
            Assert.IsTrue(session.Storage.TryAdd(harness) && session.Storage.TryAdd(pendant));
            for (var i = 0; i < 4; i++) Assert.IsTrue(session.Storage.TryAdd(new RuinRail.Gameplay.Items.ItemInstance("weapon_wasp_45")));
            var fingerprint = loadout.BackpackSlots.Where(i => i != null).Concat(session.Storage.Items).Select(i => i.InstanceId + "x" + i.Quantity).OrderBy(x => x).ToList();

            IEnumerator Rest(float seconds) { var until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }

            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Control(hub, "storage.open").SimulateClick();
            yield return null;
            var view = hub.StashView;
            var stash = hub.Stash;
            var popup = view.StatPopup;
            RuinRail.UI.Inventory.InventorySlotRef Cell(string instanceId)
            {
                foreach (var slot in view.WornSlots.Concat(view.BackpackSlots).Concat(view.StorageSlots))
                    if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                Assert.Fail(instanceId + " is not on screen");
                return default;
            }

            void AssertBeside(RuinRail.UI.Inventory.InventorySlotRef cell, string what)
            {
                var slot = popup.SlotBounds(view.SlotFor(cell).Rect);
                var canvas = ((RectTransform)popup.transform.parent).rect;
                Assert.IsFalse(popup.Bounds.Overlaps(slot), what + ": the panel does not cover the inspected item");
                Assert.IsTrue(popup.Bounds.xMin >= canvas.xMin && popup.Bounds.xMax <= canvas.xMax && popup.Bounds.yMin >= canvas.yMin && popup.Bounds.yMax <= canvas.yMax, what + ": fully on screen " + popup.Bounds + " in " + canvas);
                Assert.AreEqual(popup.Bounds.x, Mathf.Round(popup.Bounds.x), what + ": whole pixels");
                if (!popup.IsComparing) return;
                var b = popup.ComparedBounds;
                Assert.IsFalse(b.Overlaps(slot), what + ": the equipped card does not cover the inspected item");
                Assert.IsFalse(b.Overlaps(popup.Bounds), what + ": the two cards never overlap");
                Assert.IsTrue(b.xMin >= canvas.xMin && b.xMax <= canvas.xMax && b.yMin >= canvas.yMin && b.yMax <= canvas.yMax, what + ": equipped card on screen " + b);
            }

            // The second card: the worn item of the relevant slot with its own lines (never a difference list), or none.
            void AssertCompared(RuinRail.Gameplay.Items.EquippedSlot? slot, string what)
            {
                var worn = slot.HasValue ? loadout.GetEquipped(slot.Value) : null;
                Assert.AreEqual(worn != null, popup.IsComparing, what + ": compared only with a worn counterpart");
                if (worn == null) return;
                StringAssert.StartsWith(stash.DefinitionOf(worn).DisplayName, popup.ComparedTitleText, what + ": the relevant worn item");
                Assert.AreEqual("EQUIPPED", popup.ComparedTagText);
                var own = RuinRail.UI.Inventory.ItemStatPopup.RowsOf(RuinRail.UI.Inventory.ItemTooltip.Build(worn, stash.DefinitionOf(worn), stash.Specials)).Select(r => RuinRail.UI.Inventory.ItemDetailLayout.Render(r, RuinRail.UI.Inventory.ItemStatPopup.InnerWidth)).ToList();
                CollectionAssert.AreEqual(own, popup.ComparedRowTexts, what + ": the worn item's own lines");
                Assert.IsFalse(popup.RowTexts.Concat(popup.ComparedRowTexts).Any(r => r.Contains("VS EQUIPPED") || r.Contains(" vs ")), what + ": no difference list");
            }

            // ---- mouse: hover, a deliberate rest opens the weapon's combat stats; leaving closes it at once ----
            var rifleCell = Cell(rifle.InstanceId);
            view.SlotFor(rifleCell).SimulateHover(true);
            yield return Rest(0.2f);
            Assert.IsFalse(popup.IsVisible, "not before the delay");
            yield return Rest(0.4f);
            Assert.IsTrue(popup.IsVisible, "open after the delay");
            Assert.AreEqual(stash.DefinitionOf(rifle).DisplayName, popup.TitleText);
            StringAssert.StartsWith("RARE", popup.SubtitleText);
            var tooltip = stash.TooltipFor(rifleCell);
            Assert.IsTrue(tooltip.BaseStats.All(line => popup.RowTexts.Any(r => r.StartsWith(line.Label) && r.EndsWith(line.Value))), "the weapon's stat lines, exactly as the item data states them: " + string.Join(" | ", popup.RowTexts));
            AssertCompared(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon, "weapon");
            AssertBeside(rifleCell, "weapon");
            UiScreenCapture.Capture("inspect_01_weapon_hover");
            view.SlotFor(rifleCell).SimulateHover(false);
            yield return null;
            Assert.IsFalse(popup.IsVisible, "leaving the item closes it at once");

            // Legendary: its Legendary line comes with it.
            var legendaryCell = Cell(legendary.InstanceId);
            view.SlotFor(legendaryCell).SimulateHover(true);
            yield return Rest(0.6f);
            Assert.IsTrue(popup.IsVisible);
            Assert.AreEqual(stash.DefinitionOf(legendary).DisplayName, popup.TitleText);
            Assert.IsNotEmpty(stash.TooltipFor(legendaryCell).LegendaryText);
            Assert.IsTrue(popup.RowTexts.Any(r => r.Length > 0 && stash.TooltipFor(legendaryCell).LegendaryText.StartsWith(r.TrimEnd().Substring(0, Mathf.Min(8, r.TrimEnd().Length)))), "the Legendary line is shown");
            UiScreenCapture.Capture("inspect_02_legendary_hover");
            view.SlotFor(legendaryCell).SimulateHover(false);

            // ---- keyboard / controller focus: the same panel without a pointer; moving on closes and re-opens ----
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(medkit.InstanceId)));
            yield return Rest(0.6f);
            Assert.IsTrue(popup.IsVisible, "focus inspects too");
            Assert.AreEqual(stash.DefinitionOf(medkit).DisplayName + " x2", popup.TitleText, "the stack count rides on the name");
            Assert.IsTrue(stash.TooltipFor(Cell(medkit.InstanceId)).BaseStats.Count > 0 && popup.RowTexts.Count > 0, "a consumable shows its effect lines");
            AssertCompared(RuinRail.Gameplay.Items.EquippedSlot.ActiveConsumable, "consumable");
            AssertBeside(Cell(medkit.InstanceId), "consumable");
            UiScreenCapture.Capture("inspect_03_consumable_focus");
            Assert.IsTrue(hub.Input.Stack.Navigate(Vector2Int.right), "arrows / D-pad move the focus on");
            yield return null;
            Assert.IsFalse(popup.IsVisible, "moving on closes it at once");

            var harnessCell = Cell(harness.InstanceId);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(harnessCell));
            yield return Rest(0.6f);
            Assert.AreEqual(stash.DefinitionOf(harness).DisplayName, popup.TitleText, "the new item, not the old one");
            Assert.IsTrue(popup.RowTexts.Any(r => r.StartsWith("Max HP")), "armor shows its item stats");
            AssertCompared(RuinRail.Gameplay.Items.EquippedSlot.Armor, "armor");
            AssertBeside(harnessCell, "armor");

            // Right-hand Storage column: the panel flips to the left of the item instead of leaving the screen.
            var edge = view.StorageSlots.First(sl => sl.Slot.Index == 5);
            Assert.IsNotNull(stash.ItemAt(edge.Slot), "an item in the last Storage column");
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(edge.Slot));
            yield return Rest(0.6f);
            Assert.IsTrue(popup.IsVisible);
            Assert.LessOrEqual(popup.Bounds.xMax, popup.SlotBounds(edge.Rect).xMin, "placed to the left at the right edge");
            AssertCompared(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon, "right edge");
            if (popup.IsComparing) Assert.LessOrEqual(popup.ComparedBounds.xMax, popup.Bounds.xMin, "the pair runs leftwards, the inspected card nearest the item");
            AssertBeside(edge.Slot, "right edge");
            UiScreenCapture.Capture("inspect_04_storage_right_edge");

            // Worn slot and the accessory.
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Equipped, 2)));
            yield return Rest(0.6f);
            Assert.AreEqual(stash.DefinitionOf(loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor)).DisplayName, popup.TitleText, "worn gear inspects too");
            Assert.IsFalse(popup.IsComparing, "a worn item is not compared with itself");
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(pendant.InstanceId)));
            yield return Rest(0.6f);
            Assert.AreEqual(stash.DefinitionOf(pendant).DisplayName, popup.TitleText);
            Assert.Greater(popup.RowTexts.Count, 0, "an accessory shows its stat lines");
            AssertCompared(RuinRail.Gameplay.Items.EquippedSlot.Accessory, "accessory");
            AssertBeside(Cell(pendant.InstanceId), "accessory");
            UiScreenCapture.Capture("inspect_06_accessory_focus");

            // Empty cell: nothing to inspect.
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Backpack, 7)));
            yield return Rest(0.6f);
            Assert.IsFalse(popup.IsVisible);

            // Inspection never moves anything, and the window closes with its panel.
            CollectionAssert.AreEqual(fingerprint, loadout.BackpackSlots.Where(i => i != null).Concat(session.Storage.Items).Select(i => i.InstanceId + "x" + i.Quantity).OrderBy(x => x).ToList());
            view.Buttons[RuinRail.UI.Inventory.StashView.CloseId].SimulateClick();
            yield return null;
            Assert.IsTrue(popup == null, "the panel went with the stash");

            // ---- LOADOUT station: the same inspection on its worn and backpack slots ----
            hub.Hub.Open(BaseStation.Loadout);
            yield return null;
            var body = hub.LoadoutView;
            Assert.IsNotNull(body);
            var bodyPopup = body.StatPopup;
            body.SlotFor(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Equipped, 0)).SimulateHover(true);
            yield return Rest(0.6f);
            Assert.IsTrue(bodyPopup.IsVisible, "Loadout station slots inspect too");
            Assert.AreEqual(stash.DefinitionOf(loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon)).DisplayName, bodyPopup.TitleText);
            Assert.IsTrue(bodyPopup.RowTexts.Any(r => r.StartsWith("Damage")));
            UiScreenCapture.Capture("inspect_05_loadout_worn");
            body.SlotFor(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Equipped, 0)).SimulateHover(false);
            yield return null;
            Assert.IsFalse(bodyPopup.IsVisible);
            // A Loadout backpack weapon: its card beside the worn Primary's.
            var bodyRifle = body.BackpackSlots.First(sl => loadout.BackpackSlots[sl.Slot.Index]?.InstanceId == rifle.InstanceId);
            bodyRifle.SimulateHover(true);
            yield return Rest(0.6f);
            Assert.IsTrue(bodyPopup.IsVisible && bodyPopup.IsComparing, "the Loadout compares the same way");
            Assert.AreEqual(stash.DefinitionOf(loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon)).DisplayName, bodyPopup.ComparedTitleText);
            Assert.IsFalse(bodyPopup.Bounds.Overlaps(bodyPopup.ComparedBounds));
            UiScreenCapture.Capture("inspect_07_loadout_backpack_compare");
            bodyRifle.SimulateHover(false);
            yield return null;
            hub.Hub.Close();
            yield return null;
            Assert.IsTrue(bodyPopup == null || !bodyPopup.IsVisible, "leaving the station takes the panel with it");
        }

        [UnityTest]
        public IEnumerator Stash_KeyboardAndController_SwapBackpackAndStorageItems_WithBothFull_AndCancelCleanly()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Stash Swapper");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var loadout = session.Loadout;
            for (var i = 0; i < RuinRail.Gameplay.Items.PlayerInventory.BackpackCapacity; i++) loadout.RemoveFromBackpack(i);
            var smg = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var harness = new RuinRail.Gameplay.Items.ItemInstance("armor_combat_harness", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            var pendant = new RuinRail.Gameplay.Items.ItemInstance("accessory_trauma_pendant", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            Assert.IsTrue(loadout.TryAddToBackpack(smg));
            while (loadout.BackpackSlots.Any(i => i == null)) Assert.IsTrue(loadout.TryAddToBackpack(new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12")));
            Assert.IsTrue(session.Storage.TryAdd(harness) && session.Storage.TryAdd(pendant));
            while (session.Storage.FreeSlots > 0) Assert.IsTrue(session.Storage.TryAdd(new RuinRail.Gameplay.Items.ItemInstance("weapon_wasp_45")));
            List<string> Ids() => loadout.BackpackSlots.Where(i => i != null).Concat(session.Storage.Items).Select(i => i.InstanceId).OrderBy(x => x).ToList();
            var ids = Ids();

            // Batch PlayMode runs unfocused: by default the Input System disables devices without focus and routes the editor's
            // keyboard to the editor, not the game. For this test the real input path is kept live (both restored in finally),
            // as it is for a player with the game window focused.
            var background = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            var editorRouting = UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode;
            UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            var gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();
            UnityEngine.InputSystem.InputSystem.EnableDevice(keyboard);
            UnityEngine.InputSystem.InputSystem.EnableDevice(gamepad);
            keyboard.MakeCurrent(); // the game reads Keyboard.current / Gamepad.current: these are the devices pressed below
            gamepad.MakeCurrent();
            try
            {
                IEnumerator Key(UnityEngine.InputSystem.Key key)
                {
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
                    yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState());
                    yield return null;
                }

                IEnumerator Button(UnityEngine.InputSystem.LowLevel.GamepadButton button)
                {
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(gamepad, new UnityEngine.InputSystem.LowLevel.GamepadState(button));
                    yield return null;
                    UnityEngine.InputSystem.InputSystem.QueueStateEvent(gamepad, new UnityEngine.InputSystem.LowLevel.GamepadState());
                    yield return null;
                }

                Control(hub, "station." + BaseStation.Storage).SimulateClick();
                yield return null;
                Control(hub, "storage.open").SimulateClick();
                yield return null;
                var view = hub.StashView;
                var stash = hub.Stash;
                RuinRail.UI.Inventory.InventorySlotRef Cell(string instanceId)
                {
                    while (stash.PrevPage()) { }
                    for (var page = 0; page < stash.PageCount; page++)
                    {
                        foreach (var slot in view.BackpackSlots.Concat(view.StorageSlots))
                            if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                        if (!stash.NextPage()) break;
                    }

                    Assert.Fail(instanceId + " is not on screen");
                    return default;
                }

                // ---- keyboard: R picks the backpack item, the focus jumps to a marked Storage item ----
                var smgCell = Cell(smg.InstanceId);
                var smgIndex = smgCell.Index;
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(smgCell));
                yield return null;
                yield return Key(UnityEngine.InputSystem.Key.R);
                Assert.IsTrue(stash.IsTargetingSwap, "R starts a swap");
                Assert.IsTrue(view.SlotFor(smgCell).ShowsSelectedFrame, "the picked item keeps the selection frame");
                StringAssert.StartsWith("stash.store.", view.FocusList.Focused.Id, "the focus moved to the Storage side");
                Assert.IsTrue(view.StorageSlots.Where(sl => stash.ItemAt(sl.Slot) != null).All(view.IsMarkedTarget), "every Storage item is a marked target");
                Assert.IsFalse(view.BackpackSlots.Any(view.IsMarkedTarget), "the picked side is not");
                StringAssert.Contains("ENTER: SWAP HERE", view.HintsText);
                StringAssert.Contains("SWAP: ", view.DetailTitleText);
                UiScreenCapture.Capture("stash_swap_01_keyboard_targeting");

                // Enter on an invalid target (the item's own side) changes nothing and keeps the pick.
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Backpack, (smgIndex + 1) % 8)));
                yield return Key(UnityEngine.InputSystem.Key.Enter);
                Assert.IsTrue(stash.IsTargetingSwap);
                CollectionAssert.AreEqual(ids, Ids());
                StringAssert.Contains("Pick a Storage item", view.MessageText);

                // Enter on the harness: the two trade places, with neither side having a free slot.
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(harness.InstanceId)));
                yield return null;
                StringAssert.Contains("SWAP WITH " + stash.DefinitionOf(harness).DisplayName.ToUpperInvariant(), view.ActionText);
                UiScreenCapture.Capture("stash_swap_02_keyboard_target_focused");
                yield return Key(UnityEngine.InputSystem.Key.Enter);
                Assert.IsFalse(stash.IsTargetingSwap);
                Assert.AreSame(harness, loadout.BackpackSlots[smgIndex], "the Storage item took the backpack cell");
                Assert.IsNotNull(session.Storage.Find(smg.InstanceId), "the backpack item took its Storage cell");
                Assert.AreEqual(0, session.Storage.FreeSlots);
                Assert.IsTrue(loadout.BackpackSlots.All(i => i != null));
                CollectionAssert.AreEqual(ids, Ids(), "nothing lost or duplicated");
                StringAssert.Contains(stash.DefinitionOf(harness).DisplayName, view.MessageText);
                Assert.IsFalse(view.MessageText.EndsWith("…"), $"the confirmation is shown whole: '{view.MessageText}'");

                // Esc while choosing cancels only the swap; the stash stays open, nothing moved.
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(pendant.InstanceId)));
                yield return Key(UnityEngine.InputSystem.Key.R);
                Assert.IsTrue(stash.IsTargetingSwap);
                yield return Key(UnityEngine.InputSystem.Key.Escape);
                Assert.IsFalse(stash.IsTargetingSwap, "Esc cancels the swap");
                Assert.IsTrue(hub.StashOpen, "and does not close the stash");
                Assert.IsFalse(view.BackpackSlots.Concat(view.StorageSlots).Any(view.IsMarkedTarget), "no target stays marked");
                CollectionAssert.AreEqual(ids, Ids());

                // ---- controller: Y picks the Storage item, A swaps it into the marked backpack cell ----
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(pendant.InstanceId)));
                yield return Button(UnityEngine.InputSystem.LowLevel.GamepadButton.North);
                Assert.IsTrue(stash.IsTargetingSwap, "Y starts a swap");
                StringAssert.StartsWith("stash.bag.", view.FocusList.Focused.Id, "the focus moved to the backpack");
                StringAssert.Contains("A: SWAP HERE", view.HintsText);
                var target = view.FocusList.Focused.Id;
                var bagIndex = int.Parse(target.Substring("stash.bag.".Length));
                var outgoing = loadout.BackpackSlots[bagIndex];
                UiScreenCapture.Capture("stash_swap_03_controller_targeting");
                yield return Button(UnityEngine.InputSystem.LowLevel.GamepadButton.South);
                Assert.IsFalse(stash.IsTargetingSwap);
                Assert.AreSame(pendant, loadout.BackpackSlots[bagIndex], "A swapped the stored pendant into the focused backpack cell");
                Assert.IsNotNull(session.Storage.Find(outgoing.InstanceId));
                CollectionAssert.AreEqual(ids, Ids());

                // B while choosing cancels; the next B closes the stash as before.
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(pendant.InstanceId)));
                yield return Button(UnityEngine.InputSystem.LowLevel.GamepadButton.North);
                Assert.IsTrue(stash.IsTargetingSwap);
                yield return Button(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
                Assert.IsFalse(stash.IsTargetingSwap, "B cancels the swap");
                Assert.IsTrue(hub.StashOpen);
                CollectionAssert.AreEqual(ids, Ids());

                // Plain Enter / A are unchanged: with Storage full, STORE on a backpack item is still a refusal that moves nothing.
                view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(pendant.InstanceId)));
                yield return Key(UnityEngine.InputSystem.Key.Enter);
                StringAssert.Contains("STORAGE FULL", view.ActionText);
                CollectionAssert.AreEqual(ids, Ids());
                yield return Button(UnityEngine.InputSystem.LowLevel.GamepadButton.East);
                Assert.IsFalse(hub.StashOpen, "B with no swap pending closes the stash, as before");
            }
            finally
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
                UnityEngine.InputSystem.InputSystem.RemoveDevice(gamepad);
                UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = background;
                UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = editorRouting;
            }
        }

        [UnityTest]
        public IEnumerator Stash_EquipsStorageItemsStraightIntoWornSlots_SwappingWithoutFreeSlots_ByKeyboardControllerAndDrag_AndItPersists()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Stash Equip");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var loadout = session.Loadout;
            RuinRail.Gameplay.Items.ItemInstance Worn(RuinRail.Gameplay.Items.EquippedSlot slot) => loadout.GetEquipped(slot);
            var pistol = Worn(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon);
            var vest = Worn(RuinRail.Gameplay.Items.EquippedSlot.Armor);
            var bandage = Worn(RuinRail.Gameplay.Items.EquippedSlot.ActiveConsumable);
            Assert.IsNotNull(pistol, "starter kit worn");
            Assert.IsNotNull(Worn(RuinRail.Gameplay.Items.EquippedSlot.SecondaryWeapon), "both weapon slots taken");

            // Storage holds one item for every worn slot (plus ammo, which has none); then the backpack and Storage are filled completely.
            var rattler = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var harness = new RuinRail.Gameplay.Items.ItemInstance("armor_combat_harness", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            var pendant = new RuinRail.Gameplay.Items.ItemInstance("accessory_trauma_pendant", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            foreach (var item in new[] { rattler, harness, pendant, new RuinRail.Gameplay.Items.ItemInstance("consumable_frag_grenade", 2), new RuinRail.Gameplay.Items.ItemInstance("ammo_light", 30) })
                Assert.IsTrue(session.Storage.TryAdd(item), item.DefinitionId);
            var grenades = session.Storage.Items.First(i => i.DefinitionId == "consumable_frag_grenade");
            var ammo = session.Storage.Items.First(i => i.DefinitionId == "ammo_light");
            while (loadout.BackpackSlots.Any(i => i == null)) Assert.IsTrue(loadout.TryAddToBackpack(new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12")));
            while (session.Storage.FreeSlots > 0) Assert.IsTrue(session.Storage.TryAdd(new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12")));
            var everything = new System.Func<List<string>>(() => System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(Worn)
                .Concat(loadout.BackpackSlots).Concat(session.Storage.Items).Where(i => i != null).Select(i => i.InstanceId).OrderBy(x => x).ToList());
            var allIds = everything();
            Assert.AreEqual(allIds.Count, allIds.Distinct().Count());

            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Control(hub, "storage.open").SimulateClick();
            yield return null;
            var view = hub.StashView;
            var stash = hub.Stash;
            Assert.IsTrue(hub.StashOpen && view.IsVisible);
            RuinRail.UI.Inventory.InventorySlotRef Cell(string instanceId)
            {
                for (var page = 0; page < stash.PageCount; page++)
                {
                    foreach (var slot in view.WornSlots.Concat(view.BackpackSlots).Concat(view.StorageSlots))
                        if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                    if (!stash.NextPage()) break;
                }

                while (stash.PrevPage()) { }
                foreach (var slot in view.WornSlots.Concat(view.BackpackSlots).Concat(view.StorageSlots))
                    if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                Assert.Fail(instanceId + " is not on screen");
                return default;
            }

            RuinRail.UI.Inventory.InventorySlotRef WornCell(RuinRail.Gameplay.Items.EquippedSlot slot) => new(RuinRail.UI.Inventory.InventorySlotKind.Equipped, (int)slot);

            // ---- keyboard: focus the stored weapon; the strip offers F: EQUIP (a swap) although the backpack is full ----
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            var rattlerCell = Cell(rattler.InstanceId);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(rattlerCell));
            yield return null;
            StringAssert.Contains("BACKPACK FULL", view.ActionText);
            StringAssert.Contains("F:  EQUIP  >  PRIMARY (SWAPS " + stash.DefinitionOf(pistol).DisplayName.ToUpperInvariant() + ")", view.ActionText);
            Assert.AreEqual(UiTheme.Terminal, view.ActionColor, "the open route is drawn as available");
            StringAssert.Contains("F: EQUIP", view.HintsText);
            UiScreenCapture.Capture("stash_equip_01_offer_swap");
            Assert.IsTrue(view.EquipCursor(), view.MessageText);
            yield return null;
            Assert.AreSame(rattler, Worn(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon), "equipped straight from Storage");
            Assert.AreSame(pistol, session.Storage.Find(pistol.InstanceId), "the worn weapon went into Storage");
            Assert.AreEqual(0, session.Storage.FreeSlots, "into the freed cell: Storage stays exactly full");
            Assert.IsTrue(loadout.BackpackSlots.All(i => i != null), "no backpack slot was needed");
            CollectionAssert.AreEqual(allIds, everything(), "nothing lost or duplicated");
            Assert.AreEqual($"Worn: {stash.DefinitionOf(rattler).DisplayName} · Stored: {stash.DefinitionOf(pistol).DisplayName}", view.MessageText, "the confirmation fits its line whole");
            Assert.AreEqual(view.WornSlots[0].IconSprite, stash.DefinitionOf(rattler).Icon, "the worn slot shows the new weapon at once");
            UiScreenCapture.Capture("stash_equip_02_after_swap");

            // ---- controller: the same action on X; hints and strip speak the pad's buttons ----
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(harness.InstanceId)));
            yield return null;
            StringAssert.Contains("X:  EQUIP  >  ARMOR", view.ActionText);
            StringAssert.Contains("X: EQUIP", view.HintsText);
            Assert.IsTrue(view.EquipCursor(), view.MessageText);
            yield return null;
            Assert.AreSame(harness, Worn(RuinRail.Gameplay.Items.EquippedSlot.Armor));
            Assert.IsNotNull(session.Storage.Find(vest.InstanceId));
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);

            // ---- mouse: drag onto an empty worn slot (accessory), and onto an occupied one (active consumable) ----
            view.SlotFor(WornCell(RuinRail.Gameplay.Items.EquippedSlot.Accessory)).SimulateDrop(view.SlotFor(Cell(pendant.InstanceId)));
            yield return null;
            Assert.AreSame(pendant, Worn(RuinRail.Gameplay.Items.EquippedSlot.Accessory));
            Assert.AreEqual(1, session.Storage.FreeSlots, "an empty worn slot simply takes it out of Storage");
            view.SlotFor(WornCell(RuinRail.Gameplay.Items.EquippedSlot.ActiveConsumable)).SimulateDrop(view.SlotFor(Cell(grenades.InstanceId)));
            yield return null;
            Assert.AreSame(grenades, Worn(RuinRail.Gameplay.Items.EquippedSlot.ActiveConsumable));
            Assert.AreEqual(2, grenades.Quantity);
            Assert.AreSame(bandage, session.Storage.Find(bandage.InstanceId), "the worn consumable stack went to Storage whole");
            CollectionAssert.AreEqual(allIds, everything(), "still every item exactly once");

            // ---- invalid pairings are refused and change nothing ----
            var beforeRefusals = everything();
            var wornBefore = System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(Worn).ToList();
            view.SlotFor(WornCell(RuinRail.Gameplay.Items.EquippedSlot.Armor)).SimulateDrop(view.SlotFor(Cell(pistol.InstanceId)));
            yield return null;
            StringAssert.Contains("does not fit", view.MessageText);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(ammo.InstanceId)));
            Assert.IsFalse(view.EquipCursor());
            StringAssert.Contains("cannot be worn", view.MessageText);
            CollectionAssert.AreEqual(beforeRefusals, everything());
            CollectionAssert.AreEqual(wornBefore, System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(Worn).ToList());

            // ---- Storage → Backpack is unchanged: refused while full, Enter / A takes once there is room ----
            var vestCell = Cell(vest.InstanceId);
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(vestCell));
            Assert.IsFalse(stash.Activate(vestCell));
            Assert.IsNotNull(session.Storage.Find(vest.InstanceId));
            loadout.RemoveFromBackpack(0);
            yield return null;
            view.FocusList.Focus(RuinRail.UI.Inventory.StashView.IdOf(Cell(vest.InstanceId)));
            Assert.IsTrue(hub.Input.Stack.Activate(), "Enter / A takes");
            yield return null;
            Assert.IsTrue(loadout.BackpackSlots.Contains(vest), "into the backpack, as before");
            Assert.IsNull(session.Storage.Find(vest.InstanceId));
            UiScreenCapture.Capture("stash_equip_03_final");

            // ---- save / reload ----
            var expectedWorn = System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(s => Worn(s)?.InstanceId).ToList();
            var expectedStorage = session.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList();
            view.Buttons[RuinRail.UI.Inventory.StashView.CloseId].SimulateClick();
            yield return null;
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            Assert.AreEqual(RuinRail.UI.Base.PlayOutcome.Continued, _app.Menu.Play());
            yield return WaitComposed(SceneNames.Base);
            var again = _app.Menu.Session;
            CollectionAssert.AreEqual(expectedWorn, System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(s => again.Loadout.GetEquipped(s)?.InstanceId).ToList(), "the worn slots came back as left");
            CollectionAssert.AreEqual(expectedStorage, again.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList(), "Storage came back as left");
            Assert.AreEqual(RuinRail.Gameplay.Items.Rarity.Rare, again.Loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon).Rarity);
        }

        [UnityTest]
        public IEnumerator PostRun_Stash_MovesLootAndWornGearIntoStorage_ByMouseKeyboardAndDrag_AndItPersists()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Stash Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            // ---- the run: loot picked up, boss down, return alive ----
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var carried = run.Expedition.State.Inventory;
            var smg = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var harness = new RuinRail.Gameplay.Items.ItemInstance("armor_combat_harness", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            Assert.IsTrue(carried.TryAddToBackpack(smg) && carried.TryAddToBackpack(harness), "loot picked up");
            // A looted weapon is worn home in place of the free Starter pistol, which is run-only (75) and stays behind.
            var kitPistol = carried.Unequip(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(kitPistol != null && kitPistol.IsUnsellable);
            Assert.IsTrue(carried.TryEquip(new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12", 1, RuinRail.Gameplay.Items.Rarity.Uncommon), RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon));
            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Boss);
            var player = run.Rig.Player;
            player.transform.position = bossRoom.InteriorWorldBounds.center;
            player.GetComponent<Rigidbody2D>().position = bossRoom.InteriorWorldBounds.center;
            for (var i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            BossIntroSequence.Current?.Finish();
            bossRoom.GetComponent<RuinRail.Dungeon.Runtime.RoomContentBinding>().Boss.Boss.Health.TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(100000000));
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!(run.Vote != null && run.Expedition.Transit?.State == RuinRail.Gameplay.Expedition.TransitDecisionState.Open)) { Assert.Less(Time.realtimeSinceStartup, deadline, "transit opened"); yield return null; }
            Assert.IsTrue(run.Vote.Vote(RuinRail.Gameplay.Expedition.TransitChoice.ReturnToShelter));
            if (run.Vote.AwaitingReturnConfirmation) run.Vote.ConfirmReturn();
            yield return WaitComposed(SceneNames.Base);
            yield return null;

            // ---- home: the Shelter points at Storage ----
            hub = Object.FindFirstObjectByType<BaseHubScreen>();
            var session = hub.Session;
            Assert.IsTrue(session.Loadout.Contains(smg.InstanceId) && session.Loadout.Contains(harness.InstanceId), "the loot came home on the survivor");
            Assert.IsFalse(session.Loadout.Contains(kitPistol.InstanceId) || session.Storage.Contains(kitPistol.InstanceId), "the free Starter pistol did not come home");
            yield return null;
            Assert.IsTrue(hub.LootCueVisible, "the STORAGE tab carries the loot pip");
            StringAssert.Contains("OPEN STASH", hub.NextCardText);
            UiScreenCapture.Capture("stash_00_shelter_loot_cue");

            // ---- mouse: STORAGE tab, then OPEN STASH ----
            Control(hub, "station." + BaseStation.Storage).SimulateClick();
            yield return null;
            Control(hub, "storage.open").SimulateClick();
            yield return null;
            Assert.IsTrue(hub.StashOpen && hub.StashView.IsVisible, "the stash window is up");
            Assert.AreSame(hub.StashView.FocusList, hub.Input.Stack.Current, "the stash owns the keyboard/controller focus");
            yield return null;
            Assert.IsFalse(hub.LootCueVisible, "opening the stash acknowledges the loot");
            var view = hub.StashView;
            var stash = hub.Stash;
            UiScreenCapture.Capture("stash_01_open_after_return");

            RuinRail.UI.Inventory.InventorySlotRef Cell(string instanceId)
            {
                foreach (var slot in view.WornSlots.Concat(view.BackpackSlots).Concat(view.StorageSlots))
                    if (stash.ItemAt(slot.Slot)?.InstanceId == instanceId) return slot.Slot;
                Assert.Fail(instanceId + " is not on screen");
                return default;
            }

            // ---- mouse: hover says STORE and lights Storage; click stores ----
            var smgSlot = view.SlotFor(Cell(smg.InstanceId));
            smgSlot.SimulateHover(true);
            yield return null;
            StringAssert.Contains("STORE", view.ActionText);
            Assert.IsTrue(smgSlot.ShowsFocusBrackets, "the hovered slot is the focused one");
            UiScreenCapture.Capture("stash_02_hover_store");
            smgSlot.SimulateClick();
            yield return null;
            Assert.IsFalse(session.Loadout.Contains(smg.InstanceId));
            Assert.IsNotNull(session.Storage.Find(smg.InstanceId), "stored by click");

            // ---- keyboard / controller: step to the worn primary and confirm ----
            var primary = session.Loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon);
            view.FocusList.Focus("stash.worn.4");
            Assert.IsTrue(hub.Input.Stack.Navigate(Vector2Int.left), "arrows step the worn row");
            for (var i = 0; i < 3; i++) hub.Input.Stack.Navigate(Vector2Int.left);
            Assert.AreEqual("stash.worn.0", view.FocusList.Focused.Id);
            Assert.IsTrue(hub.Input.Stack.Activate(), "Enter / A moves the focused item");
            yield return null;
            Assert.IsNull(session.Loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon), "the worn weapon left the slot");
            Assert.IsNotNull(session.Storage.Find(primary.InstanceId), "stored by keyboard/controller");
            view.FocusList.Focus("stash.worn.4");
            Assert.IsTrue(hub.Input.Stack.Navigate(Vector2Int.right), "right from the survivor crosses into Storage");
            StringAssert.StartsWith("stash.store.", view.FocusList.Focused.Id);

            // ---- drag: the stored weapon back onto the primary slot ----
            var primarySlot = view.SlotFor(new RuinRail.UI.Inventory.InventorySlotRef(RuinRail.UI.Inventory.InventorySlotKind.Equipped, 0));
            primarySlot.SimulateDrop(view.SlotFor(Cell(primary.InstanceId)));
            yield return null;
            Assert.AreEqual(primary.InstanceId, session.Loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon)?.InstanceId, "dragged from Storage onto the worn slot: equipped");

            // ---- STORE WHOLE BACKPACK ----
            view.Buttons[RuinRail.UI.Inventory.StashView.StoreBackpackId].SimulateClick();
            yield return null;
            Assert.AreEqual(0, session.Loadout.BackpackSlots.Count(i => i != null), "the whole backpack went into Storage");
            UiScreenCapture.Capture("stash_03_backpack_stored");

            // ---- a full Storage refuses, with the reason on screen ----
            var expectedStorage = session.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList();
            var expectedWorn = System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(session.Loadout.GetEquipped).Where(i => i != null).Select(i => i.InstanceId).OrderBy(x => x).ToList();
            var filler = new List<RuinRail.Gameplay.Items.ItemInstance>();
            while (session.Storage.Items.Count() < session.Storage.Capacity) { var f = new RuinRail.Gameplay.Items.ItemInstance("weapon_kestrel_12"); Assert.IsTrue(session.Storage.TryAdd(f)); filler.Add(f); }
            var spare = new RuinRail.Gameplay.Items.ItemInstance("weapon_wasp_45");
            Assert.IsTrue(session.Loadout.TryAddToBackpack(spare));
            yield return null;
            var spareSlot = view.SlotFor(Cell(spare.InstanceId));
            spareSlot.SimulateHover(true);
            yield return null;
            StringAssert.Contains("STORAGE FULL", view.ActionText);
            Assert.AreEqual(UiTheme.Danger, view.ActionColor, "a refusal is drawn as a refusal");
            StringAssert.Contains("FULL", view.StorageCountText);
            UiScreenCapture.Capture("stash_04_storage_full_refused");
            spareSlot.SimulateClick();
            yield return null;
            Assert.IsTrue(session.Loadout.Contains(spare.InstanceId), "the refused item stays on the survivor");

            // Undo the test-only filler so the persistence check below is about the real moves.
            foreach (var f in filler) session.Storage.TryRemove(f.InstanceId);
            session.Loadout.RemoveFromBackpack(session.Loadout.BackpackSlots.ToList().FindIndex(i => i?.InstanceId == spare.InstanceId));

            // ---- CLOSE returns to the Storage station ----
            view.Buttons[RuinRail.UI.Inventory.StashView.CloseId].SimulateClick();
            yield return null;
            Assert.IsFalse(hub.StashOpen);
            Assert.AreEqual(BaseStation.Storage, hub.Hub.Current, "still at the Storage station");
            Assert.AreSame(hub.PanelList, hub.Input.Stack.Current, "focus is back on the station controls");

            // ---- persistence: leave the Shelter, continue the profile ----
            _app.Menu.LeaveBase();
            _app.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            Assert.AreEqual(RuinRail.UI.Base.PlayOutcome.Continued, _app.Menu.Play());
            yield return WaitComposed(SceneNames.Base);
            var again = _app.Menu.Session;
            CollectionAssert.AreEqual(expectedStorage, again.Storage.Items.Select(i => i.InstanceId).OrderBy(x => x).ToList(), "Storage came back as left");
            CollectionAssert.AreEqual(expectedWorn, System.Enum.GetValues(typeof(RuinRail.Gameplay.Items.EquippedSlot)).Cast<RuinRail.Gameplay.Items.EquippedSlot>().Select(again.Loadout.GetEquipped).Where(i => i != null).Select(i => i.InstanceId).OrderBy(x => x).ToList(), "the survivor came back as left");
            Assert.IsNotNull(again.Storage.Find(smg.InstanceId), "the stored loot persisted");
        }

        /// <summary>Every string the open station's data column is currently drawing.</summary>
        private static string[] StationRows(BaseHubScreen hub) =>
            AllChildren(hub.transform).Where(t => t != null && t.name == "StationData")
                .SelectMany(t => t.GetComponentsInChildren<Text>(true))
                .Select(t => t.text).ToArray();

        /// <summary>A rect transform box expressed in the reference frame’s own space.</summary>
        private static Rect CanvasRect(RectTransform canvas, RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var min = canvas.InverseTransformPoint(corners[0]);
            var max = canvas.InverseTransformPoint(corners[2]);
            // Shrink by a hair so two boxes that merely share an edge are not reported as overlapping.
            return Rect.MinMaxRect(min.x + 0.05f, min.y + 0.05f, max.x - 0.05f, max.y - 0.05f);
        }

        private static IEnumerable<Transform> AllChildren(Transform root) =>
            root.GetComponentsInChildren<Transform>(true);

        /// <summary>
        /// LOADOUT in the stash's graphical language (94): worn slots and the backpack grid instead of slot-name buttons,
        /// one details strip for the pointed-at item, picked-up items mark the worn slots they fit; mouse click-to-move,
        /// drag, keyboard/controller confirm and the EQUIP / UNEQUIP shortcut all move items through the same view model;
        /// refusals (wrong slot, full backpack) show on the strip; Back first puts a picked-up item down.
        /// Captures: TestResults/PolishPreview/loadout_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator LoadoutTab_IsGraphical_AndEveryInputMovesItemsThroughTheSameRules()
        {
            yield return OpenShelter();
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Loadout Tester");
            hub.Onboarding.AcknowledgeStarterKit();
            var loadout = hub.Session.Loadout;
            var smg = new RuinRail.Gameplay.Items.ItemInstance("weapon_rattler_9", 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var rig = new RuinRail.Gameplay.Items.ItemInstance("armor_scout_rig", 1, RuinRail.Gameplay.Items.Rarity.Uncommon);
            Assert.IsTrue(loadout.TryAddToBackpack(smg) && loadout.TryAddToBackpack(rig));
            Control(hub, "station." + BaseStation.Loadout).SimulateClick();
            yield return null;

            var view = hub.LoadoutView;
            var vm = hub.Hub.Loadout.Inventory;
            Assert.IsNotNull(view, "LOADOUT draws the graphical body");
            Assert.AreSame(view.FocusList, hub.Input.Stack.Current, "the loadout owns keyboard/controller focus");
            Assert.AreEqual(5, view.WornSlots.Count);
            Assert.AreEqual(8, view.BackpackSlots.Count);
            Assert.IsTrue(view.WornSlots[0].IsOccupied && view.WornSlots[2].IsOccupied && !view.WornSlots[3].IsOccupied, "worn slots read at a glance (starter gear, empty accessory)");
            StringAssert.StartsWith("BACKPACK  3/8", view.BackpackHeaderText);
            Assert.IsFalse(hub.Controls.Any(c => c.Id == "inventory.drop" || c.Id == "inventory.consumable" || c.Id == "slot.PrimaryWeapon"), "no slot-name text buttons, no DROP that the Shelter always refuses");
            Assert.GreaterOrEqual(((RectTransform)view.transform).sizeDelta.y, RuinRail.UI.Inventory.LoadoutPanelView.RequiredHeight, "the body fits the station panel");
            AssertInsideScreen(view);
            UiScreenCapture.Capture("loadout_01_open");

            int IndexOf(RuinRail.Gameplay.Items.ItemInstance item) => loadout.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == item.InstanceId);
            RuinRail.UI.Inventory.InventorySlotRef Bag(int i) => new(RuinRail.UI.Inventory.InventorySlotKind.Backpack, i);
            RuinRail.UI.Inventory.InventorySlotRef Worn(RuinRail.Gameplay.Items.EquippedSlot slot) => new(RuinRail.UI.Inventory.InventorySlotKind.Equipped, (int)slot);

            // ---- mouse: hover shows the item; click picks it up and marks where it fits; click on PRIMARY swaps ----
            var smgSlot = view.SlotFor(Bag(IndexOf(smg)));
            smgSlot.SimulateHover(true);
            yield return null;
            StringAssert.Contains("Rattler", view.DetailTitleText, "details follow the pointer");
            StringAssert.EndsWith("· BACKPACK", view.DetailSubtitleText);
            Assert.IsTrue(view.IsMarkedTarget(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon) || view.IsMarkedTarget(RuinRail.Gameplay.Items.EquippedSlot.SecondaryWeapon), "the slot EQUIP would fill is marked");
            smgSlot.SimulateClick();
            yield return null;
            Assert.IsTrue(smgSlot.ShowsSelectedFrame, "the picked-up item shows the selected frame");
            Assert.IsTrue(view.IsMarkedTarget(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon) && view.IsMarkedTarget(RuinRail.Gameplay.Items.EquippedSlot.SecondaryWeapon), "both weapon slots are marked");
            Assert.IsFalse(view.IsMarkedTarget(RuinRail.Gameplay.Items.EquippedSlot.Armor), "a weapon never marks the armor slot");
            StringAssert.StartsWith("CHOOSE A SLOT", view.ActionText);
            Assert.IsFalse(view.ActionText.EndsWith("…"), "the action line fits");
            UiScreenCapture.Capture("loadout_02_picked_up");
            var pistol = loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon);
            var smgIndex = IndexOf(smg);
            view.SlotFor(Worn(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon)).SimulateClick();
            yield return null;
            Assert.AreSame(smg, loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon), "click-to-move swapped the SMG in");
            Assert.AreSame(pistol, loadout.BackpackSlots[smgIndex], "the pistol took the SMG's backpack slot");
            Assert.IsFalse(vm.Selected.HasValue);

            // ---- keyboard / controller: arrows to the rig, confirm, arrows to ARMOR, confirm ----
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            var rigIndex = IndexOf(rig);
            view.FocusList.Focus("backpack.0");
            for (var guard = 0; guard < 8 && view.FocusList.Focused.Id != "backpack." + rigIndex; guard++) hub.Input.Stack.Navigate(Vector2Int.right);
            Assert.AreEqual("backpack." + rigIndex, view.FocusList.Focused.Id, "arrows step the backpack grid");
            hub.Input.Stack.Activate();
            yield return null;
            StringAssert.Contains("B: CANCEL", view.ActionText, "controller wording on the strip");
            for (var guard = 0; guard < 4 && !view.FocusList.Focused.Id.StartsWith("slot."); guard++) hub.Input.Stack.Navigate(Vector2Int.up);
            StringAssert.StartsWith("slot.", view.FocusList.Focused.Id, "up from the backpack reaches the worn row");
            view.FocusList.Focus("slot.Armor");
            var vest = loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor);
            hub.Input.Stack.Activate();
            yield return null;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            Assert.AreSame(rig, loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor), "confirm / confirm swapped the rig in");
            Assert.AreSame(vest, loadout.BackpackSlots[rigIndex]);

            // ---- EQUIP / UNEQUIP shortcut and drag ----
            view.FocusList.Focus("slot.Armor");
            yield return null;
            Assert.AreEqual("UNEQUIP", view.ActionButtonText);
            view.ActionButton.SimulateClick();
            yield return null;
            Assert.IsNull(loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor), "UNEQUIP returned the rig to the backpack");
            view.SlotFor(Worn(RuinRail.Gameplay.Items.EquippedSlot.Armor)).SimulateDrop(view.SlotFor(Bag(IndexOf(vest))));
            yield return null;
            Assert.AreSame(vest, loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor), "dragging the vest onto ARMOR wears it");

            // ---- a refusal reads on the strip and changes nothing ----
            var before = JsonUtility.ToJson(loadout.ToSnapshot());
            view.SlotFor(Worn(RuinRail.Gameplay.Items.EquippedSlot.PrimaryWeapon)).SimulateDrop(view.SlotFor(Bag(IndexOf(rig))));
            yield return null;
            Assert.AreEqual("That item does not fit this slot.", view.ActionText);
            Assert.AreEqual(UiTheme.Danger, view.ActionColor);
            Assert.AreEqual(before, JsonUtility.ToJson(loadout.ToSnapshot()));
            UiScreenCapture.Capture("loadout_03_refused_wrong_slot");

            // ---- full backpack: the header says so, a worn item says how to change it, UNEQUIP is refused ----
            while (loadout.BackpackSlots.Any(i => i == null)) Assert.IsTrue(loadout.TryAddToBackpack(new RuinRail.Gameplay.Items.ItemInstance("weapon_field_knife")));
            view.FocusList.Focus("slot.Armor");
            yield return null;
            StringAssert.Contains("FULL", view.BackpackHeaderText);
            StringAssert.Contains("BACKPACK FULL", view.ActionText);
            Assert.IsFalse(view.ActionText.EndsWith("…") || view.DetailSubtitleText.EndsWith("…"), "no clipped strip text");
            UiScreenCapture.Capture("loadout_04_full_backpack");
            view.ActionButton.SimulateClick();
            yield return null;
            Assert.AreEqual("BACKPACK FULL", view.ActionText);
            Assert.AreSame(vest, loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor), "refused, still worn");
            // A swap still works with the backpack full.
            view.SlotFor(Worn(RuinRail.Gameplay.Items.EquippedSlot.Armor)).SimulateDrop(view.SlotFor(Bag(IndexOf(rig))));
            yield return null;
            Assert.AreSame(rig, loadout.GetEquipped(RuinRail.Gameplay.Items.EquippedSlot.Armor));

            // ---- Back puts a picked-up item down first, then leaves the station ----
            view.SlotFor(Bag(0)).SimulateClick();
            yield return null;
            Assert.IsTrue(vm.Selected.HasValue);
            var back = typeof(BaseHubScreen).GetMethod("OnBack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            back.Invoke(hub, null); // what Esc / B raises through MenuInput.Back
            yield return null;
            Assert.IsFalse(vm.Selected.HasValue, "Back cancels the pick-up");
            Assert.AreEqual(BaseStation.Loadout, hub.Hub.Current, "and the station stays open");
            back.Invoke(hub, null);
            yield return null;
            Assert.IsNull(hub.Hub.Current, "the next Back leaves LOADOUT");
        }

        /// <summary>
        /// Coins for the run through the real flow: chosen on the TRANSIT tab with mouse and keyboard/controller (banked,
        /// taking and what stays banked on screen), START moves exactly that amount into the run's Carried Coins, a
        /// return banks them again, and a death loses them — with the bank on disk matching every step.
        /// Captures: TestResults/PolishPreview/coins_*.png.
        /// </summary>
        [UnityTest]
        public IEnumerator CoinsForTheRun_ChosenAtTransit_MovedOnceAtStart_BankedOnReturn_LostOnDeath()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Coin Carrier");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            session.Banked.Credit(1000, "test");
            session.SaveNow("test");

            Control(hub, "station." + BaseStation.Transit).SimulateClick();
            yield return null;
            var transit = hub.Hub.Transit;
            var step = transit.CoinStep;
            Assert.AreEqual("0 C", hub.CoinSelectorText, "nothing is taken unless chosen");
            Assert.IsFalse(Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsLessId).Item.IsEnabled, "- is disabled at zero");
            UiScreenCapture.Capture("coins_01_transit_nothing_taken");

            // Mouse: + twice, ALL, then - once.
            Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsMoreId).SimulateClick();
            Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsMoreId).SimulateClick();
            yield return null;
            Assert.AreEqual(step * 2, session.CoinsToCarry);
            Assert.AreEqual((step * 2) + " C", hub.CoinSelectorText);
            Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsAllId).SimulateClick();
            yield return null;
            Assert.AreEqual(1000, session.CoinsToCarry);
            Assert.IsFalse(Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsMoreId).Item.IsEnabled, "+ is disabled at the whole bank");
            UiScreenCapture.Capture("coins_02_transit_all_taken");
            Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsNoneId).SimulateClick();
            yield return null;
            Assert.AreEqual(0, session.CoinsToCarry);

            // Keyboard / controller: focus + and confirm three times.
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            hub.Input.Stack.Current.Focus(RuinRail.UI.Navigation.ScreenNavigation.CoinsMoreId);
            for (var i = 0; i < 6; i++) { hub.Input.Stack.Activate(); }
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            yield return null;
            var taking = System.Math.Min(step * 6, 1000);
            Assert.AreEqual(taking, session.CoinsToCarry);
            Assert.AreEqual(1000, session.Profile.BankedCoins, "choosing moved nothing");
            UiScreenCapture.Capture("coins_03_transit_partial");

            // READY, START: exactly the chosen amount becomes Carried Coins; the start save holds the lower bank.
            Control(hub, "transit.ready").SimulateClick();
            yield return null;
            Control(hub, "transit.start").SimulateClick();
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(taking, run.Expedition.State.CarriedCoins);
            Assert.AreEqual(1000 - taking, session.Profile.BankedCoins);
            Assert.AreEqual(1000 - taking, _app.Saves.Load().Slot.Profile.BankedCoins, "the debit is on disk with the open run");
            Assert.AreEqual(taking.ToString(), run.HudView.CoinsText, "the HUD shows the carried coins");
            LiveDungeonCapture.Capture("TestResults/PolishPreview", "coins_04_run_hud_carried", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);

            // Return alive: the taken coins come home, once.
            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RuinRail.Dungeon.Rooms.RoomType.Boss);
            var player = run.Rig.Player;
            player.transform.position = bossRoom.InteriorWorldBounds.center;
            player.GetComponent<Rigidbody2D>().position = bossRoom.InteriorWorldBounds.center;
            for (var i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            BossIntroSequence.Current?.Finish();
            bossRoom.GetComponent<RuinRail.Dungeon.Runtime.RoomContentBinding>().Boss.Boss.Health.TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(100000000));
            var deadline = Time.realtimeSinceStartup + 20f;
            while (!(run.Vote != null && run.Expedition.Transit?.State == RuinRail.Gameplay.Expedition.TransitDecisionState.Open)) { Assert.Less(Time.realtimeSinceStartup, deadline, "transit opened"); yield return null; }
            var carriedAtReturn = run.Expedition.State.CarriedCoins;
            Assert.GreaterOrEqual(carriedAtReturn, taking);
            Assert.IsTrue(run.Vote.Vote(RuinRail.Gameplay.Expedition.TransitChoice.ReturnToShelter));
            if (run.Vote.AwaitingReturnConfirmation) run.Vote.ConfirmReturn();
            yield return WaitComposed(SceneNames.Base);
            yield return null;
            hub = Object.FindFirstObjectByType<BaseHubScreen>();
            session = hub.Session;
            var home = 1000 - taking + carriedAtReturn;
            Assert.AreEqual(home, session.Profile.BankedCoins, "stayed + taken + found, banked once");
            Assert.AreEqual(home, _app.Saves.Load().Slot.Profile.BankedCoins);
            Assert.AreEqual(0, session.CoinsToCarry, "the next preparation starts with nothing taken");

            // A second run takes everything and dies: the taken coins are lost, the bank stays as the start save wrote it.
            Control(hub, "station." + BaseStation.Transit).SimulateClick();
            yield return null;
            Control(hub, RuinRail.UI.Navigation.ScreenNavigation.CoinsAllId).SimulateClick();
            Control(hub, "transit.ready").SimulateClick();
            yield return null;
            Control(hub, "transit.start").SimulateClick();
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
            run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(home, run.Expedition.State.CarriedCoins);
            Assert.AreEqual(0, session.Profile.BankedCoins);
            var health = run.Rig.Player.GetComponent<RuinRail.Gameplay.Combat.HealthComponent>();
            health.SetInvulnerabilityState(null);
            deadline = Time.realtimeSinceStartup + 10f;
            while (run.Expedition.IsExpeditionActive)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "the death closed the run");
                health.TryApplyDamage(new RuinRail.Gameplay.Combat.DamageRequest(100000));
                yield return null;
            }

            Assert.AreEqual(home, run.Expedition.LastSummary.CoinsLost, "the taken coins were lost with the run");
            Assert.AreEqual(0, _app.Saves.Load().Slot.Profile.BankedCoins, "the loss is saved; nothing refunded or duplicated");
            yield return null;
            run.RunFailed.ReturnToShelter();
            yield return WaitComposed(SceneNames.Base);
            yield return null;
            Assert.AreEqual(0, Object.FindFirstObjectByType<BaseHubScreen>().Session.Profile.BankedCoins);
        }

        private static void AssertInsideScreen(RuinRail.UI.Inventory.LoadoutPanelView view)
        {
            var corners = new Vector3[4];
            foreach (var rect in view.GetComponentsInChildren<RectTransform>())
            {
                rect.GetWorldCorners(corners);
                foreach (var corner in corners)
                {
                    Assert.GreaterOrEqual(corner.x, -0.5f, rect.name);
                    Assert.GreaterOrEqual(corner.y, -0.5f, rect.name);
                    Assert.LessOrEqual(corner.x, Screen.width + 0.5f, rect.name);
                    Assert.LessOrEqual(corner.y, Screen.height + 0.5f, rect.name);
                }
            }
        }
    
        // ---------------- TRADER → SELL and WORKSHOP → UPGRADE TRADER through the real screen ----------------

        private BaseHubScreen Hub() => Object.FindFirstObjectByType<BaseHubScreen>();

        private static string ScreenText(BaseHubScreen hub) =>
            string.Join(" | ", hub.GetComponentsInChildren<Text>().Select(t => t.text).Where(t => !string.IsNullOrEmpty(t)));

        private IEnumerator Restart()
        {
            // A fresh process view of the same save folder: everything below is read back from disk.
            Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            yield return null;
            yield return OpenShelter();
        }

        private static MerchantRowViewRef RowFor(BaseHubScreen hub, string instanceId) =>
            new(hub.TraderRows.FirstOrDefault(r => r.gameObject.activeSelf && r.Row?.Item?.InstanceId == instanceId));

        private readonly struct MerchantRowViewRef
        {
            public MerchantRowViewRef(RuinRail.UI.Merchant.MerchantRowView view) { View = view; }
            public RuinRail.UI.Merchant.MerchantRowView View { get; }
        }

        [UnityTest]
        public IEnumerator TraderSell_SellsExactlyThePickedItem_FromBackpackAndStorage_ByMouseAndKeyboard_AndSurvivesReload()
        {
            yield return OpenShelter();
            var hub = Hub();
            hub.Onboarding.SubmitDisplayName("Seller");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var definitions = session.Configs.Registry.Definitions;
            var weaponDef = definitions.OfType<RuinRail.Gameplay.Items.EquipmentItemDefinition>().First(d => d.Category == RuinRail.Gameplay.Items.ItemCategory.Weapon && d.IsAcquirableInV1);
            var consumableDef = definitions.OfType<RuinRail.Gameplay.Items.Consumables.ConsumableDefinition>().First(d => d.IsStackable);
            var weapon = new RuinRail.Gameplay.Items.ItemInstance(weaponDef.Id, 1, RuinRail.Gameplay.Items.Rarity.Rare);
            var starter = new RuinRail.Gameplay.Items.ItemInstance(weaponDef.Id, 1) { IsUnsellable = true };
            var storedSource = new RuinRail.Gameplay.Items.ItemInstance(consumableDef.Id, 2);
            Assert.IsTrue(session.Loadout.TryAddToBackpack(starter));
            Assert.IsTrue(session.Loadout.TryAddToBackpack(weapon));
            Assert.IsTrue(session.Storage.TryAdd(storedSource));
            // A stackable is re-instanced on add (the source is zeroed): the stack to sell is the one Storage holds.
            var stored = session.Storage.Items.Single(i => i.DefinitionId == consumableDef.Id);
            Assert.AreEqual(2, stored.Quantity);
            session.Banked.Credit(1000, "test_funds");
            var weaponQuote = session.Trader.QuoteSellValue(weapon);
            var storedQuote = session.Trader.QuoteSellValue(stored);
            Assert.Greater(weaponQuote, 0);
            Assert.Greater(storedQuote, 0);

            Control(hub, "station." + BaseStation.Trader).SimulateClick();
            yield return null;
            // SELL turns the counter to the survivor's items (mouse).
            Control(hub, "trader.sell").SimulateClick();
            yield return null;
            Assert.IsTrue(hub.TraderSelling, "SELL shows what can be sold");
            var held = session.Loadout.BackpackSlots.Count(i => i != null) + session.Storage.Items.Count();
            Assert.AreEqual(held, hub.Input.Stack.Current.Items.Count(i => i.Id.StartsWith(RuinRail.UI.Navigation.ScreenNavigation.TraderSellItemPrefix)), "every held item is a row (backpack, then Storage)");
            UiScreenCapture.Capture("trader_sell_list");

            // The starter item is listed but disabled: nothing happens when it is pressed.
            var starterItem = hub.Input.Stack.Current.Items.First(i => i.Label == "SELL " + weaponDef.DisplayName && !i.IsEnabled);
            var banked0 = session.Banked.Balance;
            Assert.IsFalse(starterItem.TryActivate(), "a disabled row does not activate");
            RowFor(hub, starter.InstanceId).View?.Control.SimulateClick();
            yield return null;
            Assert.AreEqual(banked0, session.Banked.Balance, "a Starter Kit item pays nothing");
            Assert.IsNotNull(session.Loadout.BackpackSlots.FirstOrDefault(i => i?.InstanceId == starter.InstanceId), "and stays in the backpack");

            // Mouse: click the rare weapon's row — exactly that item, exactly its quote.
            var weaponRow = RowFor(hub, weapon.InstanceId).View;
            if (weaponRow == null)
            {
                // Scroll the counter until the row is on screen (the list pages four rows at a time).
                var id = hub.Input.Stack.Current.Items.First(i => i.Label == "SELL " + weaponDef.DisplayName && i.IsEnabled).Id;
                hub.Input.Stack.Current.Focus(id);
                yield return null;
                weaponRow = RowFor(hub, weapon.InstanceId).View;
            }

            Assert.IsNotNull(weaponRow, "the weapon has a row on the counter");
            weaponRow.Control.SimulateClick();
            Assert.AreEqual(banked0 + weaponQuote, session.Banked.Balance, "paid the quote once");
            Assert.IsNull(session.Loadout.BackpackSlots.FirstOrDefault(i => i?.InstanceId == weapon.InstanceId), "the sold weapon left the backpack");
            StringAssert.Contains("Sold for " + weaponQuote, hub.Hub.Trader.Feedback.Text);
            // The same activation again in the same frame (a double press): nothing more is paid.
            hub.Input.Stack.Activate();
            Assert.AreEqual(banked0 + weaponQuote, session.Banked.Balance, "a repeated press never pays twice");
            yield return null;
            yield return null;
            Assert.IsTrue(hub.TraderSelling);
            Assert.IsNull(RowFor(hub, weapon.InstanceId).View, "the counter updated: the sold item is gone from the list");

            // Keyboard: the Storage stack.
            var storedId = hub.Input.Stack.Current.Items.First(i => i.Label == "SELL " + consumableDef.DisplayName && i.IsEnabled).Id;
            hub.Input.Stack.Current.Focus(storedId);
            hub.Input.Stack.Activate();
            Assert.AreEqual(banked0 + weaponQuote + storedQuote, session.Banked.Balance, "the Storage stack paid its quote (the whole stack)");
            Assert.IsNull(session.Storage.Find(stored.InstanceId), "the whole stack left Storage");
            yield return null;
            yield return null;

            // Back returns to the offers, the next Back leaves the Trader.
            typeof(BaseHubScreen).GetMethod("OnBack", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(hub, null);
            yield return null;
            yield return null;
            Assert.IsFalse(hub.TraderSelling);
            Assert.AreEqual(BaseStation.Trader, hub.Hub.Current);
            var expected = session.Banked.Balance;

            yield return null;
            yield return Restart();
            var reloaded = Hub().Session;
            Assert.AreEqual(expected, reloaded.Banked.Balance, "the coins survived the reload");
            Assert.IsNull(reloaded.Loadout.BackpackSlots.FirstOrDefault(i => i?.InstanceId == weapon.InstanceId), "the sold weapon stays sold");
            Assert.IsNull(reloaded.Storage.Find(stored.InstanceId), "the sold stack stays sold");
            Assert.IsNotNull(reloaded.Loadout.BackpackSlots.FirstOrDefault(i => i?.InstanceId == starter.InstanceId), "the unsellable item is still there");
        }

        [UnityTest]
        public IEnumerator WorkshopUpgradeTrader_ChargesOnce_RaisesTheLevel_ShowsTheNewStockAndPanel_AndSurvivesReload()
        {
            yield return OpenShelter();
            var hub = Hub();
            hub.Onboarding.SubmitDisplayName("Upgrader");
            hub.Onboarding.AcknowledgeStarterKit();
            var session = hub.Session;
            var config = session.Configs.Trader;
            var toTwo = config.UpgradeCostFrom(1);
            var toThree = config.UpgradeCostFrom(2);
            session.Banked.Credit(toTwo + 100 - session.Banked.Balance, "test_funds");

            Control(hub, "station." + BaseStation.Workshop).SimulateClick();
            yield return null;
            // Keyboard / controller confirm.
            hub.Input.Stack.Current.Focus("workshop.trader");
            hub.Input.Stack.Activate();
            yield return null;
            Assert.AreEqual(2, session.Trader.Level, "one confirm, one level");
            Assert.AreEqual(100, session.Banked.Balance, "charged the level-2 cost once");
            Assert.AreEqual(config.GetLevel(2).Offers, session.Trader.Offers.Count, "the new level's stock is on the counter now");
            var text = ScreenText(hub);
            StringAssert.Contains("LEVEL 2 / 3", text, "the Workshop panel shows the new level without reopening");
            StringAssert.Contains("TO LEVEL 3", text);
            StringAssert.Contains(toThree + " C", text);
            StringAssert.Contains("100 C", text);
            UiScreenCapture.Capture("workshop_trader_upgraded");

            // Unaffordable: nothing is spent.
            hub.Input.Stack.Activate();
            yield return null;
            Assert.AreEqual(2, session.Trader.Level);
            Assert.AreEqual(100, session.Banked.Balance, "an unaffordable upgrade spends nothing");
            Assert.IsTrue(hub.Hub.Workshop.Feedback.IsError);

            // Mouse, to the top level; then MAX refuses without charging.
            session.Banked.Credit(toThree, "test_funds");
            Control(hub, "workshop.trader").SimulateClick();
            yield return null;
            Assert.AreEqual(3, session.Trader.Level);
            Assert.AreEqual(100, session.Banked.Balance);
            Assert.AreEqual(config.GetLevel(3).Offers, session.Trader.Offers.Count);
            StringAssert.Contains("MAX LEVEL", ScreenText(hub));
            session.Banked.Credit(50000, "test_funds");
            var rich = session.Banked.Balance;
            Control(hub, "workshop.trader").SimulateClick();
            yield return null;
            Assert.AreEqual(3, session.Trader.Level);
            Assert.AreEqual(rich, session.Banked.Balance, "the max level never charges");

            // The counter shows the level-3 stock; reload rebuilds exactly that stock.
            var liveStock = session.Trader.Offers.Select(o => $"{o.Definition.Id}:{o.Item.Rarity}:{o.Price}").ToList();
            Control(hub, "station." + BaseStation.Trader).SimulateClick();
            yield return null;
            Assert.AreEqual(config.GetLevel(3).Offers, hub.Input.Stack.Current.Items.Count(i => i.Id.StartsWith("trader.buy.")));
            hub.Hub.Close();
            yield return null;
            yield return null;
            yield return Restart();
            var reloaded = Hub().Session;
            Assert.AreEqual(3, reloaded.Trader.Level, "the level survived the reload");
            Assert.AreEqual(rich, reloaded.Banked.Balance);
            CollectionAssert.AreEqual(liveStock, reloaded.Trader.Offers.Select(o => $"{o.Definition.Id}:{o.Item.Rarity}:{o.Price}").ToList(), "the reloaded counter is the stock the upgrade showed");
        }
}
}
