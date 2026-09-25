using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RuinRail.EditorTools.ArtGen;
using RuinRail.Gameplay.Progression;
using RuinRail.Gameplay.Stats;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The character-attribute gate (player/13, ui/94) — the progression counterpart of
    /// <see cref="ItemDescriptionValidator"/>.
    ///
    /// It fails when an attribute's player-facing text is empty, carries placeholder text, is too long for the
    /// Character panel's data column, hard-codes a numeric value (which is how a description goes stale), or uses a
    /// character the pixel face cannot draw. It also re-derives every rank's effect text from
    /// <see cref="SkillRules.ModifiersForRank"/> and fails when the panel's "now" value or next-rank preview disagrees
    /// with it, and when an attribute contributes to no stat at all.
    ///
    /// Finally it scans the gameplay sources for the consumer of every stat an attribute feeds. A stat that no system
    /// reads is reported as a disconnected stat: the attribute would raise a number nothing acts on. That list is
    /// asserted against the known set by the EditMode test, so a newly disconnected stat fails immediately.
    /// </summary>
    public static class AttributeDescriptionValidator
    {
        public const string ReportPath = "TestResults/attribute_descriptions.md";
        public const string SourceRoot = "Assets/Game/Scripts";

        private static readonly string[] PlaceholderMarkers = { "TODO", "TBD", "PLACEHOLDER", "LOREM", "XXX", "FIXME", "???" };

        /// <summary>
        /// The gameplay system that actually acts on each stat an attribute feeds, and the expression that reads it.
        /// Authored rather than guessed, because a mention of a stat id in a tooltip, a cap table or a modifier source
        /// is not a consumer. The scan verifies the named file still contains that expression, so a consumer that stops
        /// reading its stat fails this gate instead of silently turning the attribute cosmetic.
        /// A stat with no entry here is reported as disconnected.
        /// </summary>
        private static readonly Dictionary<StatId, (string File, string Reads)> StatConsumers = new()
        {
            [StatId.MaxHealth] = ("PlayerStatsBinder.cs", "_health.ResizeMaxHealth(Stats.MaxHealth)"),
            [StatId.MovementSpeed] = ("PlayerMovement.cs", "GetMultiplier(StatId.MovementSpeed)"),
            [StatId.WeaponDamage] = ("RangedWeapon.cs", "GetMultiplier(StatId.WeaponDamage)"),
            [StatId.ReloadSpeed] = ("RangedWeapon.cs", "GetMultiplier(StatId.ReloadSpeed)"),
            [StatId.HealingReceived] = ("ConsumableEffects.cs", "GetMultiplier(StatId.HealingReceived)"),
            [StatId.KnockbackResistance] = ("PlayerImpactReceiver.cs", "GetPercent(StatId.KnockbackResistance)"),
            [StatId.StaggerResistance] = ("PlayerImpactReceiver.cs", "GetPercent(StatId.StaggerResistance)")
        };

        public sealed class Line
        {
            public SkillId Skill;
            public string Name;
            public string Description;
            public int MaxRank;
            public string MaxEffect;
            public readonly List<StatId> Stats = new();
            public readonly List<StatId> Disconnected = new();
            public readonly Dictionary<StatId, string> Consumers = new();
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

            /// <summary>Every stat an attribute feeds that no gameplay system reads (the documented gaps).</summary>
            public IReadOnlyList<StatId> Disconnected => Lines.SelectMany(l => l.Disconnected).Distinct().OrderBy(s => s.ToString(), StringComparer.Ordinal).ToList();

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL character attribute validation (player/13, ui/94)");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Count} attributes audited, {Count - Failed} complete, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Attribute | Description | Max rank | Effect at max | Stats | Consumer | Result |");
                sb.AppendLine("|---|---|---:|---|---|---|---|");
                foreach (var line in Lines)
                {
                    var consumers = string.Join("; ", line.Stats.Select(s => $"{s}: {(line.Consumers.TryGetValue(s, out var c) ? c : "NONE")}"));
                    sb.AppendLine($"| {line.Name} | {line.Description} | {line.MaxRank} | {line.MaxEffect} | {string.Join(", ", line.Stats)} | {consumers} | {(line.Pass ? "PASS" : "FAIL: " + string.Join("; ", line.Problems))} |");
                }

                sb.AppendLine();
                var disconnected = Disconnected;
                sb.AppendLine(disconnected.Count == 0
                    ? "Every stat fed by an attribute has a gameplay consumer."
                    : "Stats fed by an attribute with no gameplay consumer: " + string.Join(", ", disconnected) + ".");
                foreach (var p in Problems) sb.AppendLine($"- FAIL {p}");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Character Attributes")]
        public static void ValidateMenu() => Debug.Log(WriteReport().ToMarkdown());

        public static Report WriteReport()
        {
            var report = Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ReportPath)) ?? ".");
            File.WriteAllText(ReportPath, report.ToMarkdown());
            return report;
        }

        public static Report Validate()
        {
            var report = new Report();
            var sources = LoadSources();
            if (sources.Count == 0) report.Problems.Add("no gameplay source files found; the consumer scan cannot run");
            if (SkillRules.All.Length != SkillRules.SkillCount) report.Problems.Add($"SkillRules.All has {SkillRules.All.Length} entries but SkillCount is {SkillRules.SkillCount}");

            foreach (var skill in SkillRules.All)
            {
                var line = new Line
                {
                    Skill = skill,
                    Name = SkillCatalog.DisplayName(skill),
                    Description = SkillCatalog.Description(skill),
                    MaxRank = SkillRules.MaxRank,
                    MaxEffect = SkillCatalog.EffectText(skill, SkillRules.MaxRank)
                };
                line.Stats.AddRange(SkillCatalog.AffectedStats(skill));
                CheckText(line);
                CheckEffects(line);
                CheckConsumers(line, sources);
                report.Lines.Add(line);
            }

            return report;
        }

        private static void CheckText(Line line)
        {
            if (string.IsNullOrWhiteSpace(line.Name)) line.Problems.Add("empty display name");
            else if (line.Name != line.Name.ToUpperInvariant()) line.Problems.Add("display name is not in the screen's upper case");

            var description = line.Description ?? string.Empty;
            if (string.IsNullOrWhiteSpace(description)) line.Problems.Add("empty description");
            else if (description.Trim().Length < 16) line.Problems.Add("description too short to explain the attribute");
            else if (description.Length > SkillCatalog.MaxLineCharacters) line.Problems.Add($"description is {description.Length} characters, more than the {SkillCatalog.MaxLineCharacters} the panel can draw");

            foreach (var marker in PlaceholderMarkers)
                if (description.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) line.Problems.Add($"placeholder text '{marker}'");

            // A number authored into the prose is the way a description goes stale: every value the player sees must be
            // formatted from SkillRules instead.
            if (Regex.IsMatch(description, @"\d")) line.Problems.Add("description hard-codes a numeric value instead of formatting it from the rule");
            if (description.Contains("%")) line.Problems.Add("description hard-codes a percentage instead of formatting it from the rule");
            if (Regex.IsMatch(description, @"\b[a-z]+_[a-z_]+\b")) line.Problems.Add("internal snake_case id leaks into the text");

            foreach (var text in new[] { line.Name, description })
                foreach (var c in text.Where(c => PixelFontFactory.Charset.IndexOf(c) < 0).Distinct())
                    line.Problems.Add($"character '{c}' is not in the pixel font");
        }

        private static void CheckEffects(Line line)
        {
            if (line.Stats.Count == 0)
            {
                line.Problems.Add("attribute contributes no stat modifier at any rank");
                return;
            }

            if (SkillCatalog.ModifiersAt(line.Skill, 0).Count != 0) line.Problems.Add("rank 0 already grants a modifier");

            for (var rank = 0; rank <= SkillRules.MaxRank; rank++)
            {
                var expected = SkillRules.ModifiersForRank(line.Skill, rank).ToList();
                var shown = SkillCatalog.ModifiersAt(line.Skill, rank);
                if (!expected.SequenceEqual(shown)) { line.Problems.Add($"rank {rank} effect does not come from SkillRules"); continue; }

                var rows = SkillCatalog.EffectRows(line.Skill, rank);
                if (rows.Count != line.Stats.Count) { line.Problems.Add($"rank {rank} shows {rows.Count} effect lines for {line.Stats.Count} stats"); continue; }
                foreach (var row in rows)
                {
                    if (row.Length > SkillCatalog.MaxLineCharacters) line.Problems.Add($"rank {rank} effect line '{row}' is longer than the panel can draw");
                }

                var preview = SkillCatalog.NextRankEffectText(line.Skill, rank);
                if (rank >= SkillRules.MaxRank)
                {
                    if (preview != null) line.Problems.Add("a next-rank preview is offered at the rank cap");
                    if (!rows.All(r => r.EndsWith(SkillCatalog.MaxedText, StringComparison.Ordinal))) line.Problems.Add("the rank cap is not marked MAX on every effect line");
                }
                else
                {
                    var authoritative = SkillCatalog.EffectText(line.Skill, rank + 1);
                    if (preview != authoritative) line.Problems.Add($"rank {rank} preview '{preview}' disagrees with the rule's '{authoritative}'");
                }
            }
        }

        private static void CheckConsumers(Line line, IReadOnlyDictionary<string, string> sources)
        {
            foreach (var stat in line.Stats)
            {
                if (!StatConsumers.TryGetValue(stat, out var consumer)) { line.Disconnected.Add(stat); continue; }
                if (!sources.TryGetValue(consumer.File, out var text))
                {
                    line.Problems.Add($"{stat}: the recorded consumer {consumer.File} does not exist");
                    line.Disconnected.Add(stat);
                    continue;
                }

                if (!text.Contains(consumer.Reads, StringComparison.Ordinal))
                {
                    line.Problems.Add($"{stat}: {consumer.File} no longer reads it as '{consumer.Reads}'");
                    line.Disconnected.Add(stat);
                    continue;
                }

                line.Consumers[stat] = consumer.File;
            }

            if (line.Disconnected.Count == line.Stats.Count)
                line.Problems.Add($"no gameplay system reads any stat this attribute feeds ({string.Join(", ", line.Stats)})");
        }

        private static Dictionary<string, string> LoadSources()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!Directory.Exists(SourceRoot)) return files;
            foreach (var path in Directory.GetFiles(SourceRoot, "*.cs", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                files[Path.GetFileName(path)] = File.ReadAllText(path);
            }

            return files;
        }
    }
}
