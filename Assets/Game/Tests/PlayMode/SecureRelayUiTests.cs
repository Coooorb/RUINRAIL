using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.SecureRelay;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The Secure Relay window (57.7) over the real event, slot views and skin: pixel-exact 640x360 captures of the
    /// choose / refused / storage-full / secured states (TestResults/PolishPreview), and keyboard/controller reach.
    /// </summary>
    public sealed class SecureRelayUiTests
    {
        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private PlayerInventory _inventory;
        private GameObject _player;

        [SetUp]
        public void SetUp()
        {
            GameplayInputGate.Reset();
            CursorService.SetApplier(_ => true);
            var catalog = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = UnityEditor.AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            GameplayInputGate.Reset();
            CursorService.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            Time.timeScale = 1f;
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private AmmoItemDefinition ResolveAmmo(AmmoType type) => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);

        /// <summary>A mid-run loadout: starter pistol/knife/vest, a found Epic rifle, an accessory, bandages and ammo.</summary>
        private (SecureRelayViewModel vm, SecureRelayView view, SecureRelayEvent relay, Storage storage, ItemInstance rifle) Open(bool fullStorage = false)
        {
            _player = new GameObject("RelayPlayer");
            _created.Add(_player);
            _inventory = new PlayerInventory(Resolve, ResolveAmmo, _ammoBalance) { MarksIncomingAtRisk = true };
            foreach (var (item, slot) in StarterKitService.CreateKit())
            {
                if (slot.HasValue) _inventory.TryEquip(item, slot.Value);
                else _inventory.TryAddToBackpack(item);
            }

            var rifleDef = _registry.Definitions.OfType<WeaponDefinition>().First(w => w.Id != StarterKitService.PistolId && w.Id != StarterKitService.KnifeId && w.Icon != null);
            var rifle = new ItemInstance(rifleDef.Id, 1, Rarity.Epic);
            _inventory.TryAddToBackpack(rifle);
            var accessory = _registry.Definitions.FirstOrDefault(d => d.Category == ItemCategory.Accessory && d.Icon != null);
            if (accessory != null) _inventory.TryEquip(new ItemInstance(accessory.Id, 1, Rarity.Rare), EquippedSlot.Accessory);
            _inventory.TryAddToBackpack(new ItemInstance(StarterKitService.BandageId, 2));

            _player.AddComponent<HealthComponent>().SetMaxHealth(100);
            var receiver = _player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(_inventory);
            receiver.SetWallet(new CoinWallet(CoinDomain.Carried));

            var storage = new Storage(Resolve, _ammoBalance);
            storage.TryAdd(new ItemInstance(StarterKitService.VestId));
            if (fullStorage) while (storage.FreeSlots > 0) storage.TryAdd(new ItemInstance(StarterKitService.VestId));

            var relay = new SecureRelayEvent(new DungeonEventContext(11, 3, 2), Resolve);
            var vm = new SecureRelayViewModel();
            vm.ConfigurePause(new TimeScalePause(), isCoop: false);
            var view = SecureRelayView.Create(vm);
            _created.Add(view.gameObject);
            var actor = DungeonEventInteractable.ActorFor(_player);
            vm.Bind(relay, actor, _inventory, receiver.CarriedContainers, storage, Resolve);
            vm.Commit = id => relay.Secure(actor.ParticipantId, receiver.CarriedContainers, id, new SecureRelayStorageTarget(storage));
            vm.Open();
            return (vm, view, relay, storage, rifle);
        }

        [UnityTest]
        public IEnumerator Window_ShowsCarriedSlots_Refusals_Confirm_AndSecuredCard()
        {
            var (vm, view, relay, storage, rifle) = Open();
            yield return null;
            var rifleCell = vm.Cells.ToList().FindIndex(c => c.Item == rifle);
            vm.SetCursor(rifleCell);
            yield return null;
            var choose = UiScreenCapture.Capture("ui_secure_relay_choose");
            Assert.Less(choose.DominantColourShare(), 0.9f, "the window renders");
            Assert.AreEqual("CAN BE SECURED", view.StatusText);

            var starterCell = vm.Cells.ToList().FindIndex(c => !c.IsEmpty && c.Refusal == SecureRelayRefusal.StarterItem);
            vm.SetCursor(starterCell);
            yield return null;
            Assert.AreEqual("STARTER GEAR CANNOT BE SECURED", view.StatusText);
            Assert.IsTrue(view.CellShaded(starterCell));
            UiScreenCapture.Capture("ui_secure_relay_starter_refused");

            vm.SetCursor(rifleCell);
            Assert.IsTrue(vm.Secure());
            yield return null;
            Assert.AreEqual("CONFIRM", view.SecureButtonText);
            UiScreenCapture.Capture("ui_secure_relay_confirm");
            Assert.IsTrue(vm.Secure());
            yield return null;
            Assert.IsTrue(view.ShowsSecuredCard);
            Assert.IsNotNull(storage.Find(rifle.InstanceId));
            UiScreenCapture.Capture("ui_secure_relay_secured");
            Assert.IsTrue(relay.HasSecured(DungeonEventInteractable.ActorFor(_player).ParticipantId));
            vm.Close();
        }

        [UnityTest]
        public IEnumerator FullStorage_MarksEverySlotRefused_AndNothingMoves()
        {
            var (vm, view, relay, storage, rifle) = Open(fullStorage: true);
            yield return null;
            var rifleCell = vm.Cells.ToList().FindIndex(c => c.Item == rifle);
            vm.SetCursor(rifleCell);
            yield return null;
            Assert.AreEqual("SHELTER STORAGE FULL", view.StatusText);
            Assert.IsFalse(vm.CanSecure);
            Assert.IsFalse(vm.Secure());
            Assert.IsTrue(_inventory.BackpackSlots.Contains(rifle));
            Assert.IsNull(storage.Find(rifle.InstanceId));
            Assert.AreEqual(0, relay.SecuredCount);
            StringAssert.Contains("60 / 60", view.StorageText);
            UiScreenCapture.Capture("ui_secure_relay_storage_full");
            vm.Close();
        }

        [UnityTest]
        public IEnumerator KeyboardAndController_ReachEverySlot_BothActions_AndBackCloses()
        {
            var (vm, view, _, _, _) = Open();
            var menuInput = new GameObject("MenuInput").AddComponent<MenuInput>();
            _created.Add(menuInput.gameObject);
            menuInput.Stack.Push(view.FocusList);
            vm.SetCursor(0);
            yield return null;
            Assert.IsTrue(GameplayInputGate.IsHeld);
            Assert.AreEqual(0f, Time.timeScale, "solo pauses the world under the terminal");

            var visited = new HashSet<string> { view.FocusList.Focused.Id };
            // Worn row left → right, then down through both backpack rows, then into the actions.
            for (var i = 0; i < 4; i++) { Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.right)); visited.Add(view.FocusList.Focused.Id); }
            Assert.AreEqual(SecureRelayView.CellFocusPrefix + "4", view.FocusList.Focused.Id);
            Assert.AreEqual(4, vm.Cursor, "the selection follows the focus");
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            for (var row = 0; row < 2; row++)
            {
                // First backpack row right → left, second left → right (a snake through the 4×2 grid).
                var step = row == 0 ? Vector2Int.left : Vector2Int.right;
                for (var i = 0; i < SecureRelayView.BackpackColumns; i++)
                {
                    visited.Add(view.FocusList.Focused.Id);
                    if (i < SecureRelayView.BackpackColumns - 1) Assert.IsTrue(menuInput.Stack.Navigate(step));
                }

                if (row == 0) Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            }

            Assert.AreEqual(SecureRelayViewModel.CellCount, visited.Count, "every carried slot is reachable");
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            Assert.AreEqual(SecureRelayView.CloseFocusId, view.FocusList.Focused.Id, "SECURE is skipped while the slot cannot be secured");
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.up));
            StringAssert.StartsWith(SecureRelayView.CellFocusPrefix, view.FocusList.Focused.Id, "back up into the grid");

            var eligible = vm.Cells.ToList().FindIndex(c => c.IsEligible);
            vm.SetCursor(eligible);
            yield return null;
            Assert.IsTrue(view.FocusList.Find(SecureRelayView.SecureFocusId).IsEnabled);
            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            vm.SetCursor(eligible == 0 ? 1 : 0);
            yield return null;
            vm.SetCursor(eligible);
            yield return null;
            StringAssert.Contains("A:", HintOf(view), "controller hints");

            view.Buttons[SecureRelayView.CloseFocusId].SimulateClick();
            yield return null;
            Assert.IsFalse(vm.IsOpen);
            Assert.IsFalse(GameplayInputGate.IsHeld, "closing releases gameplay input");
            Assert.AreEqual(1f, Time.timeScale);
        }

        private static string HintOf(SecureRelayView view) =>
            view.GetComponentsInChildren<UnityEngine.UI.Text>(true).First(t => t.name == "Hints").text;
    }
}
