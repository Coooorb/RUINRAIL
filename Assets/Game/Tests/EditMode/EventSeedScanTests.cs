using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// Representative generated-depth sweep across the three biomes (57/55): for run seeds 1..80 the first two depths
    /// are generated through the real pipeline and their special rooms and seeded event kinds are listed, so the
    /// built-player smoke and the live proof drive seeds that provably place a Broken Machine (and every other kind)
    /// on the play path instead of hoping. Written to the proof folder as evidence.
    /// </summary>
    public sealed class EventSeedScanTests
    {
        public const string ScanPath = NonCombatRoomMatrixTestsPaths.Folder + "/event_seed_scan.txt";

        public sealed class DepthSummary
        {
            public int Seed;
            public int Depth;
            public RuinRail.Core.Biome Biome;
            public readonly List<string> Specials = new();
            public readonly List<DungeonEventKind> Events = new();
        }

        public static List<DepthSummary> Scan(int fromSeed, int toSeed, int depths)
        {
            var content = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(content.Rooms);
            var rules = DungeonGraphRules.CreateDefault();
            var generator = new DungeonGraphGenerator(rules);
            var result = new List<DepthSummary>();
            for (var seed = fromSeed; seed <= toSeed; seed++)
            {
                var biomes = BiomeSelector.Sequence(seed, depths);
                for (var depth = 1; depth <= depths; depth++)
                {
                    var generation = DungeonGenerationPipeline.Generate(generator, pools.PoolFor(biomes[depth - 1]), seed, depth);
                    var summary = new DepthSummary { Seed = seed, Depth = depth, Biome = biomes[depth - 1] };
                    if (generation.Success)
                    {
                        foreach (var placement in generation.Layout.Placements.OrderBy(p => p.NodeId))
                        {
                            var type = placement.Definition.RoomType;
                            if (type == RoomType.Combat || type == RoomType.Start) continue;
                            summary.Specials.Add(type + ":" + placement.Definition.Id);
                            if (type == RoomType.Event) summary.Events.Add(RoomCategoryComposer.ResolveEventKind(placement.Definition.Tags, seed, depth, placement.NodeId));
                        }
                    }

                    result.Add(summary);
                }
            }

            Object.DestroyImmediate(rules);
            return result;
        }

        [Test]
        public void EveryEventKind_AppearsOnThePlayPathOfSomeSeed_AndTheScanIsWritten()
        {
            Directory.CreateDirectory(NonCombatRoomMatrixTestsPaths.Folder);
            var scan = Scan(1, 80, 2);
            var sb = new StringBuilder();
            sb.AppendLine("# generated-depth sweep: seeds 1..80, depths 1-2 (real pipeline, real pools); special rooms and seeded event kinds");
            foreach (var d in scan) sb.AppendLine($"seed {d.Seed} depth {d.Depth} {d.Biome}: {string.Join(", ", d.Specials)} | events: {string.Join(", ", d.Events)}");
            var perKind = new Dictionary<DungeonEventKind, List<int>>();
            foreach (var kind in RoomCategoryComposer.RandomEventKinds) perKind[kind] = scan.Where(d => d.Events.Contains(kind)).Select(d => d.Seed).Distinct().ToList();
            sb.AppendLine();
            foreach (var (kind, seeds) in perKind.Select(kv => (kv.Key, kv.Value))) sb.AppendLine($"{kind}: seeds {string.Join(" ", seeds.Take(20))}");
            var brokenOnDepth1 = scan.Where(d => d.Depth == 1 && d.Events.Contains(DungeonEventKind.BrokenMachine)).Select(d => d.Seed).ToList();
            sb.AppendLine($"BrokenMachine on depth 1: seeds {string.Join(" ", brokenOnDepth1)}");
            var allBiomes = scan.Select(d => d.Biome).Distinct().Count();
            sb.AppendLine($"biomes covered: {allBiomes} of 3");
            File.WriteAllText(ScanPath, sb.ToString());
            Assert.AreEqual(3, allBiomes, "the sweep crosses all three biomes");
            foreach (var kind in RoomCategoryComposer.RandomEventKinds) Assert.IsNotEmpty(perKind[kind], kind + " never appears on a generated depth");
            Assert.IsNotEmpty(brokenOnDepth1, "a Broken Machine reaches depth 1 of some seed");
            Assert.IsTrue(scan.All(d => d.Specials.Count > 0), "every generated depth has special rooms");
        }
    }

    /// <summary>Shared proof-folder path (mirrors the PlayMode matrix test's constant, which this assembly cannot reference).</summary>
    public static class NonCombatRoomMatrixTestsPaths
    {
        public const string Folder = "TestResults/DepthSettingsDescriptionsNonCombatProof";
    }
}
