using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Expedition;
using RuinRail.Networking;
using RuinRail.Presentation;
using RuinRail.Presentation.Animation;
using RuinRail.Presentation.Vfx;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Biome enemy palettes in the running game: in a real run of each biome, every shipped normal enemy and every Elite
    /// — bound through the run's own presentation seams (and a co-op client's replica seam) — wears that biome's tint
    /// (Elites at half strength, so they keep more of their authored colour), the hit flash still replaces it and returns
    /// to it, and nothing else about the actor changes. Captures: TestResults/RegressionProof/enemy_tint_*.png.
    /// </summary>
    public sealed class EnemyBiomeTintLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_enemy_tint_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory(Folder);
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
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

        private static void AssertColor(Color expected, Color actual, string what) =>
            Assert.IsTrue(Mathf.Abs(expected.r - actual.r) < 0.01f && Mathf.Abs(expected.g - actual.g) < 0.01f && Mathf.Abs(expected.b - actual.b) < 0.01f, $"{what}: expected {expected}, got {actual}");

        [UnityTest]
        public IEnumerator LiveRun_EveryNormalEnemyAndElite_WearsTheBiomePalette_EliteStrongerAndHitFlashStillReads(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            var content = GameContentCatalog.Load();
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Palette Check");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var player = run.Rig.Player;
            var health = player.GetComponent<HealthComponent>();
            var normalTint = EnemyBiomeTint.For(biome, elite: false);
            var eliteTint = EnemyBiomeTint.For(biome, elite: true);
            Assert.Greater(Mathf.Min(eliteTint.r, eliteTint.g, eliteTint.b), Mathf.Min(normalTint.r, normalTint.g, normalTint.b), "an Elite keeps more of its authored colour than a normal enemy");

            // The line-up in the start room: every shipped normal enemy on two rows, every Elite below.
            var centre = (Vector2)run.Rooms[run.Generation.Graph.StartId].InteriorWorldBounds.center;
            var enemies = content.Enemies.Where(d => d != null).ToList();
            var normals = new List<EnemyController>();
            for (var i = 0; i < enemies.Count; i++)
            {
                var row = i / 6;
                var position = centre + new Vector2(-6f + (i % 6) * 2.4f, 3.6f - row * 2.2f);
                var enemy = new DefaultEnemySpawner(content.Stagger).Spawn(enemies[i], position, null);
                enemy.enabled = false; // a still line-up: the AI would walk out of the frame
                enemy.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                run.BindEnemyPresentation(enemy);
                normals.Add(enemy);
            }

            var elites = new List<EliteController>();
            for (var i = 0; i < content.Elites.Count; i++)
            {
                var encounter = new DefaultEliteSpawner(content.Stagger).Spawn(content.Elites[i], centre + new Vector2(-7.5f + i * 3f, -3.4f), null, null);
                encounter.Elite.enabled = false;
                encounter.Elite.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                run.BindActorPresentation(encounter.Elite, content.Elites[i].Id, isElite: true);
                elites.Add(encounter.Elite);
            }

            Assert.AreEqual(8, elites.Count, "all eight shipped Elites");
            for (var i = 0; i < 6; i++) { health.Heal(100000); yield return null; }
            foreach (var enemy in normals) AssertColor(normalTint, CharacterVisual.RendererOf(enemy.gameObject).color, enemy.Definition.Id);
            foreach (var elite in elites) AssertColor(eliteTint, CharacterVisual.RendererOf(elite.gameObject).color, elite.Definition.Id);
            Assert.IsTrue(normals.All(e => CharacterVisual.RendererOf(e.gameObject).sprite != null), "every body still draws its own sprite");

            var cam = run.Camera.Camera.transform;
            cam.position = new Vector3(centre.x, centre.y, cam.position.z);
            player.transform.position = centre + new Vector2(0f, 0.3f);
            yield return null;
            LiveDungeonCapture.Capture(Folder, $"enemy_tint_{biome}", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);

            // Combat feedback over the tint: the flash replaces the resting colour for its frames and returns to the tint.
            var config = content.Feedback;
            var victim = normals[0];
            var body = CharacterVisual.RendererOf(victim.gameObject);
            victim.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(1));
            AssertColor(config.HitFlashColor, body.color, "normal enemy hit flash");
            LiveDungeonCapture.Capture(Folder, $"enemy_tint_{biome}_hit_flash", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
            for (var f = 0f; f < config.HitFlashSeconds + 0.3f; f += Time.deltaTime) yield return null;
            AssertColor(normalTint, body.color, "back to the biome tint after the flash");
            var eliteBody = CharacterVisual.RendererOf(elites[0].gameObject);
            elites[0].Health.TryApplyDamage(new DamageRequest(1));
            AssertColor(config.HitFlashColor, eliteBody.color, "Elite hit flash");
            for (var f = 0f; f < config.HitFlashSeconds + 0.3f; f += Time.deltaTime) yield return null;
            AssertColor(eliteTint, eliteBody.color, "Elite back to its tint");

            // A co-op client's replicas take the same palette through the replica seam.
            var bind = typeof(ExpeditionScene).GetMethod("BindReplicaPresentation", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(bind, "the client's replica presentation seam");
            EnemyReplica Replica(uint id, CoopActorKind kind, string definitionId, Vector2 at)
            {
                var go = new GameObject("TestReplica_" + id);
                var replica = go.AddComponent<EnemyReplica>();
                replica.Initialize(new EnemySpawnRecord { NetId = id, DefinitionId = definitionId, Position = at, IsBossOrElite = kind != CoopActorKind.Normal });
                bind.Invoke(run, new object[] { replica, new EnemySpawnMessage { NetId = id, Kind = (int)kind, DefinitionId = definitionId, X = at.x, Y = at.y } });
                return replica;
            }

            var normalReplica = Replica(9001, CoopActorKind.Normal, enemies[0].Id, centre + new Vector2(0f, -6f));
            var eliteReplica = Replica(9002, CoopActorKind.Elite, content.Elites[0].Id, centre + new Vector2(3f, -6f));
            AssertColor(normalTint, CharacterVisual.RendererOf(normalReplica.gameObject).color, "client replica of a normal enemy");
            AssertColor(eliteTint, CharacterVisual.RendererOf(eliteReplica.gameObject).color, "client replica of an Elite");
            Debug.Log($"[PROOF] {biome}: {normals.Count} normal enemies at {normalTint}, {elites.Count} Elites at {eliteTint}; flash {config.HitFlashColor} returns to the tint; replicas match");
        }
    }
}
