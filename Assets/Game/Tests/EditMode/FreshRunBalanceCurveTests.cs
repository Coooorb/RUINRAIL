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
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Items.Consumables;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Stats;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// Depth pacing, the in-run coin economy, the loot rarity curve over 510 deterministic depth generations, the
    /// descend-vs-return snapshot, and the strict role-overlap re-check. Deterministic seeds only.
    /// </summary>
    public class FreshRunBalanceCurveTests
    {
        public const string Folder = FreshRunBalanceWeaponTests.Folder;

        private GameContentCatalog _content;
        private FreshRunSimulator _simulator;
        private PriceService _prices;
        private LootRoller _roller;
        private BiomeRoomPools _pools;
        private Dictionary<string, ItemDefinition> _itemsById;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            Assert.IsNotNull(_content);
            _simulator = new FreshRunSimulator(_content);
            _prices = new PriceService(_content.Economy);
            _roller = _content.Loot.CreateRoller();
            _pools = BiomeRoomPools.Build(_content.Rooms);
            _itemsById = _content.Items.Where(i => i != null).GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            Directory.CreateDirectory(Folder);
        }

        private PlayerStats Stats(params StatModifier[] modifiers)
        {
            var stats = new PlayerStats(_content.StatCaps, 100);
            if (modifiers.Length > 0) stats.SetSource(new StatModifierSource("harness", modifiers));
            return stats;
        }

        private WeaponDefinition Weapon(string id) => _content.Items.OfType<WeaponDefinition>().First(w => w.Id == id);

        /// <summary>A depth as generated, without simulating combat: what rooms exist and how big they are.</summary>
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

        // ================= PHASE 11 =================

        [Test]
        public void Phase11_WritesDepthPacing()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Depth,Build,SkillProfile,RoomKind,SizeClass,Count,AvgEnemiesPerRoom,AvgRoomHpTotal,AvgClearSeconds,AvgTraversalSeconds,AvgInteractionSeconds,SecondsShareOfDepth,Notes");
            var perDepth = new List<(int depth, string build, string skill, float total, float combat, float traversal, float loot, float boss, int rooms, int enemies, int deaths, int sampled)>();

            foreach (var depth in FreshRunBalanceRunTests.DepthLadder)
            {
                foreach (var build in Builds())
                {
                    foreach (var skill in SkillProfile.All)
                    {
                        // Three seeds per point so a single unusual layout cannot define the pacing figure.
                        var all = new List<FreshRunSimulator.RunResult>();
                        for (var i = 0; i < 3; i++)
                        {
                            var seed = FreshRunBalanceRunTests.FreshRunSeeds[i];
                            var biome = FreshRunBalanceRunTests.Biomes[i % FreshRunBalanceRunTests.Biomes.Length];
                            all.Add(_simulator.Simulate(build(skill), skill, biome, seed, depth));
                        }

                        var name = all[0].LoadoutName;
                        // A run that ended in a death stops early, so its clock is not a depth duration. Timing is
                        // averaged over the runs that finished the depth; the deaths are reported as their own column
                        // rather than silently shortening the pacing figure.
                        var deaths = all.Count(r => r.Died);
                        var runs = all.Where(r => !r.Died).ToList();
                        if (runs.Count == 0) runs = all;
                        var totals = (
                            combat: (float)runs.Average(r => r.CombatSeconds - r.BossSeconds),
                            boss: (float)runs.Average(r => r.BossSeconds),
                            traversal: (float)runs.Average(r => r.TraversalSeconds),
                            loot: (float)runs.Average(r => r.LootSeconds),
                            rooms: (float)runs.Average(r => r.RoomCount),
                            combatRooms: (float)runs.Average(r => r.CombatRooms),
                            eliteRooms: (float)runs.Average(r => r.EliteRooms),
                            enemies: (float)runs.Average(r => r.EnemiesKilled));
                        var total = totals.combat + totals.boss + totals.traversal + totals.loot;
                        perDepth.Add((depth, name, skill.Name, total, totals.combat, totals.traversal, totals.loot, totals.boss, (int)totals.rooms, (int)totals.enemies, deaths, all.Count));

                        var roomHealth = RoomHealth(depth);
                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), name, skill.Name, "combat (all sizes)", "Small/Medium/Large",
                            F1(totals.combatRooms), F1(totals.enemies / Mathf.Max(1f, totals.combatRooms)), F1(roomHealth),
                            F1(totals.combat / Mathf.Max(1f, totals.combatRooms)), F1(totals.traversal / Mathf.Max(1f, totals.rooms)), "0",
                            F(totals.combat / Mathf.Max(0.01f, total)), Csv("threat budget " + BudgetText(depth))
                        }));
                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), name, skill.Name, "elite", "Medium/Large", F1(totals.eliteRooms), "1",
                            F1((float)_content.Elites.Average(e => DepthScaling.ScaledHealth(e.BaseHealth, depth, 1, false, _content.DepthScaling))),
                            F1(EliteSeconds(build(skill), skill, depth)), "-", "0", "-",
                            Csv($"elite chance {DepthScaling.EliteChancePercent(depth, _content.DepthScaling)}% per eligible combat room")
                        }));
                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), name, skill.Name, "boss", "Boss", "1", "1",
                            F1((float)_content.Bosses.Average(b => DepthScaling.ScaledHealth(b.BaseHealth, depth, 1, true, _content.DepthScaling))),
                            F1(totals.boss), "-", "0", F(totals.boss / Mathf.Max(0.01f, total)), Csv("35% telegraph downtime modelled")
                        }));
                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), name, skill.Name, "loot/merchant/event interaction", "-", "-", "-", "-", "-", "-",
                            F1(totals.loot), F(totals.loot / Mathf.Max(0.01f, total)), Csv("3s a supply chest, 4s a loot chest, 8s the merchant, 6s an event, 5s the boss cache")
                        }));
                        csv.AppendLine(string.Join(",", new[]
                        {
                            depth.ToString(), name, skill.Name, "WHOLE DEPTH", "-", F1(totals.rooms), F1(totals.enemies), F1(roomHealth * totals.combatRooms),
                            F1(total), F1(totals.traversal), F1(totals.loot), "1", Csv($"combat {F(totals.combat / total * 100f)}%, boss {F(totals.boss / total * 100f)}%, walking {F(totals.traversal / total * 100f)}%, interacting {F(totals.loot / total * 100f)}%; {deaths} of {all.Count} sampled runs died before the depth ended and are excluded from these timings")
                        }));
                    }
                }
            }

            File.WriteAllText(Path.Combine(Folder, "depth_pacing.csv"), csv.ToString());

            // The question the pacing table exists to answer, recorded as data rather than prose.
            var summary = new StringBuilder();
            summary.AppendLine("Depth,Build,SkillProfile,TotalSeconds,VsD1,CombatSeconds,BossSeconds,Rooms,Enemies,EnemyHpMultiplier,EnemyDamageMultiplier,SecondsPerEnemy,DeathsSampled,Verdict");
            foreach (var group in perDepth.GroupBy(p => (p.build, p.skill)))
            {
                var d1 = group.First(g => g.depth == 1);
                foreach (var p in group.OrderBy(g => g.depth))
                {
                    var hp = DepthScaling.HealthMultiplier(p.depth, _content.DepthScaling);
                    var dmg = DepthScaling.DamageMultiplier(p.depth, _content.DepthScaling);
                    var lengthRatio = p.total / Mathf.Max(0.01f, d1.total);
                    var verdict = p.depth == 1 ? "baseline"
                        : lengthRatio / Mathf.Max(0.01f, dmg) > 1.35f ? "LONGER_MORE_THAN_DEADLIER" : "length and lethality scale together";
                    summary.AppendLine(string.Join(",", new[]
                    {
                        p.depth.ToString(), p.build, p.skill, F1(p.total), F(lengthRatio), F1(p.combat), F1(p.boss),
                        p.rooms.ToString(), p.enemies.ToString(), F(hp), F(dmg),
                        F(p.total / Mathf.Max(1, p.enemies)), $"{p.deaths}/{p.sampled}", verdict
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "depth_pacing_summary.csv"), summary.ToString());
        }

        private string BudgetText(int depth)
        {
            var (min, max) = ThreatBudgetTable.SoloBudget(depth);
            return $"{F1(min)}-{F1(max)} threat";
        }

        private float RoomHealth(int depth)
        {
            var (min, max) = ThreatBudgetTable.SoloBudget(depth);
            var target = (min + max) * 0.5f;
            var enemies = _content.Enemies.Where(e => e != null && e.ThreatCost > 0f && e.UnlockDepth <= depth).ToList();
            return target * (float)enemies.Average(e => DepthScaling.ScaledHealth(e.BaseHealth, depth, 1, false, _content.DepthScaling) / e.ThreatCost);
        }

        private float EliteSeconds(FreshRunSimulator.Loadout loadout, SkillProfile skill, int depth)
        {
            var elite = EnemyProfile.From(_content.Elites.OrderBy(e => e.Id, StringComparer.Ordinal).First(), depth, _content.DepthScaling);
            return loadout.Primary.TimeToKill(elite.Health) * (1f + ExposureModel.EliteDowntime);
        }

        private List<Func<SkillProfile, FreshRunSimulator.Loadout>> Builds() => new()
        {
            skill => new FreshRunSimulator.Loadout("STARTER",
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.PistolId), Stats(), skill),
                new WeaponProfile(Weapon(RuinRail.Gameplay.Base.StarterKitService.KnifeId), Stats(), skill),
                new Dictionary<AmmoType, int> { [AmmoType.Light] = RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount }, 120, 1),
            skill =>
            {
                var stats = Stats(StatModifier.Percent(StatId.WeaponDamage, 10), StatModifier.Percent(StatId.FireRate, 9),
                    StatModifier.Percent(StatId.ReloadSpeed, 14), StatModifier.Flat(StatId.MaxHealth, 18));
                return new FreshRunSimulator.Loadout("MID_RARE",
                    new WeaponProfile(Weapon("weapon_marauder_a2"), stats, skill),
                    new WeaponProfile(Weapon("weapon_field_knife"), stats, skill),
                    new Dictionary<AmmoType, int> { [AmmoType.Medium] = _content.AmmoBalance.GetStackLimit(AmmoType.Medium) / 2 }, stats.MaxHealth, 2);
            },
            skill =>
            {
                var stats = Stats(StatModifier.Percent(StatId.WeaponDamage, 20), StatModifier.Percent(StatId.FireRate, 9),
                    StatModifier.Percent(StatId.MagazineSize, 20), StatModifier.Percent(StatId.ReloadSpeed, 24), StatModifier.Flat(StatId.MaxHealth, 50));
                return new FreshRunSimulator.Loadout("STRONG_EPIC",
                    new WeaponProfile(Weapon("weapon_vanguard"), stats, skill),
                    new WeaponProfile(Weapon("weapon_railspike"), stats, skill),
                    new Dictionary<AmmoType, int> { [AmmoType.Medium] = _content.AmmoBalance.GetStackLimit(AmmoType.Medium) }, stats.MaxHealth, 3);
            }
        };

        // ================= PHASE 12 =================

        [Test]
        public void Phase12_WritesCoinEconomy()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Scenario,Depth,CoinsEnteringDepth,CoinsFromContainers,CoinsFromBossCache,CoinsFromOtherSources,CoinsEarnedTotal,MerchantPresent,CheapestWeaponPrice,CheapestArmorPrice,AccessoryPrice,AmmoBundlePrice,ConsumablePrice,AffordableOffers,EventCostsEncountered,AffordableEvents,CoinsSpent,CoinsLeavingDepth,CoinsCarriedForward,CoinsSecuredOnExtraction,Notes");
            var cheapestWeapon = _content.Items.OfType<WeaponDefinition>().Min(w => _prices.BuyValue(w, Rarity.Common));
            var cheapestArmor = _content.Items.OfType<ArmorDefinition>().Min(a => _prices.BuyValue(a, Rarity.Common));
            var accessoryPrice = _content.Items.OfType<AccessoryDefinition>().Min(a => _prices.BuyValue(a, Rarity.Common));
            _content.Economy.TryGetAmmoBundle(AmmoType.Light, out var lightBundle);
            var bandage = _content.Items.FirstOrDefault(i => i.Id == "consumable_bandage");
            var bandagePrice = bandage != null ? _prices.BuyValue(bandage, Rarity.Common) : 0;

            foreach (var (scenario, depths) in new (string, int[])[]
            {
                ("D1_EXTRACT", new[] { 1 }),
                ("D1_TO_D2", new[] { 1, 2 }),
                ("D1_TO_D5", new[] { 1, 2, 3, 4, 5 }),
                ("D1_TO_D10", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })
            })
            {
                var carried = 0;
                foreach (var depth in depths)
                {
                    var entering = carried;
                    var seed = FreshRunBalanceRunTests.FreshRunSeeds[depth % FreshRunBalanceRunTests.FreshRunSeeds.Length];
                    var biome = FreshRunBalanceRunTests.Biomes[depth % FreshRunBalanceRunTests.Biomes.Length];
                    var run = _simulator.Simulate(Builds()[0](SkillProfile.Normal), SkillProfile.Normal, biome, seed, depth);
                    var eventPrices = EventPricesFor(depth);
                    var affordable = new List<string>();
                    if (entering + run.CoinsEarned >= lightBundle.Price) affordable.Add("light ammo bundle");
                    if (entering + run.CoinsEarned >= bandagePrice && bandagePrice > 0) affordable.Add("bandage");
                    if (entering + run.CoinsEarned >= cheapestWeapon) affordable.Add("cheapest weapon");
                    if (entering + run.CoinsEarned >= cheapestArmor) affordable.Add("cheapest armor");
                    if (entering + run.CoinsEarned >= accessoryPrice) affordable.Add("accessory");
                    carried = entering + run.CoinsEarned - run.CoinsSpent;

                    csv.AppendLine(string.Join(",", new[]
                    {
                        scenario, depth.ToString(), entering.ToString(),
                        (run.CoinsEarned - BossCacheCoins(seed, depth, biome)).ToString(), BossCacheCoins(seed, depth, biome).ToString(), "0",
                        run.CoinsEarned.ToString(), run.HasMerchant ? "yes" : "no",
                        cheapestWeapon.ToString(), cheapestArmor.ToString(), accessoryPrice.ToString(),
                        lightBundle.Price.ToString(), bandagePrice.ToString(),
                        Csv(affordable.Count == 0 ? "nothing" : string.Join("; ", affordable)),
                        Csv(string.Join("; ", eventPrices.Select(e => $"{e.kind} {e.price}"))),
                        Csv(string.Join("; ", eventPrices.Where(e => entering + run.CoinsEarned >= e.price).Select(e => e.kind.ToString()))),
                        run.CoinsSpent.ToString(), carried.ToString(),
                        depth == depths[^1] ? "0 (extracted)" : carried.ToString(),
                        depth == depths[^1] ? carried.ToString() : "0 (still at risk)",
                        Csv($"seed {seed} {biome}; a lost run forfeits every carried coin")
                    }));
                }
            }

            File.WriteAllText(Path.Combine(Folder, "coin_economy.csv"), csv.ToString());
        }

        private List<(DungeonEventPriceKind kind, int price)> EventPricesFor(int depth) =>
            Enum.GetValues(typeof(DungeonEventPriceKind)).Cast<DungeonEventPriceKind>()
                .Select(k => (k, _prices.EventPrice(k, depth)))
                .Where(t => t.Item2 > 0)
                .ToList();

        private int BossCacheCoins(int seed, int depth, Biome biome)
        {
            if (!_content.Loot.TryGet(LootSourceKind.BossCache, out var source) || source.Table == null) return 0;
            var roll = _roller.Roll(source.Table, LootContext.ForSource(seed, depth, 0, source.Quality, 1, new[] { AmmoType.Light }));
            return roll.Coins;
        }

        // ================= PHASE 13 =================

        [Test]
        public void Phase13_WritesLootRarityCurveFromALargeDeterministicSample()
        {
            var csv = new StringBuilder();
            csv.AppendLine("Depth,Scope,SourceKind,Samples,Common,Uncommon,Rare,Epic,Legendary,CommonPct,UncommonPct,RarePct,EpicPct,LegendaryPct,IntendedCommonPermille,IntendedUncommonPermille,IntendedRarePermille,IntendedEpicPermille,IntendedLegendaryPermille,MatchesIntendedTable");
            var generations = 0;
            var perDepthTotals = new Dictionary<int, int[]>();

            // Per (depth, source), aggregated across all three biomes: the curve a player actually meets.
            foreach (var depth in FreshRunBalanceRunTests.DepthLadder)
            {
                perDepthTotals[depth] = new int[5];
                var counts = new Dictionary<LootSourceKind, int[]>();
                foreach (var kind in Enum.GetValues(typeof(LootSourceKind)).Cast<LootSourceKind>()) counts[kind] = new int[5];

                foreach (var biome in FreshRunBalanceRunTests.Biomes)
                {
                    foreach (var seed in FreshRunBalanceRunTests.LootSeeds)
                    {
                        var generation = Generate(biome, seed, depth);
                        generations++;
                        if (!generation.Success) continue;
                        var graph = generation.Graph;
                        var layout = generation.Layout;
                        var supply = SupplyChestPlanner.Plan(graph, seed, depth, SupplyChestPlanner.OrdinaryRoomPercent, SupplyChestPlanner.MinimumPerDepth);

                        foreach (var node in graph.Nodes.OrderBy(n => n.Id))
                        {
                            var definition = layout.GetPlacement(node.Id)?.Definition;
                            switch (node.Type)
                            {
                                case RoomType.Combat when supply.Contains(node.Id):
                                    Tally(counts, LootSourceKind.SupplyChest, seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + RoomCategoryComposer.SupplyChestSourceSlot);
                                    break;
                                case RoomType.Loot:
                                    for (var i = 0; i < ChestMarkers(definition); i++) Tally(counts, LootSourceKind.EquipmentChest, seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + i);
                                    break;
                                case RoomType.Treasure:
                                    for (var i = 0; i < ChestMarkers(definition); i++) Tally(counts, LootSourceKind.TreasureChest, seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + i);
                                    break;
                                case RoomType.Boss:
                                    Tally(counts, LootSourceKind.BossCache, seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride);
                                    break;
                            }
                        }
                    }
                }

                foreach (var kv in counts.OrderBy(k => k.Key.ToString(), StringComparer.Ordinal))
                {
                    var total = kv.Value.Sum();
                    if (total == 0) continue;
                    for (var i = 0; i < 5; i++) perDepthTotals[depth][i] += kv.Value[i];
                    var quality = _content.Loot.TryGet(kv.Key, out var source) ? source.Quality : LootQuality.Standard;
                    var intended = _content.Loot.RarityTableFor(quality).WeightsAt(depth);
                    csv.AppendLine(Line(depth, "ALL_BIOMES", kv.Key.ToString(), total, kv.Value, intended, Verdict(kv.Value, intended, total)));
                }
            }

            foreach (var depth in FreshRunBalanceRunTests.DepthLadder)
            {
                var t = perDepthTotals[depth];
                var total = t.Sum();
                if (total == 0) continue;
                csv.AppendLine(Line(depth, "ALL_BIOMES", "ALL_SOURCES", total, t, new[] { 0, 0, 0, 0, 0 },
                    "aggregate across every source and biome — what a player actually ends a depth holding"));
            }

            // A separate high-n check of the tables themselves: sampling noise in the sweep above cannot tell a 0.9%
            // Epic rate from a 2% one, so each quality table is also rolled 20 000 times through the real LootRoller.
            foreach (var quality in Enum.GetValues(typeof(LootQuality)).Cast<LootQuality>())
            {
                var kind = Enum.GetValues(typeof(LootSourceKind)).Cast<LootSourceKind>()
                    .FirstOrDefault(k => _content.Loot.TryGet(k, out var s) && s.Quality == quality && s.Table != null);
                if (!_content.Loot.TryGet(kind, out var source) || source.Table == null) continue;

                foreach (var depth in FreshRunBalanceRunTests.DepthLadder)
                {
                    var counts = new Dictionary<LootSourceKind, int[]> { [kind] = new int[5] };
                    for (var i = 0; i < 20000; i++) Tally(counts, kind, 900000 + i, depth, i);
                    var total = counts[kind].Sum();
                    if (total == 0) continue;
                    var intended = _content.Loot.RarityTableFor(quality).WeightsAt(depth);
                    var coverage = LegendaryVariantCoverage(source.Table);
                    // Separate the rarity roll itself from what the table can deliver: RollRarity is the authored
                    // distribution, CreateInstance is where a Legendary without an authored variant becomes an Epic.
                    var rolled = new int[5];
                    for (var i = 0; i < 20000; i++)
                        rolled[(int)_roller.RollRarity(LootContext.ForSource(800000 + i, depth, i, quality, 1, new[] { AmmoType.Light }))]++;
                    csv.AppendLine(Line(depth, "RARITY_ROLL_20K", $"{quality} (RollRarity only)", rolled.Sum(), rolled, intended, Verdict(rolled, intended, rolled.Sum())));
                    var verdict = Verdict(counts[kind], intended, total);
                    var predictedLegendary = intended[4] / 10f * coverage;
                    verdict += coverage >= 0.999f
                        ? " (every eligible equipment entry has an authored LegendaryVariant, so the delivered Legendary rate is the authored one)"
                        : $" — only {F(coverage * 100f)}% of this table's eligible equipment weight has an authored LegendaryVariant, and LootRoller.CreateInstance turns a Legendary roll on an entry without one into an Epic, so the DELIVERED Legendary rate here is {F(predictedLegendary)}% rather than the authored {F(intended[4] / 10f)}%";
                    csv.AppendLine(Line(depth, "TABLE_CHECK_20K", $"{quality} ({kind})", total, counts[kind], intended, verdict));
                }
            }

            // Any affix-roll failure downgrades an item to Common; if the sampling hit any, say so with the count and
            // the distinct messages rather than letting it hide inside the rarity percentages.
            if (_lootWarnings.Count > 0)
            {
                csv.AppendLine();
                csv.AppendLine("# affix-roll failures observed while sampling (each one silently produces a Common item)");
                foreach (var group in _lootWarnings.GroupBy(w => w, StringComparer.Ordinal).OrderByDescending(g => g.Count()))
                    csv.AppendLine($"# {group.Count()}x {group.Key}");
            }
            else
            {
                csv.AppendLine();
                csv.AppendLine("# no affix-roll failure occurred anywhere in the sample");
            }

            File.WriteAllText(Path.Combine(Folder, "loot_rarity_curve.csv"), csv.ToString());
            Assert.GreaterOrEqual(generations, 500, $"the loot sample must be at least 500 depth generations (was {generations})");
        }

        /// <summary>
        /// Share of a table's equipment weight that can actually deliver a Legendary. A Legendary rarity roll on an
        /// entry with no authored LegendaryVariant is turned into an Epic, so this fraction is the ceiling on the
        /// table's real Legendary rate however high the rarity table sets it.
        /// </summary>
        private static float LegendaryVariantCoverage(LootTableDefinition table)
        {
            // Only entries the shipped eligibility filter would actually pick: content held back from V1 acquisition
            // never drops, so counting it would overstate what the table can deliver.
            var entries = table.Rolls.Where(r => r.Entries != null).SelectMany(r => r.Entries)
                .Where(e => e.Item is EquipmentItemDefinition equipment && equipment.IsAcquirableInV1).ToList();
            var total = entries.Sum(e => Mathf.Max(1, e.Weight));
            if (total == 0) return 0f;
            return entries.Where(e => e.LegendaryVariant != null).Sum(e => Mathf.Max(1, e.Weight)) / (float)total;
        }

        private static string Line(int depth, string biome, string kind, int total, int[] counts, int[] intended, string verdict) =>
            string.Join(",", new[]
            {
                depth.ToString(), biome, Csv(kind), total.ToString(),
                counts[0].ToString(), counts[1].ToString(), counts[2].ToString(), counts[3].ToString(), counts[4].ToString(),
                F(counts[0] / (float)total * 100f), F(counts[1] / (float)total * 100f), F(counts[2] / (float)total * 100f),
                F(counts[3] / (float)total * 100f), F(counts[4] / (float)total * 100f),
                intended[0].ToString(), intended[1].ToString(), intended[2].ToString(), intended[3].ToString(), intended[4].ToString(),
                Csv(verdict)
            });

        /// <summary>
        /// Compares the observed split to the authored permille with a tolerance that respects the sample size: a rate
        /// whose expected count is under ten cannot be distinguished from the table by any number of decimal places,
        /// and saying otherwise would be reading noise as a finding.
        /// </summary>
        private static string Verdict(int[] counts, int[] intended, int total)
        {
            var undersampled = new List<int>();
            var off = new List<string>();
            for (var i = 0; i < 5; i++)
            {
                var expected = intended[i] / 1000f * total;
                if (expected < 10f) { undersampled.Add(i); continue; }
                var band = Mathf.Max(3f, 3f * Mathf.Sqrt(expected)); // ~3 standard deviations of a binomial count
                if (Mathf.Abs(counts[i] - expected) > band) off.Add($"{(Rarity)i} {counts[i]} vs {expected:0.#}");
            }

            if (off.Count > 0) return "OFF TABLE: " + string.Join("; ", off);
            return undersampled.Count > 0
                ? $"matches the table where the sample can tell ({string.Join("/", undersampled.Select(i => (Rarity)i))} under-sampled)"
                : "matches the authored table";
        }

        /// <summary>Affix-roll failures the real roller reported while sampling: a failure silently produces a Common.</summary>
        private readonly List<string> _lootWarnings = new();

        private void Tally(Dictionary<LootSourceKind, int[]> counts, LootSourceKind kind, int seed, int depth, int sourceIndex)
        {
            if (!_content.Loot.TryGet(kind, out var source) || source.Table == null) return;
            var roll = _roller.Roll(source.Table, LootContext.ForSource(seed, depth, sourceIndex, source.Quality, 1, new[] { AmmoType.Light }));
            if (roll.Warnings.Count > 0) _lootWarnings.AddRange(roll.Warnings);
            foreach (var item in roll.Items)
            {
                _itemsById.TryGetValue(item.DefinitionId, out var definition);
                if (definition is AmmoItemDefinition || definition is ConsumableDefinition) continue;
                counts[kind][(int)item.Rarity]++;
            }
        }

        private static int ChestMarkers(RoomDefinition definition) =>
            FreshRunSimulator.MarkerCount(definition, RoomMarkerRole.ChestSpawn);

        // ================= PHASE 14 =================

        [Test]
        public void Phase14_WritesDescendValueSnapshot()
        {
            var csv = new StringBuilder();
            csv.AppendLine("AfterDepth,SecuredValueIfReturning,CarriedValueAtRisk,ItemsAtRisk,CoinsAtRisk,ExpectedRarityNow,ExpectedRarityNextDepth,RarityImprovement,ExpectedCoinsNextDepth,ExpectedXpNextDepth,NextDepthEnemyHp,NextDepthEnemyDamage,NextDepthEliteChance,AmmoReserveShare,ConsumablesLeft,EffectiveHpAfterDepthHeal,Notes");
            var standard = _content.Loot.RarityTableFor(LootQuality.Standard);
            var carriedItems = 0;
            var carriedValue = 0;
            var carriedCoins = 0;
            var xp = 0;

            foreach (var depth in new[] { 1, 5, 10, 20, 30 })
            {
                var seed = FreshRunBalanceRunTests.FreshRunSeeds[depth % FreshRunBalanceRunTests.FreshRunSeeds.Length];
                var biome = FreshRunBalanceRunTests.Biomes[depth % FreshRunBalanceRunTests.Biomes.Length];
                var run = _simulator.Simulate(Builds()[1](SkillProfile.Normal), SkillProfile.Normal, biome, seed, depth);
                carriedItems += run.ItemsFound;
                carriedCoins += run.CoinsEarned - run.CoinsSpent;
                carriedValue += run.RarityFound.Sum(kv => kv.Value * AverageItemValue(kv.Key));
                xp += run.EnemiesKilled * 20;

                var next = depth + 1;
                var here = ExpectedRarityScore(standard.WeightsAt(depth));
                var there = ExpectedRarityScore(standard.WeightsAt(next));
                var reserveShare = run.ReserveAfterBoss / Mathf.Max(1f, (float)_content.AmmoBalance.GetStackLimit(AmmoType.Medium));

                csv.AppendLine(string.Join(",", new[]
                {
                    depth.ToString(), (carriedValue + carriedCoins).ToString(), (carriedValue + carriedCoins).ToString(),
                    carriedItems.ToString(), carriedCoins.ToString(),
                    F(here), F(there), Pct(there, here),
                    BossCacheCoins(seed, next, biome).ToString(), xp.ToString(),
                    F(DepthScaling.HealthMultiplier(next, _content.DepthScaling)), F(DepthScaling.DamageMultiplier(next, _content.DepthScaling)),
                    DepthScaling.EliteChancePercent(next, _content.DepthScaling) + "%",
                    F(reserveShare), run.BandagesUsed == 0 ? "unused" : (1 - run.BandagesUsed).ToString(),
                    run.MaxHealth.ToString(),
                    Csv("descending restores full effective HP on arrival; returning secures everything in this row, a death forfeits all of it")
                }));
            }

            File.WriteAllText(Path.Combine(Folder, "descend_value_snapshot.csv"), csv.ToString());
        }

        private int AverageItemValue(Rarity rarity) => _prices.BuyValue(400, rarity);

        /// <summary>Expected rarity of one dropped item on a 0–4 scale, from the authored permille table.</summary>
        private static float ExpectedRarityScore(int[] weights)
        {
            var total = weights.Sum();
            return total == 0 ? 0f : Enumerable.Range(0, weights.Length).Sum(i => i * weights[i]) / (float)total;
        }

        private static string Pct(float value, float baseline) =>
            baseline <= 0.0001f ? "-" : (value / baseline - 1f).ToString("+0.0%;-0.0%;0.0%", System.Globalization.CultureInfo.InvariantCulture);

        // ================= PHASE 15 =================

        [Test]
        public void Phase15_WritesWeaponRoleOverlap()
        {
            var csv = new StringBuilder();
            csv.AppendLine("WeaponId,Class,Acquisition,ComparedWith,ComparedClass,SustainedDpsDelta,BurstDelta,PerShotDamageDelta,RangeDelta,AmmoEfficiencyDelta,MagazineDelta,ReloadDelta,AmmoTypeScarcity,AoE,ImpactDelta,LegendarySpecial,PriceDelta,Classification,Reason");
            var stats = Stats();
            var profiles = _content.Items.OfType<WeaponDefinition>()
                .Select(w => (definition: w, profile: new WeaponProfile(w, stats, SkillProfile.Normal)))
                .OrderBy(t => t.definition.Id, StringComparer.Ordinal).ToList();

            foreach (var (definition, profile) in profiles)
            {
                // Only compare weapons that occupy the same functional space AND the same acquisition tier: same ammo
                // family for firearms, same behaviour kind, and regular-vs-regular. A Legendary-only weapon (one with a
                // LegendaryMechanicId) is meant to beat its class's regular weapons, so including it would flag every
                // regular weapon in the game as dominated and say nothing.
                var regular = RuinRail.Gameplay.Loot.EquipmentRollService.IsRegular(definition);
                var peers = profiles.Where(p => p.definition.Id != definition.Id
                                                && p.profile.Kind == profile.Kind
                                                && RuinRail.Gameplay.Loot.EquipmentRollService.IsRegular(p.definition) == regular
                                                && (!profile.UsesAmmo || p.profile.AmmoType == profile.AmmoType)).ToList();
                var dominator = peers.FirstOrDefault(p => Dominates(p.profile, profile, p.definition, definition));
                var classification = dominator.definition != null
                    // A strictly bigger hit per trigger pull is a real compensating axis that DPS hides: fewer, larger
                    // commitments play differently even when the damage per second is lower. That is a feel question.
                    ? dominator.profile.MaxDamagePerShot < profile.MaxDamagePerShot ? "requires human feel test" : "likely dominated"
                    : peers.Any(p => Overlaps(p.profile, profile)) ? "heavy overlap"
                    : "distinct role";
                var best = dominator.definition ?? peers.OrderByDescending(p => p.profile.SustainedDps).Select(p => p.definition).FirstOrDefault();
                var bestProfile = best == null ? null : profiles.First(p => p.definition.Id == best.Id).profile;

                csv.AppendLine(string.Join(",", new[]
                {
                    definition.Id, definition.WeaponClass.ToString(),
                    FreshRunBalanceWeaponTests.AcquisitionOf(definition),
                    best != null ? best.Id : "-", best != null ? best.WeaponClass.ToString() : "-",
                    bestProfile != null ? Pct(bestProfile.SustainedDps, profile.SustainedDps) : "-",
                    bestProfile != null ? Pct(bestProfile.BurstDps, profile.BurstDps) : "-",
                    bestProfile != null ? Pct(bestProfile.MaxDamagePerShot, profile.MaxDamagePerShot) : "-",
                    bestProfile != null ? Pct(bestProfile.Range, profile.Range) : "-",
                    bestProfile != null ? Pct(bestProfile.DamagePerAmmoUnit, profile.DamagePerAmmoUnit) : "-",
                    bestProfile != null && profile.Magazine > 0 ? Pct(bestProfile.Magazine, profile.Magazine) : "-",
                    bestProfile != null && profile.ReloadSeconds > 0f ? Pct(bestProfile.ReloadSeconds, profile.ReloadSeconds) : "-",
                    profile.UsesAmmo ? Csv($"{profile.AmmoType} cap {_content.AmmoBalance.GetStackLimit(profile.AmmoType)}") : "none",
                    profile.IsAoe ? F(profile.ExplosionRadius) + " tiles" : "no",
                    bestProfile != null ? Pct(bestProfile.Knockback + bestProfile.StaggerPower, profile.Knockback + profile.StaggerPower) : "-",
                    definition is EquipmentItemDefinition eq && !string.IsNullOrEmpty(eq.LegendaryMechanicId) ? eq.LegendaryMechanicId : "none",
                    best != null ? Pct(_prices.BuyValue(best, Rarity.Common), _prices.BuyValue(definition, Rarity.Common)) : "-",
                    classification,
                    Csv(ReasonFor(classification, definition, profile, best, bestProfile))
                }));
            }

            File.WriteAllText(Path.Combine(Folder, "weapon_role_overlap.csv"), csv.ToString());
        }

        /// <summary>
        /// Dominance is only claimed when the other weapon is at least as good on EVERY axis that matters and strictly
        /// better on one, with no compensating role: same ammo family, no worse range, no worse ammo efficiency, no
        /// unique impact or AoE, and not cheaper to acquire in a way that would pay for the gap.
        /// </summary>
        private bool Dominates(WeaponProfile other, WeaponProfile weapon, WeaponDefinition otherDefinition, WeaponDefinition definition)
        {
            if (other.SustainedDps < weapon.SustainedDps * 1.05f) return false;
            if (other.BurstDps < weapon.BurstDps) return false;
            if (other.Range < weapon.Range) return false;
            if (weapon.UsesAmmo && other.DamagePerAmmoUnit < weapon.DamagePerAmmoUnit) return false;
            if (weapon.Magazine > 0 && other.Magazine < weapon.Magazine) return false;
            if (weapon.ReloadSeconds > 0f && other.ReloadSeconds > weapon.ReloadSeconds) return false;
            if (weapon.IsAoe && !other.IsAoe) return false;
            if (weapon.Knockback + weapon.StaggerPower > other.Knockback + other.StaggerPower) return false;
            if (weapon.Pellets > other.Pellets) return false;
            if (_prices.BuyValue(otherDefinition, Rarity.Common) > _prices.BuyValue(definition, Rarity.Common) * 1.5f) return false;
            return true;
        }

        private static bool Overlaps(WeaponProfile other, WeaponProfile weapon) =>
            Mathf.Abs(other.SustainedDps - weapon.SustainedDps) / Mathf.Max(1f, weapon.SustainedDps) < 0.12f
            && Mathf.Abs(other.Range - weapon.Range) / Mathf.Max(1f, weapon.Range) < 0.15f
            && other.AmmoType == weapon.AmmoType;

        private string ReasonFor(string classification, WeaponDefinition definition, WeaponProfile profile, WeaponDefinition best, WeaponProfile bestProfile) =>
            classification switch
            {
                "likely dominated" => $"{best.Id} matches or beats it on sustained DPS, burst, range, ammo efficiency, magazine, reload and impact within 1.5x the price, with no compensating axis",
                "requires human feel test" => $"{best.Id} wins on damage per second, but this weapon lands a strictly bigger hit per trigger pull; whether fewer, larger commitments play better is not decidable from the numbers",
                "heavy overlap" => "another weapon of the same class and ammo type sits within 12% DPS and 15% range; the difference is feel, not function",
                _ => DistinctReason(definition, profile)
            };

        private string DistinctReason(WeaponDefinition definition, WeaponProfile profile)
        {
            if (!profile.UsesAmmo && profile.Kind == WeaponKind.Melee) return "zero reserve cost at contact range";
            if (!profile.UsesAmmo) return "no reserve ammo, paid for in heat downtime";
            if (profile.IsAoe) return "area damage and heavy impact";
            if (profile.Pellets > 1) return "cone damage plus knockback and stagger";
            if (profile.Range >= 18f) return "reach well beyond every other class";
            if (profile.Knockback > 0f) return "impact identity no peer shares";
            return "distinct cadence/magazine/ammo-efficiency position in its class";
        }

        // ================= PHASE 16 =================

        [Test]
        public void Phase16_WritesTheHumanPlaytestChecklist()
        {
            var checklist = @"# RUINRAIL — human playtest checklist (fresh-run balance pass)

Each item is a diagnostic the automated pass could not settle. Answer from a real session, not from memory of an
older build. The measured prediction is given so you can say whether the game agrees with the model.

Play a **fresh profile** for items 1–9. Use an existing account for 10–20.

## First run (fresh profile, Depth 1)

1. **Did you run the P9 dry before the boss room?** If yes: which room number, and had you found a Supply Chest yet?
   *Model predicts:* yes for a 55%-accuracy player on most seeds; no for a 92%-accuracy player. See `fresh_profile_runs.csv`.
2. **When you first switched to the Field Knife, was it a choice or a necessity?** If forced, were you at zero Light
   ammo or just conserving?
3. **Did the knife feel like a fallback or like the better weapon?** Specifically: did any part of Depth 1 feel easier
   with the knife than with the pistol?
4. **How many bandages did you want and how many did you have?** The starter kit grants exactly one.
5. **Did the boss fight end because you killed it or because you ran out of resources and retried?**
6. **At the boss, did you have ammo left, and did you finish it with the firearm or the knife?**
7. **Did Depth 1 feel like it had enough loot to justify the risk of pushing to Depth 2?**
8. **Was the first merchant (if the seed had one) affordable?** What could you actually buy?
9. **Did you die on Depth 1?** If yes, to what — chip damage across rooms, one burst, or the boss?

## Weapons and rarity (any account state)

10. **Pick up a Rare or Epic version of a weapon you already have in Common. Did it feel meaningfully different, or
    only numerically different?** *Model predicts:* +8–15% sustained DPS and a visibly larger magazine, but no change
    in how the weapon plays. See `rarity_affix_effect.csv`.
11. **Shotgun knockback: did shoving an enemy back actually buy you space, or did they close again before it mattered?**
12. **Spear knockback vs knife: did the 2-tile shove change how safe heavy melee felt?**
13. **Rocket: did the splash ever hit enough enemies to justify the Shells cost, or did you fire it at single targets?**
14. **Blaster: did fighting with no ammo cost feel like an advantage, or did the overheat lockout make it feel worse
    than a rifle?** Did you ever avoid firing to stay under the heat cap?
15. **Sniper: did the reach let you handle anything the AR could not, or did you just fight at the same distance anyway?**
16. **Bow: did the charge time ever cost you a fight, and did a full draw feel worth waiting for?**
17. **Was there a weapon you picked up and immediately knew you would not use?** Which, and what told you that?

## Depth and pacing

18. **Compare a Depth 10 room to a Depth 1 room: was it harder, or the same fight for longer?**
    *Model predicts:* longer. See `depth_pacing_summary.csv`.
19. **At Depth 20, did enemies feel like they were absorbing damage without threatening you more?**
20. **Did any deep-depth boss fight become mostly waiting for openings rather than a fight?** Which boss, which depth?
21. **At which depth did you first feel like the merchant was worth visiting?**
22. **When you chose to descend rather than return, what was the actual reason?** Rarity, coins, XP, or momentum?
23. **When you chose to return, what specifically made you stop?**
24. **Did carrying coins deeper ever feel like a real gamble, or were the amounts too small to care about?**
25. **Did the full heal on arriving at a new depth make descending feel safe enough to be automatic?**
";
            File.WriteAllText(Path.Combine(Folder, "HUMAN_PLAYTEST_CHECKLIST.md"), checklist);
            Assert.GreaterOrEqual(checklist.Split('\n').Count(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d+\. ")), 15, "15-25 targeted observations");
        }
    }
}
