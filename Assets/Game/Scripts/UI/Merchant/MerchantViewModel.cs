using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.UI.Inventory;
using UnityEngine;

namespace RuinRail.UI.Merchant
{
    public enum MerchantTab
    {
        Buy,
        Sell
    }

    /// <summary>One row of the merchant list: an offer (BUY) or a backpack item (SELL), as the view shows it.</summary>
    public sealed class MerchantRow
    {
        public MerchantTab Tab;
        /// <summary>Offer index (BUY) or backpack slot index (SELL).</summary>
        public int Index;
        public ItemInstance Item;
        public ItemDefinition Definition;
        public int Price;
        public bool IsSold;
        public bool IsUnsellable;
        public string Name => Definition != null && !string.IsNullOrEmpty(Definition.DisplayName) ? Definition.DisplayName : Item?.DefinitionId ?? string.Empty;
        public Sprite Icon => Definition != null ? Definition.Icon : null;
        public int Rarity => Item != null ? (int)Item.Rarity : 0;
        public string Id => $"merchant.{(Tab == MerchantTab.Buy ? "offer" : "item")}.{Index}";
    }

    /// <summary>
    /// The in-expedition merchant trade screen (dungeon/58) over the existing <see cref="DungeonMerchantService"/>:
    /// the depth's deterministic offers to buy for Carried Coins, the player's dungeon-held backpack items to sell,
    /// and the item details/comparison the inventory tooltip already builds. Every trade goes through the service's
    /// Buy/Sell (debit → transfer → refund on failure; sold offers stay sold), the view model never edits a container
    /// or a wallet itself. While open it holds gameplay input, exactly like the inventory.
    /// </summary>
    public sealed class MerchantViewModel : IDisposable
    {
        private DungeonMerchantService _merchant;
        private PlayerInventory _inventory;
        private BackpackContainer _backpack;
        private Func<int> _coins;
        private LegendarySpecialRegistry _specials;
        private IWorldPause _pause;
        private bool _isCoop;
        private readonly List<MerchantRow> _rows = new();
        private int _cursor;
        private bool _disposed;
        private bool OverGameplay => _pause != null;

        public bool IsOpen { get; private set; }
        public int Opens { get; private set; }
        public MerchantTab Tab { get; private set; } = MerchantTab.Buy;
        public string Message { get; private set; } = string.Empty;
        public bool MessageIsError { get; private set; }
        public int Purchases { get; private set; }
        public int Sales { get; private set; }
        public int Coins => _coins?.Invoke() ?? 0;
        public int Depth => _merchant?.Depth ?? 0;
        public int FreeSlots => _inventory != null ? _inventory.BackpackSlots.Count(s => s == null) : 0;
        public int BackpackCapacity => PlayerInventory.BackpackCapacity;
        public IReadOnlyList<MerchantRow> Rows => _rows;
        public int Cursor => _cursor;
        public MerchantRow Selected => _rows.Count == 0 ? null : _rows[Mathf.Clamp(_cursor, 0, _rows.Count - 1)];
        public DungeonMerchantService Merchant => _merchant;
        public bool IsBound => _merchant != null && _inventory != null;
        public bool IsDisposed => _disposed;

        public event Action Changed;

        public void Bind(DungeonMerchantService merchant, PlayerInventory inventory, Func<int> coins = null, LegendarySpecialRegistry specials = null)
        {
            if (_disposed) return; // a screen torn down with its scene never comes back through a late Opened event
            Unsubscribe();
            _merchant = merchant;
            _inventory = inventory;
            _backpack = inventory != null ? new BackpackContainer(inventory) : null;
            _coins = coins;
            _specials = specials;
            if (_merchant != null) { _merchant.Bought += OnTraded; _merchant.Sold += OnSold; }
            if (_inventory != null) { _inventory.BackpackChanged += OnBackpackChanged; _inventory.EquippedChanged += OnEquippedChanged; }
            RebuildRows();
            Raise();
        }

        /// <summary>Solo pauses through the world pause; co-op never pauses (92 applies to every gameplay overlay).</summary>
        public void ConfigurePause(IWorldPause pause, bool isCoop)
        {
            _pause = pause;
            _isCoop = isCoop;
        }

        // ---- Open / close ----

        public void Open()
        {
            if (IsOpen || !IsBound) return;
            IsOpen = true;
            Opens++;
            Tab = MerchantTab.Buy;
            _cursor = 0;
            SetMessage(string.Empty, false);
            RebuildRows();
            if (!_isCoop) _pause?.Pause();
            if (OverGameplay)
            {
                // The trade window owns the screen: no shot, dash, interact or weapon change leaks through a click or a menu key.
                RuinRail.Core.Input.GameplayInputGate.Hold();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            }

            Raise();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            SetMessage(string.Empty, false);
            if (!_isCoop) _pause?.Resume();
            if (OverGameplay)
            {
                RuinRail.Core.Input.GameplayInputGate.Release();
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Cancel);
            }

            Raise();
        }

        // ---- Navigation ----

        public void SetTab(MerchantTab tab)
        {
            if (Tab == tab) return;
            Tab = tab;
            _cursor = 0;
            SetMessage(string.Empty, false);
            RebuildRows();
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        public void SetCursor(int index)
        {
            var clamped = _rows.Count == 0 ? 0 : Mathf.Clamp(index, 0, _rows.Count - 1);
            if (clamped == _cursor) return;
            _cursor = clamped;
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Navigate);
            Raise();
        }

        public void MoveCursor(int delta) => SetCursor(_cursor + delta);

        // ---- Reading ----

        public ItemTooltip TooltipFor(MerchantRow row) =>
            row?.Item == null ? null : ItemTooltip.Build(row.Item, row.Definition ?? _inventory?.Resolve(row.Item.DefinitionId), _specials);

        /// <summary>93: an offer compares against the equipped item of the slot it would go to; a sale row compares the same way.</summary>
        public IReadOnlyList<ComparisonLine> CompareFor(MerchantRow row)
        {
            if (row?.Item == null || _inventory == null) return Array.Empty<ComparisonLine>();
            var definition = row.Definition ?? _inventory.Resolve(row.Item.DefinitionId);
            if (definition == null || definition.Category == ItemCategory.Consumable || definition.Category == ItemCategory.Ammo) return Array.Empty<ComparisonLine>();
            EquippedSlot? target = definition.Category switch
            {
                ItemCategory.Weapon => _inventory.GetEquipped(EquippedSlot.PrimaryWeapon) != null ? EquippedSlot.PrimaryWeapon : _inventory.GetEquipped(EquippedSlot.SecondaryWeapon) != null ? EquippedSlot.SecondaryWeapon : null,
                ItemCategory.Armor => EquippedSlot.Armor,
                ItemCategory.Accessory => EquippedSlot.Accessory,
                _ => null
            };
            if (target == null) return Array.Empty<ComparisonLine>();
            var current = _inventory.GetEquipped(target.Value);
            if (current == null) return Array.Empty<ComparisonLine>();
            return TooltipComparison.Compare(TooltipFor(row), ItemTooltip.Build(current, _inventory.Resolve(current.DefinitionId), _specials));
        }

        public string ActionLabel => Tab == MerchantTab.Buy ? "BUY" : "SELL";

        public bool CanAct
        {
            get
            {
                var row = Selected;
                if (row == null || !IsBound) return false;
                if (row.Tab == MerchantTab.Buy) return !row.IsSold && row.Price <= Coins && _backpack != null && _backpack.CanAccept(row.Item);
                return !row.IsUnsellable && row.Price > 0;
            }
        }

        /// <summary>Why the selected row cannot be traded, for the details panel; empty when it can.</summary>
        public string BlockReason
        {
            get
            {
                var row = Selected;
                if (row == null || !IsBound) return string.Empty;
                if (row.Tab == MerchantTab.Buy)
                {
                    if (row.IsSold) return "SOLD OUT";
                    if (row.Price > Coins) return "NOT ENOUGH COINS";
                    if (_backpack == null || !_backpack.CanAccept(row.Item)) return "BACKPACK FULL";
                    return string.Empty;
                }

                if (row.IsUnsellable) return "STARTER GEAR — CANNOT BE SOLD";
                if (row.Price <= 0) return "NO VALUE";
                return string.Empty;
            }
        }

        // ---- Trading (exactly once per confirm; the service is the authority) ----

        /// <summary>Buys or sells the selected row on the current tab.</summary>
        public TradeError Act() => Tab == MerchantTab.Buy ? Buy() : Sell();

        public TradeError Buy()
        {
            var row = Selected;
            if (row == null || row.Tab != MerchantTab.Buy || !IsBound) { Fail("NOTHING SELECTED"); return TradeError.NoSuchOffer; }
            if (BuyRequest != null)
            {
                // Co-op client (82): the host commits the trade; the screen only sends the request and shows the answer.
                var sent = BuyRequest(row.Index);
                if (sent == TradeError.None) SetMessage($"BUYING {row.Name.ToUpperInvariant()}…", false);
                else Fail("REQUEST NOT SENT");
                RebuildRows();
                Raise();
                return sent;
            }

            var error = _merchant.Buy(row.Index, _backpack);
            switch (error)
            {
                case TradeError.None:
                    Purchases++;
                    SetMessage($"BOUGHT {row.Name.ToUpperInvariant()} FOR {row.Price} COINS", false);
                    RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
                    break;
                case TradeError.AlreadySold: Fail("SOLD OUT"); break;
                case TradeError.InsufficientFunds: Fail("NOT ENOUGH COINS"); break;
                case TradeError.DestinationRejected: Fail("BACKPACK FULL"); break;
                default: Fail("NO SUCH OFFER"); break;
            }

            RebuildRows();
            Raise();
            return error;
        }

        public TradeError Sell()
        {
            var row = Selected;
            if (row == null || row.Tab != MerchantTab.Sell || !IsBound || row.Item == null) { Fail("NOTHING SELECTED"); return TradeError.SourceMissingItem; }
            var name = row.Name;
            var value = row.Price;
            if (SellRequest != null)
            {
                var sent = SellRequest(row.Item.InstanceId);
                if (sent == TradeError.None) SetMessage($"SELLING {name.ToUpperInvariant()}…", false);
                else Fail("REQUEST NOT SENT");
                RebuildRows();
                Raise();
                return sent;
            }

            var error = _merchant.Sell(_backpack, row.Item.InstanceId);
            switch (error)
            {
                case TradeError.None:
                    Sales++;
                    SetMessage($"SOLD {name.ToUpperInvariant()} FOR {value} COINS", false);
                    RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
                    break;
                case TradeError.Unsellable: Fail("STARTER GEAR — CANNOT BE SOLD"); break;
                case TradeError.NoValue: Fail("NO VALUE"); break;
                default: Fail("ITEM NO LONGER IN BACKPACK"); break;
            }

            RebuildRows();
            Raise();
            return error;
        }

        /// <summary>
        /// Co-op client: set by the run so Buy/Sell become requests to the host (the host's merchant, the member's own
        /// wallet and backpack decide). Null in solo and on the host, where the screen trades directly as before.
        /// </summary>
        public Func<int, TradeError> BuyRequest { get; set; }
        public Func<string, TradeError> SellRequest { get; set; }

        /// <summary>The host's answer to a request this screen sent: the same messages a local trade shows.</summary>
        public void ReportRemoteResult(bool isBuy, TradeError error, string itemName, int coins)
        {
            var name = (itemName ?? string.Empty).ToUpperInvariant();
            if (error == TradeError.None)
            {
                if (isBuy) Purchases++; else Sales++;
                SetMessage(isBuy ? $"BOUGHT {name} FOR {coins} COINS" : $"SOLD {name} FOR {coins} COINS", false);
                RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Confirm);
            }
            else
            {
                Fail(error switch
                {
                    TradeError.AlreadySold => "SOLD OUT",
                    TradeError.InsufficientFunds => "NOT ENOUGH COINS",
                    TradeError.DestinationRejected => "BACKPACK FULL",
                    TradeError.Unsellable => "STARTER GEAR — CANNOT BE SOLD",
                    TradeError.NoValue => "NO VALUE",
                    TradeError.SourceMissingItem => "ITEM NO LONGER IN BACKPACK",
                    _ => "TRADE REFUSED"
                });
            }

            Refresh();
        }

        /// <summary>Re-reads stock, coins and backpack (a replicated change arrived while the screen is up).</summary>
        public void Refresh()
        {
            RebuildRows();
            Raise();
        }

        // ---- Rows ----

        private void RebuildRows()
        {
            _rows.Clear();
            if (!IsBound) return;
            if (Tab == MerchantTab.Buy)
            {
                foreach (var offer in _merchant.Offers)
                    _rows.Add(new MerchantRow { Tab = MerchantTab.Buy, Index = offer.Index, Item = offer.Item, Definition = offer.Definition, Price = offer.Price, IsSold = offer.IsSold });
            }
            else
            {
                for (var i = 0; i < _inventory.BackpackSlots.Count; i++)
                {
                    var item = _inventory.BackpackSlots[i];
                    if (item == null) continue;
                    _rows.Add(new MerchantRow { Tab = MerchantTab.Sell, Index = i, Item = item, Definition = _inventory.Resolve(item.DefinitionId), Price = _merchant.QuoteSellValue(item), IsUnsellable = item.IsUnsellable });
                }
            }

            _cursor = _rows.Count == 0 ? 0 : Mathf.Clamp(_cursor, 0, _rows.Count - 1);
        }

        private void SetMessage(string text, bool error)
        {
            Message = text ?? string.Empty;
            MessageIsError = error;
        }

        private void Fail(string text)
        {
            SetMessage(text, true);
            RuinRail.Core.Rendering.UiSoundBus.Raise(RuinRail.Core.Rendering.UiSound.Failure);
        }

        private void OnTraded(TraderOffer _) { RebuildRows(); Raise(); }
        private void OnSold(ItemInstance _, int __) { RebuildRows(); Raise(); }
        private void OnBackpackChanged() { RebuildRows(); Raise(); }
        private void OnEquippedChanged(EquippedSlot _, ItemInstance __) => Raise();

        private void Raise() => Changed?.Invoke();

        private void Unsubscribe()
        {
            if (_merchant != null) { _merchant.Bought -= OnTraded; _merchant.Sold -= OnSold; }
            if (_inventory != null) { _inventory.BackpackChanged -= OnBackpackChanged; _inventory.EquippedChanged -= OnEquippedChanged; }
        }

        public void Dispose()
        {
            if (IsOpen) Close();
            Unsubscribe();
            _disposed = true;
            _merchant = null;
            _inventory = null;
            _backpack = null;
        }
    }
}
