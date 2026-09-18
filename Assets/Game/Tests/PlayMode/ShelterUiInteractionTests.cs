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
    }
}
