using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Base;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 147 — the production boot flow: Bootstrap → Main Menu → Base → Dungeon → Base composed from code over the real scenes, content catalog and file persistence; test-only content excluded.</summary>
    public class BootFlowTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_bootflow_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
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

        [Test]
        public void ContentCatalog_IsComplete_AndExcludesTestOnlyContent()
        {
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog, "Resources/GameContentCatalog.asset");
            CollectionAssert.IsEmpty(catalog.Problems());
            Assert.AreEqual(63, catalog.Rooms.Count);
            Assert.IsFalse(catalog.Rooms.Any(r => r.Id.StartsWith("test_") || AssetDatabase.GetAssetPath(r).Contains("/_Test/")), "The grid test fixture room never ships in normal navigation.");
            var buildScenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            CollectionAssert.AreEqual(new[] { "Assets/Game/Scenes/Bootstrap.unity", "Assets/Game/Scenes/MainMenu.unity", "Assets/Game/Scenes/Base.unity", "Assets/Game/Scenes/Dungeon.unity" }, buildScenes);
            Assert.IsFalse(buildScenes.Any(s => s.Contains("_Test")), "Test scenes are not in the build.");
        }

        [UnityTest]
        public IEnumerator BootFlow_MenuToBaseToDungeonAndBack_WithFilePersistence()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            Assert.IsFalse(_app.IsSmoke);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            var menuScreen = Object.FindFirstObjectByType<MainMenuScreen>();
            Assert.IsNotNull(menuScreen);
            Assert.AreEqual("menu.Play", menuScreen.Input.Stack.Focused.Id, "Controller/keyboard focus starts on PLAY.");
            Assert.AreEqual(1, Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length);

            // PLAY through the focus list (the same path the pointer button and Enter use).
            menuScreen.Input.Stack.Activate();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            Assert.IsNotNull(hub);
            Assert.AreEqual(PlayOutcome.NewProfile, _app.Menu.LastOutcome);
            Assert.IsTrue(File.Exists(Path.Combine(_saveDir, "ruinrail_save.json")), "The new profile is on disk (file store).");
            Assert.IsNotNull(hub.Onboarding);
            Assert.IsTrue(hub.Onboarding.SubmitDisplayName("Smoke Runner"));
            hub.Onboarding.AcknowledgeStarterKit();
            var pistol = hub.Session.Loadout.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;

            // Stations are reachable by stepping the station bar; TRANSIT starts the run and loads the Dungeon scene.
            Assert.AreEqual("station.Storage", hub.Input.Stack.Focused.Id);
            hub.StationList.Focus("station.Multiplayer");
            hub.Input.Stack.Activate();
            Assert.IsNotNull(hub.PanelList);
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            hub.PanelList.Focus("transit.start");
            hub.Input.Stack.Activate();
            yield return WaitComposed(SceneNames.Dungeon);
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.IsNotNull(run);
            Assert.IsTrue(run.Expedition.IsExpeditionActive);
            Assert.Greater(run.Rooms.Count, 0, "Rooms composed from the biome pool.");
            Assert.IsNotNull(run.Rig.Player);
            Assert.IsNotNull(run.Rig.Loadout.GetSlot(RuinRail.Gameplay.Combat.Weapons.WeaponSlot.Primary), "Starter pistol mounted from the at-risk inventory.");
            Assert.IsNotNull(run.Camera);
            Assert.IsNotNull(run.Hud);
            Assert.IsTrue(run.Camera.HasTarget);
            for (var i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(run.Rooms.Values.Any(r => r.State.EntryCount > 0), "The player stands in the start room (entry trigger).");
            Assert.AreEqual(1, run.DepthsBuilt);

            // Extract: the service commits, the scene hands back to the Shelter, the save carries the secured pistol.
            run.Expedition.AddCarriedCoins(40);
            var summary = run.Expedition.Return();
            Assert.IsTrue(summary.IsSuccess);
            yield return WaitComposed(SceneNames.Base);
            Assert.AreEqual(40, _app.Menu.Session.Profile.BankedCoins);
            var reloaded = _app.ProbeSave();
            Assert.IsTrue(reloaded.Success);
            Assert.AreEqual(40, reloaded.BankedCoins);
            Assert.AreEqual("Smoke Runner", reloaded.DisplayName);
            CollectionAssert.Contains(reloaded.EquippedInstanceIds, pistol);
            Assert.IsFalse(reloaded.ExpeditionMarkerOpen);
            Assert.AreEqual(4, _app.ComposeCount, "MainMenu, Base, Dungeon, Base.");
        }
    }
}
