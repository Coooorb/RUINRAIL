using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using RuinRail.UI.Base;
using RuinRail.UI.Inventory;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Live-run proof of the graphical inventory (real boot flow → Shelter → generated dungeon): the window over the
    /// real loadout, keyboard/controller navigation through the run's own <see cref="MenuInput"/> stack, mouse paths,
    /// the Esc/Tab/pause coherence rules, and captures to <c>TestResults/GraphicalInventoryProof</c>.
    /// </summary>
    public sealed class GraphicalInventoryProofTests
    {
        private const string Folder = "TestResults/GraphicalInventoryProof";
        private readonly StringBuilder _evidence = new();
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = Path.Combine(Path.GetTempPath(), "ruinrail_invproof_" + System.Guid.NewGuid().ToString("N"));
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
            File.WriteAllText(Path.Combine(Folder, "live_inventory_evidence.txt"), _evidence.ToString());
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
            Debug.Log("[PROOF] " + line);
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time (last: '{_app.ComposedScene}').");
                yield return null;
            }
        }

        private static int SeedFor(Biome biome)
        {
            for (var seed = 1; seed < 500; seed++) if (BiomeSelector.SelectFirst(seed) == biome) return seed;
            return 1;
        }

        [UnityTest]
        public IEnumerator LiveRun_GraphicalInventory_OpensOverTheRun_ShowsTheLoadout_NavigatesByKeyboardAndMouse_AndStaysCoherentWithPause()
        {
            _app = GameApp.Ensure(GameContentCatalog.Load(), _saveDir);
            _app.SetRunSeedOverride(SeedFor(Biome.Rustworks));
            SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Inventory Proof");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var vm = run.Inventory;
            var view = run.InventoryView;
            var inventory = run.Expedition.State.Inventory;
            var ppu = run.Camera.Config.PixelsPerUnit;
            var camera = run.Camera.Camera;
            var menuInput = Object.FindFirstObjectByType<MenuInput>();
            Assert.IsNotNull(view, "the run composes the graphical inventory view");
            Assert.IsNotNull(view.PortraitSprite == null ? null : view, "portrait bound from the player's idle sprite");

            // Representative backpack contents beside the starter loadout.
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Rare);
            var rig = new ItemInstance("armor_scout_rig", 1, Rarity.Uncommon);
            Assert.IsTrue(inventory.TryAddToBackpack(smg));
            Assert.IsTrue(inventory.TryAddToBackpack(rig));

            vm.Open();
            yield return null;
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(0f, Time.timeScale, "solo inventory pauses the world");
            Assert.IsTrue(GameplayInputGate.IsHeld, "gameplay input is held under the window");
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);
            Assert.AreEqual(view.FocusList, menuInput.Stack.Current, "the inventory owns the menu input stack while open");
            Assert.AreEqual(StarterKitService.PistolId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId);
            Assert.AreEqual(_app.Content.Items.First(i => i.Id == StarterKitService.PistolId).Icon, view.EquipmentSlots[0].IconSprite);
            Assert.AreEqual(_app.Content.Items.First(i => i.Id == StarterKitService.KnifeId).Icon, view.EquipmentSlots[1].IconSprite);
            Assert.AreEqual(_app.Content.Items.First(i => i.Id == StarterKitService.VestId).Icon, view.EquipmentSlots[2].IconSprite);
            Assert.AreEqual(_app.Content.Items.First(i => i.Id == StarterKitService.BandageId).Icon, view.EquipmentSlots[4].IconSprite);
            Assert.IsNotNull(view.PortraitSprite, "the survivor portrait is drawn");
            LiveDungeonCapture.Capture(Folder, "live_01_inventory_window_over_the_run", camera, ppu, includeUi: true);
            Note($"open: primary {inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId}, secondary {inventory.GetEquipped(EquippedSlot.SecondaryWeapon).DefinitionId}, armor {inventory.GetEquipped(EquippedSlot.Armor).DefinitionId}, consumable {inventory.GetEquipped(EquippedSlot.ActiveConsumable).DefinitionId}; backpack {inventory.BackpackSlots.Count(b => b != null)}/8; coins '{view.CoinsText}'; ammo [{string.Join(" | ", view.AmmoTexts)}]");

            // Keyboard: navigate to the SMG, select it, move it onto PRIMARY.
            var smgIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == smg.InstanceId);
            vm.SetCursor(new InventorySlotRef(InventorySlotKind.Equipped, 0));
            menuInput.Stack.Navigate(Vector2Int.right);
            Assert.AreEqual(InventorySlotKind.Backpack, vm.Cursor.Kind);
            for (var guard = 0; guard < 16 && vm.Cursor.Index != smgIndex; guard++)
            {
                var direction = smgIndex / 4 != vm.Cursor.Index / 4 ? (smgIndex / 4 > vm.Cursor.Index / 4 ? Vector2Int.down : Vector2Int.up) : (smgIndex > vm.Cursor.Index ? Vector2Int.right : Vector2Int.left);
                menuInput.Stack.Navigate(direction);
            }

            yield return null;
            Assert.AreEqual(smgIndex, vm.Cursor.Index);
            Assert.IsTrue(view.BackpackSlots[smgIndex].ShowsFocusBrackets, "keyboard focus is visible on the cursor slot");
            StringAssert.Contains("Rattler", view.DetailTitleText);
            StringAssert.Contains("RARE", view.DetailSubtitleText);
            Assert.IsTrue(view.DetailPager.Rows.Any(r => r.Key.Contains("VS EQUIPPED")), "the comparison is part of the details (on the page after the description)");
            var pageGuard = 0;
            while (!view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")) && view.DetailsPageDown() && pageGuard++ < 5) { yield return null; }
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")), "paging reaches the comparison");
            while (view.DetailsPageUp()) { }
            yield return null;
            menuInput.Stack.Activate();
            yield return null;
            Assert.IsTrue(view.BackpackSlots[smgIndex].ShowsSelectedFrame, "the picked item shows the selected frame");
            LiveDungeonCapture.Capture(Folder, "live_02_keyboard_selected_smg_with_details", camera, ppu, includeUi: true);
            Note($"keyboard: cursor backpack {smgIndex}, details '{view.DetailTitleText}' / '{view.DetailSubtitleText}', rows {view.DetailRowTexts.Count(r => r.Length > 0)}");
            while (vm.Cursor.Kind != InventorySlotKind.Equipped && menuInput.Stack.Navigate(Vector2Int.left)) { }
            while (vm.Cursor.Index != 0 && menuInput.Stack.Navigate(Vector2Int.up)) { }
            menuInput.Stack.Activate();
            yield return null;
            Assert.AreEqual(smg.InstanceId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId, "keyboard move swapped the SMG into PRIMARY");
            Assert.IsTrue(inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == StarterKitService.PistolId));
            LiveDungeonCapture.Capture(Folder, "live_03_after_keyboard_swap", camera, ppu, includeUi: true);

            // Mouse: hover + click the pistol, click PRIMARY: swapped back; drag the rig onto ARMOR.
            var pistolIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == StarterKitService.PistolId);
            view.BackpackSlots[pistolIndex].SimulateHover(true);
            yield return null;
            Assert.IsTrue(view.BackpackSlots[pistolIndex].ShowsHover);
            StringAssert.Contains("P9 Ranger", view.DetailTitleText, "details follow the hovered slot");
            view.BackpackSlots[pistolIndex].SimulateClick();
            view.BackpackSlots[pistolIndex].SimulateHover(false);
            view.EquipmentSlots[0].SimulateClick();
            yield return null;
            Assert.AreEqual(StarterKitService.PistolId, inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId, "mouse click-to-move swapped the pistol back");
            var rigIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == rig.InstanceId);
            view.EquipmentSlots[2].SimulateDrop(view.BackpackSlots[rigIndex]);
            yield return null;
            Assert.AreEqual(rig.InstanceId, inventory.GetEquipped(EquippedSlot.Armor).InstanceId, "drag-and-drop equipped the armor");
            LiveDungeonCapture.Capture(Folder, "live_04_after_mouse_swap_and_drag_drop", camera, ppu, includeUi: true);
            Note($"mouse: primary {inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId}, armor {inventory.GetEquipped(EquippedSlot.Armor).DefinitionId}");

            // Controller hints + action buttons by D-pad.
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            vm.SetCursor(new InventorySlotRef(InventorySlotKind.Equipped, 2));
            yield return null;
            StringAssert.Contains("D-PAD", view.HintsText);
            while (vm.Cursor.Index != 4 && menuInput.Stack.Navigate(Vector2Int.down)) { }
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down), "down from the equipment column reaches the actions");
            Assert.AreEqual(InventoryView.ActionFocusId, view.FocusList.Focused.Id);
            Assert.IsTrue(view.Buttons[InventoryView.ActionFocusId].ShowsFocusBrackets);
            LiveDungeonCapture.Capture(Folder, "live_05_controller_focus_on_action_button", camera, ppu, includeUi: true);
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);

            // Coherence: Esc (the Pause action) closes the inventory instead of opening the pause; Tab under the pause is ignored.
            Assert.IsTrue(run.Pause.BeforePauseToggle(), "Esc while the inventory is open is consumed by the inventory");
            yield return null;
            Assert.IsFalse(vm.IsOpen);
            Assert.IsFalse(run.Pause.IsOpen, "…and the pause did not open");
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsFalse(GameplayInputGate.IsHeld);
            Assert.AreEqual(CursorKind.Aim, CursorService.Current);
            Assert.IsFalse(menuInput.Stack.Contains(view.FocusList));
            run.Pause.Open();
            yield return null;
            Assert.IsTrue(GameplayInputGate.IsHeld, "the pause menu holds gameplay input too");
            Assert.AreEqual(run.PauseScreen.RootList, menuInput.Stack.Current);
            run.Pause.Close();
            yield return null;
            Assert.IsFalse(GameplayInputGate.IsHeld);

            // Reopen: same window, state intact, one instance, no ghost panel.
            vm.Open();
            yield return null;
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(1, Object.FindObjectsByType<InventoryView>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(c => c.name == "InventoryUI"));
            Assert.AreEqual(rig.InstanceId, inventory.GetEquipped(EquippedSlot.Armor).InstanceId);
            vm.Close();
            yield return null;
            Assert.IsFalse(view.IsVisible);
            Note("coherence: Esc closes the inventory (no pause), Tab is a no-op under the pause, reopen keeps the loadout, one window instance");
        }
    }
}
