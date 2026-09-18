using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Networking;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Navigation;
using RuinRail.UI.Theme;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The merchant trade flow (dungeon/58): the prompt in reach, E opening the trade screen exactly once through the
    /// interactable's Opened seam, the focus list pushed and gameplay input gated, buy/sell through the existing
    /// DungeonMerchantService paths with exact coin/item changes and no duplicates, starter gear protected, the
    /// graphical window (icons, rarity frames, prices, details) and mouse/keyboard/controller navigation, close
    /// restoring gameplay, and scene-transition cleanup.
    /// </summary>
    public class MerchantTradeUiTests
    {
        private readonly List<Object> _created = new();
        private readonly List<System.IDisposable> _disposables = new();
        private GameContentCatalog _catalog;
        private ItemDefinitionRegistry _registry;
        private Dictionary<AmmoType, AmmoItemDefinition> _ammo;
        private PlayerRig _rig;
        private ExpeditionState _state;
        private FakePlayerInputReader _reader;
        private DungeonMerchantService _service;
        private DungeonMerchantInteractable _interactable;
        private MerchantViewModel _vm;
        private MerchantView _view;
        private FocusStack _stack;
        private TimeScalePause _pause;
        private int _opened;

        [SetUp]
        public void SetUp()
        {
            _catalog = GameContentCatalog.Load();
            Assert.IsNotNull(_catalog);
            _registry = _catalog.BuildRegistry();
            _ammo = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            DamageAuthority.LocalIsAuthoritative = true;
            GameplayInputGate.Reset();
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);
            _opened = 0;

            var expedition = new ExpeditionService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance);
            var profile = new PlayerProfile();
            new StarterKitService(Resolve, t => _ammo.TryGetValue(t, out var a) ? a : null, _catalog.AmmoBalance).GrantFirstProfileKit(profile);
            _state = expedition.Start(profile, 11, Biome.OvergrownLabs);
            _reader = new FakePlayerInputReader();
            _rig = new PlayerRig(_catalog, _registry, _catalog.BuildSpecials());
            var player = _rig.Build(_state, null, "local", Vector2.zero, _reader);
            _disposables.Add(_rig);
            _created.Add(player);

            _service = new DungeonMerchantService(_catalog.Merchant, new PriceService(_catalog.Economy), _state.CarriedWallet, new DungeonMerchantState(1), 11, 1, _catalog.Items, Resolve, _catalog.Loot.RarityTableFor);
            var merchantGo = new GameObject("Merchant");
            _created.Add(merchantGo);
            merchantGo.transform.position = new Vector3(0.6f, 0f, 0f);
            var collider = merchantGo.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one;
            _interactable = merchantGo.AddComponent<DungeonMerchantInteractable>();
            _interactable.Bind(_service);

            // The composition seam ExpeditionScene uses: Opened → bind + open, once.
            _vm = new MerchantViewModel();
            _pause = new TimeScalePause();
            _vm.ConfigurePause(_pause, isCoop: false);
            _disposables.Add(_vm);
            _view = MerchantView.Create(_vm);
            _created.Add(_view.gameObject);
            _stack = new FocusStack();
            _vm.Changed += () =>
            {
                if (_vm.IsOpen) { if (!_stack.Contains(_view.FocusList)) _stack.Push(_view.FocusList); }
                else _stack.Remove(_view.FocusList);
            };
            _interactable.Opened += (merchant, _) =>
            {
                _opened++;
                if (_vm.IsOpen) return;
                _vm.Bind(merchant.Merchant, _state.Inventory, () => _state.CarriedCoins, _catalog.BuildSpecials());
                _vm.Open();
            };
            Physics2D.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in _disposables) d.Dispose();
            _disposables.Clear();
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            GameplayInputGate.Reset();
            Time.timeScale = 1f;
        }

        private sealed class HostAuthority : IAuthorityContext
        {
            public NetworkRole Role => NetworkRole.Host;
            public bool IsAuthority => true;
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private PlayerInteractor Interactor => _rig.Player.GetComponent<PlayerInteractor>();

        [Test]
        public void Prompt_ShowsInReach_AndInteractOpensTheTradeScreen_ExactlyOnce()
        {
            Assert.AreSame(_interactable, Interactor.FindNearestInteractable(), "the merchant is the nearest interactable");
            Assert.AreEqual("TRADE WITH MERCHANT", _interactable.PromptFor(_rig.Player));
            Assert.IsFalse(_vm.IsOpen);

            _reader.RaiseInteract();
            Assert.AreEqual(1, _interactable.OpenCount);
            Assert.AreEqual(1, _opened);
            Assert.IsTrue(_vm.IsOpen && _view.IsVisible, "E opens a real trade screen");
            Assert.AreEqual(1, _vm.Opens);
            Assert.AreSame(_service, _vm.Merchant);
            Assert.AreEqual(_service.Offers.Count, _vm.Rows.Count);
            Assert.AreSame(_view.FocusList, _stack.Current, "the merchant focus list is pushed on the focus stack");
            Assert.IsTrue(GameplayInputGate.IsHeld, "gameplay input is held while trading");
            Assert.IsTrue(_pause.IsPaused, "solo: the world pauses like the inventory");

            // A second E while the screen is open is swallowed by the input gate: no second open, no second window.
            _reader.RaiseInteract();
            Assert.AreEqual(1, _vm.Opens);
            Assert.AreEqual(1, Object.FindObjectsByType<MerchantView>(FindObjectsSortMode.None).Length);
        }

        [Test]
        public void Window_ShowsIconsRarityFramesPricesCoinsAndFreeSlots_AndDetailsReuseTheTooltip()
        {
            _reader.RaiseInteract();
            var skin = UiSkin.Load();
            Assert.IsNotNull(skin);
            Assert.IsTrue(skin.HasInventoryFrames, "the same skin frames as the inventory");
            StringAssert.Contains("MERCHANT — DEPTH 1", _view.TitleText);
            StringAssert.Contains($"CARRIED COINS  {_state.CarriedCoins}", _view.CoinsText);
            StringAssert.Contains($"{_vm.FreeSlots} / {PlayerInventory.BackpackCapacity} SLOTS FREE", _view.BackpackText);
            for (var i = 0; i < _vm.Rows.Count; i++)
            {
                var row = _view.RowViews[i];
                Assert.IsTrue(row.gameObject.activeSelf);
                Assert.IsTrue(row.IconVisible, $"row {i} shows the definition icon");
                Assert.AreSame(_vm.Rows[i].Definition.Icon, row.IconSprite);
                Assert.AreSame(skin.RarityFrame(_vm.Rows[i].Rarity), row.FrameSprite, $"row {i} carries its rarity frame");
                Assert.AreEqual($"{_vm.Rows[i].Price} C", row.PriceText);
                StringAssert.Contains(_vm.Rows[i].Name.Substring(0, System.Math.Min(6, _vm.Rows[i].Name.Length)), row.NameText);
            }

            Assert.IsFalse(_view.RowViews[_vm.Rows.Count].gameObject.activeSelf, "unused rows stay hidden");
            var selected = _vm.Selected;
            var tooltip = _vm.TooltipFor(selected);
            Assert.IsNotNull(tooltip);
            StringAssert.Contains(UiText.Fit(tooltip.Name, MerchantView.DetailsPanel.Width - UiTheme.Pad * 2), _view.DetailTitleText);
            StringAssert.Contains(RarityStyle.For(tooltip.Rarity).Label, _view.DetailSubtitleText);
            StringAssert.Contains($"PRICE {selected.Price} C", _view.DetailSubtitleText);
            if (tooltip.BaseStats.Count > 0) StringAssert.Contains(tooltip.BaseStats[0].Label, _view.DetailRowTexts[0]);
            foreach (var text in _view.GetComponentsInChildren<UnityEngine.UI.Text>(true)) Assert.AreSame(UiFont.Font(), text.font, text.name);
        }

        [Test]
        public void Buy_DebitsExactlyThePrice_DeliversOnce_AndRepeatedConfirmsChangeNothing()
        {
            _reader.RaiseInteract();
            var offer = _vm.Rows.OrderBy(r => r.Price).First();
            _state.CarriedWallet.Credit(offer.Price + 5, "test");
            _vm.SetCursor(_vm.Rows.ToList().IndexOf(offer));
            Assert.IsTrue(_vm.CanAct);
            var coins = _state.CarriedCoins;
            int Held() => _state.Inventory.BackpackSlots.Where(i => i != null && i.DefinitionId == offer.Item.DefinitionId).Sum(i => i.Quantity);
            var held = Held();
            var delivered = offer.Item.Quantity; // a stackable is absorbed into the backpack stacks (its own quantity drops to 0)

            Assert.AreEqual(TradeError.None, _vm.Buy());
            Assert.AreEqual(coins - offer.Price, _state.CarriedCoins, "exactly the price");
            Assert.AreEqual(held + delivered, Held(), "exactly the offer's quantity delivered (a stackable merges into an existing stack)");
            Assert.IsTrue(_service.Offers[offer.Index].IsSold);
            Assert.AreEqual(1, _vm.Purchases);
            StringAssert.StartsWith("BOUGHT", _view.MessageText);
            Assert.AreEqual("SOLD", _view.RowViews[_vm.Cursor].SubtitleText);

            // Repeated confirms on the sold offer: refused, nothing changes.
            var after = _state.CarriedCoins;
            Assert.AreEqual(TradeError.AlreadySold, _vm.Buy());
            Assert.AreEqual(TradeError.AlreadySold, _vm.Act());
            Assert.AreEqual(after, _state.CarriedCoins);
            Assert.AreEqual(held + delivered, Held(), "no duplicate");
            Assert.AreEqual(1, _vm.Purchases);
            Assert.IsFalse(_vm.CanAct);
            Assert.AreEqual("SOLD OUT", _vm.BlockReason);
        }

        [Test]
        public void Buy_IsRefused_WithoutCoins_OrWithAFullBackpack_WithVisibleFeedback()
        {
            _reader.RaiseInteract();
            // A non-stackable offer: a full backpack refuses it (a stackable would still merge into its stack).
            var offer = _vm.Rows.Where(r => !r.Definition.IsStackable).OrderBy(r => r.Price).First();
            _vm.SetCursor(_vm.Rows.ToList().IndexOf(offer));
            _state.CarriedWallet.Debit(_state.CarriedCoins, "test");
            Assert.IsFalse(_vm.CanAct);
            Assert.AreEqual("NOT ENOUGH COINS", _vm.BlockReason);
            var backpack = _state.Inventory.BackpackSlots.Count(i => i != null);
            Assert.AreEqual(TradeError.InsufficientFunds, _vm.Buy());
            Assert.AreEqual(0, _state.CarriedCoins);
            Assert.AreEqual(backpack, _state.Inventory.BackpackSlots.Count(i => i != null));
            Assert.AreEqual("NOT ENOUGH COINS", _view.MessageText);

            _state.CarriedWallet.Credit(offer.Price * 2, "test");
            while (_state.Inventory.BackpackSlots.Any(i => i == null)) _state.Inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger"));
            Assert.IsFalse(_vm.CanAct);
            Assert.AreEqual("BACKPACK FULL", _vm.BlockReason);
            var coins = _state.CarriedCoins;
            Assert.AreEqual(TradeError.DestinationRejected, _vm.Buy());
            Assert.AreEqual(coins, _state.CarriedCoins, "refund on a rejected destination: no coins lost");
            Assert.IsFalse(_service.Offers[offer.Index].IsSold);
            Assert.AreEqual("BACKPACK FULL", _view.MessageText);
        }

        [Test]
        public void Sell_CreditsTheQuote_RemovesTheItem_AndStarterGearIsProtected()
        {
            var smg = new ItemInstance("weapon_rattler_9", 1, Rarity.Uncommon);
            Assert.IsTrue(_state.Inventory.TryAddToBackpack(smg));
            _reader.RaiseInteract();
            _vm.SetTab(MerchantTab.Sell);
            Assert.AreEqual(_state.Inventory.BackpackSlots.Count(i => i != null), _vm.Rows.Count, "every backpack item is listed");
            var sale = _vm.Rows.First(r => r.Item.InstanceId == smg.InstanceId);
            var quote = _service.QuoteSellValue(smg);
            Assert.Greater(quote, 0);
            Assert.AreEqual(quote, sale.Price);
            _vm.SetCursor(_vm.Rows.ToList().IndexOf(sale));
            Assert.IsTrue(_vm.CanAct);
            var coins = _state.CarriedCoins;
            Assert.AreEqual(TradeError.None, _vm.Sell());
            Assert.AreEqual(coins + quote, _state.CarriedCoins);
            Assert.IsFalse(_state.Inventory.BackpackSlots.Any(i => i != null && i.InstanceId == smg.InstanceId));
            Assert.AreEqual(1, _vm.Sales);
            StringAssert.StartsWith("SOLD", _view.MessageText);

            // Starter gear (unsellable) in the backpack is listed but cannot be sold.
            var starterAmmo = _state.Inventory.BackpackSlots.FirstOrDefault(i => i != null && i.IsUnsellable);
            if (starterAmmo == null)
            {
                var vest = _state.Inventory.Unequip(EquippedSlot.Armor);
                Assert.IsTrue(vest.IsUnsellable);
                Assert.IsTrue(_state.Inventory.TryAddToBackpack(vest));
                starterAmmo = vest;
            }

            var protectedRow = _vm.Rows.First(r => r.Item.InstanceId == starterAmmo.InstanceId);
            Assert.IsTrue(protectedRow.IsUnsellable);
            StringAssert.Contains("STARTER", _view.RowViews[_vm.Rows.ToList().IndexOf(protectedRow)].SubtitleText);
            _vm.SetCursor(_vm.Rows.ToList().IndexOf(protectedRow));
            Assert.IsFalse(_vm.CanAct);
            coins = _state.CarriedCoins;
            Assert.AreEqual(TradeError.Unsellable, _vm.Sell());
            Assert.AreEqual(coins, _state.CarriedCoins);
            Assert.IsTrue(_state.Inventory.BackpackSlots.Any(i => i != null && i.InstanceId == starterAmmo.InstanceId), "the starter item stays");
        }

        [Test]
        public void KeyboardAndController_NavigateRowsTabsAndActions_MouseSelectsAndCloses()
        {
            _reader.RaiseInteract();
            _state.CarriedWallet.Credit(1000, "test"); // every offer affordable, so BUY is an enabled stop
            var list = _view.FocusList;
            Assert.IsTrue(list.HasNavigator);
            Assert.AreEqual("merchant.row.0", list.Focused.Id, "opens on the first offer");
            Assert.IsTrue(_stack.Navigate(Vector2Int.down));
            Assert.AreEqual("merchant.row.1", list.Focused.Id);
            Assert.AreEqual(1, _vm.Cursor, "focus moves the cursor (details follow)");
            Assert.IsTrue(_view.RowViews[1].Control.ShowsFocusBrackets);
            for (var i = 0; i < 12; i++) if (!list.Focused.Id.StartsWith("merchant.row.") || !_stack.Navigate(Vector2Int.down)) break;
            Assert.AreEqual(MerchantView.ActionFocusId, list.Focused.Id, "below the last row sits BUY");
            Assert.IsTrue(_stack.Navigate(Vector2Int.right));
            Assert.AreEqual(MerchantView.CloseFocusId, list.Focused.Id);
            Assert.IsTrue(_stack.Navigate(Vector2Int.up));
            Assert.AreEqual(MerchantView.ActionFocusId, list.Focused.Id, "up from CLOSE steps back to BUY");
            Assert.IsTrue(_stack.Navigate(Vector2Int.up));
            StringAssert.StartsWith("merchant.row.", list.Focused.Id, "up from BUY returns to the rows");
            for (var i = 0; i < 12; i++) if (!list.Focused.Id.StartsWith("merchant.row.") || !_stack.Navigate(Vector2Int.up)) break;
            Assert.AreEqual(MerchantView.BuyTabId, list.Focused.Id, "above the first row sits the BUY tab");
            Assert.IsTrue(_stack.Navigate(Vector2Int.right));
            Assert.AreEqual(MerchantView.SellTabId, list.Focused.Id);
            Assert.IsTrue(_stack.Activate());
            Assert.AreEqual(MerchantTab.Sell, _vm.Tab, "confirm on the SELL tab switches the list");
            Assert.IsTrue(_view.Buttons[MerchantView.SellTabId].IsActiveSection);

            ActiveInputDevice.Set(InputDeviceKind.Gamepad);
            _vm.SetTab(MerchantTab.Buy);
            StringAssert.Contains("D-PAD", _view.HintsText, "controller hints");
            ActiveInputDevice.Set(InputDeviceKind.KeyboardMouse);

            // Mouse: clicking a row selects it; clicking CLOSE closes.
            _view.RowViews[0].Control.SimulateClick();
            Assert.AreEqual(0, _vm.Cursor);
            Assert.AreEqual(1, _view.RowViews[0].Control.PointerActivations);
            _view.Buttons[MerchantView.CloseFocusId].SimulateClick();
            Assert.IsFalse(_vm.IsOpen);
        }

        [Test]
        public void Close_RestoresGameplay_AndReopenKeepsTheSoldState()
        {
            _reader.RaiseInteract();
            var offer = _vm.Rows.OrderBy(r => r.Price).First();
            _state.CarriedWallet.Credit(offer.Price, "test");
            _vm.SetCursor(_vm.Rows.ToList().IndexOf(offer));
            Assert.AreEqual(TradeError.None, _vm.Buy());
            _vm.Close();
            Assert.IsFalse(_vm.IsOpen || _view.IsVisible);
            Assert.IsFalse(GameplayInputGate.IsHeld, "gameplay input released");
            Assert.IsFalse(_pause.IsPaused, "world resumed");
            Assert.IsFalse(_stack.Contains(_view.FocusList), "focus list popped");
            Assert.AreEqual(Vector2.zero, _reader.Move);

            _reader.RaiseInteract();
            Assert.AreEqual(2, _interactable.OpenCount);
            Assert.IsTrue(_vm.IsOpen);
            Assert.IsTrue(_vm.Rows[offer.Index].IsSold, "sold offers stay sold on reopen (no refresh path)");
            Assert.AreEqual(MerchantTab.Buy, _vm.Tab);
            Assert.AreEqual("merchant.row.0", _view.FocusList.Focused.Id, "a fresh open starts on the first row again");
        }

        [Test]
        public void SceneTransition_DisposesTheScreen_ReleasingInputAndFocus()
        {
            _reader.RaiseInteract();
            Assert.IsTrue(GameplayInputGate.IsHeld);
            _vm.Dispose();
            Assert.IsFalse(_vm.IsOpen);
            Assert.IsFalse(GameplayInputGate.IsHeld, "disposing an open screen releases the gate");
            Assert.IsFalse(_stack.Contains(_view.FocusList));
            Assert.IsFalse(_pause.IsPaused);
            _reader.RaiseInteract();
            Assert.IsFalse(_vm.IsOpen, "an unbound (disposed) screen ignores further opens");
        }

        [Test]
        public void CoopAuthority_RequestMerchantBuy_IsIdempotentPerTransaction()
        {
            var authority = new LootAuthorityService(new HostAuthority());
            authority.RegisterParticipant(new LootParticipant(7, "guest", new BackpackContainer(_state.Inventory), _state.CarriedWallet));
            var offer = _service.Offers.OrderBy(o => o.Price).First();
            _state.CarriedWallet.Credit(offer.Price * 3, "test");
            var coins = _state.CarriedCoins;
            var first = authority.RequestMerchantBuy("tx-1", 7, _service, offer.Index);
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreEqual(offer.Price, first.Coins);
            var replay = authority.RequestMerchantBuy("tx-1", 7, _service, offer.Index);
            Assert.AreEqual(LootVerdict.Accepted, replay.Verdict, "the replayed transaction returns the cached result");
            Assert.AreEqual(coins - offer.Price, _state.CarriedCoins, "charged once");
            var again = authority.RequestMerchantBuy("tx-2", 7, _service, offer.Index);
            Assert.AreEqual(LootVerdict.AlreadyTaken, again.Verdict);
            Assert.AreEqual(coins - offer.Price, _state.CarriedCoins);
        }
    }
}
