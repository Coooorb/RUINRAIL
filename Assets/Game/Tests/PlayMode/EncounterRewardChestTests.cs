using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Attacks;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Encounter rewards (45 Elite mini-boss, 46/58 boss): every shipped Elite leaves exactly one normal chest and every
    /// shipped boss exactly one Boss Cache — neither exists before the kill, both stand at the room's playable centre,
    /// repeated completion signals never add a second, ordinary rooms get none, and a co-op client builds the same chest
    /// from the host's replicated clear. Real shipped room prefabs, real Elite/boss spawners, the real loot catalog.
    /// </summary>
    public sealed class EncounterRewardChestTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private DungeonRuntimeServices _services;
        private ItemDefinitionRegistry _registry;

        private sealed class FixedBossSpawner : IBossSpawner
        {
            private readonly DefaultBossSpawner _inner;
            private readonly BossDefinition _definition;
            public int Calls;
            public FixedBossSpawner(DefaultBossSpawner inner, BossDefinition definition) { _inner = inner; _definition = definition; }
            public BossEncounter Spawn(in BossSpawnRequest request) { Calls++; return _inner.Spawn(_definition, request.Position, request.Parent); }
        }

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            _registry = _content.BuildRegistry();
            _services = new DungeonRuntimeServices
            {
                LootCatalog = _content.Loot,
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = id => _registry.TryGet(id, out var d) ? d : null,
                Prices = new PriceService(_content.Economy)
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var go in _services.GroundLoot.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var actor in Object.FindObjectsByType<MovesetActorController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.transform.parent != null ? actor.transform.parent.gameObject : actor.gameObject);
            foreach (var p in Object.FindObjectsByType<RuinRail.Gameplay.Combat.Projectiles.Projectile>(FindObjectsSortMode.None)) if (p != null) Object.DestroyImmediate(p.gameObject);
            _created.Clear();
        }

        private DungeonRuntimeContext Context(int depth = 3) =>
            new(41, depth, 1, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling, _content.Elites, new DefaultEliteSpawner(_content.Stagger));

        private (RoomRuntime runtime, RoomRoot root) Instantiate(RoomDefinition definition, Vector2 at, int nodeId)
        {
            var go = Object.Instantiate(definition.Prefab, at, Quaternion.identity);
            _created.Add(go);
            var root = go.GetComponent<RoomRoot>();
            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, 3, 1);
            return (runtime, root);
        }

        private GameObject Player(Vector2 at)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<HealthComponent>().SetMaxHealth(100000);
            player.transform.position = at;
            return player;
        }

        private static int ChestsIn(RoomRuntime room) => room.GetComponentsInChildren<SupplyChest>(true).Length;

        /// <summary>The chest stands on the room's playable centre: a walkable, door-reachable, hazard-free cell nearest the geometric centre.</summary>
        private static float AssertCentred(RoomRoot root, SupplyChest chest, string label)
        {
            var cell = EncounterRewardPlacement.CenterCell(root);
            Assert.IsTrue(cell.HasValue, label + ": the room has a playable centre");
            var expected = SupplyChestPlacement.WorldCenter(root, cell.Value);
            Assert.Less(Vector2.Distance(chest.transform.position, expected), 0.01f, $"{label}: chest at the playable centre {expected}, was {chest.transform.position}");
            var grid = RoomLogicGrid.FromRoom(root);
            Assert.IsTrue(grid.IsWalkable(cell.Value), label + ": on floor, not in a wall or obstacle");
            Assert.IsTrue(SupplyChestPlacement.ReachableFromDoors(grid, root.GetSockets()).Contains(cell.Value), label + ": reachable from the doors");
            Assert.IsFalse(root.GetMarkers(RoomMarkerRole.Hazard).Any(m => m.Rect.Contains(cell.Value)), label + ": not on a hazard");
            return Vector2.Distance(cell.Value, EncounterRewardPlacement.GeometricCenterCell(root));
        }

        private static IEnumerator Until(System.Func<bool> done, string label, float seconds = 10f)
        {
            var deadline = Time.realtimeSinceStartup + seconds;
            while (!done())
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, label);
                yield return null;
            }
        }

        // ---------------------------------------------------------------- Elites (mini-bosses)

        [UnityTest]
        public IEnumerator EveryShippedElite_LeavesExactlyOneNormalChest_OnlyAfterItsDeath_AtTheRoomsPlayableCentre()
        {
            var elites = _content.Elites.Where(e => e != null).OrderBy(e => e.Id).ToList();
            Assert.AreEqual(6, elites.Count, "six shipped Elites");
            var report = new List<string>();
            var x = 0f;
            foreach (var elite in elites)
            {
                var definition = _content.Rooms.First(r => r != null && r.SupportsElite && r.Biome == elite.Biome && r.RoomType == RoomType.Combat && r.Prefab != null);
                var (room, root) = Instantiate(definition, new Vector2(x += 200f, 0f), 7);
                room.Configure(root, 7, 3, 1, isElite: true);
                var engagement = new EliteEngagement(elite, new DefaultEliteSpawner(_content.Stagger), 3, 1, _content.DepthScaling, 41, 7);
                room.SetEngagement(engagement);
                var binding = RoomCategoryComposer.Compose(room, Context(), _services);
                var chestsBefore = ChestsIn(room);
                Assert.IsNull(binding.RewardChest, elite.Id + ": no reward chest before the fight");

                Assert.IsTrue(room.NotifyPlayerEntered(Player((Vector2)root.transform.position + new Vector2(root.Size.x * 0.5f, 2.5f))));
                Assert.IsNotNull(engagement.Encounter?.Elite, elite.Id + ": the Elite spawned");
                yield return null;
                Assert.IsNull(binding.RewardChest, elite.Id + ": none while the Elite lives");
                Assert.AreEqual(chestsBefore, ChestsIn(room));

                engagement.Encounter.Elite.Health.TryApplyDamage(new DamageRequest(10000000));
                yield return Until(() => room.Lifecycle == RoomLifecycleState.Cleared, elite.Id + ": the room clears when the Elite dies");
                var chest = binding.RewardChest;
                Assert.IsNotNull(chest, elite.Id + ": the kill leaves the reward chest");
                Assert.AreEqual(LootSourceKind.SupplyChest, chest.Kind, elite.Id + ": a normal (Supply) chest from the existing loot catalog");
                Assert.AreEqual(chestsBefore + 1, ChestsIn(room), elite.Id + ": exactly one chest was added");
                var offset = AssertCentred(root, chest, elite.Id);

                // Repeated completion signals: a restored state, another spawn request — still the one chest.
                var reward = room.GetComponent<EncounterRewardChest>();
                room.RestoreState(room.State.Clone());
                Assert.AreSame(chest, reward.TrySpawn());
                yield return null;
                Assert.AreEqual(1, reward.SpawnCount);
                Assert.AreEqual(chestsBefore + 1, ChestsIn(room));

                Assert.IsTrue(chest.TryOpen(out var loot), elite.Id + ": opens through the existing chest-loot path");
                Assert.IsFalse(chest.TryOpen(out _), elite.Id + ": pays once");
                CollectionAssert.Contains(room.State.Resolved, RoomCategoryComposer.EliteRewardResolvedId);
                report.Add($"{elite.Id} in {definition.Id}: chest at cell {EncounterRewardPlacement.CenterCell(root)} ({offset:0.0} from the geometric centre), loot {loot.Items.Count} items + {loot.Coins} coins");
            }

            Debug.Log("[PROOF] elite rewards\n" + string.Join("\n", report));
        }

        // ---------------------------------------------------------------- Bosses

        [UnityTest]
        public IEnumerator EveryShippedBoss_LeavesExactlyOneBossCache_OnlyAfterItsDeath_AtTheArenasPlayableCentre()
        {
            var bosses = _content.Bosses.Where(b => b != null).OrderBy(b => b.Id).ToList();
            Assert.AreEqual(6, bosses.Count, "six shipped bosses");
            var report = new List<string>();
            var x = 0f;
            foreach (var boss in bosses)
            {
                var definition = _content.Rooms.First(r => r != null && r.RoomType == RoomType.Boss && r.Biome == boss.Biome && r.Prefab != null);
                var (room, root) = Instantiate(definition, new Vector2(x += 200f, 400f), 9);
                var spawner = new FixedBossSpawner(new DefaultBossSpawner(_content.Bosses, _content.Stagger, new DefaultEnemySpawner(_content.Stagger)), boss);
                _services.BossSpawner = spawner;
                var binding = RoomCategoryComposer.Compose(room, Context(), _services);
                Assert.IsNotNull(binding.Boss?.Boss, boss.Id + ": boss composed");
                Assert.IsNull(binding.BossCache, boss.Id + ": no Boss Cache before the fight");
                Assert.IsNull(binding.RewardChest);
                var chestsBefore = ChestsIn(room);
                Assert.AreEqual(0, chestsBefore, boss.Id + ": the arena holds no chest before the kill");

                Assert.IsTrue(room.NotifyPlayerEntered(Player((Vector2)root.transform.position + new Vector2(root.Size.x * 0.5f, 2.5f))));
                yield return null;
                Assert.IsNull(binding.BossCache, boss.Id + ": none while the boss lives");

                binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
                yield return Until(() => binding.Boss.IsDefeated && room.Lifecycle == RoomLifecycleState.Cleared, boss.Id + ": defeated and cleared");
                var cache = binding.BossCache;
                Assert.IsNotNull(cache, boss.Id + ": the kill leaves the Boss Cache");
                Assert.AreSame(cache, binding.RewardChest);
                Assert.AreEqual(LootSourceKind.BossCache, cache.Kind, boss.Id + ": the existing Boss Cache loot (contents unchanged)");
                Assert.IsFalse(cache.IsLocked, boss.Id + ": openable at once");
                Assert.AreEqual(1, ChestsIn(room), boss.Id + ": exactly one chest, never a second on top of the cache");
                var offset = AssertCentred(root, cache, boss.Id);
                Assert.IsTrue(binding.Transit.IsActivated, boss.Id + ": the Transit Car still activates");

                var reward = room.GetComponent<EncounterRewardChest>();
                room.RestoreState(room.State.Clone());
                reward.TrySpawn();
                yield return null;
                Assert.AreEqual(1, reward.SpawnCount, boss.Id + ": repeated signals never duplicate the cache");
                Assert.AreEqual(1, ChestsIn(room));
                Assert.IsTrue(cache.TryOpen(out var loot));
                Assert.IsFalse(cache.TryOpen(out _));
                CollectionAssert.Contains(room.State.Resolved, "boss_cache");
                report.Add($"{boss.Id} in {definition.Id}: cache at cell {EncounterRewardPlacement.CenterCell(root)} ({offset:0.0} from the geometric centre), loot {loot.Items.Count} items + {loot.Coins} coins");

                // A rebuilt (revisited / reconnect-restored) won arena gets its cache back, opened, exactly once.
                var (again, againRoot) = Instantiate(definition, new Vector2(x, 800f), 9);
                again.RestoreState(room.State.Clone());
                var rebuilt = RoomCategoryComposer.Compose(again, Context(), _services);
                Assert.AreEqual(1, spawner.Calls, boss.Id + ": a beaten boss is never respawned");
                Assert.IsNotNull(rebuilt.BossCache);
                Assert.IsTrue(rebuilt.BossCache.IsOpened);
                Assert.AreEqual(1, ChestsIn(again));
                AssertCentred(againRoot, rebuilt.BossCache, boss.Id + " (rebuilt)");
            }

            Debug.Log("[PROOF] boss rewards\n" + string.Join("\n", report));
        }

        // ---------------------------------------------------------------- ordinary rooms

        [UnityTest]
        public IEnumerator OrdinaryCombatRoom_EnemyDeaths_NeverSpawnARewardChest()
        {
            var definition = _content.Rooms.First(r => r != null && r.RoomType == RoomType.Combat && r.Prefab != null && r.Biome == Biome.RuinedMetro);
            var (room, root) = Instantiate(definition, new Vector2(-400f, 0f), 5);
            var context = Context();
            var plan = EncounterDirector.Compose(new EncounterContext(41, 3, 1, definition.Biome, 5, definition.Tags, root.GetMarkers(RoomMarkerRole.EnemySpawn).Count), context.Archetypes);
            room.SetEncounter(plan, context.Spawner);
            var binding = RoomCategoryComposer.Compose(room, context, _services);
            var chestsBefore = ChestsIn(room);
            Assert.IsNull(room.GetComponent<EncounterRewardChest>(), "an ordinary combat room has no encounter reward");

            Assert.IsTrue(room.NotifyPlayerEntered(Player((Vector2)root.transform.position + new Vector2(root.Size.x * 0.5f, 2.5f))));
            var deadline = Time.realtimeSinceStartup + 30f;
            while (room.Lifecycle != RoomLifecycleState.Cleared)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, "the ordinary encounter resolved");
                foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy.IsAlive && enemy.transform.IsChildOf(room.transform.root)) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy.IsAlive) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                yield return new WaitForSeconds(0.02f);
            }

            Assert.Greater(room.State.EnemiesDefeated, 0, "enemies actually died");
            Assert.IsNull(binding.RewardChest, "ordinary kills never earn a reward chest");
            Assert.AreEqual(chestsBefore, ChestsIn(room));
        }

        // ---------------------------------------------------------------- co-op client + real layouts

        [UnityTest]
        public IEnumerator CoopClient_MirrorsTheEliteRoom_AndBuildsTheSameChestFromTheHostsClear_Once()
        {
            // A generated depth that has an Elite room, composed as the host does and as a client does.
            var pools = BiomeRoomPools.Build(_content.Rooms);
            DungeonGenerationResult generation = null;
            var seed = 0;
            for (var s = 1; s < 200 && generation == null; s++)
            {
                var biome = BiomeSelector.SelectFirst(s);
                var candidate = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), pools.PoolFor(biome), s, 1);
                if (candidate.Success && candidate.Graph.Nodes.Any(n => n.IsElite)) { generation = candidate; seed = s; }
            }

            Assert.IsNotNull(generation, "a seed with an Elite room on D1");
            var eliteNode = generation.Graph.Nodes.First(n => n.IsElite).Id;

            Dictionary<int, RoomRuntime> Compose(bool authoritative, Vector2 at)
            {
                var dungeon = new GameObject(authoritative ? "HostDungeon" : "ClientDungeon");
                _created.Add(dungeon);
                dungeon.transform.position = at;
                var rooms = DungeonLayoutInstantiator.Instantiate(generation.Layout, dungeon.transform);
                var context = new DungeonRuntimeContext(seed, 1, 2, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling, _content.Elites,
                    authoritative ? new DefaultEliteSpawner(_content.Stagger) : null) { IsAuthoritative = authoritative };
                var runtimes = DungeonRoomRuntimeComposer.Attach(generation.Layout, rooms, context, _services);
                if (!authoritative) foreach (var r in runtimes.Values) r.SetAuthoritative(false);
                return runtimes;
            }

            var host = Compose(true, Vector2.zero)[eliteNode];
            var client = Compose(false, new Vector2(0f, 3000f))[eliteNode];
            Assert.IsTrue(host.State.IsElite && host.Engagement is EliteEngagement, "the host runs the Elite");
            Assert.IsTrue(client.State.IsElite, "the client knows it is an Elite room (same seeded graph)");
            Assert.IsNull(client.Engagement, "the client runs no Elite of its own");
            Assert.IsNull(client.GetComponent<RoomContentBinding>().RewardChest);

            // Host: the kill.
            var hostRoot = host.Root;
            Assert.IsTrue(host.NotifyPlayerEntered(Player((Vector2)hostRoot.transform.position + new Vector2(hostRoot.Size.x * 0.5f, 2.5f))));
            var engagement = (EliteEngagement)host.Engagement;
            engagement.Encounter.Elite.Health.TryApplyDamage(new DamageRequest(10000000));
            yield return Until(() => host.Lifecycle == RoomLifecycleState.Cleared, "host room cleared");
            var hostChest = host.GetComponent<RoomContentBinding>().RewardChest;
            Assert.IsNotNull(hostChest);

            // Client: the host's replicated states (Active, then Cleared) — the same chest at the same room cell, once.
            var active = host.State.Clone();
            active.State = RoomLifecycleState.Active;
            client.RestoreState(active);
            Assert.IsNull(client.GetComponent<RoomContentBinding>().RewardChest, "not before the host's clear");
            client.RestoreState(host.State.Clone());
            var clientChest = client.GetComponent<RoomContentBinding>().RewardChest;
            Assert.IsNotNull(clientChest, "the host's clear builds the client's chest");
            Assert.AreEqual(hostChest.transform.position - hostRoot.transform.position, clientChest.transform.position - client.Root.transform.position, "same room cell on both peers");
            Assert.AreEqual(hostChest.Kind, clientChest.Kind);
            client.RestoreState(host.State.Clone());
            client.RestoreState(host.State.Clone());
            Assert.AreEqual(1, client.GetComponent<EncounterRewardChest>().SpawnCount, "re-delivered states never add a second chest");

            // A client that joins after the clear (no Active step) still gets it.
            var late = Compose(false, new Vector2(0f, 6000f))[eliteNode];
            late.RestoreState(host.State.Clone());
            Assert.IsNotNull(late.GetComponent<RoomContentBinding>().RewardChest);
        }

        [Test]
        public void EveryShippedBossAndEliteRoom_HasAReachableHazardFreePlayableCentre()
        {
            var lines = new List<string>();
            var x = 0f;
            foreach (var definition in _content.Rooms.Where(r => r != null && r.Prefab != null && (r.RoomType == RoomType.Boss || r.SupportsElite)).OrderBy(r => r.Id))
            {
                var go = Object.Instantiate(definition.Prefab, new Vector2(x += 150f, -2000f), Quaternion.identity);
                _created.Add(go);
                var root = go.GetComponent<RoomRoot>();
                var cell = EncounterRewardPlacement.CenterCell(root);
                Assert.IsTrue(cell.HasValue, definition.Id + ": a playable centre exists");
                var grid = RoomLogicGrid.FromRoom(root);
                Assert.IsTrue(grid.IsWalkable(cell.Value) && SupplyChestPlacement.ReachableFromDoors(grid, root.GetSockets()).Contains(cell.Value), definition.Id);
                Assert.IsFalse(root.GetMarkers(RoomMarkerRole.Hazard).Any(m => m.Rect.Contains(cell.Value)), definition.Id + ": not on a hazard");
                Assert.IsFalse(root.GetMarkers(RoomMarkerRole.InteractableSpawn).Any(m => m.Rect.Contains(cell.Value)), definition.Id + ": not on the Transit Car");
                lines.Add($"{definition.Id} ({definition.RoomType}{(definition.SupportsElite ? ", elite" : string.Empty)}) {root.Size.x}x{root.Size.y}: cell {cell.Value}, geometric {EncounterRewardPlacement.GeometricCenterCell(root)}, offset {Vector2.Distance(cell.Value, EncounterRewardPlacement.GeometricCenterCell(root)):0.0}");
            }

            Assert.Greater(lines.Count, 0);
            Debug.Log("[PROOF] reward placement\n" + string.Join("\n", lines));
        }
    }

    /// <summary>
    /// The real encounter flow in a live run (boot → Shelter → generated dungeon): the Elite room's chest appears only
    /// after the Elite dies, at the room's playable centre, and opens through the player's own interaction; the boss
    /// arena holds no cache until the boss dies and then exactly one, centred. Captures: TestResults/RegressionProof/reward_*.png.
    /// </summary>
    public sealed class EncounterRewardLiveRunTests
    {
        private string _saveDir;
        private GameApp _app;

        [SetUp]
        public void SetUp()
        {
            _saveDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ruinrail_reward_" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(_saveDir);
            System.IO.Directory.CreateDirectory("TestResults/RegressionProof");
            RuinRail.Core.Input.GameplayInputGate.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_app != null) Object.DestroyImmediate(_app.gameObject);
            foreach (var scene in Object.FindObjectsByType<ExpeditionScene>(FindObjectsSortMode.None)) Object.DestroyImmediate(scene.gameObject);
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root == null || root.name.IndexOf("tests runner", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (root.GetComponents<Component>().Any(c => c != null && (c.GetType().Namespace ?? string.Empty).StartsWith("UnityEngine.TestTools"))) continue;
                Object.DestroyImmediate(root);
            }

            Time.timeScale = 1f;
            RuinRail.Networking.NetworkPlayerObject.VisualComposer = null;
            try { System.IO.Directory.Delete(_saveDir, true); } catch { /* best effort */ }
        }

        private IEnumerator WaitComposed(string scene)
        {
            var deadline = Time.realtimeSinceStartup + 30f;
            while (_app.ComposedScene != scene)
            {
                Assert.Less(Time.realtimeSinceStartup, deadline, $"'{scene}' was not composed in time");
                yield return null;
            }
        }

        private static IEnumerator Teleport(ExpeditionScene run, Vector2 position)
        {
            var body = run.Rig.Player.GetComponent<Rigidbody2D>();
            run.Rig.Player.transform.position = position;
            body.position = position;
            for (var i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            for (var i = 0; i < 12; i++) yield return null;
        }

        /// <summary>Capture framing only: the follow camera may still be converging after a teleport.</summary>
        private static void LookAt(ExpeditionScene run, Vector2 world)
        {
            var cam = run.Camera.Camera.transform;
            cam.position = new Vector3(world.x, world.y, cam.position.z);
        }

        [UnityTest]
        public IEnumerator LiveRun_EliteAndBoss_EachLeaveOneCentredChest_OnlyAfterTheKill()
        {
            var content = GameContentCatalog.Load();
            var pools = BiomeRoomPools.Build(content.Rooms);
            var seed = 0;
            for (var s = 1; s < 300 && seed == 0; s++)
            {
                var generation = DungeonGenerationPipeline.Generate(new DungeonGraphGenerator(DungeonGraphRules.CreateDefault()), pools.PoolFor(BiomeSelector.SelectFirst(s)), s, 1);
                if (generation.Success && generation.Graph.Nodes.Any(n => n.IsElite)) seed = s;
            }

            Assert.Greater(seed, 0, "a seed whose first depth has an Elite room");
            _app = GameApp.Ensure(content, _saveDir);
            _app.SetRunSeedOverride(seed);
            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneNames.MainMenu);
            yield return WaitComposed(SceneNames.MainMenu);
            _app.Menu.Play();
            yield return WaitComposed(SceneNames.Base);
            var hub = Object.FindFirstObjectByType<BaseHubScreen>();
            hub.Onboarding.SubmitDisplayName("Reward Hunter");
            hub.Onboarding.AcknowledgeStarterKit();
            Assert.IsTrue(hub.Hub.Multiplayer.SetReady(true));
            hub.Hub.Open(RuinRail.UI.Base.BaseStation.Transit);
            Assert.IsTrue(hub.Hub.Transit.StartExpedition());
            yield return WaitComposed(SceneNames.Dungeon);
            for (var i = 0; i < 12; i++) yield return null;

            var run = Object.FindFirstObjectByType<ExpeditionScene>();
            var health = run.Rig.Player.GetComponent<HealthComponent>();
            var eliteNode = run.Generation.Graph.Nodes.First(n => n.IsElite).Id;
            var eliteRoom = run.Rooms[eliteNode];
            var eliteBinding = eliteRoom.GetComponent<RoomContentBinding>();
            var engagement = eliteRoom.Engagement as EliteEngagement;
            Assert.IsNotNull(engagement, "the live run composed the Elite encounter");
            Assert.IsNull(eliteBinding.RewardChest, "no reward chest before the fight");
            var chestsBefore = eliteRoom.GetComponentsInChildren<SupplyChest>(true).Length;
            var centre = EncounterRewardPlacement.WorldCenter(eliteRoom.Root).Value;

            yield return Teleport(run, centre);
            Assert.AreEqual(RoomLifecycleState.Active, eliteRoom.Lifecycle, "entering starts the Elite fight");
            Assert.IsNotNull(engagement.Encounter?.Elite, "the Elite is in the room");
            Assert.IsNull(eliteBinding.RewardChest, "none while it lives");
            LookAt(run, centre);
            LiveDungeonCapture.Capture("TestResults/RegressionProof", "reward_01_elite_alive_no_chest", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
            health.Heal(100000);
            engagement.Encounter.Elite.Health.TryApplyDamage(new DamageRequest(100000000));
            var deadline = Time.realtimeSinceStartup + 10f;
            while (eliteRoom.Lifecycle != RoomLifecycleState.Cleared) { Assert.Less(Time.realtimeSinceStartup, deadline, "the Elite's room cleared"); yield return null; }
            yield return null;
            var chest = eliteBinding.RewardChest;
            Assert.IsNotNull(chest, "the kill left the reward chest");
            Assert.AreEqual(LootSourceKind.SupplyChest, chest.Kind);
            Assert.AreEqual(chestsBefore + 1, eliteRoom.GetComponentsInChildren<SupplyChest>(true).Length, "exactly one chest added");
            Assert.Less(Vector2.Distance(chest.transform.position, centre), 0.01f, "at the room's playable centre");
            Assert.IsTrue(chest.Visual != null && chest.Visual.IsVisible, "drawn as the normal chest");
            LookAt(run, chest.transform.position);
            LiveDungeonCapture.Capture("TestResults/RegressionProof", "reward_02_elite_dead_chest_centre", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);

            // The player opens it by the ordinary interaction; its loot lands on the tracked ground.
            var groundBefore = run.GroundLoot.Tracked.Count(g => g != null);
            var interactor = run.Rig.Player.GetComponent<RuinRail.Gameplay.Player.PlayerInteractor>();
            deadline = Time.realtimeSinceStartup + 3f;
            while (!chest.IsOpened && Time.realtimeSinceStartup < deadline) { interactor.TryInteract(); yield return null; }
            Assert.IsTrue(chest.IsOpened, "opened by the player's interaction");
            Assert.Greater(run.GroundLoot.Tracked.Count(g => g != null), groundBefore, "the existing chest loot dropped");
            Assert.IsFalse(chest.CanInteract(run.Rig.Player), "opens once");

            // Boss arena: nothing before the kill, one centred Boss Cache after.
            var bossRoom = run.Rooms[run.Generation.Graph.BossId];
            var bossBinding = bossRoom.GetComponent<RoomContentBinding>();
            Assert.IsNull(bossBinding.BossCache, "no Boss Cache before the boss fight");
            Assert.AreEqual(0, bossRoom.GetComponentsInChildren<SupplyChest>(true).Length);
            var bossCentre = EncounterRewardPlacement.WorldCenter(bossRoom.Root).Value;
            yield return Teleport(run, bossCentre);
            BossIntroSequence.Current?.Finish();
            Assert.IsNull(bossBinding.BossCache, "none while the boss lives");
            health.Heal(100000);
            bossBinding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(100000000));
            deadline = Time.realtimeSinceStartup + 10f;
            while (bossBinding.BossCache == null) { Assert.Less(Time.realtimeSinceStartup, deadline, "the boss's death spawned the cache"); yield return null; }
            yield return null;
            Assert.AreEqual(1, bossRoom.GetComponentsInChildren<SupplyChest>(true).Length, "exactly one chest in the arena");
            Assert.Less(Vector2.Distance(bossBinding.BossCache.transform.position, bossCentre), 0.01f, "at the arena's playable centre");
            Assert.IsTrue(bossBinding.BossCache.CanInteract(run.Rig.Player) && bossBinding.Transit.IsActivated);
            LookAt(run, bossBinding.BossCache.transform.position);
            LiveDungeonCapture.Capture("TestResults/RegressionProof", "reward_03_boss_dead_cache_centre", run.Camera.Camera, run.Camera.Config.PixelsPerUnit, includeUi: true);
            Debug.Log($"[PROOF] live seed {seed}: elite {engagement.Definition.Id} chest at {chest.transform.position} (room centre {centre}); boss cache at {bossBinding.BossCache.transform.position} (arena centre {bossCentre})");
        }
    }
}
