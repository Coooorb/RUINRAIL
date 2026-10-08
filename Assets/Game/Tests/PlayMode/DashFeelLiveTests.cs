using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Base;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Dash presentation in a live run at 640×360, composed by the shipping seams (PlayerVisualComposer): the same dash
    /// down a clear lane past a frozen enemy, once with the presentation off and once on — captured at launch, mid-dash
    /// and just after — and the dash itself (distance, duration, iFrames) identical both ways. Captures: TestResults/DashFeel.
    /// </summary>
    public sealed class DashFeelLiveTests
    {
        private const string Folder = "TestResults/DashFeel";

        private GameApp _app;
        private string _saveDir;
        private readonly StringBuilder _evidence = new();

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_dashfeel_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
        }

        [TearDown]
        public void TearDown()
        {
            File.AppendAllText(Path.Combine(Folder, "dash_feel_evidence.txt"), _evidence.ToString());
            _evidence.Clear();
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

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 10; i++) yield return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            var until = Time.time + seconds;
            while (Time.time < until) yield return null;
        }

        /// <summary>A horizontal lane of <paramref name="length"/> tiles inside the room with nothing solid along it.</summary>
        private static (Vector2 from, Vector2 to)? ClearLane(Rect interior, float length)
        {
            for (var y = interior.center.y; y < interior.yMax - 1f; y += 0.5f)
            for (var sign = -1; sign <= 1; sign += 2)
            {
                var row = interior.center.y + (y - interior.center.y) * sign;
                for (var x = interior.xMin + 1.2f; x + length < interior.xMax - 0.8f; x += 0.5f)
                {
                    var from = new Vector2(x, row);
                    var to = from + Vector2.right * length;
                    if (!RoomRuntime.IsSpawnClear(from) || !RoomRuntime.IsSpawnClear(to)) continue;
                    if (Physics2D.CircleCastAll(from, 0.45f, Vector2.right, length).Any(h => h.collider != null && !h.collider.isTrigger && h.collider.GetComponentInParent<EnvironmentObstacle>() != null)) continue;
                    return (from, to);
                }
            }

            return null;
        }

        [UnityTest]
        public IEnumerator LiveRun_Dash_ShowsBurstAndAfterimages_WithoutChangingTheDash([Values(11, 27)] int seed)
        {
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Dash Feel");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var biome = run.Expedition.State.Biome;
            var player = run.Rig.Player;
            var dash = player.GetComponent<PlayerDash>();
            var trail = player.GetComponent<DashTrailVfx>();
            Assert.IsNotNull(trail, "the shipping player composition carries the dash presentation");
            Assert.IsTrue(trail.Pool.HasArtFor("dash_burst"), "the dash burst art is bound in the content catalog");

            var room = run.Rooms[run.Generation.Graph.StartId];
            var lane = ClearLane(room.InteriorWorldBounds, 5f);
            Assert.IsTrue(lane.HasValue, "a clear 5-tile lane in the start room");
            var (from, laneEnd) = lane.Value;

            // A frozen enemy just off the lane: the trail must sit behind actors, never over them.
            var enemyAt = from + new Vector2(2.2f, 0.9f);
            var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset), enemyAt, player.transform);
            room.BindEncounterBounds(enemy.gameObject);
            run.BindEnemyPresentation(enemy);
            enemy.enabled = false;
            enemy.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var ortho = LiveDungeonCapture.Height / (2f * ppu);
            var framing = from + new Vector2(2f, 0.3f);
            _evidence.AppendLine($"biome {biome} seed {seed} lane {from}->{laneEnd}");

            (float distance, float duration, bool iFramesOnLaunch) result = default;
            IEnumerator DashOnce(string tag)
            {
                yield return Teleport(run, from);
                yield return Wait(Mathf.Max(0.1f, dash.CooldownRemaining + 0.1f));
                var body = player.GetComponent<Rigidbody2D>();
                var start = body.position;
                var started = Time.time;
                var startedFixed = Time.fixedTime;
                var endedFixed = -1f;
                void OnEnded(PlayerDash _) => endedFixed = Time.fixedTime; // raised inside the physics step that ends it
                dash.DashEnded += OnEnded;
                Assert.IsTrue(dash.TryStartDash(Vector2.right), tag + ": dash starts");
                var iFrames = dash.IsInvulnerable;
                var shots = new[] { (0.02f, "launch"), (0.09f, "mid") };
                foreach (var (at, name) in shots)
                {
                    while (Time.time < started + at && dash.IsDashing) yield return null;
                    yield return null; // one more frame: the last frame's LateUpdate has emitted its trail (no WaitForEndOfFrame under -nographics)
                    LiveDungeonCapture.Capture(Folder, $"dash_{biome}_{tag}_{name}", cam, framing, ortho, ppu, true);
                    LiveDungeonCapture.Capture(Folder, $"dash_{biome}_{tag}_{name}_zoom", cam, framing, ortho * 0.5f, ppu * 2, false);
                }

                while (dash.IsDashing) { Assert.Less(Time.time, started + 2f, tag + ": dash ends"); yield return null; }
                dash.DashEnded -= OnEnded;
                Assert.Greater(endedFixed, 0f, tag + ": DashEnded raised");
                var duration = endedFixed - startedFixed; // physics steps: immune to capture stalls
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                var distance = Vector2.Distance(start, body.position);
                yield return Wait(0.04f);
                LiveDungeonCapture.Capture(Folder, $"dash_{biome}_{tag}_after", cam, framing, ortho, ppu, true);
                result = (distance, duration, iFrames);
                _evidence.AppendLine($"  {tag}: distance {distance:0.000} duration {duration:0.000} iFrames {iFrames}");
            }

            trail.enabled = false;
            yield return DashOnce("off");
            var off = result;
            Assert.AreEqual(0, trail.DashesShown, "presentation off: nothing shown");

            trail.enabled = true;
            yield return DashOnce("on");
            var on = result;

            // Gameplay unchanged: the same dash with and without its presentation.
            Assert.AreEqual(off.distance, on.distance, 0.05f, "dash distance");
            Assert.AreEqual(off.duration, on.duration, Time.fixedDeltaTime + 0.001f, "dash duration");
            Assert.AreEqual(off.iFramesOnLaunch, on.iFramesOnLaunch, "iFrames on launch");

            Assert.AreEqual(1, trail.DashesShown);
            Assert.AreEqual(1, trail.BurstsShown);
            Assert.GreaterOrEqual(trail.GhostsShown, 4, "an afterimage trail along the ~3-tile dash");
            Assert.LessOrEqual(trail.GhostsShown, DashTrailVfx.MaxGhostsPerDash);

            // Nothing lingers: the effect is gone shortly after the dash.
            yield return Wait(0.4f);
            Assert.AreEqual(0, trail.Pool.Live, "every dash effect returned to the pool");
            _evidence.AppendLine($"  ghosts {trail.GhostsShown} bursts {trail.BurstsShown}");
        }
    }
}
