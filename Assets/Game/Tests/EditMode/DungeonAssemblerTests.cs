using RuinRail.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.EditorTools.Rooms;
using UnityEngine;

namespace RuinRail.Tests
{
    public class DungeonAssemblerTests
    {
        private readonly List<Object> _created = new();
        private DungeonGraphRules _rules;
        private DungeonGraphGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _rules = DungeonGraphRules.CreateDefault();
            _created.Add(_rules);
            _generator = new DungeonGraphGenerator(_rules);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        /// <summary>Synthetic 16x12 room: definition + RoomRoot prefab-like object with sockets in the given directions.</summary>
        private RoomDefinition SyntheticRoom(string id, RoomType type, Biome biome = Biome.RuinedMetro, bool supportsElite = false, int minDepth = 1, int maxDepth = 0, params DoorDirection[] doors)
        {
            if (doors.Length == 0) doors = new[] { DoorDirection.North, DoorDirection.South, DoorDirection.East, DoorDirection.West };
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            definition.name = id;
            _created.Add(definition);
            Set(definition, "_id", id);
            Set(definition, "_biome", biome);
            Set(definition, "_roomType", type);
            Set(definition, "_sizeClass", RoomSizeClass.Small);
            Set(definition, "_dimensions", new Vector2Int(16, 12));
            Set(definition, "_supportsElite", supportsElite);
            Set(definition, "_minDepth", minDepth);
            Set(definition, "_maxDepth", maxDepth);
            Set(definition, "_supportedDoors", doors);

            var grid = RoomGridBuilder.CreateRoomGrid(id);
            _created.Add(grid.gameObject);
            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            foreach (var door in doors)
            {
                var socket = new GameObject($"Door_{door}").AddComponent<DoorSocket>();
                socket.transform.SetParent(grid.transform, false);
                var cell = door switch
                {
                    DoorDirection.North => new Vector2Int(7, 11),
                    DoorDirection.South => new Vector2Int(7, 0),
                    DoorDirection.East => new Vector2Int(15, 5),
                    _ => new Vector2Int(0, 5)
                };
                socket.Configure(door, cell);
            }

            Set(definition, "_prefab", grid.gameObject);
            return definition;
        }

        private RoomPool RichPool(Biome biome = Biome.RuinedMetro)
        {
            // Realistic variety: dead-end Start/Boss rooms, 2â€“3 socket optional rooms, combat rooms from straight
            // corridors to four-way hubs (all 16x12 with centred sockets).
            var n = DoorDirection.North; var so = DoorDirection.South; var e = DoorDirection.East; var w = DoorDirection.West;
            var rooms = new List<RoomDefinition>
            {
                SyntheticRoom("m_start_01", RoomType.Start, biome, doors: new[] { e }),
                SyntheticRoom("m_start_02", RoomType.Start, biome, doors: new[] { n, e }),
                SyntheticRoom("m_boss_01", RoomType.Boss, biome, doors: new[] { w }),
                SyntheticRoom("m_boss_02", RoomType.Boss, biome, doors: new[] { so }),
                SyntheticRoom("m_merchant_01", RoomType.Merchant, biome, doors: new[] { n, so }),
                SyntheticRoom("m_event_01", RoomType.Event, biome, doors: new[] { e, w }),
                SyntheticRoom("m_event_02", RoomType.Event, biome, doors: new[] { n, so, e }),
                SyntheticRoom("m_loot_01", RoomType.Loot, biome, doors: new[] { n, so }),
                SyntheticRoom("m_loot_02", RoomType.Loot, biome, doors: new[] { e, w }),
                SyntheticRoom("m_treasure_01", RoomType.Treasure, biome, doors: new[] { so, e }),
                SyntheticRoom("m_medical_01", RoomType.MedicalRecovery, biome, doors: new[] { n, w })
            };
            var combatShapes = new[]
            {
                new[] { e, w }, new[] { n, so }, new[] { e, w, n }, new[] { e, w, so }, new[] { n, so, e },
                new[] { n, so, w }, new[] { n, so, e, w }, new[] { n, so, e, w }, new[] { n, e, so, w }
            };
            for (var i = 1; i <= combatShapes.Length; i++)
            {
                rooms.Add(SyntheticRoom($"m_combat_{i:00}", RoomType.Combat, biome, supportsElite: true, doors: combatShapes[i - 1]));
            }

            return RoomPool.FromTrusted(rooms, biome);
        }

        // ---- Acceptance 1: determinism ----

        [Test]
        public void SameGraphPoolAndSeed_ProduceSameRoomIdsAndPlacements()
        {
            var pool = RichPool();
            var graph = _generator.Generate(2024, 5).Graph;

            var a = new DungeonAssembler(pool, new SeededRandom(77)).Assemble(graph);
            var b = new DungeonAssembler(pool, new SeededRandom(77)).Assemble(graph);
            var c = new DungeonAssembler(pool, new SeededRandom(78)).Assemble(graph);

            Assert.IsTrue(a.Success, a.Error);
            Assert.IsTrue(b.Success, b.Error);
            Assert.AreEqual(a.Layout.Signature(), b.Layout.Signature());
            Assert.AreEqual(a.PlacementAttempts, b.PlacementAttempts);
            Assert.IsTrue(c.Success, c.Error);
            Assert.AreNotEqual(a.Layout.Signature(), c.Layout.Signature(), "Assembly seed changes room choice/placement.");

            var viaRun = DungeonAssembler.ForRun(pool, 2024, 5).Assemble(graph);
            var viaRunAgain = DungeonAssembler.ForRun(pool, 2024, 5).Assemble(graph);
            Assert.AreEqual(viaRun.Layout.Signature(), viaRunAgain.Layout.Signature());
        }

        [Test]
        public void AssemblyStream_IsSeparateFromGraphStream()
        {
            var pool = RichPool();
            var graph = _generator.Generate(11, 3).Graph;
            var dungeonStream = RngStreams.Derive(11, 3, RngStream.Dungeon);
            for (var i = 0; i < 100; i++) dungeonStream.NextInt(10);
            Assert.AreNotEqual(RngStreams.Derive(11, 3, RngStream.Assembly).Seed, RngStreams.Derive(11, 3, RngStream.Dungeon).Seed);

            var first = DungeonAssembler.ForRun(pool, 11, 3).Assemble(graph).Layout.Signature();
            var second = DungeonAssembler.ForRun(pool, 11, 3).Assemble(graph).Layout.Signature();
            Assert.AreEqual(first, second);
            Assert.AreEqual(_generator.Generate(11, 3).Graph.Signature(), graph.Signature(), "Assembling never perturbs the graph stream.");
        }

        // ---- Acceptance 2 + 3: invariants across many seeds ----

        [Test]
        public void ManySeeds_NoOverlap_EveryEdgeConnected_AllReachable()
        {
            var pool = RichPool();
            var dungeons = 0;
            var dungeonsWithReuse = 0;
            foreach (var depth in new[] { 1, 4, 9, 20 })
            {
                for (var seed = 0; seed < 25; seed++)
                {
                    var graph = _generator.Generate(seed * 17 + depth, depth).Graph;
                    var result = DungeonAssembler.ForRun(pool, seed * 17 + depth, depth).Assemble(graph);
                    Assert.IsTrue(result.Success, $"depth {depth} seed {seed}: {result.Error}");
                    var layout = result.Layout;

                    Assert.IsEmpty(DungeonLayoutValidator.Validate(layout), $"depth {depth} seed {seed}");
                    Assert.AreEqual(graph.Nodes.Count, layout.Placements.Count);
                    var bounds = layout.Placements.Select(p => p.Bounds).ToList();
                    for (var i = 0; i < bounds.Count; i++)
                        for (var j = i + 1; j < bounds.Count; j++)
                            Assert.IsFalse(bounds[i].Overlaps(bounds[j]), $"depth {depth} seed {seed}: rooms overlap");

                    Assert.AreEqual(graph.Edges.Count, layout.Connections.Count, "Exactly one connection per graph edge.");
                    foreach (var (a, b) in graph.Edges)
                    {
                        var c = layout.Connections.Single(x => x.Joins(a, b));
                        Assert.AreEqual(DoorDirections.Opposite(c.SocketA), c.SocketB);
                    }

                    var reachable = DungeonLayoutValidator.ReachableThroughConnections(layout, graph.StartId);
                    Assert.AreEqual(graph.Nodes.Count, reachable.Count);
                    Assert.IsTrue(reachable.Contains(graph.BossId));
                    Assert.AreEqual(RoomType.Start, layout.StartPlacement.Definition.RoomType);
                    Assert.AreEqual(RoomType.Boss, layout.BossPlacement.Definition.RoomType);
                    foreach (var node in graph.Nodes.Where(n => n.IsElite))
                        Assert.IsTrue(layout.GetPlacement(node.Id).Definition.SupportsElite);
                    if (layout.ReusedRoomIds.Count > 0) dungeonsWithReuse++;
                    foreach (var reused in layout.ReusedRoomIds)
                        Assert.Greater(layout.Placements.Count(p => p.Definition.Id == reused), 1, "Flagged ids really are used more than once.");
                    foreach (var id in layout.Placements.GroupBy(p => p.Definition.Id).Where(g => g.Count() > 1).Select(g => g.Key))
                        CollectionAssert.Contains(layout.ReusedRoomIds, id, "Every duplicate use is flagged.");
                    dungeons++;
                }
            }

            Assert.Less(dungeonsWithReuse, dungeons / 4, $"Reuse is the exception with a sufficient pool ({dungeonsWithReuse}/{dungeons}).");
        }

        [Test]
        public void SocketAlignment_PlacesNeighbourAtExactIntegerOffset()
        {
            var pool = RichPool();
            var graph = _generator.Generate(5, 1).Graph;
            var layout = DungeonAssembler.ForRun(pool, 5, 1).Assemble(graph).Layout;

            foreach (var c in layout.Connections)
            {
                var pa = layout.GetPlacement(c.NodeA);
                var pb = layout.GetPlacement(c.NodeB);
                var delta = pb.Offset - pa.Offset;
                var expected = c.SocketA switch
                {
                    DoorDirection.North => new Vector2Int(0, 12),
                    DoorDirection.South => new Vector2Int(0, -12),
                    DoorDirection.East => new Vector2Int(16, 0),
                    _ => new Vector2Int(-16, 0)
                };
                Assert.AreEqual(expected, delta, "Same-size rooms with centred sockets sit exactly one room apart.");
            }
        }

        // ---- Acceptance 4: clear failures ----

        [Test]
        public void PoolWithoutBossRoom_FailsNamingTheMissingCategory()
        {
            var rooms = new List<RoomDefinition> { SyntheticRoom("s", RoomType.Start) };
            for (var i = 0; i < 9; i++) rooms.Add(SyntheticRoom($"c{i}", RoomType.Combat, supportsElite: true));
            rooms.Add(SyntheticRoom("mer", RoomType.Merchant));
            rooms.Add(SyntheticRoom("ev", RoomType.Event));
            rooms.Add(SyntheticRoom("lo", RoomType.Loot));
            rooms.Add(SyntheticRoom("tr", RoomType.Treasure));
            rooms.Add(SyntheticRoom("md", RoomType.MedicalRecovery));
            var pool = RoomPool.FromTrusted(rooms, Biome.RuinedMetro);

            var result = DungeonAssembler.ForRun(pool, 1, 1).Assemble(_generator.Generate(1, 1).Graph);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Layout);
            StringAssert.Contains("no generator-ready room for: Boss", result.Error);
        }

        [Test]
        public void EliteNodeWithoutEliteCapableRoom_FailsExplicitly()
        {
            var pool = RichPool();
            var graph = _generator.Generate(3, 1).Graph;
            graph.GetNode(graph.MainPath[2]).IsElite = true;
            var noElitePool = RoomPool.FromTrusted(pool.Rooms.Select(r =>
            {
                if (r.RoomType == RoomType.Combat) Set(r, "_supportsElite", false);
                return r;
            }), Biome.RuinedMetro);

            var result = DungeonAssembler.ForRun(noElitePool, 3, 1).Assemble(graph);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Combat (elite-capable)", result.Error);
        }

        [Test]
        public void IncompatibleSockets_FailAfterBoundedSearch_WithDiagnostic()
        {
            var rooms = new List<RoomDefinition>
            {
                SyntheticRoom("s", RoomType.Start, doors: DoorDirection.North),
                SyntheticRoom("b", RoomType.Boss, doors: DoorDirection.North),
                SyntheticRoom("mer", RoomType.Merchant, doors: DoorDirection.North),
                SyntheticRoom("ev", RoomType.Event, doors: DoorDirection.North),
                SyntheticRoom("lo", RoomType.Loot, doors: DoorDirection.North),
                SyntheticRoom("tr", RoomType.Treasure, doors: DoorDirection.North),
                SyntheticRoom("md", RoomType.MedicalRecovery, doors: DoorDirection.North)
            };
            for (var i = 0; i < 9; i++) rooms.Add(SyntheticRoom($"c{i}", RoomType.Combat, supportsElite: true, doors: DoorDirection.North));
            var pool = RoomPool.FromTrusted(rooms, Biome.RuinedMetro);

            var result = new DungeonAssembler(pool, new SeededRandom(1), maxPlacementAttempts: 200).Assemble(_generator.Generate(1, 1).Graph);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("Could not place all", result.Error);
            StringAssert.Contains("socket/overlap constraints", result.Error);
            Assert.LessOrEqual(result.PlacementAttempts, 200);
        }

        [Test]
        public void DepthGatedRooms_AreExcluded_AndReportedWhenNothingRemains()
        {
            var pool = RichPool();
            foreach (var boss in pool.Rooms.Where(r => r.RoomType == RoomType.Boss)) Set(boss, "_minDepth", 10);

            var shallow = DungeonAssembler.ForRun(pool, 1, 2).Assemble(_generator.Generate(1, 2).Graph);
            Assert.IsFalse(shallow.Success);
            StringAssert.Contains("Boss", shallow.Error);

            var deep = DungeonAssembler.ForRun(pool, 1, 12).Assemble(_generator.Generate(1, 12).Graph);
            Assert.IsTrue(deep.Success, deep.Error);
        }

        // ---- Requirement 3: controlled reuse ----

        [Test]
        public void SmallPool_ReusesRooms_AndFlagsThem_LargePoolDoesNot()
        {
            var rooms = new List<RoomDefinition>
            {
                SyntheticRoom("s", RoomType.Start),
                SyntheticRoom("b", RoomType.Boss),
                SyntheticRoom("c_only", RoomType.Combat, supportsElite: true),
                SyntheticRoom("mer", RoomType.Merchant),
                SyntheticRoom("ev", RoomType.Event),
                SyntheticRoom("ev2", RoomType.Event),
                SyntheticRoom("lo", RoomType.Loot),
                SyntheticRoom("lo2", RoomType.Loot),
                SyntheticRoom("tr", RoomType.Treasure),
                SyntheticRoom("md", RoomType.MedicalRecovery)
            };
            var tinyPool = RoomPool.FromTrusted(rooms, Biome.RuinedMetro);
            var graph = _generator.Generate(9, 1).Graph;

            var tiny = DungeonAssembler.ForRun(tinyPool, 9, 1).Assemble(graph);
            Assert.IsTrue(tiny.Success, tiny.Error);
            CollectionAssert.AreEquivalent(new[] { "c_only" }, tiny.Layout.ReusedRoomIds);
            Assert.IsEmpty(DungeonLayoutValidator.Validate(tiny.Layout));

            var richPool = RichPool();
            var richReuseFree = 0;
            for (var seed = 0; seed < 20; seed++)
            {
                var rich = DungeonAssembler.ForRun(richPool, seed, 1).Assemble(_generator.Generate(seed, 1).Graph);
                Assert.IsTrue(rich.Success, rich.Error);
                if (rich.Layout.ReusedRoomIds.Count == 0)
                {
                    richReuseFree++;
                    Assert.AreEqual(rich.Layout.Placements.Count, rich.Layout.Placements.Select(p => p.Definition.Id).Distinct().Count());
                }
            }

            Assert.GreaterOrEqual(richReuseFree, 15, "With a sufficient pool most dungeons use every room at most once.");
        }

        // ---- Requirement 1: only validated rooms of the requested biome ----

        [Test]
        public void RoomPoolBuild_KeepsOnlyGeneratorReadyRoomsOfTheBiome()
        {
            var fixture = UnityEditor.AssetDatabase.LoadAssetAtPath<RoomDefinition>("Assets/Game/ScriptableObjects/Rooms/_Test/Room_GridTestRoom.asset");
            var broken = SyntheticRoom("broken_combat", RoomType.Combat); // synthetic rooms have no floor tiles → not generator-ready
            var otherBiome = SyntheticRoom("rust_combat", RoomType.Combat, Biome.Rustworks);

            var pool = RoomPool.Build(new[] { fixture, broken, otherBiome }, Biome.RuinedMetro);

            CollectionAssert.AreEquivalent(new[] { "test_grid_small_01" }, pool.Rooms.Select(r => r.Id));
            Assert.AreEqual(1, pool.Rejected.Count);
            Assert.AreEqual("broken_combat", pool.Rejected[0].RoomId);
            Assert.IsFalse(pool.Rejected[0].IsGeneratorReady);
        }

        // ---- Real authored room-set smoke test (TASK 083+: the set grows subset by subset until Boss arenas exist) ----

        [Test]
        public void AuthoredRoomSet_SmokeTest_BuildsPoolAndReportsMissingCategoriesClearly()
        {
            var definitions = RoomValidationTools.LoadAllRoomDefinitions();
            var pool = RoomPool.Build(definitions, Biome.RuinedMetro);
            Assert.IsTrue(pool.Rooms.Any(r => r.Id == "test_grid_small_01"), "Authored fixture must be generator-ready.");
            Assert.IsEmpty(pool.Rejected.Where(r => definitions.Any(d => d.Id == r.RoomId && d.Biome == Biome.RuinedMetro)));

            var graph = _generator.Generate(1, 1).Graph;
            var result = DungeonAssembler.ForRun(pool, 1, 1).Assemble(graph);

            var hasStart = pool.Rooms.Any(r => r.RoomType == RoomType.Start);
            var hasBoss = pool.Rooms.Any(r => r.RoomType == RoomType.Boss);
            if (!hasStart || !hasBoss)
            {
                Assert.IsFalse(result.Success, "Without an authored Start/Boss set assembly must fail clearly, never produce a partial dungeon.");
                StringAssert.Contains("no generator-ready room for", result.Error);
                if (!hasStart) StringAssert.Contains("Start", result.Error);
                if (!hasBoss) StringAssert.Contains("Boss", result.Error);
            }
            else
            {
                Assert.IsTrue(result.Success, result.Error);
                Assert.IsEmpty(DungeonLayoutValidator.Validate(result.Layout));
            }
        }

        [Test]
        public void LayoutValidator_DetectsOverlap_MissingEdge_AndUnintendedContact()
        {
            var start = SyntheticRoom("s", RoomType.Start);
            var combat = SyntheticRoom("c", RoomType.Combat);
            var boss = SyntheticRoom("b", RoomType.Boss);
            var graph = new DungeonGraph(1, 1);
            var s = graph.AddNode(RoomType.Start).Id;
            var c = graph.AddNode(RoomType.Combat).Id;
            var b = graph.AddNode(RoomType.Boss).Id;
            graph.AddEdge(s, c);
            graph.AddEdge(c, b);
            graph.SetMainPath(new[] { s, c, b });

            var layout = new DungeonLayout(graph, Biome.RuinedMetro);
            layout.Add(new RoomPlacement(s, start, Vector2Int.zero));
            layout.Add(new RoomPlacement(c, combat, new Vector2Int(8, 0)));   // overlaps Start
            layout.Add(new RoomPlacement(b, boss, new Vector2Int(0, 12)));    // touches Start via N/S sockets but is not an edge
            layout.AddConnection(new RoomConnection(s, DoorDirection.East, c, DoorDirection.West));

            var problems = DungeonLayoutValidator.Validate(layout);

            Assert.IsTrue(problems.Any(p => p.Contains("overlap")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains($"Graph edge {c}-{b} has no socket connection")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("not aligned")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("without a graph edge")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("Boss is not reachable")), string.Join("\n", problems));
        }
    }
}
