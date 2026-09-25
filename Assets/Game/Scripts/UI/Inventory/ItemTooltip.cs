using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Stats;

namespace RuinRail.UI.Inventory
{
    /// <summary>One labelled integer/text line of a tooltip (93: relevant base stats, affixes with integer values).</summary>
    public readonly struct TooltipLine
    {
        public TooltipLine(string label, string value, int? numeric = null)
        {
            Label = label;
            Value = value;
            Numeric = numeric;
        }

        public string Label { get; }
        public string Value { get; }
        /// <summary>Integer value used for side-by-side comparison; null for text-only lines.</summary>
        public int? Numeric { get; }
        public override string ToString() => $"{Label}: {Value}";
    }

    /// <summary>
    /// The tooltip of one ItemInstance (93): name, rarity as explicit text, relevant base stats, the exact rolled
    /// affixes with integer values, the fixed Legendary special/passive when applicable, and the stack quantity where
    /// relevant. Never a summarising score.
    /// </summary>
    public sealed class ItemTooltip
    {
        public string Name = string.Empty;
        public string RarityText = string.Empty;
        public string CategoryText = string.Empty;
        public readonly List<TooltipLine> BaseStats = new();
        public readonly List<TooltipLine> Affixes = new();
        public string LegendaryText;
        /// <summary>The item's data-derived explanation (ui/93): what it does with its own numbers; always present for a known definition.</summary>
        public string Description = string.Empty;
        public int? Quantity;
        public bool IsUnsellable;
        public bool IsAtRisk;
        public Rarity Rarity;

        /// <summary>ui/90 (TASK 142): marker + rarity label + name — never colour alone — clamped to the reference line budget.</summary>
        public string Title => RuinRail.UI.Navigation.TextFit.Clamp(RuinRail.UI.Navigation.RarityStyle.For(Rarity).Decorate(Name), RuinRail.UI.Navigation.TextFit.ItemNameLineChars);

        public IEnumerable<string> Lines()
        {
            yield return Name;
            yield return RarityText;
            yield return CategoryText;
            if (!string.IsNullOrEmpty(Description)) yield return Description;
            foreach (var line in BaseStats) yield return line.ToString();
            foreach (var line in Affixes) yield return line.ToString();
            if (!string.IsNullOrEmpty(LegendaryText)) yield return LegendaryText;
            if (Quantity.HasValue) yield return $"Quantity: {Quantity.Value}";
        }

        public static string RarityLabel(Rarity rarity) => rarity.ToString().ToUpperInvariant();

        /// <summary>Builds the tooltip from the instance and its definition; affix names come from the definition's pool.</summary>
        public static ItemTooltip Build(ItemInstance item, ItemDefinition definition, LegendarySpecialRegistry specials = null)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            var tooltip = new ItemTooltip
            {
                Name = definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : item.DefinitionId,
                RarityText = RarityLabel(item.Rarity),
                Rarity = item.Rarity,
                CategoryText = definition != null ? definition.Category.ToString().ToUpperInvariant() : string.Empty,
                IsUnsellable = item.IsUnsellable,
                IsAtRisk = item.IsAtRisk
            };

            switch (definition)
            {
                case RangedWeaponDefinition ranged:
                    tooltip.BaseStats.Add(new TooltipLine("Damage", $"{ranged.DamageMin}–{ranged.DamageMax}", ranged.DamageMax));
                    tooltip.BaseStats.Add(new TooltipLine("Fire rate", $"{ranged.FireRate:0.#}/s", (int)Math.Round(ranged.FireRate * 10f)));
                    tooltip.BaseStats.Add(new TooltipLine("Magazine", ranged.MagazineSize.ToString(), ranged.MagazineSize));
                    tooltip.BaseStats.Add(new TooltipLine("Reload", $"{ranged.ReloadTime:0.#}s", -(int)Math.Round(ranged.ReloadTime * 10f)));
                    tooltip.BaseStats.Add(new TooltipLine("Range", $"{ranged.Range:0.#}", (int)Math.Round(ranged.Range)));
                    tooltip.BaseStats.Add(new TooltipLine("Ammo", ranged.AmmoType.ToString()));
                    break;
                case BlasterWeaponDefinition blaster:
                    tooltip.BaseStats.Add(new TooltipLine("Damage", $"{blaster.DamageMin}–{blaster.DamageMax}", blaster.DamageMax));
                    tooltip.BaseStats.Add(new TooltipLine("Max heat", $"{blaster.MaxHeat:0}", (int)Math.Round(blaster.MaxHeat)));
                    tooltip.BaseStats.Add(new TooltipLine("Ammo", "None (heat)"));
                    break;
                case BowWeaponDefinition bow:
                    tooltip.BaseStats.Add(new TooltipLine("Quick shot", $"{bow.QuickDamageMin}–{bow.QuickDamageMax}", bow.QuickDamageMax));
                    tooltip.BaseStats.Add(new TooltipLine("Full draw", $"{bow.FullDrawDamageMin}–{bow.FullDrawDamageMax}", bow.FullDrawDamageMax));
                    tooltip.BaseStats.Add(new TooltipLine("Range", $"{bow.QuickRange:0.#}–{bow.FullRange:0.#}", (int)Math.Round(bow.FullRange)));
                    tooltip.BaseStats.Add(new TooltipLine("Ammo", "None"));
                    break;
                case MeleeWeaponDefinition melee:
                    tooltip.BaseStats.Add(new TooltipLine("Damage", $"{melee.DamageMin}–{melee.DamageMax}", melee.DamageMax));
                    tooltip.BaseStats.Add(new TooltipLine("Reach", $"{melee.AttackRange:0.#}", (int)Math.Round(melee.AttackRange * 10f)));
                    break;
                case ArmorDefinition armor:
                    tooltip.BaseStats.Add(new TooltipLine("Max HP", $"+{armor.BaseMaxHealth}", armor.BaseMaxHealth));
                    tooltip.BaseStats.Add(new TooltipLine("Damage reduction", $"{armor.BaseDamageReductionPercent}%", armor.BaseDamageReductionPercent));
                    foreach (var modifier in armor.BaseModifiers().Where(m => m.Stat != StatId.MaxHealth && m.Stat != StatId.GeneralDamageReduction))
                    {
                        tooltip.BaseStats.Add(new TooltipLine(Label(modifier.Stat), Format(modifier), modifier.Value));
                    }

                    break;
                case AccessoryDefinition accessory:
                    foreach (var modifier in accessory.BaseModifiers())
                    {
                        tooltip.BaseStats.Add(new TooltipLine(Label(modifier.Stat), Format(modifier), modifier.Value));
                    }

                    break;
                case ConsumableDefinition consumable:
                    // Consumables had no stat lines at all: their effect (amount, duration, use time) is data and reads as data.
                    foreach (var (label, value) in ItemDescriptions.Build(consumable, specials).Effects)
                        tooltip.BaseStats.Add(new TooltipLine(label, value, NumericOf(value)));
                    break;
                case AmmoItemDefinition ammo:
                    foreach (var (label, value) in ItemDescriptions.Build(ammo, specials).Effects)
                        tooltip.BaseStats.Add(new TooltipLine(label, value));
                    break;
            }

            if (definition != null)
            {
                var description = ItemDescriptions.Build(definition, specials);
                tooltip.Description = description.Summary;
                if (!string.IsNullOrEmpty(description.Legendary) && definition is EquipmentItemDefinition legendaryFamily && item.Rarity == Rarity.Legendary)
                    tooltip.LegendaryText = description.Legendary;
            }

            if (definition is EquipmentItemDefinition equipment)
            {
                foreach (var roll in item.AffixRolls)
                {
                    // A roll whose affix is no longer in this item's pool can only come from a save written before the
                    // pool was corrected. The item keeps it verbatim (nothing is rerolled or dropped), but the tooltip
                    // must not present it as a working bonus, and it must never leak the internal snake_case id.
                    var affix = equipment.AffixPool != null ? equipment.AffixPool.Affixes.FirstOrDefault(a => a != null && a.Id == roll.AffixId) : null;
                    var label = affix != null
                        ? affix.DisplayName + " (affix)"
                        : LegacyAffixName(roll.AffixId) + " (legacy affix, no effect)";
                    tooltip.Affixes.Add(new TooltipLine(label, roll.Value >= 0 ? $"+{roll.Value}" : roll.Value.ToString(), roll.Value));
                }

                if (item.Rarity == Rarity.Legendary && !string.IsNullOrEmpty(equipment.LegendaryMechanicId) && string.IsNullOrEmpty(tooltip.LegendaryText))
                {
                    tooltip.LegendaryText = specials != null && specials.TryGet(equipment.LegendaryMechanicId, out var special)
                        ? $"LEGENDARY SPECIAL: {special.DisplayName} ({special.Kind}, {special.CooldownSeconds:0.#}s cooldown)"
                        : $"LEGENDARY: {equipment.LegendaryMechanicId}";
                }
            }

            if (definition == null || definition.IsStackable) tooltip.Quantity = item.Quantity;
            return tooltip;
        }

        public static string Label(StatId stat) => StatLabels.Of(stat);

        /// <summary>
        /// Player-facing name for an affix id that no longer belongs to the item's pool ("affix_stagger_power" →
        /// "Stagger Power"). Used only for legacy save data; it never invents a value, only a readable name.
        /// </summary>
        public static string LegacyAffixName(string affixId)
        {
            if (string.IsNullOrEmpty(affixId)) return "Unknown";
            var body = affixId.StartsWith("affix_", System.StringComparison.Ordinal) ? affixId.Substring("affix_".Length) : affixId;
            var words = body.Split('_');
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length == 0) continue;
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            }

            return string.Join(" ", words);
        }

        public static string Format(StatModifier modifier) => StatLabels.Format(modifier);

        /// <summary>The leading integer of a value ("+25 HP" → 25, "8 s" → 8); null when the value carries none.</summary>
        private static int? NumericOf(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            var start = 0;
            while (start < value.Length && !char.IsDigit(value[start]) && value[start] != '-') start++;
            var end = start;
            if (end < value.Length && value[end] == '-') end++;
            while (end < value.Length && char.IsDigit(value[end])) end++;
            return end > start && int.TryParse(value.Substring(start, end - start), out var n) ? n : null;
        }
    }

    /// <summary>One compared line: the candidate's value against the current item's, with an explicit up/down/same mark (93).</summary>
    public readonly struct ComparisonLine
    {
        public ComparisonLine(string label, string candidate, string current, int delta)
        {
            Label = label;
            Candidate = candidate;
            Current = current;
            Delta = delta;
        }

        public string Label { get; }
        public string Candidate { get; }
        public string Current { get; }
        public int Delta { get; }
        public string Mark => Delta > 0 ? "▲" : Delta < 0 ? "▼" : "=";
        public override string ToString() => $"{Label}: {Candidate} vs {Current} {Mark}";
    }

    /// <summary>Side-by-side comparison of two tooltips of the same category — per stat, never a single score.</summary>
    public static class TooltipComparison
    {
        public static IReadOnlyList<ComparisonLine> Compare(ItemTooltip candidate, ItemTooltip current)
        {
            var lines = new List<ComparisonLine>();
            if (candidate == null || current == null) return lines;
            var labels = candidate.BaseStats.Select(s => s.Label).Concat(candidate.Affixes.Select(a => a.Label))
                .Concat(current.BaseStats.Select(s => s.Label)).Concat(current.Affixes.Select(a => a.Label)).Distinct().ToList();
            foreach (var label in labels)
            {
                var a = candidate.BaseStats.Concat(candidate.Affixes).FirstOrDefault(l => l.Label == label);
                var b = current.BaseStats.Concat(current.Affixes).FirstOrDefault(l => l.Label == label);
                var aValue = a.Label == label ? a.Value : "—";
                var bValue = b.Label == label ? b.Value : "—";
                var delta = (a.Label == label ? a.Numeric ?? 0 : 0) - (b.Label == label ? b.Numeric ?? 0 : 0);
                lines.Add(new ComparisonLine(label, aValue, bValue, delta));
            }

            return lines;
        }
    }
}
