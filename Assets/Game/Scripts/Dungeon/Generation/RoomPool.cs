using RuinRail.Core;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// The generator-ready room definitions of one biome. <see cref="Build"/> runs the full room validator and keeps
    /// only ready rooms (rejections are listed with reasons); <see cref="FromTrusted"/> skips validation for callers
    /// that already validated (tests, cached production audits).
    /// </summary>
    public sealed class RoomPool
    {
        private readonly List<RoomDefinition> _rooms;

        private RoomPool(Biome biome, List<RoomDefinition> rooms, List<RoomValidationReport> rejected)
        {
            Biome = biome;
            _rooms = rooms;
            Rejected = rejected;
        }

        public Biome Biome { get; }
        public IReadOnlyList<RoomDefinition> Rooms => _rooms;
        public IReadOnlyList<RoomValidationReport> Rejected { get; }

        public static RoomPool Build(IEnumerable<RoomDefinition> definitions, Biome biome)
        {
            var candidates = definitions.Where(d => d != null && d.Biome == biome).ToList();
            var reports = RoomValidator.ValidateAll(candidates);
            var ready = new List<RoomDefinition>();
            var rejected = new List<RoomValidationReport>();
            for (var i = 0; i < candidates.Count; i++)
            {
                if (reports[i].IsGeneratorReady) ready.Add(candidates[i]);
                else rejected.Add(reports[i]);
            }

            return new RoomPool(biome, ready.OrderBy(r => r.Id).ToList(), rejected);
        }

        public static RoomPool FromTrusted(IEnumerable<RoomDefinition> definitions, Biome biome)
        {
            return new RoomPool(biome, definitions.Where(d => d != null && d.Biome == biome).OrderBy(r => r.Id).ToList(), new List<RoomValidationReport>());
        }

        public IEnumerable<RoomDefinition> OfType(RoomType type, bool requireElite = false)
        {
            return _rooms.Where(r => r.RoomType == type && (!requireElite || r.SupportsElite));
        }

        public IEnumerable<RoomDefinition> AvailableAtDepth(int depth)
        {
            return _rooms.Where(r => r.IsAvailableAtDepth(depth));
        }
    }
}
