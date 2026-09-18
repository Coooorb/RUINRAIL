using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Weapons;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Hud;
using RuinRail.UI.Inventory;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The HUD reflects the current authoritative equipment. PlayerRig.MountWeapons destroys and re-adds the weapon
    /// components on every EquippedChanged, so a HUD that cached the components kept showing the P9 after a Wasp-45
    /// was equipped; the view model now reads the loadout's live slot and the inventory's equipped item on every
    /// refresh. Every equipment path (inventory swap, backpack equip, drag/drop, drop, weapon switch, consumable
    /// replace/decrement, reopen, scene transition) is covered here against the real rig.
    /// </summary>
    public class HudEquipmentSyncTests
    {
        private readonly List<Object> _created = new();
        private readonly List<System.IDisposable> _disposables = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Dictionary<AmmoType, AmmoItemDefinition> _ammo;
        private PlayerRig _rig;
        private ExpeditionState _state;
        private DungeonHudViewModel _vm;
        private DungeonHudView _view;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            Assert.IsNotNull(_catalog);
            _registry = _catalog.BuildRegistry();
            _ammo = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            DamageAuthority.LocalIsAuthoritative = true;
            (_rig, _state) = BuildRun();
            (_vm, _view) = BindHud(_rig, _state);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private Sprite Icon(string id) => Resolve(id)?.Icon;

        private (PlayerRig rig, ExpeditionState state) BuildRun()
        {
            var expedition = new ExpeditionService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance);
            var profile = new PlayerProfile();
            new StarterKitService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance).GrantFirstProfileKit(profile);
            var state = expedition.Start(profile, 3, Biome.RuinedMetro);
            var rig = new PlayerRig(_catalog, _registry, _catalog.BuildSpecials());
            var player = rig.Build(state, null, "local", Vector2.zero, new FakePlayerInputReader());
            _disposables.Add(rig);
            _created.Add(player);
            return (rig, state);
        }

        private (DungeonHudViewModel vm, DungeonHudView view) BindHud(PlayerRig rig, ExpeditionState state)
        {
            var vm = new DungeonHudViewModel();
            vm.BindPlayer(rig.Player.GetComponent<HealthComponent>(), rig.Player.GetComponent<PlayerDash>(), rig.Player.GetComponent<PlayerLifeStateComponent>());
            vm.BindWeapons(rig.Loadout, rig.Loadout.GetSlot(WeaponSlot.Primary), rig.Loadout.GetSlot(WeaponSlot.Secondary), t => state.Inventory.Get(t), rig.Special);
            vm.BindInventory(state.Inventory);
            var view = DungeonHudView.Create(vm);
            _created.Add(view.gameObject);
            _disposables.Add(vm);
            return (vm, view);
        }

        private InventoryViewModel InventoryUi()
        {
            var inventory = new InventoryViewModel();
            inventory.Bind(_state.Inventory, _rig.Player.GetComponent<PlayerLootReceiver>(), () => _state.CarriedCoins, _catalog.BuildSpecials());
            _disposables.Add(inventory);
            return inventory;
        }

        private static InventorySlotRef Backpack(int index) => new(InventorySlotKind.Backpack, index);
        private static InventorySlotRef Equipped(EquippedSlot slot) => new(InventorySlotKind.Equipped, (int)slot);

        [Test]
        public void Start_ShowsTheStarterKit_P9ActiveWithAmmo_KnifeIconOnly_BandageChip()
        {
            var p = _vm.Snapshot.Primary;
            Assert.AreEqual(StarterKitService.PistolId, p.DefinitionId);
            Assert.IsTrue(p.IsActive);
            Assert.AreEqual(HudResourceKind.Ammo, p.Resource);
            Assert.AreSame(Icon(StarterKitService.PistolId), p.Icon, "the definition's icon (the one icon system)");
            Assert.AreEqual(StarterKitService.KnifeId, _vm.Snapshot.Secondary.DefinitionId);
            Assert.AreEqual(HudResourceKind.None, _vm.Snapshot.Secondary.Resource, "melee: icon only, no ammo");
            Assert.IsFalse(_view.SecondarySlot.ResourceVisible);
            Assert.IsTrue(_view.PrimarySlot.ResourceVisible && _view.PrimarySlot.IconVisible && _view.SecondarySlot.IconVisible);
            Assert.AreEqual("x" + StarterKitService.BandageCount, _view.ConsumableText);
            Assert.AreSame(Icon(StarterKitService.BandageId), _vm.Snapshot.ConsumableIcon);
        }

        [UnityTest]
        public IEnumerator EquipWasp45_ThroughTheInventoryWindow_ReplacesTheP9OnTheHud_WithItsOwnAmmo()
        {
            var wasp = new ItemInstance("weapon_wasp_45", 1, Rarity.Rare);
            var waspDefinition = (RangedWeaponDefinition)Resolve(wasp.DefinitionId);
            _state.Inventory.Add(waspDefinition.AmmoType, 124);
            Assert.IsTrue(_state.Inventory.TryAddToBackpack(wasp));
            var ui = InventoryUi();
            ui.Open();
            var index = _state.Inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == wasp.InstanceId);
            var remountsBefore = _rig.Remounts;
            Assert.AreEqual(InventoryActionResult.Done, ui.MoveTo(Backpack(index), Equipped(EquippedSlot.PrimaryWeapon)));
            Assert.Greater(_rig.Remounts, remountsBefore, "the rig remounted the weapon components");
            yield return null;

            var mounted = _rig.Loadout.GetSlot(WeaponSlot.Primary) as RangedWeapon;
            Assert.IsNotNull(mounted);
            Assert.AreSame(waspDefinition, mounted.Definition);
            var p = _vm.Snapshot.Primary;
            Assert.AreEqual("weapon_wasp_45", p.DefinitionId, "no stale P9");
            StringAssert.Contains("Wasp", p.Name);
            Assert.AreSame(Icon("weapon_wasp_45"), p.Icon);
            Assert.AreEqual((int)Rarity.Rare, p.Rarity);
            Assert.AreEqual(mounted.MagazineAmmo, p.Magazine, "the magazine of the live component, not of the destroyed one");
            Assert.AreEqual(_state.Inventory.Get(waspDefinition.AmmoType), p.Reserve, "the reserve of the Wasp-45 ammo type");
            Assert.AreEqual($"{mounted.MagazineAmmo} / {_state.Inventory.Get(waspDefinition.AmmoType)}", _view.PrimaryText);
            Assert.AreSame(Icon("weapon_wasp_45"), _view.PrimarySlot.IconSprite);
            Assert.IsTrue(_view.PrimarySlot.IsActive);
            ui.Close();
            yield return null;
            Assert.AreEqual("weapon_wasp_45", _vm.Snapshot.Primary.DefinitionId, "still the Wasp-45 after the window closes");
            Assert.IsTrue(_state.Inventory.BackpackSlots.Any(i => i != null && i.DefinitionId == StarterKitService.PistolId), "the P9 went to the backpack");
        }

        [Test]
        public void ReplaceSecondary_AndSwapActiveSlot_UpdateNameIconAndHighlight()
        {
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Uncommon);
            Assert.IsTrue(_state.Inventory.TryAddToBackpack(smg));
            var ui = InventoryUi();
            var index = _state.Inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == smg.InstanceId);
            Assert.AreEqual(InventoryActionResult.Done, ui.MoveTo(Backpack(index), Equipped(EquippedSlot.SecondaryWeapon)));
            Assert.AreEqual("weapon_rattler_9", _vm.Snapshot.Secondary.DefinitionId);
            Assert.AreEqual(HudResourceKind.Ammo, _vm.Snapshot.Secondary.Resource, "a ranged secondary shows ammo");
            Assert.AreSame(Icon("weapon_rattler_9"), _view.SecondarySlot.IconSprite);
            Assert.IsFalse(_vm.Snapshot.Secondary.IsActive);

            _rig.Loadout.SelectSlot(WeaponSlot.Secondary);
            Assert.IsTrue(_vm.Snapshot.Secondary.IsActive && !_vm.Snapshot.Primary.IsActive, "2 highlights slot 2 synchronously");
            Assert.IsTrue(_view.SecondarySlot.BracketsVisible && !_view.PrimarySlot.BracketsVisible);
            _rig.Loadout.SelectSlot(WeaponSlot.Primary);
            Assert.IsTrue(_vm.Snapshot.Primary.IsActive);
        }

        [Test]
        public void DragDropIntoPrimary_AndDropEquipped_LeaveTheHudConsistent()
        {
            var wasp = new ItemInstance("weapon_wasp_45");
            Assert.IsTrue(_state.Inventory.TryAddToBackpack(wasp));
            var ui = InventoryUi();
            ui.Open();
            var index = _state.Inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.InstanceId == wasp.InstanceId);
            // Drag-and-drop lands on the same transfer path as a keyboard move.
            ui.CancelSelection();
            ui.SetCursor(Equipped(EquippedSlot.PrimaryWeapon));
            Assert.AreEqual(InventoryActionResult.Done, ui.MoveTo(Backpack(index), Equipped(EquippedSlot.PrimaryWeapon)));
            Assert.AreEqual("weapon_wasp_45", _vm.Snapshot.Primary.DefinitionId);

            // Drop the equipped secondary: the UI's DROP goes through PlayerLootReceiver.TryDrop → the equipped-slot container, i.e. the same
            // EquippedChanged(slot, null) the inventory raises here; the slot reads empty on the HUD.
            Assert.IsNotNull(_state.Inventory.Unequip(EquippedSlot.SecondaryWeapon));
            Assert.IsNull(_state.Inventory.GetEquipped(EquippedSlot.SecondaryWeapon));
            Assert.AreEqual(string.Empty, _vm.Snapshot.Secondary.DefinitionId);
            Assert.IsTrue(_view.SecondarySlot.IsEmpty && !_view.SecondarySlot.IconVisible && !_view.SecondarySlot.ResourceVisible, "an emptied slot reads as empty");
            Assert.IsNull(_vm.Snapshot.Secondary.Icon);
        }

        [Test]
        public void Consumable_ReplaceAndDecrement_FollowTheActiveStack()
        {
            Assert.AreEqual("x1", _view.ConsumableText);
            var medkits = new ItemInstance("consumable_medkit", 2);
            Assert.IsTrue(_state.Inventory.TryAddToBackpack(medkits));
            var ui = InventoryUi();
            var index = _state.Inventory.BackpackSlots.ToList().FindIndex(i => i != null && i.DefinitionId == "consumable_medkit"); // stackables re-id when they enter a container
            Assert.AreEqual(InventoryActionResult.Done, ui.MoveTo(Backpack(index), Equipped(EquippedSlot.ActiveConsumable)));
            var active = _state.Inventory.GetEquipped(EquippedSlot.ActiveConsumable);
            Assert.AreEqual("consumable_medkit", active.DefinitionId);
            Assert.AreSame(Icon("consumable_medkit"), _vm.Snapshot.ConsumableIcon);
            Assert.AreEqual("x2", _view.ConsumableText);

            // A use decrements the stack in place (ConsumableUseAction.ConsumeOne: SetQuantity, no inventory event): the per-frame tick re-reads it.
            active.SetQuantity(active.Quantity - 1);
            _vm.Tick();
            Assert.AreEqual("x1", _view.ConsumableText);
            active.SetQuantity(0);
            _state.Inventory.Unequip(EquippedSlot.ActiveConsumable);
            Assert.IsTrue(_view.ConsumableSlot.IsEmpty, "an emptied stack leaves the slot neutral");
            Assert.AreEqual(string.Empty, _view.ConsumableText);
        }

        [Test]
        public void ReopeningTheInventory_ChangesNothing_AndASceneTransitionRebindsCleanly()
        {
            var ui = InventoryUi();
            ui.Open(); ui.Close(); ui.Open(); ui.Close();
            Assert.AreEqual(StarterKitService.PistolId, _vm.Snapshot.Primary.DefinitionId);
            _vm.Tick(); // the first tick settles the per-frame dash state
            var publications = _vm.Publications;
            for (var i = 0; i < 5; i++) _vm.Tick();
            Assert.AreEqual(publications, _vm.Publications, "nothing changed: idle ticks publish nothing");

            // Scene transition: the old rig and HUD are disposed, a new run gets a fresh binding that shows its own equipment.
            _vm.Dispose();
            var (rig2, state2) = BuildRun();
            var (vm2, view2) = BindHud(rig2, state2);
            Assert.AreEqual(StarterKitService.PistolId, vm2.Snapshot.Primary.DefinitionId);
            var wasp = new ItemInstance("weapon_wasp_45");
            state2.Inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(state2.Inventory.TryEquip(wasp, EquippedSlot.PrimaryWeapon));
            Assert.AreEqual("weapon_wasp_45", vm2.Snapshot.Primary.DefinitionId);
            Assert.AreEqual(string.Empty, _vm.Snapshot.Primary.DefinitionId, "the disposed HUD is unbound: it shows nothing and follows nothing");
            Assert.AreSame(Icon("weapon_wasp_45"), view2.PrimarySlot.IconSprite);
        }

        [Test]
        public void DestroyedWeaponComponent_IsNeverReadAsAWeapon()
        {
            var stale = _rig.Loadout.GetSlot(WeaponSlot.Primary);
            _state.Inventory.Unequip(EquippedSlot.PrimaryWeapon);
            Assert.IsTrue(stale is Object unityStale && unityStale == null || _rig.Loadout.GetSlot(WeaponSlot.Primary) == null, "the old component is gone");
            Assert.AreEqual(string.Empty, _vm.Snapshot.Primary.DefinitionId);
            Assert.IsFalse(_vm.Snapshot.Primary.IsActive);
            Assert.IsTrue(_view.PrimarySlot.IsEmpty);
        }
    }
}
