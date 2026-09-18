using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Produces the front-end half of the polish pass evidence: one 640x360 capture of every real menu screen, in
    /// <c>TestResults/PolishPreview</c>.
    ///
    /// Each capture is also checked, because a PNG existing proves nothing. The three measurements are aimed at the
    /// failure modes the pass names by name: a frame that is almost entirely one colour is a screen that did not
    /// render, a frame with very few distinct colours is a screen drawing chrome and no content, and a frame with one
    /// enormous connected block of flat colour is the "giant blank black rectangle" that section A4 forbids.
    /// </summary>
    public sealed class PolishPreviewCaptureTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_shots_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
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

        /// <summary>The checks every captured menu screen has to pass to count as evidence.</summary>
        private static void AssertLooksLikeARenderedScreen(string label, UiScreenCapture.Result shot)
        {
            Assert.IsTrue(File.Exists(shot.Path), $"{label}: no capture was written.");

            Assert.Less(shot.DominantColourShare(), 0.72f,
                $"{label}: {shot.DominantColourShare():P0} of the frame is a single colour — that is a screen that did not draw.");

            Assert.Greater(shot.DistinctColours(), 24,
                $"{label}: only {shot.DistinctColours()} distinct colours; the screen is drawing chrome and no content.");

            Assert.Less(shot.LargestFlatBlockShare(), 0.34f,
                $"{label}: {shot.LargestFlatBlockShare():P0} of the frame is one connected block of flat colour — " +
                "that is the empty content area the polish pass forbids.");
        }

        [UnityTest]
        public IEnumerator Capture_MainMenu()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            yield return null;

            var shot = UiScreenCapture.Capture("ui_main_menu");
            AssertLooksLikeARenderedScreen("Main Menu", shot);

            // And the settings page over it, since that is a second full screen the player sees.
            var screen = Object.FindFirstObjectByType<MainMenuScreen>();
            screen.MenuList.Focus("menu." + MainMenuEntry.Settings);
            screen.Input.Stack.Activate();
            yield return null;
            yield return null;

            var settings = UiScreenCapture.Capture("ui_main_menu_settings");
            AssertLooksLikeARenderedScreen("Main Menu / Settings", settings);
        }

        [UnityTest]
        public IEnumerator Capture_ShelterAndEveryStation()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            yield return null;

            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            Assert.IsNotNull(hub);

            // A named profile and a little progress, so the captures show a populated hub rather than all zeroes.
            hub.Onboarding.SubmitDisplayName("Smoke Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            hub.Session.Banked.Credit(1450, "preview");
            yield return null;

            var written = new List<string>();

            var shelter = UiScreenCapture.Capture("ui_shelter_main");
            AssertLooksLikeARenderedScreen("Shelter", shelter);
            written.Add(shelter.Path);

            foreach (var station in BaseHubViewModel.Stations)
            {
                hub.Hub.Open(station);
                yield return null;
                yield return null;

                var shot = UiScreenCapture.Capture("ui_shelter_" + station.ToString().ToLowerInvariant());
                AssertLooksLikeARenderedScreen(station.ToString(), shot);
                written.Add(shot.Path);
            }

            Assert.AreEqual(BaseHubViewModel.Stations.Length + 1, written.Count,
                "One capture for the hub itself and one for each of the seven stations.");
            Debug.Log("Polish UI previews written:\n" + string.Join("\n", written));
        }
    }
}
