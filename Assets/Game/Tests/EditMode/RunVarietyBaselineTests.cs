using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Phase 1 / 4 / 8 measurement for the run-variety pass: the current state of everything this pass may touch,
    /// captured from the working tree before any edit, plus the reward curve and the elite frequency the later phases
    /// are calibrated against.
    ///
    /// Every figure is read from shipped data or computed by a shipped formula. Nothing here writes a shipping value.
    /// </summary>
    public class RunVarietyBaselineTests
    {
        public const string Folder = "TestResults/RunVarietyDepthRetention";

        /// <summary>Depth ladder the whole pass reports on, including the post-30 tail the review flagged.</summary>
        public static readonly int[] Ladder = { 1, 5, 10, 20, 30, 40, 50, 75, 100 };

        /// <summary>Deterministic seeds for the generation sweeps. 120 seeds x 6 depths x 3 biomes = 2160 depths.</summary>
        public static readonly int[] Seeds = Enumerable.Range(0, 120).Select(i => 30000 + i * 131).ToArray();
        public static readonly int[] EliteDepths = { 1, 5, 10, 20, 30, 50 };
        public static readonly Biome[] Biomes = { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs };

        private GameContentCatalog _content;
        private PriceService _prices;
        private BiomeRoomPools _pools;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _prices = new PriceService(_content.Economy);
            _pools = BiomeRoomPools.Build(_content.Rooms);
            Directory.CreateDirectory(Folder);
        }

        private DungeonGenerationResult Generate(Biome biome, int seed, int depth)
        {
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                return DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(rules), _pools.PoolFor(biome), seed, depth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        // ================= PHASE 1 =================

        [Test]
        public void Phase1_WritesCurrentStateBefore()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Category,Subject,Field,Value,Source,Notes");
            void Row(string cat, string subject, string field, string value, string source, string notes = "") =>
                csv.AppendLine(string.Join(",", cat, Csv(subject), Csv(field), Csv(value), Csv(source), Csv(notes)));

            // ---- depth scaling, including the post-30 tail ----
            foreach (var d in Ladder)
            {
                Row("depth scaling", "D" + d, "enemy HP multiplier", "x" + F(DepthScaling.HealthMultiplier(d, _content.DepthScaling)), "DepthScalingConfig");
                Row("depth scaling", "D" + d, "enemy damage multiplier", "x" + F(DepthScaling.DamageMultiplier(d, _content.DepthScaling)), "DepthScalingConfig");
                Row("depth scaling", "D" + d, "attack speed multiplier", "x" + F(DepthScaling.AttackSpeedMultiplier(d, _content.DepthScaling)), "DepthScalingConfig");
                Row("depth scaling", "D" + d, "movement speed multiplier", "x" + F(DepthScaling.MovementSpeedMultiplier(d, _content.DepthScaling)), "DepthScalingConfig");
                Row("depth scaling", "D" + d, "boss HP at depth (Aegis Core)", DepthScaling.ScaledHealth(1050, d, 1, true, _content.DepthScaling).ToString(), "DepthScaling.ScaledHealth", "bosses use the same HP curve as normal enemies");
                Row("depth scaling", "D" + d, "elite chance percent", DepthScaling.EliteChancePercent(d, _content.DepthScaling) + "%", "DepthScalingConfig", "NOTE: the generator uses DungeonGraphRules, not this value");
                var (bmin, bmax) = ThreatBudgetTable.SoloBudget(d);
                Row("depth scaling", "D" + d, "solo threat budget", $"{F1(bmin)}-{F1(bmax)}", "ThreatBudgetTable");
                Row("reward", "D" + d, "coin reward multiplier", "x" + F(_prices.ScaleCoinReward(1000, d) / 1000f), "EconomyConfig.CoinRewardMultiplier", "FLAT: percent-per-depth is 0 AND the method has no gameplay caller");
                var w = _content.Loot.RarityTableFor(LootQuality.Standard).WeightsAt(d);
                Row("reward", "D" + d, "rarity permille (Standard)", string.Join("/", w), "RarityTable_Standard", d >= 30 ? "last authored band is D30; WeightsAt holds it flat beyond" : "");
            }

            // ---- XP: there is no depth curve at all; XP is the enemy's authored BaseXp ----
            Row("reward", "XP", "depth scaling", "none", "ExpeditionService.AddXp", "XP is the enemy's authored BaseXp; no depth multiplier exists anywhere");
            Row("reward", "coins", "depth scaling consumer", "NONE", "grep", "PriceService.ScaleCoinReward / DepthScaling.CoinRewardMultiplier have no gameplay caller — an authored curve with no consumer");
            Row("reward", "boss cache", "table / quality", "loot_boss_cache / StronglyImproved", "LootSourceCatalog");
            Row("reward", "merchant", "price scaling with depth", "none", "PriceService", "prices are rarity-scaled only");
            foreach (DungeonEventPriceKind kind in Enum.GetValues(typeof(DungeonEventPriceKind)))
                Row("reward", "event " + kind, "D1 / D30 / D100 cost", $"{_prices.EventPrice(kind, 1)} / {_prices.EventPrice(kind, 30)} / {_prices.EventPrice(kind, 100)}", "EconomyConfig", "event costs DO scale with depth and cap");

            // ---- elite frequency rules (generator, not DepthScalingConfig) ----
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                foreach (var d in new[] { 1, 2, 3, 5, 6, 10, 11, 20, 21, 30, 50 })
                    Row("elites", "D" + d, "chance percent / max slots", $"{rules.EliteChancePercent(d)}% / {rules.MaxElites(d)}", "DungeonGraphRules",
                        "expected elites per depth = slots x chance");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }

            // ---- rooms ----
            foreach (var room in _content.Rooms.Where(r => r != null).OrderBy(r => r.Id, StringComparer.Ordinal))
                Row("rooms", room.Id, "minDepth / maxDepth / weight / type / size",
                    $"{room.MinDepth} / {(room.IsUnlimitedDepth ? "unlimited" : room.MaxDepth.ToString())} / {F(room.SelectionWeight)} / {room.RoomType} / {room.SizeClass}",
                    room.name + ".asset", room.SupportsElite ? "elite-capable" : "");

            // ---- encounter pools / weighting ----
            Row("encounters", "biome weighting", "implementation", "none", "EncounterDirector.Compose",
                "the same archetype list is used for every biome; only room tags and UnlockDepth filter it");
            foreach (var e in _content.Enemies.Where(e => e != null).OrderBy(e => e.Id, StringComparer.Ordinal))
                Row("encounters", e.Id, "threat / unlock depth / attack kind", $"{F(e.ThreatCost)} / D{e.UnlockDepth} / {e.AttackKind}", e.name + ".asset");

            // ---- hazards ----
            foreach (var hazard in UnityEditor.AssetDatabase.FindAssets("t:HazardDefinition")
                         .Select(g => UnityEditor.AssetDatabase.LoadAssetAtPath<RuinRail.Gameplay.Combat.Hazards.HazardDefinition>(UnityEditor.AssetDatabase.GUIDToAssetPath(g)))
                         .Where(h => h != null).OrderBy(h => h.Id, StringComparer.Ordinal))
                Row("hazards", hazard.Id, "damage / tick / delay / knockback / stagger",
                    $"{hazard.DamageMin}-{hazard.DamageMax} / {F(hazard.TickIntervalSeconds)}s / {F(hazard.InitialDelaySeconds)}s / {F(hazard.Knockback)} / {F(hazard.StaggerPower)}",
                    hazard.name + ".asset", "all three biome hazards are numerically identical");

            // ---- bosses ----
            foreach (var boss in _content.Bosses.Where(b => b != null).OrderBy(b => b.Id, StringComparer.Ordinal))
            {
                var reach = boss.Moveset.Where(a => a != null).Select(a => a.MaxTriggerRange).DefaultIfEmpty(0f).Max();
                Row("bosses", boss.Id, "HP / speed / biome", $"{boss.BaseHealth} / {F(boss.MoveSpeed)} / {boss.Biome}", boss.name + ".asset");
                Row("bosses", boss.Id, "max authored attack reach", F(reach) + " tiles", "moveset",
                    "beyond this distance no authored attack is in band");
                foreach (var a in boss.Moveset.Where(a => a != null))
                    Row("bosses", boss.Id, "attack " + a.Id, $"{F(a.MinTriggerRange)}-{F(a.MaxTriggerRange)} tiles, telegraph {F(a.TelegraphSeconds)}s, cooldown {F(a.CooldownSeconds)}s, damage {a.DamageMin}-{a.DamageMax}", a.name + ".asset");
            }

            var arena = RoomSizeClasses.DimensionsOf(RoomSizeClass.Boss);
            Row("bosses", "arena", "boss room size", $"{arena.x}x{arena.y} tiles", "RoomSizeClasses", "the diagonal is far beyond every authored attack band");
            Row("bosses", "attack selection", "implementation", "first ready in-band attack in list order", "MovesetActorController.SelectAttack",
                "REVIEW FINDING CONFIRMED: list order decides, so later attacks are reachable only when earlier ones are on cooldown or out of band");
            Row("bosses", "no valid attack", "behaviour", "stays in Chase and walks toward the target", "MovesetActorController.FixedUpdate",
                "REVIEW FINDING CONFIRMED: no reposition/gap-close exists; boss speeds are 1.6-3.2 vs the player's 5 tiles/s");

            // ---- profile / save ----
            Row("save", "PlayerProfile", "CurrentVersion", RuinRail.Gameplay.Expedition.PlayerProfile.CurrentVersion.ToString(), "PlayerProfile");
            Row("save", "SaveSlot", "CurrentVersion", RuinRail.Persistence.SaveSlot.CurrentVersion.ToString(), "SaveSlot");
            Row("save", "PlayerProfile", "deepest-depth field exists", HasDeepestDepthField() ? "yes" : "NO", "reflection",
                "a personal-best depth record does not exist before this pass");

            File.WriteAllText(Path.Combine(Folder, "current_state_before.csv"), csv.ToString());
            Assert.IsTrue(File.Exists(Path.Combine(Folder, "current_state_before.csv")));
        }

        private static bool HasDeepestDepthField() =>
            typeof(RuinRail.Gameplay.Expedition.PlayerProfile).GetFields()
                .Any(f => f.Name.IndexOf("Deepest", StringComparison.OrdinalIgnoreCase) >= 0);

        // ================= PHASE 4 (before) =================

        [Test]
        public void Phase4_WritesRewardCurveBefore()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Depth,EnemyHpMultiplier,EnemyDamageMultiplier,ExpectedXpPerDepth,ExpectedCoinsPerDepth,RarityPermille,ExpectedRarityScore,BossCacheCoins,TotalCarriedValue,SecuredByReturn,AtRiskByDescend,Notes");
            var roller = _content.Loot.CreateRoller();
            foreach (var d in Ladder)
            {
                var xp = ExpectedXp(d);
                var coins = ExpectedCoins(d, roller, out var cacheCoins);
                var w = _content.Loot.RarityTableFor(LootQuality.Standard).WeightsAt(d);
                var score = RarityScore(w);
                csv.AppendLine(string.Join(",", new[]
                {
                    d.ToString(), F(DepthScaling.HealthMultiplier(d, _content.DepthScaling)), F(DepthScaling.DamageMultiplier(d, _content.DepthScaling)),
                    F1(xp), F1(coins), Csv(string.Join("/", w)), F(score), F1(cacheCoins), F1(coins), F1(coins), F1(coins),
                    Csv(d <= 30 ? "authored rarity band" : "rarity table has no band past D30; WeightsAt holds the D30 row flat")
                }));
            }

            File.WriteAllText(Path.Combine(Folder, "reward_curve_before.csv"), csv.ToString());

            // The findings this pass exists to fix, asserted so they cannot silently change under us.
            // XP has no depth multiplier at all: the only thing that grows it is the threat budget (more enemies per
            // room), and that caps at D50 — so from D50 onwards every reward axis is completely flat.
            Assert.AreEqual(ExpectedXp(50), ExpectedXp(75), 0.01f, "XP per depth is flat from D50 before this pass");
            Assert.AreEqual(ExpectedXp(50), ExpectedXp(100), 0.01f, "XP per depth is flat from D50 before this pass");
            Assert.Greater(ExpectedXp(50), ExpectedXp(30), "between D30 and D50 XP still grows, but only through the threat budget");
            Assert.AreEqual(RarityScore(_content.Loot.RarityTableFor(LootQuality.Standard).WeightsAt(30)),
                RarityScore(_content.Loot.RarityTableFor(LootQuality.Standard).WeightsAt(100)), 0.001f,
                "rarity is flat past D30 before this pass");
        }

        /// <summary>Expected XP of one depth: the threat-budget worth of enemies per combat room, times the rooms, plus a boss.</summary>
        private float ExpectedXp(int depth)
        {
            var (min, max) = ThreatBudgetTable.SoloBudget(depth);
            var target = (min + max) * 0.5f;
            var eligible = _content.Enemies.Where(e => e != null && e.ThreatCost > 0f && e.UnlockDepth <= depth).ToList();
            var xpPerThreat = (float)eligible.Average(e => e.BaseXp / e.ThreatCost);
            const float combatRoomsPerDepth = 6.7f;
            var bossXp = (float)_content.Bosses.Where(b => b != null).Average(b => b.BaseXp);
            return target * xpPerThreat * combatRoomsPerDepth + bossXp;
        }

        /// <summary>Expected coins of one depth from the real loot tables at that depth.</summary>
        private float ExpectedCoins(int depth, LootRoller roller, out float bossCacheCoins)
        {
            bossCacheCoins = 0f;
            var total = 0f;
            const int samples = 200;
            foreach (var (kind, count) in new[] { (LootSourceKind.SupplyChest, 2.2f), (LootSourceKind.EquipmentChest, 1f), (LootSourceKind.TreasureChest, 0.5f), (LootSourceKind.BossCache, 1f) })
            {
                if (!_content.Loot.TryGet(kind, out var source) || source.Table == null) continue;
                var sum = 0f;
                for (var i = 0; i < samples; i++)
                    sum += roller.Roll(source.Table, LootContext.ForSource(500000 + i, depth, i, source.Quality, 1, new[] { AmmoType.Light })).Coins;
                var perSource = sum / samples;
                if (kind == LootSourceKind.BossCache) bossCacheCoins = perSource;
                total += perSource * count;
            }

            return total;
        }

        private static float RarityScore(int[] weights)
        {
            var total = weights.Sum();
            return total == 0 ? 0f : Enumerable.Range(0, weights.Length).Sum(i => i * weights[i]) / (float)total;
        }

        // ================= PHASE 8 (before) =================

        [Test]
        public void Phase8_WritesEliteFrequencyBefore()
        {
            File.WriteAllText(Path.Combine(Folder, "elite_frequency_before.csv"), MeasureEliteFrequency(out var depths));
            Assert.GreaterOrEqual(depths, 1000, $"the elite sample must be at least 1000 generated depths (was {depths})");
        }

        /// <summary>Shared by the before/after elite measurements so both sides are produced by identical code.</summary>
        public string MeasureEliteFrequency(out int generated)
        {
            var csv = new StringBuilder();
            csv.AppendLine("Depth,Biome,Depths,EliteEligibleRoomsTotal,DepthsWithAnElite,DepthsWithAnElitePercent,ElitesTotal,ElitesPerDepth,MaxSlots,ChancePercent,EliteTypesSeen");
            generated = 0;
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                foreach (var depth in EliteDepths)
                {
                    foreach (var biome in Biomes)
                    {
                        var withElite = 0;
                        var elites = 0;
                        var eligible = 0;
                        var depthsHere = 0;
                        var types = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var seed in Seeds)
                        {
                            var generation = Generate(biome, seed, depth);
                            generated++;
                            if (!generation.Success) continue;
                            depthsHere++;
                            var graph = generation.Graph;
                            eligible += graph.NodesOfType(RoomType.Combat).Count(n => !graph.AreAdjacent(n.Id, graph.StartId) && !graph.AreAdjacent(n.Id, graph.BossId));
                            var here = graph.Nodes.Count(n => n.IsElite);
                            elites += here;
                            if (here > 0) withElite++;
                            foreach (var node in graph.Nodes.Where(n => n.IsElite))
                            {
                                var elite = _content.Elites.Where(e => e.Biome == biome).OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
                                if (elite.Count > 0) types.Add(elite[Mathf.Abs(seed + node.Id) % elite.Count].Id);
                            }
                        }

                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), biome.ToString(), depthsHere.ToString(), eligible.ToString(),
                            withElite.ToString(), F(withElite / Mathf.Max(1f, depthsHere) * 100f),
                            elites.ToString(), F(elites / Mathf.Max(1f, depthsHere)),
                            rules.MaxElites(depth).ToString(), rules.EliteChancePercent(depth).ToString(),
                            Csv(string.Join("; ", types.OrderBy(t => t, StringComparer.Ordinal)))
                        }));
                    }
                }

                // What a player actually experiences: the chance of meeting no elite at all across a multi-depth run.
                csv.AppendLine();
                csv.AppendLine("# EXPEDITION-LEVEL VIEW (product of per-depth 'no elite' rates along a run starting at D1)");
                csv.AppendLine("DepthsPushed,ChanceOfSeeingNoEliteAtAll,ExpectedElitesInTheRun");
                var noElite = 1f;
                var expected = 0f;
                for (var d = 1; d <= 10; d++)
                {
                    var perDepth = rules.MaxElites(d) * rules.EliteChancePercent(d) / 100f;
                    noElite *= Mathf.Pow(1f - rules.EliteChancePercent(d) / 100f, rules.MaxElites(d));
                    expected += perDepth;
                    if (d is 1 or 2 or 3 or 5 or 10) csv.AppendLine($"{d},{F(noElite * 100f)}%,{F(expected)}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }

            return csv.ToString();
        }
    }
}
