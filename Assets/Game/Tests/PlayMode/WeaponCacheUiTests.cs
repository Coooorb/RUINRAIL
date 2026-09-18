using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.UI.Inventory;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using RuinRail.UI.WeaponCache;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The Weapon Cache interaction, end to end over the real event and the real world handle.
    ///
    /// The bug this covers: the prompt read <c>[E] CHOOSE WEAPON CACHE</c> and pressing Interact did nothing at all.
    /// The event answered activation with "a choice is required" and there was no screen — nothing in the runtime
    /// ever subscribed to that answer, so the press was silently swallowed. These tests hold the whole path: prompt
    /// in range, one press → one screen, input gated while it is up, one reward, no duplicate, clean restore.
    /// </summary>
    public sealed class WeaponCacheUiTests
    {
        private readonly List<Object> _created = new();
        private PlayerInventory _inventory;
        private CoinWallet _wallet;
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private DungeonEventConfig _config;
        private LootSourceCatalog _loot;

        [SetUp]
        public void SetUp()
        {
            GameplayInputGate.Reset();
            CursorService.SetApplier(_ => true);
            _catalog = UnityEditor.AssetDatabase.FindAssets("t:ItemDefinition")
                .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                .Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            _ammoBalance = UnityEditor.AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _config = UnityEditor.AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset");
            _loot = UnityEditor.AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset");
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private AmmoItemDefinition ResolveAmmo(AmmoType type) => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            GameplayInputGate.Reset();
            CursorService.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
        }

        /// <summary>A player object with the pieces the event actor is built from (wallet, backpack, health).</summary>
        private GameObject Player()
        {
            var go = new GameObject("CachePlayer");
            _created.Add(go);
            _inventory = new PlayerInventory(Resolve, ResolveAmmo, _ammoBalance);
            _wallet = new CoinWallet(CoinDomain.Carried);
            go.AddComponent<HealthComponent>().SetMaxHealth(100);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(_inventory);
            receiver.SetWallet(_wallet);
            return go;
        }

        private (DungeonEventInteractable handle, WeaponCacheEvent cache) Cache(int seed = 7, int depth = 2)
        {
            var context = new DungeonEventContext(seed, depth, 3, 1, Biome.RuinedMetro);
            var cache = new WeaponCacheEvent(context, _config, _catalog, _loot.RarityTableFor);
            var go = new GameObject("WeaponCache");
            _created.Add(go);
            var handle = go.AddComponent<DungeonEventInteractable>();
            handle.Bind(cache);
            return (handle, cache);
        }

        private (WeaponCacheViewModel vm, WeaponCacheView view) Screen(IWorldPause pause = null)
        {
            var vm = new WeaponCacheViewModel();
            if (pause != null) vm.ConfigurePause(pause, isCoop: false);
            var view = WeaponCacheView.Create(vm);
            _created.Add(view.gameObject);
            return (vm, view);
        }

        // ---- The seam that was missing ----

        [Test]
        public void TheEventAnswersInteractWithAChoiceRequest_AndTheWorldHandleForwardsIt()
        {
            var player = Player();
            var (handle, cache) = Cache();
            Assert.AreEqual(3, cache.Choices.Count, "57.6: three weapons are presented");
            Assert.IsTrue(handle.CanInteract(player), "the cache is usable while it is available");
            Assert.AreEqual("CHOOSE WEAPON CACHE", ((IInteractionPrompt)handle).PromptFor(player));

            EventActor forwarded = null;
            var requests = 0;
            handle.ChoiceRequested += (_, actor) => { requests++; forwarded = actor; };
            Assert.IsTrue(handle.Interact(player), "the press succeeds: it opened a choice");
            Assert.AreEqual(1, requests, "exactly one request per press");
            Assert.AreEqual(1, handle.ChoiceRequests);
            Assert.IsNotNull(forwarded, "the acting player is handed to the screen");
            Assert.IsNotNull(forwarded.Backpack, "with the backpack the reward would go into");
            Assert.AreEqual(DungeonEventOutcome.Unavailable, handle.LastResult.Outcome);
            Assert.AreEqual(DungeonEventDetails.ChoiceRequired, handle.LastResult.Detail);
            Assert.AreEqual(DungeonEventPhase.Available, cache.Phase, "activation changed nothing; the pick is still to come");
        }

        [Test]
        public void NoSubscriber_LeavesTheEventUntouched_SoTheOldBehaviourCannotSilentlyReturn()
        {
            var player = Player();
            var (handle, cache) = Cache();
            handle.Interact(player);
            Assert.AreEqual(DungeonEventPhase.Available, cache.Phase);
            Assert.IsFalse(cache.IsConsumed);
            Assert.AreEqual(-1, cache.ChosenIndex, "nothing is granted without a choice");
        }

        // ---- The screen ----

        [UnityTest]
        public IEnumerator Interact_OpensTheSelectionExactlyOnce_AndPresentsTheRolledWeapons()
        {
            var player = Player();
            var (handle, cache) = Cache();
            var pause = new TimeScalePause();
            var (vm, view) = Screen(pause);
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };

            Assert.IsFalse(view.IsVisible);
            handle.Interact(player);
            yield return null;
            Assert.AreEqual(1, vm.Opens);
            Assert.IsTrue(vm.IsOpen && view.IsVisible, "one press opens the screen");
            Assert.AreEqual(cache.Choices.Count, vm.Rows.Count);
            Assert.AreSame(cache, vm.Cache);

            // Exactly one instance: a second press while it is up changes nothing.
            handle.Interact(player);
            yield return null;
            Assert.AreEqual(1, vm.Opens, "the open screen swallows a repeat press");
            Assert.AreEqual(1, Object.FindObjectsByType<WeaponCacheView>(FindObjectsSortMode.None).Length, "one window object, never a second");

            // Every row shows real weapon data.
            for (var i = 0; i < vm.Rows.Count; i++)
            {
                var row = vm.Rows[i];
                Assert.IsNotNull(row.Item, "a rolled instance");
                Assert.IsNotNull(row.Definition, "its definition");
                Assert.IsInstanceOf<WeaponDefinition>(row.Definition, "a Weapon Cache presents weapons");
                Assert.IsNotEmpty(row.Name);
                Assert.AreNotEqual(row.Item.DefinitionId, row.Name, "a display name, not an id");
                var rowView = view.RowViews[i];
                Assert.IsTrue(rowView.IconVisible && rowView.IconSprite != null, "the weapon icon is drawn");
                Assert.IsNotNull(rowView.FrameSprite, "with its rarity frame");
                Assert.IsNotEmpty(rowView.NameText);
            }

            var ids = vm.Rows.Select(r => r.Item.DefinitionId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "three distinct weapons");
            Assert.IsNotNull(vm.TooltipFor(vm.Selected), "the details reuse the inventory tooltip");
            pause.Resume();
        }

        [UnityTest]
        public IEnumerator Choosing_GrantsExactlyOneWeapon_ConsumesTheCache_AndCannotBeRepeated()
        {
            var player = Player();
            var (handle, cache) = Cache();
            var (vm, view) = Screen(new TimeScalePause());
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };
            handle.Interact(player);
            yield return null;

            var chosen = vm.Rows[1];
            vm.SetCursor(1);
            Assert.IsTrue(vm.CanTake);
            var before = _inventory.BackpackSlots.Count(s => s != null);
            Assert.AreEqual(DungeonEventOutcome.Success, vm.Take());
            yield return null;

            Assert.AreEqual(before + 1, _inventory.BackpackSlots.Count(s => s != null), "exactly one item arrived");
            Assert.AreEqual(1, _inventory.BackpackSlots.Count(s => s != null && s.InstanceId == chosen.Item.InstanceId));
            Assert.AreEqual(1, vm.Takes);
            Assert.AreEqual(1, cache.ChosenIndex);
            Assert.IsTrue(cache.IsConsumed, "one choice consumes the cache for the whole party (57.6)");
            Assert.IsFalse(vm.IsOpen, "the screen closes itself once the reward is taken");
            Assert.IsFalse(view.IsVisible);

            // No second reward, by any route.
            Assert.AreEqual(DungeonEventOutcome.None, cache.Choose(handle.LastActor, 0).Outcome);
            Assert.AreEqual(before + 1, _inventory.BackpackSlots.Count(s => s != null));
            Assert.IsFalse(handle.CanInteract(player), "a consumed cache is no longer a target");
            Assert.AreEqual(string.Empty, ((IInteractionPrompt)handle).PromptFor(player), "and shows no prompt");
            var opens = vm.Opens;
            handle.Interact(player);
            yield return null;
            Assert.AreEqual(opens, vm.Opens, "it cannot be reopened");
            Assert.AreEqual(before + 1, _inventory.BackpackSlots.Count(s => s != null));
        }

        [UnityTest]
        public IEnumerator AFullBackpack_RefusesTheChoice_WithoutConsumingTheCache()
        {
            var player = Player();
            var (handle, cache) = Cache();
            var (vm, _) = Screen(new TimeScalePause());
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };
            for (var i = 0; i < PlayerInventory.BackpackCapacity; i++)
                Assert.IsTrue(_inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger")), "filling the backpack");
            handle.Interact(player);
            yield return null;

            Assert.IsFalse(vm.CanTake);
            Assert.AreEqual("BACKPACK FULL", vm.BlockReason);
            var outcome = vm.Take();
            Assert.AreNotEqual(DungeonEventOutcome.Success, outcome);
            Assert.IsFalse(cache.IsConsumed, "a refused choice leaves the cache for later");
            Assert.AreEqual(-1, cache.ChosenIndex);
            Assert.IsTrue(vm.IsOpen, "the screen stays up so the player can make room");
            Assert.IsTrue(vm.MessageIsError);
        }

        // ---- Input, focus and cursor coherence ----

        [UnityTest]
        public IEnumerator WhileTheScreenIsOpen_GameplayInputIsGated_AndClosingRestoresEverything()
        {
            var player = Player();
            var (handle, _) = Cache();
            var pause = new TimeScalePause();
            var (vm, view) = Screen(pause);
            var menuInput = new GameObject("MenuInput").AddComponent<MenuInput>();
            _created.Add(menuInput.gameObject);
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };
            vm.Changed += () =>
            {
                if (vm.IsOpen) { if (!menuInput.Stack.Contains(view.FocusList)) menuInput.Stack.Push(view.FocusList); }
                else menuInput.Stack.Remove(view.FocusList);
            };

            CursorService.SetBase(CursorKind.Aim);
            Assert.IsFalse(GameplayInputGate.IsHeld);
            handle.Interact(player);
            CursorService.PushOverlay();
            yield return null;
            Assert.IsTrue(GameplayInputGate.IsHeld, "no shot, dash or interact leaks through while choosing");
            Assert.AreEqual(CursorKind.Pointer, CursorService.Current);
            Assert.AreSame(view.FocusList, menuInput.Stack.Current, "the window owns keyboard/controller navigation");
            Assert.AreEqual(WeaponCacheView.RowFocusPrefix + "0", view.FocusList.Focused.Id, "a fresh open starts on the first choice");
            Assert.AreEqual(0f, Time.timeScale, "solo pauses the world under the window (92)");

            vm.Close();
            CursorService.PopOverlay();
            yield return null;
            Assert.IsFalse(GameplayInputGate.IsHeld, "closing releases gameplay input");
            Assert.AreEqual(CursorKind.Aim, CursorService.Current, "and returns the aim cursor");
            Assert.IsFalse(menuInput.Stack.Contains(view.FocusList));
            Assert.AreEqual(1f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator KeyboardControllerAndMouse_AllReachEveryChoiceAndBothActions()
        {
            var player = Player();
            var (handle, cache) = Cache();
            var (vm, view) = Screen(new TimeScalePause());
            var menuInput = new GameObject("MenuInput").AddComponent<MenuInput>();
            _created.Add(menuInput.gameObject);
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };
            handle.Interact(player);
            menuInput.Stack.Push(view.FocusList);
            yield return null;

            // Keyboard / controller: down through the choices, then into the actions.
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            Assert.AreEqual(1, vm.Cursor, "the highlighted weapon follows the focus");
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            Assert.AreEqual(2, vm.Cursor);
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.down));
            Assert.AreEqual(WeaponCacheView.TakeFocusId, view.FocusList.Focused.Id);
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.right));
            Assert.AreEqual(WeaponCacheView.CloseFocusId, view.FocusList.Focused.Id);
            Assert.IsTrue(menuInput.Stack.Navigate(Vector2Int.up));
            StringAssert.StartsWith(WeaponCacheView.RowFocusPrefix, view.FocusList.Focused.Id, "back up into the list");

            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            vm.SetCursor(0);
            yield return null;
            StringAssert.Contains("A:", view.HintsText, "controller hints");
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            vm.SetCursor(1);
            yield return null;
            StringAssert.Contains("ENTER:", view.HintsText);

            // Mouse: clicking a row selects it, clicking LEAVE closes.
            view.RowViews[2].Control.SimulateClick();
            yield return null;
            Assert.AreEqual(2, vm.Cursor, "a click selects the weapon it points at");
            Assert.IsFalse(cache.IsConsumed, "selecting is not taking");
            view.Buttons[WeaponCacheView.CloseFocusId].SimulateClick();
            yield return null;
            Assert.IsFalse(vm.IsOpen);
            Assert.IsFalse(cache.IsConsumed, "leaving takes nothing");
            Assert.IsTrue(handle.CanInteract(player), "and the cache is still there to come back to");
        }

        [UnityTest]
        public IEnumerator ASceneTransition_DisposesTheScreen_AndItIgnoresLateRequests()
        {
            var player = Player();
            var (handle, _) = Cache();
            var (vm, view) = Screen(new TimeScalePause());
            handle.ChoiceRequested += (h, actor) => { vm.Bind((WeaponCacheEvent)h.Event, actor, _inventory); vm.Open(); };
            handle.Interact(player);
            yield return null;
            Assert.IsTrue(vm.IsOpen);

            vm.Dispose();
            yield return null;
            Assert.IsFalse(vm.IsOpen, "disposal closes the window");
            Assert.IsFalse(view.IsVisible);
            Assert.IsFalse(GameplayInputGate.IsHeld, "and releases gameplay input");
            Assert.IsTrue(vm.IsDisposed);

            var opens = vm.Opens;
            handle.Interact(player);
            yield return null;
            Assert.AreEqual(opens, vm.Opens, "a screen torn down with its scene ignores a late request");
        }
    }
}
