using RuinRail.Core;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Generated-dungeon validation (54_DUNGEON_VALIDATION "Generated Dungeon Validation"): every node placed, no room
    /// overlap, every graph edge realised by a compatible aligned socket pair, no unintended socket contact, and every
    /// room (Boss included) reachable from Start through realised connections.
    /// </summary>
    public static class DungeonLayoutValidator
    {
        public static List<string> Validate(DungeonLayout layout)
        {
            var problems = new List<string>();
            if (layout == null)
            {
                problems.Add("Layout is null.");
                return problems;
            }

            var graph = layout.Graph;
            foreach (var node in graph.Nodes)
            {
                var placement = layout.GetPlacement(node.Id);
                if (placement == null)
                {
                    problems.Add($"Node {node.Id} ({node.Type}) has no placed room.");
                    continue;
                }

                if (placement.Definition.RoomType != node.Type)
                {
                    problems.Add($"Node {node.Id} is {node.Type} but room '{placement.Definition.Id}' is {placement.Definition.RoomType}.");
                }

                if (node.IsElite && !placement.Definition.SupportsElite)
                {
                    problems.Add($"Node {node.Id} hosts an Elite but room '{placement.Definition.Id}' does not support elites.");
                }

                if (placement.Definition.Biome != layout.Biome)
                {
                    problems.Add($"Room '{placement.Definition.Id}' is {placement.Definition.Biome}, layout biome is {layout.Biome}.");
                }
            }

            var placements = layout.Placements.ToList();
            for (var i = 0; i < placements.Count; i++)
            {
                for (var j = i + 1; j < placements.Count; j++)
                {
                    if (placements[i].Bounds.Overlaps(placements[j].Bounds))
                    {
                        problems.Add($"Rooms for nodes {placements[i].NodeId} and {placements[j].NodeId} overlap ({placements[i].Bounds} vs {placements[j].Bounds}).");
                    }
                }
            }

            foreach (var (a, b) in graph.Edges)
            {
                var connection = layout.Connections.FirstOrDefault(c => c.Joins(a, b));
                if (connection == null)
                {
                    problems.Add($"Graph edge {a}-{b} has no socket connection.");
                    continue;
                }

                var pa = layout.GetPlacement(connection.NodeA);
                var pb = layout.GetPlacement(connection.NodeB);
                if (pa == null || pb == null) continue;

                var sa = SocketOf(pa, connection.SocketA);
                var sb = SocketOf(pb, connection.SocketB);
                if (sa == null || sb == null)
                {
                    problems.Add($"Edge {a}-{b}: a referenced socket does not exist on its room.");
                    continue;
                }

                if (!sa.IsCompatibleWith(sb))
                {
                    problems.Add($"Edge {a}-{b}: sockets {sa.Direction}/{sb.Direction} (widths {sa.Width}/{sb.Width}) are not compatible.");
                }

                if (!SocketsAligned(pa, sa, pb, sb))
                {
                    problems.Add($"Edge {a}-{b}: sockets are not aligned on the layout grid.");
                }
            }

            foreach (var connection in layout.Connections)
            {
                if (!graph.AreAdjacent(connection.NodeA, connection.NodeB))
                {
                    problems.Add($"Connection {connection.NodeA}-{connection.NodeB} is not a graph edge (unintended connection).");
                }

                // Reciprocity: a connection joins one socket on each side, the two face each other, and neither socket
                // is claimed by a second connection — otherwise one doorway would lead into two rooms, or one room
                // would have a doorway with no matching doorway opposite it.
                if (connection.SocketB != DoorDirections.Opposite(connection.SocketA))
                {
                    problems.Add($"Connection {connection.NodeA}.{connection.SocketA}-{connection.NodeB}.{connection.SocketB} is not reciprocal (sockets do not face each other).");
                }

                var usesA = layout.Connections.Count(c => (c.NodeA == connection.NodeA && c.SocketA == connection.SocketA) || (c.NodeB == connection.NodeA && c.SocketB == connection.SocketA));
                var usesB = layout.Connections.Count(c => (c.NodeA == connection.NodeB && c.SocketA == connection.SocketB) || (c.NodeB == connection.NodeB && c.SocketB == connection.SocketB));
                if (usesA > 1 || usesB > 1)
                {
                    problems.Add($"Socket {connection.NodeA}.{connection.SocketA} or {connection.NodeB}.{connection.SocketB} is claimed by more than one connection.");
                }
            }

            // Unintended socket contact: two aligned compatible sockets that are not a connection.
            for (var i = 0; i < placements.Count; i++)
            {
                for (var j = i + 1; j < placements.Count; j++)
                {
                    foreach (var sa in Sockets(placements[i]))
                    {
                        foreach (var sb in Sockets(placements[j]))
                        {
                            if (sa.IsCompatibleWith(sb) && SocketsAligned(placements[i], sa, placements[j], sb)
                                && !layout.Connections.Any(c => c.Joins(placements[i].NodeId, placements[j].NodeId)))
                            {
                                problems.Add($"Rooms {placements[i].NodeId} and {placements[j].NodeId} touch through {sa.Direction}/{sb.Direction} sockets without a graph edge.");
                            }
                        }
                    }
                }
            }

            if (layout.StartPlacement != null)
            {
                var reachable = ReachableThroughConnections(layout, graph.StartId);
                if (reachable.Count != graph.Nodes.Count)
                {
                    problems.Add($"Only {reachable.Count}/{graph.Nodes.Count} rooms reachable from Start through connections.");
                }

                if (!reachable.Contains(graph.BossId))
                {
                    problems.Add("Boss is not reachable from Start.");
                }
            }

            return problems;
        }

        public static HashSet<int> ReachableThroughConnections(DungeonLayout layout, int start)
        {
            var visited = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var c in layout.Connections)
                {
                    var next = c.NodeA == current ? c.NodeB : c.NodeB == current ? c.NodeA : -1;
                    if (next >= 0 && visited.Add(next)) queue.Enqueue(next);
                }
            }

            return visited;
        }

        public static IReadOnlyList<DoorSocket> Sockets(RoomPlacement placement)
        {
            var root = placement.Definition.Prefab != null ? placement.Definition.Prefab.GetComponent<RoomRoot>() : null;
            return root != null ? root.GetSockets() : new List<DoorSocket>();
        }

        public static DoorSocket SocketOf(RoomPlacement placement, DoorDirection direction)
        {
            return Sockets(placement).FirstOrDefault(s => s.Direction == direction);
        }

        /// <summary>Two sockets align when every tile of A steps (in A's direction) exactly onto the matching tile of B.</summary>
        public static bool SocketsAligned(RoomPlacement pa, DoorSocket sa, RoomPlacement pb, DoorSocket sb)
        {
            var step = DoorDirections.Step(sa.Direction);
            var cellsA = sa.Cells();
            var cellsB = sb.Cells();
            if (cellsA.Length != cellsB.Length) return false;
            for (var i = 0; i < cellsA.Length; i++)
            {
                if (pa.ToLayout(cellsA[i]) + step != pb.ToLayout(cellsB[i])) return false;
            }

            return true;
        }
    }
}
