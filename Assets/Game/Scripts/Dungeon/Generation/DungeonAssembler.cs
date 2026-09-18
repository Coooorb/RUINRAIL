using RuinRail.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    public sealed class DungeonAssemblyResult
    {
        private DungeonAssemblyResult(DungeonLayout layout, string error, int placementAttempts)
        {
            Layout = layout;
            Error = error;
            PlacementAttempts = placementAttempts;
        }

        public bool Success => Layout != null;
        public DungeonLayout Layout { get; }
        public string Error { get; }
        public int PlacementAttempts { get; }

        public static DungeonAssemblyResult Ok(DungeonLayout layout, int attempts) => new(layout, null, attempts);
        public static DungeonAssemblyResult Fail(string error, int attempts) => new(null, error, attempts);
    }

    /// <summary>
    /// Maps a valid graph onto validated handmade room prefabs: breadth-first from Start, each edge is realised by an
    /// unused socket on the placed parent and a compatible (opposite, same width) socket on a candidate room, placed at
    /// the exact integer offset that aligns the two sockets. Overlaps and unintended socket contacts are rejected and
    /// the search backtracks. Selection order comes only from the injected Assembly stream; nothing is instantiated.
    /// </summary>
    public sealed class DungeonAssembler
    {
        public const int DefaultMaxPlacementAttempts = 5000;

        private readonly RoomPool _pool;
        private readonly IRandomSource _random;
        private readonly int _maxPlacementAttempts;

        public DungeonAssembler(RoomPool pool, IRandomSource random, int maxPlacementAttempts = DefaultMaxPlacementAttempts)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _maxPlacementAttempts = Mathf.Max(1, maxPlacementAttempts);
        }

        public static DungeonAssembler ForRun(RoomPool pool, int runSeed, int depth)
        {
            return new DungeonAssembler(pool, RngStreams.Derive(runSeed, depth, RngStream.Assembly));
        }

        public DungeonAssemblyResult Assemble(DungeonGraph graph)
        {
            if (graph == null)
            {
                return DungeonAssemblyResult.Fail("Graph is null.", 0);
            }

            var eligible = _pool.AvailableAtDepth(graph.Depth).ToList();
            var missing = graph.Nodes
                .Select(n => (n.Type, n.IsElite))
                .Distinct()
                .Where(need => !eligible.Any(r => r.RoomType == need.Type && (!need.IsElite || r.SupportsElite)))
                .Select(need => need.IsElite ? $"{need.Type} (elite-capable)" : need.Type.ToString())
                .ToList();
            if (missing.Count > 0)
            {
                return DungeonAssemblyResult.Fail(
                    $"Room pool for {_pool.Biome} at depth {graph.Depth} has no generator-ready room for: {string.Join(", ", missing)}. Pool size {eligible.Count}, rejected {_pool.Rejected.Count}.",
                    0);
            }

            var layout = new DungeonLayout(graph, _pool.Biome);
            var usage = new Dictionary<string, int>();
            var attempts = 0;

            var order = PlacementOrder(graph);
            var startNode = graph.GetNode(graph.StartId);
            foreach (var startRoom in Candidates(eligible, startNode, usage, degree: startNode.Neighbors.Count))
            {
                attempts++;
                layout.Add(new RoomPlacement(startNode.Id, startRoom, Vector2Int.zero));
                Use(usage, layout, startRoom);
                if (PlaceRemaining(graph, layout, eligible, order, 1, usage, ref attempts))
                {
                    foreach (var reused in usage.Where(kv => kv.Value > 1).Select(kv => kv.Key).OrderBy(id => id))
                    {
                        layout.MarkReused(reused);
                    }

                    var problems = DungeonLayoutValidator.Validate(layout);
                    if (problems.Count == 0)
                    {
                        return DungeonAssemblyResult.Ok(layout, attempts);
                    }

                    return DungeonAssemblyResult.Fail("Assembled layout failed validation: " + string.Join(" | ", problems), attempts);
                }

                Unuse(usage, startRoom);
                layout.Remove(startNode.Id);
                if (attempts >= _maxPlacementAttempts) break;
            }

            return DungeonAssemblyResult.Fail(
                $"Could not place all {graph.Nodes.Count} rooms for {_pool.Biome} depth {graph.Depth} within {attempts} placement attempts (limit {_maxPlacementAttempts}); the eligible pool ({eligible.Count} rooms) cannot satisfy the graph's socket/overlap constraints.",
                attempts);
        }

        /// <summary>BFS order from Start with each node's parent, so every placement has exactly one already placed neighbour.</summary>
        private static List<(int node, int parent)> PlacementOrder(DungeonGraph graph)
        {
            var order = new List<(int, int)> { (graph.StartId, -1) };
            var visited = new HashSet<int> { graph.StartId };
            var queue = new Queue<int>();
            queue.Enqueue(graph.StartId);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in graph.GetNode(current).Neighbors.OrderBy(n => n))
                {
                    if (visited.Add(next))
                    {
                        order.Add((next, current));
                        queue.Enqueue(next);
                    }
                }
            }

            return order;
        }

        private bool PlaceRemaining(DungeonGraph graph, DungeonLayout layout, List<RoomDefinition> eligible, List<(int node, int parent)> order, int index, Dictionary<string, int> usage, ref int attempts)
        {
            if (index >= order.Count)
            {
                return true;
            }

            var (nodeId, parentId) = order[index];
            var node = graph.GetNode(nodeId);
            var parent = layout.GetPlacement(parentId);
            var parentSockets = DungeonLayoutValidator.Sockets(parent)
                .Where(s => !layout.IsSocketUsed(parentId, s.Direction))
                .ToList();
            parentSockets = Shuffle(parentSockets);

            // Rooms with identical dimensions + socket geometry behave identically at a given attachment; once such a
            // shape failed here, every other room of that shape is skipped instead of re-exploring the same subtree.
            var failedShapes = new HashSet<string>();

            foreach (var parentSocket in parentSockets)
            {
                var needed = DoorDirections.Opposite(parentSocket.Direction);
                foreach (var candidate in Candidates(eligible, node, usage, node.Neighbors.Count))
                {
                    if (attempts >= _maxPlacementAttempts)
                    {
                        return false;
                    }

                    var root = candidate.Prefab.GetComponent<RoomRoot>();
                    var socket = root.GetSocket(needed);
                    if (socket == null || !parentSocket.IsCompatibleWith(socket))
                    {
                        continue;
                    }

                    var shapeKey = $"{parentSocket.Direction}|{ShapeSignature(candidate, root)}";
                    if (failedShapes.Contains(shapeKey))
                    {
                        continue;
                    }

                    attempts++;

                    // Align: parent socket cell stepped outward must land exactly on the candidate's socket cell.
                    var offset = parent.ToLayout(parentSocket.Cell) + DoorDirections.Step(parentSocket.Direction) - socket.Cell;
                    var placement = new RoomPlacement(nodeId, candidate, offset);
                    if (!Fits(layout, placement, parentId))
                    {
                        failedShapes.Add(shapeKey);
                        continue;
                    }

                    layout.Add(placement);
                    layout.AddConnection(new RoomConnection(parentId, parentSocket.Direction, nodeId, needed));
                    Use(usage, layout, candidate);

                    if (PlaceRemaining(graph, layout, eligible, order, index + 1, usage, ref attempts))
                    {
                        return true;
                    }

                    Unuse(usage, candidate);
                    layout.RemoveConnectionsOf(nodeId);
                    layout.Remove(nodeId);
                    failedShapes.Add(shapeKey);
                }
            }

            return false;
        }

        private static string ShapeSignature(RoomDefinition definition, RoomRoot root)
        {
            var sockets = string.Join(",", root.GetSockets().Select(s => $"{s.Direction}:{s.Cell.x}:{s.Cell.y}:{s.Width}"));
            return $"{definition.Dimensions.x}x{definition.Dimensions.y}[{sockets}]";
        }

        /// <summary>No bounds overlap with any placed room and no socket contact except with the intended parent.</summary>
        private static bool Fits(DungeonLayout layout, RoomPlacement placement, int parentId)
        {
            var sockets = DungeonLayoutValidator.Sockets(placement);
            foreach (var other in layout.Placements)
            {
                if (other.Bounds.Overlaps(placement.Bounds))
                {
                    return false;
                }

                if (other.NodeId == parentId)
                {
                    continue;
                }

                foreach (var sa in sockets)
                {
                    foreach (var sb in DungeonLayoutValidator.Sockets(other))
                    {
                        if (sa.IsCompatibleWith(sb) && DungeonLayoutValidator.SocketsAligned(placement, sa, other, sb))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        /// <summary>Unused rooms first (shuffled), then already used rooms (shuffled) as controlled reuse.</summary>
        private IEnumerable<RoomDefinition> Candidates(List<RoomDefinition> eligible, RoomNode node, Dictionary<string, int> usage, int degree)
        {
            var matching = eligible
                .Where(r => r.RoomType == node.Type && (!node.IsElite || r.SupportsElite))
                .Where(r => r.Prefab != null && r.Prefab.GetComponent<RoomRoot>() != null && r.Prefab.GetComponent<RoomRoot>().GetSockets().Count >= degree)
                .ToList();
            // Prefer rooms with no more sockets than the node needs: spare sockets are the main source of unintended
            // contacts. Order within a socket-count tier is random, so the assembly stream still decides room identity.
            var unused = Shuffle(matching.Where(r => !usage.ContainsKey(r.Id)).ToList()).OrderBy(SocketCount).ToList();
            var used = Shuffle(matching.Where(r => usage.ContainsKey(r.Id)).ToList()).OrderBy(SocketCount).ToList();
            return unused.Concat(used);
        }

        private static int SocketCount(RoomDefinition room) => room.Prefab.GetComponent<RoomRoot>().GetSockets().Count;

        private static void Use(Dictionary<string, int> usage, DungeonLayout layout, RoomDefinition room)
        {
            usage.TryGetValue(room.Id, out var count);
            usage[room.Id] = count + 1;
        }

        private static void Unuse(Dictionary<string, int> usage, RoomDefinition room)
        {
            if (!usage.TryGetValue(room.Id, out var count)) return;
            if (count <= 1) usage.Remove(room.Id);
            else usage[room.Id] = count - 1;
        }

        private List<T> Shuffle<T>(List<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = _random.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }
    }
}
