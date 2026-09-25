using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.UI.Inventory;
using RuinRail.UI.Merchant;
using RuinRail.UI.Navigation;

namespace RuinRail.UI.Base
{
    /// <summary>
    /// What the Shelter Trader's counter shows, in the same content model the accepted Dungeon Merchant already uses.
    ///
    /// The Shelter counter used to be a list of "BUY &lt;name&gt;" strings beside a key/value line per offer: no icon,
    /// no rarity, no category, no affixes and no comparison, so two rolls of the same definition were one word apart
    /// and a player could not tell which one was worth the coins. Every fact needed to fix that was already on the
    /// offer — <see cref="TraderOffer.Item"/> is a rolled <see cref="ItemInstance"/> — so this maps an offer onto the
    /// merchant's <see cref="MerchantRow"/> and reuses <see cref="ItemTooltip"/>, <see cref="TooltipComparison"/> and
    /// <see cref="ItemDetailLayout"/> rather than growing a second presentation language for the same data.
    ///
    /// It holds no Unity types of its own and changes nothing: prices, offers and the transaction path stay in
    /// <see cref="TraderService"/>.
    /// </summary>
    public static class ShelterTraderPresentation
    {
        /// <summary>The counter's offers as merchant rows, in offer order.</summary>
        public static List<MerchantRow> Rows(IReadOnlyList<TraderOffer> offers)
        {
            var rows = new List<MerchantRow>();
            if (offers == null) return rows;
            foreach (var offer in offers)
            {
                if (offer == null) continue;
                rows.Add(new MerchantRow
                {
                    Tab = MerchantTab.Buy,
                    Index = offer.Index,
                    Item = offer.Item,
                    Definition = offer.Definition,
                    Price = offer.Price,
                    IsSold = offer.IsSold
                });
            }

            return rows;
        }

        /// <summary>The focus id the Shelter's trader list uses for an offer index (unchanged from the existing list).</summary>
        public static string FocusIdFor(int offerIndex) => "trader.buy." + offerIndex;

        /// <summary>The offer index behind a focus id, or -1 when the id is not an offer row.</summary>
        public static int OfferIndexOf(string focusId)
        {
            const string prefix = "trader.buy.";
            if (string.IsNullOrEmpty(focusId) || !focusId.StartsWith(prefix, StringComparison.Ordinal)) return -1;
            return int.TryParse(focusId.Substring(prefix.Length), out var index) ? index : -1;
        }

        public static ItemTooltip TooltipFor(MerchantRow row, PlayerInventory loadout, LegendarySpecialRegistry specials = null) =>
            row?.Item == null ? null : ItemTooltip.Build(row.Item, row.Definition ?? loadout?.Resolve(row.Item.DefinitionId), specials);

        /// <summary>
        /// The equipment slot an offer would go to, or null when it is not comparable (consumables and ammo have no
        /// equipped counterpart). Identical rule to the merchant's, so the same offer compares the same way in both.
        /// </summary>
        public static EquippedSlot? CompareSlotFor(ItemDefinition definition, PlayerInventory loadout)
        {
            if (definition == null || loadout == null) return null;
            return definition.Category switch
            {
                ItemCategory.Weapon => loadout.GetEquipped(EquippedSlot.PrimaryWeapon) != null ? EquippedSlot.PrimaryWeapon
                    : loadout.GetEquipped(EquippedSlot.SecondaryWeapon) != null ? EquippedSlot.SecondaryWeapon : null,
                ItemCategory.Armor => EquippedSlot.Armor,
                ItemCategory.Accessory => EquippedSlot.Accessory,
                _ => null
            };
        }

        /// <summary>The offer compared against what the Shelter loadout has equipped for its slot; empty when nothing to compare.</summary>
        public static IReadOnlyList<ComparisonLine> CompareFor(MerchantRow row, PlayerInventory loadout, LegendarySpecialRegistry specials = null)
        {
            if (row?.Item == null || loadout == null) return Array.Empty<ComparisonLine>();
            var definition = row.Definition ?? loadout.Resolve(row.Item.DefinitionId);
            var target = CompareSlotFor(definition, loadout);
            if (target == null) return Array.Empty<ComparisonLine>();
            var current = loadout.GetEquipped(target.Value);
            if (current == null) return Array.Empty<ComparisonLine>();
            return TooltipComparison.Compare(TooltipFor(row, loadout, specials), ItemTooltip.Build(current, loadout.Resolve(current.DefinitionId), specials));
        }

        /// <summary>True when the same definition is already equipped in the Shelter loadout (the row says so, so a duplicate purchase is a choice).</summary>
        public static bool IsEquippedAlready(MerchantRow row, PlayerInventory loadout)
        {
            if (row?.Item == null || loadout == null) return false;
            foreach (EquippedSlot slot in Enum.GetValues(typeof(EquippedSlot)))
            {
                var equipped = loadout.GetEquipped(slot);
                if (equipped != null && equipped.DefinitionId == row.Item.DefinitionId) return true;
            }

            return false;
        }

        /// <summary>The detail pane's subtitle: rarity, category, stack, then the offer's own state and price.</summary>
        public static string DetailSubtitle(MerchantRow row, ItemTooltip tooltip, bool affordable)
        {
            if (row == null || tooltip == null) return string.Empty;
            var text = RarityStyle.For(tooltip.Rarity).Label + " · " + tooltip.CategoryText;
            if (tooltip.Quantity.HasValue && tooltip.Quantity.Value > 1) text += $" · x{tooltip.Quantity.Value}";
            text += row.IsSold ? " · SOLD" : $" · {row.Price} C";
            if (!row.IsSold && !affordable) text += " · NOT ENOUGH COINS";
            return text;
        }
    }
}
