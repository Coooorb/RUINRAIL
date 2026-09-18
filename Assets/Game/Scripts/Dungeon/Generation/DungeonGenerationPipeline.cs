using System.Collections.Generic;
using RuinRail.Core.Rng;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>Outcome of the full graph → assembly → validation pipeline for one depth.</summary>
    public sealed class DungeonGenerationResult
    {
        private DungeonGenerationResult(DungeonLayout layout, DungeonGraph graph, int rounds, string error, IReadOnlyList<string> discarded)
        {
            Layout = layout;
            Graph = graph;
            Rounds = rounds;
            Error = error;
            Discarded = discarded;
        }

        public bool Success => Layout != null;
        public DungeonLayout Layout { get; }
        public DungeonGraph Graph { get; }

        /// <summary>1 = first graph assembled; more = earlier layouts were discarded and regenerated (53).</summary>
        public int Rounds { get; }

        public string Error { get; }
        public IReadOnlyList<string> Discarded { get; }

        public static DungeonGenerationResult Ok(DungeonLayout layout, DungeonGraph graph, int rounds, List<string> discarded) => new(layout, graph, rounds, null, discarded);
        public static DungeonGenerationResult Fail(string error, int rounds, List<string> discarded) => new(null, null, rounds, error, discarded);
    }

    /// <summary>
    /// 53 "Generation Failure": if a layout cannot be assembled or fails validation, discard it and regenerate — never
    /// patch it. Each round derives fresh Dungeon/Assembly streams from RunSeed + Depth + round, so the outcome for a
    /// seed is deterministic and the first round equals the plain single-shot generation.
    /// </summary>
    public static class DungeonGenerationPipeline
    {
        public const int DefaultMaxRounds = 8;

        /// <param name="firstRound">Round to start at (1 = the plain single-shot generation); a caller that rejected an
        /// earlier round after instantiation (scene-level validation) continues from the next one, so the reroll stays
        /// deterministic for the seed.</param>
        public static DungeonGenerationResult Generate(DungeonGraphGenerator generator, RoomPool pool, int runSeed, int depth, int maxRounds = DefaultMaxRounds, int firstRound = 1)
        {
            var discarded = new List<string>();
            for (var round = System.Math.Max(1, firstRound); round <= maxRounds; round++)
            {
                var graphRandom = round == 1
                    ? RngStreams.Derive(runSeed, depth, RngStream.Dungeon)
                    : new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Dungeon, round));
                var graphResult = generator.Generate(runSeed, depth, graphRandom);
                if (!graphResult.Success)
                {
                    discarded.Add($"round {round}: graph: {graphResult.Error}");
                    continue;
                }

                var assemblyRandom = round == 1
                    ? RngStreams.Derive(runSeed, depth, RngStream.Assembly)
                    : new SeededRandom(SeededRandom.MixSeed(runSeed, depth, (int)RngStream.Assembly, round));
                var assembly = new DungeonAssembler(pool, assemblyRandom).Assemble(graphResult.Graph);
                if (!assembly.Success)
                {
                    discarded.Add($"round {round}: assembly: {assembly.Error}");
                    continue;
                }

                var problems = DungeonLayoutValidator.Validate(assembly.Layout);
                if (problems.Count > 0)
                {
                    discarded.Add($"round {round}: validation: {string.Join(" | ", problems)}");
                    continue;
                }

                return DungeonGenerationResult.Ok(assembly.Layout, graphResult.Graph, round, discarded);
            }

            return DungeonGenerationResult.Fail($"No valid dungeon for seed {runSeed} depth {depth} after {maxRounds} rounds.", maxRounds, discarded);
        }
    }
}
