using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Items;

namespace RuinRail.Gameplay.Loot
{
    /// <summary>
    /// Shared "one equipment instance at a rolled rarity" path used by shops and choice events: Legendary rolls swap to
    /// the family's Legendary-only definition (33: weapons by class; armor/accessory families are their own), a
    /// missing Legendary falls back to Epic, and a failed affix roll produces a Common item rather than a broken one.
    /// </summary>
    public static class EquipmentRollService
    {
        private static readonly AffixRollService Affixes = new();

        /// <summary>Regular (non-Legendary-only) equipment definitions of a catalog, in stable id order.</summary>
        public static List<EquipmentItemDefinition> RegularEquipment(IEnumerable<ItemDefinition> catalog)
        {
            return catalog.OfType<EquipmentItemDefinition>()
                .Where(IsRegular)
                .OrderBy(d => d.Id, StringComparer.Ordinal)
                .ToList();
        }

        public static bool IsRegular(EquipmentItemDefinition d)
        {
            return d != null && d.Category != ItemCategory.Consumable && d.Category != ItemCategory.Ammo
                   && (string.IsNullOrEmpty(d.LegendaryMechanicId) || d is not WeaponDefinition);
        }

        public static EquipmentItemDefinition LegendaryVariantFor(EquipmentItemDefinition equipment, IEnumerable<ItemDefinition> catalog)
        {
            if (equipment is not WeaponDefinition weapon) return string.IsNullOrEmpty(equipment.LegendaryMechanicId) ? null : equipment;
            return catalog.OfType<WeaponDefinition>().Where(w => w.WeaponClass == weapon.WeaponClass && !string.IsNullOrEmpty(w.LegendaryMechanicId)).OrderBy(w => w.Id, StringComparer.Ordinal).FirstOrDefault();
        }

        public static Rarity RollRarity(RarityTableDefinition table, int depth, IRandomSource random)
        {
            if (table == null) return Rarity.Common;
            var weights = table.WeightsAt(depth);
            var total = weights.Sum();
            if (total <= 0) return Rarity.Common;
            var pick = random.NextInt(total);
            for (var r = 0; r < weights.Length; r++)
            {
                pick -= weights[r];
                if (pick < 0) return (Rarity)r;
            }

            return Rarity.Common;
        }

        /// <summary>Creates the instance; returns the definition actually used (the Legendary variant when swapped).</summary>
        public static ItemInstance Create(EquipmentItemDefinition equipment, Rarity rarity, IRandomSource affixRandom, IEnumerable<ItemDefinition> catalog, out EquipmentItemDefinition used)
        {
            used = equipment;
            if (rarity == Rarity.Legendary)
            {
                var legendary = LegendaryVariantFor(equipment, catalog);
                if (legendary != null) used = legendary;
                else rarity = Rarity.Epic;
            }

            var instance = new ItemInstance(used.Id, 1);
            var roll = Affixes.Roll(instance, used, rarity, affixRandom);
            if (!roll.Success) instance.Rarity = Rarity.Common;
            return instance;
        }
    }
}
