using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RuinRail.Dungeon.Grid;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Schema-level checks of the room contract: definition metadata, definition/prefab agreement, cardinal grid-aligned
    /// sockets on the correct edge, markers inside bounds without overlapping footprints. Problems name the asset and cell.
    /// (Reachability / room-type content rules belong to the full room validator.)
    /// </summary>
    public static class RoomSchemaValidator
    {
        private static readonly Regex IdPattern = new("^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.Compiled);

        public static List<string> ValidateDefinition(RoomDefinition definition)
        {
            var problems = new List<string>();
            if (definition == null)
            {
                problems.Add("Room definition is null.");
                return problems;
            }

            var label = definition.name;
            if (string.IsNullOrEmpty(definition.Id) || !IdPattern.IsMatch(definition.Id))
            {
                problems.Add($"{label}: id '{definition.Id}' must match {IdPattern}.");
            }

            var classDimensions = RoomSizeClasses.DimensionsOf(definition.SizeClass);
            if (definition.Dimensions != classDimensions && !definition.AllowsAuthoredSizeVariation)
            {
                problems.Add($"{label}: dimensions {definition.Dimensions} do not match {definition.SizeClass} class {classDimensions} and authored variation is not allowed.");
            }

            if (definition.Dimensions.x <= 0 || definition.Dimensions.y <= 0)
            {
                problems.Add($"{label}: dimensions must be positive.");
            }

            if (!definition.IsUnlimitedDepth && definition.MaxDepth < definition.MinDepth)
            {
                problems.Add($"{label}: MaxDepth {definition.MaxDepth} is below MinDepth {definition.MinDepth}.");
            }

            if (definition.SupportedDoors.Count == 0)
            {
                problems.Add($"{label}: declares no supported door directions.");
            }

            if (definition.SupportedDoors.Distinct().Count() != definition.SupportedDoors.Count)
            {
                problems.Add($"{label}: supported door directions contain duplicates.");
            }

            if (definition.SupportsElite && definition.RoomType != RoomType.Combat)
            {
                problems.Add($"{label}: only Combat rooms may support Elite encounters.");
            }

            if (definition.RoomType == RoomType.Boss && definition.SizeClass != RoomSizeClass.Boss)
            {
                problems.Add($"{label}: Boss rooms use the Boss size class.");
            }

            if (definition.Prefab == null)
            {
                problems.Add($"{label}: no room prefab referenced.");
            }
            else if (definition.Prefab.GetComponent<RoomRoot>() == null)
            {
                problems.Add($"{label}: prefab '{definition.Prefab.name}' has no RoomRoot.");
            }

            return problems;
        }

        public static List<string> ValidateRoom(RoomRoot room)
        {
            var problems = new List<string>();
            if (room == null)
            {
                problems.Add("Room root is null.");
                return problems;
            }

            var label = room.name;
            var size = room.Size;
            if (size.x <= 0 || size.y <= 0)
            {
                problems.Add($"{label}: room size must be positive.");
                return problems;
            }

            if (room.Definition == null)
            {
                problems.Add($"{label}: RoomRoot has no RoomDefinition.");
            }
            else
            {
                if (room.Definition.Dimensions != size)
                {
                    problems.Add($"{label}: RoomRoot size {size} differs from definition '{room.Definition.Id}' dimensions {room.Definition.Dimensions}.");
                }

                if (room.Definition.Prefab != null && room.Definition.Prefab.GetComponent<RoomRoot>() != null && room.Definition.Prefab.GetComponent<RoomRoot>().Definition != room.Definition)
                {
                    problems.Add($"{label}: definition '{room.Definition.Id}' prefab does not point back to this definition.");
                }
            }

            problems.AddRange(RoomTilemapValidator.Validate(room.Grid).Select(p => $"{label}: {p}"));

            var sockets = room.GetSockets();
            foreach (var socket in sockets)
            {
                if (!socket.LiesOnEdge(size))
                {
                    problems.Add($"{label}: {socket.Direction} socket at {socket.Cell} (width {socket.Width}) is not on the {socket.Direction} edge of a {size} room.");
                }

                if (room.Definition != null && !room.Definition.SupportsDoor(socket.Direction))
                {
                    problems.Add($"{label}: {socket.Direction} socket at {socket.Cell} is not declared in definition '{room.Definition.Id}'.");
                }

                var expectedLocal = socket.transform.localPosition;
                socket.SnapToGrid();
                if ((socket.transform.localPosition - expectedLocal).sqrMagnitude > 1e-6f)
                {
                    problems.Add($"{label}: {socket.Direction} socket at {socket.Cell} transform is not grid-snapped.");
                }
            }

            foreach (var direction in sockets.GroupBy(s => s.Direction).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                problems.Add($"{label}: more than one {direction} socket.");
            }

            if (room.Definition != null)
            {
                foreach (var declared in room.Definition.SupportedDoors)
                {
                    if (sockets.All(s => s.Direction != declared))
                    {
                        problems.Add($"{label}: definition '{room.Definition.Id}' declares a {declared} door but the prefab has no {declared} socket.");
                    }
                }
            }

            var markers = room.GetMarkers();
            foreach (var marker in markers)
            {
                if (!marker.IsInsideRoom(size))
                {
                    problems.Add($"{label}: {marker.Role} marker at {marker.Cell} footprint {marker.Footprint} leaves the {size} room bounds.");
                }
            }

            for (var i = 0; i < markers.Count; i++)
            {
                for (var j = i + 1; j < markers.Count; j++)
                {
                    if (markers[i].Overlaps(markers[j]) && IsExclusive(markers[i].Role) && IsExclusive(markers[j].Role))
                    {
                        problems.Add($"{label}: {markers[i].Role} marker at {markers[i].Cell} overlaps {markers[j].Role} marker at {markers[j].Cell}.");
                    }
                }
            }

            return problems;
        }

        /// <summary>Spawn/anchor markers reserve their footprint; Walkable/Hazard annotations may coexist with them.</summary>
        private static bool IsExclusive(RoomMarkerRole role)
        {
            return role != RoomMarkerRole.Walkable && role != RoomMarkerRole.Hazard;
        }
    }
}
