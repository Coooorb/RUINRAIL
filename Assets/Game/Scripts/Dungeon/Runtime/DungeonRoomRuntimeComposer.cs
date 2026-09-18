using System.Collections.Generic;
using System.Linq;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>Everything the per-room runtimes need from the expedition: seed, depth, party, archetypes, spawner.</summary>
    public sealed class DungeonRuntimeContext
    {
        public DungeonRuntimeContext(int runSeed, int depth, int partySize, IEnumerable<EnemyDefinition> archetypes, IEnemySpawner spawner, DepthScalingConfig scaling = null, IEnumerable<EliteDefinition> elites = null, IEliteSpawner eliteSpawner = null)
        {
            Elites = (elites ?? Enumerable.Empty<EliteDefinition>()).Where(e => e != null).OrderBy(e => e.Id, System.StringComparer.Ordinal).ToList();
            EliteSpawner = eliteSpawner;
            RunSeed = runSeed;
            Depth = depth;
            PartySize = partySize;
            Archetypes = (archetypes ?? Enumerable.Empty<EnemyDefinition>()).Where(a => a != null).ToList();
            Spawner = spawner;
            Scaling = scaling;
        }

        public int RunSeed { get; }
        public int Depth { get; }
        public int PartySize { get; }
        public IReadOnlyList<EnemyDefinition> Archetypes { get; }
        public IEnemySpawner Spawner { get; }
        public DepthScalingConfig Scaling { get; }

        /// <summary>Node ids of the ordinary rooms that carry a Supply Chest this depth (SupplyChestPlanner); empty until the composer plans the layout.</summary>
        public IReadOnlyCollection<int> SupplyChestRooms { get; set; } = System.Array.Empty<int>();

        public bool HasSupplyChest(int nodeId) => SupplyChestRooms != null && SupplyChestRooms.Contains(nodeId);

        /// <summary>The biome's hand-designed Elites (45: two per biome); one is picked per Elite room by seed.</summary>
        public IReadOnlyList<EliteDefinition> Elites { get; }
        public IEliteSpawner EliteSpawner { get; }

        /// <summary>Seeded, per-room Elite pick on the Encounter stream (45: one Elite per Elite encounter).</summary>
        public EliteDefinition PickElite(Core.Biome biome, int roomIndex)
        {
            var candidates = Elites.Where(e => e.Biome == biome).ToList();
            if (candidates.Count == 0) return null;
            var random = new SeededRandom(SeededRandom.MixSeed(RunSeed, Depth, (int)RngStream.Encounter, 0x454C, roomIndex));
            return candidates[random.NextInt(candidates.Count)];
        }
    }

    /// <summary>
    /// Attaches a RoomRuntime (+ entry trigger) to every instantiated room of a layout and composes each Combat room's
    /// encounter through the Encounter Director with the room's node id as the deterministic room index. Non-combat
    /// rooms get no encounter, so a Start room can never spawn combat unless a later task authors it explicitly.
    /// </summary>
    public static class DungeonRoomRuntimeComposer
    {
        public static Dictionary<int, RoomRuntime> Attach(DungeonLayout layout, IReadOnlyDictionary<int, RoomRoot> rooms, DungeonRuntimeContext context, DungeonRuntimeServices services = null)
        {
            var runtimes = new Dictionary<int, RoomRuntime>();
            // The depth's ordinary-room loot budget is decided once per layout, from the graph and the seed, never per room.
            if (layout.Graph != null) context.SupplyChestRooms = SupplyChestPlanner.Plan(layout.Graph, context.RunSeed, context.Depth);
            foreach (var placement in layout.Placements)
            {
                if (!rooms.TryGetValue(placement.NodeId, out var root) || root == null) continue;
                var runtime = root.gameObject.AddComponent<RoomRuntime>();
                runtime.Configure(root, placement.NodeId, context.Depth, context.PartySize);
                runtime.SetScaling(context.Scaling);
                runtime.SetSpawner(context.Spawner);
                var isElite = layout.Graph != null && layout.Graph.GetNode(placement.NodeId).IsElite;
                if (placement.Definition.RoomType == RoomType.Combat && isElite && placement.Definition.SupportsElite)
                {
                    var elite = context.PickElite(layout.Biome, placement.NodeId);
                    if (elite != null && context.EliteSpawner != null)
                    {
                        runtime.Configure(root, placement.NodeId, context.Depth, context.PartySize, isElite: true);
                        var engagement = new EliteEngagement(elite, context.EliteSpawner, context.Depth, context.PartySize, context.Scaling);
                        if (services?.Expedition != null) engagement.EliteDefeated += (_, xp) => { if (services.Expedition.IsExpeditionActive) services.Expedition.RecordEliteDefeated(xp); };
                        runtime.SetEngagement(engagement);
                    }
                    else
                    {
                        isElite = false;
                    }
                }

                if (placement.Definition.RoomType == RoomType.Combat && !isElite)
                {
                    var markers = root.GetMarkers(RoomMarkerRole.EnemySpawn).Count;
                    var encounterContext = new EncounterContext(context.RunSeed, context.Depth, context.PartySize, layout.Biome, placement.NodeId, placement.Definition.Tags, markers);
                    runtime.SetEncounter(EncounterDirector.Compose(encounterContext, context.Archetypes), context.Spawner);
                }

                if (services?.Expedition != null)
                {
                    runtime.EnemySpawned += (_, enemy) => enemy.Died += e => { if (services.Expedition.IsExpeditionActive) services.Expedition.RecordEnemyDefeated(e.XpValue); };
                }

                RoomCategoryComposer.Compose(runtime, context, services);
                RoomEntryTrigger.Attach(runtime);
                runtimes[placement.NodeId] = runtime;
            }

            return runtimes;
        }
    }
}
