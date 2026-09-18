using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Generation
{
    public sealed class DungeonGraphResult
    {
        private DungeonGraphResult(DungeonGraph graph, string error, int attempts)
        {
            Graph = graph;
            Error = error;
            Attempts = attempts;
        }

        public bool Success => Graph != null;
        public DungeonGraph Graph { get; }
        public string Error { get; }
        public int Attempts { get; }

        public static DungeonGraphResult Ok(DungeonGraph graph, int attempts) => new(graph, null, attempts);
        public static DungeonGraphResult Fail(string error, int attempts) => new(null, error, attempts);
    }

    /// <summary>
    /// Deterministic abstract layout generator for one depth (53_DUNGEON_GENERATOR). Draws exclusively from the
    /// Dungeon RNG stream derived from RunSeed + Depth, never touches prefabs or UnityEngine.Random, and discards any
    /// candidate that fails <see cref="DungeonGraphValidator"/> instead of patching it. Optional content is placed on branches.
    /// </summary>
    public sealed class DungeonGraphGenerator
    {
        private readonly DungeonGraphRules _rules;

        public DungeonGraphGenerator(DungeonGraphRules rules)
        {
            _rules = rules != null ? rules : throw new ArgumentNullException(nameof(rules));
        }

        public DungeonGraphResult Generate(int runSeed, int depth)
        {
            return Generate(runSeed, depth, RngStreams.Derive(runSeed, depth, RngStream.Dungeon));
        }

        /// <summary>Generates from an explicitly supplied source (tests / host-owned streams).</summary>
        public DungeonGraphResult Generate(int runSeed, int depth, IRandomSource random)
        {
            if (depth < 1)
            {
                return DungeonGraphResult.Fail($"Depth must be >= 1 (got {depth}).", 0);
            }

            string lastError = null;
            for (var attempt = 1; attempt <= _rules.MaxGenerationAttempts; attempt++)
            {
                var graph = BuildCandidate(runSeed, depth, random, out var buildError);
                if (graph == null)
                {
                    lastError = buildError;
                    continue;
                }

                var problems = DungeonGraphValidator.Validate(graph, _rules);
                if (problems.Count == 0)
                {
                    return DungeonGraphResult.Ok(graph, attempt);
                }

                lastError = string.Join(" | ", problems);
            }

            return DungeonGraphResult.Fail(
                $"Could not generate a valid graph for seed {runSeed} depth {depth} after {_rules.MaxGenerationAttempts} attempts. Last: {lastError}",
                _rules.MaxGenerationAttempts);
        }

        private DungeonGraph BuildCandidate(int runSeed, int depth, IRandomSource random, out string error)
        {
            error = null;
            var countRange = _rules.RoomCountRange(depth);
            var roomCount = random.NextInt(countRange.x, countRange.y);

            // Main path length must leave room for at least one branch and at most BranchCount.y × BranchLength.y branch rooms.
            var minBranchRooms = _rules.BranchCount.x * _rules.BranchLength.x;
            var maxBranchRooms = _rules.BranchCount.y * _rules.BranchLength.y;
            var mainOptions = Enumerable.Range(_rules.MainPathLength.x, _rules.MainPathLength.y - _rules.MainPathLength.x + 1)
                .Where(l => roomCount - l >= minBranchRooms && roomCount - l <= maxBranchRooms)
                .ToList();
            if (mainOptions.Count == 0)
            {
                error = $"No main-path length in {_rules.MainPathLength} fits {roomCount} rooms with branch limits.";
                return null;
            }

            var mainLength = mainOptions[random.NextInt(mainOptions.Count)];
            var branchRooms = roomCount - mainLength;

            var branchLengths = SplitIntoBranches(branchRooms, random);
            if (branchLengths == null)
            {
                error = $"Cannot split {branchRooms} branch rooms into {_rules.BranchCount} branches of {_rules.BranchLength}.";
                return null;
            }

            var graph = new DungeonGraph(runSeed, depth);

            // Main path: Start, Combat..., Boss.
            var mainIds = new List<int>();
            for (var i = 0; i < mainLength; i++)
            {
                var type = i == 0 ? RoomType.Start : i == mainLength - 1 ? RoomType.Boss : RoomType.Combat;
                mainIds.Add(graph.AddNode(type).Id);
                if (i > 0)
                {
                    graph.AddEdge(mainIds[i - 1], mainIds[i]);
                }
            }

            graph.SetMainPath(mainIds);

            // Branches attach to interior main-path rooms (never Start or Boss); distinct attachment points when possible.
            var interior = mainIds.Skip(1).Take(mainLength - 2).ToList();
            var attachPool = Shuffle(interior, random);
            var branchNodeIds = new List<int>();
            for (var b = 0; b < branchLengths.Count; b++)
            {
                var attachTo = attachPool[b % attachPool.Count];
                var ids = new List<int>();
                var previous = attachTo;
                for (var step = 0; step < branchLengths[b]; step++)
                {
                    var node = graph.AddNode(RoomType.Combat);
                    graph.AddEdge(previous, node.Id);
                    ids.Add(node.Id);
                    previous = node.Id;
                }

                graph.AddBranch(ids);
                branchNodeIds.AddRange(ids);
            }

            // Optional content goes to branch rooms: fill from the capped category pool, remaining branch rooms stay Combat.
            var optional = new List<RoomType>();
            optional.AddRange(Enumerable.Repeat(RoomType.Merchant, _rules.MaxMerchant));
            optional.AddRange(Enumerable.Repeat(RoomType.Event, _rules.MaxEvent));
            optional.AddRange(Enumerable.Repeat(RoomType.Loot, _rules.MaxLoot));
            optional.AddRange(Enumerable.Repeat(RoomType.Treasure, _rules.MaxTreasure));
            optional.AddRange(Enumerable.Repeat(RoomType.MedicalRecovery, _rules.MaxMedical));
            optional = Shuffle(optional, random);

            var combatSoFar = mainLength - 2;
            var maxExtraCombat = Math.Max(0, _rules.CombatRooms.y - combatSoFar);
            var extraCombat = random.NextInt(0, Math.Min(maxExtraCombat, branchNodeIds.Count));
            var branchOrder = Shuffle(branchNodeIds, random);
            var optionalIndex = 0;
            for (var i = 0; i < branchOrder.Count; i++)
            {
                var node = graph.GetNode(branchOrder[i]);
                if (i < extraCombat || optionalIndex >= optional.Count)
                {
                    node.Type = RoomType.Combat;
                    continue;
                }

                var candidate = optional[optionalIndex++];
                if (candidate == RoomType.Merchant && (graph.AreAdjacent(node.Id, graph.StartId) || graph.AreAdjacent(node.Id, graph.BossId)))
                {
                    node.Type = RoomType.Combat;
                    continue;
                }

                node.Type = candidate;
            }

            // Elite encounters: capped per depth, rolled per slot, only on Combat rooms not touching Start/Boss.
            var eliteCandidates = graph.NodesOfType(RoomType.Combat)
                .Where(n => !graph.AreAdjacent(n.Id, graph.StartId) && !graph.AreAdjacent(n.Id, graph.BossId))
                .ToList();
            eliteCandidates = Shuffle(eliteCandidates, random);
            var elites = 0;
            for (var slot = 0; slot < _rules.MaxElites(depth) && elites < eliteCandidates.Count; slot++)
            {
                if (random.NextInt(100) < _rules.EliteChancePercent(depth))
                {
                    eliteCandidates[elites].IsElite = true;
                    elites++;
                }
            }

            return graph;
        }

        private List<int> SplitIntoBranches(int rooms, IRandomSource random)
        {
            var minLen = _rules.BranchLength.x;
            var maxLen = _rules.BranchLength.y;
            var options = new List<int>();
            for (var count = _rules.BranchCount.x; count <= _rules.BranchCount.y; count++)
            {
                if (rooms >= count * minLen && rooms <= count * maxLen)
                {
                    options.Add(count);
                }
            }

            if (options.Count == 0)
            {
                return null;
            }

            var branchCount = options[random.NextInt(options.Count)];
            var lengths = Enumerable.Repeat(minLen, branchCount).ToList();
            var remaining = rooms - branchCount * minLen;
            while (remaining > 0)
            {
                var growable = Enumerable.Range(0, branchCount).Where(i => lengths[i] < maxLen).ToList();
                lengths[growable[random.NextInt(growable.Count)]]++;
                remaining--;
            }

            return lengths;
        }

        private static List<T> Shuffle<T>(IEnumerable<T> source, IRandomSource random)
        {
            var list = source.ToList();
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }

            return list;
        }
    }
}
