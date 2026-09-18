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
        public SupplyChest SupplyChest => Chests.Find(c => c != null && c.Kind == LootSourceKind.SupplyChest);
        public DungeonMerchantInteractable Merchant { get; internal set; }
        public DungeonEventInteractable Event { get; internal set; }
        public IDungeonEvent EventInstance { get; internal set; }
        public BossEncounter Boss { get; internal set; }
        public SupplyChest BossCache { get; internal set; }
        public TransitCar Transit { get; internal set; }
        public readonly List<string> Skipped = new();

        public static RoomContentBinding For(RoomRuntime room)
        {
            var existing = room.GetComponent<RoomContentBinding>();
            return existing != null ? existing : room.gameObject.AddComponent<RoomContentBinding>();
        }
    }
}
