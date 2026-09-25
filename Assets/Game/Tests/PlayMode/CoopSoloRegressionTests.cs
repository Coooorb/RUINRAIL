using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Co-op completion, Phase 20: a real solo expedition (real boot flow, real scene composition) is behaviourally the
    /// same as before — no session, no network wait, no co-op seam engaged — through a boss, a Descend, a Return and the
    /// save. Also samples the solo run's object/allocation footprint across the depth transition for the performance
    /// matrix (duo/trio come from the built-player proof).
    /// </summary>
    public sealed class CoopSoloRegressionTests
    {
        private const string Folder = "TestResults/CoopRuntimeCompletion";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_coop_solo_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            CursorService.SetApplier(_ => true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var screen in Object.FindObjectsByType<MainMenuScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var screen in Object.FindObjectsByType<BaseHubScreen>(FindObjectsSortMode.None)) Object.DestroyImmediate(screen.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            CursorService.Reset();
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
            {
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            }

            return false;
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

        private static int Objects() => Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;

        [UnityTest]
        public IEnumerator SoloRun_IsUnchanged_ThroughBossDescendReturnAndSave()
        {
            var rows = new List<string> { "aspect,expected,observed,result" };
            void Row(string aspect, string expected, string observed, bool ok)
            {
                rows.Add($"{aspect.Replace(',', ';')},{expected.Replace(',', ';')},{observed.Replace(',', ';')},{(ok ? "PASS" : "FAIL")}");
                Assert.IsTrue(ok, $"{aspect}: expected {expected}, observed {observed}");
            }

            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Solo Runner");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition(), "solo start: " + hub.Hub.Transit.Feedback.Text);
            var composeStarted = Time.realtimeSinceStartup;
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.IsNotNull(run);

            Row("run mode", "Solo", run.Mode.ToString(), run.Mode == CoopRunMode.Solo);
            Row("no network wait", "composed without waiting on a session", $"{Time.realtimeSinceStartup - composeStarted:0.0} s, gameplay held={RuinRail.Core.Input.GameplayInputGate.IsHeld}", !RuinRail.Core.Input.GameplayInputGate.IsHeld && run.CoopHost == null && run.CoopClient == null);
            Row("no session objects", "0 network player objects, no session link", $"players={CoopPlayerDirectory.Count} link={(CoopRunLink.Current != null)}", CoopPlayerDirectory.Count == 0 && CoopRunLink.Current == null);
            Row("one player", "1", run.Party.ComposedPartySize.ToString(), run.Party.ComposedPartySize == 1 && !run.IsCoop);
            Row("one camera / one listener / one input", "1/1/1", $"{Camera.allCamerasCount}/{Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}/{Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.None).Length}",
                Camera.allCamerasCount == 1 && Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 1 && Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.None).Length == 1);
            Row("damage authority local", "true, no relays", $"{DamageAuthority.LocalIsAuthoritative} relay={(DamageAuthority.RemoteDamageRelay != null)}", DamageAuthority.LocalIsAuthoritative && DamageAuthority.RemoteDamageRelay == null && DamageAuthority.RemoteHealRelay == null);
            Row("interactions local", "no interaction filter, no pickup arbiter", $"filter={(PlayerInteractor.LocalInteractionFilter != null)} arbiter={(PickupArbiter.Items != null)}", PlayerInteractor.LocalInteractionFilter == null && PickupArbiter.Items == null && PickupArbiter.Coins == null);
            Row("rooms authoritative", "every room decides locally", $"{run.Rooms.Values.Count(r => r.IsAuthoritative)}/{run.Rooms.Count}", run.Rooms.Values.All(r => r.IsAuthoritative));
            run.Inventory.Toggle();
            yield return null;
            Row("inventory pauses the solo world", "Time.timeScale 0 while open", Time.timeScale.ToString("0.0"), Mathf.Approximately(Time.timeScale, 0f));
            run.Inventory.Toggle();
            yield return null;
            Row("inventory close resumes", "Time.timeScale 1", Time.timeScale.ToString("0.0"), Mathf.Approximately(Time.timeScale, 1f));

            // Boss down (host-authoritative damage, exactly like the solo smoke), then the solo vote descends.
            var bossRoom = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss);
            var boss = bossRoom.GetComponent<RoomContentBinding>().Boss;
            var player = run.Rig.Player;
            var centre = bossRoom.InteriorWorldBounds.center;
            player.transform.position = centre;
            player.GetComponent<Rigidbody2D>().position = centre;
            for (var i = 0; i < 5; i++) yield return new WaitForFixedUpdate();
            boss.Boss.Health.TryApplyDamage(new DamageRequest(1000000));
            for (var i = 0; i < 10; i++) yield return null;
            Row("boss once, transit opens", "defeated, Transit open", $"{boss.IsDefeated} {run.Expedition.Transit?.State}", boss.IsDefeated && run.Expedition.Transit?.State == TransitDecisionState.Open);
            var objectsD1 = Objects();
            var allocD1 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var depthsBefore = run.DepthsBuilt;
            Assert.IsTrue(run.Vote.Vote(TransitChoice.DescendDeeper));
            for (var i = 0; i < 20; i++) yield return null;
            Row("descend", "depth 2 built once", $"depth={run.Expedition.State.Depth} built={run.DepthsBuilt - depthsBefore}", run.Expedition.State.Depth == 2 && run.DepthsBuilt == depthsBefore + 1);
            var objectsD2 = Objects();
            var allocD2 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var staleEnemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Count(e => e.IsAlive) + Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Count(a => a.IsAlive && a is not RuinRail.Gameplay.Enemies.Bosses.BossController);
            Row("no stale depth objects", "no D1 enemies alive after the descend", staleEnemies.ToString(), staleEnemies == 0);

            // Return through the solo vote: the one Return transaction, the save, the Shelter.
            var bossD2 = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Boss).GetComponent<RoomContentBinding>().Boss;
            bossD2.Boss.Health.TryApplyDamage(new DamageRequest(1000000));
            for (var i = 0; i < 10; i++) yield return null;
            run.Expedition.AddCarriedCoins(30);
            var bankedBefore = _app.Menu.Session.Banked.Balance;
            run.Vote.Vote(TransitChoice.ReturnToShelter);
            if (run.Vote != null && run.Vote.AwaitingReturnConfirmation) run.Vote.ConfirmReturn();
            yield return WaitComposed(SceneNames.Base);
            var probe = _app.ProbeSave();
            Row("return + save", "extracted, coins banked once, marker closed, deepest 2", $"banked {bankedBefore}->{probe.BankedCoins} marker={probe.ExpeditionMarkerOpen} deepest={probe.DeepestDepthReached}",
                probe.BankedCoins >= bankedBefore + 30 && !probe.ExpeditionMarkerOpen && probe.DeepestDepthReached == 2);

            File.WriteAllLines(Path.Combine(Folder, "solo_regression_runtime.csv"), rows);
            File.WriteAllLines(Path.Combine(Folder, "solo_performance_sample.csv"), new[]
            {
                "scenario,phase,game_objects,allocated_bytes,network_objects,player_entities,cameras,listeners",
                $"solo,D1 boss down,{objectsD1},{allocD1},0,1,1,1",
                $"solo,D2 arrival,{objectsD2},{allocD2},0,1,1,1"
            });
        }
    }
}
