using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The player's receiving side for world loot: exposes the backpack as a transfer-service container and holds the
    /// expedition's Carried Coins (base/77_ECONOMY). Co-op even split happens before coins reach this component.
    /// </summary>
    public sealed class PlayerLootReceiver : MonoBehaviour, IItemReceiver, ICoinReceiver, IPickupQuantityHook
    {
        private RuinRail.Gameplay.Stats.PlayerCombatEvents _combatEvents;

        /// <summary>The collector's passive hub (Scavenger's Reserve); null = stacks are taken as they lie.</summary>
        public void SetCombatEvents(RuinRail.Gameplay.Stats.PlayerCombatEvents events) => _combatEvents = events;

        /// <summary>An ammo stack is taken with the wearer's ammo-pickup bonus; every other item as it lies.</summary>
        public int PickupQuantityFor(ItemInstance item)
        {
            if (item == null || _combatEvents == null || _inventory == null || _inventory.Resolve(item.DefinitionId) is not AmmoItemDefinition) return item?.Quantity ?? 0;
            return _combatEvents.RaiseAmmoPickupRolling(item.Quantity).FinalAmount;
        }

        private PlayerInventory _inventory;
        private BackpackContainer _backpack;
        private readonly ItemTransferService _transferService = new();
        private readonly List<IItemContainer> _carried = new();
        private readonly ActionGateLookup _actionGate = new();
        private ItemDropService _drops;

        public IItemContainer Backpack => _backpack;
        public ItemTransferService TransferService => _transferService;
        public PlayerInventory Inventory => _inventory;

        /// <summary>Every carried container a drop may come from: backpack first, then the equipped slots.</summary>
        public IReadOnlyList<IItemContainer> CarriedContainers => _carried;
        public ItemDropService DropService => _drops;

        public event Action<DropResult> Dropped;

        private CoinWallet _wallet = new(CoinDomain.Carried);
        private ICoinDistributor _coinDistributor;

        /// <summary>The Carried Coins wallet coins are credited into (bind the expedition's wallet so pickups land there).</summary>
        public CoinWallet Wallet => _wallet;
        public int CarriedCoins => _wallet.Balance;

        public event Action<int> CarriedCoinsChanged;

        /// <summary>Last coin pickup outcome (solo: one share, the full amount).</summary>
        public CoinDistributionResult LastCoinDistribution { get; private set; }

        public void SetWallet(CoinWallet wallet)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (wallet.Domain != CoinDomain.Carried) throw new ArgumentException("Loot is credited to Carried Coins only.", nameof(wallet));
            _wallet = wallet;
        }

        /// <summary>
        /// Binds the party distribution (58: coins are split evenly across current participants). Null restores the
        /// solo path: the full amount to this player's Carried wallet.
        /// </summary>
        public void SetCoinDistributor(ICoinDistributor distributor) => _coinDistributor = distributor;

        public void SetInventory(PlayerInventory inventory)
        {
            _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            _backpack = new BackpackContainer(inventory);
            _carried.Clear();
            _carried.Add(_backpack);
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                _carried.Add(new EquippedSlotContainer(inventory, slot));
            }
        }

        /// <summary>Binds the drop path (ground pickup factory); without it TryDrop reports InvalidRequest.</summary>
        public void SetDropService(ItemDropService drops) => _drops = drops;

        /// <summary>32: drops the whole carried instance (backpack or equipped) to the ground at the player's position.</summary>
        public DropResult TryDrop(string instanceId) => TryDrop(instanceId, FindCarried(instanceId)?.Quantity ?? 0);

        /// <summary>Drops a quantity of a carried stack; ownership moves atomically into a shared ground pickup.</summary>
        public DropResult TryDrop(string instanceId, int quantity)
        {
            // 84: Downed/Dead players drop nothing (carried gear never reaches teammates through the ground).
            if (_drops == null || _inventory == null || !_actionGate.CanAct(this))
            {
                return new DropResult(TransferResult.Fail(TransferError.InvalidRequest, instanceId), null);
            }

            var result = _drops.DropFromAny(_carried, instanceId, quantity, transform.position);
            if (result.Success) Dropped?.Invoke(result);
            return result;
        }

        private ItemInstance FindCarried(string instanceId)
        {
            foreach (var container in _carried)
            {
                var item = container.Find(instanceId);
                if (item != null) return item;
            }

            return null;
        }

        public bool TryReceiveCoins(int amount)
        {
            if (amount <= 0)
            {
                return false;
            }

            if (_coinDistributor != null)
            {
                var result = _coinDistributor.Distribute(amount, "pickup");
                if (result.IsEmpty) return false;
                LastCoinDistribution = result;
            }
            else
            {
                if (!_wallet.Credit(amount, "pickup").Success) return false;
                LastCoinDistribution = new CoinDistributionResult(amount, new[] { new CoinShare("local", amount) }, 0, "pickup");
            }

            CarriedCoinsChanged?.Invoke(CarriedCoins);
            return true;
        }
    }
}
