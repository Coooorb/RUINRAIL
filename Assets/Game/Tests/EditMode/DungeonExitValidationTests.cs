using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Tests
{
    /// <summary>
    /// Room-exit rule regression: every traversable exit of every placed room leads into a connected room, and no
    /// doorway opens onto the void. Handmade rooms carry up to four sockets; the ones a layout does not use are
    /// sealed with the room's own wall when the dungeon is instantiated, the scene-level validator proves it, and a
    /// layout that would ship an open unconnected doorway fails validation instead of being patched.
    /// </summary>
    public sealed class DungeonExitValidationTests
    {
        private readonly List<UnityEngine.Object> _created = new();
        private DungeonGraphGenerator _generator;
        private BiomeRoomPools _pools;

        [SetUp]
        public void SetUp()
        {
            var rules = DungeonGraphRules.CreateDefault();
            _created.Add(rules);
            _generator = new DungeonGraphGenerator(rules);
            var catalog = GameContentCatalog.Load();
            Assert.IsNotNull(catalog, "Resources/GameContentCatalog.asset");
            _pools = BiomeRoomPools.Build(catalog.Rooms);
            Assert.IsTrue(_pools.IsComplete, string.Join("\n", _pools.Problems()));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            _created.Clear();
        }

        private static readonly (Biome biome, int depth)[] Depths =
        {
            (Biome.RuinedMetro, 1), (Biome.RuinedMetro, 4), (Biome.RuinedMetro, 9),
            (Biome.Rustworks, 1), (Biome.Rustworks, 5), (Biome.Rustworks, 12),
            (Biome.OvergrownLabs, 2), (Biome.OvergrownLabs, 6), (Biome.OvergrownLabs, 15)
        };

        // ---- The rule holds for the shipped pools across many seeds ----

        [Test]
        public void EveryGeneratedDungeon_HasNoOpenExitIntoTheVoid_AndEveryConnectionIsReciprocal()
        {
            var sealedTotal = 0;
            var connectedTotal = 0;
            foreach (var (biome, depth) in Depths)
            foreach (var seed in Enumerable.Range(1, 15))
            {
                var generation = DungeonGenerationPipeline.Generate(_generator, _pools.PoolFor(biome), seed, depth);
                Assert.IsTrue(generation.Success, $"{biome} seed {seed} depth {depth}: {generation.Error}");
                Assert.IsEmpty(DungeonLayoutValidator.Validate(generation.Layout), $"{biome} seed {seed} depth {depth}");

                var parent = new GameObject($"Dungeon_{biome}_{seed}_{depth}");
                _created.Add(parent);
                var rooms = DungeonLayoutInstantiator.Instantiate(generation.Layout, parent.transform);

                var problems = DungeonExitValidator.Validate(generation.Layout, rooms);
                Assert.IsEmpty(problems, $"{biome} seed {seed} depth {depth}:\n{string.Join("\n", problems)}");

                foreach (var placement in generation.Layout.Placements)
                {
                    var room = rooms[placement.NodeId];
                    foreach (var socket in room.GetSockets())
                    {
                        if (generation.Layout.IsSocketUsed(placement.NodeId, socket.Direction))
                        {
                            Assert.IsTrue(RoomExitSealer.IsOpen(room, socket), $"{biome} seed {seed} depth {depth}: connected exit {placement.NodeId}.{socket.Direction} must stay open.");
                            connectedTotal++;
                        }
                        else
                        {
                            Assert.IsTrue(RoomExitSealer.IsSealed(room, socket), $"{biome} seed {seed} depth {depth}: spare exit {placement.NodeId}.{socket.Direction} must be walled.");
                            sealedTotal++;
                        }
                    }
                }

                UnityEngine.Object.DestroyImmediate(parent);
            }

            Assert.Greater(connectedTotal, 0);
            Assert.Greater(sealedTotal, 0, "The authored rooms carry spare sockets; the sweep must have exercised sealing.");
        }

        // ---- The sealer uses the room's own wall, on exact socket cells, with the wall's collider ----

        [Test]
        public void Sealing_WallsExactlyTheSocketCells_WithTheRoomsOwnWallTile()
        {
            var room = _pools.PoolFor(Biome.RuinedMetro).Rooms.First(r => r.Prefab.GetComponent<RoomRoot>().GetSockets().Count == 4);
            var instance = UnityEngine.Object.Instantiate(room.Prefab);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var walls = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Walls);
            var socket = root.GetSocket(DoorDirection.North);
            Assert.IsTrue(RoomExitSealer.IsOpen(root, socket), "Authored doorways are open floor.");
            var before = CountTiles(walls);
            var neighbour = walls.GetTile(GridCoordinates.ToTilemapCell(socket.Cell - Vector2Int.right));
            Assert.IsNotNull(neighbour, "A door run is flanked by wall on the same edge.");

            Assert.IsTrue(RoomExitSealer.Seal(root, socket));

            Assert.IsTrue(RoomExitSealer.IsSealed(root, socket));
            Assert.AreEqual(before + socket.Width, CountTiles(walls), "Exactly the socket cells were walled, nothing else.");
            foreach (var cell in socket.Cells())
            {
                var tile = walls.GetTile(GridCoordinates.ToTilemapCell(cell));
                Assert.AreSame(neighbour, tile, $"cell {cell} carries the flanking wall tile");
                Assert.AreEqual(Tile.ColliderType.Grid, ((Tile)tile).colliderType, "The seal blocks movement like the rest of the wall.");
            }

            foreach (var other in root.GetSockets().Where(s => s != socket))
            {
                Assert.IsTrue(RoomExitSealer.IsOpen(root, other), $"{other.Direction} untouched");
            }
        }

        // ---- The scene validator rejects an open, unconnected doorway ----

        [Test]
        public void ExitValidator_FailsADungeonWhoseSpareDoorwaysAreLeftOpen()
        {
            var generation = DungeonGenerationPipeline.Generate(_generator, _pools.PoolFor(Biome.Rustworks), 3, 1);
            Assert.IsTrue(generation.Success, generation.Error);

            // Raw instantiation, bypassing the sealing the real instantiator performs.
            var parent = new GameObject("Unsealed");
            _created.Add(parent);
            var rooms = new Dictionary<int, RoomRoot>();
            foreach (var placement in generation.Layout.Placements)
            {
                var world = GridCoordinates.CellToWorldMin(placement.Offset);
                var instance = UnityEngine.Object.Instantiate(placement.Definition.Prefab, new Vector3(world.x, world.y, 0f), Quaternion.identity, parent.transform);
                rooms[placement.NodeId] = instance.GetComponent<RoomRoot>();
            }

            var problems = DungeonExitValidator.Validate(generation.Layout, rooms);
            Assert.IsNotEmpty(problems, "Spare sockets left as open floor are void exposure.");
            Assert.IsTrue(problems.All(p => p.Contains("void exposure")), string.Join("\n", problems));

            foreach (var placement in generation.Layout.Placements) RoomExitSealer.SealUnconnected(generation.Layout, placement, rooms[placement.NodeId]);
            Assert.IsEmpty(DungeonExitValidator.Validate(generation.Layout, rooms), "Sealed, the same dungeon passes.");
        }

        [Test]
        public void ExitValidator_FailsAConnectedDoorwayThatIsWalledOnEitherSide()
        {
            var generation = DungeonGenerationPipeline.Generate(_generator, _pools.PoolFor(Biome.OvergrownLabs), 5, 2);
            Assert.IsTrue(generation.Success, generation.Error);
            var parent = new GameObject("Blocked");
            _created.Add(parent);
            var rooms = DungeonLayoutInstantiator.Instantiate(generation.Layout, parent.transform);
            Assert.IsEmpty(DungeonExitValidator.Validate(generation.Layout, rooms));

            var connection = generation.Layout.Connections[0];
            RoomExitSealer.Seal(rooms[connection.NodeA], rooms[connection.NodeA].GetSocket(connection.SocketA));
            var problems = DungeonExitValidator.Validate(generation.Layout, rooms);
            Assert.IsTrue(problems.Any(p => p.Contains("walled on one side")), string.Join("\n", problems));
        }

        // ---- A rejected round is rerolled deterministically, never patched ----

        [Test]
        public void Pipeline_ResumesFromTheNextRound_Deterministically_WhenASceneLevelCheckRejectsARound()
        {
            var pool = _pools.PoolFor(Biome.RuinedMetro);
            var first = DungeonGenerationPipeline.Generate(_generator, pool, 11, 3);
            Assert.IsTrue(first.Success, first.Error);
            var next = DungeonGenerationPipeline.Generate(_generator, pool, 11, 3, firstRound: first.Rounds + 1);
            var again = DungeonGenerationPipeline.Generate(_generator, pool, 11, 3, firstRound: first.Rounds + 1);
            Assert.IsTrue(next.Success, next.Error);
            Assert.AreEqual(first.Rounds + 1, next.Rounds, "the reroll starts at the round after the rejected one");
            Assert.AreEqual(next.Layout.Signature(), again.Layout.Signature(), "the reroll is a pure function of seed, depth and round");
            Assert.AreNotEqual(first.Layout.Signature(), next.Layout.Signature(), "a rejected layout is replaced, not re-issued");
            Assert.IsEmpty(DungeonLayoutValidator.Validate(next.Layout));
        }

        // ---- Layout-level reciprocity ----

        private RoomDefinition Fixture(string id, RoomType type, params DoorDirection[] doors)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", id);
            Set(definition, "_roomType", type);
            Set(definition, "_dimensions", new Vector2Int(16, 12));
            var grid = RoomGridBuilder.CreateRoomGrid(id);
            grid.gameObject.SetActive(false);
            _created.Add(grid.gameObject);
            var root = grid.gameObject.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            foreach (var door in doors)
            {
                var socket = new GameObject($"Door_{door}").AddComponent<DoorSocket>();
                socket.transform.SetParent(grid.transform, false);
                var cell = door switch
                {
                    DoorDirection.East => new Vector2Int(15, 5),
                    DoorDirection.West => new Vector2Int(0, 5),
                    DoorDirection.North => new Vector2Int(7, 11),
                    _ => new Vector2Int(7, 0)
                };
                socket.Configure(door, cell);
            }

            Set(definition, "_prefab", grid.gameObject);
            return definition;
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(target, value);

        private (DungeonGraph graph, int s, int b) TwoNodeGraph()
        {
            var graph = new DungeonGraph(1, 1);
            var s = graph.AddNode(RoomType.Start).Id;
            var b = graph.AddNode(RoomType.Boss).Id;
            graph.AddEdge(s, b);
            graph.SetMainPath(new[] { s, b });
            return (graph, s, b);
        }

        [Test]
        public void LayoutValidator_RejectsAConnectionWhoseSocketsDoNotFaceEachOther()
        {
            var start = Fixture("s", RoomType.Start, DoorDirection.East, DoorDirection.North);
            var boss = Fixture("b", RoomType.Boss, DoorDirection.West, DoorDirection.North);
            var (graph, s, b) = TwoNodeGraph();
            var layout = new DungeonLayout(graph, Biome.RuinedMetro);
            layout.Add(new RoomPlacement(s, start, Vector2Int.zero));
            layout.Add(new RoomPlacement(b, boss, new Vector2Int(16, 0)));
            layout.AddConnection(new RoomConnection(s, DoorDirection.East, b, DoorDirection.North));

            var problems = DungeonLayoutValidator.Validate(layout);
            Assert.IsTrue(problems.Any(p => p.Contains("not reciprocal")), string.Join("\n", problems));
        }

        [Test]
        public void LayoutValidator_RejectsASocketClaimedByTwoConnections()
        {
            var start = Fixture("s", RoomType.Start, DoorDirection.East);
            var boss = Fixture("b", RoomType.Boss, DoorDirection.West);
            var (graph, s, b) = TwoNodeGraph();
            var layout = new DungeonLayout(graph, Biome.RuinedMetro);
            layout.Add(new RoomPlacement(s, start, Vector2Int.zero));
            layout.Add(new RoomPlacement(b, boss, new Vector2Int(16, 0)));
            layout.AddConnection(new RoomConnection(s, DoorDirection.East, b, DoorDirection.West));
            Assert.IsEmpty(DungeonLayoutValidator.Validate(layout), "A single reciprocal connection is valid.");

            layout.AddConnection(new RoomConnection(s, DoorDirection.East, b, DoorDirection.West));
            var problems = DungeonLayoutValidator.Validate(layout);
            Assert.IsTrue(problems.Any(p => p.Contains("more than one connection")), string.Join("\n", problems));
        }

        private static int CountTiles(Tilemap map)
        {
            map.CompressBounds();
            var count = 0;
            foreach (var p in map.cellBounds.allPositionsWithin) if (map.HasTile(p)) count++;
            return count;
        }
    }
}
