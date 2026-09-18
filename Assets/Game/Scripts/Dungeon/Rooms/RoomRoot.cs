using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Grid;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>
    /// Root component of a hand-authored room prefab: links the metadata definition, declares authored tile size and
    /// gives deterministic, ordered access to sockets and markers (sorted by cell, so queries do not depend on hierarchy order).
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Grid))]
    public sealed class RoomRoot : MonoBehaviour
    {
        [SerializeField] private RoomDefinition _definition;
        [SerializeField] private Vector2Int _size = new(16, 12);

        public RoomDefinition Definition => _definition;
        public Vector2Int Size => _size;
        public UnityEngine.Grid Grid => GetComponent<UnityEngine.Grid>();

        public void Configure(RoomDefinition definition, Vector2Int size)
        {
            _definition = definition;
            _size = size;
        }

        public IReadOnlyList<DoorSocket> GetSockets()
        {
            return GetComponentsInChildren<DoorSocket>(true)
                .OrderBy(s => s.Direction).ThenBy(s => s.Cell.x).ThenBy(s => s.Cell.y)
                .ToList();
        }

        public DoorSocket GetSocket(DoorDirection direction)
        {
            return GetSockets().FirstOrDefault(s => s.Direction == direction);
        }

        public IReadOnlyList<RoomMarker> GetMarkers()
        {
            return GetComponentsInChildren<RoomMarker>(true)
                .OrderBy(m => m.Role).ThenBy(m => m.Cell.x).ThenBy(m => m.Cell.y)
                .ToList();
        }

        public IReadOnlyList<RoomMarker> GetMarkers(RoomMarkerRole role)
        {
            return GetMarkers().Where(m => m.Role == role).ToList();
        }

        public bool IsInside(Vector2Int cell) => GridCoordinates.IsInsideBounds(cell, _size);
    }
}
