using RuinRail.Core;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>One graph node mapped to a room definition at an integer layout-grid offset (tiles).</summary>
    public sealed class RoomPlacement
    {
        public RoomPlacement(int nodeId, RoomDefinition definition, Vector2Int offset)
        {
            NodeId = nodeId;
            Definition = definition;
            Offset = offset;
        }

        public int NodeId { get; }
        public RoomDefinition Definition { get; }
        public Vector2Int Offset { get; }
        public RectInt Bounds => new(Offset, Definition.Dimensions);

        /// <summary>Local room cell → layout cell.</summary>
        public Vector2Int ToLayout(Vector2Int localCell) => Offset + localCell;
    }

    /// <summary>A realised graph edge: which sockets of which rooms are joined.</summary>
    public sealed class RoomConnection
    {
        public RoomConnection(int nodeA, DoorDirection socketA, int nodeB, DoorDirection socketB)
        {
            NodeA = nodeA;
            SocketA = socketA;
            NodeB = nodeB;
            SocketB = socketB;
        }

        public int NodeA { get; }
        public DoorDirection SocketA { get; }
        public int NodeB { get; }
        public DoorDirection SocketB { get; }

        public bool Joins(int a, int b) => (NodeA == a && NodeB == b) || (NodeA == b && NodeB == a);
    }

    /// <summary>Complete, validated assembly of one depth: placements + connections. Geometry stays inside the prefabs.</summary>
    public sealed class DungeonLayout
    {
        private readonly Dictionary<int, RoomPlacement> _placements = new();
        private readonly List<RoomConnection> _connections = new();
        private readonly List<string> _reusedRoomIds = new();

        public DungeonLayout(DungeonGraph graph, Biome biome)
        {
            Graph = graph;
            Biome = biome;
        }

        public DungeonGraph Graph { get; }
        public Biome Biome { get; }
        public IReadOnlyCollection<RoomPlacement> Placements => _placements.Values;
        public IReadOnlyList<RoomConnection> Connections => _connections;

        /// <summary>Room definition ids that had to be used more than once because the eligible pool was too small.</summary>
        public IReadOnlyList<string> ReusedRoomIds => _reusedRoomIds;

        public RoomPlacement GetPlacement(int nodeId) => _placements.TryGetValue(nodeId, out var p) ? p : null;

        public RoomPlacement StartPlacement => GetPlacement(Graph.StartId);
        public RoomPlacement BossPlacement => GetPlacement(Graph.BossId);

        internal void Add(RoomPlacement placement) => _placements[placement.NodeId] = placement;
        internal void Remove(int nodeId) => _placements.Remove(nodeId);
        internal void AddConnection(RoomConnection connection) => _connections.Add(connection);
        internal void RemoveConnectionsOf(int nodeId) => _connections.RemoveAll(c => c.NodeA == nodeId || c.NodeB == nodeId);
        internal void MarkReused(string roomId)
        {
            if (!_reusedRoomIds.Contains(roomId)) _reusedRoomIds.Add(roomId);
        }

        public bool IsSocketUsed(int nodeId, DoorDirection direction)
        {
            return _connections.Any(c => (c.NodeA == nodeId && c.SocketA == direction) || (c.NodeB == nodeId && c.SocketB == direction));
        }

        /// <summary>Layout bounding box in tiles.</summary>
        public RectInt TotalBounds()
        {
            if (_placements.Count == 0) return new RectInt(0, 0, 0, 0);
            var minX = _placements.Values.Min(p => p.Bounds.xMin);
            var minY = _placements.Values.Min(p => p.Bounds.yMin);
            var maxX = _placements.Values.Max(p => p.Bounds.xMax);
            var maxY = _placements.Values.Max(p => p.Bounds.yMax);
            return new RectInt(minX, minY, maxX - minX, maxY - minY);
        }

        public string Signature()
        {
            var rooms = string.Join(";", _placements.Values.OrderBy(p => p.NodeId).Select(p => $"{p.NodeId}={p.Definition.Id}@{p.Offset.x},{p.Offset.y}"));
            var links = string.Join(";", _connections.Select(c => $"{c.NodeA}.{c.SocketA}-{c.NodeB}.{c.SocketB}"));
            return $"biome={Biome} rooms=[{rooms}] links=[{links}]";
        }
    }
}
