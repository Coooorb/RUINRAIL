using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Economy
{
    public enum DungeonEventPriceKind
    {
        LockedVault,
        BrokenMachine,
        MedicalStationHeal,
        MedicalStationRevive
    }

    /// <summary>
    /// Approved V1 economy baselines (base/77_ECONOMY) as data: rarity multipliers, sell percent, rounding step,
    /// base prices per weapon class / armor family / accessory / consumable, ammo trader bundles, dungeon event price
    /// formulas with caps, respec price and the coin-reward depth multiplier hook. Defaults equal the GDD tables.
    /// </summary>
    [CreateAssetMenu(fileName = "EconomyConfig", menuName = "RuinRail/Balance/Economy Config")]
    public sealed class EconomyConfig : ScriptableObject
    {
        [Serializable] public struct WeaponClassPrice { public WeaponClass Class; public int BasePrice; }
        [Serializable] public struct ItemPrice { public string ItemId; public int Price; }
        [Serializable] public struct AmmoBundle { public AmmoType Type; public int Units; public int Price; }
        [Serializable] public struct EventPrice { public DungeonEventPriceKind Kind; public int Base; public int PerDepth; public int Cap; }

        [Header("Rarity multipliers (percent) and sale")]
        [SerializeField] private int _commonPercent = 100;
        [SerializeField] private int _uncommonPercent = 135;
        [SerializeField] private int _rarePercent = 190;
        [SerializeField] private int _epicPercent = 300;
        [SerializeField] private int _legendaryPercent = 500;
        [SerializeField] private int _sellPercentOfBuyValue = 35;
        [SerializeField] private int _roundingStep = 5;

        [Header("Base prices")]
        [SerializeField] private WeaponClassPrice[] _weaponClassPrices =
        {
            new() { Class = WeaponClass.Pistol, BasePrice = 250 }, new() { Class = WeaponClass.Smg, BasePrice = 350 },
            new() { Class = WeaponClass.AssaultRifle, BasePrice = 450 }, new() { Class = WeaponClass.BattleRifle, BasePrice = 500 },
            new() { Class = WeaponClass.Shotgun, BasePrice = 450 }, new() { Class = WeaponClass.Sniper, BasePrice = 600 },
            new() { Class = WeaponClass.Bow, BasePrice = 400 }, new() { Class = WeaponClass.RocketLauncher, BasePrice = 700 },
            new() { Class = WeaponClass.Blaster, BasePrice = 650 }, new() { Class = WeaponClass.Knife, BasePrice = 250 },
            new() { Class = WeaponClass.Spear, BasePrice = 350 }
        };
        [SerializeField] private ItemPrice[] _armorPrices =
        {
            new() { ItemId = "armor_scrap_vest", Price = 300 }, new() { ItemId = "armor_scout_rig", Price = 350 },
            new() { ItemId = "armor_riot_armor", Price = 450 }, new() { ItemId = "armor_heavy_plate", Price = 650 },
            new() { ItemId = "armor_blast_suit", Price = 500 }, new() { ItemId = "armor_medic_harness", Price = 450 },
            new() { ItemId = "armor_combat_harness", Price = 400 }, new() { ItemId = "armor_reinforced_exo_rig", Price = 600 },
            new() { ItemId = "armor_runner_suit", Price = 350 }
        };
        [SerializeField] private int _accessoryBasePrice = 300;
        [SerializeField] private ItemPrice[] _consumablePrices =
        {
            new() { ItemId = "consumable_bandage", Price = 60 }, new() { ItemId = "consumable_frag_grenade", Price = 80 },
            new() { ItemId = "consumable_medkit", Price = 150 }, new() { ItemId = "consumable_combat_stim", Price = 140 },
            new() { ItemId = "consumable_smoke_grenade", Price = 110 }, new() { ItemId = "consumable_shock_grenade", Price = 220 },
            new() { ItemId = "consumable_damage_stim", Price = 250 }, new() { ItemId = "consumable_armor_injector", Price = 250 },
            new() { ItemId = "consumable_incendiary_grenade", Price = 220 }, new() { ItemId = "consumable_defibrillator", Price = 1500 }
        };
        [SerializeField] private AmmoBundle[] _ammoBundles =
        {
            new() { Type = AmmoType.Light, Units = 60, Price = 60 }, new() { Type = AmmoType.Medium, Units = 40, Price = 70 },
            new() { Type = AmmoType.Heavy, Units = 20, Price = 90 }, new() { Type = AmmoType.Shells, Units = 12, Price = 80 }
        };

        [Header("Dungeon event prices (Carried Coins): base + perDepth × (Depth − 1), capped")]
        [SerializeField] private EventPrice[] _eventPrices =
        {
            new() { Kind = DungeonEventPriceKind.LockedVault, Base = 250, PerDepth = 25, Cap = 1000 },
            new() { Kind = DungeonEventPriceKind.BrokenMachine, Base = 100, PerDepth = 10, Cap = 400 },
            new() { Kind = DungeonEventPriceKind.MedicalStationHeal, Base = 150, PerDepth = 15, Cap = 600 },
            new() { Kind = DungeonEventPriceKind.MedicalStationRevive, Base = 500, PerDepth = 30, Cap = 1500 }
        };

        [Header("Base services")]
        [SerializeField] private int _skillRespecPrice = 2500;

        [Header("Coin reward depth scaling hook (percent per depth above 1; 0 = flat, V1 FINAL (TASK 179) until approved)")]
        [SerializeField, Min(0)] private int _coinRewardPercentPerDepth;
        [SerializeField, Min(100)] private int _coinRewardCapPercent = 100;

        public int SellPercentOfBuyValue => _sellPercentOfBuyValue;
        public int RoundingStep => Mathf.Max(1, _roundingStep);
        public int AccessoryBasePrice => _accessoryBasePrice;
        public int SkillRespecPrice => _skillRespecPrice;
        public IReadOnlyList<AmmoBundle> AmmoBundles => _ammoBundles;

        public int RarityPercent(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Common => _commonPercent,
                Rarity.Uncommon => _uncommonPercent,
                Rarity.Rare => _rarePercent,
                Rarity.Epic => _epicPercent,
                Rarity.Legendary => _legendaryPercent,
                _ => 100
            };
        }

        public bool TryGetWeaponClassPrice(WeaponClass weaponClass, out int price)
        {
            foreach (var entry in _weaponClassPrices)
            {
                if (entry.Class == weaponClass) { price = entry.BasePrice; return true; }
            }

            price = 0;
            return false;
        }

        public bool TryGetArmorPrice(string itemId, out int price) => TryGet(_armorPrices, itemId, out price);
        public bool TryGetConsumablePrice(string itemId, out int price) => TryGet(_consumablePrices, itemId, out price);

        public bool TryGetAmmoBundle(AmmoType type, out AmmoBundle bundle)
        {
            foreach (var entry in _ammoBundles)
            {
                if (entry.Type == type) { bundle = entry; return true; }
            }

            bundle = default;
            return false;
        }

        public bool TryGetEventPrice(DungeonEventPriceKind kind, out EventPrice price)
        {
            foreach (var entry in _eventPrices)
            {
                if (entry.Kind == kind) { price = entry; return true; }
            }

            price = default;
            return false;
        }

        /// <summary>Multiplier applied to coin rewards found at a depth (1.0 everywhere until an approved curve exists).</summary>
        public float CoinRewardMultiplier(int depth)
        {
            var percent = 100 + Mathf.Max(0, depth - 1) * _coinRewardPercentPerDepth;
            return Mathf.Min(percent, _coinRewardCapPercent) / 100f;
        }

        private static bool TryGet(ItemPrice[] table, string itemId, out int price)
        {
            foreach (var entry in table)
            {
                if (entry.ItemId == itemId) { price = entry.Price; return true; }
            }

            price = 0;
            return false;
        }
    }
}
