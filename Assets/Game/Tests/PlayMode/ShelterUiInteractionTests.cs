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
    }
}
