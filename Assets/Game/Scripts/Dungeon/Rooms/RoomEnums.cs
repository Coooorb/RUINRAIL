using System;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{

    /// <summary>Approved room categories (dungeon/55_ROOM_TYPES.md). Elite is a flag on compatible Combat rooms, not a type.</summary>
    public enum RoomType
    {
        Start,
        Combat,
        Loot,
        Treasure,
        Merchant,
        Event,
        MedicalRecovery,
        Boss
    }

    /// <summary>V1 authored size classes (dungeon/51_ROOM_AUTHORING.md).</summary>
    public enum RoomSizeClass
    {
        Small,
        Medium,
        Large,
        Boss
    }

    public static class RoomSizeClasses
    {
        public static Vector2Int DimensionsOf(RoomSizeClass sizeClass)
        {
            return sizeClass switch
            {
                RoomSizeClass.Small => new Vector2Int(16, 12),
                RoomSizeClass.Medium => new Vector2Int(24, 16),
                RoomSizeClass.Large => new Vector2Int(32, 20),
                RoomSizeClass.Boss => new Vector2Int(36, 24),
                _ => throw new ArgumentOutOfRangeException(nameof(sizeClass))
            };
        }
    }

    /// <summary>Cardinal door sockets only; no diagonal connections.</summary>
    public enum DoorDirection
    {
        North,
        South,
        East,
        West
    }

    public static class DoorDirections
    {
        public static DoorDirection Opposite(DoorDirection direction)
        {
            return direction switch
            {
                DoorDirection.North => DoorDirection.South,
                DoorDirection.South => DoorDirection.North,
                DoorDirection.East => DoorDirection.West,
                DoorDirection.West => DoorDirection.East,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }

        public static Vector2Int Step(DoorDirection direction)
        {
            return direction switch
            {
                DoorDirection.North => Vector2Int.up,
                DoorDirection.South => Vector2Int.down,
                DoorDirection.East => Vector2Int.right,
                DoorDirection.West => Vector2Int.left,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }

        public static bool IsHorizontalEdge(DoorDirection direction)
        {
            return direction == DoorDirection.North || direction == DoorDirection.South;
        }
    }

    /// <summary>Authoring marker roles (dungeon/51_ROOM_AUTHORING.md). The generator only ever uses legal markers.</summary>
    public enum RoomMarkerRole
    {
        Walkable,
        Blocked,
        Reserved,
        PlayerSpawn,
        EnemySpawn,
        LootSpawn,
        ChestSpawn,
        TrapSpawn,
        InteractableSpawn,
        EventAnchor,
        MerchantAnchor,
        BossAnchor,
        Hazard
    }
}
