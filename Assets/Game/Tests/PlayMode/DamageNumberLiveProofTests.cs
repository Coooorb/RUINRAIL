using System.Collections;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Expedition;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Damage numbers in real combat (boot → Shelter → dungeon, one run per biome): damage dealt to a live enemy (small
    /// and large values), damage the player takes, and a hit on the boss, captured from the run camera over each biome's
    /// floor. Captures: TestResults/RegressionProof/damage_numbers_*.png.
    /// </summary>
    public sealed class DamageNumberLiveProofTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_numbers_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory("TestResults/RegressionProof");
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

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
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = null;
            RuinRail.Core.Input.GameplayInputGate.Reset();
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();
        }

        private static void Frame(ExpeditionScene run, Vector2 world)
        {
            var cam = run.Camera.Camera.transform;
            cam.position = new Vector3(world.x, world.y, cam.position.z);
        }

        [UnityTest] public IEnumerator RealCombat_RuinedMetro() => RealCombat(Biome.RuinedMetro);
        [UnityTest] public IEnumerator RealCombat_Rustworks() => RealCombat(Biome.Rustworks);
        [UnityTest] public IEnumerator RealCombat_OvergrownLabs() => RealCombat(Biome.OvergrownLabs);

        private IEnumerator RealCombat(Biome biome)
        {
            {
                _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
                _app.SetRunSeedOverride(SeedFor(biome));
                SceneManager.LoadScene(SceneNames.MainMenu);
                yield return WaitComposed(SceneNames.MainMenu);
                _app.Menu.Play();
                yield return WaitComposed(SceneNames.Base);
                var hub = Object.FindFirstObjectByType<BaseHubScreen>();
                hub.Onboarding.SubmitDisplayName("Number Reader");
                hub.Onboarding.AcknowledgeStarterKit();
                Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
                hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
                Assert.IsTrue(hub.Hub.Transit.StartExpedition());
                yield return WaitComposed(SceneNames.Dungeon);
                for (var i = 0; i < 12; i++) yield return null;

                var run = Object.FindFirstObjectByType<ExpeditionScene>();
                var numbers = Object.FindFirstObjectByType<DamageNumberPool>();
                var player = run.Rig.Player;
                var playerHealth = player.GetComponent<HealthComponent>();
                playerHealth.SetInvulnerabilityState(null);

                // A live combat room: enter, let the encounter spawn, hit two enemies (a small and a large value).
                var combat = run.Rooms.Values.First(r => r.State.RoomType == RoomType.Combat && !r.State.IsElite && r.HasEncounter);
                yield return Teleport(run, combat.InteriorWorldBounds.center);
                var deadline = Time.time + 5f;
                while (Time.time < deadline && !Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Any(e => e.IsAlive)) yield return null;
                var enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e.IsAlive).OrderBy(e => Vector2.Distance(e.transform.position, player.transform.position)).Take(2).ToList();
                Assert.Greater(enemies.Count, 0, $"{biome}: enemies in the room");
                var shownBefore = numbers.Shown;
                enemies[0].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(7));
                if (enemies.Count > 1) enemies[1].GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(enemies[1].GetComponent<HealthComponent>().CurrentHealth - 1));
                playerHealth.TryApplyDamage(new DamageRequest(12));
                Assert.GreaterOrEqual(numbers.Shown - shownBefore, 2, $"{biome}: numbers shown for the hits");
                Assert.IsTrue(numbers.LiveNumbers.Any(n => n.Kind == DamageNumberKind.Taken), $"{biome}: the player's hit reads as damage taken");
                Assert.IsTrue(numbers.LiveNumbers.All(n => n.UsesPixelFont));
                for (var i = 0; i < 6; i++) yield return null; // a few frames into the rise
                var centre = enemies.Aggregate(Vector2.zero, (sum, e) => sum + (Vector2)e.transform.position) / enemies.Count;
                Frame(run, Vector2.Lerp(centre, player.transform.position, 0.5f));
                LiveDungeonCapture.Capture("TestResults/RegressionProof", $"damage_numbers_{biome}_combat", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
                playerHealth.Heal(100000);

                // The boss: a hit on its large body.
                var bossRoom = run.Rooms[run.Generation.Graph.BossId];
                var boss = bossRoom.GetComponent<RoomContentBinding>().Boss.Boss;
                yield return Teleport(run, (Vector2)boss.transform.position + Vector2.down * 3f);
                BossIntroSequence.Current?.Finish();
                for (var i = 0; i < 6; i++) yield return null;
                boss.Health.TryApplyDamage(new DamageRequest(boss.Health.MaxHealth / 10));
                for (var i = 0; i < 6; i++) yield return null;
                Frame(run, (Vector2)boss.transform.position + Vector2.down * 1f);
                LiveDungeonCapture.Capture("TestResults/RegressionProof", $"damage_numbers_{biome}_boss", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);

            }
        }
    }
}
