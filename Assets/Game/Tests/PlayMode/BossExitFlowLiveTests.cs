using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Leaving a depth after its boss, in a live run, with no in-world transit object: the boss arena holds no
    /// transit art, collider or prompt anywhere; the boss's death opens the post-boss Return / Descend panel and the
    /// Boss Cache as before; and the panel's own vote is the way out — Return lands in the Shelter, Descend builds the
    /// next depth. Captures: <c>TestResults/BossExitFlow</c>.
    /// </summary>
    public sealed class BossExitFlowLiveTests
    {
        private const string Folder = "TestResults/BossExitFlow";
        private GameApp _app;
        private string _saveDir;

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 90f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed"); yield return null; }
        }

        private static IEnumerator Put(GameObject player, Vector2 p)
        {
            var body = player.GetComponent<Rigidbody2D>();
            player.transform.position = p;
            body.position = p;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator BossDies_NoInWorldTransit_PanelIsTheWayOut([Values(TransitChoice.ReturnToShelter, TransitChoice.DescendDeeper)] TransitChoice choice)
        {
            Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_bossexit_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Exit Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var binding = bossRoom.GetComponent<RoomContentBinding>();
            var arena = bossRoom.InteriorWorldBounds;

            // No transit object anywhere in the run's rooms: no art, no collider, no interactable.
            Assert.IsFalse(Object.FindObjectsByType<WorldObjectVisual>(FindObjectsSortMode.None).Any(v => v.Key == WorldObjectArt.TransitCar), "no transit art is drawn");
            Assert.IsNotNull(binding.Transit, "the post-boss hook is still composed");
            Assert.IsNull(binding.Transit.GetComponent<Collider2D>());
            Assert.IsNull(binding.Transit.GetComponentInChildren<WorldObjectVisual>());

            // Into the arena; the intro, then the boss falls.
            yield return Put(player, new Vector2(arena.center.x, arena.yMin + 1.4f));
            var intro = BossIntroSequence.Current;
            var until = Time.time + 5f;
            while (intro != null && intro.IsPlaying && Time.time < until) yield return null;
            health.Heal(health.MaxHealth);
            Assert.IsNull(run.Expedition.Transit, "no decision before the boss falls");
            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
            until = Time.time + 6f;
            while ((run.TransitDecisionView == null || binding.BossCache == null) && Time.time < until) yield return null;

            Assert.IsTrue(binding.Boss.IsDefeated);
            Assert.IsTrue(binding.Transit.IsActivated);
            Assert.IsNotNull(run.Expedition.Transit);
            Assert.AreEqual(TransitDecisionState.Open, run.Expedition.Transit.State);
            Assert.IsNotNull(run.TransitDecisionView, "the post-boss Return / Descend panel is up");
            Assert.IsNotNull(binding.BossCache, "the Boss Cache still drops");
            LiveDungeonCapture.Capture(Folder, $"{choice}_a_after_boss", run.Camera.Camera, run.Camera.Config.PixelsPerUnit);

            // Walk the arena: nowhere offers a transit interaction (no stale prompt where the object used to be).
            var interactor = player.GetComponent<PlayerInteractor>();
            for (var x = arena.xMin + 1f; x < arena.xMax - 1f; x += 2f)
            for (var y = arena.yMin + 1f; y < arena.yMax - 1f; y += 2f)
            {
                yield return Put(player, new Vector2(x, y));
                StringAssert.DoesNotContain("TRANSIT", run.CurrentInteractionPrompt, $"no transit prompt at {x:0},{y:0}");
                Assert.IsFalse(interactor.FindNearestInteractable() is Component c && c.GetComponent<TransitCar>() != null, "nothing interactable is a transit");
            }

            // The Boss Cache opens as before.
            yield return Put(player, (Vector2)binding.BossCache.transform.position + Vector2.down * 1.1f);
            Assert.IsTrue(binding.BossCache.TryOpen(out _));
            Assert.IsFalse(binding.BossCache.TryOpen(out _));

            // Leave through the panel's own vote.
            var depthBefore = run.Expedition.State.Depth;
            var expedition = run.Expedition;
            Assert.IsTrue(run.Vote.Vote(choice), "the panel takes the choice");
            if (choice == TransitChoice.ReturnToShelter)
            {
                yield return WaitComposed(SceneNames.Base);
                Assert.AreEqual(ExpeditionOutcome.Extracted, expedition.LastSummary.Outcome, "returned to the Shelter: extracted");
            }
            else
            {
                until = Time.realtimeSinceStartup + 30f;
                while (run.Expedition.State.Depth == depthBefore && Time.realtimeSinceStartup < until) yield return null;
                for (var i = 0; i < 20; i++) yield return null;
                Assert.AreEqual(depthBefore + 1, run.Expedition.State.Depth, "descended to the next depth");
                Assert.IsNull(run.Expedition.Transit, "the new depth starts without an open decision");
                var nextBoss = run.Rooms[run.Generation.Graph.BossId].GetComponent<RoomContentBinding>();
                Assert.IsNotNull(nextBoss.Transit);
                Assert.IsFalse(nextBoss.Transit.IsActivated);
                LiveDungeonCapture.Capture(Folder, $"{choice}_b_next_depth", run.Camera.Camera, run.Camera.Config.PixelsPerUnit);
            }
        }
    }
}
