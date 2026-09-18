using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Closes every door socket of an instantiated room that the layout did not connect to a neighbour.
    ///
    /// Handmade rooms are authored with up to four sockets so the assembler can attach them in any orientation, but a
    /// placed room only uses as many as the graph gives it a neighbour for. The socket cells are floor in the prefab,
    /// so every spare socket was an open doorway onto the black outside the dungeon. A spare socket is now walled with
    /// the room's own wall tile on the room's own Walls layer — exact grid cells, the same tile and the same collider
    /// the surrounding wall already has — so a sealed exit is wall in every sense: not drawn open, not walkable, not
    /// shootable through. Nothing else about the prefab instance changes.
    /// </summary>
    public static class RoomExitSealer
    {
        /// <summary>Seals every socket of <paramref name="room"/> that the layout has no connection for; returns the sealed sockets.</summary>
        public static IReadOnlyList<DoorSocket> SealUnconnected(DungeonLayout layout, RoomPlacement placement, RoomRoot room)
        {
            var sealedSockets = new List<DoorSocket>();
            if (layout == null || placement == null || room == null) return sealedSockets;
            foreach (var socket in room.GetSockets())
            {
                if (layout.IsSocketUsed(placement.NodeId, socket.Direction)) continue;
                if (Seal(room, socket)) sealedSockets.Add(socket);
            }

            return sealedSockets;
        }

        /// <summary>Walls one socket's cells. False when the room has no Walls layer or no wall tile to seal with.</summary>
        public static bool Seal(RoomRoot room, DoorSocket socket)
        {
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            if (walls == null) return false;
            var wallTile = WallTileBeside(walls, socket, room.Size);
            if (wallTile == null) return false;

            foreach (var cell in socket.Cells())
            {
                walls.SetTile(GridCoordinates.ToTilemapCell(cell), wallTile);
            }

            // The Walls collider normally rebuilds at the end of the frame; the player may already be standing in the
            // room, so the sealed cells have to block right now.
            var collider = walls.GetComponent<TilemapCollider2D>();
            if (collider != null) collider.ProcessTilemapChanges();
            return true;
        }

        /// <summary>True when every cell of the socket carries a tile on the Walls layer (the exit is closed).</summary>
        public static bool IsSealed(RoomRoot room, DoorSocket socket)
        {
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            return walls != null && socket.Cells().All(cell => walls.GetTile(GridCoordinates.ToTilemapCell(cell)) != null);
        }

        /// <summary>True when no cell of the socket carries a Walls tile (the exit is open / traversable).</summary>
        public static bool IsOpen(RoomRoot room, DoorSocket socket)
        {
            var walls = RoomGridBuilder.FindLayer(room.Grid, RoomTilemapLayer.Walls);
            return walls != null && socket.Cells().All(cell => walls.GetTile(GridCoordinates.ToTilemapCell(cell)) == null);
        }

        /// <summary>
        /// The wall tile the doorway sits in: the edge cell just before or just after the door run, so the seal is the
        /// biome's own wall art. Falls back to any wall tile in the room, which every authored room has.
        /// </summary>
        private static TileBase WallTileBeside(Tilemap walls, DoorSocket socket, Vector2Int roomSize)
        {
            var cells = socket.Cells();
            var along = DoorDirections.IsHorizontalEdge(socket.Direction) ? Vector2Int.right : Vector2Int.up;
            foreach (var candidate in new[] { cells[0] - along, cells[cells.Length - 1] + along })
            {
                if (!GridCoordinates.IsInsideBounds(candidate, roomSize)) continue;
                var tile = walls.GetTile(GridCoordinates.ToTilemapCell(candidate));
                if (tile != null) return tile;
            }

            walls.CompressBounds();
            foreach (var position in walls.cellBounds.allPositionsWithin)
            {
                var tile = walls.GetTile(position);
                if (tile != null) return tile;
            }

            return null;
        }
    }
}
