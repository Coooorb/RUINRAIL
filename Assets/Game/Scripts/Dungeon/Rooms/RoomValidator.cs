using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace RuinRail.Dungeon.Rooms
{
    /// <summary>One room's validation outcome. Only <see cref="IsGeneratorReady"/> rooms may enter a generator pool.</summary>
    public sealed class RoomValidationReport
    {
        public RoomValidationReport(string roomId, IReadOnlyList<string> problems)
        {
            RoomId = roomId;
            Problems = problems;
        }

        public string RoomId { get; }
        public IReadOnlyList<string> Problems { get; }
        public bool IsGeneratorReady => Problems.Count == 0;
    }

    /// <summary>
    /// Full authored-room validation (dungeon/54_DUNGEON_VALIDATION.md, technical/115_EDITOR_VALIDATION_TOOLS.md):
    /// schema (layers, dimensions, sockets, markers) + walkable reachability between doors and anchors + spawn rules +
    /// room-type requirements + stable-id uniqueness across a set. Pure C#, so production audits and EditMode tests run
    /// it on prefabs without opening scenes. Every message names the room and, where possible, the grid coordinate.
    /// </summary>
    public static class RoomValidator
    {
        /// <summary>Approximate minimum distance (tiles, cell-centre Euclidean) between enemy spawns and player/start anchors.</summary>
        public const float MinEnemySpawnDistanceFromPlayerTiles = 5f;

        public static RoomValidationReport Validate(RoomDefinition definition)
        {
            var problems = RoomSchemaValidator.ValidateDefinition(definition);
            var id = definition != null ? definition.Id : "<null>";
            var root = definition != null && definition.Prefab != null ? definition.Prefab.GetComponent<RoomRoot>() : null;
            if (root != null)
            {
                problems.AddRange(ValidateRoomInternal(root, id));
            }

            return new RoomValidationReport(id, problems);
        }

        public static RoomValidationReport Validate(RoomRoot room)
        {
            var id = room != null && room.Definition != null ? room.Definition.Id : room != null ? room.name : "<null>";
            var problems = new List<string>();
            if (room != null && room.Definition != null)
            {
                problems.AddRange(RoomSchemaValidator.ValidateDefinition(room.Definition));
            }

            problems.AddRange(ValidateRoomInternal(room, id));
            return new RoomValidationReport(id, problems);
        }

        /// <summary>Validates every definition and rejects duplicate stable ids across the set.</summary>
        public static List<RoomValidationReport> ValidateAll(IEnumerable<RoomDefinition> definitions)
        {
            var list = definitions.Where(d => d != null).ToList();
            var reports = list.Select(Validate).ToList();

            foreach (var group in list.GroupBy(d => d.Id).Where(g => g.Count() > 1))
            {
                var names = string.Join(", ", group.Select(d => d.name));
                for (var i = 0; i < reports.Count; i++)
                {
                    if (reports[i].RoomId == group.Key)
                    {
                        var problems = reports[i].Problems.ToList();
                        problems.Add($"{group.Key}: duplicate stable room id used by assets [{names}].");
                        reports[i] = new RoomValidationReport(group.Key, problems);
                    }
                }
            }

            return reports;
        }

        private static List<string> ValidateRoomInternal(RoomRoot room, string id)
        {
            var problems = RoomSchemaValidator.ValidateRoom(room);
            if (room == null || room.Size.x <= 0 || room.Size.y <= 0)
            {
                return problems;
            }

            var logic = RoomLogicGrid.FromRoom(room);
            var sockets = room.GetSockets();
            var markers = room.GetMarkers();
            var anchors = new List<(string label, Vector2Int cell)>();

            // Door tiles must be open and lead into the room.
            foreach (var socket in sockets)
            {
                foreach (var cell in socket.Cells())
                {
                    if (!logic.IsWalkable(cell))
                    {
                        problems.Add($"{id}: {socket.Direction} door tile {cell} is not walkable (wall/obstacle/blocked or missing floor).");
                    }
                }

                var inward = socket.Cell - DoorDirections.Step(socket.Direction);
                if (logic.IsInside(inward) && !logic.IsWalkable(inward))
                {
                    problems.Add($"{id}: {socket.Direction} door at {socket.Cell} opens into a blocked cell {inward}.");
                }

                anchors.Add(($"{socket.Direction} door", socket.Cell));
            }

            // Spawn/anchor markers must sit on walkable cells (never inside walls/blocked cells).
            foreach (var marker in markers)
            {
                if (!RequiresWalkableCell(marker.Role))
                {
                    continue;
                }

                var rect = marker.Rect;
                for (var x = rect.xMin; x < rect.xMax; x++)
                {
                    for (var y = rect.yMin; y < rect.yMax; y++)
                    {
                        var cell = new Vector2Int(x, y);
                        if (logic.IsInside(cell) && !logic.IsWalkable(cell))
                        {
                            problems.Add($"{id}: {marker.Role} marker cell {cell} is inside a wall/obstacle/blocked cell.");
                        }
                    }
                }

                if (logic.IsWalkable(marker.Cell))
                {
                    anchors.Add(($"{marker.Role} marker", marker.Cell));
                }
            }

            // Reachability: every door and every anchor must share one walkable region.
            if (anchors.Count > 1)
            {
                var origin = anchors[0];
                var reachable = logic.Reachable(origin.cell);
                foreach (var anchor in anchors.Skip(1))
                {
                    if (!reachable.Contains(anchor.cell))
                    {
                        problems.Add($"{id}: {anchor.label} at {anchor.cell} is not reachable from {origin.label} at {origin.cell}.");
                    }
                }
            }
            else if (sockets.Count == 0)
            {
                problems.Add($"{id}: room has no door sockets to enter through.");
            }

            // Enemy spawn distance from player/start anchors.
            var playerSpawns = markers.Where(m => m.Role == RoomMarkerRole.PlayerSpawn).ToList();
            foreach (var enemy in markers.Where(m => m.Role == RoomMarkerRole.EnemySpawn))
            {
                foreach (var player in playerSpawns)
                {
                    var distance = Vector2.Distance(enemy.WorldCenter, player.WorldCenter);
                    if (distance < MinEnemySpawnDistanceFromPlayerTiles)
                    {
                        problems.Add($"{id}: EnemySpawn marker at {enemy.Cell} is {distance.ToString("0.0", CultureInfo.InvariantCulture)} tiles from PlayerSpawn at {player.Cell}; minimum is {MinEnemySpawnDistanceFromPlayerTiles.ToString("0", CultureInfo.InvariantCulture)}.");
                    }
                }
            }

            // Room-type requirements.
            if (room.Definition != null)
            {
                problems.AddRange(ValidateRoomTypeRequirements(room.Definition.RoomType, markers, id));
            }

            return problems;
        }

        private static IEnumerable<string> ValidateRoomTypeRequirements(RoomType type, IReadOnlyList<RoomMarker> markers, string id)
        {
            int Count(RoomMarkerRole role) => markers.Count(m => m.Role == role);

            switch (type)
            {
                case RoomType.Start:
                    if (Count(RoomMarkerRole.PlayerSpawn) == 0) yield return $"{id}: Start room requires at least one PlayerSpawn marker.";
                    if (Count(RoomMarkerRole.EnemySpawn) > 0) yield return $"{id}: Start room must not contain EnemySpawn markers (safe spawn).";
                    break;
                case RoomType.Combat:
                    if (Count(RoomMarkerRole.EnemySpawn) == 0) yield return $"{id}: Combat room requires at least one EnemySpawn marker.";
                    break;
                case RoomType.Boss:
                    if (Count(RoomMarkerRole.BossAnchor) != 1) yield return $"{id}: Boss room requires exactly one BossAnchor (found {Count(RoomMarkerRole.BossAnchor)}).";
                    break;
                case RoomType.Merchant:
                    if (Count(RoomMarkerRole.MerchantAnchor) != 1) yield return $"{id}: Merchant room requires exactly one MerchantAnchor (found {Count(RoomMarkerRole.MerchantAnchor)}).";
                    break;
                case RoomType.Event:
                case RoomType.MedicalRecovery:
                    if (Count(RoomMarkerRole.EventAnchor) == 0) yield return $"{id}: {type} room requires an EventAnchor marker.";
                    break;
                case RoomType.Loot:
                case RoomType.Treasure:
                    if (Count(RoomMarkerRole.ChestSpawn) + Count(RoomMarkerRole.LootSpawn) == 0) yield return $"{id}: {type} room requires a ChestSpawn or LootSpawn marker.";
                    break;
            }

            if (type != RoomType.Boss && Count(RoomMarkerRole.BossAnchor) > 0)
            {
                yield return $"{id}: only Boss rooms may contain a BossAnchor.";
            }

            // Hazard markers are gameplay: each needs an authored hazard definition on a RoomHazard next to it.
            foreach (var marker in markers.Where(m => m.Role == RoomMarkerRole.Hazard))
            {
                var hazard = marker.GetComponent<RoomHazard>();
                if (hazard == null)
                {
                    yield return $"{id}: Hazard marker at {marker.Cell} needs a RoomHazard component.";
                }
                else if (hazard.Definition == null)
                {
                    yield return $"{id}: Hazard marker at {marker.Cell} has no HazardDefinition.";
                }
            }
        }

        private static bool RequiresWalkableCell(RoomMarkerRole role)
        {
            return role switch
            {
                RoomMarkerRole.Walkable => false,
                RoomMarkerRole.Blocked => false,
                RoomMarkerRole.Reserved => false,
                RoomMarkerRole.Hazard => false,
                _ => true
            };
        }
    }
}
