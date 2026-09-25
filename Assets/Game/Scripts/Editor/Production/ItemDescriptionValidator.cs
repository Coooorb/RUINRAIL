using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuinRail.EditorTools.ArtGen;
using RuinRail.Gameplay.Combat.Weapons.Specials;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The player-facing item description gate (ui/93): every ItemDefinition in the authoritative catalog resolves a
    /// complete description from its own data. It fails when a description is empty, still carries placeholder text,
    /// leaks an internal id, cannot resolve a consumable effect, omits a Legendary special/passive the family owns,
    /// contradicts the definition's numbers, or uses a character the pixel face cannot draw. Deterministic markdown.
    /// </summary>
    public static class ItemDescriptionValidator
    {
        public const string ReportPath = "TestResults/item_descriptions.md";
        public const string SpecialsFolder = "Assets/Game/ScriptableObjects/Specials";

        private static readonly string[] PlaceholderMarkers = { "TODO", "TBD", "PLACEHOLDER", "LOREM", "XXX", "FIXME", "???" };

        public sealed class Line
        {
            public string Id;
            public string Name;
            public string Category;
            public string Summary;
            public string Legendary;
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Line> Lines = new();
            public readonly List<string> Problems = new();
            public int Count => Lines.Count;
            public int Failed => Lines.Count(l => !l.Pass);
            public bool Pass => Problems.Count == 0 && Lines.Count > 0 && Lines.All(l => l.Pass);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL item description validation (ui/93)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Count} items audited, {Count - Failed} complete, {Failed} with problems.");
                sb.AppendLine();
                foreach (var group in Lines.GroupBy(l => l.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    sb.AppendLine($"## {group.Key} ({group.Count()})");
                    sb.AppendLine();
                    sb.AppendLine("| Id | Name | Description | Legendary | Result |");
                    sb.AppendLine("|---|---|---|---|---|");
                    foreach (var line in group.OrderBy(l => l.Id, StringComparer.Ordinal))
                        sb.AppendLine($"| {line.Id} | {line.Name} | {Cell(line.Summary)} | {Cell(line.Legendary)} | {(line.Pass ? "PASS" : "FAIL: " + string.Join("; ", line.Problems))} |");
                    sb.AppendLine();
                }

                foreach (var p in Problems) sb.AppendLine($"- FAIL {p}");
                return sb.ToString();
            }

            private static string Cell(string text) => (text ?? string.Empty).Replace("|", "/").Replace("\n", " ");
        }

        [MenuItem("RuinRail/Production/Validate Item Descriptions")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = ValidateProject();
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath) ?? "TestResults");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report ValidateProject()
        {
            var items = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null)
                .OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            var specials = new LegendarySpecialRegistry(AssetDatabase.FindAssets("t:LegendarySpecialDefinition", new[] { SpecialsFolder })
                .Select(g => AssetDatabase.LoadAssetAtPath<LegendarySpecialDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null));
            var previousCatalog = ItemDescriptions.WeaponCatalog;
            ItemDescriptions.WeaponCatalog = items.OfType<WeaponDefinition>().ToList();
            try
            {
                return Validate(items, specials);
            }
            finally
            {
                ItemDescriptions.WeaponCatalog = previousCatalog;
            }
        }

        /// <summary>Pure form over an explicit catalog (tests feed fixtures).</summary>
        public static Report Validate(IReadOnlyList<ItemDefinition> items, LegendarySpecialRegistry specials)
        {
            var report = new Report();
            if (items == null || items.Count == 0) report.Problems.Add("no item definitions found");
            foreach (var item in items ?? Array.Empty<ItemDefinition>())
            {
                var line = new Line { Id = item.Id ?? string.Empty, Name = item.DisplayName ?? string.Empty, Category = item.Category.ToString() };
                var description = ItemDescriptions.Build(item, specials);
                line.Summary = description.Summary;
                line.Legendary = description.Legendary;
                line.Problems.AddRange(description.Problems);
                Check(item, description, line.Problems);
                report.Lines.Add(line);
            }

            return report;
        }

        private static void Check(ItemDefinition item, ItemDescription description, List<string> problems)
        {
            var summary = description.Summary ?? string.Empty;
            if (string.IsNullOrWhiteSpace(summary)) problems.Add("empty description");
            else if (summary.Trim().Length < 24) problems.Add("description too short to explain the item");
            foreach (var text in new[] { summary, description.Legendary ?? string.Empty })
            {
                foreach (var marker in PlaceholderMarkers) if (text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) problems.Add($"placeholder text '{marker}'");
                if (!string.IsNullOrEmpty(item.Id) && text.Contains(item.Id)) problems.Add("internal item id leaks into the text");
                if (Regex.IsMatch(text, @"\b[a-z]+_[a-z_]+\b")) problems.Add("internal snake_case id leaks into the text");
                foreach (var c in text.Where(c => PixelFontFactory.Charset.IndexOf(c) < 0).Distinct()) problems.Add($"character '{c}' is not in the pixel font");
            }

            switch (item)
            {
                case ConsumableDefinition c:
                    if (c.EffectKind == ConsumableEffectKind.Heal && !summary.Contains($"{c.HealAmount} HP")) problems.Add("heal amount missing from the text");
                    if (c.EffectKind == ConsumableEffectKind.TimedBuff && (!summary.Contains($"{c.BuffPercent}%") || !summary.Contains($"{c.BuffDurationSeconds:0.##} s"))) problems.Add("buff percent/duration missing from the text");
                    if (c.EffectKind == ConsumableEffectKind.Grenade && (!summary.Contains($"{c.Grenade.RadiusTiles:0.##}-tile") || (c.Grenade.DamageMax > 0 && !summary.Contains($"{c.Grenade.DamageMin}–{c.Grenade.DamageMax}")))) problems.Add("grenade radius/damage missing from the text");
                    if (c.EffectKind == ConsumableEffectKind.Revive && !summary.Contains($"{c.ReviveHealthPercent}%")) problems.Add("revive percent missing from the text");
                    if (c.UseTimeSeconds > 0f && !summary.Contains($"{c.UseTimeSeconds:0.##} s")) problems.Add("use time missing from the text");
                    if (description.Effects.Count == 0) problems.Add("consumable effect lines could not be generated");
                    break;
                case RangedWeaponDefinition r:
                    if (!summary.Contains($"{r.DamageMin}–{r.DamageMax}") || !summary.Contains($"{r.MagazineSize}-round")) problems.Add("weapon damage/magazine missing from the text");
                    if (!summary.Contains(ItemDescriptions.AmmoTypeName(r.AmmoType))) problems.Add("ammo type missing from the text");
                    if (r.ProjectilesPerShot > 1 && !summary.Contains($"{r.ProjectilesPerShot} pellets")) problems.Add("pellet count missing from the text");
                    if (r.IsExplosive && !summary.Contains("explodes")) problems.Add("explosion missing from the text");
                    break;
                case BlasterWeaponDefinition b:
                    if (!summary.Contains($"{b.DamageMin}–{b.DamageMax}") || !summary.Contains("Heat")) problems.Add("blaster damage/heat missing from the text");
                    break;
                case BowWeaponDefinition w:
                    if (!summary.Contains($"{w.FullDrawDamageMin}–{w.FullDrawDamageMax}") || !summary.Contains("draw")) problems.Add("bow draw damage missing from the text");
                    break;
                case MeleeWeaponDefinition m:
                    if (!summary.Contains($"{m.DamageMin}–{m.DamageMax}")) problems.Add("melee damage missing from the text");
                    break;
                case ArmorDefinition a:
                    if (a.BaseMaxHealth != 0 && !summary.Contains($"+{a.BaseMaxHealth} Max HP")) problems.Add("armor Max HP missing from the text");
                    if (a.BaseDamageReductionPercent != 0 && !summary.Contains($"+{a.BaseDamageReductionPercent}% Damage Reduction")) problems.Add("armor DR missing from the text");
                    break;
                case AccessoryDefinition acc:
                    foreach (var modifier in acc.BaseModifiers())
                        if (!summary.Contains(RuinRail.Gameplay.Stats.StatLabels.Of(modifier.Stat))) problems.Add($"accessory intrinsic {modifier.Stat} missing from the text");
                    break;
                case AmmoItemDefinition ammo:
                    if (!summary.Contains($"{ammo.MaxStack}")) problems.Add("ammo stack limit missing from the text");
                    break;
            }

            if (item is EquipmentItemDefinition equipment && !string.IsNullOrEmpty(equipment.LegendaryMechanicId))
            {
                if (string.IsNullOrWhiteSpace(description.Legendary) || description.Legendary.Contains("unavailable")) problems.Add($"Legendary mechanic '{equipment.LegendaryMechanicId}' is not described");
                else if (!description.Legendary.StartsWith("LEGENDARY")) problems.Add("Legendary line is not marked as such");
            }
        }
    }
}
