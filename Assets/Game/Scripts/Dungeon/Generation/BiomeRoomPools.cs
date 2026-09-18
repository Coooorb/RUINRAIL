using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;

namespace RuinRail.Dungeon.Generation
{
    /// <summary>
    /// The three validated biome pools (126: exactly 21 rooms each) built once from the room definitions and looked up
    /// per depth by the selected biome. Loading a depth never re-validates or re-reads assets; the pool for the chosen
    /// biome is simply handed to the generation pipeline.
    /// </summary>
    public sealed class BiomeRoomPools
    {
        public const int RoomsPerBiome = 21;

        private readonly Dictionary<Biome, RoomPool> _pools = new();

        private BiomeRoomPools()
        {
        }

        public static BiomeRoomPools Build(IEnumerable<RoomDefinition> definitions)
        {
            var list = (definitions ?? throw new ArgumentNullException(nameof(definitions))).Where(d => d != null).ToList();
            var pools = new BiomeRoomPools();
            foreach (Biome biome in Enum.GetValues(typeof(Biome)))
            {
                pools._pools[biome] = RoomPool.Build(list, biome);
            }

            return pools;
        }

        public RoomPool PoolFor(Biome biome) => _pools.TryGetValue(biome, out var pool) ? pool : throw new ArgumentOutOfRangeException(nameof(biome), biome, "No such biome.");

        public IReadOnlyDictionary<Biome, RoomPool> Pools => _pools;

        /// <summary>True when every biome has exactly its 21 validator-passing rooms and nothing was rejected.</summary>
        public bool IsComplete => _pools.Values.All(p => p.Rooms.Count == RoomsPerBiome && p.Rejected.Count == 0);

        public IEnumerable<string> Problems()
        {
            foreach (var (biome, pool) in _pools.Select(kv => (kv.Key, kv.Value)))
            {
                if (pool.Rooms.Count != RoomsPerBiome) yield return $"{biome}: {pool.Rooms.Count} rooms, expected {RoomsPerBiome}.";
                foreach (var rejected in pool.Rejected) yield return $"{biome}: {rejected.RoomId} rejected: {string.Join("; ", rejected.Problems)}";
            }
        }
    }
}
