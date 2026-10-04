using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The front-end places in the real screens: the Main Menu tunnel and the Shelter room draw their baked scene with
    /// <see cref="FrontEndAmbience"/> alive on it (it moves, it stays small and dim, it draws under every panel and
    /// takes no input), and the screens still work (PLAY reaches the Shelter, stations open and close). Captures at
    /// two moments each: <c>TestResults/FrontEndBackdrop</c>.
    /// </summary>
    public sealed class FrontEndBackdropTests
    {
        private const string Folder = "TestResults/FrontEndBackdrop";
        private GameApp _app;
        private string _saveDir;

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Real(float seconds)
        {
            var until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until) yield return null;
        }

        private static FrontEndAmbience AssertAmbience(string label)
        {
            var ambience = Object.FindFirstObjectByType<FrontEndAmbience>();
            Assert.IsNotNull(ambience, $"{label}: the backdrop carries its ambient life");
            var backdrop = ambience.GetComponent<Image>();
            Assert.IsNotNull(backdrop.sprite, $"{label}: the baked scene is drawn");
            Assert.AreEqual(0, backdrop.transform.GetSiblingIndex(), $"{label}: the backdrop draws first, under every panel");
            Assert.Greater(ambience.Elements, 8, $"{label}: lamps, dust and details are animated");
            foreach (var image in ambience.GetComponentsInChildren<Image>(true).Where(i => i != backdrop))
            {
                Assert.IsFalse(image.raycastTarget, $"{label}: {image.name} never takes input");
                var size = ((RectTransform)image.transform).sizeDelta;
                Assert.LessOrEqual(Mathf.Max(size.x, size.y), 32f, $"{label}: {image.name} stays small");
            }

            return ambience;
        }

        private static string State(FrontEndAmbience ambience) =>
            string.Join(";", ambience.GetComponentsInChildren<Image>(true).Select(i => ((RectTransform)i.transform).anchoredPosition + ":" + i.color.a.ToString("0.00")));

        private static float MaxAlpha(FrontEndAmbience ambience) =>
            ambience.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != ambience.gameObject).Max(i => i.color.a);

        [UnityTest]
        public IEnumerator MainMenuAndShelter_AreLitPlacesWithQuietAmbientLife_AndStillWork()
        {
            Directory.CreateDirectory(Folder);
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_frontend_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);

            // ---- Main Menu ----
            var menu = AssertAmbience("main menu");
            yield return Real(0.5f);
            var a = State(menu);
            LiveDungeonCapture.Capture(Folder, "menu_a", Camera.main, Vector2.zero, LiveDungeonCapture.Height / 64f, 32, includeUi: true);
            var peak = 0f;
            var until = Time.realtimeSinceStartup + 2.2f;
            while (Time.realtimeSinceStartup < until) { peak = Mathf.Max(peak, MaxAlpha(menu)); yield return null; }
            Assert.AreNotEqual(a, State(menu), "the menu backdrop is not static");
            Assert.LessOrEqual(peak, 0.85f, "ambient life stays dim");
            LiveDungeonCapture.Capture(Folder, "menu_b", Camera.main, Vector2.zero, LiveDungeonCapture.Height / 64f, 32, includeUi: true);

            // ---- Shelter ----
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Front End");
            hub.Onboarding.AcknowledgeStarterKit();
            var shelter = AssertAmbience("shelter");
            yield return Real(0.5f);
            var s0 = State(shelter);
            LiveDungeonCapture.Capture(Folder, "shelter_a", Camera.main, Vector2.zero, LiveDungeonCapture.Height / 64f, 32, includeUi: true);
            peak = 0f;
            until = Time.realtimeSinceStartup + 2.2f;
            while (Time.realtimeSinceStartup < until) { peak = Mathf.Max(peak, MaxAlpha(shelter)); yield return null; }
            Assert.AreNotEqual(s0, State(shelter), "the shelter backdrop is not static");
            Assert.LessOrEqual(peak, 0.85f, "ambient life stays dim");
            LiveDungeonCapture.Capture(Folder, "shelter_b", Camera.main, Vector2.zero, LiveDungeonCapture.Height / 64f, 32, includeUi: true);

            // Stations still open over it.
            hub.Hub.Open(BaseStation.Trader);
            yield return Real(0.4f);
            Assert.AreEqual(BaseStation.Trader, hub.Hub.Current);
            LiveDungeonCapture.Capture(Folder, "shelter_trader", Camera.main, Vector2.zero, LiveDungeonCapture.Height / 64f, 32, includeUi: true);
            hub.Hub.Close();
            yield return null;
        }
    }
}
