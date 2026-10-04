using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Telegraphs in a live run at 640×360, composed by the shipping presentation seams: a shooter, a charger, a bomber,
    /// a Brute and an Elite all attacking the player at once (busy combat), with the player backed against a wall (edge
    /// position). Every marker is painted on the ground layer below characters, projectiles and pickups, unrotated on the
    /// world grid; captures are taken mid-telegraph, in the final warning, on impact and while danger lingers.
    /// Captures: TestResults/TelegraphLive.
    /// </summary>
    public sealed class TelegraphLiveCaptureTests
    {
        private const string Folder = "TestResults/TelegraphLive";
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

        [UnityTest]
        public IEnumerator LiveRun_BusyCombatAtAWall_EveryTelegraphReadsOnTheGround_AndCaptures([Values(11, 27)] int seed)
        {
            Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_telegraph_live_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Telegraph Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var content = _app.Content;
            var biome = run.Expedition.State.Biome;
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var room = run.Rooms[run.Generation.Graph.StartId];
            var interior = room.InteriorWorldBounds;

            // Edge position: the player stands one tile off the room's west wall, the attackers spread east of them.
            var stand = new Vector2(interior.xMin + 1.1f, interior.center.y);
            var body = player.GetComponent<Rigidbody2D>();
            player.transform.position = stand;
            body.position = stand;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();

            var indicators = new List<(string Label, TelegraphIndicator Indicator)>();
            void Normal(string id, Vector2 offset)
            {
                var definition = content.Enemies.First(d => d.Id == id);
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(definition, stand + offset, player.transform);
                if (enemy.GetComponent<ProjectilePool>() == null) enemy.gameObject.AddComponent<ProjectilePool>();
                room.BindEncounterBounds(enemy.gameObject);
                run.BindEnemyPresentation(enemy);
                indicators.Add((id, enemy.GetComponent<TelegraphIndicator>()));
            }

            Normal("shooter", new Vector2(5.5f, 2.2f));
            Normal("charger", new Vector2(4.5f, -0.4f));
            Normal(content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Lob).Id, new Vector2(6f, -2.4f));
            Normal(content.Enemies.First(e => e.AttackKind == EnemyAttackKind.Moveset).Id, new Vector2(2f, 1.2f));
            var eliteDefinition = content.Elites.First(e => e.Biome == biome);
            var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(eliteDefinition, stand + new Vector2(3.2f, -2.6f), room.transform, player.transform);
            room.BindEncounterBounds(encounter.Elite.gameObject);
            run.BindActorPresentation(encounter.Elite, eliteDefinition.Id, true);
            indicators.Add((eliteDefinition.Id, encounter.Elite.GetComponent<TelegraphIndicator>()));
            Assert.IsTrue(indicators.All(i => i.Indicator != null), "the shipping seams composed a telegraph on every attacker");
            yield return null;
            foreach (var (label, indicator) in indicators) Assert.AreEqual(0, indicator.Impacts, label + ": no impact before any attack");

            var cam = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var captured = new HashSet<string>();
            var showingSeen = new HashSet<string>();
            var warningSeen = 0;
            var impactsBefore = indicators.ToDictionary(i => i.Indicator, i => i.Indicator.Impacts);
            var lingerSeen = new HashSet<string>();
            var deadline = Time.time + 9f;
            void Shot(string name)
            {
                if (!captured.Add(name) || captured.Count > 12) return;
                LiveDungeonCapture.Capture(Folder, $"{biome}_{seed}_{captured.Count:00}_{name}", cam, ppu, includeUi: true);
            }

            while (Time.time < deadline)
            {
                if (health.IsAlive) health.Heal(health.MaxHealth);
                var showing = 0;
                foreach (var (label, indicator) in indicators)
                {
                    if (indicator == null || !indicator.IsShowing) continue;
                    showing++;
                    showingSeen.Add(label);
                    var view = indicator.View;
                    Assert.IsTrue(view != null && view.IsVisible, label + ": the marker is painted");
                    Assert.AreEqual(SortingConvention.LayerOf(SortingRole.Hazard), view.Renderer.sortingLayerName, label + ": on the ground layer, under characters, projectiles and pickups");
                    Assert.AreEqual(Quaternion.identity, view.transform.rotation, label + ": on the world pixel grid");
                    if (indicator.IsWarning) warningSeen++;
                    if (indicator.IsLive) lingerSeen.Add(label);
                    if (indicator.Fill01 > 0.45f && indicator.Fill01 < 0.6f) Shot($"{label}_mid");
                    if (indicator.IsWarning) Shot($"{label}_warning");
                    if (indicator.IsLive) Shot($"{label}_live");
                }

                foreach (var (label, indicator) in indicators)
                    if (indicator != null && indicator.Impacts > impactsBefore[indicator]) { Shot($"{label}_impact"); impactsBefore[indicator] = indicator.Impacts; }
                if (showing >= 3) Shot("busy");
                yield return null;
            }

            Debug.Log($"[TELEGRAPH-LIVE] {biome} seed {seed}: showed {string.Join(",", showingSeen)}; lingering {string.Join(",", lingerSeen)}; warning frames {warningSeen}; captures {captured.Count}");
            Assert.GreaterOrEqual(showingSeen.Count, 4, "most attackers telegraphed in the window");
            Assert.Greater(warningSeen, 0, "the final warning was seen");
        }
    }
}
