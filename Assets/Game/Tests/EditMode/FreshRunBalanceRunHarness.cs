using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;
using static RuinRail.Tests.FreshRunBalance;

namespace RuinRail.Tests
{
    /// <summary>
    /// A whole deterministic depth, walked in the order a player meets the rooms, with a real loadout.
    ///
    /// Everything structural is the shipped implementation: the graph/layout generator and room pool, the Supply Chest
    /// planner, the encounter director and its threat budget, the elite and boss selectors, the depth curves, the loot
    /// tables rolled through the real LootRoller with the real per-source contexts, the real merchant bundle and the
    /// real prices. What this harness adds is the player: it spends the loadout's real effective cadence, magazine,
    /// reload and ammo cost (through <see cref="WeaponProfile"/>, i.e. through WeaponStatMath) against each enemy's
    /// real scaled health, falls back to the ammo-free secondary exactly when the firearm cannot fire, and accumulates
    /// time and incoming damage.
    ///
    /// The single-number assumptions are on the <see cref="SkillProfile"/>; they are stated in the report, never buried.
    /// </summary>
    public sealed class FreshRunSimulator
    {
        private readonly GameContentCatalog _content;
        private readonly BiomeRoomPools _pools;
        private readonly LootRoller _roller;
        private readonly Dictionary<string, ItemDefinition> _itemsById;

        /// <summary>Marker counts are a prefab walk; caching them keeps a 500-generation sweep from re-walking the same prefabs.</summary>
        private static readonly Dictionary<(RoomDefinition, RoomMarkerRole), int> MarkerCache = new();

        public FreshRunSimulator(GameContentCatalog content)
        {
            _content = content ?? throw new ArgumentNullException(nameof(content));
            _pools = BiomeRoomPools.Build(content.Rooms);
            _roller = content.Loot.CreateRoller();
            _itemsById = content.Items.Where(i => i != null).GroupBy(i => i.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        /// <summary>The weapons and starting reserve a simulated run carries.</summary>
        public sealed class Loadout
        {
            public Loadout(string name, WeaponProfile primary, WeaponProfile secondary, Dictionary<AmmoType, int> startingAmmo,
                int maxHealth, int bandages, float meleeShare = 0f)
            {
                Name = name;
                Primary = primary;
                Secondary = secondary;
                StartingAmmo = startingAmmo;
                MaxHealth = maxHealth;
                Bandages = bandages;
                MeleeShare = Mathf.Clamp01(meleeShare);
            }

            public string Name { get; }
            public WeaponProfile Primary { get; }
            public WeaponProfile Secondary { get; }
            public Dictionary<AmmoType, int> StartingAmmo { get; }
            public int MaxHealth { get; }
            public int Bandages { get; }

            /// <summary>
            /// Share of room enemies the player chooses to kill with the ammo-free secondary before the firearm runs
            /// dry. 0 models pistol-only clearing (the firearm is used until it cannot fire); 0.5 models the mixed
            /// two-weapon play the starter kit is built around. This is a usage assumption, not shipped data.
            /// </summary>
            public float MeleeShare { get; }
        }

        public sealed class RunResult
        {
            public string LoadoutName;
            public string SkillProfile;
            public Biome Biome;
            public int Seed;
            public int Depth;
            public int RoomCount;
            public int CombatRooms;
            public int EliteRooms;
            public bool HasMerchant;
            public bool HasBossCache;
            public int SupplyChests;
            public int OtherChests;
            public int EventRooms;

            public int StartHealth;
            public int MaxHealth;
            public readonly Dictionary<AmmoType, int> StartAmmo = new();
            public readonly Dictionary<string, int> EnemiesByArchetype = new(StringComparer.Ordinal);
            public int EnemiesKilled;
            public int ShotsFired;
            public int Hits;
            public int Misses;
            public int Reloads;
            public int MeleeAttacks;
            public int MeleeFallbackRooms;
            /// <summary>Enemies killed with the secondary by choice (the melee share), not because the firearm was dry.</summary>
            public int VoluntaryMeleeKills;
            /// <summary>Enemies the firearm could not finish: the secondary was the only option.</summary>
            public int ForcedMeleeKills;
            public int AmmoFound;
            public int AmmoPurchased;
            public int AmmoUsed;
            public int ReserveBeforeBoss;
            public int AmmoUsedOnBoss;
            public int ReserveAfterBoss;
            public int BandagesUsed;
            public float HealthBeforeBoss;
            public float HealthAfterBoss;
            public float LowestHealth;
            public bool Died;
            public string DiedAt = string.Empty;
            public int CoinsEarned;
            public int CoinsSpent;
            public int ItemsFound;
            public readonly Dictionary<Rarity, int> RarityFound = new();
            public float CombatSeconds;
            public float TraversalSeconds;
            public float LootSeconds;
            public float BossSeconds;
            public float RunSeconds => CombatSeconds + TraversalSeconds + LootSeconds;
            public string BossId = string.Empty;
            public bool BossCleared;
            public bool RanDryBeforeBoss;

            public float HitRate => ShotsFired == 0 ? 0f : Hits / (float)ShotsFired;
        }

        /// <summary>Seconds of walking between two rooms at the player's real move speed, from the authored room sizes.</summary>
        private float TraversalSecondsFor(RoomDefinition definition)
        {
            var size = RoomSizeClasses.DimensionsOf(definition != null ? definition.SizeClass : RoomSizeClass.Medium);
            var speed = Mathf.Max(0.1f, _content.PlayerBalance.MoveSpeed);
            return (size.x + size.y) * 0.5f / speed;
        }

        public RunResult Simulate(Loadout loadout, SkillProfile skill, Biome biome, int seed, int depth)
        {
            var rules = DungeonGraphRules.CreateDefault();
            try
            {
                var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(rules), _pools.PoolFor(biome), seed, depth);
                if (!generation.Success) throw new InvalidOperationException($"generation failed for {biome} seed {seed} depth {depth}: {generation.Error}");
                return Simulate(loadout, skill, generation, biome, seed, depth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rules);
            }
        }

        private RunResult Simulate(Loadout loadout, SkillProfile skill, DungeonGenerationResult generation, Biome biome, int seed, int depth)
        {
            var graph = generation.Graph;
            var layout = generation.Layout;
            var r = new RunResult
            {
                LoadoutName = loadout.Name, SkillProfile = skill.Name, Biome = biome, Seed = seed, Depth = depth,
                RoomCount = graph.Nodes.Count, MaxHealth = loadout.MaxHealth, StartHealth = loadout.MaxHealth
            };
            foreach (var kv in loadout.StartingAmmo) r.StartAmmo[kv.Key] = kv.Value;

            var reserve = new Dictionary<AmmoType, int>(loadout.StartingAmmo);
            var primary = loadout.Primary;
            var magazine = primary.Magazine;
            var health = (float)loadout.MaxHealth;
            r.LowestHealth = health;
            var bandages = loadout.Bandages;
            var coins = 0;
            var supplyRooms = SupplyChestPlanner.Plan(graph, seed, depth, SupplyChestPlanner.OrdinaryRoomPercent, SupplyChestPlanner.MinimumPerDepth);
            var context = new DungeonRuntimeContext(seed, depth, 1, _content.Enemies, null, _content.DepthScaling, _content.Elites, null);
            var useful = new[] { primary.UsesAmmo ? primary.AmmoType : AmmoType.Light };

            foreach (var node in AmmoEconomyHarness.TraversalOrder(graph))
            {
                var placement = layout.GetPlacement(node.Id);
                var definition = placement?.Definition;
                r.TraversalSeconds += TraversalSecondsFor(definition);

                switch (node.Type)
                {
                    case RoomType.Combat:
                    {
                        r.CombatRooms++;
                        var enemies = new List<EnemyProfile>();
                        if (node.IsElite && definition != null && definition.SupportsElite && context.PickElite(biome, node.Id) is { } elite)
                        {
                            r.EliteRooms++;
                            enemies.Add(EnemyProfile.From(elite, depth, _content.DepthScaling));
                            r.EnemiesByArchetype.TryGetValue(elite.Id, out var had);
                            r.EnemiesByArchetype[elite.Id] = had + 1;
                        }
                        else
                        {
                            var markers = definition != null ? MarkerCount(definition) : 0;
                            var plan = EncounterDirector.Compose(new EncounterContext(seed, depth, 1, biome, node.Id, definition?.Tags, markers), _content.Enemies);
                            foreach (var e in plan.Expand())
                            {
                                enemies.Add(EnemyProfile.From(e, depth, _content.DepthScaling));
                                r.EnemiesByArchetype.TryGetValue(e.Id, out var had);
                                r.EnemiesByArchetype[e.Id] = had + 1;
                            }
                        }

                        Fight(r, loadout, skill, enemies, ref magazine, reserve, ref health, ref bandages, false, out var usedMelee);
                        if (usedMelee) r.MeleeFallbackRooms++;
                        if (health <= 0f) { r.Died = true; r.DiedAt = "combat room " + node.Id; return Finish(r, reserve, magazine, health, coins); }

                        if (supplyRooms.Contains(node.Id))
                        {
                            r.SupplyChests++;
                            r.LootSeconds += 3f;
                            Collect(r, reserve, ref coins, _roller.Roll(SupplyTable(), LootContext.ForSource(seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + RoomCategoryComposer.SupplyChestSourceSlot, LootQuality.Standard, 1, useful)));
                        }

                        break;
                    }

                    case RoomType.Loot:
                    case RoomType.Treasure:
                    {
                        var chests = definition != null ? MarkerCount(definition, RoomMarkerRole.ChestSpawn) : 0;
                        r.OtherChests += chests;
                        var kind = node.Type == RoomType.Treasure ? LootSourceKind.TreasureChest : LootSourceKind.EquipmentChest;
                        for (var i = 0; i < chests; i++)
                        {
                            r.LootSeconds += 4f;
                            if (!_content.Loot.TryGet(kind, out var source) || source.Table == null) continue;
                            Collect(r, reserve, ref coins, _roller.Roll(source.Table, LootContext.ForSource(seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride + i, source.Quality, 1, useful)));
                        }

                        break;
                    }

                    case RoomType.Merchant:
                    {
                        r.HasMerchant = true;
                        r.LootSeconds += 8f;
                        if (primary.UsesAmmo && _content.Economy.TryGetAmmoBundle(primary.AmmoType, out var bundle))
                        {
                            var cap = _content.AmmoBalance.GetStackLimit(primary.AmmoType);
                            reserve.TryGetValue(primary.AmmoType, out var have);
                            while (have < cap && coins >= bundle.Price && bundle.Units > 0)
                            {
                                coins -= bundle.Price;
                                r.CoinsSpent += bundle.Price;
                                var added = Mathf.Min(cap - have, bundle.Units);
                                have += added;
                                r.AmmoPurchased += added;
                            }

                            reserve[primary.AmmoType] = have;
                        }

                        break;
                    }

                    case RoomType.Event:
                    {
                        r.EventRooms++;
                        r.LootSeconds += 6f;
                        var kind = RoomCategoryComposer.ResolveEventKind(definition?.Tags, seed, depth, node.Id);
                        // Paid events are only taken when the coins found so far actually cover them, which is the
                        // measurement this pass needs: affordability, not a scripted purchase.
                        var price = PriceFor(kind, depth);
                        if (price > 0 && coins >= price)
                        {
                            coins -= price;
                            r.CoinsSpent += price;
                        }

                        break;
                    }

                    case RoomType.MedicalRecovery:
                    {
                        r.LootSeconds += 5f;
                        var healed = Mathf.Min(loadout.MaxHealth - health, loadout.MaxHealth * 0.5f);
                        health += healed;
                        break;
                    }

                    case RoomType.Boss:
                    {
                        reserve.TryGetValue(primary.UsesAmmo ? primary.AmmoType : AmmoType.Light, out var beforeReserve);
                        r.ReserveBeforeBoss = beforeReserve + (primary.UsesAmmo ? magazine : 0);
                        r.HealthBeforeBoss = health;
                        r.RanDryBeforeBoss = primary.UsesAmmo && r.ReserveBeforeBoss <= 0;
                        var ammoBefore = r.AmmoUsed;
                        var secondsBefore = r.CombatSeconds;

                        var boss = BossSelection.Select(_content.Bosses, new BossSpawnRequest(biome, definition?.Tags ?? Array.Empty<string>(), seed, depth, node.Id, Vector2.zero, null));
                        if (boss != null)
                        {
                            r.BossId = boss.Id;
                            var profile = EnemyProfile.From(boss, depth, _content.DepthScaling);
                            Fight(r, loadout, skill, new List<EnemyProfile> { profile }, ref magazine, reserve, ref health, ref bandages, true, out var usedMelee);
                            if (usedMelee) r.MeleeFallbackRooms++;
                            r.BossCleared = health > 0f;
                            if (health <= 0f) { r.Died = true; r.DiedAt = "boss " + boss.Id; }
                        }

                        r.AmmoUsedOnBoss = r.AmmoUsed - ammoBefore;
                        r.BossSeconds = r.CombatSeconds - secondsBefore;
                        reserve.TryGetValue(primary.UsesAmmo ? primary.AmmoType : AmmoType.Light, out var afterReserve);
                        r.ReserveAfterBoss = afterReserve + (primary.UsesAmmo ? magazine : 0);
                        r.HealthAfterBoss = Mathf.Max(0f, health);

                        if (r.BossCleared && _content.Loot.TryGet(LootSourceKind.BossCache, out var cache) && cache.Table != null)
                        {
                            r.HasBossCache = true;
                            r.LootSeconds += 5f;
                            Collect(r, reserve, ref coins, _roller.Roll(cache.Table, LootContext.ForSource(seed, depth, node.Id * RoomCategoryComposer.ChestSourceStride, cache.Quality, 1, useful)));
                        }

                        break;
                    }
                }
            }

            return Finish(r, reserve, magazine, health, coins);
        }

        private RunResult Finish(RunResult r, Dictionary<AmmoType, int> reserve, int magazine, float health, int coins)
        {
            r.CoinsEarned = coins + r.CoinsSpent;
            r.LowestHealth = Mathf.Min(r.LowestHealth, Mathf.Max(0f, health));
            return r;
        }

        /// <summary>One engagement: spend the primary while it can fire, fall back to the ammo-free secondary when it cannot.</summary>
        private void Fight(RunResult r, Loadout loadout, SkillProfile skill, List<EnemyProfile> enemies,
            ref int magazine, Dictionary<AmmoType, int> reserve, ref float health, ref int bandages, bool isBoss, out bool usedMelee)
        {
            usedMelee = false;
            var primary = loadout.Primary;
            var secondary = loadout.Secondary;
            var downtime = isBoss ? ExposureModel.BossDowntime : enemies.Count > 0 && enemies[0].Id.StartsWith("elite_", StringComparison.Ordinal) ? ExposureModel.EliteDowntime : 0f;

            var enemyIndex = 0;
            foreach (var enemy in enemies)
            {
                // A shielded enemy is fought at its plain health here: whether the player flanks it is a skill question
                // the depth TTK matrix measures on its own axis, and assuming either answer inside a run would bias it.
                var remaining = enemy.Health;
                var seconds = 0f;
                var shotsThisFight = 0;
                // Deliberate mixed-weapon play: an even, deterministic share of each room's enemies is taken with the
                // ammo-free secondary by choice. A boss is never fought by choice with the secondary.
                var byChoice = !isBoss && secondary != null && loadout.MeleeShare > 0f
                               && Mathf.FloorToInt((enemyIndex + 1) * loadout.MeleeShare) > Mathf.FloorToInt(enemyIndex * loadout.MeleeShare);
                enemyIndex++;
                if (byChoice) r.VoluntaryMeleeKills++;

                while (remaining > 0f)
                {
                    var weapon = byChoice ? secondary : primary;
                    if (!byChoice && primary.UsesAmmo)
                    {
                        reserve.TryGetValue(primary.AmmoType, out var have);
                        if (magazine < primary.AmmoPerShot && have >= primary.AmmoPerShot)
                        {
                            var wanted = Mathf.Min(primary.Magazine - magazine, have);
                            magazine += wanted;
                            reserve[primary.AmmoType] = have - wanted;
                            r.Reloads++;
                            seconds += primary.ReloadSeconds * (1f + skill.ReloadWasteFactor);
                        }

                        if (magazine < primary.AmmoPerShot)
                        {
                            weapon = secondary;
                            usedMelee = true;
                        }
                    }

                    if (!byChoice && weapon == primary && primary.UsesAmmo)
                    {
                        magazine -= primary.AmmoPerShot;
                        r.AmmoUsed += primary.AmmoPerShot;
                    }

                    r.ShotsFired++;
                    shotsThisFight++;
                    if (weapon.Kind == WeaponKind.Melee) r.MeleeAttacks++;
                    var hit = Fraction(r.ShotsFired, skill.Accuracy);
                    if (hit)
                    {
                        r.Hits++;
                        remaining -= weapon.Pellets > 1
                            ? weapon.PerPelletDamage * weapon.Pellets * skill.PelletConnect
                            : weapon.PerPelletDamage;
                    }
                    else
                    {
                        r.Misses++;
                    }

                    seconds += 1f / Mathf.Max(0.0001f, weapon.ShotsPerSecond);
                    if (weapon.Kind == WeaponKind.Blaster && weapon.ShotsToOverheat > 0 && shotsThisFight % weapon.ShotsToOverheat == 0)
                        seconds += weapon.OverheatLockout;

                    if (seconds > 600f) break; // a fight that cannot end is recorded, not looped forever
                }

                if (!byChoice && usedMelee) r.ForcedMeleeKills++;
                var exposureSeconds = seconds * (1f + downtime);
                r.CombatSeconds += exposureSeconds;
                var incoming = enemy.DamagePerSecond * exposureSeconds *
                               (byChoice || usedMelee || primary.Kind == WeaponKind.Melee ? ExposureModel.MeleeContact : enemy.IsRanged ? ExposureModel.RangedVsRanged : ExposureModel.RangedVsMelee) *
                               (1f - skill.DodgeEfficiency);
                health -= incoming;
                r.LowestHealth = Mathf.Min(r.LowestHealth, Mathf.Max(0f, health));
                r.EnemiesKilled++;

                while (health <= loadout.MaxHealth * 0.35f && bandages > 0)
                {
                    bandages--;
                    r.BandagesUsed++;
                    health = Mathf.Min(loadout.MaxHealth, health + 40f);
                }

                if (health <= 0f) return;
            }
        }

        /// <summary>
        /// Deterministic hit/miss. Exactly round(n x accuracy) of the first n shots connect, spread evenly, with no RNG
        /// stream of its own — so a run is reproducible from its seed and the skill profile alone, and two loadouts
        /// compared at the same profile see the identical miss pattern.
        /// </summary>
        private static bool Fraction(int index, float share) =>
            share >= 1f || Mathf.FloorToInt(index * share) > Mathf.FloorToInt((index - 1) * share);

        private void Collect(RunResult r, Dictionary<AmmoType, int> reserve, ref int coins, LootResult roll)
        {
            coins += roll.Coins;
            foreach (var item in roll.Items)
            {
                _itemsById.TryGetValue(item.DefinitionId, out var definition);
                if (definition is AmmoItemDefinition ammo)
                {
                    var cap = _content.AmmoBalance.GetStackLimit(ammo.AmmoType);
                    reserve.TryGetValue(ammo.AmmoType, out var have);
                    // Three stacks: the backpack can hold several stacks of one ammo type, so a depth's finds are not
                    // silently capped at one stack's worth.
                    var added = Mathf.Min(cap * 3 - have, item.Quantity);
                    reserve[ammo.AmmoType] = have + Mathf.Max(0, added);
                    r.AmmoFound += Mathf.Max(0, added);
                    continue;
                }

                r.ItemsFound++;
                r.RarityFound.TryGetValue(item.Rarity, out var had);
                r.RarityFound[item.Rarity] = had + 1;
            }
        }

        /// <summary>
        /// Replaces the Supply Chest table for the duration of a measurement, so a before/after comparison of a
        /// candidate ammo quantity runs on one code path with one seed list instead of two repository states.
        /// </summary>
        public LootTableDefinition SupplyTableOverride { get; set; }

        private LootTableDefinition SupplyTable() =>
            SupplyTableOverride != null ? SupplyTableOverride
            : _content.Loot.TryGet(LootSourceKind.SupplyChest, out var source) ? source.Table : null;

        private int PriceFor(DungeonEventKind kind, int depth)
        {
            var prices = new RuinRail.Gameplay.Economy.PriceService(_content.Economy);
            return kind switch
            {
                DungeonEventKind.LockedVault => prices.EventPrice(RuinRail.Gameplay.Economy.DungeonEventPriceKind.LockedVault, depth),
                DungeonEventKind.BrokenMachine => prices.EventPrice(RuinRail.Gameplay.Economy.DungeonEventPriceKind.BrokenMachine, depth),
                _ => 0
            };
        }

        public static int MarkerCount(RoomDefinition definition, RoomMarkerRole role = RoomMarkerRole.EnemySpawn)
        {
            if (definition == null || definition.Prefab == null) return 0;
            if (MarkerCache.TryGetValue((definition, role), out var cached)) return cached;
            var count = definition.Prefab.GetComponentsInChildren<RoomMarker>(true).Count(m => m.Role == role);
            MarkerCache[(definition, role)] = count;
            return count;
        }
    }
}
