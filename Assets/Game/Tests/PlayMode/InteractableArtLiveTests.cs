using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Non-combat interactables in real runs of each biome: the merchant and every composed event object are drawn by
    /// <see cref="InteractableArt"/> for the depth's biome (untinted, unscaled, interaction footprint unchanged, prompt
    /// unchanged), each event follows its phase through the state presenter, and an in-room gallery shows every kind in
    /// every state against the biome's real floor. Captures: TestResults/RegressionProof/interactable_art_*.png.
    /// </summary>
    public sealed class InteractableArtLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        private static readonly (string Key, string[] States)[] Gallery =
        {
            (WorldObjectArt.DungeonMerchant, new[] { "" }),
            (WorldObjectArt.EventKey("MedicalStation"), new[] { "", InteractableArt.Used }),
            (WorldObjectArt.EventKey("WeaponCache"), new[] { "", InteractableArt.Used }),
            (WorldObjectArt.EventKey("CursedChest"), new[] { "", InteractableArt.Active, InteractableArt.Used }),
            (WorldObjectArt.EventKey("LockedVault"), new[] { "", InteractableArt.Used }),
            (WorldObjectArt.EventKey("BrokenMachine"), new[] { "", InteractableArt.Used, InteractableArt.Failed }),
            (WorldObjectArt.EventKey("SupplySignal"), new[] { "", InteractableArt.Active, InteractableArt.Used }),
            (WorldObjectArt.EventKey("SecureRelay"), new[] { "", InteractableArt.Secured })
        };

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_interactable_art_" + System.Guid.NewGuid().ToString("N"));
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
            RoomDoorLock.SkinResolver = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 60f;
            while (_app.ComposedScene != scene) { Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time"); yield return null; }
        }

        /// <summary>
        /// The first run seed whose depth 1 starts in the biome and holds an Event room (and a Merchant room when one
        /// exists in range), found with the shipped generator rather than pinned, so a generation change cannot silently
        /// empty the proof.
        /// </summary>
        private static int SeedFor(Biome biome)
        {
            var pools = BiomeRoomPools.Build(GameContentCatalog.Load().Rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            var fallback = 0;
            try
            {
                for (var seed = 1; seed <= 600; seed++)
                {
                    if (BiomeSelector.SelectFirst(seed) != biome) continue;
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(biome), seed, 1);
                    if (!generation.Success) continue;
                    var types = generation.Layout.Placements.Select(p => p.Definition.RoomType).ToList();
                    if (!types.Contains(RoomType.Event)) continue;
                    if (types.Contains(RoomType.Merchant)) return seed;
                    if (fallback == 0) fallback = seed;
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }

            Assert.AreNotEqual(0, fallback, $"no seed in 1..600 starts in {biome} with an Event room on depth 1");
            return fallback;
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            var until = Time.time + 0.3f;
            while (Time.time < until) yield return null;
        }

        private static void Capture(ExpeditionScene run, string name, Vector2 at, bool ui = true) =>
            LiveDungeonCapture.Capture(Folder, name, run.Camera.Camera, at, LiveDungeonCapture.Height / 2f / run.Camera.Config.PixelsPerUnit, run.Camera.Config.PixelsPerUnit, ui);

        private static void AssertDrawn(WorldObjectVisual visual, Biome biome, string label)
        {
            Assert.IsNotNull(visual, label);
            Assert.IsTrue(visual.IsVisible, label + ": drawn");
            Assert.AreEqual(InteractableArt.For(visual.Key, biome), visual.Renderer.sprite, label + ": the biome's interactable art");
            Assert.AreEqual(Vector3.one, visual.Renderer.transform.localScale, label + ": 1:1 on the pixel grid");
            Assert.AreEqual(Vector2.one, ((BoxCollider2D)visual.GetComponent<Collider2D>()).size, label + ": the interaction footprint is unchanged");
        }

        [UnityTest]
        public IEnumerator LiveRun_NonCombatInteractables_WearTheBiomeArt_AndReadTheirState(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs, Biome.CryoVaults)] Biome biome)
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Prop Check");
            hub.Onboarding.AcknowledgeStarterKit();
            _app.Settings.Current.Tutorial.ShowPrompts = false;
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            Assert.AreEqual(biome, run.Expedition.State.Biome);
            var player = run.Rig.Player;
            player.GetComponent<HealthComponent>().Heal(100000);
            var bindings = run.Rooms.Values.Select(r => (Room: r, Binding: r.GetComponent<RoomContentBinding>())).Where(x => x.Binding != null).ToList();

            // Every composed merchant and event object: the biome's art, prompt and footprint as before.
            var seen = new List<string>();
            foreach (var (room, binding) in bindings)
            {
                if (binding.Merchant != null)
                {
                    AssertDrawn(binding.Merchant.GetComponent<WorldObjectVisual>(), biome, "merchant");
                    Assert.AreEqual("TRADE WITH MERCHANT", ((IInteractionPrompt)binding.Merchant).PromptFor(player));
                    yield return Teleport(run, (Vector2)binding.Merchant.transform.position + Vector2.down * 1.1f);
                    Capture(run, $"interactable_art_{biome}_merchant", binding.Merchant.transform.position);
                    seen.Add("Merchant");
                }

                if (binding.Event != null)
                {
                    var kind = binding.EventInstance.Kind;
                    var visual = binding.Event.GetComponent<WorldObjectVisual>();
                    AssertDrawn(visual, biome, kind.ToString());
                    Assert.AreEqual(Color.white, visual.Renderer.color, kind + ": untinted while usable");
                    Assert.AreEqual(WorldObjectArt.EventKey(kind.ToString()), visual.Key, kind + ": idle while available");
                    Assert.AreEqual(kind == DungeonEventKind.SecureRelay, binding.Event.GetComponent<InteractableStatePresenter>() == null,
                        kind + ": every event but the per-member relay follows its phase");
                    yield return Teleport(run, (Vector2)binding.Event.transform.position + Vector2.down * 1.1f);
                    Assert.IsFalse(string.IsNullOrEmpty(run.CurrentInteractionPrompt), kind + ": its prompt reads");
                    Capture(run, $"interactable_art_{biome}_{room.State.NodeId}_{kind}_idle", binding.Event.transform.position);
                    seen.Add(kind.ToString());
                }
            }

            Assert.IsTrue(seen.Count > 0, "the depth has non-combat interactables");

            // The state follows the phase: resolve one composed event and it is redrawn in its used/failed look.
            var target = bindings.Select(b => b.Binding).FirstOrDefault(b => b.Event != null && b.EventInstance is DungeonEventBase && b.EventInstance.Kind != DungeonEventKind.SecureRelay);
            if (target != null)
            {
                var visual = target.Event.GetComponent<WorldObjectVisual>();
                ((DungeonEventBase)target.EventInstance).RestoreResolved(true);
                for (var i = 0; i < 3; i++) yield return null;
                Assert.AreEqual(WorldObjectArt.EventKey(target.EventInstance.Kind.ToString()) + InteractableArt.StateFor(target.EventInstance), visual.Key);
                Assert.AreNotEqual(WorldObjectArt.EventKey(target.EventInstance.Kind.ToString()), visual.Key, "a resolved event leaves its idle look");
                Assert.AreEqual(InteractableArt.For(visual.Key, biome), visual.Renderer.sprite);
                Capture(run, $"interactable_art_{biome}_{target.EventInstance.Kind}_resolved", target.Event.transform.position);
            }

            // Gallery: every kind in every state, staged on this depth's real floor (a non-combat room).
            var stage = bindings.FirstOrDefault(b => b.Binding.Event != null || b.Binding.Merchant != null).Room ?? run.Rooms[run.Generation.Graph.StartId];
            var centre = (Vector2)stage.InteriorWorldBounds.center;
            foreach (var (_, binding) in bindings)
            {
                if (binding.Event != null) binding.Event.GetComponent<WorldObjectVisual>().Renderer.enabled = false;
                if (binding.Merchant != null) binding.Merchant.GetComponent<WorldObjectVisual>().Renderer.enabled = false;
            }

            yield return Teleport(run, centre + new Vector2(0f, -30f));
            var staged = new List<GameObject>();
            for (var k = 0; k < Gallery.Length; k++)
            for (var s = 0; s < Gallery[k].States.Length; s++)
            {
                var go = new GameObject($"Gallery_{Gallery[k].Key}{Gallery[k].States[s]}");
                go.transform.position = centre + new Vector2(-7.35f + k * 2.1f, -4.2f + s * 3.4f);
                var visual = WorldObjectVisual.Attach(go, Gallery[k].Key, SortingRole.Character);
                InteractableArt.Apply(visual, biome);
                visual.Show(Gallery[k].Key + Gallery[k].States[s]);
                Assert.IsTrue(visual.IsVisible, go.name);
                Assert.AreEqual(InteractableArt.For(visual.Key, biome), visual.Renderer.sprite, go.name);
                staged.Add(go);
            }

            // Every state reads apart from its idle look.
            foreach (var (key, states) in Gallery)
            foreach (var state in states.Where(s => s != ""))
                Assert.AreNotSame(InteractableArt.For(key, biome), InteractableArt.For(key + state, biome), key + state);

            for (var i = 0; i < 3; i++) yield return null;
            Capture(run, $"interactable_art_{biome}_gallery", centre, ui: false);
            foreach (var go in staged) Object.Destroy(go);
            Debug.Log($"[PROOF] {biome}: drawn {string.Join(",", seen)}");
        }
    }
}
