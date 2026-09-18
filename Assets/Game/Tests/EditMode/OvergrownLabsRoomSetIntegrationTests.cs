using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Rooms;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 123: the complete Overgrown Labs set feeds the biome pool with exact counts, every seeded graph + assembly
    /// across many seeds/depths succeeds and validates, boss/utility rooms only fill their own slots, and a room-set
    /// validation summary is written.
    /// </summary>
    public class OvergrownLabsRoomSetIntegrationTests
    {
        private const string SummaryPath = "TestResults/roomset_overgrown_labs_summary.md";

        private readonly List<Object> _created = new();
        private DungeonGraphGenerator _generator;
        private RoomPool _pool;

        private static readonly (RoomType type, RoomSizeClass? size, int count)[] ApprovedDistribution =
        {
            (RoomType.Start, null, 2),
            (RoomType.Combat, RoomSizeClass.Small, 5),
            (RoomType.Combat, RoomSizeClass.Medium, 4),
            (RoomType.Combat, RoomSizeClass.Large, 2),
            (RoomType.Merchant, null, 1),
            (RoomType.Event, null, 2),
            (RoomType.Loot, null, 1),
            (RoomType.Treasure, null, 1),
            (RoomType.MedicalRecovery, null, 1),
            (RoomType.Boss, null, 2)
        };

        [SetUp]
        public void SetUp()
        {
            var rules = DungeonGraphRules.CreateDefault();
            _created.Add(rules);
            _generator = new DungeonGraphGenerator(rules);
            var labs = RoomValidationTools.LoadAllRoomDefinitions().Where(d => UnityEditor.AssetDatabase.GetAssetPath(d).StartsWith(OvergrownLabsRoomSet.DefinitionFolder)).ToList();
            _pool = RoomPool.Build(labs, Biome.OvergrownLabs);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        // ---- Acceptance 1 + 3: exact pool distribution, only validator-passing rooms ----

        [Test]
        public void LabsPool_HasExactlyTwentyOneRooms_WithTheApprovedDistribution_AndNoRejects()
        {
            Assert.IsEmpty(_pool.Rejected.Select(r => r.RoomId + ": " + string.Join("; ", r.Problems)), "Every authored room is validator-passing.");
            Assert.AreEqual(21, _pool.Rooms.Count);
            foreach (var (type, size, count) in ApprovedDistribution)
            {
                var actual = _pool.Rooms.Count(r => r.RoomType == type && (size == null || r.SizeClass == size));
                Assert.AreEqual(count, actual, $"{type}{(size != null ? " " + size : string.Empty)}");
            }

            Assert.AreEqual(21, _pool.Rooms.Select(r => r.Id).Distinct().Count(), "Stable unique ids.");
            Assert.IsTrue(_pool.Rooms.All(r => r.Biome == Biome.OvergrownLabs && r.Prefab != null && r.SupportedDoors.Count > 0));
        }

        [Test]
        public void InvalidRoom_NeverEntersThePool()
        {
            var broken = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(broken);
            typeof(RoomDefinition).GetField("_id", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(broken, "labs_broken_99");
            typeof(RoomDefinition).GetField("_biome", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(broken, Biome.OvergrownLabs);
            typeof(RoomDefinition).GetField("_roomType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(broken, RoomType.Combat);
            var pool = RoomPool.Build(_pool.Rooms.Append(broken), Biome.OvergrownLabs);
            Assert.AreEqual(21, pool.Rooms.Count);
            Assert.AreEqual(1, pool.Rejected.Count);
            Assert.AreEqual("labs_broken_99", pool.Rejected[0].RoomId);
        }

        [Test]
        public void DuplicateRoomId_ExcludesEveryDefinitionSharingIt_WithAClearReport()
        {
            var original = _pool.Rooms.First(r => r.RoomType == RoomType.Combat && r.SizeClass == RoomSizeClass.Small);
            var duplicate = Object.Instantiate(original);
            _created.Add(duplicate);
            var pool = RoomPool.Build(_pool.Rooms.Append(duplicate), Biome.OvergrownLabs);
            Assert.AreEqual(20, pool.Rooms.Count, "Both definitions sharing the id are excluded, never picked silently.");
            Assert.IsFalse(pool.Rooms.Any(r => r.Id == original.Id));
            Assert.AreEqual(2, pool.Rejected.Count(r => r.RoomId == original.Id));
            StringAssert.Contains("duplicate stable room id", pool.Rejected.First(r => r.RoomId == original.Id).Problems[0], "RoomValidator.ValidateAll reports duplicates across the set.");
            Assert.IsEmpty(RoomPool.Build(_pool.Rooms, Biome.OvergrownLabs).Rejected, "The authored set itself has no duplicates.");
        }

        // ---- Acceptance 2 + 4: many seeds/depths assemble and validate; slots respect categories ----

        [Test]
        public void ManySeedsAndDepths_AssembleValidDungeons_WithCategorySlotsRespected_AndBoundedReuse()
        {
            var seeds = Enumerable.Range(1, 40).ToArray();
            var depths = new[] { 1, 2, 5, 10, 25 };
            var attempts = 0;
            var reuseTotal = 0;
            var maxReuse = 0;
            var maxRooms = 0;
            var summary = new StringBuilder();
            summary.AppendLine("# Overgrown Labs room-set validation summary");
            summary.AppendLine();
            summary.AppendLine("## Pool");
            summary.AppendLine();
            summary.AppendLine("| ID | Type | Size | Doors | Elite | Weight |");
            summary.AppendLine("|---|---|---|---|---|---:|");
            foreach (var room in _pool.Rooms.OrderBy(r => r.Id))
            {
                summary.AppendLine($"| {room.Id} | {room.RoomType} | {room.SizeClass} | {string.Join("/", room.SupportedDoors)} | {room.SupportsElite} | {room.SelectionWeight:0.0} |");
            }

            var regenerated = new List<string>();
            foreach (var depth in depths)
            foreach (var seed in seeds)
            {
                var generation = DungeonGenerationPipeline.Generate(_generator, _pool, seed, depth);
                Assert.IsTrue(generation.Success, $"seed {seed} depth {depth}: {generation.Error}\n{string.Join("\n", generation.Discarded)}");
                Assert.LessOrEqual(generation.Rounds, 3, $"seed {seed} depth {depth}: needed {generation.Rounds} rounds - the pool should satisfy most graphs first time.");
                if (generation.Rounds > 1) regenerated.Add($"seed {seed} depth {depth}: {generation.Rounds} rounds");
                var graph = generation.Graph;
                var layout = generation.Layout;
                Assert.IsEmpty(DungeonLayoutValidator.Validate(layout));
                attempts++;

                foreach (var placement in layout.Placements)
                {
                    var node = graph.GetNode(placement.NodeId);
                    Assert.AreEqual(node.Type, placement.Definition.RoomType, $"seed {seed} depth {depth}: node {placement.NodeId} slot {node.Type} got {placement.Definition.Id}");
                    if (placement.Definition.RoomType == RoomType.Boss) Assert.AreEqual(graph.BossId, placement.NodeId, "Boss arenas only fill the Boss node.");
                    if (placement.Definition.RoomType == RoomType.Start) Assert.AreEqual(graph.StartId, placement.NodeId);
                }

                Assert.AreEqual(1, layout.Placements.Count(p => p.Definition.RoomType == RoomType.Boss));
                Assert.AreEqual(1, layout.Placements.Count(p => p.Definition.RoomType == RoomType.Start));
                reuseTotal += layout.ReusedRoomIds.Count;
                maxReuse = Mathf.Max(maxReuse, layout.ReusedRoomIds.Count);
                maxRooms = Mathf.Max(maxRooms, layout.Placements.Count);
                Assert.LessOrEqual(layout.ReusedRoomIds.Count, Mathf.Max(2, layout.Placements.Count / 3), $"seed {seed} depth {depth}: excessive duplicate reuse ({string.Join(",", layout.ReusedRoomIds)})");
            }

            Assert.AreEqual(seeds.Length * depths.Length, attempts);
            Assert.LessOrEqual(regenerated.Count, attempts / 20, "At most 5% of dungeons may need a discard-and-regenerate round.");
            summary.AppendLine();
            summary.AppendLine("## Seeded generation (graph -> assembly -> validation, 53 discard-and-regenerate on failure)");
            summary.AppendLine();
            summary.AppendLine($"- Seeds: {seeds.Length} x depths {string.Join(",", depths)} = {attempts} dungeons generated and layout-validated: PASS");
            summary.AppendLine($"- Regenerated rounds: {regenerated.Count} of {attempts} ({string.Join("; ", regenerated)})");
            summary.AppendLine($"- Largest dungeon: {maxRooms} rooms; rooms reused within one dungeon: max {maxReuse}, average {(float)reuseTotal / attempts:0.00}");
            summary.AppendLine("- Boss arenas only on the Boss node, Start rooms only on the Start node, utility rooms only in their category slots: PASS");
            summary.AppendLine();
            summary.AppendLine($"Result: PASS (21 rooms, 0 rejected)");
            Directory.CreateDirectory(Path.GetDirectoryName(SummaryPath) ?? ".");
            File.WriteAllText(SummaryPath, summary.ToString());
        }

        [Test]
        public void Assembly_IsDeterministic_ForTheSameSeedAndDepth()
        {
            var a = DungeonGenerationPipeline.Generate(_generator, _pool, 7, 3);
            var b = DungeonGenerationPipeline.Generate(_generator, _pool, 7, 3);
            Assert.IsTrue(a.Success && b.Success);
            Assert.AreEqual(a.Rounds, b.Rounds);
            Assert.AreEqual(a.Layout.Signature(), b.Layout.Signature());
            var c = DungeonGenerationPipeline.Generate(_generator, _pool, 8, 3);
            Assert.AreNotEqual(a.Layout.Signature(), c.Layout.Signature());

            // Round 1 is the plain single-shot generation (same streams), so existing seed contracts hold.
            var single = DungeonAssembler.ForRun(_pool, 7, 3).Assemble(_generator.Generate(7, 3).Graph);
            if (a.Rounds == 1) Assert.AreEqual(single.Layout.Signature(), a.Layout.Signature());
        }
    }
}
