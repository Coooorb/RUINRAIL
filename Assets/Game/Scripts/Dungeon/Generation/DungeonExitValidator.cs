using System.Collections.Generic;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Validates the instantiated dungeon against the room-exit rule: every traversable exit leads into a connected
    /// neighbouring room, and no doorway opens onto empty space.
    ///
    /// The layout validator proves the graph and the socket pairing on paper; this one checks the scene the player
    /// is about to walk through. For every socket of every placed room it requires exactly one of two states —
    /// connected: the partner room exists, its reciprocal socket lands on the adjacent cells, and both doorways are
    /// open; or sealed: every cell of the socket is wall. An open, unconnected doorway is a failure the expedition
    /// must not start with, and the caller discards the dungeon and regenerates (53 "Generation Failure").
    /// </summary>
    public static class DungeonExitValidator
    {
        public static List<string> Validate(DungeonLayout layout, IReadOnlyDictionary<int, RoomRoot> rooms)
        {
            var problems = new List<string>();
            if (layout == null) { problems.Add("Layout is null."); return problems; }
            if (rooms == null) { problems.Add("No room instances."); return problems; }

            foreach (var placement in layout.Placements)
            {
                if (!rooms.TryGetValue(placement.NodeId, out var room) || room == null)
                {
                    problems.Add($"Node {placement.NodeId} has no room instance.");
                    continue;
                }

                foreach (var socket in room.GetSockets())
                {
                    var connection = ConnectionOf(layout, placement.NodeId, socket.Direction);
                    if (connection == null)
                    {
                        if (!RoomExitSealer.IsSealed(room, socket))
                            problems.Add($"Room {placement.NodeId} ('{placement.Definition.Id}') has an open {socket.Direction} exit with no connected room (void exposure).");
                        continue;
                    }

                    var partnerId = connection.NodeA == placement.NodeId ? connection.NodeB : connection.NodeA;
                    var partnerDirection = connection.NodeA == placement.NodeId ? connection.SocketB : connection.SocketA;
                    if (!rooms.TryGetValue(partnerId, out var partner) || partner == null)
                    {
                        problems.Add($"Room {placement.NodeId} {socket.Direction} exit connects to node {partnerId}, which has no room instance.");
                        continue;
                    }

                    var reciprocal = partner.GetSocket(partnerDirection);
                    if (reciprocal == null || !socket.IsCompatibleWith(reciprocal))
                    {
                        problems.Add($"Room {placement.NodeId} {socket.Direction} exit has no compatible reciprocal socket on room {partnerId}.");
                        continue;
                    }

                    if (!Adjacent(room, socket, partner, reciprocal))
                    {
                        problems.Add($"Room {placement.NodeId} {socket.Direction} exit and room {partnerId} {partnerDirection} exit are not adjacent in the scene.");
                        continue;
                    }

                    if (!RoomExitSealer.IsOpen(room, socket) || !RoomExitSealer.IsOpen(partner, reciprocal))
                        problems.Add($"Connected exit {placement.NodeId}.{socket.Direction}–{partnerId}.{partnerDirection} is walled on one side.");
                }
            }

            return problems;
        }

        private static RoomConnection ConnectionOf(DungeonLayout layout, int nodeId, DoorDirection direction)
        {
            foreach (var c in layout.Connections)
            {
                if ((c.NodeA == nodeId && c.SocketA == direction) || (c.NodeB == nodeId && c.SocketB == direction)) return c;
            }

            return null;
        }

        /// <summary>Every door cell of A, stepped outward one tile in world space, lands on the matching door cell of B.</summary>
        private static bool Adjacent(RoomRoot a, DoorSocket sa, RoomRoot b, DoorSocket sb)
        {
            var cellsA = sa.Cells();
            var cellsB = sb.Cells();
            if (cellsA.Length != cellsB.Length) return false;
            var step = (Vector2)DoorDirections.Step(sa.Direction) * GridConstants.TileWorldSize;
            for (var i = 0; i < cellsA.Length; i++)
            {
                var worldA = (Vector2)a.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cellsA[i])) + step;
                var worldB = (Vector2)b.transform.TransformPoint(GridCoordinates.CellToWorldCenter(cellsB[i]));
                if ((worldA - worldB).sqrMagnitude > 1e-4f) return false;
            }

            return true;
        }
    }
}
