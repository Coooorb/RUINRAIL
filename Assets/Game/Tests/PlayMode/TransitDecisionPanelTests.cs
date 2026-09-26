using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.UI.Hud;
using RuinRail.UI.Multiplayer;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The post-boss Transit decision as a graphical panel at the top of the run screen (dungeon/60, ui/91): two real
    /// buttons over the existing vote (<see cref="TransitVoteViewModel"/> + <see cref="ScreenNavigation.TransitVote"/>),
    /// the co-op tally / pending voters / Return warning on it, mouse + keyboard/controller through the same focus list,
    /// and in a live run: no input bleed before the player takes it, and DESCEND / RETURN reach the same outcomes.
    /// Captures: TestResults/PolishPreview/transit_*.png, TestResults/RegressionProof/transit_*.png.
    /// </summary>
    public sealed class TransitDecisionPanelTests
    {
        private readonly List<Object> _created = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_transit_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory("TestResults/RegressionProof");
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = null;
            GameplayInputGate.Reset();
            CursorService.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private Transform Canvas()
        {
            var go = new GameObject("TestCanvas");
            _created.Add(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(UiTheme.ScreenWidth, UiTheme.ScreenHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        private static (string, string) Context() => ("DEPTH 3  >  4", "BEST DEPTH 3  ·  240 C AT RISK");

        // ---------------------------------------------------------------- the panel over the real vote

        [UnityTest]
        public IEnumerator CoopPanel_ShowsTallies_PendingVoters_DeadPlayersVote_AndTheReturnWarning_ThroughItsButtons()
        {
            var decision = new TransitDecision(new PartyTransitPolicy(), new[] { "a", "b" }, new[] { "c" });
            decision.Open();
            var vote = new TransitVoteViewModel(decision, "a", id => id.ToUpperInvariant());
            var list = ScreenNavigation.TransitVote(vote);
            var canvas = Canvas();
            var view = TransitDecisionView.Create(canvas, vote, list, Context);
            yield return null;

            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual("TRANSIT READY", view.TitleText);
            StringAssert.Contains("DEPTH 3", view.DepthText);
            Assert.IsTrue(view.IsButtonShown(TransitDecisionView.ReturnId) && view.IsButtonShown(TransitDecisionView.DescendId));
            Assert.IsFalse(view.IsButtonShown(TransitDecisionView.ConfirmId));
            StringAssert.StartsWith("RETURN TO SHELTER", view.ButtonText(TransitDecisionView.ReturnId));
            StringAssert.EndsWith("0/2", view.ButtonText(TransitDecisionView.DescendId), "co-op: the tally over the living voters");
            UiScreenCapture.Capture("transit_01_coop_open");

            // Mouse: DESCEND — the tally and the status follow; the button stays marked as this player's vote.
            view.Buttons[TransitDecisionView.DescendId].SimulateClick();
            yield return null;
            Assert.AreEqual(TransitChoice.DescendDeeper, vote.LocalVote, "the click cast the vote through the existing view model");
            StringAssert.EndsWith("1/2", view.ButtonText(TransitDecisionView.DescendId));
            StringAssert.Contains("YOUR VOTE: DESCEND", view.StatusText);
            StringAssert.Contains("WAITING FOR: B", view.StatusText);
            Assert.IsTrue(view.Buttons[TransitDecisionView.DescendId].ShowsSelectedMarker, "this player's vote stays visibly marked");
            Assert.AreEqual(TransitDecisionState.Open, decision.State, "B has not voted: nothing resolves locally");
            UiScreenCapture.Capture("transit_02_coop_voted_waiting");

            // RETURN while C is Dead: the warning replaces the choice with CONFIRM / CANCEL; CANCEL keeps the vote.
            view.Buttons[TransitDecisionView.ReturnId].SimulateClick();
            yield return null;
            Assert.IsTrue(vote.AwaitingReturnConfirmation);
            Assert.IsTrue(view.IsButtonShown(TransitDecisionView.ConfirmId) && view.IsButtonShown(TransitDecisionView.CancelId));
            Assert.IsFalse(view.IsButtonShown(TransitDecisionView.ReturnId));
            StringAssert.Contains("C is Dead", view.StatusText);
            UiScreenCapture.Capture("transit_03_coop_return_warning");
            view.Buttons[TransitDecisionView.CancelId].SimulateClick();
            yield return null;
            Assert.IsFalse(vote.AwaitingReturnConfirmation);
            Assert.AreEqual(TransitChoice.DescendDeeper, vote.LocalVote);

            // A dead player's panel: no vote, said so, and no way to force one.
            var dead = new TransitVoteViewModel(decision, "c", id => id.ToUpperInvariant());
            var deadView = TransitDecisionView.Create(canvas, dead, ScreenNavigation.TransitVote(dead), Context);
            yield return null;
            Assert.AreEqual("DEAD PLAYERS HAVE NO VOTE.", deadView.StatusText);
            Assert.IsFalse(deadView.Buttons[TransitDecisionView.ReturnId].Item.IsEnabled);
            deadView.Buttons[TransitDecisionView.ReturnId].SimulateClick();
            Assert.IsNull(dead.LocalVote);
            Assert.AreEqual(string.Empty, deadView.HintText, "nothing to take");

            vote.Dispose();
            dead.Dispose();
        }

        [UnityTest]
        public IEnumerator SoloPanel_KeyboardControllerFocus_StepsBetweenTheTwoButtons_AndFitsTheTopOfTheScreen()
        {
            var decision = new TransitDecision(new SoloTransitPolicy(), new[] { "solo" });
            decision.Open();
            var vote = new TransitVoteViewModel(decision, "solo");
            var list = ScreenNavigation.TransitVote(vote);
            var view = TransitDecisionView.Create(Canvas(), vote, list, Context);
            yield return null;
            Assert.AreEqual("RETURN TO SHELTER", view.ButtonText(TransitDecisionView.ReturnId), "solo: no tally");
            Assert.AreEqual("DESCEND DEEPER", view.ButtonText(TransitDecisionView.DescendId));
            StringAssert.Contains("F: CHOOSE", view.HintText);

            view.SetEngaged(true);
            Assert.AreEqual(TransitDecisionView.ReturnId, list.Focused.Id, "focus starts on RETURN");
            Assert.IsTrue(view.Buttons[TransitDecisionView.ReturnId].ShowsFocusBrackets, "the focused button is obvious");
            StringAssert.Contains("ENTER: CONFIRM", view.HintText);
            var stack = new FocusStack();
            stack.Push(list);
            Assert.IsTrue(stack.Navigate(Vector2Int.right));
            Assert.AreEqual(TransitDecisionView.DescendId, list.Focused.Id, "right steps to DESCEND");
            Assert.IsFalse(stack.Navigate(Vector2Int.right), "no dead focus past the last button");
            Assert.IsFalse(stack.Navigate(Vector2Int.up) || stack.Navigate(Vector2Int.down), "up/down do not wander off");
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            view.Render();
            StringAssert.Contains("A: CONFIRM", view.HintText, "controller wording");
            UiScreenCapture.Capture("transit_04_solo_focus_descend");
            Assert.IsTrue(stack.Activate());
            Assert.IsTrue(vote.IsResolved);
            Assert.AreEqual(TransitChoice.DescendDeeper, decision.Result, "confirm on the focused button casts that vote");

            // Layout: the panel sits at the top, inside the reference screen, clear of the minimap and the coin readout.
            var panel = TransitDecisionView.Panel;
            Assert.IsTrue(panel.Within(ScreenLayout.Screen));
            Assert.LessOrEqual(panel.Bottom, 90, "compact: only the top band of the play area");
            Assert.GreaterOrEqual(panel.X, 110, "clear of the minimap");
            Assert.LessOrEqual(panel.Right, 530, "clear of the coin readout");
            foreach (var text in view.GetComponentsInChildren<Text>()) Assert.IsFalse(text.text.EndsWith("…"), $"'{text.text}' fits");
            vote.Dispose();
        }

        // ---------------------------------------------------------------- the real post-boss flow

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static IEnumerator KillTheBoss(ExpeditionScene run)
        {
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var player = run.Rig.Player;
            var centre = EncounterRewardPlacement.WorldCenter(bossRoom.Root).Value;
            player.transform.position = centre;
            player.GetComponent<Rigidbody2D>().position = centre;
            for (var i = 0; i < 6; i++) yield return new WaitForFixedUpdate();
            BossIntroSequence.Current?.Finish();
            player.GetComponent<HealthComponent>().Heal(100000);
            bossRoom.GetComponent<RoomContentBinding>().Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
            var deadline = Time.realtimeSinceStartup + 20f;
            while (run.TransitDecisionView == null) { Assert.Less(Time.realtimeSinceStartup, deadline, "the decision opened"); yield return null; }
            yield return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_PostBossPanel_NoInputBleed_KeyboardDescendsAndMouseReturns_WithTheExistingOutcomes()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(11);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Transit Rider");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;
            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var menuInput = Object.FindFirstObjectByType<MenuInput>();

            Assert.IsNull(run.TransitDecisionView, "no panel before the boss falls");
            yield return KillTheBoss(run);
            var view = run.TransitDecisionView;
            Assert.IsTrue(view.IsVisible, "the decision appears as the top panel after the boss");
            Assert.IsFalse(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Any(t => t.text.Contains("RETURN TO SHELTER or DESCEND DEEPER")), "the prototype text is gone");
            Assert.IsFalse(menuInput.Stack.Contains(view.FocusList), "until taken, the panel is not on the menu stack: Space (dash), E / A (interact), WASD never touch the vote");
            Assert.IsFalse(GameplayInputGate.IsHeld, "the world stays playable (the Boss Cache is still to open)");
            LiveDungeonCapture.Capture("TestResults/RegressionProof", "transit_live_01_panel_after_boss", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);

            // Mouse on the panel: gameplay input is held so a click cannot also fire; off it, released.
            view.Buttons[TransitDecisionView.DescendId].SimulateHover(true);
            yield return null;
            Assert.IsTrue(GameplayInputGate.IsHeld, "hovering the panel holds gameplay input");
            view.Buttons[TransitDecisionView.DescendId].SimulateHover(false);
            yield return null;
            yield return null;
            Assert.IsFalse(GameplayInputGate.IsHeld, "leaving it returns input to the game");

            // Keyboard / controller: take the panel (F / D-pad up), step right, confirm DESCEND.
            typeof(ExpeditionScene).GetMethod("EngageVotePanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(run, null);
            Assert.IsTrue(run.TransitPanelEngaged);
            Assert.IsTrue(GameplayInputGate.IsHeld, "while the panel owns focus, gameplay input is held");
            Assert.AreSame(view.FocusList, menuInput.Stack.Current);
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.right));
            Assert.AreEqual(TransitDecisionView.DescendId, view.FocusList.Focused.Id);
            LiveDungeonCapture.Capture("TestResults/RegressionProof", "transit_live_02_keyboard_focus_descend", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
            var depthBefore = run.Expedition.State.Depth;
            menuInput.Stack.Activate();
            var deadline = Time.realtimeSinceStartup + 20f;
            while (run.Expedition.State.Depth == depthBefore) { Assert.Less(Time.realtimeSinceStartup, deadline, "DESCEND built the next depth"); yield return null; }
            yield return null;
            yield return null;
            Assert.AreEqual(depthBefore + 1, run.Expedition.State.Depth, "the existing Descend outcome");
            Assert.IsNull(run.TransitDecisionView, "the panel is gone once the decision resolved");
            Assert.IsFalse(run.TransitPanelEngaged);
            Assert.IsFalse(GameplayInputGate.IsHeld, "every hold was returned");
            Assert.IsFalse(menuInput.Stack.Contains(view.FocusList));

            // Depth 2: the next boss, and RETURN by mouse click — the existing Return outcome (back to the Shelter).
            yield return KillTheBoss(run);
            view = run.TransitDecisionView;
            Assert.IsTrue(view.IsVisible);
            view.Buttons[TransitDecisionView.ReturnId].SimulateHover(true);
            yield return null;
            view.Buttons[TransitDecisionView.ReturnId].SimulateClick();
            yield return WaitComposed(SceneNames.Base);
            Assert.AreEqual(ExpeditionOutcome.Extracted, _app.Menu.Session.Expedition.LastSummary.Outcome, "RETURN extracted the run");
            Assert.IsFalse(GameplayInputGate.IsHeld);
        }
    }
}
