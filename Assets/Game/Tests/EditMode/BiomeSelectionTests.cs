using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Core.Rng;
using RuinRail.Dungeon.Generation;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 129 — exact 20/40/40 per-depth biome selection on the dedicated Biome stream, uniform first depth, three biomes only, pool loading on Descend, host-selected biome for clients.</summary>
    public sealed class BiomeSelectionTests
    {
        /// <summary>Scripted RNG: hands out the given draws in order (controlled boundary tests).</summary>
        private sealed class ScriptedRandom : IRandomSource
        {
            private readonly Queue<int> _draws;
            public ScriptedRandom(params int[] draws) { _draws = new Queue<int>(draws); }
            public int NextInt(int minInclusive, int maxInclusive) => minInclusive + _draws.Dequeue();
            public int NextInt(int maxExclusive) => _draws.Dequeue();
            public float NextFloat() => 0f;
        }

        private static readonly List<UnityEngine.Object> Created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in Created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            Created.Clear();
        }

        // ---- Acceptance 1: exact mapping at every boundary; no fourth biome ----

        [Test]
        public void ExactlyThreeBiomes_Exist()
        {
            CollectionAssert.AreEqual(new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs }, BiomeSelector.All);
            Assert.AreEqual(3, Enum.GetValues(typeof(Biome)).Length, "56: exactly Ruined Metro, Rustworks, Overgrown Labs.");
            Assert.AreEqual(40, BiomeSelector.OtherBiomeWeight);
            Assert.AreEqual(20, BiomeSelector.RepeatBiomeWeight);
        }

        [Test]
        public void NextDepth_PreviousBiomeHasTwentyOfHundred_EachOtherForty_AtEveryBoundary()
        {
            // Weights are laid out in enum order: Metro [0,40) or [0,20) when previous, etc. Total is always 100.
            foreach (var previous in BiomeSelector.All)
            {
                var ranges = new List<(Biome biome, int start, int end)>();
                var cursor = 0;
                foreach (var candidate in BiomeSelector.All)
                {
                    var weight = BiomeSelector.WeightFor(candidate, previous);
                    ranges.Add((candidate, cursor, cursor + weight));
                    cursor += weight;
                }

                Assert.AreEqual(100, cursor, $"previous {previous}: weights sum to 100.");
                foreach (var (biome, start, end) in ranges)
                {
                    Assert.AreEqual(biome == previous ? 20 : 40, end - start, $"previous {previous}: {biome} weight");
                    Assert.AreEqual(biome, BiomeSelector.SelectNext(previous, new ScriptedRandom(start)), $"previous {previous}: draw {start} -> {biome}");
                    Assert.AreEqual(biome, BiomeSelector.SelectNext(previous, new ScriptedRandom(end - 1)), $"previous {previous}: draw {end - 1} -> {biome}");
                }
            }
        }

        [Test]
        public void FirstDepth_IsUniform_WithoutAnyRepeatWeighting()
        {
            Assert.AreEqual(Biome.RuinedMetro, BiomeSelector.SelectFirst(new ScriptedRandom(0)));
            Assert.AreEqual(Biome.Rustworks, BiomeSelector.SelectFirst(new ScriptedRandom(1)));
            Assert.AreEqual(Biome.OvergrownLabs, BiomeSelector.SelectFirst(new ScriptedRandom(2)));
            var counts = new Dictionary<Biome, int>();
            for (var seed = 1; seed <= 3000; seed++)
            {
                var b = BiomeSelector.SelectFirst(seed);
                counts[b] = counts.TryGetValue(b, out var c) ? c + 1 : 1;
            }

            foreach (var biome in BiomeSelector.All) Assert.That(counts[biome], Is.InRange(850, 1150), $"{biome}: ~1/3 of first depths");
        }

        // ---- Acceptance 2: dedicated stream, same seed/history → same sequence ----

        [Test]
        public void Selection_UsesTheDedicatedBiomeStream_AndIsReproducibleFromSeedAndHistory()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(RngStream), "Biome"), "114: a dedicated Biome stream exists.");
            Assert.AreNotEqual((int)RngStream.Biome, (int)RngStream.Dungeon);
            for (var seed = 1; seed <= 50; seed++)
            {
                CollectionAssert.AreEqual(BiomeSelector.Sequence(seed, 12), BiomeSelector.Sequence(seed, 12), $"seed {seed}");
                var sequence = BiomeSelector.Sequence(seed, 12);
                for (var depth = 2; depth <= 12; depth++)
                {
                    Assert.AreEqual(sequence[depth - 1], BiomeSelector.SelectNext(sequence[depth - 2], seed, depth), $"seed {seed} depth {depth}: the sequence element is the per-depth draw given the same history");
                }
            }

            // Different histories at the same depth can differ; the draw depends only on (seed, depth) and previous.
            var draw = BiomeSelector.StreamFor(7, 3).NextInt(100);
            Assert.AreEqual(draw, BiomeSelector.StreamFor(7, 3).NextInt(100));
            Assert.AreEqual(draw, RngStreams.Derive(7, 3, RngStream.Biome).NextInt(100), "Same derivation as every other stream, on its own stream id.");
            Assert.IsTrue(Enumerable.Range(1, 200).Select(s => BiomeSelector.Sequence(s, 6)).Select(seq => string.Join(",", seq)).Distinct().Count() > 100, "Seeds spread over many sequences.");
        }

        [Test]
        public void RepeatFrequency_MatchesTwentyPercent_OverTheSeededStream()
        {
            var repeats = 0;
            var total = 0;
            for (var seed = 1; seed <= 400; seed++)
            {
                var sequence = BiomeSelector.Sequence(seed, 11);
                for (var i = 1; i < sequence.Length; i++)
                {
                    total++;
                    if (sequence[i] == sequence[i - 1]) repeats++;
                }
            }

            Assert.That(repeats / (float)total, Is.InRange(0.16f, 0.24f), "~20% direct repeats (56).");
        }

        // ---- Acceptance 3: Descend loads the selected biome's 21-room pool and preserves risk state ----

        [Test]
        public void Descend_SelectsTheNextBiome_LoadsItsValidatedPool_AndPreservesInventoryCoinsAndParty()
        {
            var pools = BiomeRoomPools.Build(RoomValidationTools.LoadAllRoomDefinitions().Where(d => AssetDatabase.GetAssetPath(d).Contains("/Rooms/RuinedMetro/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/Rustworks/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/OvergrownLabs/")));
            Assert.IsTrue(pools.IsComplete, string.Join("\n", pools.Problems()));
            foreach (var biome in BiomeSelector.All) Assert.AreEqual(21, pools.PoolFor(biome).Rooms.Count, biome.ToString());

            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var registry = ItemDefinitionRegistry.Build(catalog);
            var ammoByType = registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var expedition = new ExpeditionService(id => registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"));
            var profile = new PlayerProfile
            {
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 30).ToSnapshot() } }
                }
            };
            var rules = DungeonGraphRules.CreateDefault();
            Created.Add(rules);
            var generator = new DungeonGraphGenerator(rules);

            var seed = 2026;
            var expectedSequence = BiomeSelector.Sequence(seed, 5);
            var state = expedition.Start(profile, seed, expectedSequence[0], 3);
            expedition.AddCarriedCoins(150);
            var rifleId = state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId;
            var ammoId = state.Inventory.BackpackSlots.First(i => i != null).InstanceId;
            var inventory = state.Inventory;
            var wallet = state.CarriedWallet;
            var biomesPlayed = new List<Biome>();

            for (var depth = 1; depth <= 5; depth++)
            {
                Assert.AreEqual(depth, state.Depth);
                Assert.AreEqual(expectedSequence[depth - 1], state.Biome, $"depth {depth}: biome from the seeded sequence");
                var pool = pools.PoolFor(state.Biome);
                Assert.AreEqual(state.Biome, pool.Biome);
                var generation = DungeonGenerationPipeline.Generate(generator, pool, state.RunSeed, state.Depth);
                Assert.IsTrue(generation.Success, generation.Error);
                Assert.IsTrue(generation.Layout.Placements.All(p => p.Definition.Biome == state.Biome), $"depth {depth}: every placed room belongs to the selected biome");
                biomesPlayed.Add(state.Biome);
                if (depth == 5) break;

                expedition.RecordBossDefeated(0);
                expedition.Descend();
                Assert.AreSame(state, expedition.State, "Same at-risk transaction across the boundary.");
                Assert.AreSame(inventory, state.Inventory);
                Assert.AreSame(wallet, state.CarriedWallet);
                Assert.AreEqual(150, state.CarriedCoins, "Carried Coins preserved.");
                Assert.AreEqual(rifleId, state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId, "Equipped instance preserved.");
                Assert.AreEqual(ammoId, state.Inventory.BackpackSlots.First(i => i != null).InstanceId, "Backpack instance preserved.");
                Assert.AreEqual(3, state.StartingPartySize, "Party scaling preserved.");
                Assert.IsTrue(state.IsActive);
            }

            CollectionAssert.AreEqual(expectedSequence, biomesPlayed);
            CollectionAssert.AreEqual(expectedSequence, state.BiomeHistory);
        }

        // ---- Acceptance 4: clients match the host-selected biome; never reroll ----

        [Test]
        public void Clients_RebuildTheHostSelectedBiome_FromThePayload_NeverTheirOwnRoll()
        {
            var definitions = RoomValidationTools.LoadAllRoomDefinitions().Where(d => AssetDatabase.GetAssetPath(d).Contains("/Rooms/RuinedMetro/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/Rustworks/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/OvergrownLabs/")).ToList();
            var hostPools = BiomeRoomPools.Build(definitions);
            var clientPools = BiomeRoomPools.Build(definitions);
            var rules = DungeonGraphRules.CreateDefault();
            Created.Add(rules);

            foreach (var seed in new[] { 3, 11, 2026 })
            {
                var sequence = BiomeSelector.Sequence(seed, 4);
                for (var depth = 1; depth <= 4; depth++)
                {
                    var hostBiome = sequence[depth - 1];
                    var (payload, hostGeneration) = DungeonSync.HostGenerate(new DungeonGraphGenerator(rules), hostPools, seed, depth, hostBiome);
                    Assert.IsTrue(payload.IsValid && hostGeneration.Success);
                    Assert.AreEqual((int)hostBiome, payload.Biome, "The host publishes the biome it selected.");
                    var rebuilt = DungeonSync.ClientRebuild(payload, new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), clientPools);
                    Assert.IsTrue(rebuilt.Success, rebuilt.Diagnostic);
                    Assert.IsTrue(rebuilt.Layout.Placements.All(p => p.Definition.Biome == hostBiome), "The client loaded the host's biome pool.");
                    Assert.AreEqual(hostGeneration.Layout.Signature(), rebuilt.Layout.Signature());
                }
            }

            // A forged/foreign biome id or a client using a different biome's pool is refused, never silently rerolled.
            var (ok, _) = DungeonSync.HostGenerate(new DungeonGraphGenerator(rules), hostPools, 5, 2, Biome.Rustworks);
            var wrongPool = DungeonSync.ClientRebuild(ok, new DungeonGraphGenerator(rules), clientPools.PoolFor(Biome.OvergrownLabs));
            Assert.AreEqual(DungeonSyncError.BiomeMismatch, wrongPool.Error);
            var forged = ok;
            forged.Biome = 7;
            Assert.AreEqual(DungeonSyncError.BiomeMismatch, DungeonSync.ClientRebuild(forged, new DungeonGraphGenerator(rules), clientPools).Error, "No fourth biome can be loaded.");
        }

        [Test]
        public void LobbyStart_WithoutAnExplicitBiome_UsesTheSeededFirstDepthDraw()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var registry = ItemDefinitionRegistry.Build(catalog);
            var lobby = new PartyLobby(0, id => registry.TryGet(id, out var d) ? d : null);
            lobby.Join(0, "host");
            lobby.SetLoadout(0, new InventorySnapshot { Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } }, Backpack = Array.Empty<InventorySnapshot.Entry>() });
            lobby.SetReady(0, true);
            Assert.AreEqual(LobbyStartError.None, lobby.TryStart(0, 4242, out var snapshot));
            Assert.AreEqual((int)BiomeSelector.SelectFirst(4242), snapshot.Biome, "Depth 1 biome is the host's seeded draw, replicated in the start snapshot.");
        }
    }
}
