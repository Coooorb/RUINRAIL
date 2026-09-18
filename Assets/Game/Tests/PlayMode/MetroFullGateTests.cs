using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// TASK 090 Ruined Metro gate: complete seeded solo expeditions on the real 21-room pool (generation pipeline →
    /// instantiated prefabs → room runtimes → every category → boss → transit), conservation through descend/return
    /// and failure, both bosses and both Elites, and a performance smoke at the active caps.
    /// </summary>
    public class MetroFullGateTests
    {
        // Biome parameters: the Rustworks gate (TASK 118) derives from this class and overrides them.
        protected virtual Biome GateBiome => Biome.RuinedMetro;
        protected virtual string RoomFolder => "Assets/Game/ScriptableObjects/Rooms/RuinedMetro";
        protected virtual string ReportPath => "TestResults/gate_090_metro_runs.md";
        protected virtual string PerfReportPath => "TestResults/gate_090_perf.md";
        protected virtual string ReportTitle => "# Ruined Metro full-run gate (TASK 090)";
        protected virtual string[] ExpectedBossIds => new[] { "boss_the_conductor", "boss_tunnel_maw" };
        protected virtual (string eliteId, int depth)[] EliteFixtures => new[] { ("elite_tunnel_stalker", 3), ("elite_railguard", 12) };

        private readonly List<Object> _created = new();
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private List<EnemyDefinition> _archetypes;
        private List<EliteDefinition> _elites;
        private List<BossDefinition> _bosses;
        private StaggerConfig _stagger;
        private DepthScalingConfig _scaling;
        private DungeonGraphRules _rules;
        private RoomPool _pool;
        private BiomeRoomPools _biomePools;

        /// <summary>TASK 146: a cross-biome run picks the pool of the biome the seeded selector chose for the depth; single-biome gates keep their own pool.</summary>
        private RoomPool PoolForDepth(Run run) => run.CrossBiome ? _biomePools.PoolFor(run.State.Biome) : _pool;
        private DungeonEventConfig _eventConfig;

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" }).Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _elites = AssetDatabase.FindAssets("t:EliteDefinition").Select(g => AssetDatabase.LoadAssetAtPath<EliteDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _bosses = AssetDatabase.FindAssets("t:BossDefinition").Select(g => AssetDatabase.LoadAssetAtPath<BossDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _stagger = AssetDatabase.LoadAssetAtPath<StaggerConfig>("Assets/Game/ScriptableObjects/Balance/StaggerConfig.asset");
            _scaling = AssetDatabase.LoadAssetAtPath<DepthScalingConfig>("Assets/Game/ScriptableObjects/Balance/DepthScalingConfig.asset");
            _rules = DungeonGraphRules.CreateDefault();
            _created.Add(_rules);
            var rooms = AssetDatabase.FindAssets("t:RoomDefinition", new[] { RoomFolder }).Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _pool = RoomPool.Build(rooms, GateBiome);
            Assert.AreEqual(21, _pool.Rooms.Count);
            _biomePools = BiomeRoomPools.Build(AssetDatabase.FindAssets("t:RoomDefinition", new[] { "Assets/Game/ScriptableObjects/Rooms" }).Select(g => AssetDatabase.LoadAssetAtPath<RoomDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null && !AssetDatabase.GetAssetPath(d).Contains("/_Test/")));
            Assert.IsTrue(_biomePools.IsComplete, string.Join("; ", _biomePools.Problems()));
            Assert.AreEqual(2, _elites.Count(e => e.Biome == GateBiome));
            Assert.AreEqual(2, _bosses.Count(b => b.Biome == GateBiome));

            // Gate copy of the event config with a short Supply Signal so a full run stays within test time (30 s authored).
            _eventConfig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset"));
            _created.Add(_eventConfig);
            Set(_eventConfig, "_supplySignalWaveSeconds", 1.5f);
            Set(_eventConfig, "_supplySignalWaveInterval", 1f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var actor in Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            foreach (var p in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            foreach (var root in Object.FindObjectsByType<RoomRoot>(FindObjectsSortMode.None)) if (root != null) Object.DestroyImmediate(root.gameObject);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private sealed class Run
        {
            public ExpeditionService Expedition;
            public PlayerProfile Profile;
            public ExpeditionState State;
            public DungeonRuntimeServices Services;
            public GameObject Player;
            public PlayerLootReceiver Receiver;
            public HealthComponent Health;
            public readonly List<string> Log = new();
            public readonly HashSet<string> BossesSeen = new();
            public readonly List<Biome> BiomesPlayed = new();
            public readonly HashSet<string> ElitesSeen = new();
            public readonly HashSet<RoomType> CategoriesVisited = new();
            public readonly HashSet<DungeonEventKind> EventsResolved = new();
            public int Rooms;
            public bool CrossBiome;
        }

        private Run StartRun(int runSeed, int bankedCoins = 100, bool crossBiome = false)
        {
            var ammoByType = _catalog.OfType<AmmoItemDefinition>().GroupBy(a => a.AmmoType).ToDictionary(g => g.Key, g => g.First());
            var run = new Run
            {
                Expedition = new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance),
                Profile = new PlayerProfile
                {
                    BankedCoins = bankedCoins,
                    TotalXp = 0,
                    SafeLoadout = new InventorySnapshot
                    {
                        Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                        Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
                    }
                }
            };
            run.CrossBiome = crossBiome;
            run.State = run.Expedition.Start(run.Profile, runSeed, crossBiome ? BiomeSelector.SelectFirst(runSeed) : GateBiome);
            var spawner = new DefaultEnemySpawner(_stagger);
            run.Services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = id => _registry.TryGet(id, out var d) ? d : null,
                ItemCatalog = _catalog,
                Prices = new PriceService(AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset")),
                MerchantConfig = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset"),
                CarriedWallet = run.State.CarriedWallet,
                EventConfig = _eventConfig,
                BossSpawner = new RosterBossSpawner(new DefaultBossSpawner(_bosses, _stagger, spawner)),
                Expedition = run.Expedition
            };
            new GroundLootLifetime(run.Services.GroundLoot, run.Expedition);

            run.Player = new GameObject("Player");
            _created.Add(run.Player);
            run.Player.AddComponent<CircleCollider2D>().isTrigger = true;
            run.Player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            run.Health = run.Player.AddComponent<HealthComponent>();
            run.Health.SetMaxHealth(100);
            run.Receiver = run.Player.AddComponent<PlayerLootReceiver>();
            run.Receiver.SetInventory(run.State.Inventory);
            run.Receiver.SetWallet(run.State.CarriedWallet);
            return run;
        }

        private static int TotalUnits(PlayerInventory inventory)
        {
            var total = 0;
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) { var e = inventory.GetEquipped(slot); if (e != null) total += e.Quantity; }
            foreach (var s in inventory.BackpackSlots) if (s != null) total += s.Quantity;
            return total;
        }

        private static int GroundUnits(GroundLootRegistry ground) => ground.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null && p.Item != null).Sum(p => p.Item.Quantity);

        private static IEnumerable<string> CarriedIds(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) { var e = inventory.GetEquipped(slot); if (e != null) yield return e.InstanceId; }
            foreach (var s in inventory.BackpackSlots) if (s != null) yield return s.InstanceId;
        }

        private static void AssertNoDuplicates(Run run)
        {
            var containers = new List<IItemContainer> { new BackpackContainer(run.State.Inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) containers.Add(new EquippedSlotContainer(run.State.Inventory, slot));
            containers.AddRange(run.Services.GroundLoot.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null));
            CollectionAssert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(containers));
            var ids = CarriedIds(run.State.Inventory).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count());
        }

        private IEnumerator PickUpEverything(Run run)
        {
            foreach (var go in run.Services.GroundLoot.Tracked.ToArray())
            {
                if (go == null) continue;
                var item = go.GetComponent<WorldItemPickup>();
                if (item != null)
                {
                    var result = item.TryPickUp(run.Receiver.Backpack, run.Receiver.TransferService);
                    Assert.IsTrue(result.Success || result.Error == TransferError.DestinationRejected, result.Error.ToString());
                    continue;
                }

                var coins = go.GetComponent<CoinPickup>();
                if (coins != null) coins.Interact(run.Player);
            }

            yield return null;
        }

        /// <summary>The player only fights what is in the current room: kills are limited to the room's bounds (with a margin for summons).</summary>
        private static bool Inside(RoomRuntime room, Vector3 position)
        {
            if (room == null) return true;
            var min = (Vector2)room.Root.transform.position - Vector2.one * 3f;
            var max = (Vector2)room.Root.transform.position + (Vector2)room.Root.Size + Vector2.one * 3f;
            return position.x >= min.x && position.x <= max.x && position.y >= min.y && position.y <= max.y;
        }

        private static IEnumerator KillUntil(System.Func<bool> done, int maxSteps = 400, System.Func<string> label = null, RoomRuntime room = null)
        {
            var guard = 0;
            while (!done() && guard++ < maxSteps)
            {
                foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy.IsAlive && Inside(room, enemy.transform.position)) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                foreach (var actor in Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)) if (actor.IsAlive && Inside(room, actor.transform.position)) actor.Health.TryApplyDamage(new DamageRequest(99999));
                yield return new WaitForSeconds(0.02f);
            }

            if (!done())
            {
                var alive = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Where(e => e.IsAlive).Select(e => $"{e.name}@{e.transform.position}").ToList();
                var actors = Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None).Where(a => a.IsAlive).Select(a => $"{a.name}@{a.transform.position}").ToList();
                Assert.Fail($"Encounter did not resolve within the step budget: {label?.Invoke()}; alive enemies [{string.Join(", ", alive)}]; alive actors [{string.Join(", ", actors)}]");
            }
        }

        /// <summary>Plays one depth of the run: generate, instantiate, compose, visit every room, resolve the boss, board the transit.</summary>
        private IEnumerator PlayDepth(Run run, TransitChoice choice)
        {
            var depth = run.State.Depth;
            var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(_rules), PoolForDepth(run), run.State.RunSeed, depth);
            Assert.IsTrue(generation.Success, generation.Error);
            var dungeonRoot = new GameObject($"Dungeon_D{depth}");
            _created.Add(dungeonRoot);
            var rooms = DungeonLayoutInstantiator.Instantiate(generation.Layout, dungeonRoot.transform);
            var context = new DungeonRuntimeContext(run.State.RunSeed, depth, 1, _archetypes, new DefaultEnemySpawner(_stagger), _scaling, _elites, new DefaultEliteSpawner(_stagger));
            run.BiomesPlayed.Add(run.State.Biome);
            if (run.CrossBiome) Assert.IsTrue(rooms.Values.All(r => r.Definition == null || r.Definition.Biome == run.State.Biome), $"depth {depth}: every room belongs to the selected biome {run.State.Biome}.");
            var runtimes = DungeonRoomRuntimeComposer.Attach(generation.Layout, rooms, context, run.Services);
            Assert.AreEqual(rooms.Count, runtimes.Count);
            run.Log.Add($"depth {depth}: {rooms.Count} rooms, {generation.Rounds} generation round(s), layout {generation.Layout.Signature().GetHashCode():X8}");

            var graph = generation.Graph;
            var order = new List<int> { graph.StartId };
            var visited = new HashSet<int> { graph.StartId };
            for (var i = 0; i < order.Count; i++)
            {
                foreach (var next in graph.GetNode(order[i]).Neighbors.OrderBy(n => n)) if (visited.Add(next)) order.Add(next);
            }

            // The boss arena is the end of the run: every other room first, then the boss and the transit.
            order = order.Where(n => n != graph.BossId).Append(graph.BossId).ToList();
            foreach (var nodeId in order)
            {
                var runtime = runtimes[nodeId];
                var root = runtime.Root;
                var center = (Vector2)root.transform.position + (Vector2)root.Size * 0.5f;
                var spawn = root.GetMarkers(RoomMarkerRole.PlayerSpawn).FirstOrDefault();
                run.Player.transform.position = spawn != null ? (Vector2)root.transform.TransformPoint(spawn.WorldCenter) : center;
                run.CategoriesVisited.Add(runtime.State.RoomType);
                run.Rooms++;
                var binding = runtime.GetComponent<RoomContentBinding>();
                Assert.IsEmpty(binding.Skipped.Where(s => s.Contains("services_missing") || s.Contains("no_anchor") || s.Contains("no_spawner")), $"{runtime.State.RoomId}: {string.Join(",", binding.Skipped)}");

                Assert.IsTrue(runtime.NotifyPlayerEntered(run.Player), $"{runtime.State.RoomId} first entry");
                if (runtime.Lifecycle == RoomLifecycleState.Active)
                {
                    Assert.IsTrue(runtime.DoorsLocked, runtime.State.RoomId);
                    if (runtime.Engagement is EliteEngagement elite) run.ElitesSeen.Add(elite.Definition.Id);
                    if (runtime.Engagement is BossEngagement boss) run.BossesSeen.Add(boss.Encounter.Boss.Definition.Id);
                    yield return null;
                    yield return KillUntil(() => runtime.Lifecycle == RoomLifecycleState.Cleared, 400, () => $"{runtime.State.RoomId} node {runtime.State.NodeId} engagement {runtime.Engagement?.GetType().Name} encounter living {runtime.Encounter?.LivingCount} pending {runtime.Encounter?.PendingCount} spawned {runtime.State.EnemiesSpawned}", runtime);
                    Assert.IsFalse(runtime.DoorsLocked, runtime.State.RoomId);
                    if (run.Expedition.IsExpeditionActive) run.Expedition.RecordRoomCleared();
                }

                Assert.AreEqual(RoomLifecycleState.Cleared, runtime.Lifecycle, runtime.State.RoomId);
                Assert.IsFalse(runtime.NotifyPlayerEntered(run.Player), "Re-entry never re-runs a room.");

                switch (runtime.State.RoomType)
                {
                    case RoomType.Loot:
                    case RoomType.Treasure:
                        foreach (var chest in binding.Chests) Assert.IsTrue(chest.TryOpen(out _), runtime.State.RoomId);
                        foreach (var chest in binding.Chests) Assert.IsFalse(chest.TryOpen(out _));
                        var unitsBefore = TotalUnits(run.State.Inventory) + GroundUnits(run.Services.GroundLoot);
                        yield return PickUpEverything(run);
                        Assert.AreEqual(unitsBefore, TotalUnits(run.State.Inventory) + GroundUnits(run.Services.GroundLoot), "Pickups conserve item units.");
                        break;
                    case RoomType.Merchant:
                        Assert.IsNotNull(binding.Merchant, runtime.State.RoomId);
                        run.State.CarriedWallet.Credit(1500, "gate_funding");
                        var offer = binding.Merchant.Merchant.Offers.First(o => o.Definition is EquipmentItemDefinition);
                        var before = run.State.CarriedCoins;
                        var bought = binding.Merchant.Merchant.Buy(offer.Index, run.Receiver.Backpack);
                        if (bought == TradeError.None)
                        {
                            Assert.AreEqual(before - offer.Price, run.State.CarriedCoins);
                            Assert.AreEqual(TradeError.AlreadySold, binding.Merchant.Merchant.Buy(offer.Index, run.Receiver.Backpack));
                        }
                        else
                        {
                            Assert.AreEqual(TradeError.DestinationRejected, bought, "A full backpack is the only acceptable refusal here.");
                            Assert.AreEqual(before, run.State.CarriedCoins);
                        }

                        break;
                    case RoomType.Event:
                    case RoomType.MedicalRecovery:
                        Assert.IsNotNull(binding.EventInstance, runtime.State.RoomId);
                        yield return ResolveEvent(run, runtime, binding);
                        break;
                    case RoomType.Boss:
                        Assert.IsTrue(binding.Boss.IsDefeated);
                        Assert.IsFalse(binding.BossCache.IsLocked);
                        Assert.IsTrue(binding.Transit.IsActivated);
                        Assert.IsTrue(binding.BossCache.TryOpen(out var cache));
                        Assert.IsFalse(binding.BossCache.TryOpen(out _));
                        Assert.IsTrue(cache.Items.Count > 0);
                        yield return PickUpEverything(run);
                        Assert.IsTrue(binding.Transit.Interact(run.Player));
                        Assert.IsFalse(binding.Transit.Interact(run.Player));
                        AssertNoDuplicates(run);
                        Assert.IsTrue(binding.Transit.Choose(choice), "Transit choice applies once.");
                        Assert.IsFalse(binding.Transit.Choose(choice));
                        break;
                }

                AssertNoDuplicates(run);
            }

            Object.DestroyImmediate(dungeonRoot);
            yield return null;
        }

        private IEnumerator ResolveEvent(Run run, RoomRuntime runtime, RoomContentBinding binding)
        {
            var instance = binding.EventInstance;
            run.State.CarriedWallet.Credit(2000, "gate_event_funding");
            var coinsBefore = run.State.CarriedCoins;
            switch (instance)
            {
                case MedicalStationEvent station:
                    run.Health.TryApplyDamage(new DamageRequest(40));
                    Assert.IsTrue(binding.Event.Interact(run.Player));
                    Assert.AreEqual(100, run.Health.CurrentHealth, "Healed to max.");
                    Assert.AreEqual(coinsBefore - station.HealCost, run.State.CarriedCoins);
                    Assert.IsFalse(binding.Event.Interact(run.Player), "Full health: no second charge.");
                    run.EventsResolved.Add(DungeonEventKind.MedicalStation);
                    break;
                case WeaponCacheEvent cache:
                    var choice = cache.Choose(DungeonEventInteractable.ActorFor(run.Player), 0);
                    Assert.IsTrue(choice.Outcome == DungeonEventOutcome.Success || choice.Outcome == DungeonEventOutcome.Unavailable);
                    if (choice.Outcome == DungeonEventOutcome.Success) Assert.AreEqual(DungeonEventOutcome.None, cache.Choose(DungeonEventInteractable.ActorFor(run.Player), 1).Outcome);
                    run.EventsResolved.Add(DungeonEventKind.WeaponCache);
                    break;
                default:
                    Assert.IsTrue(binding.Event.Interact(run.Player), $"{instance.Kind} activation");
                    if (instance is CursedChestEvent || instance is SupplySignalEvent)
                    {
                        yield return null;
                        yield return KillUntil(() => instance.Phase == DungeonEventPhase.Completed || instance.Phase == DungeonEventPhase.Failed, 600, () => $"event {instance.Kind} in {runtime.State.RoomId} phase {instance.Phase}", runtime);
                    }

                    Assert.IsTrue(instance.Phase == DungeonEventPhase.Completed || instance.Phase == DungeonEventPhase.Failed, $"{instance.Kind} resolves");
                    Assert.IsFalse(binding.Event.Interact(run.Player), "Resolved events refuse further interaction.");
                    Assert.AreEqual(coinsBefore - instance.Result.CoinsSpent, run.State.CarriedCoins, $"{instance.Kind} charges exactly once.");
                    run.EventsResolved.Add(instance.Kind);
                    break;
            }

            yield return PickUpEverything(run);
        }

        // ---- Acceptance 1-3: complete runs across seeds ----

        [UnityTest]
        public IEnumerator SeededSoloExpeditions_DescendThenReturn_ConserveEverything_AndCoverBothBossesAndElites()
        {
            var report = new StringBuilder();
            report.AppendLine(ReportTitle);
            report.AppendLine();
            var bossesSeen = new HashSet<string>();
            var elitesSeen = new HashSet<string>();
            var categories = new HashSet<RoomType>();
            var events = new HashSet<DungeonEventKind>();
            var seeds = new[] { 2026, 7, 13, 21, 42, 99, 104, 313, 5, 64, 77, 128, 256, 512, 777, 1024 };
            var required = new[] { RoomType.Start, RoomType.Combat, RoomType.Merchant, RoomType.Event, RoomType.Loot, RoomType.Treasure, RoomType.MedicalRecovery, RoomType.Boss };
            var runsPlayed = 0;

            foreach (var seed in seeds)
            {
                // At least eight seeds; keep going (bounded) until every category and both bosses were exercised.
                if (runsPlayed >= 8 && required.All(categories.Contains) && bossesSeen.Count == 2) break;
                runsPlayed++;
                var run = StartRun(seed);
                var startingIds = CarriedIds(run.State.Inventory).ToList();
                yield return PlayDepth(run, TransitChoice.DescendDeeper);
                Assert.AreEqual(2, run.State.Depth, $"seed {seed}: descended");
                Assert.AreEqual(0, run.Services.GroundLoot.Count, "Ground loot discarded on depth change.");
                Assert.AreEqual(100, run.Profile.BankedCoins, "Banked untouched mid-run.");
                yield return PlayDepth(run, TransitChoice.ReturnToShelter);

                var summary = run.Expedition.LastSummary;
                Assert.IsNotNull(summary, $"seed {seed}");
                Assert.IsTrue(summary.IsSuccess);
                Assert.IsFalse(run.Expedition.IsExpeditionActive);
                var secured = summary.ExtractedItemIds;
                Assert.AreEqual(secured.Length, secured.Distinct().Count());
                CollectionAssert.IsSubsetOf(startingIds, secured, "Everything carried in comes back.");
                var safe = run.Profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).Concat(run.Profile.SafeLoadout.Backpack.Select(b => b.Item.InstanceId)).ToList();
                CollectionAssert.AreEquivalent(secured, safe);
                Assert.AreEqual(100 + summary.CoinsExtracted, run.Profile.BankedCoins, "Carried Coins banked exactly once.");
                Assert.AreEqual(summary.TransactionId, run.Expedition.Return().TransactionId, "Replay is idempotent.");
                Assert.AreEqual(100 + summary.CoinsExtracted, run.Profile.BankedCoins);
                Assert.Greater(run.Profile.TotalXp, 0, "XP (rooms, enemies, bosses) committed to the profile.");
                Assert.AreEqual(2, run.State.Stats.BossesDefeated);

                bossesSeen.UnionWith(run.BossesSeen);
                elitesSeen.UnionWith(run.ElitesSeen);
                categories.UnionWith(run.CategoriesVisited);
                events.UnionWith(run.EventsResolved);
                report.AppendLine($"- seed {seed}: {run.Rooms} rooms over 2 depths, bosses [{string.Join(",", run.BossesSeen)}], elites [{string.Join(",", run.ElitesSeen)}], events [{string.Join(",", run.EventsResolved)}], secured {secured.Length} items + {summary.CoinsExtracted} coins, XP {run.Profile.TotalXp}: PASS");
                foreach (var line in run.Log) report.AppendLine($"  - {line}");
            }

            CollectionAssert.AreEquivalent(ExpectedBossIds, bossesSeen, "Both of the biome's bosses fought across the seeds.");
            foreach (var type in required)
            {
                Assert.IsTrue(categories.Contains(type), $"Room category {type} reached in a full run.");
            }

            report.AppendLine();
            report.AppendLine($"- Seeds played: {runsPlayed} (2 depths each)");

            report.AppendLine();
            report.AppendLine($"- Bosses fought: {string.Join(", ", bossesSeen.OrderBy(b => b))}");
            report.AppendLine($"- Elites fought in seeded runs: {string.Join(", ", elitesSeen.OrderBy(e => e))} (both Elites are additionally covered by the dedicated Elite fixture below)");
            report.AppendLine($"- Event kinds resolved in seeded runs: {string.Join(", ", events.OrderBy(e => e))}");
            report.AppendLine($"- Room categories reached: {string.Join(", ", categories.OrderBy(c => c))}");
            Directory.CreateDirectory("TestResults");
            File.WriteAllText(ReportPath, report.ToString());
        }

        /// <summary>TASK 146: one seeded solo run that crosses all three biomes (seeded selector), Descend twice, Return once — played once (from the Metro gate class only).</summary>
        [UnityTest]
        public IEnumerator CrossBiomeSoloRun_DescendsThroughAllThreeBiomes_ThenReturns_ConservingEverything()
        {
            if (GetType() != typeof(MetroFullGateTests)) yield break; // shared machinery; the cross-biome run is biome-independent and played once
            var seed = Enumerable.Range(1, 500).First(s => BiomeSelector.Sequence(s, 3).Distinct().Count() == 3);
            var expected = BiomeSelector.Sequence(seed, 3);
            var run = StartRun(seed, crossBiome: true);
            var startingIds = CarriedIds(run.State.Inventory).ToList();
            Assert.AreEqual(expected[0], run.State.Biome);
            yield return PlayDepth(run, TransitChoice.DescendDeeper);
            Assert.AreEqual(expected[1], run.State.Biome, "Depth 2 biome from the seeded selector.");
            yield return PlayDepth(run, TransitChoice.DescendDeeper);
            Assert.AreEqual(expected[2], run.State.Biome, "Depth 3 biome from the seeded selector.");
            yield return PlayDepth(run, TransitChoice.ReturnToShelter);
            CollectionAssert.AreEqual(expected, run.BiomesPlayed, "All three biomes played in the seeded order.");
            var summary = run.Expedition.LastSummary;
            Assert.IsTrue(summary.IsSuccess);
            Assert.AreEqual(3, summary.DepthReached);
            Assert.AreEqual(3, run.State.Stats.BossesDefeated, "One boss per depth.");
            CollectionAssert.AreEqual(expected, summary.Biomes);
            var secured = summary.ExtractedItemIds;
            Assert.AreEqual(secured.Length, secured.Distinct().Count());
            CollectionAssert.IsSubsetOf(startingIds, secured);
            Assert.AreEqual(100 + summary.CoinsExtracted, run.Profile.BankedCoins);
            Assert.Greater(run.Profile.TotalXp, 0);
            File.WriteAllText("TestResults/cross_biome_run.md", "# Cross-biome solo run (TASK 146)" + System.Environment.NewLine + $"- Cross-biome run seed {seed}: {string.Join(" → ", expected)}, 3 bosses, {secured.Length} items secured, {summary.CoinsExtracted} coins banked, XP {run.Profile.TotalXp}: PASS" + System.Environment.NewLine);
        }

        [UnityTest]
        public IEnumerator BothElites_RunAsEliteRoomEngagements_AtRepresentativeDepths()
        {
            foreach (var (elite, depth) in EliteFixtures.Select(f => (_elites.Single(e => e.Id == f.eliteId), f.depth)))
            {
                var definition = _pool.Rooms.First(r => r.SupportsElite);
                var roomObject = Object.Instantiate(definition.Prefab);
                _created.Add(roomObject);
                var root = roomObject.GetComponent<RoomRoot>();
                var runtime = roomObject.AddComponent<RoomRuntime>();
                runtime.Configure(root, 1, depth, 1, isElite: true);
                var engagement = new EliteEngagement(elite, new DefaultEliteSpawner(_stagger), depth, 1, _scaling);
                var xp = new List<int>();
                engagement.EliteDefeated += (_, v) => xp.Add(v);
                runtime.SetEngagement(engagement);
                var player = new GameObject("Player");
                _created.Add(player);
                player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
                player.AddComponent<HealthComponent>().SetMaxHealth(100);
                player.transform.position = root.transform.position + new Vector3(2f, 2f, 0f);

                Assert.IsTrue(runtime.NotifyPlayerEntered(player));
                Assert.AreEqual(DepthScaling.ScaledHealth(elite.BaseHealth, depth, 1, false, _scaling), engagement.Encounter.Elite.Health.MaxHealth, elite.Id);
                yield return null;
                yield return KillUntil(() => runtime.Lifecycle == RoomLifecycleState.Cleared);
                CollectionAssert.AreEqual(new[] { elite.BaseXp }, xp, elite.Id);
                Object.DestroyImmediate(roomObject);
            }
        }

        // ---- Failure conservation ----

        [UnityTest]
        public IEnumerator FailedExpedition_LosesCarriedLootAndCoins_KeepsBankedAndXp()
        {
            var run = StartRun(555, bankedCoins: 250);
            run.State.CarriedWallet.Credit(300, "found");
            run.Expedition.AddXp(120);
            run.State.Inventory.TryAddToBackpack(new ItemInstance("weapon_p9_ranger") { IsAtRisk = true });
            var carried = CarriedIds(run.State.Inventory).ToList();
            yield return null;

            var summary = run.Expedition.Fail();
            Assert.IsFalse(summary.IsSuccess);
            Assert.AreEqual(300, summary.CoinsLost);
            Assert.AreEqual(carried.Count, summary.LostItems.Count, "Every carried instance is lost exactly once.");
            Assert.AreEqual(250, run.Profile.BankedCoins, "Banked untouched.");
            Assert.AreEqual(120, run.Profile.TotalXp, "XP is permanent even on failure.");
            Assert.IsNull(run.Profile.SafeLoadout);
            Assert.AreEqual(summary.TransactionId, run.Expedition.Fail().TransactionId, "Failure replay is idempotent.");
            Assert.AreEqual(250, run.Profile.BankedCoins);
        }

        // ---- Requirement 4: performance smoke at the active caps ----

        [UnityTest]
        public IEnumerator PerformanceSmoke_ActiveEnemyCap_PooledProjectiles_AndPickups_StayResponsive()
        {
            var target = new GameObject("Target");
            _created.Add(target);
            target.AddComponent<BoxCollider2D>().size = Vector2.one;
            target.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            target.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            target.AddComponent<HealthComponent>().SetMaxHealth(100000);
            target.transform.position = new Vector3(50f, 50f, 0f);

            var grunt = _archetypes.Single(a => a.Id == "grunt");
            var shooter = _archetypes.Single(a => a.Id == "shooter");
            var spawner = new DefaultEnemySpawner(_stagger);
            var cap = PartyScaling.ActiveNormalCap(1);
            for (var i = 0; i < cap; i++) spawner.Spawn(i % 2 == 0 ? grunt : shooter, new Vector2(50f + Mathf.Cos(i) * 6f, 50f + Mathf.Sin(i) * 6f), target.transform);

            var poolObject = new GameObject("PerfPool");
            _created.Add(poolObject);
            var pool = poolObject.AddComponent<ProjectilePool>();
            for (var i = 0; i < 60; i++)
            {
                var direction = new Vector2(Mathf.Cos(i * 0.4f), Mathf.Sin(i * 0.4f));
                pool.Spawn(new Vector2(50f, 50f) + direction, new ProjectileSpawnData(1, 8f, 12f, 0f, 0f, direction, poolObject));
            }

            var ground = new GroundLootRegistry();
            var lootSpawner = poolObject.AddComponent<LootSpawner>();
            lootSpawner.SetRegistry(ground);
            for (var i = 0; i < 40; i++)
            {
                var pickup = lootSpawner.CreateItemPickup(new Vector2(40f + i % 8, 40f + i / 8));
                pickup.Hold(new ItemInstance("ammo_light", 5), ItemCategory.Ammo);
            }

            var frames = 120;
            var worst = 0f;
            var total = 0f;
            for (var f = 0; f < frames; f++)
            {
                var start = Time.realtimeSinceStartup;
                yield return null;
                var dt = Time.realtimeSinceStartup - start;
                total += dt;
                if (dt > worst) worst = dt;
            }

            var average = total / frames;
            Assert.AreEqual(cap, Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None).Length);
            Assert.AreEqual(60, pool.SpawnCount);
            Assert.AreEqual(40, ground.Count);
            Assert.Less(average, 0.05f, $"Average frame {average * 1000f:0.0} ms with {cap} enemies, 60 projectiles and 40 pickups (smoke budget 50 ms, no optimisation pass yet).");
            File.WriteAllText(PerfReportPath, $"- Performance smoke: {cap} active enemies + 60 pooled projectiles + 40 pickups over {frames} frames: average {average * 1000f:0.0} ms, worst {worst * 1000f:0.0} ms (test-runner editor frames): PASS\n");
            foreach (var go in ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }
    }
}
