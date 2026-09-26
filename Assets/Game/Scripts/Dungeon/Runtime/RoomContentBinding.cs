using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// What the category composer bound into a room (for UI, tests and sync): chests, merchant, event, boss pieces.
    /// Pure record of references; every behaviour lives in the bound objects themselves.
    /// </summary>
    public sealed class RoomContentBinding : MonoBehaviour
    {
        public readonly List<SupplyChest> Chests = new();
        /// <summary>Room-local cell of the ordinary-room Supply Chest (null when the room has none or no valid cell existed).</summary>
        public Vector2Int? SupplyChestCell { get; internal set; }
        /// <summary>The ordinary-room Supply Chest the planner placed here, if any.</summary>
        public SupplyChest SupplyChest => Chests.Find(c => c != null && c.Kind == LootSourceKind.SupplyChest && c != RewardChest);
        public DungeonMerchantInteractable Merchant { get; internal set; }
        public DungeonEventInteractable Event { get; internal set; }
        public IDungeonEvent EventInstance { get; internal set; }
        public BossEncounter Boss { get; internal set; }
        public SupplyChest BossCache { get; internal set; }
        public TransitCar Transit { get; internal set; }
        public readonly List<string> Skipped = new();

        /// <summary>The encounter reward chest (Elite: a Supply Chest; boss: the Boss Cache) once the encounter is won.</summary>
        public SupplyChest RewardChest { get; private set; }
        /// <summary>The room-state id its opened state replicates under.</summary>
        public string RewardChestResolvedId { get; private set; }
        /// <summary>Raised once when the reward chest appears (audio and presentation hooks bind to it here).</summary>
        public event Action<SupplyChest> RewardChestSpawned;

        internal void AddRewardChest(SupplyChest chest, string resolvedId)
        {
            RewardChest = chest;
            RewardChestResolvedId = resolvedId;
            if (chest.Kind == LootSourceKind.BossCache) BossCache = chest;
            Chests.Add(chest);
            RewardChestSpawned?.Invoke(chest);
        }

        public static RoomContentBinding For(RoomRuntime room)
        {
            var existing = room.GetComponent<RoomContentBinding>();
            return existing != null ? existing : room.gameObject.AddComponent<RoomContentBinding>();
        }
    }
}
