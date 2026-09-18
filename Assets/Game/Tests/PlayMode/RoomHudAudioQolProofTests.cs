using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Audio;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Hud;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using RuinRail.UI.WeaponCache;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of this pass (real boot flow → Shelter → generated dungeon on a seed whose first depth carries a
    /// Weapon Cache): the cache prompt → selection screen → one reward, the room-title reveal on a genuine entry, the
    /// minimap's current room and discovery, the graphical coin readout, the low-HP vignette and the dash cooldown
    /// wipe. Captures and the audio runtime evidence go to <c>TestResults/RoomHudAudioQolProof</c>.
    /// </summary>
    public sealed class RoomHudAudioQolProofTests
    {
        private const string Folder = "TestResults/RoomHudAudioQolProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_qolproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder, "live_*")) File.Delete(stale);
            CursorService.SetApplier(_ => true);
            GameplayInputGate.Reset();
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "live_room_hud_qol_evidence.txt"), _evidence.ToString());
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || IsTestRunner(root)) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            NetworkPlayerObject.VisualComposer = null;
            RoomDoorLock.SkinResolver = null;
            WorldObjectArt.Resolver = null;
            CursorService.Reset();
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            try { Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private static bool IsTestRunner(GameObject root)
        {
            if (root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            foreach (var component in root.GetComponents<Component>())
                if (component != null && (component.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools")) return true;
            return false;
        }

        private void Note(string line)
        {
            _evidence.AppendLine(line);
            Debug.Log("[QOLPROOF] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 40f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        /// <summary>
        /// The first run seed whose depth-1 layout places a Weapon Cache. Everything here is the shipped deterministic
        /// logic — the biome draw, the graph, the room pool and the event-kind pick — so the seed this finds is exactly
        /// what the real run will generate.
        /// </summary>
        private static int SeedWithAWeaponCacheOnDepthOne(IReadOnlyList<RoomDefinition> rooms, out Biome biome)
        {
            var pools = BiomeRoomPools.Build(rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            try
            {
                for (var seed = 1; seed <= 400; seed++)
                {
                    var candidate = BiomeSelector.SelectFirst(seed);
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(candidate), seed, 1);
                    if (!generation.Success) continue;
                    foreach (var placement in generation.Layout.Placements)
                    {
                        if (placement.Definition.RoomType != RoomType.Event) continue;
                        if (RoomCategoryComposer.ResolveEventKind(placement.Definition.Tags, seed, 1, placement.NodeId) != DungeonEventKind.WeaponCache) continue;
                        biome = candidate;
                        return seed;
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(rules);
            }

            biome = Biome.RuinedMetro;
            return -1;
        }

        [UnityTest]
        public IEnumerator LiveRun_WeaponCache_RoomTitles_Minimap_Coins_Vignette_AndDashWipe()
        {
            var rooms = UnityEditor.AssetDatabase.FindAssets("t:RoomDefinition")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/_Test/"))
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinition>)
                .Where(d => d != null)
                .ToList();
            var seed = SeedWithAWeaponCacheOnDepthOne(rooms, out var expectedBiome);
            Assert.Greater(seed, 0, "a first depth with a Weapon Cache exists in the shipped generation");
            Note($"seed {seed} ({expectedBiome}) places a Weapon Cache on depth 1");

            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("QoL Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var hud = run.HudView;
            var map = run.Minimap;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var health = player.GetComponent<HealthComponent>();
            var dash = player.GetComponent<PlayerDash>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var menuInput = Object.FindFirstObjectByType<MenuInput>();
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            Assert.AreEqual(expectedBiome, run.Expedition.State.Biome, "the run generated the biome the seed search predicted");

            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle()
            {
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 4; i++) yield return null;
            }

            // ---- Minimap + biome label, nothing spoiled ----
            Assert.AreSame(hud.Minimap.Rect, hud.TopLeftPanel);
            Assert.AreEqual(RoomDisplayNames.BiomeName(run.Expedition.State.Biome), hud.BiomeText);
            Assert.AreEqual("D1", hud.DepthText);
            Assert.IsTrue(map.CurrentNodeId.HasValue && map.Room(map.CurrentNodeId.Value).Visited, "the start room is the current room");
            Assert.Less(map.DiscoveredRooms.Count(), map.Rooms.Count, "the generated dungeon is not revealed up front");
            Assert.IsTrue(map.DiscoveredLinks.All(l => run.Generation.Layout.Connections.Any(c => c.Joins(l.a, l.b))), "every drawn door is a real one");
            Note($"minimap: {map.Rooms.Count} rooms on the depth, {map.DiscoveredRooms.Count()} discovered at the start, biome '{hud.BiomeText}'");
            LiveDungeonCapture.Capture(Folder, "live_06_minimap_current_room", camera, ppu, includeUi: true);

            // ---- Room title on a genuine entry, once ----
            var startNode = map.CurrentNodeId.Value;
            var neighbour = run.Rooms.Values.FirstOrDefault(r => r.State.NodeId != startNode && map.Neighbours(startNode).Contains(r.State.NodeId));
            Assert.IsNotNull(neighbour, "the start room has a neighbour to walk into");
            Put(RoomCentre(neighbour));
            yield return Settle();
            Assert.AreSame(neighbour, run.CurrentRoom, "the one room-entry event moved the run's current room");
            var expectedName = RoomDisplayNames.NameOf(neighbour.State.RoomId, neighbour.Root.Definition.RoomType).ToUpperInvariant();
            Assert.AreEqual(expectedName, hud.RoomTitle.NameText);
            Assert.IsTrue(hud.RoomTitle.IsShowing);
            Assert.AreEqual(map.CurrentNodeId, neighbour.State.NodeId, "the map followed the same event");
            var reveals = hud.RoomTitle.Reveals;
            Note($"entered '{expectedName}' ({neighbour.Root.Definition.RoomType}); reveal #{reveals}");
            LiveDungeonCapture.Capture(Folder, "live_04_room_title_reveal", camera, ppu, includeUi: true);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.AreEqual(reveals, hud.RoomTitle.Reveals, "standing still in the same room never repeats the reveal");
            LiveDungeonCapture.Capture(Folder, "live_07_minimap_after_discovery", camera, ppu, includeUi: true);

            // ---- The Weapon Cache: prompt → screen → exactly one reward ----
            var cacheBinding = run.Rooms.Values
                .Select(r => r.GetComponent<RoomContentBinding>())
                .First(b => b != null && b.EventInstance is WeaponCacheEvent && b.Event != null);
            var cacheRoom = run.Rooms.Values.First(r => r.GetComponent<RoomContentBinding>() == cacheBinding);
            var cache = (WeaponCacheEvent)cacheBinding.EventInstance;
            Put(RoomCentre(cacheRoom));
            yield return Settle();
            Assert.AreEqual("WEAPON CACHE", hud.RoomTitle.RoleText, "the special room announces what it is");
            LiveDungeonCapture.Capture(Folder, "live_05_room_title_special_room", camera, ppu, includeUi: true);
            hud.Minimap.Render();
            Assert.IsTrue(hud.Minimap.DrawnMarkerNodeIds.Contains(cacheRoom.State.NodeId), "its marker appears once entered");
            LiveDungeonCapture.Capture(Folder, "live_08_minimap_special_marker", camera, ppu, includeUi: true);

            Put((Vector2)cacheBinding.Event.transform.position + Vector2.down * 1.1f);
            yield return Settle();
            Assert.IsTrue(run.CurrentInteractionPrompt.Contains("CHOOSE WEAPON CACHE"), $"prompt was '{run.CurrentInteractionPrompt}'");
            Note($"cache prompt: '{run.CurrentInteractionPrompt}'");
            LiveDungeonCapture.Capture(Folder, "live_01_weapon_cache_prompt", camera, ppu, includeUi: true);

            Assert.IsTrue(interactor.TryInteract(), "E opens the selection");
            yield return null;
            Assert.AreEqual(1, run.WeaponCache.Opens);
            Assert.IsTrue(run.WeaponCache.IsOpen && run.WeaponCacheView.IsVisible);
            Assert.AreSame(cache, run.WeaponCache.Cache);
            Assert.AreEqual(cache.Choices.Count, run.WeaponCache.Rows.Count);
            Assert.IsTrue(run.WeaponCacheView.RowViews.Take(run.WeaponCache.Rows.Count).All(r => r.IconVisible && r.FrameSprite != null));
            Assert.IsTrue(GameplayInputGate.IsHeld, "gameplay is gated while the screen is up");
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);
            Assert.AreSame(run.WeaponCacheView.FocusList, menuInput.Stack.Current);
            Assert.AreEqual(0, run.CurrentInteractionPrompt.Length, "the world prompt stands down");
            interactor.TryInteract();
            yield return null;
            Assert.AreEqual(1, run.WeaponCache.Opens, "a second press opens nothing new");
            Note($"cache screen open with {run.WeaponCache.Rows.Count} choices: {string.Join(", ", run.WeaponCache.Rows.Select(r => r.Name))}");
            LiveDungeonCapture.Capture(Folder, "live_02_weapon_cache_ui_open", camera, ppu, includeUi: true);

            var inventory = run.Expedition.State.Inventory;
            var chosen = run.WeaponCache.Rows[0];
            var before = inventory.BackpackSlots.Count(s => s != null);
            Assert.IsTrue(menuInput.Stack.Activate(), "confirm takes the focused weapon");
            yield return null;
            Assert.AreEqual(before + 1, inventory.BackpackSlots.Count(s => s != null), "exactly one weapon arrived");
            Assert.AreEqual(1, run.WeaponCache.Takes);
            Assert.IsTrue(cache.IsConsumed);
            Assert.IsFalse(run.WeaponCache.IsOpen);
            Assert.IsFalse(GameplayInputGate.IsHeld, "closing restores gameplay input");
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);
            Note($"took '{chosen.Name}' ({chosen.Item.DefinitionId}, {chosen.Item.Rarity}); cache consumed = {cache.IsConsumed}");
            LiveDungeonCapture.Capture(Folder, "live_03_weapon_cache_reward_granted", camera, ppu, includeUi: true);

            yield return Settle();
            Assert.AreEqual(0, run.CurrentInteractionPrompt.Length, "a consumed cache offers nothing");
            Assert.IsFalse(interactor.TryInteract());
            Assert.AreEqual(before + 1, inventory.BackpackSlots.Count(s => s != null), "and grants nothing a second time");

            // ---- Graphical coins ----
            var coins = run.Expedition.State.CarriedCoins;
            Assert.IsTrue(hud.CoinView.HasIconSprite);
            Assert.AreEqual(coins.ToString(), hud.CoinsText);
            run.Expedition.AddCarriedCoins(249);
            yield return null;
            Assert.AreEqual((coins + 249).ToString(), hud.CoinsText, "the readout follows the wallet immediately");
            Note($"coin readout: token + '{hud.CoinsText}'");
            LiveDungeonCapture.Capture(Folder, "live_10_graphical_coin_display", camera, ppu, includeUi: true);

            // ---- Low-HP vignette ----
            Assert.IsTrue(hud.Vignette.HasSprite);
            Assert.IsFalse(hud.Vignette.IsActive, "hidden at full health");
            var target = Mathf.CeilToInt(health.MaxHealth * (HudLowHealthVignetteView.DefaultThreshold - 0.05f));
            health.TryApplyDamage(new DamageRequest(Mathf.Max(1, health.CurrentHealth - target)));
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsTrue(hud.Vignette.IsActive && hud.Vignette.IsVisible, $"active at {health.CurrentHealth}/{health.MaxHealth}");
            var alphaA = hud.Vignette.Alpha;
            for (var i = 0; i < 25; i++) yield return null;
            Assert.AreNotEqual(alphaA, hud.Vignette.Alpha, "it pulses");
            Note($"low-HP vignette at {health.CurrentHealth}/{health.MaxHealth} (effective max), intensity {hud.Vignette.Intensity:0.00}, alpha {hud.Vignette.Alpha:0.000}");
            LiveDungeonCapture.Capture(Folder, "live_11_low_hp_vignette", camera, ppu, includeUi: true);
            health.Heal(health.MaxHealth);
            for (var i = 0; i < 30; i++) yield return null;
            Assert.IsFalse(hud.Vignette.IsActive && hud.Vignette.IsVisible, "healed above the threshold it fades away");

            // ---- Dash cooldown wipe ----
            var wait = Time.realtimeSinceStartup + 5f;
            while (!dash.CanDash && Time.realtimeSinceStartup < wait) yield return null;
            Assert.AreEqual(HudDashState.Ready, hud.DashIcon.State);
            Assert.AreEqual(0f, hud.DashIcon.CoveredPixels);
            LiveDungeonCapture.Capture(Folder, "live_15_dash_ready", camera, ppu, includeUi: true);
            Assert.IsTrue(dash.TryStartDash(Vector2.right));
            yield return null;
            Assert.IsTrue(hud.DashIcon.CoverIsVertical, "a vertical wipe, not a static grey block");
            Assert.Greater(hud.DashIcon.Cooldown01, 0.85f);
            LiveDungeonCapture.Capture(Folder, "live_12_dash_cooldown_full", camera, ppu, includeUi: true);
            yield return WaitUntilCover(hud, 0.55f);
            Assert.That(hud.DashIcon.Cooldown01, Is.GreaterThan(0.35f).And.LessThan(0.6f));
            Assert.AreEqual(dash.CooldownRemaining / dash.CurrentDashCooldown, hud.DashIcon.Cooldown01, 0.06f);
            LiveDungeonCapture.Capture(Folder, "live_13_dash_cooldown_half", camera, ppu, includeUi: true);
            yield return WaitUntilCover(hud, 0.2f);
            Assert.Greater(hud.DashIcon.CoveredPixels, 0f, "a sliver still covers the icon just before ready");
            LiveDungeonCapture.Capture(Folder, "live_14_dash_cooldown_near_ready", camera, ppu, includeUi: true);
            var readyDeadline = Time.realtimeSinceStartup + 5f;
            while (!dash.CanDash && Time.realtimeSinceStartup < readyDeadline) yield return null;
            yield return null;
            Assert.AreEqual(HudDashState.Ready, hud.DashIcon.State);
            Assert.AreEqual(0f, hud.DashIcon.CoveredPixels, "completely clear when ready");
            Note($"dash wipe: cooldown {dash.CurrentDashCooldown:0.####}s, vertical fill, clear at ready");

            // ---- Clean full HUD ----
            LiveDungeonCapture.Capture(Folder, "live_16_clean_hud_640x360", camera, ppu, includeUi: true);

            // ---- Audio runtime state of this live run ----
            var diagnostics = AudioRuntimeDiagnostics.Capture();
            File.WriteAllText(Path.Combine(Folder, "live_audio_runtime_evidence.txt"), diagnostics.ToText());
            Note("audio runtime: " + diagnostics.Configuration);
            CollectionAssert.IsEmpty(diagnostics.Problems(), "no in-engine cause of silence in a live run");
            Assert.IsNotNull(AudioOutputWatchdog.Current, "the output-device watchdog is part of the composition");
            Assert.Greater(AudioLevels.AmbienceGain, 0f);
            Assert.IsTrue(_app.Music.ActiveRole.HasValue && !_app.Music.IsSilent, "the expedition music bed is playing");
            Assert.AreEqual(run.Expedition.State.Biome, _app.Music.ActiveAmbience, "with the biome's ambience under it");
        }

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            if (marker != null) return room.Root.transform.TransformPoint(marker.WorldCenter);
            var size = (Vector2)room.Root.Size * RuinRail.Dungeon.Grid.GridConstants.TileWorldSize;
            return (Vector2)room.Root.transform.position + size * 0.5f;
        }

        private static IEnumerator WaitUntilCover(DungeonHudView hud, float atMost)
        {
            var deadline = Time.realtimeSinceStartup + 5f;
            while (hud.DashIcon.Cooldown01 > atMost && Time.realtimeSinceStartup < deadline) yield return null;
        }
    }
}
