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
    /// Chest art in real runs of each biome: every composed chest is drawn by <see cref="ChestArt"/> for the depth's
    /// biome and its reward tier (untinted, unscaled, so the art stays on the pixel grid); a planned Supply Chest opens
    /// by the player's own interaction into its opened art with its loot delivered as before; the boss's Boss Cache is
    /// the larger vault crate with its own prompt. Captures: TestResults/RegressionProof/chest_art_*.png.
    /// </summary>
    public sealed class ChestArtLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_chest_art_" + System.Guid.NewGuid().ToString("N"));
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
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
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

            // Every chest composed on the depth is drawn for the biome and its tier, untinted and unscaled.
            var chests = run.Rooms.Values.SelectMany(r => r.GetComponentsInChildren<SupplyChest>(true)).ToList();
            Assert.Greater(chests.Count, 0, "the depth has chests");
            foreach (var c in chests)
            {
                var tier = ChestArt.TierOf(c.Kind, c.name == "EliteRewardChest");
                Assert.AreEqual(ChestArt.For(biome, tier, c.Visual.Key), c.Visual.Renderer.sprite, c.name + ": the biome/tier chest art");
                Assert.AreEqual(Color.white, c.Visual.Renderer.color, c.name);
                Assert.AreEqual(Vector3.one, c.Visual.Renderer.transform.localScale, c.name + ": drawn at 1:1 on the pixel grid");
                Assert.AreEqual(Vector2.one, ((BoxCollider2D)c.GetComponent<Collider2D>()).size, c.name + ": the interaction footprint is unchanged");
            }

            // A planned Supply Chest: closed, then opened by the player's own interaction — the art follows, the loot lands.
            var chest = chests.First(c => c.Kind == LootSourceKind.SupplyChest && !c.IsOpened);
            var closedArt = chest.Visual.Renderer.sprite;
            yield return Teleport(run, (Vector2)chest.transform.position + Vector2.down * 1.1f);
            StringAssert.Contains("OPEN CHEST", run.CurrentInteractionPrompt, "the prompt reads over the chest");
            Capture(run, $"chest_art_{biome}_supply_closed", chest.transform.position);
            var interactor = player.GetComponent<RuinRail.Gameplay.Player.PlayerInteractor>();
            var deadline = Time.realtimeSinceStartup + 3f;
            while (!chest.IsOpened && Time.realtimeSinceStartup < deadline) { interactor.TryInteract(); yield return null; }
            Assert.IsTrue(chest.IsOpened);
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, chest.Visual.Key, "the opened crate art");
            Assert.AreEqual(ChestArt.For(biome, ChestTier.Supply, WorldObjectArt.SupplyChestOpen), chest.Visual.Renderer.sprite, "the biome's opened chest");
            Assert.AreNotEqual(closedArt, chest.Visual.Renderer.sprite, "opened reads apart from closed");
            Assert.Greater(chest.SpawnedPickups.Count, 0, "the loot landed as before");
            for (var i = 0; i < 6; i++) yield return null;
            Capture(run, $"chest_art_{biome}_supply_opened", chest.transform.position);

            // The Boss Cache: the larger gold-framed vault crate of the biome, its own prompt.
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
            Assert.AreEqual(ChestArt.For(biome, ChestTier.Boss, cache.Visual.Key), cache.Visual.Renderer.sprite, "the biome's Boss Cache art");
            Assert.Greater(cache.Visual.Renderer.sprite.rect.width, closedArt.rect.width, "the Boss Cache is drawn on a larger canvas");
            Assert.Greater(ChestArt.BodyWidth(ChestTier.Boss), ChestArt.BodyWidth(ChestTier.Supply) * 1.6f, "and is far bulkier than a Supply Chest");
            Assert.AreEqual(Vector3.one, cache.Visual.Renderer.transform.localScale, "at 1:1 on the pixel grid");
            yield return Teleport(run, (Vector2)cache.transform.position + Vector2.down * 1.2f);
            StringAssert.Contains("OPEN BOSS CACHE", cache.PromptFor(player), "its own prompt");
            Capture(run, $"chest_art_{biome}_boss_cache", cache.transform.position);
            Debug.Log($"[PROOF] {biome}: {chests.Count} chests drawn for their tiers; Boss Cache {cache.Visual.Renderer.sprite.name}");
        }
    }
}
