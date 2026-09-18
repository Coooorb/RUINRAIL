namespace RuinRail.Gameplay.Items
{
    public static class RarityRules
    {
        /// <summary>Common 0, Uncommon 1, Rare 2, Epic 3, Legendary 3 (GDD 22).</summary>
        public static int RandomAffixCount(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Common => 0,
                Rarity.Uncommon => 1,
                Rarity.Rare => 2,
                Rarity.Epic => 3,
                Rarity.Legendary => 3,
                _ => 0
            };
        }

        public static bool HasLegendaryMechanic(Rarity rarity) => rarity == Rarity.Legendary;

        public static bool CanHaveAffixes(ItemCategory category)
        {
            return category == ItemCategory.Weapon || category == ItemCategory.Armor || category == ItemCategory.Accessory;
        }
    }
}
