using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Grid;
using RuinRail.Dungeon.Rooms;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Where an encounter's reward chest stands: the room's playable centre. The walkable cell (floor, no wall,
    /// obstacle or blocked/reserved footprint) reachable from the doors that lies nearest the geometric centre of the
    /// room, never on a hazard footprint and never on an interactable anchor (the boss room's Transit Car). Ties break
    /// by row then column, so every peer picks the same cell. No room coordinates are authored for it.
    /// </summary>
    public static class EncounterRewardPlacement
    {
        /// <summary>Anchors a chest must not share a cell or an edge with.</summary>
        private static readonly RoomMarkerRole[] KeepClear =
            { RoomMarkerRole.InteractableSpawn, RoomMarkerRole.EventAnchor, RoomMarkerRole.MerchantAnchor, RoomMarkerRole.Hazard };

        public static Vector2 GeometricCenterCell(RoomRoot root) => new((root.Size.x - 1) * 0.5f, (root.Size.y - 1) * 0.5f);

        public static Vector2Int? CenterCell(RoomRoot root, RoomLogicGrid grid = null)
        {
            if (root == null) return null;
            grid ??= RoomLogicGrid.FromRoom(root);
            // A runtime room with no authored floor data at all (no tilemaps): nothing to avoid, so the geometric centre.
            if (grid.WalkableCount == 0) return Vector2Int.FloorToInt(GeometricCenterCell(root) + new Vector2(0.5f, 0.5f));
            var reachable = SupplyChestPlacement.ReachableFromDoors(grid, root.GetSockets());
            var blocked = BlockedCells(root);
            var centre = GeometricCenterCell(root);
            Vector2Int? best = null;
            var bestDistance = float.MaxValue;
            for (var y = 0; y < root.Size.y; y++)
            {
                for (var x = 0; x < root.Size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!grid.IsWalkable(cell) || !reachable.Contains(cell) || blocked.Contains(cell)) continue;
                    var distance = (cell - centre).sqrMagnitude;
                    if (distance >= bestDistance - 0.0001f) continue; // strictly nearer only: the first (lowest row, column) wins ties
                    bestDistance = distance;
                    best = cell;
                }
            }

            return best;
        }

        /// <summary>World position of the playable centre (null when the room has no reachable floor at all).</summary>
        public static Vector2? WorldCenter(RoomRoot root)
        {
            var cell = CenterCell(root);
            return cell.HasValue ? SupplyChestPlacement.WorldCenter(root, cell.Value) : (Vector2?)null;
        }

        private static HashSet<Vector2Int> BlockedCells(RoomRoot root)
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var marker in root.GetMarkers().Where(m => Array.IndexOf(KeepClear, m.Role) >= 0))
            {
                // Hazards: their footprint. Anchors: their footprint plus the ring around it (the object stands there).
                var pad = marker.Role == RoomMarkerRole.Hazard ? 0 : 1;
                var rect = marker.Rect;
                for (var x = rect.xMin - pad; x < rect.xMax + pad; x++)
                    for (var y = rect.yMin - pad; y < rect.yMax + pad; y++)
                        cells.Add(new Vector2Int(x, y));
            }

            var hazards = RoomGridBuilder.FindLayer(root.Grid, RoomTilemapLayer.Hazards);
            if (hazards != null)
            {
                for (var x = 0; x < root.Size.x; x++)
                    for (var y = 0; y < root.Size.y; y++)
                        if (hazards.HasTile(new Vector3Int(x, y, 0))) cells.Add(new Vector2Int(x, y));
            }

            return cells;
        }
    }

    /// <summary>
    /// The encounter reward (45 Elite mini-boss, 46/58 boss): exactly one existing chest, created only once the room's
    /// encounter is won, at the room's playable centre (<see cref="EncounterRewardPlacement"/>).
    ///
    /// One trigger for every peer: the room being won. The host learns it from the encounter itself (the room clears
    /// once when the Elite or boss dies; a boss also reports its defeat directly); a co-op client learns it from the
    /// host's replicated room state (<see cref="RoomRuntime.RestoreState"/>); a room composed already won (rebuild,
    /// reconnect) gets its chest at once. Every peer builds its copy at the same cell from the same loot seed, the
    /// host alone resolves opening (the loot authority), and the opened state replicates under
    /// <see cref="ResolvedId"/> — so every player sees one authoritative chest. Repeated signals find the chest already
    /// there and do nothing.
    /// </summary>
    public sealed class EncounterRewardChest : MonoBehaviour
    {
        private RoomRuntime _room;
        private RoomContentBinding _binding;
        private Func<bool> _won;
        private Func<Vector2, SupplyChest> _create;

        public string ResolvedId { get; private set; }
        public SupplyChest Chest { get; private set; }
        /// <summary>How many chests this encounter has created (diagnostics; never more than one).</summary>
        public int SpawnCount { get; private set; }

        public static EncounterRewardChest Bind(RoomRuntime room, RoomContentBinding binding, string resolvedId, Func<bool> won, Func<Vector2, SupplyChest> create)
        {
            var reward = room.gameObject.AddComponent<EncounterRewardChest>();
            reward._room = room;
            reward._binding = binding;
            reward._won = won;
            reward._create = create;
            reward.ResolvedId = resolvedId;
            room.Cleared += reward.OnCleared;
            room.StateRestored += reward.OnStateRestored;
            reward.TrySpawn();
            return reward;
        }

        private void OnCleared(RoomRuntime room, RoomClearedContext context) => TrySpawn();
        private void OnStateRestored(RoomRuntime room) => TrySpawn();

        /// <summary>Creates the chest if the encounter is won and it does not exist yet; returns the chest (or null).</summary>
        public SupplyChest TrySpawn()
        {
            if (Chest != null || _room == null || _won == null || !_won()) return Chest;
            var position = EncounterRewardPlacement.WorldCenter(_room.Root);
            if (!position.HasValue)
            {
                _binding.Skipped.Add("reward_chest:no_playable_center");
                return null;
            }

            Chest = _create(position.Value);
            if (Chest == null) return null;
            SpawnCount++;
            if (_room.State.IsResolved(ResolvedId)) Chest.RestoreOpened();
            var room = _room;
            var id = ResolvedId;
            Chest.Opened += (_, _) => room.State.MarkResolved(id);
            _binding.AddRewardChest(Chest, ResolvedId);
            return Chest;
        }

        private void OnDestroy()
        {
            if (_room == null) return;
            _room.Cleared -= OnCleared;
            _room.StateRestored -= OnStateRestored;
        }
    }
}
