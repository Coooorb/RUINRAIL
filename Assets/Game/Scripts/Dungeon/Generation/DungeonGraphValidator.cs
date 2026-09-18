using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// Enforces the approved graph rules (53_DUNGEON_GENERATOR "Required Rules", room count/path/branch targets,
    /// 55_ROOM_TYPES category ranges). The generator discards and re-rolls anything that fails here.
    /// </summary>
    public static class DungeonGraphValidator
    {
        public static List<string> Validate(DungeonGraph graph, DungeonGraphRules rules)
        {
            var problems = new List<string>();
            if (graph == null)
            {
                problems.Add("Graph is null.");
                return problems;
            }

            var count = graph.Nodes.Count;
            var countRange = rules.RoomCountRange(graph.Depth);
            if (count < countRange.x || count > countRange.y)
            {
                problems.Add($"Room count {count} outside depth {graph.Depth} target {countRange.x}-{countRange.y}.");
            }

            var starts = graph.NodesOfType(RoomType.Start).ToList();
            var bosses = graph.NodesOfType(RoomType.Boss).ToList();
            if (starts.Count != 1) problems.Add($"Expected exactly 1 Start room, found {starts.Count}.");
            if (bosses.Count != 1) problems.Add($"Expected exactly 1 Boss room, found {bosses.Count}.");

            var main = graph.MainPath;
            if (main.Count < rules.MainPathLength.x || main.Count > rules.MainPathLength.y)
            {
                problems.Add($"Main path length {main.Count} outside {rules.MainPathLength.x}-{rules.MainPathLength.y}.");
            }

            if (main.Count >= 2)
            {
                if (graph.GetNode(main[0]).Type != RoomType.Start) problems.Add("Main path must begin with the Start room.");
                if (graph.GetNode(main[main.Count - 1]).Type != RoomType.Boss) problems.Add("Main path must end with the Boss room.");
                for (var i = 0; i + 1 < main.Count; i++)
                {
                    if (!graph.AreAdjacent(main[i], main[i + 1])) problems.Add($"Main path nodes {main[i]} and {main[i + 1]} are not connected.");
                }
            }

            if (starts.Count == 1 && bosses.Count == 1 && graph.AreAdjacent(starts[0].Id, bosses[0].Id))
            {
                problems.Add("Boss must not be adjacent to Start.");
            }

            var branches = graph.Branches;
            if (branches.Count < rules.BranchCount.x || branches.Count > rules.BranchCount.y)
            {
                problems.Add($"Branch count {branches.Count} outside {rules.BranchCount.x}-{rules.BranchCount.y}.");
            }

            for (var b = 0; b < branches.Count; b++)
            {
                var length = branches[b].Count;
                if (length < rules.BranchLength.x || length > rules.BranchLength.y)
                {
                    problems.Add($"Branch {b} length {length} outside {rules.BranchLength.x}-{rules.BranchLength.y}.");
                }

                foreach (var id in branches[b])
                {
                    if (graph.GetNode(id).IsOnMainPath) problems.Add($"Branch {b} room {id} is also on the main path.");
                }
            }

            var mainSet = new HashSet<int>(main);
            var branchSet = new HashSet<int>(branches.SelectMany(b => b));
            if (mainSet.Count + branchSet.Count != count || mainSet.Overlaps(branchSet))
            {
                problems.Add("Every room must belong to exactly one of: main path, one branch.");
            }

            // Reachability from Start over undirected edges.
            if (starts.Count == 1)
            {
                var reachable = Reachable(graph, starts[0].Id);
                if (reachable.Count != count) problems.Add($"Only {reachable.Count}/{count} rooms reachable from Start.");
            }

            // Category rules.
            foreach (var merchant in graph.NodesOfType(RoomType.Merchant))
            {
                if (starts.Any(s => graph.AreAdjacent(merchant.Id, s.Id)) || bosses.Any(b => graph.AreAdjacent(merchant.Id, b.Id)))
                {
                    problems.Add($"Merchant room {merchant.Id} is adjacent to Start or Boss.");
                }
            }

            foreach (var elite in graph.Nodes.Where(n => n.IsElite))
            {
                if (elite.Type != RoomType.Combat) problems.Add($"Elite flag on non-Combat room {elite.Id}.");
                if (starts.Any(s => graph.AreAdjacent(elite.Id, s.Id))) problems.Add($"Elite room {elite.Id} is directly after Start.");
                if (bosses.Any(b => graph.AreAdjacent(elite.Id, b.Id))) problems.Add($"Elite room {elite.Id} is directly before Boss.");
            }

            var combat = graph.NodesOfType(RoomType.Combat).Count();
            if (combat < rules.CombatRooms.x || combat > rules.CombatRooms.y)
            {
                problems.Add($"Combat room count {combat} outside {rules.CombatRooms.x}-{rules.CombatRooms.y}.");
            }

            CheckMax(problems, graph, RoomType.Merchant, rules.MaxMerchant);
            CheckMax(problems, graph, RoomType.Event, rules.MaxEvent);
            CheckMax(problems, graph, RoomType.Loot, rules.MaxLoot);
            CheckMax(problems, graph, RoomType.Treasure, rules.MaxTreasure);
            CheckMax(problems, graph, RoomType.MedicalRecovery, rules.MaxMedical);

            var elites = graph.Nodes.Count(n => n.IsElite);
            if (elites > rules.MaxElites(graph.Depth))
            {
                problems.Add($"Elite count {elites} exceeds depth {graph.Depth} maximum {rules.MaxElites(graph.Depth)}.");
            }

            return problems;
        }

        private static void CheckMax(List<string> problems, DungeonGraph graph, RoomType type, int max)
        {
            var n = graph.NodesOfType(type).Count();
            if (n > max) problems.Add($"{type} room count {n} exceeds maximum {max}.");
        }

        public static HashSet<int> Reachable(DungeonGraph graph, int start)
        {
            var visited = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in graph.GetNode(current).Neighbors)
                {
                    if (visited.Add(next)) queue.Enqueue(next);
                }
            }

            return visited;
        }
    }
}
