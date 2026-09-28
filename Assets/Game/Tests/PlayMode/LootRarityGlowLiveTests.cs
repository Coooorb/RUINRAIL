using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using RuinRail.Presentation.Vfx;
using RuinRail.UI.Navigation;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Rarity on the ground in real runs of each biome: items dropped through the run's own loot spawner path (the one a
    /// co-op client's replicated loot uses too) show their rarity in the established rarity colour — Common and ammo
    /// quiet, coins untouched, stronger tiers a little stronger, Epic/Legendary a short shimmer column — below hazards and
    /// everything that moves; the presentation follows the item, follows a change of item, and is gone the moment the
    /// item is taken, destroyed or the depth is torn down. Captures: TestResults/RegressionProof/loot_rarity_*.png.
    /// </summary>
    public sealed class LootRarityGlowLiveTests
    {
        private const string Folder = "TestResults/RegressionProof";
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_loot_glow_" + System.Guid.NewGuid().ToString("N"));
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

        private sealed class Guard : IInvulnerabilityState { public bool IsInvulnerable => true; }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
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

        private static void AssertRgb(Color expected, Color actual, string what) =>
            Assert.IsTrue(Mathf.Abs(expected.r - actual.r) < 0.01f && Mathf.Abs(expected.g - actual.g) < 0.01f && Mathf.Abs(expected.b - actual.b) < 0.01f, $"{what}: expected {expected}, got {actual}");

        [UnityTest]
        public IEnumerator LiveRun_DroppedItems_ShowTheirRarityOnTheGround_AndTheGlowFollowsTheItemsLife(
            [Values(Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs)] Biome biome)
        {
            var content = GameContentCatalog.Load();
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(SeedFor(biome));
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Loot Glow");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var player = run.Rig.Player;
            player.GetComponent<HealthComponent>().SetInvulnerabilityState(new Guard());
            var start = run.Rooms[run.Generation.Graph.StartId];
            var centre = start.InteriorWorldBounds.center;
            // The player stands aside so the line-up is the subject; the camera follows the player.
            var body = player.GetComponent<Rigidbody2D>();
            var stand = centre + new Vector2(0f, 1.6f);
            player.transform.position = stand; body.position = stand; Physics2D.SyncTransforms();
            for (var i = 0; i < 4; i++) yield return new WaitForFixedUpdate();

            // The run's own loot spawner path: tracked by the depth's ground-loot registry (as a co-op client's is).
            var spawner = run.CreateLootSpawnerFor(new GameObject("TestLootHost"));
            ItemDefinition First(ItemCategory category) => content.Items.First(d => d != null && d.Category == category);
            var weapon = First(ItemCategory.Weapon);
            var lineup = new List<(string label, ItemInstance item, ItemCategory category, Rarity? expected)>
            {
                ("common_weapon", new ItemInstance(weapon.Id, 1, Rarity.Common), ItemCategory.Weapon, null),
                ("uncommon_armor", new ItemInstance(First(ItemCategory.Armor).Id, 1, Rarity.Uncommon), ItemCategory.Armor, Rarity.Uncommon),
                ("rare_accessory", new ItemInstance(First(ItemCategory.Accessory).Id, 1, Rarity.Rare), ItemCategory.Accessory, Rarity.Rare),
                ("epic_weapon", new ItemInstance(weapon.Id, 1, Rarity.Epic), ItemCategory.Weapon, Rarity.Epic),
                ("legendary_weapon", new ItemInstance(weapon.Id, 1, Rarity.Legendary), ItemCategory.Weapon, Rarity.Legendary),
                ("rare_consumable", new ItemInstance(First(ItemCategory.Consumable).Id, 1, Rarity.Rare), ItemCategory.Consumable, Rarity.Rare),
                ("ammo", new ItemInstance(First(ItemCategory.Ammo).Id, 20, Rarity.Rare), ItemCategory.Ammo, null),
            };

            var pickups = new List<(string label, WorldItemPickup pickup, Rarity? expected)>();
            for (var i = 0; i < lineup.Count; i++)
            {
                var (label, item, category, expected) = lineup[i];
                var pickup = spawner.CreateItemPickup(centre + new Vector2(-4.5f + i * 1.5f, -0.6f));
                pickup.Hold(item, category); // filled after tracking: the glow follows the item's state, not a snapshot
                pickups.Add((label, pickup, expected));
            }

            var coins = spawner.CreateCoinPickup(centre + new Vector2(0f, -2.2f));
            coins.SetAmount(25);
            yield return null;
            yield return null;

            var widths = new List<float>();
            foreach (var (label, pickup, expected) in pickups)
            {
                var glow = pickup.GetComponentInChildren<LootRarityGlow>();
                Assert.IsNotNull(glow, label + ": every tracked item gets the presentation");
                Assert.AreEqual(expected, glow.ShownRarity, label);
                if (expected == null)
                {
                    Assert.IsFalse(glow.IsGlowVisible || glow.IsBeamVisible, label + " stays quiet");
                    continue;
                }

                AssertRgb(RarityStyle.For(expected.Value).Color, glow.Glow.color, label + " uses the established rarity colour");
                Assert.AreEqual(RuinRail.Core.Rendering.SortingLayers.GroundDetails, glow.Glow.sortingLayerName);
                Assert.Less(glow.Glow.sortingOrder, RuinRail.Core.Rendering.SortingConvention.BaseOrderOf(RuinRail.Core.Rendering.SortingRole.Hazard), label + ": below hazard footprints");
                var itemRenderer = pickup.GetComponentsInChildren<SpriteRenderer>().First(r => r.GetComponentInParent<LootRarityGlow>() == null);
                Assert.AreNotEqual(RuinRail.Core.Rendering.SortingLayers.GroundDetails, itemRenderer.sortingLayerName, label + ": the item itself draws over its glow");
                var epicOrBetter = expected.Value >= Rarity.Epic;
                Assert.AreEqual(epicOrBetter, glow.IsBeamVisible, label + ": a shimmer column only for Epic and Legendary");
                if (glow.IsBeamVisible) Assert.LessOrEqual(glow.Beam.sprite.rect.height, LootRarityGlow.MaxBeamPixels, "a short column, never a tall beam");
                Assert.AreEqual(0, glow.GetComponentsInChildren<Collider2D>(true).Length + glow.GetComponentsInChildren<UnityEngine.Rendering.Universal.Light2D>(true).Length, "presentation only");
                if (label.EndsWith("weapon") || label == "uncommon_armor" || label == "rare_accessory") widths.Add(glow.Glow.sprite.rect.width);
            }

            CollectionAssert.IsOrdered(widths, "rarer tiers get a larger glow");
            var epicBeam = pickups.First(p => p.label == "epic_weapon").pickup.GetComponentInChildren<LootRarityGlow>().Beam.sprite.rect.height;
            Assert.Greater(pickups.First(p => p.label == "legendary_weapon").pickup.GetComponentInChildren<LootRarityGlow>().Beam.sprite.rect.height,
                pickups.First(p => p.label == "epic_weapon").pickup.GetComponentInChildren<LootRarityGlow>().Beam.sprite.rect.height, "Legendary's column is taller than Epic's");
            Assert.IsNull(coins.GetComponentInChildren<LootRarityGlow>(), "coins are unchanged");

            var cam = run.Camera.Camera;
            LiveDungeonCapture.Capture(Folder, $"loot_rarity_{biome}", cam, run.Camera.Config.PixelsPerUnit, includeUi: true);
            LiveDungeonCapture.Capture(Folder, $"loot_rarity_{biome}_closeup", cam, centre + new Vector2(0f, -0.3f), 2.4f, run.Camera.Config.PixelsPerUnit, includeUi: false);

            // Follows the exact item: moved, it carries its glow; its item changed, the glow changes with it.
            var epic = pickups.First(p => p.label == "epic_weapon").pickup;
            var epicGlow = epic.GetComponentInChildren<LootRarityGlow>();
            epic.transform.position += new Vector3(0.5f, 0.25f, 0f);
            yield return null;
            Assert.AreEqual(epic.transform.position.x, epicGlow.Glow.transform.position.x, 1e-4f, "the glow follows the item");
            var swapped = epic.TryRemove(epic.Item.InstanceId);
            Assert.IsNotNull(swapped);
            yield return null;
            Assert.IsNull(epicGlow.ShownRarity, "an emptied pickup shows nothing");
            Assert.IsTrue(epic.TryAdd(new ItemInstance(weapon.Id, 1, Rarity.Uncommon)));
            yield return null;
            Assert.AreEqual(Rarity.Uncommon, epicGlow.ShownRarity, "a new item on the pickup shows its own rarity");
            Assert.IsFalse(epicGlow.IsBeamVisible);

            // Collected: gone the same frame the item is taken.
            var legendary = pickups.First(p => p.label == "legendary_weapon").pickup;
            var legendaryGlow = legendary.GetComponentInChildren<LootRarityGlow>();
            player.transform.position = legendary.transform.position; body.position = legendary.transform.position; Physics2D.SyncTransforms();
            Assert.IsTrue(legendary.Interact(player), "the player takes the Legendary");
            Assert.IsTrue(legendaryGlow == null || (!legendaryGlow.IsGlowVisible && !legendaryGlow.IsBeamVisible), "hidden the moment it is taken");

            // Removed: destroyed with its pickup.
            var rare = pickups.First(p => p.label == "rare_accessory").pickup;
            var rareGlow = rare.GetComponentInChildren<LootRarityGlow>();
            Object.Destroy(rare.gameObject);
            yield return null;
            Assert.IsTrue(rareGlow == null, "destroyed with the pickup");

            // Depth teardown: the ground loot of the depth is discarded, and every glow with it.
            var remaining = pickups.Where(p => p.pickup != null).Select(p => p.pickup.GetComponentInChildren<LootRarityGlow>()).Where(g => g != null).ToList();
            Assert.IsNotEmpty(remaining);
            run.Expedition.RecordBossDefeated(0);
            run.Expedition.Descend();
            for (var i = 0; i < 10; i++) yield return null;
            Assert.IsTrue(remaining.All(g => g == null), "no glow of the old depth survives the teardown");
            var leftovers = Object.FindObjectsByType<LootRarityGlow>(FindObjectsSortMode.None).Length;
            Debug.Log($"[PROOF] {biome}: glow widths {string.Join("/", widths)} px; Epic beam {epicBeam} px; glow objects after Descend {leftovers}");
        }
    }
}
