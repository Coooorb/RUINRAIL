using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEditor;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// The contract gate for the run-variety / depth-retention / boss pass.
    ///
    /// It exists because every change in that pass is a *contract*, not a value: depth gating must not break Depth 1
    /// generation, the deep-depth reward curve must stay flat through Depth 30 and bounded above it, biome weighting
    /// must never make an archetype impossible, and the elite rate must stay inside its declared band. A validator that
    /// only counted content would pass all of those while any one of them was broken.
    ///
    /// Each rule is checked against the shipped data, and <see cref="Validate"/> takes its inputs so a test can feed it
    /// a deliberately broken fixture and prove the gate fails.
    /// </summary>
    public static class RunVarietyDepthRetentionValidator
    {
        public const string ReportPath = "TestResults/run_variety_depth_retention.md";

        /// <summary>Rooms per biome the content contract requires, unchanged by depth gating.</summary>
        public const int RoomsPerBiome = 21;

        /// <summary>Depth from which the reward continuation may rise; at or below it every multiplier must be exactly 1.</summary>
        public const int RewardCurveStartDepth = 30;

        /// <summary>The reward curve may never exceed this multiplier, however deep the run goes.</summary>
        public const float RewardCurveHardCap = 2.0f;

        /// <summary>Declared elite band: per-slot chance must stay inside it at every depth.</summary>
        public const int EliteChanceMinPercent = 5;
        public const int EliteChanceMaxPercent = 25;

        public sealed class Line
        {
            public string Rule = string.Empty;
            public string Subject = string.Empty;
            public string Detail = string.Empty;
            public readonly List<string> Problems = new();
            public bool Pass => Problems.Count == 0;
        }

        public sealed class Report
        {
            public readonly List<Line> Lines = new();
            public int Failed => Lines.Count(l => !l.Pass);
            public bool Pass => Lines.Count > 0 && Lines.All(l => l.Pass);

            public string ToMarkdown()
            {
                var sb = new StringBuilder();
                sb.AppendLine("# RUINRAIL run variety / depth retention / boss contract");
                sb.AppendLine();
                sb.AppendLine($"Result: **{(Pass ? "PASS" : "FAIL")}** — {Lines.Count} rules checked, {Lines.Count - Failed} clean, {Failed} with problems.");
                sb.AppendLine();
                sb.AppendLine("| Rule | Subject | Detail | Result |");
                sb.AppendLine("|---|---|---|---|");
                foreach (var l in Lines.OrderBy(l => l.Rule, StringComparer.Ordinal).ThenBy(l => l.Subject, StringComparer.Ordinal))
                    sb.AppendLine($"| {l.Rule} | {l.Subject} | {l.Detail} | {(l.Pass ? "PASS" : "FAIL: " + string.Join("; ", l.Problems))} |");
                return sb.ToString();
            }
        }

        [MenuItem("RuinRail/Production/Validate Run Variety / Depth Retention")]
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
            var catalog = GameContentCatalog.Load();
            return Validate(catalog != null ? catalog.Rooms : new List<RoomDefinition>(),
                catalog?.Economy, catalog != null ? catalog.Enemies : new List<RuinRail.Gameplay.Enemies.EnemyDefinition>());
        }

        /// <summary>Test seam: the same rules over injected content, so a broken fixture can be proven to fail.</summary>
        public static Report Validate(IReadOnlyList<RoomDefinition> rooms, EconomyConfig economy,
            IReadOnlyList<RuinRail.Gameplay.Enemies.EnemyDefinition> enemies)
        {
            var report = new Report();
            // The _Test grid room is not part of any biome contract; only authored biome rooms are counted.
            var authored = (rooms ?? Array.Empty<RoomDefinition>()).Where(r => r != null).ToList();

            // ---- 1. the content contract survives depth gating ----
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var ofBiome = authored.Where(r => r.Biome == biome).ToList();
                var line = new Line { Rule = "rooms per biome", Subject = biome.ToString(), Detail = ofBiome.Count + " authored" };
                if (ofBiome.Count != RoomsPerBiome) line.Problems.Add($"expected {RoomsPerBiome} authored rooms, found {ofBiome.Count}");
                report.Lines.Add(line);

                // ---- 2. depth gating never removes a room category from a depth a player can reach ----
                foreach (var depth in new[] { 1, 5, 10, 20, 30 })
                {
                    var eligible = ofBiome.Where(r => r.IsAvailableAtDepth(depth)).ToList();
                    var gate = new Line
                    {
                        Rule = "depth eligibility",
                        Subject = $"{biome} D{depth}",
                        Detail = $"{eligible.Count} rooms: " + string.Join(", ", eligible.GroupBy(r => r.RoomType).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}"))
                    };
                    foreach (RoomType type in Enum.GetValues(typeof(RoomType)))
                    {
                        if (ofBiome.All(r => r.RoomType != type)) continue; // the biome never authored this type
                        if (eligible.All(r => r.RoomType != type)) gate.Problems.Add($"{type} has no room available at D{depth}");
                    }

                    // The graph asks for up to 8 combat rooms and reuses definitions only as a fallback; fewer than that
                    // many distinct combat rooms is a generation risk rather than an outright failure, so it is flagged.
                    var combat = eligible.Count(r => r.RoomType == RoomType.Combat);
                    if (combat < 8) gate.Problems.Add($"only {combat} combat rooms at D{depth}; the graph asks for up to 8 distinct ones");
                    if (!eligible.Any(r => r.RoomType == RoomType.Combat && r.SupportsElite))
                        gate.Problems.Add($"no elite-capable combat room at D{depth}");
                    report.Lines.Add(gate);
                }
            }

            // ---- 3. the reward curve: exactly flat through the start depth, bounded and monotonic above it ----
            if (economy == null)
            {
                report.Lines.Add(new Line { Rule = "reward curve", Subject = "EconomyConfig", Detail = "missing", Problems = { "no economy config to validate" } });
            }
            else
            {
                var flat = new Line { Rule = "reward curve", Subject = $"D1-D{RewardCurveStartDepth} unchanged", Detail = "coin and XP multipliers" };
                for (var d = 1; d <= RewardCurveStartDepth; d++)
                {
                    if (Mathf.Abs(economy.CoinRewardMultiplier(d) - 1f) > 0.0001f) flat.Problems.Add($"coin multiplier at D{d} is {economy.CoinRewardMultiplier(d):0.####}, must be exactly 1");
                    if (Mathf.Abs(economy.XpRewardMultiplier(d) - 1f) > 0.0001f) flat.Problems.Add($"XP multiplier at D{d} is {economy.XpRewardMultiplier(d):0.####}, must be exactly 1");
                }

                report.Lines.Add(flat);

                var bounded = new Line
                {
                    Rule = "reward curve",
                    Subject = "D31+ bounded and monotonic",
                    Detail = $"D40 x{economy.CoinRewardMultiplier(40):0.00}, D50 x{economy.CoinRewardMultiplier(50):0.00}, D100 x{economy.CoinRewardMultiplier(100):0.00}, D500 x{economy.CoinRewardMultiplier(500):0.00}"
                };
                var previousCoin = economy.CoinRewardMultiplier(RewardCurveStartDepth);
                var previousXp = economy.XpRewardMultiplier(RewardCurveStartDepth);
                var rose = false;
                for (var d = RewardCurveStartDepth + 1; d <= 500; d++)
                {
                    var coin = economy.CoinRewardMultiplier(d);
                    var xp = economy.XpRewardMultiplier(d);
                    if (coin < previousCoin - 0.0001f) bounded.Problems.Add($"coin multiplier fell between D{d - 1} and D{d}");
                    if (xp < previousXp - 0.0001f) bounded.Problems.Add($"XP multiplier fell between D{d - 1} and D{d}");
                    if (coin > RewardCurveHardCap) bounded.Problems.Add($"coin multiplier at D{d} is {coin:0.00}, above the {RewardCurveHardCap:0.00} hard cap");
                    if (xp > RewardCurveHardCap) bounded.Problems.Add($"XP multiplier at D{d} is {xp:0.00}, above the {RewardCurveHardCap:0.00} hard cap");
                    if (coin > previousCoin + 0.0001f) rose = true;
                    previousCoin = coin;
                    previousXp = xp;
                }

                if (!rose) bounded.Problems.Add("the curve never rises past the start depth, so deep play stays reward-flat");
                report.Lines.Add(bounded);
            }

            // ---- 4. biome weighting is weighting, never exclusion ----
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                var line = new Line { Rule = "biome encounter weighting", Subject = biome.ToString() };
                var weights = (enemies ?? Array.Empty<RuinRail.Gameplay.Enemies.EnemyDefinition>())
                    .Where(e => e != null && e.ThreatCost > 0f)
                    .Select(e => (e.Id, Weight: BiomeEncounterWeights.Of(biome, e)))
                    .OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
                line.Detail = string.Join(" ", weights.Select(w => $"{w.Id}:{w.Weight}"));
                foreach (var (id, weight) in weights)
                {
                    if (weight <= 0) line.Problems.Add($"{id} has weight {weight} — an archetype must never be excluded from a biome");
                }

                if (weights.Count > 0 && weights.All(w => w.Weight == BiomeEncounterWeights.Default))
                    line.Problems.Add("every archetype sits at the default weight, so this biome has no encounter identity");
                report.Lines.Add(line);
            }

            // Identity means the three biomes must not share one weighting.
            var signatures = ((Biome[])Enum.GetValues(typeof(Biome))).Select(b => string.Join(",",
                (enemies ?? Array.Empty<RuinRail.Gameplay.Enemies.EnemyDefinition>()).Where(e => e != null && e.ThreatCost > 0f)
                .OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => BiomeEncounterWeights.Of(b, e)))).ToList();
            var distinct = new Line
            {
                Rule = "biome encounter weighting",
                Subject = "the three biomes differ",
                Detail = signatures.Distinct().Count() + " distinct weightings"
            };
            if (signatures.Distinct().Count() != signatures.Count) distinct.Problems.Add("two biomes share an identical archetype weighting");
            report.Lines.Add(distinct);

            // ---- 5. elite chance stays inside the declared band ----
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var elite = new Line
                {
                    Rule = "elite frequency",
                    Subject = "declared band",
                    Detail = string.Join(" ", new[] { 1, 3, 6, 11, 21 }.Select(d => $"D{d}:{rules.EliteChancePercent(d)}%x{rules.MaxElites(d)}"))
                };
                foreach (var d in new[] { 1, 2, 3, 5, 6, 10, 11, 20, 21, 30, 50, 100 })
                {
                    var chance = rules.EliteChancePercent(d);
                    if (chance < EliteChanceMinPercent || chance > EliteChanceMaxPercent)
                        elite.Problems.Add($"D{d} chance {chance}% is outside the declared {EliteChanceMinPercent}-{EliteChanceMaxPercent}% band");
                }

                if (rules.EliteChancePercent(1) > rules.EliteChancePercent(21))
                    elite.Problems.Add("Depth 1 is at least as elite-heavy as the deepest band");
                report.Lines.Add(elite);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }

            return report;
        }
    }
}
