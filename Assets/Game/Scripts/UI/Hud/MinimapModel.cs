using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.UI.Hud
{
    /// <summary>
    /// What a room shows on the minimap. The UI assembly never sees the dungeon's own room types; the composition
    /// root translates once, so the map keeps working if a room category is renamed and the UI stays decoupled.
    /// </summary>
    public enum MinimapRoomKind
    {
        Normal,
        Start,
        Combat,
        Loot,
        Treasure,
        Merchant,
        Event,
        WeaponCache,
        Medical,
        Boss,
        Transit
    }

    /// <summary>One room on the minimap: where it sits in layout tiles and what the player knows about it.</summary>
    public sealed class MinimapRoom
    {
        public MinimapRoom(int nodeId, Vector2 center, MinimapRoomKind kind)
        {
            NodeId = nodeId;
            Center = center;
            Kind = kind;
        }

        public int NodeId { get; }
        /// <summary>Room centre in layout tiles (the generator's own coordinates).</summary>
        public Vector2 Center { get; }
        public MinimapRoomKind Kind { get; internal set; }
        /// <summary>The player has stood inside this room.</summary>
        public bool Visited { get; internal set; }
        /// <summary>The room is drawn: it was visited, or it is a neighbour of a visited room.</summary>
        public bool Discovered { get; internal set; }
        /// <summary>A special-room symbol is drawn only once the room itself has been entered (no spoilers).</summary>
        public bool ShowsMarker => Visited && Kind != MinimapRoomKind.Normal && Kind != MinimapRoomKind.Combat;
    }

    /// <summary>
    /// The run's room-graph minimap as data: the rooms of the current depth, the real door connections between them,
    /// and what the player has discovered. It is fed from the one authoritative room-entry event the combat rooms
    /// already use; it never detects entry itself and it never reads the world.
    ///
    /// Discovery rule (roguelite): entering a room marks it <see cref="MinimapRoom.Visited"/> and reveals its direct
    /// neighbours as <see cref="MinimapRoom.Discovered"/> outlines. Nothing else is drawn, so the generated dungeon is
    /// never shown up front, and a special room's symbol appears only once the player has actually been inside it.
    /// </summary>
    public sealed class MinimapModel
    {
        private readonly List<MinimapRoom> _rooms = new();
        private readonly Dictionary<int, MinimapRoom> _byId = new();
        private readonly List<(int a, int b)> _links = new();
        private readonly Dictionary<int, List<int>> _neighbors = new();

        public IReadOnlyList<MinimapRoom> Rooms => _rooms;
        public IReadOnlyList<(int a, int b)> Links => _links;
        public int? CurrentNodeId { get; private set; }
        public int Depth { get; private set; }
        public string BiomeName { get; private set; } = string.Empty;
        public int Entries { get; private set; }

        public event Action Changed;

        public IEnumerable<MinimapRoom> DiscoveredRooms => _rooms.Where(r => r.Discovered);
        public IEnumerable<MinimapRoom> VisitedRooms => _rooms.Where(r => r.Visited);
        public MinimapRoom Room(int nodeId) => _byId.TryGetValue(nodeId, out var room) ? room : null;
        public MinimapRoom Current => CurrentNodeId.HasValue ? Room(CurrentNodeId.Value) : null;

        /// <summary>Links whose two ends are both discovered — the only ones the player may see.</summary>
        public IEnumerable<(int a, int b)> DiscoveredLinks =>
            _links.Where(l => Room(l.a) != null && Room(l.b) != null && Room(l.a).Discovered && Room(l.b).Discovered);

        /// <summary>A new depth: nothing is known about it yet.</summary>
        public void BeginDepth(int depth, string biomeName)
        {
            _rooms.Clear();
            _byId.Clear();
            _links.Clear();
            _neighbors.Clear();
            CurrentNodeId = null;
            Depth = depth;
            BiomeName = biomeName ?? string.Empty;
            Raise();
        }

        public MinimapRoom AddRoom(int nodeId, Vector2 center, MinimapRoomKind kind)
        {
            if (_byId.TryGetValue(nodeId, out var existing)) return existing;
            var room = new MinimapRoom(nodeId, center, kind);
            _rooms.Add(room);
            _byId[nodeId] = room;
            if (!_neighbors.ContainsKey(nodeId)) _neighbors[nodeId] = new List<int>();
            return room;
        }

        public void AddLink(int a, int b)
        {
            if (a == b || !_byId.ContainsKey(a) || !_byId.ContainsKey(b)) return;
            if (_links.Any(l => (l.a == a && l.b == b) || (l.a == b && l.b == a))) return;
            _links.Add((a, b));
            Neighbors(a).Add(b);
            Neighbors(b).Add(a);
        }

        public IReadOnlyList<int> Neighbours(int nodeId) => Neighbors(nodeId);

        /// <summary>A special room reveals what it actually holds once the player is inside it (e.g. which event).</summary>
        public void SetKind(int nodeId, MinimapRoomKind kind)
        {
            var room = Room(nodeId);
            if (room == null || room.Kind == kind) return;
            room.Kind = kind;
            Raise();
        }

        /// <summary>
        /// The player entered this room: it becomes the current room, is marked visited, and its direct neighbours
        /// become discovered outlines. Re-entering the room the player is already in changes nothing.
        /// </summary>
        public bool MarkEntered(int nodeId)
        {
            var room = Room(nodeId);
            if (room == null) return false;
            var changed = CurrentNodeId != nodeId || !room.Visited;
            CurrentNodeId = nodeId;
            if (!room.Visited) { room.Visited = true; Entries++; }
            room.Discovered = true;
            foreach (var neighbour in Neighbors(nodeId))
            {
                var other = Room(neighbour);
                if (other != null && !other.Discovered) { other.Discovered = true; changed = true; }
            }

            if (changed) Raise();
            return changed;
        }

        private List<int> Neighbors(int nodeId)
        {
            if (!_neighbors.TryGetValue(nodeId, out var list)) { list = new List<int>(); _neighbors[nodeId] = list; }
            return list;
        }

        private void Raise() => Changed?.Invoke();
    }

    /// <summary>One placed minimap cell in panel pixels (origin top-left, y down), as the view draws it.</summary>
    public readonly struct MinimapCell
    {
        public MinimapCell(int nodeId, Vector2 center, Vector2 size)
        {
            NodeId = nodeId;
            Center = center;
            Size = size;
        }

        public int NodeId { get; }
        public Vector2 Center { get; }
        public Vector2 Size { get; }
    }

    /// <summary>
    /// Fits the discovered part of a room graph into the minimap panel. Pure arithmetic, so the placement is tested
    /// directly rather than through a screenshot.
    ///
    /// The discovered rooms are scaled to fill the panel; the scale is clamped so cells never become unreadably tiny.
    /// When the discovered graph no longer fits at the smallest readable scale, the view falls back to the
    /// neighbourhood around the current room (<see cref="LocalRadius"/> graph steps), which is what a player needs in
    /// the moment anyway.
    /// </summary>
    public static class MinimapLayout
    {
        /// <summary>Drawn size of one room cell, in panel pixels.</summary>
        public static readonly Vector2 CellSize = new(9f, 7f);
        /// <summary>Below this many panel pixels per layout tile the map is unreadable and the local view takes over.</summary>
        public const float MinScale = 0.22f;
        /// <summary>A two-room map must not spread across the whole panel; this caps how far apart cells can be drawn.</summary>
        public const float MaxScale = 1.1f;
        /// <summary>Graph steps kept around the current room when the whole discovered graph cannot be shown.</summary>
        public const int LocalRadius = 2;

        /// <summary>The rooms the panel will actually draw, already reduced to a readable neighbourhood if needed.</summary>
        public static List<MinimapRoom> VisibleRooms(MinimapModel model, Vector2 panelInner)
        {
            var all = model.DiscoveredRooms.ToList();
            if (all.Count == 0) return all;
            if (ScaleFor(all, panelInner) >= MinScale || !model.CurrentNodeId.HasValue) return all;

            var reachable = new HashSet<int> { model.CurrentNodeId.Value };
            var frontier = new List<int> { model.CurrentNodeId.Value };
            for (var step = 0; step < LocalRadius; step++)
            {
                var next = new List<int>();
                foreach (var id in frontier)
                foreach (var neighbour in model.Neighbours(id))
                {
                    var room = model.Room(neighbour);
                    if (room == null || !room.Discovered || !reachable.Add(neighbour)) continue;
                    next.Add(neighbour);
                }

                frontier = next;
            }

            return all.Where(r => reachable.Contains(r.NodeId)).ToList();
        }

        /// <summary>Panel pixels per layout tile for a set of rooms (0 when a single room or nothing is shown).</summary>
        public static float ScaleFor(IReadOnlyList<MinimapRoom> rooms, Vector2 panelInner)
        {
            if (rooms == null || rooms.Count <= 1) return MaxScale;
            var minX = rooms.Min(r => r.Center.x);
            var maxX = rooms.Max(r => r.Center.x);
            var minY = rooms.Min(r => r.Center.y);
            var maxY = rooms.Max(r => r.Center.y);
            var available = new Vector2(Mathf.Max(1f, panelInner.x - CellSize.x), Mathf.Max(1f, panelInner.y - CellSize.y));
            var spanX = Mathf.Max(0.0001f, maxX - minX);
            var spanY = Mathf.Max(0.0001f, maxY - minY);
            var scale = Mathf.Min(available.x / spanX, available.y / spanY);
            return Mathf.Min(scale, MaxScale);
        }

        /// <summary>
        /// Places the given rooms inside a panel of <paramref name="panelInner"/> pixels, centred on their own bounds.
        /// The returned centres are in panel pixels with the origin at the panel's top-left and y growing downward,
        /// which is how the HUD authors its rects; layout +y (north) therefore maps to screen −y (up).
        /// </summary>
        public static List<MinimapCell> Place(IReadOnlyList<MinimapRoom> rooms, Vector2 panelInner)
        {
            var cells = new List<MinimapCell>();
            if (rooms == null || rooms.Count == 0) return cells;
            var scale = ScaleFor(rooms, panelInner);
            var minX = rooms.Min(r => r.Center.x);
            var maxX = rooms.Max(r => r.Center.x);
            var minY = rooms.Min(r => r.Center.y);
            var maxY = rooms.Max(r => r.Center.y);
            var midX = (minX + maxX) * 0.5f;
            var midY = (minY + maxY) * 0.5f;
            // Rounding to whole pixels can push a cell half a pixel past the frame, so every centre is clamped to the
            // band where the whole cell fits. A cell is never drawn outside the panel, at any graph shape.
            var halfCell = CellSize * 0.5f;
            var minCentre = halfCell;
            var maxCentre = new Vector2(Mathf.Max(halfCell.x, panelInner.x - halfCell.x), Mathf.Max(halfCell.y, panelInner.y - halfCell.y));
            foreach (var room in rooms)
            {
                var x = panelInner.x * 0.5f + (room.Center.x - midX) * scale;
                var y = panelInner.y * 0.5f - (room.Center.y - midY) * scale; // layout north is screen up
                x = Mathf.Clamp(Mathf.Round(x), Mathf.Ceil(minCentre.x), Mathf.Floor(maxCentre.x));
                y = Mathf.Clamp(Mathf.Round(y), Mathf.Ceil(minCentre.y), Mathf.Floor(maxCentre.y));
                cells.Add(new MinimapCell(room.NodeId, new Vector2(x, y), CellSize));
            }

            return cells;
        }
    }
}
