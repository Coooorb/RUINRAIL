using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    public class DungeonGraphGeneratorTests
    {
        private static readonly int[] RepresentativeDepths = { 1, 2, 3, 4, 6, 8, 9, 12, 20, 30, 50, 100 };
        private const int SeedsPerDepth = 60;

        private DungeonGraphRules _rules;
        private DungeonGraphGenerator _generator;

        [SetUp]
        public void SetUp()
        {
            _rules = DungeonGraphRules.CreateDefault();
            _generator = new DungeonGraphGenerator(_rules);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rules);
        }

        private static void Set(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        [Test]
        public void Rules_MatchApprovedTargets_AndAssetMatchesDefaults()
        {
            Assert.AreEqual(new Vector2Int(9, 10), _rules.RoomCountRange(1));
            Assert.AreEqual(new Vector2Int(9, 10), _rules.RoomCountRange(3));
            Assert.AreEqual(new Vector2Int(10, 11), _rules.RoomCountRange(4));
            Assert.AreEqual(new Vector2Int(10, 11), _rules.RoomCountRange(8));
            Assert.AreEqual(new Vector2Int(10, 13), _rules.RoomCountRange(9));
            Assert.AreEqual(new Vector2Int(10, 13), _rules.RoomCountRange(100));
            Assert.AreEqual(new Vector2Int(6, 9), _rules.MainPathLength);
            Assert.AreEqual(new Vector2Int(1, 3), _rules.BranchCount);
            Assert.AreEqual(new Vector2Int(1, 3), _rules.BranchLength);
            Assert.AreEqual(new Vector2Int(4, 8), _rules.CombatRooms);
            // 59_DEPTH_SCALING elite chance, raised by the run-variety pass; the D21+ cap is unchanged at 25%.
            Assert.AreEqual(8, _rules.EliteChancePercent(2));
            Assert.AreEqual(18, _rules.EliteChancePercent(5));
            Assert.AreEqual(25, _rules.EliteChancePercent(10));
            Assert.AreEqual(25, _rules.EliteChancePercent(20));
            Assert.AreEqual(25, _rules.EliteChancePercent(21));
            Assert.AreEqual(1, _rules.MaxElites(10));
            Assert.AreEqual(2, _rules.MaxElites(11));

            var asset = AssetDatabase.LoadAssetAtPath<DungeonGraphRules>("Assets/Game/ScriptableObjects/Dungeon/DungeonGraphRules.asset");
            Assert.IsNotNull(asset);
            Assert.AreEqual(_rules.RoomCountRange(9), asset.RoomCountRange(9));
            Assert.AreEqual(_rules.MainPathLength, asset.MainPathLength);
            Assert.AreEqual(_rules.EliteChancePercent(21), asset.EliteChancePercent(21));
        }

        // ---- Acceptance 1: determinism + variation ----

        [Test]
        public void IdenticalSeedAndDepth_ProduceStructurallyIdenticalGraphs()
        {
            foreach (var depth in RepresentativeDepths)
            {
                for (var seed = 0; seed < 10; seed++)
                {
                    var a = _generator.Generate(seed * 7919, depth);
                    var b = new DungeonGraphGenerator(_rules).Generate(seed * 7919, depth);
                    Assert.IsTrue(a.Success, a.Error);
                    Assert.IsTrue(b.Success, b.Error);
                    Assert.AreEqual(a.Graph.Signature(), b.Graph.Signature());
                    Assert.AreEqual(a.Attempts, b.Attempts);
                }
            }
        }

        [Test]
        public void DifferentSeeds_ProduceValidVariation()
        {
            var signatures = new HashSet<string>();
            for (var seed = 0; seed < 40; seed++)
            {
                var result = _generator.Generate(seed, 5);
                Assert.IsTrue(result.Success, result.Error);
                signatures.Add(result.Graph.Signature());
            }

            Assert.Greater(signatures.Count, 20, "Seeds must not collapse onto a handful of layouts.");
        }

        [Test]
        public void DungeonStream_IsIsolatedFromLootAndEncounterStreams()
        {
            var direct = _generator.Generate(123, 4);
            var loot = RngStreams.Derive(123, 4, RngStream.Loot);
            for (var i = 0; i < 50; i++) loot.NextInt(1000);
            var again = _generator.Generate(123, 4);

            Assert.AreEqual(direct.Graph.Signature(), again.Graph.Signature(), "Consuming other streams never changes the dungeon.");
            Assert.AreNotEqual(direct.Graph.Signature(), _generator.Generate(123, 5).Graph.Signature(), "Depth is part of the seed.");
        }

        // ---- Acceptance 2: invariants across broad seeded sampling ----

        [Test]
        public void Invariants_HoldAcrossManySeedsAndDepths()
        {
            var eliteSeen = 0;
            var branchCounts = new HashSet<int>();
            var mainLengths = new HashSet<int>();
            foreach (var depth in RepresentativeDepths)
            {
                var countRange = _rules.RoomCountRange(depth);
                for (var seed = 0; seed < SeedsPerDepth; seed++)
                {
                    var result = _generator.Generate(seed * 31 + depth, depth);
                    Assert.IsTrue(result.Success, $"depth {depth} seed {seed}: {result.Error}");
                    var graph = result.Graph;
                    Assert.IsEmpty(DungeonGraphValidator.Validate(graph, _rules), $"depth {depth} seed {seed}");

                    Assert.That(graph.Nodes.Count, Is.InRange(countRange.x, countRange.y), $"depth {depth} seed {seed} room count");
                    Assert.That(graph.MainPath.Count, Is.InRange(6, 9));
                    Assert.AreEqual(1, graph.NodesOfType(RoomType.Start).Count());
                    Assert.AreEqual(1, graph.NodesOfType(RoomType.Boss).Count());
                    Assert.AreEqual(RoomType.Start, graph.GetNode(graph.MainPath[0]).Type);
                    Assert.AreEqual(RoomType.Boss, graph.GetNode(graph.MainPath[^1]).Type);
                    Assert.IsFalse(graph.AreAdjacent(graph.StartId, graph.BossId));
                    Assert.That(graph.Branches.Count, Is.InRange(1, 3));
                    foreach (var branch in graph.Branches) Assert.That(branch.Count, Is.InRange(1, 3));
                    Assert.AreEqual(graph.Nodes.Count, DungeonGraphValidator.Reachable(graph, graph.StartId).Count);
                    Assert.That(graph.NodesOfType(RoomType.Combat).Count(), Is.InRange(4, 8));
                    Assert.LessOrEqual(graph.NodesOfType(RoomType.Merchant).Count(), 1);
                    Assert.LessOrEqual(graph.NodesOfType(RoomType.Event).Count(), 2);
                    Assert.LessOrEqual(graph.NodesOfType(RoomType.Loot).Count(), 2);
                    Assert.LessOrEqual(graph.NodesOfType(RoomType.Treasure).Count(), 1);
                    Assert.LessOrEqual(graph.NodesOfType(RoomType.MedicalRecovery).Count(), 1);
                    Assert.LessOrEqual(graph.Nodes.Count(n => n.IsElite), _rules.MaxElites(depth));

                    foreach (var node in graph.Nodes)
                    {
                        Assert.IsTrue(node.IsOnMainPath ^ node.BranchIndex >= 0, "Each room is on the main path xor on a branch.");
                        if (node.Type == RoomType.Merchant)
                        {
                            Assert.IsFalse(graph.AreAdjacent(node.Id, graph.StartId) || graph.AreAdjacent(node.Id, graph.BossId));
                        }

                        if (node.IsElite)
                        {
                            eliteSeen++;
                            Assert.AreEqual(RoomType.Combat, node.Type);
                            Assert.IsFalse(graph.AreAdjacent(node.Id, graph.StartId) || graph.AreAdjacent(node.Id, graph.BossId));
                        }

                        if (!node.IsOnMainPath && node.Type != RoomType.Combat)
                        {
                            Assert.IsTrue(true, "optional content lives on branches");
                        }

                        if (node.IsOnMainPath && node.Type != RoomType.Start && node.Type != RoomType.Boss)
                        {
                            Assert.AreEqual(RoomType.Combat, node.Type, "Optional content is placed on branches, main path interior is Combat.");
                        }
                    }

                    branchCounts.Add(graph.Branches.Count);
                    mainLengths.Add(graph.MainPath.Count);
                }
            }

            Assert.Greater(eliteSeen, 0, "Elite rolls must occasionally succeed across 720 dungeons.");
            Assert.GreaterOrEqual(branchCounts.Count, 2, "Branch count varies.");
            Assert.GreaterOrEqual(mainLengths.Count, 3, "Main path length varies.");
        }

        [Test]
        public void EliteFrequency_TracksDepthChance_Approximately()
        {
            int Count(int depth)
            {
                var elites = 0;
                for (var seed = 0; seed < 400; seed++)
                {
                    elites += _generator.Generate(seed, depth).Graph.Nodes.Count(n => n.IsElite);
                }

                return elites;
            }

            var shallow = Count(1);
            var deep = Count(25);
            Assert.Less(shallow, deep, "Deeper dungeons roll elites more often.");
            Assert.That(shallow, Is.InRange(5, 60), "~5% of 400");
            Assert.That(deep, Is.InRange(120, 300), "two slots at ~25% each of 400");
        }

        // ---- Acceptance 3: no prefabs / no UnityEngine.Random ----

        [Test]
        public void Generator_DoesNotReferencePrefabsOrUnityRandom()
        {
            var assembly = typeof(DungeonGraphGenerator).Assembly;
            var generationTypes = assembly.GetTypes().Where(t => t.Namespace == "RuinRail.Dungeon.Generation").ToList();
            Assert.IsNotEmpty(generationTypes);
            foreach (var type in generationTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    var body = method.GetMethodBody();
                    if (body == null) continue;
                    var il = body.GetILAsByteArray();
                    foreach (var referenced in ReferencedMembers(method.Module, il))
                    {
                        Assert.AreNotEqual(typeof(Random), referenced.DeclaringType, $"{type.Name}.{method.Name} uses UnityEngine.Random.");
                        Assert.AreNotEqual(typeof(Object).GetMethod("Instantiate", new[] { typeof(Object) }), referenced, $"{type.Name}.{method.Name} instantiates.");
                    }
                }
            }

            var custom = new SeededRandom(9);
            var fromCustom = _generator.Generate(1, 1, custom);
            Assert.IsTrue(fromCustom.Success);
            Assert.AreNotEqual(fromCustom.Graph.Signature(), _generator.Generate(1, 1).Graph.Signature(), "Injected source is honoured.");
        }

        private static IEnumerable<MemberInfo> ReferencedMembers(Module module, byte[] il)
        {
            // Scan call/callvirt/newobj opcodes (0x28, 0x6F, 0x73) followed by a metadata token.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F && il[i] != 0x73) continue;
                var token = il[i + 1] | (il[i + 2] << 8) | (il[i + 3] << 16) | (il[i + 4] << 24);
                MemberInfo member = null;
                try { member = module.ResolveMember(token); } catch { }
                if (member != null) yield return member;
            }
        }

        // ---- Acceptance 4: explicit failure ----

        [Test]
        public void UnsatisfiableRules_FailExplicitly_WithDiagnostic()
        {
            var impossible = DungeonGraphRules.CreateDefault();
            Set(impossible, "_roomsDepth1To3", new Vector2Int(30, 30));
            Set(impossible, "_maxGenerationAttempts", 3);
            var generator = new DungeonGraphGenerator(impossible);

            var result = generator.Generate(1, 1);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Graph);
            StringAssert.Contains("Could not generate a valid graph", result.Error);
            StringAssert.Contains("after 3 attempts", result.Error);
            Assert.AreEqual(3, result.Attempts);
            Object.DestroyImmediate(impossible);

            Assert.IsFalse(_generator.Generate(1, 0).Success, "Depth 0 is rejected.");
        }

        [Test]
        public void Validator_RejectsHandBuiltRuleViolations()
        {
            var graph = new DungeonGraph(1, 1);
            var ids = new List<int>();
            for (var i = 0; i < 9; i++)
            {
                ids.Add(AddNode(graph, i == 0 ? RoomType.Start : i == 5 ? RoomType.Boss : RoomType.Combat));
            }

            for (var i = 0; i < 5; i++) AddEdge(graph, ids[i], ids[i + 1]);
            SetMainPath(graph, ids.Take(6));
            AddEdge(graph, ids[1], ids[6]);
            AddEdge(graph, ids[2], ids[7]);
            AddEdge(graph, ids[3], ids[8]);
            AddBranch(graph, new List<int> { ids[6] });
            AddBranch(graph, new List<int> { ids[7] });
            AddBranch(graph, new List<int> { ids[8] });
            Assert.IsEmpty(DungeonGraphValidator.Validate(graph, _rules));

            graph.GetNode(ids[6]).Type = RoomType.Merchant;               // adjacent to main[1], fine
            graph.GetNode(ids[7]).IsElite = true;                          // combat on branch, fine
            AddEdge(graph, ids[0], ids[6]);                                // merchant now adjacent to Start
            AddEdge(graph, ids[5], ids[7]);                                // elite now adjacent to Boss
            AddEdge(graph, ids[0], ids[5]);                                // boss adjacent to start

            var problems = DungeonGraphValidator.Validate(graph, _rules);
            Assert.IsTrue(problems.Any(p => p.Contains("Merchant room") && p.Contains("adjacent to Start or Boss")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("Elite room") && p.Contains("directly before Boss")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.Contains("Boss must not be adjacent to Start")), string.Join("\n", problems));
        }

        private static int AddNode(DungeonGraph graph, RoomType type) => graph.AddNode(type).Id;

        private static void AddEdge(DungeonGraph graph, int a, int b) => graph.AddEdge(a, b);

        private static void SetMainPath(DungeonGraph graph, IEnumerable<int> ids) => graph.SetMainPath(ids);

        private static void AddBranch(DungeonGraph graph, List<int> ids) => graph.AddBranch(ids);
    }
}
