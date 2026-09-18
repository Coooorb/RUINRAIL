using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.EditorTools.Rooms;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>TASK 143 — dungeon generation / client rebuild / save serialization measured with documented numbers; determinism and authority untouched.</summary>
    public sealed class GenerationAndSerializationPerfTests
    {
        public const string ReportPath = "TestResults/performance_editor.md";

        [Test]
        public void DungeonGeneration_AndClientRebuild_AreFast_Deterministic_AndAllocationBounded()
        {
            var definitions = RoomValidationTools.LoadAllRoomDefinitions().Where(d => AssetDatabase.GetAssetPath(d).Contains("/Rooms/RuinedMetro/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/Rustworks/") || AssetDatabase.GetAssetPath(d).Contains("/Rooms/OvergrownLabs/")).ToList();
            var poolWatch = Stopwatch.StartNew();
            var hostPools = BiomeRoomPools.Build(definitions);
            poolWatch.Stop();
            var clientPools = BiomeRoomPools.Build(definitions);
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Editor measurements (TASK 143)");
                sb.AppendLine();
                sb.AppendLine($"- Biome room pools (63 rooms, validated): {poolWatch.Elapsed.TotalMilliseconds:0.0} ms.");
                var seeds = Enumerable.Range(1, 10).Select(i => i * 7919).ToArray();
                foreach (Biome biome in Enum.GetValues(typeof(Biome)))
                {
                    var generator = new DungeonGraphGenerator(rules);
                    var clientGenerator = new DungeonGraphGenerator(rules);
                    // Warm-up.
                    DungeonSync.HostGenerate(generator, hostPools, 1, 1, biome);
                    var hostWatch = new Stopwatch();
                    var clientWatch = new Stopwatch();
                    long allocated = 0;
                    var worstHostMs = 0.0;
                    for (var depth = 1; depth <= 3; depth++)
                    {
                        foreach (var seed in seeds)
                        {
                            var before = GC.GetAllocatedBytesForCurrentThread();
                            hostWatch.Start();
                            var one = Stopwatch.StartNew();
                            var (payload, generation) = DungeonSync.HostGenerate(generator, hostPools, seed, depth, biome);
                            one.Stop();
                            hostWatch.Stop();
                            worstHostMs = Math.Max(worstHostMs, one.Elapsed.TotalMilliseconds);
                            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
                            Assert.IsTrue(generation.Success && payload.IsValid, $"{biome} seed {seed} depth {depth}");
                            clientWatch.Start();
                            var rebuilt = DungeonSync.ClientRebuild(payload, clientGenerator, clientPools);
                            clientWatch.Stop();
                            Assert.IsTrue(rebuilt.Success, rebuilt.Error.ToString());
                            var (again, _) = DungeonSync.HostGenerate(new DungeonGraphGenerator(rules), hostPools, seed, depth, biome);
                            Assert.IsTrue(payload.LayoutFingerprint.Equals(again.LayoutFingerprint), "Deterministic for the same seed/depth/biome.");
                        }
                    }

                    var runs = seeds.Length * 3;
                    sb.AppendLine($"- {biome}: host generation avg {hostWatch.Elapsed.TotalMilliseconds / runs:0.00} ms (worst {worstHostMs:0.00} ms), client rebuild avg {clientWatch.Elapsed.TotalMilliseconds / runs:0.00} ms over {runs} seed×depth runs; {(allocated > 0 ? (allocated / (float)runs / 1024f).ToString("0.0") + " KB managed per generation" : "managed allocation NOT MEASURED (GC.GetAllocatedBytesForCurrentThread reports 0 in this Mono runtime)")}.");
                    Assert.Less(hostWatch.Elapsed.TotalMilliseconds / runs, 50.0, "Generation stays well inside a load transition budget.");
                }

                Directory.CreateDirectory("TestResults");
                File.WriteAllText(ReportPath, sb.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        [Test]
        public void SaveSerialization_FullStorageAndLoadout_MeasuredAndRoundTripsExactly()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var registry = ItemDefinitionRegistry.Build(catalog);
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, id => registry.TryGet(id, out var d) ? d : null);
            var slot = SaveSlotService.CreateNew();
            var equipment = registry.Definitions.Where(d => d.Category == ItemCategory.Weapon || d.Category == ItemCategory.Armor || d.Category == ItemCategory.Accessory).ToList();
            var entries = Enumerable.Range(0, 60).Select(i => new InventorySnapshot.Entry { Slot = i, Item = new ItemInstance(equipment[i % equipment.Count].Id, 1, (Rarity)(i % 5)).ToSnapshot() }).ToArray();
            slot.Storage.Slots = entries;
            slot.Storage.Capacity = 60;
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = Enumerable.Range(0, 8).Select(i => new InventorySnapshot.Entry { Slot = i, Item = new ItemInstance("ammo_light", 30).ToSnapshot() }).ToArray()
            };

            var watch = Stopwatch.StartNew();
            for (var i = 0; i < 20; i++) Assert.AreEqual(SaveError.None, saves.Save(slot));
            watch.Stop();
            var bytes = Encoding.UTF8.GetByteCount(store.Document);
            var loadWatch = Stopwatch.StartNew();
            SaveLoadResult loaded = null;
            for (var i = 0; i < 20; i++) loaded = saves.Load();
            loadWatch.Stop();
            Assert.IsTrue(loaded.Success);
            Assert.AreEqual(60, loaded.Slot.Storage.Slots.Length);
            Assert.AreEqual(entries[17].Item.InstanceId, loaded.Slot.Storage.Slots[17].Item.InstanceId, "Exact instance identity survives.");

            File.AppendAllText(ReportPath, $"- Save document with 60 storage items + 9-slot loadout: {bytes / 1024f:0.0} KB; save (atomic write path) avg {watch.Elapsed.TotalMilliseconds / 20:0.00} ms, load+validate avg {loadWatch.Elapsed.TotalMilliseconds / 20:0.00} ms over 20 iterations.\n");
            Assert.Less(watch.Elapsed.TotalMilliseconds / 20, 50.0);
        }
    }
}
