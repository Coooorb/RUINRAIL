using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>Spawns the arena's boss encounter (46: selection by arena tag or seed among the biome's two bosses).</summary>
    public interface IBossSpawner
    {
        BossEncounter Spawn(in BossSpawnRequest request);
    }

    /// <summary>IBossSpawner over the authored boss roster (DefaultBossSpawner).</summary>
    public sealed class RosterBossSpawner : IBossSpawner
    {
        private readonly DefaultBossSpawner _inner;

        public RosterBossSpawner(DefaultBossSpawner inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public DefaultBossSpawner Inner => _inner;
        public BossEncounter Spawn(in BossSpawnRequest request) => _inner.Spawn(request);
    }

    /// <summary>
    /// Shared services the special room runtimes bind to. Optional members leave their room category inert (the
    /// composer reports what was skipped) rather than inventing fallbacks. All per-depth state (merchant stock,
    /// event instances, chest contexts) derives from RunSeed + Depth, so a room binds the same content every time.
    /// </summary>
    public sealed class DungeonRuntimeServices
    {
        public LootSourceCatalog LootCatalog { get; set; }
        public GroundLootRegistry GroundLoot { get; set; }
        public Func<string, ItemDefinition> ResolveDefinition { get; set; }
        public IReadOnlyList<ItemDefinition> ItemCatalog { get; set; } = Array.Empty<ItemDefinition>();
        public IReadOnlyCollection<AmmoType> UsefulAmmoTypes { get; set; }

        public PriceService Prices { get; set; }
        public DungeonMerchantConfig MerchantConfig { get; set; }
        public CoinWallet CarriedWallet { get; set; }

        public DungeonEventConfig EventConfig { get; set; }
        public IReviveAuthority ReviveAuthority { get; set; }

        public IBossSpawner BossSpawner { get; set; }
        public ExpeditionService Expedition { get; set; }

        private readonly Dictionary<int, DungeonMerchantState> _merchantStates = new();

        /// <summary>One merchant bookkeeping record per depth (58: no refresh within a depth; reopen = same stock).</summary>
        public DungeonMerchantState MerchantStateFor(int depth)
        {
            if (!_merchantStates.TryGetValue(depth, out var state))
            {
                state = new DungeonMerchantState(depth);
                _merchantStates[depth] = state;
            }

            return state;
        }

        public bool HasLoot => LootCatalog != null;
        public bool HasMerchant => Prices != null && MerchantConfig != null && CarriedWallet != null && LootCatalog != null && ItemCatalog != null && ItemCatalog.Count > 0;
        public bool HasEvents => EventConfig != null && LootCatalog != null && Prices != null;
        public bool HasBoss => BossSpawner != null;

        public LootSpawner CreateLootSpawner(GameObject host)
        {
            var spawner = host.AddComponent<LootSpawner>();
            spawner.SetRegistry(GroundLoot);
            spawner.SetDefinitionResolver(ResolveDefinition);
            return spawner;
        }

        public Func<LootQuality, RarityTableDefinition> RarityTables => LootCatalog != null ? LootCatalog.RarityTableFor : (_ => null);

        public IEnumerable<ItemDefinition> Items => ItemCatalog ?? Enumerable.Empty<ItemDefinition>();
    }
}
