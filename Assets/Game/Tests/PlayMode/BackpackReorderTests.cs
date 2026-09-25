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

namespace RuinRail.Tests
{
    /// <summary>
    /// Backpack slot reorder over the real window and view model. Root cause of the "items cannot be moved between
    /// backpack slots" report: <c>InventoryViewModel.MoveTo</c> treated backpack → backpack as a no-op that reported
    /// Done, so a click-move or a drop onto another backpack slot changed nothing. It is now a real slot operation
    /// (<see cref="ItemSlotContainer.TryMove"/>): empty target = move, occupied = swap, same-definition stack = merge
    /// with the remainder left in place; the order stays exactly as put — no compaction, no re-sort — and survives
    /// close/reopen and snapshot round trips.
    /// </summary>
    public sealed class BackpackReorderTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private ItemDefinitionRegistry _registry;
        private LegendarySpecialRegistry _specials;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        private sealed class CountingPause : IWorldPause
        {
            public void Pause() { }
            public void Resume() { }
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

        private (InventoryViewModel vm, InventoryView view, PlayerInventory inventory) Build()
        {
            var inventory = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            var go = new GameObject("Player");
            _created.Add(go);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(new CoinWallet(CoinDomain.Carried, 100));
            receiver.SetDropService(new ItemDropService(_spawner));
            var vm = new InventoryViewModel();
            vm.Bind(inventory, receiver, () => 100, _specials);
            vm.ConfigurePause(new CountingPause(), isCoop: false);
            var view = InventoryView.Create(vm);
            _created.Add(view.gameObject);
            return (vm, view, inventory);
        }

        /// <summary>Slots 0..3 filled (rifle, rig, pouch, medkit ×3), 4..7 empty; the starter kit equipped.</summary>
        private static (ItemInstance rifle, ItemInstance rig, ItemInstance pouch, ItemInstance medkits) Fill(PlayerInventory inventory)
        {
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("weapon_p9_ranger"), EquippedSlot.PrimaryWeapon));
            var rifle = new ItemInstance("weapon_rattler_9", 1, Rarity.Rare);
            var rig = new ItemInstance("armor_scout_rig", 1, Rarity.Uncommon);
            var pouch = new ItemInstance("accessory_ammo_pouch", 1, Rarity.Epic);
            Assert.IsTrue(inventory.TryAddToBackpack(rifle));
            Assert.IsTrue(inventory.TryAddToBackpack(rig));
            Assert.IsTrue(inventory.TryAddToBackpack(pouch));
            // A stackable add is absorbed into the container's own stack instance: read it back from slot 3.
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_medkit", 3)));
            var medkits = inventory.BackpackSlots[3];
            Assert.AreEqual("consumable_medkit", medkits.DefinitionId);
            return (rifle, rig, pouch, medkits);
        }

        private static InventorySlotRef Bag(int i) => new(InventorySlotKind.Backpack, i);

        private static string[] Order(PlayerInventory inventory) => inventory.BackpackSlots.Select(s => s?.InstanceId).ToArray();

        private static void AssertNoDuplicateOrLoss(PlayerInventory inventory, IEnumerable<ItemInstance> expected)
        {
            var ids = inventory.BackpackSlots.Where(s => s != null).Select(s => s.InstanceId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "no instance appears twice");
            foreach (var item in expected) Assert.IsTrue(inventory.Contains(item.InstanceId), item.DefinitionId + " still owned");
        }

        // ---- left click: select → move / swap / cancel ----

        [Test]
        public void Click_Slot0_ThenEmptySlot5_MovesTheItemToExactlySlot5()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            vm.Open();
            view.BackpackSlots[0].SimulateClick();
            Assert.IsTrue(vm.Selected.HasValue && vm.Selected.Value.Equals(Bag(0)), "click on an occupied slot picks it up");
            view.BackpackSlots[5].SimulateClick();
            Assert.IsFalse(vm.Selected.HasValue, "the move consumed the selection");
            Assert.AreEqual(SlotMoveResult.Moved, vm.LastReorder);
            Assert.IsNull(inventory.BackpackSlots[0]);
            Assert.AreSame(rifle, inventory.BackpackSlots[5], "exactly slot 5 — not the first free slot");
            CollectionAssert.AreEqual(new[] { null, rig.InstanceId, pouch.InstanceId, medkits.InstanceId, null, rifle.InstanceId, null, null }, Order(inventory));
            Assert.IsTrue(view.BackpackSlots[5].IsOccupied && !view.BackpackSlots[0].IsOccupied, "the window shows the new position");
            AssertNoDuplicateOrLoss(inventory, new[] { rifle, rig, pouch, medkits });
        }

        [Test]
        public void Click_Slot5_ThenOccupiedSlot2_SwapsTheTwo()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            Assert.AreEqual(SlotMoveResult.Moved, inventory.MoveBackpackSlot(0, 5));
            vm.Open();
            view.BackpackSlots[5].SimulateClick();
            view.BackpackSlots[2].SimulateClick();
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Assert.AreSame(rifle, inventory.BackpackSlots[2]);
            Assert.AreSame(pouch, inventory.BackpackSlots[5]);
            CollectionAssert.AreEqual(new[] { null, rig.InstanceId, rifle.InstanceId, medkits.InstanceId, null, pouch.InstanceId, null, null }, Order(inventory));
            AssertNoDuplicateOrLoss(inventory, new[] { rifle, rig, pouch, medkits });
        }

        [Test]
        public void Click_TheSourceAgain_CancelsThePickUp_WithoutMovingAnything()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            var before = Order(inventory);
            vm.Open();
            view.BackpackSlots[1].SimulateClick();
            Assert.IsTrue(vm.Selected.HasValue);
            view.BackpackSlots[1].SimulateClick();
            Assert.IsFalse(vm.Selected.HasValue, "clicking the original source cancels");
            CollectionAssert.AreEqual(before, Order(inventory));
        }

        // ---- drag and drop ----

        [Test]
        public void Drag_Slot0_OntoEmptySlot7_Moves_AndOntoOccupiedSlot1_Swaps_AndAnInvalidDropDoesNothing()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            vm.Open();
            view.BackpackSlots[7].SimulateDrop(view.BackpackSlots[0]);
            Assert.AreEqual(SlotMoveResult.Moved, vm.LastReorder);
            Assert.AreSame(rifle, inventory.BackpackSlots[7]);
            Assert.IsNull(inventory.BackpackSlots[0]);

            view.BackpackSlots[1].SimulateDrop(view.BackpackSlots[7]);
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Assert.AreSame(rifle, inventory.BackpackSlots[1]);
            Assert.AreSame(rig, inventory.BackpackSlots[7]);

            // Dropping a slot onto itself, or dragging from an empty slot, changes nothing.
            var before = Order(inventory);
            view.BackpackSlots[1].SimulateDrop(view.BackpackSlots[1]);
            view.BackpackSlots[3].SimulateDrop(view.BackpackSlots[0]);
            CollectionAssert.AreEqual(before, Order(inventory));
            Assert.IsFalse(vm.Selected.HasValue);
            AssertNoDuplicateOrLoss(inventory, new[] { rifle, rig, pouch, medkits });
        }

        // ---- keyboard / controller: select → move on the same view-model path ----

        [Test]
        public void KeyboardController_SelectThenMove_ThroughTheFocusStack_UsesTheSameOperation()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            vm.Open();
            var stack = new FocusStack();
            stack.Push(view.FocusList);
            Assert.IsTrue(view.FocusList.Focus("backpack.3"));
            Assert.IsTrue(stack.Current.ActivateFocused(), "confirm picks up the medkits");
            Assert.IsTrue(vm.Selected.HasValue && vm.Selected.Value.Equals(Bag(3)));
            Assert.IsTrue(view.FocusList.Focus("backpack.6"));
            Assert.IsTrue(stack.Current.ActivateFocused(), "confirm on an empty slot moves there");
            Assert.AreEqual(SlotMoveResult.Moved, vm.LastReorder);
            Assert.AreSame(medkits, inventory.BackpackSlots[6]);
            Assert.IsNull(inventory.BackpackSlots[3]);

            Assert.IsTrue(view.FocusList.Focus("backpack.6"));
            Assert.IsTrue(stack.Current.ActivateFocused());
            Assert.IsTrue(view.FocusList.Focus("backpack.0"));
            Assert.IsTrue(stack.Current.ActivateFocused(), "confirm on an occupied slot swaps");
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Assert.AreSame(medkits, inventory.BackpackSlots[0]);
            Assert.AreSame(rifle, inventory.BackpackSlots[6]);
            AssertNoDuplicateOrLoss(inventory, new[] { rifle, rig, pouch, medkits });
        }

        // ---- the order is the player's: no compaction, survives close/reopen, other moves, snapshots ----

        [Test]
        public void ManualOrder_SurvivesCloseReopen_AnotherMove_ARefresh_AndASnapshotRoundTrip()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            vm.Open();
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(0), Bag(7)));
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(1), Bag(4)));
            var expected = new[] { null, null, pouch.InstanceId, medkits.InstanceId, rig.InstanceId, null, null, rifle.InstanceId };
            CollectionAssert.AreEqual(expected, Order(inventory), "gaps stay gaps: nothing compacts");

            vm.Close();
            vm.Open();
            CollectionAssert.AreEqual(expected, Order(inventory), "close/reopen keeps the order");
            Assert.IsTrue(view.BackpackSlots[7].IsOccupied && view.BackpackSlots[4].IsOccupied && !view.BackpackSlots[0].IsOccupied);

            // An unrelated move (unequip into the backpack) takes the first free slot and disturbs nothing else.
            Assert.AreEqual(InventoryActionResult.Done, vm.Unequip(EquippedSlot.PrimaryWeapon));
            var pistol = inventory.BackpackSlots[0];
            Assert.IsNotNull(pistol);
            Assert.AreEqual("weapon_p9_ranger", pistol.DefinitionId);
            CollectionAssert.AreEqual(new[] { pistol.InstanceId, null, pouch.InstanceId, medkits.InstanceId, rig.InstanceId, null, null, rifle.InstanceId }, Order(inventory));

            // Snapshot → restore (save/load, the run's at-risk hand-over) keeps every slot index.
            var restored = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            restored.RestoreFromSnapshot(inventory.ToSnapshot());
            CollectionAssert.AreEqual(Order(inventory), Order(restored));
            AssertNoDuplicateOrLoss(inventory, new[] { rifle, rig, pouch, medkits, pistol });
        }

        // ---- stacks: merge with remainder; never corrupt ----

        [Test]
        public void MovingAStack_OntoASameDefinitionStack_MergesUpToTheLimit_AndLeavesTheRemainder()
        {
            var (vm, view, inventory) = Build();
            Fill(inventory);
            var ammo = Resolve("ammo_light");
            var max = inventory.MaxStackFor(ammo);
            Assert.Greater(max, 60);
            // Two light-ammo stacks in known slots: 4 → max - 20, 5 → 50 (built directly so the slot indices are exact).
            var a = new ItemInstance("ammo_light", max - 20);
            var b = new ItemInstance("ammo_light", 50);
            Assert.AreEqual(SlotMoveResult.Invalid, inventory.MoveBackpackSlot(4, 5), "nothing in slot 4 yet");
            Assert.IsTrue(inventory.TryAddToBackpack(a));
            var slotA = inventory.BackpackSlots.ToList().FindIndex(s => s != null && s.DefinitionId == "ammo_light");
            Assert.AreEqual(4, slotA);
            // b merges into a first (that is the add rule); force a second stack by filling a to the limit then adding b.
            inventory.BackpackSlots[4].SetQuantity(max);
            Assert.IsTrue(inventory.TryAddToBackpack(b));
            Assert.AreEqual(max, inventory.BackpackSlots[4].Quantity);
            Assert.AreEqual(50, inventory.BackpackSlots[5].Quantity);
            inventory.BackpackSlots[4].SetQuantity(max - 20);
            var totalBefore = inventory.Get(AmmoType.Light);

            vm.Open();
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(5), Bag(4)));
            Assert.AreEqual(SlotMoveResult.Merged, vm.LastReorder);
            Assert.AreEqual(max, inventory.BackpackSlots[4].Quantity, "the target filled to its limit");
            Assert.AreEqual(30, inventory.BackpackSlots[5].Quantity, "the remainder stays in the source slot");
            Assert.AreEqual(totalBefore, inventory.Get(AmmoType.Light), "not a single round created or lost");

            // A full target cannot absorb anything: the two stacks swap instead, still without loss.
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(5), Bag(4)));
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Assert.AreEqual(30, inventory.BackpackSlots[4].Quantity);
            Assert.AreEqual(max, inventory.BackpackSlots[5].Quantity);
            Assert.AreEqual(totalBefore, inventory.Get(AmmoType.Light));

            // A different stackable definition swaps rather than merges.
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(3), Bag(4)));
            Assert.AreEqual(SlotMoveResult.Swapped, vm.LastReorder);
            Assert.AreEqual("consumable_medkit", inventory.BackpackSlots[4].DefinitionId);
            Assert.AreEqual("ammo_light", inventory.BackpackSlots[3].DefinitionId);
            Assert.AreEqual(totalBefore, inventory.Get(AmmoType.Light));
        }

        [Test]
        public void MovingAnEmptySlot_IsRefusedWithoutChange_AndMovingOntoItself_IsANoOp()
        {
            var (vm, view, inventory) = Build();
            var (rifle, rig, pouch, medkits) = Fill(inventory);
            vm.Open();
            var before = Order(inventory);
            Assert.AreEqual(InventoryActionResult.NothingSelected, vm.MoveTo(Bag(6), Bag(0)));
            Assert.AreEqual(InventoryActionResult.Done, vm.MoveTo(Bag(0), Bag(0)));
            CollectionAssert.AreEqual(before, Order(inventory));
            Assert.AreEqual(SlotMoveResult.Invalid, inventory.MoveBackpackSlot(0, 99));
            Assert.AreEqual(SlotMoveResult.Invalid, inventory.MoveBackpackSlot(-1, 2));
            CollectionAssert.AreEqual(before, Order(inventory));
        }
    }
}
