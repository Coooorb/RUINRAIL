using System;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEngine;

namespace RuinRail.Gameplay.Economy
{
    /// <summary>
    /// Deterministic V1 value formulas (base/77_ECONOMY): buy = base × rarity multiplier rounded to the nearest 5,
    /// sell = 35% of the current buy value rounded to the nearest 5, affix quality never changes price. Event prices are
    /// base + perDepth × (Depth − 1) capped. Used by trader, merchant, events and extraction — never re-derived in UI.
    /// </summary>
    public sealed class PriceService
    {
        private readonly EconomyConfig _config;

        public PriceService(EconomyConfig config)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
        }

        public EconomyConfig Config => _config;

        /// <summary>Authored base price of a definition; false for items with no price (e.g. unknown ids).</summary>
        public bool TryGetBasePrice(ItemDefinition definition, out int basePrice)
        {
            basePrice = 0;
            switch (definition)
            {
                case WeaponDefinition weapon:
                    return _config.TryGetWeaponClassPrice(weapon.WeaponClass, out basePrice);
                case ArmorDefinition armor:
                    return _config.TryGetArmorPrice(armor.Id, out basePrice);
                case AccessoryDefinition:
                    basePrice = _config.AccessoryBasePrice;
                    return true;
                case ConsumableDefinition consumable:
                    return _config.TryGetConsumablePrice(consumable.Id, out basePrice);
                case AmmoItemDefinition ammo:
                    if (_config.TryGetAmmoBundle(ammo.AmmoType, out var bundle)) { basePrice = bundle.Price; return true; }
                    return false;
                default:
                    return false;
            }
        }

        public int BuyValue(int basePrice, Rarity rarity)
        {
            return RoundToStep(basePrice * _config.RarityPercent(rarity) / 100f);
        }

        public int BuyValue(ItemDefinition definition, Rarity rarity)
        {
            return TryGetBasePrice(definition, out var basePrice) ? BuyValue(basePrice, rarity) : 0;
        }

        public int BuyValue(ItemDefinition definition, ItemInstance instance) => BuyValue(definition, instance?.Rarity ?? Rarity.Common);

        public int SellValue(int buyValue)
        {
            return RoundToStep(buyValue * _config.SellPercentOfBuyValue / 100f);
        }

        public int SellValue(ItemDefinition definition, Rarity rarity) => SellValue(BuyValue(definition, rarity));

        /// <summary>Consumables/ammo have one fixed rarity label; the buy value is their flat catalog price.</summary>
        public int FlatPrice(ItemDefinition definition) => TryGetBasePrice(definition, out var price) ? price : 0;

        public int EventPrice(DungeonEventPriceKind kind, int depth)
        {
            if (!_config.TryGetEventPrice(kind, out var price)) return 0;
            return Mathf.Min(price.Cap, price.Base + price.PerDepth * Mathf.Max(0, depth - 1));
        }

        public int ScaleCoinReward(int baseAmount, int depth)
        {
            return Mathf.RoundToInt(baseAmount * _config.CoinRewardMultiplier(depth));
        }

        /// <summary>Nearest multiple of the rounding step (5), halves rounding away from zero.</summary>
        public int RoundToStep(float value)
        {
            var step = _config.RoundingStep;
            return (int)Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        }
    }
}
