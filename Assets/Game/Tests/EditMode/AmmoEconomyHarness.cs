using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>
    /// Deterministic ammo-economy model of one generated depth, built only from shipped data: the real graph/layout
    /// generator and room pool, the real encounter director and depth scaling (enemy HP), the real Elite/Boss rosters,
    /// the real loot tables rolled through the real LootRoller with the real per-source contexts, the real starter
    /// reserve and the real trader bundle. The player is the starter-firearm path: a P9 Ranger with a representative
    /// but imperfect accuracy — every shot that misses is a round spent for nothing. The ammo-free secondary is never
    /// counted as firearm sustainability: a required shot that cannot be fired is recorded as time at zero reserve.
    /// </summary>
    public sealed class AmmoEconomyHarness
    {
        /// <summary>Representative accuracy for a normal player against moving targets (misses included; the soft aim assist is on in the shipped player).</summary>
        public const float DefaultAccuracy = 0.65f;

        public float Accuracy { get; set; } = DefaultAccuracy;
        /// <summary>Planner parameters under test (defaults = the shipped rule).</summary>
        public int OrdinaryRoomPercent { get; set; } = SupplyChestPlanner.OrdinaryRoomPercent;
        public int MinimumChests { get; set; } = SupplyChestPlanner.MinimumPerDepth;

        public sealed class RunResult
        {
            public Biome Biome;
            public int Depth;
            public int Seed;
            public int RoomCount;
            public int CombatRooms;
            public int EliteRooms;
            public int SupplyChests;
            public int LootRoomChests;
            public int TreasureChests;
            public int SpecialEvents;
            public bool HasMerchant;
            public bool HasBrokenMachine;
            public int RelevantAmmoFound;
            public int OtherAmmoFound;
            public int CoinsFound;
            public int MerchantRoundsBought;
            public int RoundsStart;
            public int RoundsNeeded;
            public int RoundsFired;
            public int RoundsUnfired;
            public int ReserveAtEnd;
            public int EngagementRooms;
            public int RoomsEndedDry;
            /// <summary>Engagements fought mostly without the firearm: entered with nothing to fire, or more than half of the room's required rounds could not be fired.</summary>
            public int DryRooms;
            /// <summary>Share of every required round of the depth that could not be fired (shot-weighted; the boss dominates it).</summary>
            public float DryShare => RoundsNeeded == 0 ? 0f : RoundsUnfired / (float)RoundsNeeded;
            /// <summary>The majority of the depth's engagements were fought dry (room-weighted: what "spending the depth at zero reserve" means to a player).</summary>
            public bool MajorityAtZero => EngagementRooms > 0 && DryRooms * 2 > EngagementRooms;
            public bool MajorityOfShotsUnfired => DryShare >= 0.5f;
            public int ChestContainerCount => SupplyChests + LootRoomChests + TreasureChests + 1; // + the Boss Cache
            public int SpecialLootSourceCount => LootRoomChests + TreasureChests + 1 + SpecialEvents;
            public int AmmoOpportunityCount => SupplyChests + (HasMerchant ? 1 : 0) + (HasBrokenMachine ? 1 : 0);
        }

        private readonly GameContentCatalog _content;
        private readonly BiomeRoomPools _pools;
        private readonly RangedWeaponDefinition _pistol;
        private readonly LootTableDefinition _supplyTable;
        private readonly LootRoller _roller;
        private readonly int _lightCap;

        public AmmoEconomyHarness(GameContentCatalog content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _pools = BiomeRoomPools.Build(content.Rooms);
            _pistol = content.Items.OfType<RangedWeaponDefinition>().First(w => w.Id == RuinRail.Gameplay.Base.StarterKitService.PistolId);
            if (!content.Loot.TryGet(LootSourceKind.SupplyChest, out var source) || source.Table == null) throw new InvalidOperationException("Supply Chest table missing.");
            _supplyTable = source.Table;
            _roller = content.Loot.CreateRoller();
            _lightCap = content.AmmoBalance.GetStackLimit(AmmoType.Light);
        }

        public float AverageDamage => (_pistol.DamageMin + _pistol.DamageMax) * 0.5f;

        /// <summary>Rounds a representative player spends to remove <paramref name="health"/> at the pistol's damage and the modelled accuracy.</summary>
        public int RoundsFor(int health) => Mathf.CeilToInt(Mathf.CeilToInt(health / AverageDamage) / Accuracy) * _pistol.AmmoCostPerShot;

        /// <summary>One depth. <paramref name="ordinaryChests"/> false models the pre-fix economy (no Supply Chest in any ordinary room, no usefulness weighting).</summary>
        public RunResult Simulate(Biome biome, int seed, int depth, bool ordinaryChests = true)
        {
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(rules), _pools.PoolFor(biome), seed, depth);
                if (!generation.Success) throw new InvalidOperationException($"generation failed for {biome} seed {seed} depth {depth}: {generation.Error}");
                return Simulate(generation, biome, seed, depth, ordinaryChests);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        private RunResult Simulate(DungeonGenerationResult generation, Biome biome, int seed, int depth, bool ordinaryChests)
        {
            var graph = generation.Graph;
            var layout = generation.Layout;
            var result = new RunResult { Biome = biome, Depth = depth, Seed = seed, RoomCount = graph.Nodes.Count, RoundsStart = RuinRail.Gameplay.Base.StarterKitService.LightAmmoCount };
            var supplyRooms = ordinaryChests ? SupplyChestPlanner.Plan(graph, seed, depth, OrdinaryRoomPercent, MinimumChests) : new HashSet<int>();
            var useful = ordinaryChests ? new[] { _pistol.AmmoType } : Array.Empty<AmmoType>();
            var context = new DungeonRuntimeContext(seed, depth, 1, _content.Enemies, null, _content.DepthScaling, _content.Elites, null);
            var bundleUnits = _content.Economy.TryGetAmmoBundle(AmmoType.Light, out var bundle) ? bundle.Units : 0;
            var bundlePrice = _content.Economy.TryGetAmmoBundle(AmmoType.Light, out bundle) ? bundle.Price : int.MaxValue;

            var magazine = _pistol.MagazineSize;
            var reserve = result.RoundsStart;
            var coins = 0;

            foreach (var node in TraversalOrder(graph))
            {
                var placement = layout.GetPlacement(node.Id);
                var definition = placement?.Definition;
                switch (node.Type)
                {
                    case RoomType.Combat:
                    {
                        result.CombatRooms++;
                        var needed = 0;
                        if (node.IsElite && definition != null && definition.SupportsElite && context.PickElite(biome, node.Id) is { } elite)
                        {
                            result.EliteRooms++;
                            needed += RoundsFor(DepthScaling.ScaledHealth(elite.BaseHealth, depth, 1, false, _content.DepthScaling));
                        }
                        else
                        {
                            var markers = definition != null ? MarkerCount(definition) : 0;
                            var plan = EncounterDirector.Compose(new EncounterContext(seed, depth, 1, biome, node.Id, definition != null ? definition.Tags : null, markers), _content.Enemies);
                            foreach (var enemy in plan.Expand()) needed += RoundsFor(DepthScaling.ScaledHealth(enemy.BaseHealth, depth, 1, false, _content.DepthScaling));
                        }

                        Fight(result, needed, ref magazine, ref reserve);
                        if (supplyRooms.Contains(node.Id))
                        {
                            result.SupplyChests++;
                            var roll = _roller.Roll(_supplyTable, LootContext.ForSource(seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + RoomCategoryComposer.SupplyChestSourceSlot, LootQuality.Standard, 1, useful));
                            Loot(result, roll, ref reserve, ref coins);
                        }

                        break;
                    }
                    case RoomType.Loot:
                        result.LootRoomChests += definition != null ? MarkerCount(definition, RoomMarkerRole.ChestSpawn) : 0;
                        break;
                    case RoomType.Treasure:
                        result.TreasureChests += definition != null ? MarkerCount(definition, RoomMarkerRole.ChestSpawn) : 0;
                        break;
                    case RoomType.Merchant:
                        result.HasMerchant = true;
                        // The merchant's ammo slot (58): buy the Light bundle when the coins found so far cover it and the reserve is below one bundle.
                        if (reserve < bundleUnits && coins >= bundlePrice && bundleUnits > 0)
                        {
                            coins -= bundlePrice;
                            var added = Mathf.Min(_lightCap - reserve, bundleUnits);
                            reserve += added;
                            result.MerchantRoundsBought += added;
                        }

                        break;
                    case RoomType.Event:
                    {
                        var kind = EventKindFor(definition, seed, depth, node.Id);
                        if (kind == RuinRail.Gameplay.Events.DungeonEventKind.BrokenMachine) result.HasBrokenMachine = true;
                        if (kind == RuinRail.Gameplay.Events.DungeonEventKind.WeaponCache || kind == RuinRail.Gameplay.Events.DungeonEventKind.CursedChest || kind == RuinRail.Gameplay.Events.DungeonEventKind.LockedVault) result.SpecialEvents++;
                        break;
                    }
                    case RoomType.Boss:
                    {
                        var boss = _content.Bosses.Where(b => b.Biome == biome).OrderBy(b => b.Id, StringComparer.Ordinal).ToList();
                        var pick = boss.Count > 0 ? boss[Mathf.Abs(seed + depth) % boss.Count] : null;
                        var needed = pick != null ? RoundsFor(DepthScaling.ScaledHealth(pick.BaseHealth, depth, 1, true, _content.DepthScaling)) : 0;
                        Fight(result, needed, ref magazine, ref reserve);
                        break;
                    }
                }
            }

            result.ReserveAtEnd = reserve + magazine;
            return result;
        }

        private void Fight(RunResult result, int needed, ref int magazine, ref int reserve)
        {
            result.EngagementRooms++;
            result.RoundsNeeded += needed;
            var available = magazine + reserve;
            var fired = Mathf.Min(needed, available);
            result.RoundsFired += fired;
            result.RoundsUnfired += needed - fired;
            if (needed > 0 && (available == 0 || (needed - fired) * 2 > needed)) result.DryRooms++;
            available -= fired;
            magazine = Mathf.Min(_pistol.MagazineSize, available);
            reserve = available - magazine;
            if (reserve == 0) result.RoomsEndedDry++;
        }

        private void Loot(RunResult result, LootResult roll, ref int reserve, ref int coins)
        {
            coins += roll.Coins;
            result.CoinsFound += roll.Coins;
            foreach (var item in roll.Items)
            {
                var definition = _content.Items.FirstOrDefault(i => i != null && i.Id == item.DefinitionId);
                if (definition is not AmmoItemDefinition ammo) continue;
                if (ammo.AmmoType == _pistol.AmmoType)
                {
                    result.RelevantAmmoFound += item.Quantity;
                    reserve = Mathf.Min(_lightCap, reserve + item.Quantity);
                }
                else
                {
                    result.OtherAmmoFound += item.Quantity;
                }
            }
        }

        /// <summary>The order a player meets the rooms: the main path from Start, each branch fully when its attachment room is reached, the Boss last.</summary>
        public static List<RoomNode> TraversalOrder(DungeonGraph graph)
        {
            var order = new List<RoomNode>();
            var visited = new HashSet<int>();
            foreach (var mainId in graph.MainPath)
            {
                if (mainId == graph.BossId) continue;
                Visit(graph, mainId, visited, order, mainOnly: false);
            }

            order.Add(graph.GetNode(graph.BossId));
            return order;
        }

        private static void Visit(DungeonGraph graph, int id, HashSet<int> visited, List<RoomNode> order, bool mainOnly)
        {
            if (!visited.Add(id)) return;
            var node = graph.GetNode(id);
            order.Add(node);
            foreach (var next in node.Neighbors.OrderBy(n => n))
            {
                var neighbour = graph.GetNode(next);
                if (neighbour.IsOnMainPath || next == graph.BossId) continue; // main-path progress is driven by the outer loop
                Visit(graph, next, visited, order, mainOnly);
            }
        }

        private int MarkerCount(RoomDefinition definition, RoomMarkerRole role = RoomMarkerRole.EnemySpawn)
        {
            if (definition.Prefab == null) return 0;
            return definition.Prefab.GetComponentsInChildren<RoomMarker>(true).Count(m => m.Role == role);
        }

        private static RuinRail.Gameplay.Events.DungeonEventKind EventKindFor(RoomDefinition definition, int seed, int depth, int nodeId) =>
            RoomCategoryComposer.ResolveEventKind(definition != null ? definition.Tags : null, seed, depth, nodeId);
    }
}
