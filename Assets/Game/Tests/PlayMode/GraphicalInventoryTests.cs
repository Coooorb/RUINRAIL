using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.UI;

namespace RuinRail.Tests
{
    /// <summary>
    /// The graphical inventory window (92) over a real inventory: graphical slots with the definitions' icons for
    /// every category, the 4×2 backpack grid, the details panel following the cursor, the three visible states
    /// (hover / focus / selected), mouse + keyboard/controller navigation ending in the same transfer-service actions,
    /// the gameplay input hold while open, close/reopen state, and no text clipping or overlap at the reference
    /// resolution.
    /// </summary>
    public sealed class GraphicalInventoryTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private ItemDefinitionRegistry _registry;
        private LegendarySpecialRegistry _specials;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        private sealed class CountingPause : IWorldPause
        {
            public int Pauses;
            public int Resumes;
            public void Pause() => Pauses++;
            public void Resume() => Resumes++;
        }

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            _registry = _content.BuildRegistry();
            _specials = _content.BuildSpecials();
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("Spawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(Resolve);
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
            GameplayInputGate.Reset();
            CursorService.Reset();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private (InventoryViewModel vm, InventoryView view, PlayerInventory inventory) Build(CountingPause pause = null)
        {
            var inventory = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            var go = new GameObject("Player");
            _created.Add(go);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(new CoinWallet(CoinDomain.Carried, 340));
            receiver.SetDropService(new ItemDropService(_spawner));
            var vm = new InventoryViewModel();
            vm.Bind(inventory, receiver, () => 340, _specials);
            vm.ConfigurePause(pause ?? new CountingPause(), isCoop: false);
            var view = InventoryView.Create(vm);
            _created.Add(view.gameObject);
            return (vm, view, inventory);
        }

        /// <summary>The starter loadout plus one item of every category in the backpack.</summary>
        private static void FillRepresentative(PlayerInventory inventory)
        {
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_field_knife"), EquippedSlot.SecondaryWeapon));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("consumable_bandage", 2), EquippedSlot.ActiveConsumable));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_rattler_9", 1, Rarity.Rare)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("armor_scout_rig", 1, Rarity.Uncommon)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("accessory_ammo_pouch", 1, Rarity.Epic)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 3)));
            Assert.AreEqual(60, inventory.Add(AmmoType.Light, 60));
        }

        private static InventorySlotRef Bag(int i) => new(InventorySlotKind.Backpack, i);
        private static InventorySlotRef Eq(EquippedSlot slot) => new(InventorySlotKind.Equipped, (int)slot);

        // ---- layout / presentation ----

        [Test]
        public void Window_HasEquipmentColumn_CharacterPanel_4x2BackpackGrid_AndDetailsPanel_InsideTheReferenceScreen()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            Assert.IsTrue(view.IsVisible);
            Assert.AreEqual(5, view.EquipmentSlots.Count);
            Assert.AreEqual(8, view.BackpackSlots.Count);
            Assert.AreEqual(InventoryViewModel.BackpackSlots, view.BackpackSlots.Count, "the authoritative backpack size");
            CollectionAssert.AreEqual(new[] { "PRIMARY", "SECONDARY", "ARMOR", "ACCESSORY", "CONSUMABLE" }, view.EquipmentTexts.Select(t => t.Split('\n')[0]).ToList());

            var screen = ScreenLayout.Screen;
            foreach (var panel in new[] { InventoryView.Window, InventoryView.EquipmentPanel, InventoryView.CharacterPanel, InventoryView.BackpackPanel, InventoryView.DetailsPanel })
                Assert.IsTrue(panel.Within(screen), panel.ToString());
            Assert.IsTrue(InventoryView.EquipmentPanel.Within(InventoryView.Window) && InventoryView.CharacterPanel.Within(InventoryView.Window) && InventoryView.BackpackPanel.Within(InventoryView.Window) && InventoryView.DetailsPanel.Within(InventoryView.Window));
            Assert.IsFalse(InventoryView.EquipmentPanel.Overlaps(InventoryView.CharacterPanel));
            Assert.IsFalse(InventoryView.CharacterPanel.Overlaps(InventoryView.BackpackPanel));
            Assert.IsFalse(InventoryView.BackpackPanel.Overlaps(InventoryView.DetailsPanel));
            Assert.AreEqual(0, InventoryView.Window.X % 4 + InventoryView.Window.Y % 4, "the window sits on the 4 px grid");

            // The backpack is a real 4 × 2 grid of equal, non-overlapping, hover-sized slots.
            var rects = view.BackpackSlots.Select(s => s.Rect).ToList();
            var xs = rects.Select(r => r.anchoredPosition.x).Distinct().OrderBy(x => x).ToList();
            var ys = rects.Select(r => r.anchoredPosition.y).Distinct().OrderByDescending(y => y).ToList();
            Assert.AreEqual(4, xs.Count, "four columns");
            Assert.AreEqual(2, ys.Count, "two rows");
            Assert.IsTrue(rects.All(r => r.sizeDelta.x >= 24 && r.sizeDelta.y >= 24), "no unreadable tiny hover targets");
            for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
                Assert.IsFalse(Box(rects[i]).Overlaps(Box(rects[j])), $"backpack slots {i} and {j} overlap");
            Assert.AreEqual(5, view.EquipmentSlots.Select(s => s.Rect.anchoredPosition.y).Distinct().Count(), "the equipment slots form one column of five");
        }

        private static Rect Box(RectTransform rect) => new(rect.anchoredPosition.x, -rect.anchoredPosition.y, rect.sizeDelta.x, rect.sizeDelta.y);

        [Test]
        public void EverySlot_DrawsTheDefinitionIcon_RarityFrame_AndStackCount_ForEveryCategory()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            var skin = UiSkin.Load();
            Assert.IsTrue(skin.HasInventoryFrames, "the skin binds the panel, slot and five rarity frames");
            Sprite Icon(string id) => Resolve(id).Icon;
            Assert.AreEqual(Icon("weapon_p9_ranger"), view.EquipmentSlots[0].IconSprite);
            Assert.AreEqual(Icon("weapon_field_knife"), view.EquipmentSlots[1].IconSprite);
            Assert.AreEqual(Icon("armor_scrap_vest"), view.EquipmentSlots[2].IconSprite);
            Assert.IsNull(view.EquipmentSlots[3].IconSprite, "empty accessory slot draws no icon");
            Assert.IsFalse(view.EquipmentSlots[3].IsOccupied);
            StringAssert.Contains("empty", view.EquipmentTexts[3]);
            Assert.AreEqual(Icon("consumable_bandage"), view.EquipmentSlots[4].IconSprite);
            Assert.AreEqual("x2", view.EquipmentSlots[4].CountText, "consumable count on the slot");

            foreach (var (id, rarity, count) in new[] { ("weapon_rattler_9", Rarity.Rare, ""), ("armor_scout_rig", Rarity.Uncommon, ""), ("accessory_ammo_pouch", Rarity.Epic, ""), ("consumable_medkit", Rarity.Common, "x3"), ("ammo_light", Rarity.Common, "x60") })
            {
                var index = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == id);
                Assert.GreaterOrEqual(index, 0, id);
                var slot = view.BackpackSlots[index];
                Assert.IsTrue(slot.IsOccupied, id);
                Assert.AreEqual(Icon(id), slot.IconSprite, id + " icon bound through ItemDefinition.Icon");
                Assert.AreEqual(skin.RarityFrame((int)rarity), slot.FrameSprite, id + " rarity frame");
                Assert.AreEqual(count, slot.CountText, id + " count");
            }

            Assert.IsTrue(view.BackpackSlots.Skip(5).All(s => !s.IsOccupied && s.IconSprite == null && s.FrameSprite == null && s.CountText == string.Empty), "empty backpack slots are consistent placeholders");
            Assert.IsTrue(_content.Items.All(i => i != null && i.HasIcon), "every catalog item has an icon bound");
        }

        [Test]
        public void DetailsPanel_FollowsTheCursor_WithNameRarityCategoryStatsAndComparison_AndTextsNeverClip()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            var smgIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "weapon_rattler_9");
            vm.SetCursor(Bag(smgIndex));
            StringAssert.Contains("Rattler", view.DetailTitleText);
            StringAssert.Contains("RARE", view.DetailSubtitleText);
            StringAssert.Contains("WEAPON", view.DetailSubtitleText);
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.StartsWith("Damage")), "weapon stats");
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.StartsWith("Fire rate")));
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.StartsWith("Magazine")));
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.StartsWith("Ammo")));
            // The description leads (ui/93 update); the comparison follows on the next page, reachable through the pager.
            Assert.IsTrue(view.DetailPager.Rows.Any(r => r.Key.Contains("VS EQUIPPED")), "93: compared against the equipped weapon");
            var guard = 0;
            while (!view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")) && view.DetailsPageDown() && guard++ < 5) { }
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.Contains("VS EQUIPPED")), "the comparison page is reachable");
            while (view.DetailsPageUp()) { }
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.Contains("–")), "damage ranges keep their en dash");

            vm.SetCursor(Eq(EquippedSlot.Armor));
            StringAssert.Contains("Scrap Vest", view.DetailTitleText);
            StringAssert.Contains("EQUIPPED", view.DetailSubtitleText);
            Assert.IsTrue(view.DetailRowTexts.Any(r => r.StartsWith("Max HP")), "armor stats");
            vm.SetCursor(Eq(EquippedSlot.Accessory));
            StringAssert.Contains("EMPTY", view.DetailTitleText);
            Assert.IsTrue(view.DetailRowTexts.All(string.IsNullOrEmpty));
            var medkit = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "consumable_medkit");
            vm.SetCursor(Bag(medkit));
            StringAssert.Contains("x3", view.DetailSubtitleText);
            StringAssert.Contains("CONSUMABLE", view.DetailSubtitleText);

            // No text clips its box: every label's fixed-advance width fits the box it was given (single-line boxes).
            foreach (var text in view.GetComponentsInChildren<Text>(true).Where(t => t.enabled && !string.IsNullOrEmpty(t.text)))
            {
                var rect = (RectTransform)text.transform;
                var scale = Mathf.RoundToInt(rect.localScale.x);
                var width = rect.sizeDelta.x * scale;
                foreach (var line in text.text.Split('\n'))
                    Assert.LessOrEqual(UiText.Width(line, scale), width + 0.01f, $"{text.name}: '{line}' fits its {width} px box");
            }
        }

        // ---- interaction ----

        [Test]
        public void Hover_Focus_AndSelection_AreDistinctVisibleStates_AndTheCursorFollowsThePointer()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            var primary = view.EquipmentSlots[0];
            Assert.IsTrue(primary.ShowsFocusBrackets, "the cursor slot shows focus brackets");
            Assert.IsFalse(primary.ShowsSelectedFrame);
            var bag1 = view.BackpackSlots[1];
            bag1.SimulateHover(true);
            Assert.IsTrue(bag1.ShowsHover);
            Assert.IsTrue(vm.Cursor.Equals(Bag(1)), "hover moved the cursor");
            Assert.IsTrue(bag1.ShowsFocusBrackets && !primary.ShowsFocusBrackets, "focus followed the cursor");
            bag1.SimulateClick();
            Assert.IsTrue(vm.Selected.HasValue && vm.Selected.Value.Equals(Bag(1)));
            Assert.IsTrue(bag1.ShowsSelectedFrame, "the picked item shows the selected frame");
            bag1.SimulateHover(false);
            Assert.IsFalse(bag1.ShowsHover || !bag1.ShowsFocusBrackets, "focus stays after the pointer leaves");
            vm.CancelSelection();
            Assert.IsFalse(bag1.ShowsSelectedFrame);
            var empty = view.BackpackSlots[7];
            empty.SimulateClick();
            Assert.IsFalse(vm.Selected.HasValue, "clicking an empty slot selects nothing");
        }

        [Test]
        public void KeyboardAndController_NavigateTheSame2DFocusList_ThroughTheMenuInputStack()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            var stack = new FocusStack();
            stack.Push(view.FocusList);
            Assert.IsTrue(stack.CurrentHasNavigator);
            Assert.AreEqual("slot.PrimaryWeapon", stack.Focused.Id);
            Assert.IsTrue(stack.Navigate(Vector2Int.down));
            Assert.IsTrue(vm.Cursor.Equals(Eq(EquippedSlot.SecondaryWeapon)));
            Assert.IsTrue(stack.Navigate(Vector2Int.right));
            Assert.IsTrue(vm.Cursor.Equals(Bag(4)), "row 2 of the equipment column crosses into the second backpack row");
            Assert.IsTrue(stack.Navigate(Vector2Int.up));
            Assert.IsTrue(vm.Cursor.Equals(Bag(0)));
            Assert.IsTrue(stack.Navigate(Vector2Int.right) && stack.Navigate(Vector2Int.right) && stack.Navigate(Vector2Int.right));
            Assert.IsTrue(vm.Cursor.Equals(Bag(3)));
            Assert.IsFalse(stack.Navigate(Vector2Int.right), "the grid edge clamps");
            Assert.IsTrue(stack.Navigate(Vector2Int.down));
            Assert.IsTrue(vm.Cursor.Equals(Bag(7)), "an empty slot is still a cursor position");
            Assert.IsTrue(stack.Navigate(Vector2Int.down), "below the bottom row: the action buttons (only CLOSE is enabled on an empty slot)");
            Assert.AreEqual(InventoryView.CloseFocusId, stack.Focused.Id, "EQUIP and DROP are disabled for an empty slot, so focus lands on CLOSE — never a dead end");
            Assert.IsTrue(view.Buttons[InventoryView.CloseFocusId].ShowsFocusBrackets, "a focused button shows brackets");
            Assert.IsFalse(stack.Navigate(Vector2Int.down), "no dead focus below CLOSE");
            Assert.IsTrue(stack.Navigate(Vector2Int.up));
            Assert.AreEqual("backpack.7", stack.Focused.Id, "up from the actions returns to the last slot");
            Assert.IsTrue(stack.Navigate(Vector2Int.left) && stack.Navigate(Vector2Int.left) && stack.Navigate(Vector2Int.left));
            Assert.IsTrue(vm.Cursor.Equals(Bag(4)), "the ammo stack");
            Assert.IsTrue(stack.Navigate(Vector2Int.down) && stack.Focused.Id == InventoryView.DropFocusId, "ammo cannot be equipped: DROP is the first enabled action");
            Assert.IsTrue(view.Buttons[InventoryView.DropFocusId].ShowsFocusBrackets);
            Assert.IsTrue(vm.Cursor.Equals(Bag(4)), "the actions act on the cursor slot, which stays");
            Assert.IsTrue(stack.Navigate(Vector2Int.down) && stack.Focused.Id == InventoryView.CloseFocusId);
            Assert.IsTrue(stack.Navigate(Vector2Int.up) && stack.Navigate(Vector2Int.up) && stack.Focused.Id == "backpack.4");
            Assert.IsTrue(stack.Navigate(Vector2Int.left));
            Assert.IsTrue(vm.Cursor.Equals(Eq(EquippedSlot.SecondaryWeapon)), "left from the first column returns to the equipment row");
            view.FocusList.Focus("slot.ActiveConsumable");
            Assert.IsTrue(stack.Navigate(Vector2Int.down) && stack.Focused.Id == InventoryView.ActionFocusId, "below the equipment column with an item under the cursor: UNEQUIP");
            Assert.AreEqual("UNEQUIP", vm.PrimaryActionLabel);
            Assert.IsTrue(view.Buttons[InventoryView.ActionFocusId].ShowsFocusBrackets);
            Assert.IsTrue(stack.Navigate(Vector2Int.up) && stack.Focused.Id == "slot.ActiveConsumable");
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            vm.SetCursor(Eq(EquippedSlot.PrimaryWeapon));
            StringAssert.Contains("D-PAD", view.HintsText, "controller hints when the pad is the active device");
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            vm.SetCursor(Eq(EquippedSlot.PrimaryWeapon));
            StringAssert.Contains("ENTER", view.HintsText);

            // Confirm on a slot selects; confirm on the target moves through the transfer service.
            var smgIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "weapon_rattler_9");
            view.FocusList.Focus("backpack." + smgIndex);
            Assert.IsTrue(stack.Activate());
            Assert.IsTrue(vm.Selected.HasValue);
            view.FocusList.Focus("slot.PrimaryWeapon");
            Assert.IsTrue(stack.Activate());
            Assert.AreEqual("weapon_rattler_9", inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId, "keyboard/controller swap into PRIMARY");
            Assert.IsTrue(inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == "weapon_p9_ranger"));
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(Containers(inventory)));
        }

        [Test]
        public void MouseClickToMove_DragAndDrop_ActionButtons_AndConsumableSlot_AllGoThroughTheService_WithoutDuplicationOrLoss()
        {
            var (vm, view, inventory) = Build();
            FillRepresentative(inventory);
            vm.Open();
            var ids = Signature(inventory);
            var smgIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "weapon_rattler_9");
            view.BackpackSlots[smgIndex].SimulateClick();
            view.EquipmentSlots[0].SimulateClick();
            Assert.AreEqual("weapon_rattler_9", inventory.GetEquipped(EquippedSlot.PrimaryWeapon).DefinitionId, "click-select then click-target swaps");
            Assert.AreEqual(view.EquipmentSlots[0].IconSprite, Resolve("weapon_rattler_9").Icon, "the slot re-renders with the new icon");

            var rigIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "armor_scout_rig");
            view.EquipmentSlots[2].SimulateDrop(view.BackpackSlots[rigIndex]);
            Assert.AreEqual("armor_scout_rig", inventory.GetEquipped(EquippedSlot.Armor).DefinitionId, "drag-and-drop swaps armor");
            Assert.IsTrue(inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == "armor_scrap_vest"));

            var medkitIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "consumable_medkit");
            view.EquipmentSlots[4].SimulateDrop(view.BackpackSlots[medkitIndex]);
            Assert.AreEqual("consumable_medkit", inventory.GetEquipped(EquippedSlot.ActiveConsumable).DefinitionId, "the consumable slot takes a consumable");
            Assert.AreEqual("x3", view.EquipmentSlots[4].CountText);
            Assert.IsTrue(inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == "consumable_bandage"), "the bandage returned to the backpack");

            // Wrong category onto the consumable slot: refused with a message, nothing changes.
            var pouchIndex = inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "accessory_ammo_pouch");
            view.EquipmentSlots[4].SimulateDrop(view.BackpackSlots[pouchIndex]);
            Assert.AreEqual("consumable_medkit", inventory.GetEquipped(EquippedSlot.ActiveConsumable).DefinitionId);
            Assert.IsNotEmpty(view.MessageText, "invalid action feedback");

            // Action buttons: EQUIP the accessory from its cursor, UNEQUIP it again, DROP the SMG.
            vm.SetCursor(Bag(pouchIndex));
            Assert.AreEqual("EQUIP", vm.PrimaryActionLabel);
            view.Buttons[InventoryView.ActionFocusId].SimulateClick();
            Assert.AreEqual("accessory_ammo_pouch", inventory.GetEquipped(EquippedSlot.Accessory).DefinitionId);
            vm.SetCursor(Eq(EquippedSlot.Accessory));
            Assert.AreEqual("UNEQUIP", vm.PrimaryActionLabel);
            view.Buttons[InventoryView.ActionFocusId].SimulateClick();
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Accessory));
            vm.SetCursor(Eq(EquippedSlot.PrimaryWeapon));
            var smg = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            view.Buttons[InventoryView.DropFocusId].SimulateClick();
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "DROP put the SMG on the ground");
            Assert.AreEqual(1, _ground.Count);
            Assert.IsFalse(view.EquipmentSlots[0].IsOccupied);

            // Stacks are re-instanced when they change container (20: absorbed into the destination's stacks); equipment keeps its instance id.
            CollectionAssert.AreEquivalent(ids.Where(i => i != smg.InstanceId), Signature(inventory), "every other item exactly once, no duplicate, no loss");
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(Containers(inventory)));
            Assert.AreEqual(0, GameplayInputGate.Holds - 1, "the window still holds gameplay input");
            view.Buttons[InventoryView.CloseFocusId].SimulateClick();
            Assert.IsFalse(vm.IsOpen);
        }

        /// <summary>Every worn slot occupied and all eight backpack slots full.</summary>
        private static void FillCompletely(PlayerInventory inventory)
        {
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_field_knife"), EquippedSlot.SecondaryWeapon));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("armor_scrap_vest"), EquippedSlot.Armor));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("accessory_magnetic_coil"), EquippedSlot.Accessory));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("consumable_bandage", 2), EquippedSlot.ActiveConsumable));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_rattler_9", 1, Rarity.Rare)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("armor_scout_rig", 1, Rarity.Uncommon)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("accessory_ammo_pouch", 1, Rarity.Epic)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 3)));
            Assert.AreEqual(60, inventory.Add(AmmoType.Light, 60));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("armor_blast_suit")));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_field_knife")));
            Assert.IsTrue(inventory.BackpackSlots.All(s => s != null), "the backpack is completely full");
        }

        private static int IndexOf(PlayerInventory inventory, string definitionId) => inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == definitionId);

        [Test]
        public void FullBackpack_SwapsEveryEquipmentCategory_ThroughMouseKeyboardControllerAndButtons_WithoutDuplicationOrLoss()
        {
            var (vm, view, inventory) = Build();
            FillCompletely(inventory);
            vm.Open();
            var stack = new FocusStack();
            stack.Push(view.FocusList);
            var ids = AllIds(inventory).OrderBy(i => i).ToList();

            void AssertSwapped(EquippedSlot slot, ItemInstance incoming, ItemInstance outgoing, int index, string how)
            {
                Assert.AreSame(incoming, inventory.GetEquipped(slot), how + ": the backpack item is worn");
                Assert.AreSame(outgoing, inventory.BackpackSlots[index], how + ": the worn item took exactly the slot the new one left");
                Assert.IsTrue(vm.IsBackpackFull, how + ": the backpack is still full");
                CollectionAssert.AreEquivalent(ids, AllIds(inventory).ToList(), how + ": every instance exactly once");
                Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(Containers(inventory)), how);
                Assert.AreEqual(0, _ground.Count, how + ": nothing touched the ground");
            }

            // PRIMARY — mouse drag from the backpack onto the worn slot.
            var i = IndexOf(inventory, "weapon_rattler_9");
            var incoming = inventory.BackpackSlots[i];
            var outgoing = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            view.EquipmentSlots[0].SimulateDrop(view.BackpackSlots[i]);
            AssertSwapped(EquippedSlot.PrimaryWeapon, incoming, outgoing, i, "primary (drag)");

            // SECONDARY — keyboard/controller: confirm on the backpack slot, then confirm on the worn slot.
            i = IndexOf(inventory, "weapon_p9_ranger");
            incoming = inventory.BackpackSlots[i];
            outgoing = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            view.FocusList.Focus("backpack." + i);
            Assert.IsTrue(stack.Activate());
            view.FocusList.Focus("slot.SecondaryWeapon");
            Assert.IsTrue(stack.Activate());
            AssertSwapped(EquippedSlot.SecondaryWeapon, incoming, outgoing, i, "secondary (confirm/confirm)");

            // ARMOR — the EQUIP action button on the cursor's backpack item.
            i = IndexOf(inventory, "armor_scout_rig");
            incoming = inventory.BackpackSlots[i];
            outgoing = inventory.GetEquipped(EquippedSlot.Armor);
            vm.SetCursor(Bag(i));
            Assert.AreEqual("EQUIP", vm.PrimaryActionLabel);
            view.Buttons[InventoryView.ActionFocusId].SimulateClick();
            AssertSwapped(EquippedSlot.Armor, incoming, outgoing, i, "armor (EQUIP button)");

            // ACCESSORY — mouse click-select, click-target.
            i = IndexOf(inventory, "accessory_ammo_pouch");
            incoming = inventory.BackpackSlots[i];
            outgoing = inventory.GetEquipped(EquippedSlot.Accessory);
            view.BackpackSlots[i].SimulateClick();
            view.EquipmentSlots[3].SimulateClick();
            AssertSwapped(EquippedSlot.Accessory, incoming, outgoing, i, "accessory (click/click)");

            // ACTIVE CONSUMABLE — a worn item dragged onto a backpack item it can trade places with (the other direction).
            i = IndexOf(inventory, "consumable_medkit");
            incoming = inventory.BackpackSlots[i];
            outgoing = inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            view.BackpackSlots[i].SimulateDrop(view.EquipmentSlots[4]);
            AssertSwapped(EquippedSlot.ActiveConsumable, incoming, outgoing, i, "consumable (worn dragged onto the backpack item)");
            Assert.AreEqual("x3", view.EquipmentSlots[4].CountText, "the stack keeps its quantity");

            // PRIMARY <-> SECONDARY with a full backpack: a direct exchange, no parking slot needed.
            var primary = inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            var secondary = inventory.GetEquipped(EquippedSlot.SecondaryWeapon);
            view.EquipmentSlots[1].SimulateDrop(view.EquipmentSlots[0]);
            Assert.AreSame(primary, inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.AreSame(secondary, inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            CollectionAssert.AreEquivalent(ids, AllIds(inventory).ToList());

            // Invalid swaps fail safely and change nothing.
            var before = Snapshot(inventory);
            view.EquipmentSlots[0].SimulateDrop(view.BackpackSlots[IndexOf(inventory, "armor_blast_suit")]);
            Assert.AreEqual("That item does not fit this slot.", vm.Message, "armor onto a weapon slot");
            view.EquipmentSlots[4].SimulateDrop(view.BackpackSlots[IndexOf(inventory, "ammo_light")]);
            Assert.AreEqual("That item does not fit this slot.", vm.Message, "ammo onto the consumable slot");
            view.BackpackSlots[IndexOf(inventory, "weapon_field_knife")].SimulateDrop(view.EquipmentSlots[2]);
            Assert.AreEqual("BACKPACK FULL", vm.Message, "worn armor onto a weapon in a full backpack has no swap partner");
            view.EquipmentSlots[2].SimulateDrop(view.EquipmentSlots[0]);
            Assert.AreEqual("That item does not fit this slot.", vm.Message, "a weapon into the armor slot");
            vm.SetCursor(Eq(EquippedSlot.Accessory));
            view.Buttons[InventoryView.ActionFocusId].SimulateClick();
            Assert.AreEqual("BACKPACK FULL", vm.Message, "UNEQUIP into a full backpack");
            Assert.AreEqual(before, Snapshot(inventory), "every refused swap left the inventory unchanged");

            // Repeated swaps back and forth never duplicate or lose anything.
            for (var n = 0; n < 12; n++)
            {
                var slot = n % 2 == 0 ? EquippedSlot.Armor : EquippedSlot.PrimaryWeapon;
                var candidate = inventory.BackpackSlots.ToList().FindIndex(b => b != null && PlayerInventory.IsSlotCompatible(Resolve(b.DefinitionId).Category, slot));
                Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(candidate), Eq(slot)), "swap " + n);
                CollectionAssert.AreEquivalent(ids, AllIds(inventory).ToList(), "swap " + n);
            }

            Assert.IsTrue(vm.IsBackpackFull);
            Assert.AreEqual(0, _ground.Count);
        }

        [Test]
        public void Drop_FromAFullBackpackAndFromAWornSlot_MovesExactlyThatItemToTheGround_AndARefusedDropKeepsIt()
        {
            var (vm, view, inventory) = Build();
            FillCompletely(inventory);
            vm.Open();
            var stack = new FocusStack();
            stack.Push(view.FocusList);

            // DROP a stack (keyboard/controller: focus the slot, then the DROP action) — exactly its quantity reaches the ground.
            var ammo = IndexOf(inventory, "ammo_light");
            var quantity = inventory.BackpackSlots[ammo].Quantity;
            var ammoBefore = inventory.Get(AmmoType.Light);
            view.FocusList.Focus("backpack." + ammo);
            view.FocusList.Focus(InventoryView.DropFocusId);
            Assert.IsTrue(stack.Activate());
            Assert.IsNull(inventory.BackpackSlots[ammo], "the dropped slot is empty");
            Assert.AreEqual(ammoBefore - quantity, inventory.Get(AmmoType.Light));
            Assert.AreEqual(1, _ground.Count);
            var pickup = _ground.Tracked[0].GetComponent<WorldItemPickup>();
            Assert.AreEqual("ammo_light", pickup.Item.DefinitionId);
            Assert.AreEqual(quantity, pickup.Item.Quantity, "the ground stack holds exactly the dropped amount");

            // DROP a worn item with the mouse (cursor + DROP button); the drop follows the same rule as any carried item.
            var armor = inventory.GetEquipped(EquippedSlot.Armor);
            vm.SetCursor(Eq(EquippedSlot.Armor));
            view.Buttons[InventoryView.DropFocusId].SimulateClick();
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Armor));
            Assert.AreEqual(2, _ground.Count);
            Assert.AreSame(armor, _ground.Tracked[1].GetComponent<WorldItemPickup>().Item, "the same instance, now owned by the ground pickup");
            Assert.IsFalse(AllIds(inventory).Contains(armor.InstanceId));
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(Containers(inventory).Concat(_ground.Tracked.Select(g => (IItemContainer)g.GetComponent<WorldItemPickup>()))));

            // Picking it back up and dropping again: the item is only ever in one place.
            var groundPickup = _ground.Tracked[1].GetComponent<WorldItemPickup>();
            Assert.IsTrue(groundPickup.TryPickUp(new BackpackContainer(inventory), new ItemTransferService()).Success);
            var back = inventory.BackpackSlots.ToList().FindIndex(b => b != null && b.InstanceId == armor.InstanceId);
            Assert.GreaterOrEqual(back, 0);
            vm.SetCursor(Bag(back));
            view.Buttons[InventoryView.DropFocusId].SimulateClick();
            Assert.IsFalse(AllIds(inventory).Contains(armor.InstanceId));
            Assert.AreEqual(1, _ground.Tracked.Count(g => g != null && g.GetComponent<WorldItemPickup>().Item?.InstanceId == armor.InstanceId));
        }

        private static string Snapshot(PlayerInventory inventory) => JsonUtility.ToJson(inventory.ToSnapshot());

        [Test]
        public void OpenClose_HoldsGameplayInput_PausesSolo_TakesThePointer_AndReopenKeepsTheState()
        {
            var pause = new CountingPause();
            var (vm, view, inventory) = Build(pause);
            FillRepresentative(inventory);
            Assert.IsFalse(GameplayInputGate.IsHeld);
            vm.Open();
            Assert.IsTrue(GameplayInputGate.IsHeld, "gameplay input is held while the window is up");
            Assert.AreEqual(1, pause.Pauses);
            vm.SetCursor(Bag(2));
            vm.Activate();
            Assert.IsTrue(vm.Selected.HasValue);
            vm.Close();
            Assert.IsFalse(GameplayInputGate.IsHeld);
            Assert.AreEqual(1, pause.Resumes);
            Assert.IsFalse(view.IsVisible);
            Assert.IsFalse(vm.Selected.HasValue, "a picked item is put back on close");
            vm.Open();
            Assert.IsTrue(view.IsVisible);
            Assert.IsTrue(vm.Cursor.Equals(Bag(2)), "the cursor position survives close/reopen");
            Assert.AreEqual(1, Object.FindObjectsByType<InventoryView>(FindObjectsSortMode.None).Length, "one window instance");
            CollectionAssert.AreEquivalent(new[] { "weapon_p9_ranger", "weapon_field_knife", "armor_scrap_vest", "consumable_bandage" }, new[] { EquippedSlot.PrimaryWeapon, EquippedSlot.SecondaryWeapon, EquippedSlot.Armor, EquippedSlot.ActiveConsumable }.Select(s => inventory.GetEquipped(s).DefinitionId));
            vm.Dispose();
            Assert.IsFalse(GameplayInputGate.IsHeld, "disposing an open inventory releases the hold");
            Assert.AreEqual(2, pause.Resumes);
        }

        [Test]
        public void PlayerInputReader_ReportsNoGameplayInput_WhileAMenuLayerHoldsTheGate()
        {
            var reader = new PlayerInputReader();
            try
            {
                GameplayInputGate.Hold();
                Assert.AreEqual(Vector2.zero, reader.Move);
                Assert.IsFalse(reader.FireHeld);
                Assert.IsFalse(reader.SpecialHeld);
                Assert.IsFalse(reader.InteractHeld);
                GameplayInputGate.Release();
                Assert.IsFalse(GameplayInputGate.IsHeld);
            }
            finally
            {
                reader.Dispose();
            }
        }

        /// <summary>Instance ids of equipment plus "definition:quantity" of every stack — the identity that survives a stack move.</summary>
        private static List<string> Signature(PlayerInventory inventory)
        {
            var registry = GameContentCatalog.Load().BuildRegistry();
            var all = new List<ItemInstance>();
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) if (inventory.GetEquipped(slot) != null) all.Add(inventory.GetEquipped(slot));
            all.AddRange(inventory.BackpackSlots.Where(b => b != null));
            return all.Select(i => registry.TryGet(i.DefinitionId, out var d) && d.IsStackable ? i.DefinitionId + ":" + i.Quantity : i.InstanceId).OrderBy(s => s).ToList();
        }

        private static IEnumerable<string> AllIds(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) yield return e.InstanceId;
            }

            foreach (var b in inventory.BackpackSlots) if (b != null) yield return b.InstanceId;
        }

        private static List<IItemContainer> Containers(PlayerInventory inventory)
        {
            var containers = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) containers.Add(new EquippedSlotContainer(inventory, slot));
            return containers;
        }
    }
}
