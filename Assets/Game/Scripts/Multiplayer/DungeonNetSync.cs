using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RuinRail.Networking
{
    /// <summary>
    /// What the host decides for a depth (82: seed, room graph/selection) and what a client needs to rebuild it
    /// bit-for-bit: the seed inputs plus fingerprints to verify the rebuild before any gameplay starts.
    /// </summary>
    [Serializable]
    public struct DungeonSyncPayload : INetworkSerializable, IEquatable<DungeonSyncPayload>
    {
        public int RunSeed;
        public int Depth;
        public int Biome;
        public int Rounds;
        public FixedString128Bytes PoolFingerprint;
        public FixedString128Bytes LayoutFingerprint;
        public bool IsValid;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref RunSeed);
            serializer.SerializeValue(ref Depth);
            serializer.SerializeValue(ref Biome);
            serializer.SerializeValue(ref Rounds);
            serializer.SerializeValue(ref PoolFingerprint);
            serializer.SerializeValue(ref LayoutFingerprint);
            serializer.SerializeValue(ref IsValid);
        }

        public bool Equals(DungeonSyncPayload other) => RunSeed == other.RunSeed && Depth == other.Depth && Biome == other.Biome && Rounds == other.Rounds && PoolFingerprint.Equals(other.PoolFingerprint) && LayoutFingerprint.Equals(other.LayoutFingerprint) && IsValid == other.IsValid;
    }

    /// <summary>Stable fingerprints for the data every peer must share before a depth can be rebuilt identically.</summary>
    public static class DungeonFingerprints
    {
        /// <summary>FNV-1a 64 over UTF-8, rendered as 16 hex chars (deterministic across processes/platforms).</summary>
        public static string Hash(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            var hash = offset;
            foreach (var b in Encoding.UTF8.GetBytes(text ?? string.Empty))
            {
                hash ^= b;
                hash *= prime;
            }

            return hash.ToString("x16");
        }

        /// <summary>Everything assembly depends on per room: id, type, size, doors, elite flag, depth window, weight.</summary>
        public static string RoomPool(RoomPool pool)
        {
            var sb = new StringBuilder();
            sb.Append(pool.Biome).Append('|');
            foreach (var room in pool.Rooms.OrderBy(r => r.Id, StringComparer.Ordinal))
            {
                sb.Append(room.Id).Append(':').Append(room.RoomType).Append(':').Append(room.Dimensions.x).Append('x').Append(room.Dimensions.y).Append(':');
                sb.Append(string.Join(",", room.SupportedDoors.OrderBy(d => d))).Append(':').Append(room.SupportsElite ? 1 : 0).Append(':');
                sb.Append(room.MinDepth).Append('-').Append(room.MaxDepth).Append(':').Append(room.SelectionWeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
                var root = room.Prefab != null ? room.Prefab.GetComponent<RoomRoot>() : null;
                if (root != null) sb.Append(':').Append(string.Join(",", root.GetSockets().Select(s => $"{s.Direction}{s.Cell.x},{s.Cell.y}w{s.Width}")));
                sb.Append(';');
            }

            return Hash(sb.ToString());
        }

        public static string Layout(DungeonLayout layout) => Hash(layout.Signature());
    }

    public enum DungeonSyncError
    {
        None,
        InvalidPayload,
        PoolMismatch,
        GenerationFailed,
        LayoutMismatch,
        BiomeMismatch
    }

    public sealed class DungeonSyncResult
    {
        public DungeonSyncResult(DungeonSyncError error, string diagnostic, DungeonLayout layout, DungeonGraph graph)
        {
            Error = error;
            Diagnostic = diagnostic;
            Layout = layout;
            Graph = graph;
        }

        public bool Success => Error == DungeonSyncError.None;
        public DungeonSyncError Error { get; }
        public string Diagnostic { get; }
        public DungeonLayout Layout { get; }
        public DungeonGraph Graph { get; }
    }

    /// <summary>
    /// Host side: generates the depth through the pipeline and publishes the payload. Client side: verifies the local
    /// pool against the host fingerprint, rebuilds through the identical deterministic pipeline and verifies the
    /// layout fingerprint — any mismatch fails the depth load with an explicit diagnostic instead of desynchronizing.
    /// Neither side ever consults UnityEngine.Random for room, enemy or loot decisions.
    /// </summary>
    public static class DungeonSync
    {
        public static (DungeonSyncPayload payload, DungeonGenerationResult generation) HostGenerate(DungeonGraphGenerator generator, RoomPool pool, int runSeed, int depth)
        {
            var generation = DungeonGenerationPipeline.Generate(generator, pool, runSeed, depth);
            if (!generation.Success) return (new DungeonSyncPayload { IsValid = false }, generation);
            var payload = new DungeonSyncPayload
            {
                RunSeed = runSeed,
                Depth = depth,
                Biome = (int)pool.Biome,
                Rounds = generation.Rounds,
                PoolFingerprint = new FixedString128Bytes(DungeonFingerprints.RoomPool(pool)),
                LayoutFingerprint = new FixedString128Bytes(DungeonFingerprints.Layout(generation.Layout)),
                IsValid = true
            };
            return (payload, generation);
        }

        /// <summary>Host side for a selected biome (129): the depth is generated from that biome's validated pool.</summary>
        public static (DungeonSyncPayload payload, DungeonGenerationResult generation) HostGenerate(DungeonGraphGenerator generator, BiomeRoomPools pools, int runSeed, int depth, Biome biome)
        {
            if (pools == null) throw new ArgumentNullException(nameof(pools));
            return HostGenerate(generator, pools.PoolFor(biome), runSeed, depth);
        }

        /// <summary>Client side (129): the biome comes from the host payload — a client never rolls its own.</summary>
        public static DungeonSyncResult ClientRebuild(in DungeonSyncPayload payload, DungeonGraphGenerator generator, BiomeRoomPools localPools)
        {
            if (!payload.IsValid) return new DungeonSyncResult(DungeonSyncError.InvalidPayload, "Host has not published a valid depth.", null, null);
            if (localPools == null) throw new ArgumentNullException(nameof(localPools));
            if (!Enum.IsDefined(typeof(Biome), payload.Biome)) return new DungeonSyncResult(DungeonSyncError.BiomeMismatch, $"Host biome id {payload.Biome} is not one of the three biomes.", null, null);
            return ClientRebuild(payload, generator, localPools.PoolFor((Biome)payload.Biome));
        }

        public static DungeonSyncResult ClientRebuild(in DungeonSyncPayload payload, DungeonGraphGenerator generator, RoomPool localPool)
        {
            if (!payload.IsValid) return new DungeonSyncResult(DungeonSyncError.InvalidPayload, "Host has not published a valid depth.", null, null);
            if ((int)localPool.Biome != payload.Biome) return new DungeonSyncResult(DungeonSyncError.BiomeMismatch, $"Host biome {(Biome)payload.Biome} but local pool is {localPool.Biome}.", null, null);

            var localFingerprint = DungeonFingerprints.RoomPool(localPool);
            if (localFingerprint != payload.PoolFingerprint.ToString())
            {
                return new DungeonSyncResult(DungeonSyncError.PoolMismatch, $"Room pool mismatch for {localPool.Biome}: host {payload.PoolFingerprint}, local {localFingerprint} ({localPool.Rooms.Count} rooms). Update the build/content before joining.", null, null);
            }

            var generation = DungeonGenerationPipeline.Generate(generator, localPool, payload.RunSeed, payload.Depth);
            if (!generation.Success) return new DungeonSyncResult(DungeonSyncError.GenerationFailed, generation.Error, null, null);

            var layoutFingerprint = DungeonFingerprints.Layout(generation.Layout);
            if (layoutFingerprint != payload.LayoutFingerprint.ToString() || generation.Rounds != payload.Rounds)
            {
                return new DungeonSyncResult(DungeonSyncError.LayoutMismatch, $"Layout mismatch at depth {payload.Depth} (seed {payload.RunSeed}): host {payload.LayoutFingerprint}/{payload.Rounds} rounds, local {layoutFingerprint}/{generation.Rounds} rounds.", null, null);
            }

            return new DungeonSyncResult(DungeonSyncError.None, null, generation.Layout, generation.Graph);
        }
    }

    /// <summary>Guarantees one instantiation per depth even when scene/network callbacks repeat.</summary>
    public sealed class DepthInstantiationGuard
    {
        private readonly HashSet<(int seed, int depth)> _done = new();

        public int Instantiations { get; private set; }
        public int Suppressed { get; private set; }

        public bool TryBegin(int runSeed, int depth)
        {
            if (!_done.Add((runSeed, depth)))
            {
                Suppressed++;
                return false;
            }

            Instantiations++;
            return true;
        }

        public bool HasInstantiated(int runSeed, int depth) => _done.Contains((runSeed, depth));

        public void Reset() => _done.Clear();
    }

    /// <summary>Validated start positions: the Start room's PlayerSpawn markers (world space), one per party slot.</summary>
    public static class PartySpawnPoints
    {
        public static List<Vector2> ForStart(DungeonLayout layout, IReadOnlyDictionary<int, RoomRoot> rooms)
        {
            var points = new List<Vector2>();
            if (layout?.StartPlacement == null || !rooms.TryGetValue(layout.StartPlacement.NodeId, out var start) || start == null) return points;
            foreach (var marker in start.GetMarkers(RoomMarkerRole.PlayerSpawn))
            {
                if (marker.IsInsideRoom(start.Size)) points.Add(start.transform.TransformPoint(marker.WorldCenter));
            }

            if (points.Count == 0) points.Add((Vector2)start.transform.position + (Vector2)start.Size * 0.5f);
            return points;
        }

        /// <summary>Member i takes marker i (cycling); a small deterministic offset keeps bodies from overlapping.</summary>
        public static Vector2 ForMember(IReadOnlyList<Vector2> points, int memberIndex)
        {
            if (points == null || points.Count == 0) return Vector2.zero;
            var basePoint = points[memberIndex % points.Count];
            var lap = memberIndex / points.Count;
            return basePoint + new Vector2(0.6f * lap, 0f);
        }
    }

    /// <summary>Host publishes the depth payload; clients rebuild and verify when it changes.</summary>
    public sealed class NetworkDungeonSync : NetworkBehaviour
    {
        private readonly NetworkVariable<DungeonSyncPayload> _payload = new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public DungeonSyncPayload Payload => _payload.Value;
        public event Action<DungeonSyncPayload> PayloadChanged;

        public override void OnNetworkSpawn()
        {
            _payload.OnValueChanged += (_, next) => PayloadChanged?.Invoke(next);
        }

        public void Publish(in DungeonSyncPayload payload)
        {
            if (!IsServer) throw new AuthorityViolationException(AuthoritativeDomain.RoomGraph, NetworkRole.Client);
            _payload.Value = payload;
        }
    }
}
