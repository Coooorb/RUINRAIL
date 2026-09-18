using System.IO;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 097: host publishes seed/fingerprints, clients rebuild identically, mismatches fail explicitly, one spawn per depth.</summary>
    public class DungeonNetSyncTests
    {
        private DungeonGraphRules _rules;
        private RoomPool _pool;

        [SetUp]
        public void SetUp()
        {
            _rules = DungeonGraphRules.CreateDefault();
            var metro = AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/Game/ScriptableObjects/Rooms/RuinedMetro" }).Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _pool = RoomPool.Build(metro, Biome.RuinedMetro);
            Assert.AreEqual(21, _pool.Rooms.Count);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_rules);

        [Test]
        public void HostAndThreeClients_RebuildIdenticalLayouts_FromThePayload()
        {
            foreach (var (seed, depth) in new[] { (11, 1), (2026, 3), (99, 25) })
            {
                var (payload, hostGeneration) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pool, seed, depth);
                Assert.IsTrue(payload.IsValid);
                Assert.IsTrue(hostGeneration.Success);
                for (var client = 0; client < 3; client++)
                {
                    var clientPool = RoomPool.Build(_pool.Rooms, Biome.RuinedMetro);
                    var result = DungeonSync.ClientRebuild(payload, new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), clientPool);
                    Assert.IsTrue(result.Success, result.Diagnostic);
                    Assert.AreEqual(hostGeneration.Layout.Signature(), result.Layout.Signature(), $"seed {seed} depth {depth} client {client}");
                    CollectionAssert.AreEqual(hostGeneration.Layout.Placements.OrderBy(p => p.NodeId).Select(p => (p.NodeId, p.Definition.Id, p.Offset)),
                        result.Layout.Placements.OrderBy(p => p.NodeId).Select(p => (p.NodeId, p.Definition.Id, p.Offset)));
                }
            }
        }

        [Test]
        public void PoolMismatch_IsDetectedBeforeGameplay_WithAnExplicitDiagnostic()
        {
            var (payload, _) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pool, 5, 2);

            var missingRoom = RoomPool.Build(_pool.Rooms.Where(r => r.Id != "metro_combat_small_03"), Biome.RuinedMetro);
            var result = DungeonSync.ClientRebuild(payload, new DungeonGraphGenerator(_rules), missingRoom);
            Assert.AreEqual(DungeonSyncError.PoolMismatch, result.Error);
            Assert.IsNull(result.Layout);
            StringAssert.Contains("Room pool mismatch", result.Diagnostic);
            StringAssert.Contains("20 rooms", result.Diagnostic);

            var wrongBiome = RoomPool.Build(_pool.Rooms, Biome.Rustworks);
            Assert.AreEqual(DungeonSyncError.BiomeMismatch, DungeonSync.ClientRebuild(payload, new DungeonGraphGenerator(_rules), wrongBiome).Error);

            Assert.AreEqual(DungeonSyncError.InvalidPayload, DungeonSync.ClientRebuild(new DungeonSyncPayload { IsValid = false }, new DungeonGraphGenerator(_rules), _pool).Error);
        }

        [Test]
        public void LayoutMismatch_FromTamperedFingerprint_IsDetected()
        {
            var (payload, _) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pool, 7, 4);
            var tampered = payload;
            tampered.LayoutFingerprint = new Unity.Collections.FixedString128Bytes("deadbeefdeadbeef");
            var result = DungeonSync.ClientRebuild(tampered, new DungeonGraphGenerator(_rules), _pool);
            Assert.AreEqual(DungeonSyncError.LayoutMismatch, result.Error);
            StringAssert.Contains("depth 4", result.Diagnostic);
        }

        [Test]
        public void Fingerprints_AreStable_AndSensitiveToRoomData()
        {
            var a = DungeonFingerprints.RoomPool(_pool);
            var b = DungeonFingerprints.RoomPool(RoomPool.Build(_pool.Rooms.Reverse(), Biome.RuinedMetro));
            Assert.AreEqual(a, b, "Order-independent.");
            Assert.AreEqual(16, a.Length);
            Assert.AreNotEqual(a, DungeonFingerprints.RoomPool(RoomPool.Build(_pool.Rooms.Take(20), Biome.RuinedMetro)));
            Assert.AreEqual(DungeonFingerprints.Hash("x"), DungeonFingerprints.Hash("x"));
            Assert.AreNotEqual(DungeonFingerprints.Hash("x"), DungeonFingerprints.Hash("y"));
        }

        [Test]
        public void SoloGeneration_IsUnchanged_RoundOneEqualsSingleShot()
        {
            var (payload, generation) = DungeonSync.HostGenerate(new DungeonGraphGenerator(_rules), _pool, 13, 2);
            var single = DungeonAssembler.ForRun(_pool, 13, 2).Assemble(new DungeonGraphGenerator(_rules).Generate(13, 2).Graph);
            if (generation.Rounds == 1) Assert.AreEqual(single.Layout.Signature(), generation.Layout.Signature());
            Assert.AreEqual(generation.Rounds, payload.Rounds);
        }

        [Test]
        public void DepthInstantiation_HappensOnce_PerSeedAndDepth()
        {
            var guard = new DepthInstantiationGuard();
            Assert.IsTrue(guard.TryBegin(1, 1));
            Assert.IsFalse(guard.TryBegin(1, 1), "Repeated scene/network callback suppressed.");
            Assert.IsFalse(guard.TryBegin(1, 1));
            Assert.IsTrue(guard.TryBegin(1, 2));
            Assert.IsTrue(guard.TryBegin(2, 1));
            Assert.AreEqual(3, guard.Instantiations);
            Assert.AreEqual(2, guard.Suppressed);
            Assert.IsTrue(guard.HasInstantiated(1, 1));
        }

        [Test]
        public void GenerationLootAndEncounterCode_NeverUsesUnityEngineRandom()
        {
            var offenders = Directory.GetFiles("Assets/Game/Scripts/Dungeon", "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/Game/Scripts/Loot", "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/Game/Scripts/Enemies/Encounters", "*.cs", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/Game/Scripts/Events", "*.cs", SearchOption.AllDirectories))
                .Where(f => File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//")).Any(l => l.Contains("UnityEngine.Random") || System.Text.RegularExpressions.Regex.IsMatch(l, @"\bRandom\.(Range|value|insideUnitCircle|rotation)")))
                .ToList();
            CollectionAssert.IsEmpty(offenders, "Rooms, enemies, loot and events decide only through seeded streams.");
        }

        [Test]
        public void PartySpawnPoints_UseValidatedStartMarkers_OnePerMember()
        {
            var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(_rules), _pool, 3, 1);
            var parent = new GameObject("Dungeon");
            try
            {
                var rooms = DungeonLayoutInstantiator.Instantiate(generation.Layout, parent.transform);
                var points = PartySpawnPoints.ForStart(generation.Layout, rooms);
                Assert.GreaterOrEqual(points.Count, 1);
                var start = rooms[generation.Layout.StartPlacement.NodeId];
                var bounds = new Rect(start.transform.position, (Vector2)start.Size);
                foreach (var p in points) Assert.IsTrue(bounds.Contains(p), $"spawn {p} inside the Start room {bounds}");
                var a = PartySpawnPoints.ForMember(points, 0);
                var b = PartySpawnPoints.ForMember(points, 1);
                var c = PartySpawnPoints.ForMember(points, 2);
                Assert.AreNotEqual(a, b);
                Assert.AreNotEqual(b, c);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
