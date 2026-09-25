using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using RuinRail.UI.Theme;
using UnityEngine;

namespace RuinRail.App
{
    /// <summary>
    /// Graphical-inventory stage of the built-player smoke (`-inventoryproofdir dir`, default: the proof dir): the
    /// window opens over the live run with the survivor's real loadout, every equipment slot and backpack slot is a
    /// graphical slot with the definition's icon, selection/focus/hover are visible states, the details panel follows
    /// the cursor, the mouse path (hover, click, click-to-move) and the keyboard/controller path (focus list on the
    /// menu input stack, 2D navigation, action buttons) both work, gameplay input is held while the window is up, and
    /// close/reopen keeps the state. Captures are written by the shipped executable.
    /// </summary>
    public sealed partial class SmokeRunner
    {
        public const string InventoryProofDirArgument = "-inventoryproofdir";

        private string InventoryProofDir
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                var index = Array.IndexOf(args, InventoryProofDirArgument);
                if (index < 0 || index + 1 >= args.Length) return ProofDir;
                var dir = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private IEnumerator CaptureInventory(string name)
        {
            var dir = InventoryProofDir;
            if (string.IsNullOrEmpty(dir)) yield break;
            var path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            for (var i = 0; i < 180 && !File.Exists(path); i++) yield return null;
            if (File.Exists(path)) _captures.Add(path);
            Debug.Log("[SMOKE] capture " + (File.Exists(path) ? "written → " + path : "NOT written: " + path));
        }

        private IEnumerator InventoryChecks(ExpeditionScene run)
        {
            var passed = new List<string>();
            void Check(string what, bool ok) { if (ok) passed.Add(what); else if (string.IsNullOrEmpty(_result.Error)) Fail("inventory: " + what); }
            var vm = run.Inventory;
            var view = run.InventoryView;
            var inventory = run.Expedition.State.Inventory;
            var content = _app.Content;
            if (vm == null || view == null) { Check("inventory view composed", false); yield break; }

            // Representative backpack contents for the proof (the smoke profile is a throw-away): a Rare SMG and an Uncommon accessory beside the ammo stack.
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Rare);
            var pouch = new ItemInstance("accessory_ammo_pouch", 1, Rarity.Uncommon);
            inventory.TryAddToBackpack(smg);
            inventory.TryAddToBackpack(pouch);

            var timeScaleBefore = Time.timeScale;
            vm.Open();
            yield return null;
            Check("inventory opens as a window", vm.IsOpen && view.IsVisible);
            Check("solo inventory pauses the world and holds gameplay input", Time.timeScale == 0f && GameplayInputGate.IsHeld && run.Rig.Reader.Move == Vector2.zero && !run.Rig.Reader.FireHeld);
            Check($"inventory takes the pointer cursor (cursor {CursorService.Current}, base {CursorService.Base}, overlays {CursorService.Overlays})", PointerLayerOwnsCursor);
            Check("skin frames bound (panel, slot, 5 rarity frames)", UiSkin.Load() != null && UiSkin.Load().HasInventoryFrames);
            Check("five graphical equipment slots", view.EquipmentSlots.Count == 5);
            Check("eight-slot graphical backpack grid (4 x 2)", view.BackpackSlots.Count == InventoryViewModel.BackpackSlots && InventoryView.BackpackColumns == 4);
            Check("every text on the pixel face", view.GetComponentsInChildren<UnityEngine.UI.Text>(true).All(t => t.font == UiFont.Font()));

            Sprite Icon(string id) => content.Items.First(i => i != null && i.Id == id).Icon;
            var primary = view.EquipmentSlots[0];
            var secondary = view.EquipmentSlots[1];
            var armor = view.EquipmentSlots[2];
            var consumable = view.EquipmentSlots[4];
            Check("primary slot shows the P9 Ranger icon", primary.IsOccupied && primary.IconSprite == Icon(StarterKitService.PistolId));
            Check("secondary slot shows the Field Knife icon", secondary.IsOccupied && secondary.IconSprite == Icon(StarterKitService.KnifeId));
            Check("armor slot shows the Scrap Vest icon", armor.IsOccupied && armor.IconSprite == Icon(StarterKitService.VestId));
            Check("consumable slot shows the Bandage icon", consumable.IsOccupied && consumable.IconSprite == Icon(StarterKitService.BandageId));
            Check("accessory slot reads as empty", !view.EquipmentSlots[3].IsOccupied && view.EquipmentTexts[3].Contains("empty"));
            Check("occupied slots carry their rarity frame", primary.FrameSprite != null && view.BackpackSlots.Where(s => s.IsOccupied).All(s => s.FrameSprite != null));
            var ammoSlotIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == StarterKitService.LightAmmoId);
            Check("ammo stack shows its icon and count", ammoSlotIndex >= 0 && view.BackpackSlots[ammoSlotIndex].IconSprite == Icon(StarterKitService.LightAmmoId) && view.BackpackSlots[ammoSlotIndex].CountText == "x" + inventory.BackpackSlots[ammoSlotIndex].Quantity);
            Check("coins and ammo reserves shown", view.CoinsText.Contains("COINS") && view.AmmoTexts.Count == 4 && view.AmmoTexts[0].StartsWith("LIGHT"));
            Check("survivor portrait shown", view.PortraitSprite != null);
            yield return CaptureInventory("inv_01_inventory_window_open");

            // Keyboard / controller: the focus list is on the menu input stack; 2D navigation moves the same cursor.
            var menuInput = FindFirstObjectByType<MenuInput>();
            Check("inventory focus list on the menu input stack", menuInput != null && menuInput.Stack.Current == view.FocusList && view.FocusList.HasNavigator);
            var smgIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == smg.InstanceId);
            vm.SetCursor(new InventorySlotRef(InventorySlotKind.Equipped, 0));
            yield return null;
            menuInput.Stack.Navigate(Vector2Int.down);
            Check("keyboard down moves the cursor to SECONDARY", vm.Cursor.Equals(new InventorySlotRef(InventorySlotKind.Equipped, 1)) && secondary.ShowsFocusBrackets && !primary.ShowsFocusBrackets);
            menuInput.Stack.Navigate(Vector2Int.right);
            Check("keyboard right crosses into the backpack grid", vm.Cursor.Kind == InventorySlotKind.Backpack && view.BackpackSlots[vm.Cursor.Index].ShowsFocusBrackets);
            for (var i = 0; i < 4; i++) menuInput.Stack.Navigate(Vector2Int.left);
            menuInput.Stack.Navigate(Vector2Int.right);
            for (var guard = 0; guard < 16 && vm.Cursor.Index != smgIndex; guard++) { var target = smgIndex > vm.Cursor.Index ? Vector2Int.right : Vector2Int.left; if (smgIndex / 4 != vm.Cursor.Index / 4) target = smgIndex / 4 > vm.Cursor.Index / 4 ? Vector2Int.down : Vector2Int.up; if (!menuInput.Stack.Navigate(target)) break; }
            yield return null;
            Check("details panel follows the cursor (Rare SMG)", vm.Cursor.Index == smgIndex && view.DetailTitleText.Contains("Rattler") && view.DetailSubtitleText.Contains("RARE") && view.DetailPager.Rows.Any(r => r.Key.StartsWith("Damage")) && view.DetailPager.Rows.Any(r => r.Key.Contains("VS EQUIPPED")));
            // The description leads the panel; the stats and the comparison follow, paged rather than cut.
            Check("description leads the details, comparison reachable by paging", view.DetailRowTexts[0].Length > 0 && view.DetailPager.Rows[0].Key == view.DetailRowTexts[0] && (view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")) || view.DetailsPageDown() && (view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")) || view.DetailsPageDown())));
            while (view.DetailsPageUp()) { }
            menuInput.Stack.Activate();
            yield return null;
            Check("confirm selects the item (selected frame shown)", vm.Selected.HasValue && vm.Selected.Value.Index == smgIndex && view.BackpackSlots[smgIndex].ShowsSelectedFrame);
            yield return CaptureInventory("inv_02_selected_item_details_panel");

            // Move the selection with the keyboard onto PRIMARY: a swap through the transfer service.
            while (vm.Cursor.Kind != InventorySlotKind.Equipped) if (!menuInput.Stack.Navigate(Vector2Int.left)) break;
            while (vm.Cursor.Index != 0) if (!menuInput.Stack.Navigate(Vector2Int.up)) break;
            menuInput.Stack.Activate();
            yield return null;
            Check("keyboard move swaps the SMG into PRIMARY and parks the pistol in the backpack", inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.InstanceId == smg.InstanceId && inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == StarterKitService.PistolId) && primary.IconSprite == Icon("weapon_rattler_9"));
            yield return CaptureInventory("inv_03_after_keyboard_swap_primary_is_smg");

            // Mouse: hover moves the cursor + details, click selects, click on the target slot moves (pistol back to PRIMARY).
            var pistolIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == StarterKitService.PistolId);
            var pistolSlot = view.BackpackSlots[pistolIndex];
            pistolSlot.SimulateHover(true);
            yield return null;
            Check("mouse hover moves the cursor and the details to the hovered slot", vm.Cursor.Equals(pistolSlot.Slot) && pistolSlot.ShowsHover && view.DetailTitleText.Contains("P9 Ranger"));
            pistolSlot.SimulateClick();
            yield return null;
            Check("mouse click selects the hovered item", vm.Selected.HasValue && vm.Selected.Value.Equals(pistolSlot.Slot));
            pistolSlot.SimulateHover(false);
            primary.SimulateHover(true);
            primary.SimulateClick();
            yield return null;
            primary.SimulateHover(false);
            Check("mouse click on PRIMARY swaps the pistol back through the transfer service", inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId == StarterKitService.PistolId && inventory.BackpackSlots.Any(i => i != null && i.InstanceId == smg.InstanceId) && !vm.Selected.HasValue);
            var pouchIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == pouch.InstanceId);
            view.EquipmentSlots[3].SimulateDrop(view.BackpackSlots[pouchIndex]);
            yield return null;
            Check("drag-and-drop equips the accessory into its slot", inventory.GetEquipped(EquippedSlot.Accessory)?.InstanceId == pouch.InstanceId && view.EquipmentSlots[3].IconSprite == Icon("accessory_ammo_pouch"));
            var equipButton = view.Buttons[InventoryView.ActionFocusId];
            vm.SetCursor(new InventorySlotRef(InventorySlotKind.Equipped, 3));
            yield return null;
            equipButton.SimulateClick();
            yield return null;
            Check("UNEQUIP button returns the accessory to the backpack", inventory.GetEquipped(EquippedSlot.Accessory) == null && inventory.BackpackSlots.Any(i => i != null && i.InstanceId == pouch.InstanceId));
            Check("no item duplicated or lost by the UI operations", ItemTransferService.DetectDuplicateOwnership(AllContainers(inventory)).Count == 0 && AllIds(inventory).Count(id => id == smg.InstanceId) == 1 && AllIds(inventory).Count(id => id == pouch.InstanceId) == 1);
            yield return CaptureInventory("inv_04_after_mouse_swap_and_drag_drop");

            // Invalid action feedback: armor into a weapon slot is refused with a message, nothing changes.
            var armorItem = inventory.GetEquipped(EquippedSlot.Armor);
            var refused = vm.MoveTo(new InventorySlotRef(InventorySlotKind.Equipped, 2), new InventorySlotRef(InventorySlotKind.Equipped, 0));
            yield return null;
            Check("invalid move is refused with visible feedback", refused == InventoryActionResult.IncompatibleSlot && view.MessageText.Length > 0 && inventory.GetEquipped(EquippedSlot.Armor) == armorItem);

            // Controller: the hints switch with the device and the focus brackets are the cue; navigate down to the action buttons.
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            view.FocusList.Focus("slot.ActiveConsumable"); // the D-pad continues from the focused slot (the action buttons keep focus otherwise)
            yield return null;
            menuInput.Stack.Navigate(Vector2Int.down);
            yield return null;
            var actionFocused = view.FocusList.Focused != null && view.FocusList.Focused.Id == InventoryView.ActionFocusId;
            Check("controller down from the equipment column reaches the UNEQUIP button with focus brackets", actionFocused && equipButton.ShowsFocusBrackets && view.HintsText.Contains("D-PAD"));
            yield return CaptureInventory("inv_05_controller_focus_on_action_button");
            menuInput.Stack.Navigate(Vector2Int.down);
            menuInput.Stack.Navigate(Vector2Int.down);
            Check("controller reaches CLOSE", view.FocusList.Focused != null && view.FocusList.Focused.Id == InventoryView.CloseFocusId);
            menuInput.Stack.Activate();
            yield return null;
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            Check("CLOSE closes the window, resumes the world and releases input", !vm.IsOpen && !view.IsVisible && Time.timeScale == timeScaleBefore && !GameplayInputGate.IsHeld && CursorService.Current == CursorKind.Aim && !menuInput.Stack.Contains(view.FocusList));

            // Reopen: the same window, the same loadout, no second panel instance.
            vm.Open();
            yield return null;
            Check("reopen shows the same single window with the state intact", vm.IsOpen && view.IsVisible && FindObjectsByType<InventoryView>(FindObjectsSortMode.None).Length == 1 && inventory.GetEquipped(EquippedSlot.PrimaryWeapon)?.DefinitionId == StarterKitService.PistolId && menuInput.Stack.Current == view.FocusList);
            vm.Close();
            yield return null;
            Check("closed again", !vm.IsOpen && Time.timeScale == timeScaleBefore);

            _result.InventoryChecks = passed.ToArray();
            Debug.Log("[SMOKE] inventory: " + string.Join("; ", passed));
        }

        private static IEnumerable<string> AllIds(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) yield return e.InstanceId;
            }

            foreach (var b in inventory.BackpackSlots) if (b != null) yield return b.InstanceId;
        }

        private static List<IItemContainer> AllContainers(PlayerInventory inventory)
        {
            var containers = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot))) containers.Add(new EquippedSlotContainer(inventory, slot));
            return containers;
        }
    }
}
