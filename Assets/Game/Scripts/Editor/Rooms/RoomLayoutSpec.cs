using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using UnityEngine;

namespace RuinRail.EditorTools.Rooms
{
    /// <summary>
    /// A handmade room layout written as ASCII rows (top row first) plus its metadata (52). The baker paints it into
    /// the required tilemap layers and authors sockets/markers from the legend — the prefab is the finished, static
    /// geometry; nothing is generated at runtime.
    ///
    /// Legend: '#' wall, '.' floor, ',' floor + floor detail, 'O' obstacle, '~' hazard (electrified rail), 'D' door
    /// cell (on an edge; two adjacent D cells form one socket), 'P' PlayerSpawn, 'E' EnemySpawn, 'C' ChestSpawn,
    /// 'L' LootSpawn, 'M' MerchantAnchor, 'V' EventAnchor, 'B' BossAnchor, 'I' InteractableSpawn, 'T' TrapSpawn.
    /// Marker cells are floor underneath.
    /// </summary>
    public sealed class RoomLayoutSpec
    {
        public RoomLayoutSpec(string id, Biome biome, RoomType type, RoomSizeClass sizeClass, string[] rows, int difficulty = 1, float weight = 1f, bool supportsElite = false, int minDepth = 1, int maxDepth = 0, params string[] tags)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Biome = biome;
            RoomType = type;
            SizeClass = sizeClass;
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
            Difficulty = difficulty;
            Weight = weight;
            SupportsElite = supportsElite;
            MinDepth = minDepth;
            MaxDepth = maxDepth;
            Tags = tags ?? Array.Empty<string>();
            Size = RoomSizeClasses.DimensionsOf(sizeClass);
            Validate();
        }

        public string Id { get; }
        public Biome Biome { get; }
        public RoomType RoomType { get; }
        public RoomSizeClass SizeClass { get; }
        public Vector2Int Size { get; }
        public string[] Rows { get; }
        public int Difficulty { get; }
        public float Weight { get; }
        public bool SupportsElite { get; }
        public int MinDepth { get; }
        public int MaxDepth { get; }
        public IReadOnlyList<string> Tags { get; }

        /// <summary>Hazard definition asset painted for every '~' strip; null = the baker default (Electrified Rail).</summary>
        public string HazardDefinitionPath { get; set; }

        /// <summary>Character at a grid cell (y = 0 is the bottom row).</summary>
        public char At(int x, int y) => Rows[Size.y - 1 - y][x];

        public bool IsWall(int x, int y) => At(x, y) == '#';
        public bool IsObstacle(int x, int y) => At(x, y) == 'O';
        public bool IsHazard(int x, int y) => At(x, y) == '~';
        public bool IsDoor(int x, int y) => At(x, y) == 'D';
        public bool IsFloorDetail(int x, int y) => At(x, y) == ',';
        public bool HasFloor(int x, int y) => !IsWall(x, y);

        public static RoomMarkerRole? MarkerRoleOf(char c) => c switch
        {
            'P' => RoomMarkerRole.PlayerSpawn,
            'E' => RoomMarkerRole.EnemySpawn,
            'C' => RoomMarkerRole.ChestSpawn,
            'L' => RoomMarkerRole.LootSpawn,
            'M' => RoomMarkerRole.MerchantAnchor,
            'V' => RoomMarkerRole.EventAnchor,
            'B' => RoomMarkerRole.BossAnchor,
            'I' => RoomMarkerRole.InteractableSpawn,
            'T' => RoomMarkerRole.TrapSpawn,
            _ => null
        };

        public IEnumerable<(RoomMarkerRole role, Vector2Int cell)> Markers()
        {
            for (var y = 0; y < Size.y; y++)
            for (var x = 0; x < Size.x; x++)
            {
                var role = MarkerRoleOf(At(x, y));
                if (role.HasValue) yield return (role.Value, new Vector2Int(x, y));
            }
        }

        /// <summary>Door sockets derived from runs of 'D' cells along each edge (socket cell = the run's min cell).</summary>
        public IEnumerable<(DoorDirection direction, Vector2Int cell, int width)> Sockets()
        {
            var w = Size.x;
            var h = Size.y;
            foreach (var run in Runs(x => IsDoor(x, h - 1), w)) yield return (DoorDirection.North, new Vector2Int(run.start, h - 1), run.length);
            foreach (var run in Runs(x => IsDoor(x, 0), w)) yield return (DoorDirection.South, new Vector2Int(run.start, 0), run.length);
            foreach (var run in Runs(y => IsDoor(w - 1, y), h)) yield return (DoorDirection.East, new Vector2Int(w - 1, run.start), run.length);
            foreach (var run in Runs(y => IsDoor(0, y), h)) yield return (DoorDirection.West, new Vector2Int(0, run.start), run.length);
        }

        /// <summary>Horizontal hazard runs (one Hazard marker with a (length, 1) footprint each).</summary>
        public IEnumerable<RectInt> HazardStrips()
        {
            for (var y = 0; y < Size.y; y++)
            foreach (var run in Runs(x => IsHazard(x, y), Size.x)) yield return new RectInt(run.start, y, run.length, 1);
        }

        public IEnumerable<DoorDirection> SupportedDoors() => Sockets().Select(s => s.direction).Distinct();

        private static IEnumerable<(int start, int length)> Runs(Func<int, bool> predicate, int count)
        {
            var i = 0;
            while (i < count)
            {
                if (!predicate(i)) { i++; continue; }
                var start = i;
                while (i < count && predicate(i)) i++;
                yield return (start, i - start);
            }
        }

        private void Validate()
        {
            if (Rows.Length != Size.y) throw new ArgumentException($"{Id}: expected {Size.y} rows, got {Rows.Length}.");
            for (var i = 0; i < Rows.Length; i++)
            {
                if (Rows[i].Length != Size.x) throw new ArgumentException($"{Id}: row {i} has {Rows[i].Length} columns, expected {Size.x}.");
            }

            foreach (var socket in Sockets())
            {
                if (socket.width != DoorSocket.DefaultWidth) throw new ArgumentException($"{Id}: {socket.direction} door run at {socket.cell} is {socket.width} wide; sockets are {DoorSocket.DefaultWidth} wide.");
            }

            for (var y = 0; y < Size.y; y++)
            for (var x = 0; x < Size.x; x++)
            {
                if (IsDoor(x, y) && x != 0 && y != 0 && x != Size.x - 1 && y != Size.y - 1) throw new ArgumentException($"{Id}: door cell ({x},{y}) is not on an edge.");
            }
        }
    }
}
