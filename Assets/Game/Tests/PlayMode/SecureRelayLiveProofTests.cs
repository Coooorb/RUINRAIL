using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Navigation;
using RuinRail.UI.SecureRelay;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of the Secure Relay (57.7): real boot → Shelter → a generated first depth whose seeded event roll
    /// placed the relay → prompt → terminal screen over the real carried slots → SECURE → CONFIRM → ITEM SECURED, the
    /// unit in this profile's Storage and in the save on disk, a second use refused, then the run lost with the secured
    /// item still at home. Captures go to <c>TestResults/SecureRelayProof</c>.
    /// </summary>
    public sealed class SecureRelayLiveProofTests
    {
        private const string Folder = "TestResults/SecureRelayProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_relayproof_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            Directory.CreateDirectory(Folder);
            foreach (var stale in Directory.GetFiles(Folder, "relay_*")) File.Delete(stale);
            CursorService.SetApplier(_ => true);
            GameplayInputGate.Reset();
            _evidence.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            File.WriteAllText(Path.Combine(Folder, "relay_evidence.txt"), _evidence.ToString());
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
            Debug.Log("[RELAYPROOF] " + line);
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

        /// <summary>The first run seed whose depth-1 layout rolls a Secure Relay — the shipped biome draw, graph, pool and event roll.</summary>
        private static int SeedWithARelayOnDepthOne(IReadOnlyList<RoomDefinition> rooms, int chancePercent, out Biome biome)
        {
            var pools = BiomeRoomPools.Build(rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            try
            {
                for (var seed = 1; seed <= 600; seed++)
                {
                    var candidate = BiomeSelector.SelectFirst(seed);
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(candidate), seed, 1);
                    if (!generation.Success) continue;
                    if (generation.Layout.Placements.Any(p => p.Definition.RoomType == RoomType.Event
                                                              && RoomCategoryComposer.ResolveEventKind(p.Definition.Tags, seed, 1, p.NodeId, chancePercent) == DungeonEventKind.SecureRelay))
                    {
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

        private static Vector2 RoomCentre(RoomRuntime room)
        {
            var marker = room.Root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
            if (marker != null) return room.Root.transform.TransformPoint(marker.WorldCenter);
            var size = (Vector2)room.Root.Size * RuinRail.Dungeon.Grid.GridConstants.TileWorldSize;
            return (Vector2)room.Root.transform.position + size * 0.5f;
        }

        [UnityTest]
        public IEnumerator LiveRun_SecureRelay_SendsExactlyOneItemHome_OncePerPlayer_AndItSurvivesTheLostRun()
        {
            var rooms = UnityEditor.AssetDatabase.FindAssets("t:RoomDefinition")
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.Contains("/_Test/"))
                .Select(UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinition>)
                .Where(d => d != null)
                .ToList();
            var content = GameContentCatalog.Load();
            var chance = content.Events.SecureRelayChancePercent;
            Assert.AreEqual(DungeonEventConfig.DefaultSecureRelayChancePercent, chance, "the shipped config carries the documented (temporary) chance");
            var seed = SeedWithARelayOnDepthOne(rooms, chance, out var expectedBiome);
            Assert.Greater(seed, 0, "a first depth with a Secure Relay exists in the shipped generation");
            Note($"seed {seed} ({expectedBiome}) rolls a Secure Relay on depth 1 at {chance}%");

            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(seed);
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Relay Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 6; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var session = _app.Menu.Session;
            var player = run.Rig.Player;
            var body = player.GetComponent<Rigidbody2D>();
            var interactor = player.GetComponent<PlayerInteractor>();
            var menuInput = Object.FindFirstObjectByType<MenuInput>();
            var camera = run.Camera.Camera;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var inventory = run.Expedition.State.Inventory;
            Assert.AreEqual(expectedBiome, run.Expedition.State.Biome, "the run generated the biome the seed search predicted");

            void Put(Vector2 p) { player.transform.position = p; body.position = p; body.linearVelocity = Vector2.zero; Physics2D.SyncTransforms(); }
            IEnumerator Settle()
            {
                for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
                for (var i = 0; i < 4; i++) yield return null;
            }
            // The camera eases after a teleport; captures wait on a time deadline (batch PlayMode runs uncapped).
            IEnumerator CameraCaughtUp()
            {
                var until = Time.time + 1.2f;
                while (Time.time < until) yield return null;
            }

            // A found weapon in the backpack (as if looted this run): the item worth sending home.
            var weaponDef = content.Items.OfType<WeaponDefinition>().First(w => w.Id != "weapon_p9_ranger" && w.Id != "weapon_field_knife" && w.Icon != null);
            var found = new ItemInstance(weaponDef.Id, 1, Rarity.Epic);
            Assert.IsTrue(inventory.TryAddToBackpack(found));
            Assert.IsTrue(found.IsAtRisk, "loot found on the run is at risk");
            var storageBefore = session.Storage.OccupiedSlots;
            Note($"carried: {string.Join(", ", System.Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Select(inventory.GetEquipped).Concat(inventory.BackpackSlots).Where(i => i != null).Select(i => i.DefinitionId + " x" + i.Quantity + (i.IsUnsellable ? " (starter)" : "")))}");

            // ---- the room: a non-combat Event room announcing the relay ----
            var binding = run.Rooms.Values.Select(r => r.GetComponent<RoomContentBinding>()).First(b => b != null && b.EventInstance is SecureRelayEvent && b.Event != null);
            var relayRoom = run.Rooms.Values.First(r => r.GetComponent<RoomContentBinding>() == binding);
            var relay = (SecureRelayEvent)binding.EventInstance;
            Assert.AreEqual(RoomType.Event, relayRoom.State.RoomType);
            Assert.IsFalse(relayRoom.HasEncounter || relayRoom.HasEngagement, "the relay room is non-combat");
            var visual = binding.Event.GetComponent<WorldObjectVisual>();
            Assert.IsTrue(visual != null && visual.IsVisible && visual.Key == "event_SecureRelay", $"the relay draws its own world art (key '{visual?.Key}')");
            Put(RoomCentre(relayRoom));
            yield return Settle();
            Assert.AreEqual("SECURE RELAY", run.HudView.RoomTitle.RoleText, "the room announces what it is");
            Put((Vector2)binding.Event.transform.position + Vector2.down * 1.1f);
            yield return Settle();
            Assert.IsTrue(run.CurrentInteractionPrompt.Contains("ACCESS SECURE RELAY"), $"prompt was '{run.CurrentInteractionPrompt}'");
            Note($"prompt: '{run.CurrentInteractionPrompt}'");
            yield return CameraCaughtUp();
            LiveDungeonCapture.Capture(Folder, "relay_01_world_prompt", camera, ppu, includeUi: true);

            // ---- the terminal ----
            Assert.IsTrue(interactor.TryInteract(), "Interact opens the terminal");
            yield return null;
            var vm = run.SecureRelay;
            var view = run.SecureRelayView;
            Assert.IsTrue(vm.IsOpen && view.IsVisible);
            Assert.AreEqual(1, vm.Opens);
            Assert.AreSame(view.FocusList, menuInput.Stack.Current, "the terminal owns keyboard/controller navigation");
            Assert.IsTrue(GameplayInputGate.IsHeld, "no gameplay input leaks through");
            Assert.AreEqual(0, run.CurrentInteractionPrompt.Length, "the world prompt stands down");
            Assert.AreEqual(SecureRelayViewModel.CellCount, vm.Cells.Count, "5 worn slots + 8 backpack slots");
            foreach (var cell in vm.Cells.Where(c => !c.IsEmpty))
            {
                var expected = cell.Item.IsUnsellable ? SecureRelayRefusal.StarterItem : cell.Definition.Category == ItemCategory.Ammo ? SecureRelayRefusal.Ammo : SecureRelayRefusal.None;
                Assert.AreEqual(expected, cell.Refusal, cell.Item.DefinitionId);
                var index = vm.Cells.ToList().IndexOf(cell);
                Assert.AreEqual(!cell.IsEligible, view.CellShaded(index), "refused slots are shaded: " + cell.Item.DefinitionId);
                Assert.IsNotNull(view.CellViews[index].IconSprite, "every carried item is drawn with its icon: " + cell.Item.DefinitionId);
            }

            Assert.IsTrue(vm.Cells.Any(c => !c.IsEmpty && c.Refusal == SecureRelayRefusal.Ammo), "ammo is shown and refused");
            Assert.IsTrue(vm.Cells.Any(c => !c.IsEmpty && c.Refusal == SecureRelayRefusal.StarterItem), "starter gear is shown and refused");
            // Navigation: down from the worn row reaches the backpack, and the actions are reachable.
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            StringAssert.StartsWith(SecureRelayView.CellFocusPrefix, view.FocusList.Focused.Id);
            var starterCell = vm.Cells.ToList().FindIndex(c => !c.IsEmpty && c.Refusal == SecureRelayRefusal.StarterItem);
            vm.SetCursor(starterCell);
            yield return null;
            Assert.AreEqual("STARTER GEAR CANNOT BE SECURED", view.StatusText);
            Assert.IsFalse(vm.Secure(), "a refused slot cannot be armed");
            var foundCell = vm.Cells.ToList().FindIndex(c => c.Item == found);
            view.CellViews[foundCell].SimulateClick();
            yield return null;
            Assert.AreEqual(foundCell, vm.Cursor, "a click selects the slot");
            Assert.AreEqual("CAN BE SECURED", view.StatusText);
            Assert.AreEqual(string.Empty, view.MessageText, "the refusal of the previous slot does not linger");
            LiveDungeonCapture.Capture(Folder, "relay_02_terminal_select", camera, ppu, includeUi: true);

            // ---- SECURE → CONFIRM → ITEM SECURED ----
            Assert.IsTrue(vm.Secure());
            yield return null;
            Assert.AreEqual(SecureRelayStage.Confirming, vm.Stage);
            Assert.AreEqual("CONFIRM", view.SecureButtonText);
            Assert.IsNotNull(inventory.BackpackSlots.FirstOrDefault(i => i == found), "arming moves nothing");
            LiveDungeonCapture.Capture(Folder, "relay_03_terminal_confirm", camera, ppu, includeUi: true);
            view.Buttons[SecureRelayView.SecureFocusId].SimulateClick();
            yield return null;
            Assert.AreEqual(SecureRelayStage.Secured, vm.Stage);
            Assert.IsTrue(view.ShowsSecuredCard);
            Assert.AreEqual("ITEM SECURED", view.SecuredHeadlineText);
            Assert.AreEqual(weaponDef.DisplayName.ToUpperInvariant(), view.SecuredNameText);
            LiveDungeonCapture.Capture(Folder, "relay_04_item_secured", camera, ppu, includeUi: true);

            // Consequences: out of the run, into this profile's Storage, on disk, recorded for this participant only.
            var participant = DungeonEventInteractable.ActorFor(player).ParticipantId;
            Assert.IsFalse(inventory.BackpackSlots.Contains(found), "the item left the run");
            Assert.AreSame(found, session.Storage.Find(found.InstanceId), "it is in Shelter Storage");
            Assert.IsFalse(found.IsAtRisk);
            Assert.AreEqual(storageBefore + 1, session.Storage.OccupiedSlots);
            Assert.IsTrue(relayRoom.State.IsResolved(SecureRelayEvent.ResolvedIdFor(participant)));
            Assert.AreEqual(DungeonEventPhase.Available, relay.Phase, "the relay stays open for the other members");
            var onDisk = session.Saves.Load();
            Assert.IsTrue(onDisk.Success, onDisk.Diagnostics.ToString());
            Assert.IsTrue(onDisk.Slot.Storage.Slots.Any(e => e.Item != null && e.Item.InstanceId == found.InstanceId && !e.Item.IsAtRisk), "the save on disk holds the secured item");
            Assert.IsTrue(onDisk.Slot.ActiveExpedition.IsOpen, "the run itself is still open");
            Note($"secured {weaponDef.Id} ({found.InstanceId}) for '{participant}'; storage {storageBefore} → {session.Storage.OccupiedSlots}; on disk: yes");

            // ---- used state: the world object and a second use ----
            view.Buttons[SecureRelayView.CloseFocusId].SimulateClick();
            yield return Settle();
            Assert.IsFalse(vm.IsOpen);
            Assert.AreEqual("event_SecureRelay_secured", visual.Key, "the relay reads as spent for this player");
            Assert.IsTrue(run.CurrentInteractionPrompt.Contains("ITEM SECURED"), $"prompt was '{run.CurrentInteractionPrompt}'");
            Note($"used prompt: '{run.CurrentInteractionPrompt}'");
            yield return CameraCaughtUp();
            LiveDungeonCapture.Capture(Folder, "relay_05_world_used", camera, ppu, includeUi: true);
            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            Assert.IsTrue(inventory.TryAddToBackpack(vest));
            Assert.IsTrue(interactor.TryInteract());
            yield return null;
            Assert.IsTrue(vm.IsOpen);
            Assert.AreEqual(SecureRelayStage.Secured, vm.Stage, "reopening shows the ITEM SECURED card, not a second choice");
            Assert.IsFalse(vm.Secure());
            Assert.AreEqual(SecureRelayRefusal.AlreadySecured, relay.Secure(participant, player.GetComponent<PlayerLootReceiver>().CarriedContainers, vest.InstanceId, new SecureRelayStorageTarget(session.Storage)).Refusal,
                "no route secures a second item for this player");
            Assert.AreEqual(storageBefore + 1, session.Storage.OccupiedSlots);
            Assert.IsTrue(inventory.BackpackSlots.Contains(vest));
            vm.Close();
            yield return null;

            // ---- the run is lost: everything carried goes, the secured item stays home ----
            var summary = run.Expedition.Fail();
            yield return null;
            Assert.IsFalse(summary.LostItems.Any(l => l.InstanceId == found.InstanceId));
            Assert.IsTrue(summary.LostItems.Any(l => l.InstanceId == vest.InstanceId), "death rules for everything else are unchanged");
            var afterLoss = session.Saves.Load();
            Assert.IsTrue(afterLoss.Success, afterLoss.Diagnostics.ToString());
            Assert.IsFalse(afterLoss.Slot.ActiveExpedition.IsOpen);
            Assert.AreEqual(1, afterLoss.Slot.Storage.Slots.Count(e => e.Item != null && e.Item.InstanceId == found.InstanceId), "exactly one copy, in Storage");
            Note($"run lost: {summary.LostItems.Count} carried item(s) lost, secured item still in Storage on disk");
            Assert.AreEqual(1, run.RelayTransfers);
        }
    }
}
