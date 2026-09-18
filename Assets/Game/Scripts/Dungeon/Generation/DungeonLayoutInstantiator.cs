using System.Collections.Generic;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Spawns the handmade room prefabs of a validated layout at their integer offsets, then seals every socket the
    /// layout left unconnected (see <see cref="RoomExitSealer"/>). This is the only place the dungeon touches scene
    /// objects; it never freehands geometry — every wall is the prefab's own wall tile on the prefab's own grid cells.
    /// </summary>
    public static class DungeonLayoutInstantiator
    {
        public static Dictionary<int, RoomRoot> Instantiate(DungeonLayout layout, Transform parent)
        {
            var rooms = new Dictionary<int, RoomRoot>();
            foreach (var placement in layout.Placements)
            {
                var worldOffset = GridCoordinates.CellToWorldMin(placement.Offset);
                var instance = Object.Instantiate(placement.Definition.Prefab, new Vector3(worldOffset.x, worldOffset.y, 0f), Quaternion.identity, parent);
                instance.name = $"Room_{placement.NodeId}_{placement.Definition.Id}";
                var root = instance.GetComponent<RoomRoot>();
                rooms[placement.NodeId] = root;
                RoomExitSealer.SealUnconnected(layout, placement, root);
            }

            return rooms;
        }
    }
}
