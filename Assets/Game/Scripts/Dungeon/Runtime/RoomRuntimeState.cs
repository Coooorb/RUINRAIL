using System;
using System.Collections.Generic;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>55 Combat Room lifecycle: Unentered → Active (doors locked, encounter running) → Cleared. Exactly once.</summary>
    public enum RoomLifecycleState
    {
        Unentered,
        Active,
        Cleared
    }

    /// <summary>
    /// Plain, serializable per-room runtime state (the host's authoritative copy; later synchronized to clients).
    /// Everything a client needs to reproduce the room: identity, lifecycle, the encounter it ran and its outcome.
    /// </summary>
    [Serializable]
    public sealed class RoomRuntimeState
    {
        public int NodeId;
        public string RoomId;
        public RoomType RoomType;
        public bool IsElite;
        public RoomLifecycleState State;
        public string EncounterSignature;
        public int EntryCount;
        public int EnemiesSpawned;
        public int EnemiesDefeated;

        /// <summary>Ids of one-time interactions already resolved in this room (chest:0, event:cursed_chest, boss_cache, ...).</summary>
        public List<string> Resolved = new();

        public bool IsResolved(string id) => !string.IsNullOrEmpty(id) && Resolved.Contains(id);

        public bool MarkResolved(string id)
        {
            if (string.IsNullOrEmpty(id) || Resolved.Contains(id)) return false;
            Resolved.Add(id);
            return true;
        }

        public bool IsCleared => State == RoomLifecycleState.Cleared;
        public bool IsActive => State == RoomLifecycleState.Active;

        public RoomRuntimeState Clone()
        {
            var clone = (RoomRuntimeState)MemberwiseClone();
            clone.Resolved = new List<string>(Resolved);
            return clone;
        }
    }

    /// <summary>Identity/context of a room clear, carried by the clear event for passives, UI and audio.</summary>
    public readonly struct RoomClearedContext
    {
        public RoomClearedContext(int nodeId, string roomId, RoomType roomType, bool isElite, int depth, int enemiesDefeated)
        {
            NodeId = nodeId;
            RoomId = roomId;
            RoomType = roomType;
            IsElite = isElite;
            Depth = depth;
            EnemiesDefeated = enemiesDefeated;
        }

        public int NodeId { get; }
        public string RoomId { get; }
        public RoomType RoomType { get; }
        public bool IsElite { get; }
        public int Depth { get; }
        public int EnemiesDefeated { get; }
        public bool IsCombat => RoomType == RoomType.Combat;
    }
}
