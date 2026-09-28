using System.Collections;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Chest biome palettes in real runs of each biome: a planned Supply Chest wears the biome's chest tint closed, and
    /// keeps it opened (the sprite swaps under it) with its loot delivered as before; the boss's Boss Cache wears the
    /// gold-leaned biome tint at a larger size, with its own prompt. Captures: TestResults/RegressionProof/chest_tint_*.png.
    /// </summary>
    public sealed class ChestBiomeTintLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_chest_tint_" + System.Guid.NewGuid().ToString("N"));
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
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time"); yield return null; }
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
            body.linearVelocity = Vector2.zero;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 6; i++) yield return null;
        }

        private static void Capture(ExpeditionScene run, string name, Vector2 at)
        {
            var cam = run.Camera.Camera.transform;
            cam.position = new Vector3(at.x, at.y, cam.position.z);
            LiveDungeonCapture.Capture(Folder, name, run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
        }

        [UnityTest]
        public IEnumerator LiveRun_SupplyChestAndBossCache_WearTheBiomePalette_InEveryState(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Chest Check");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var player = run.Rig.Player;
            player.GetComponent<HealthComponent>().Heal(100000);

            // Every chest composed on the depth wears the biome palette for its kind.
            var chests = run.Rooms.Values.SelectMany(r => r.GetComponentsInChildren<SupplyChest>(true)).ToList();
            Assert.Greater(chests.Count, 0, "the depth has chests");
            foreach (var c in chests)
            {
                Assert.AreEqual(ChestBiomePalette.TintFor(biome, c.Kind), c.Visual.Renderer.color, c.name);
                Assert.AreEqual(Vector3.one, c.Visual.Renderer.transform.localScale, c.name + ": a normal chest keeps its size");
            }

            // A planned Supply Chest: closed, then opened by the player's own interaction — the tint stays, the loot lands.
            var chest = chests.First(c => c.Kind == LootSourceKind.SupplyChest && !c.IsOpened);
            var tint = ChestBiomePalette.TintFor(biome, LootSourceKind.SupplyChest);
            yield return Teleport(run, (Vector2)chest.transform.position + Vector2.down * 1.1f);
            StringAssert.Contains("OPEN CHEST", run.CurrentInteractionPrompt, "the prompt reads over the tinted chest");
            Capture(run, $"chest_tint_{biome}_supply_closed", chest.transform.position);
            var interactor = player.GetComponent<RuinRail.Gameplay.Player.PlayerInteractor>();
            var deadline = Time.realtimeSinceStartup + 3f;
            while (!chest.IsOpened && Time.realtimeSinceStartup < deadline) { interactor.TryInteract(); yield return null; }
            Assert.IsTrue(chest.IsOpened);
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, chest.Visual.Key, "the opened crate art");
            Assert.AreEqual(tint, chest.Visual.Renderer.color, "opened, still in the biome palette");
            Assert.Greater(chest.SpawnedPickups.Count, 0, "the loot landed as before");
            for (var i = 0; i < 6; i++) yield return null;
            Capture(run, $"chest_tint_{biome}_supply_opened", chest.transform.position);

            // The Boss Cache: gold-leaned biome tint, larger, its own prompt.
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var centre = EncounterRewardPlacement.WorldCenter(bossRoom.Root).Value;
            yield return Teleport(run, centre + Vector2.left * 2f);
            BossIntroSequence.Current?.Finish();
            player.GetComponent<HealthComponent>().Heal(100000);
            var binding = bossRoom.GetComponent<RoomContentBinding>();
            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
            deadline = Time.realtimeSinceStartup + 10f;
            while (binding.BossCache == null) { Assert.Less(Time.realtimeSinceStartup, deadline, "the boss's death spawned the cache"); yield return null; }
            var cache = binding.BossCache;
            Assert.AreEqual(ChestBiomePalette.TintFor(biome, LootSourceKind.BossCache), cache.Visual.Renderer.color);
            Assert.AreNotEqual(tint, cache.Visual.Renderer.color, "the Boss Cache reads apart from a Supply Chest");
            Assert.AreEqual(Vector3.one * ChestBiomePalette.BossCacheScale, cache.Visual.Renderer.transform.localScale, "drawn larger");
            yield return Teleport(run, (Vector2)cache.transform.position + Vector2.down * 1.2f);
            StringAssert.Contains("OPEN BOSS CACHE", cache.PromptFor(player), "its own prompt");
            Capture(run, $"chest_tint_{biome}_boss_cache", cache.transform.position);
            Debug.Log($"[PROOF] {biome}: {chests.Count} chests at {tint}, opened keeps it; Boss Cache {cache.Visual.Renderer.color} x{ChestBiomePalette.BossCacheScale}");
        }
    }
}
