using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
using RuinRail.Gameplay.Enemies.Encounters;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 081: Loot/Treasure/Merchant/Event/Medical/Boss rooms bind their category behaviour from metadata, once.</summary>
    public class SpecialRoomRuntimeTests
    {
        private readonly List<Object> _created = new();
        private DungeonRuntimeServices _services;
        private DungeonRuntimeContext _context;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        private sealed class FakeBossSpawner : IBossSpawner
        {
            private readonly SpecialRoomRuntimeTests _owner;
            public int Calls;
            public FakeBossSpawner(SpecialRoomRuntimeTests owner) { _owner = owner; }

            public BossEncounter Spawn(in BossSpawnRequest request)
            {
                Calls++;
                var root = new GameObject("BossEncounter");
                root.transform.SetParent(request.Parent, false);
                var encounter = root.AddComponent<BossEncounter>();
                var go = new GameObject("Boss");
                go.transform.SetParent(root.transform, false);
                go.transform.position = request.Position;
                go.AddComponent<CircleCollider2D>().radius = 0.7f;
                go.AddComponent<Rigidbody2D>().gravityScale = 0f;
                go.AddComponent<HealthComponent>();
                var boss = go.AddComponent<BossController>();
                var definition = ScriptableObject.CreateInstance<BossDefinition>();
                _owner._created.Add(definition);
                Set(definition, "_id", "boss_test");
                Set(definition, "_baseHealth", 300);
                Set(definition, "_baseXp", 650);
                boss.SetDefinition(definition);
                encounter.Bind(boss);
                return encounter;
            }
        }

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            var archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _services = new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = id => _registry.TryGet(id, out var d) ? d : null,
                ItemCatalog = catalog,
                Prices = new PriceService(AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset")),
                MerchantConfig = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset"),
                CarriedWallet = new CoinWallet(CoinDomain.Carried, 5000),
                EventConfig = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset"),
                BossSpawner = new FakeBossSpawner(this)
            };
            _context = new DungeonRuntimeContext(runSeed: 99, depth: 4, partySize: 1, archetypes, new DefaultEnemySpawner());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var go in _services.GroundLoot.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private RoomRuntime CreateRoom(RoomType type, int nodeId, string[] tags = null, string objectName = null, Vector2 offset = default, RoomRuntimeState restore = null)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", $"room_{type}".ToLowerInvariant());
            Set(definition, "_roomType", type);
            Set(definition, "_tags", tags ?? new string[0]);
            var go = new GameObject(objectName ?? $"Anything_{Random.Range(0, 99999)}");
            _created.Add(go);
            go.transform.position = offset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            AddMarker(go, RoomMarkerRole.ChestSpawn, new Vector2Int(4, 6));
            AddMarker(go, RoomMarkerRole.ChestSpawn, new Vector2Int(10, 6));
            AddMarker(go, RoomMarkerRole.MerchantAnchor, new Vector2Int(8, 8));
            AddMarker(go, RoomMarkerRole.EventAnchor, new Vector2Int(8, 4));
            AddMarker(go, RoomMarkerRole.BossAnchor, new Vector2Int(8, 9));
            AddMarker(go, RoomMarkerRole.InteractableSpawn, new Vector2Int(14, 10));
            AddMarker(go, RoomMarkerRole.EnemySpawn, new Vector2Int(13, 9));
            AddMarker(go, RoomMarkerRole.EnemySpawn, new Vector2Int(13, 2));
            var socket = new GameObject("Door_N").AddComponent<DoorSocket>();
            socket.transform.SetParent(go.transform, false);
            socket.Configure(DoorDirection.North, new Vector2Int(7, 11));
            socket.SnapToGrid();

            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, _context.Depth, _context.PartySize);
            runtime.SetSpawner(_context.Spawner);
            if (restore != null) runtime.RestoreState(restore);
            return runtime;
        }

        private static void AddMarker(GameObject room, RoomMarkerRole role, Vector2Int cell)
        {
            var marker = new GameObject(role.ToString()).AddComponent<RoomMarker>();
            marker.transform.SetParent(room.transform, false);
            marker.Configure(role, cell);
            marker.SnapToGrid();
        }

        private (GameObject player, PlayerLootReceiver receiver, PlayerInventory inventory) CreatePlayer(Vector2 position)
        {
            var player = new GameObject("Player");
            _created.Add(player);
            player.transform.position = position;
            player.AddComponent<CircleCollider2D>().isTrigger = true;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            receiver.SetWallet(_services.CarriedWallet);
            return (player, receiver, inventory);
        }

        private static string Signature(LootResult loot) => $"{loot.Coins}|" + string.Join(",", loot.Items.Select(i => $"{i.DefinitionId}:{i.Rarity}:{i.Quantity}"));

        // ---- Loot / Treasure ----

        [UnityTest]
        public IEnumerator LootRoom_BindsOneTimeChests_NoLock_AndARevisitRestoresOpenedChests()
        {
            var room = CreateRoom(RoomType.Loot, 11);
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            var (player, _, _) = CreatePlayer(Vector2.one);

            Assert.AreEqual(2, binding.Chests.Count);
            Assert.IsTrue(binding.Chests.All(c => c.Kind == LootSourceKind.EquipmentChest));
            Assert.IsEmpty(binding.Skipped);
            room.NotifyPlayerEntered(player);
            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle, "Little or no combat: no lock, visited = cleared.");
            Assert.IsFalse(room.DoorsLocked);

            Assert.IsTrue(binding.Chests[0].TryOpen(out var first));
            Assert.IsFalse(binding.Chests[0].TryOpen(out _));
            CollectionAssert.Contains(room.State.Resolved, "chest:0");
            yield return null;

            // Revisit: a fresh runtime restored from the persisted state never regenerates the opened chest.
            var revisit = CreateRoom(RoomType.Loot, 11, offset: new Vector2(50f, 0f), restore: room.State.Clone());
            var again = RoomCategoryComposer.Compose(revisit, _context, _services);
            Assert.IsTrue(again.Chests[0].IsOpened);
            Assert.IsFalse(again.Chests[0].CanInteract(player));
            Assert.IsFalse(again.Chests[0].TryOpen(out _));
            Assert.IsFalse(again.Chests[1].IsOpened);
            Assert.IsTrue(again.Chests[1].TryOpen(out var second));
            Assert.IsTrue(binding.Chests[1].TryOpen(out var original));
            Assert.AreEqual(Signature(original), Signature(second), "Same seed/depth/room/chest index = same loot.");
            Assert.AreNotEqual(Signature(first), Signature(second));
        }

        [Test]
        public void TreasureRoom_UsesTheTreasureChestSource()
        {
            var room = CreateRoom(RoomType.Treasure, 12);
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            Assert.AreEqual(2, binding.Chests.Count);
            Assert.IsTrue(binding.Chests.All(c => c.Kind == LootSourceKind.TreasureChest && c.Quality == LootQuality.Improved));
        }

        // ---- Merchant ----

        [Test]
        public void MerchantRoom_BindsTheDepthsSingleStock_AndRevisitKeepsSoldState()
        {
            var room = CreateRoom(RoomType.Merchant, 13);
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            var (player, receiver, inventory) = CreatePlayer(Vector2.one);
            Assert.IsNotNull(binding.Merchant);
            Assert.IsTrue(binding.Merchant.Interact(player));
            Assert.AreEqual(1, binding.Merchant.OpenCount);
            var merchant = binding.Merchant.Merchant;
            var stock = merchant.StockSignature;
            Assert.AreEqual(5, merchant.Offers.Count);
            var offer = merchant.Offers[3];
            Assert.AreEqual(RuinRail.Gameplay.Base.TradeError.None, merchant.Buy(3, receiver.Backpack));
            Assert.IsTrue(offer.IsSold);

            var revisit = CreateRoom(RoomType.Merchant, 13, offset: new Vector2(50f, 0f));
            var again = RoomCategoryComposer.Compose(revisit, _context, _services);
            Assert.AreEqual(stock, again.Merchant.Merchant.StockSignature, "One stable stock per depth.");
            Assert.IsTrue(again.Merchant.Merchant.Offers[3].IsSold, "Sold state persists for the depth.");
            Assert.AreEqual(RuinRail.Gameplay.Base.TradeError.AlreadySold, again.Merchant.Merchant.Buy(3, receiver.Backpack));
            Assert.AreEqual(1, inventory.BackpackSlots.Count(s => s != null));
        }

        // ---- Event / Medical ----

        [UnityTest]
        public IEnumerator EventRoom_BindsExactlyOneAuthoredEvent_TagPinsTheKind_AndRevisitRestoresResolution()
        {
            var room = CreateRoom(RoomType.Event, 14, new[] { "event:locked_vault" });
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            var (player, _, _) = CreatePlayer(Vector2.one);
            Assert.IsInstanceOf<LockedVaultEvent>(binding.EventInstance);
            Assert.AreEqual(1, room.GetComponentsInChildren<DungeonEventInteractable>().Length, "Exactly one event.");

            var before = _services.CarriedWallet.Balance;
            Assert.IsTrue(binding.Event.Interact(player));
            Assert.AreEqual(DungeonEventOutcome.Success, binding.Event.LastResult.Outcome);
            Assert.AreEqual(before - 325, _services.CarriedWallet.Balance, "D4 vault: 250 + 25 x 3.");
            CollectionAssert.Contains(room.State.Resolved, "event:LockedVault");
            Assert.Greater(_services.GroundLoot.Count, 0, "Reward delivered into the shared world.");
            yield return null;

            var revisit = CreateRoom(RoomType.Event, 14, new[] { "event:locked_vault" }, offset: new Vector2(50f, 0f), restore: room.State.Clone());
            var again = RoomCategoryComposer.Compose(revisit, _context, _services);
            Assert.AreEqual(DungeonEventPhase.Completed, again.EventInstance.Phase);
            Assert.IsFalse(again.Event.CanInteract(player));
            Assert.IsFalse(again.Event.Interact(player));
            Assert.AreEqual(before - 325, _services.CarriedWallet.Balance, "No second payout/charge on revisit.");
        }

        [Test]
        public void EventRoom_WithoutTag_PicksAnApprovedKindBySeed_Stably()
        {
            var a = RoomCategoryComposer.Compose(CreateRoom(RoomType.Event, 15), _context, _services);
            var b = RoomCategoryComposer.Compose(CreateRoom(RoomType.Event, 15, offset: new Vector2(50f, 0f)), _context, _services);
            var c = RoomCategoryComposer.Compose(CreateRoom(RoomType.Event, 16, offset: new Vector2(100f, 0f)), _context, _services);
            CollectionAssert.Contains(RoomCategoryComposer.RandomEventKinds, a.EventInstance.Kind);
            Assert.AreEqual(a.EventInstance.Kind, b.EventInstance.Kind);
            Assert.AreNotEqual(DungeonEventKind.MedicalStation, a.EventInstance.Kind);
            Assert.IsNotNull(c.EventInstance);
        }

        [Test]
        public void MedicalRoom_BindsTheMedicalStation()
        {
            var binding = RoomCategoryComposer.Compose(CreateRoom(RoomType.MedicalRecovery, 17), _context, _services);
            Assert.IsInstanceOf<MedicalStationEvent>(binding.EventInstance);
            Assert.AreEqual(195, ((MedicalStationEvent)binding.EventInstance).HealCost, "D4 heal: 150 + 15 x 3.");
        }

        [UnityTest]
        public IEnumerator CursedChestInEventRoom_LocksDoors_RunsTheEncounterInRoom_AndUnlocksOnClear()
        {
            var room = CreateRoom(RoomType.Event, 18, new[] { "event:cursed_chest" });
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            var (player, _, _) = CreatePlayer(new Vector2(2f, 2f));
            room.NotifyPlayerEntered(player);
            Assert.IsFalse(room.DoorsLocked);

            Assert.IsTrue(binding.Event.Interact(player));
            var cursed = (CursedChestEvent)binding.EventInstance;
            Assert.IsTrue(cursed.DoorsLocked);
            Assert.IsTrue(room.DoorsLocked, "Cursed Chest locks the room doors.");
            yield return null;
            var enemies = Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            Assert.Greater(enemies.Length, 0, "The harder encounter spawned in the room.");
            Assert.AreEqual(Mathf.Min(cursed.Plan.TotalCount, cursed.Plan.ActiveCap), room.State.EnemiesSpawned, "First wave spawned through the room's spawner.");

            var guard = 0;
            while (cursed.Phase == DungeonEventPhase.InProgress && guard++ < 50)
            {
                foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                {
                    if (enemy.IsAlive) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                }

                yield return null;
            }

            Assert.AreEqual(DungeonEventPhase.Completed, cursed.Phase);
            Assert.IsFalse(room.DoorsLocked, "Doors unlock once the cursed encounter is cleared.");
            Assert.Greater(_services.GroundLoot.Count, 0);
        }

        // ---- Boss ----

        [UnityTest]
        public IEnumerator BossRoom_LocksOnEntry_StartsOnce_AndDefeatOpensCacheAndTransitOnce()
        {
            var room = CreateRoom(RoomType.Boss, 19);
            var binding = RoomCategoryComposer.Compose(room, _context, _services);
            var spawner = (FakeBossSpawner)_services.BossSpawner;
            var (player, _, _) = CreatePlayer(new Vector2(2f, 2f));
            Assert.IsNotNull(binding.Boss);
            Assert.IsNull(binding.BossCache, "46: the boss's death spawns the Boss Cache; it does not exist before");
            Assert.IsNotNull(binding.Transit);
            Assert.IsFalse(binding.Transit.IsActivated);
            var cleared = 0;
            room.Cleared += (_, _) => cleared++;
            var transitActivations = 0;
            binding.Transit.Activated += _ => transitActivations++;

            Assert.IsTrue(room.NotifyPlayerEntered(player));
            Assert.AreEqual(RoomLifecycleState.Active, room.Lifecycle);
            Assert.IsTrue(room.DoorsLocked);
            // The room introduction holds the boss back (no target, so no attack) until it ends or is skipped.
            var engagement = (BossEngagement)room.Engagement;
            Assert.IsTrue(engagement.IsHoldingForIntro);
            Assert.IsNull(binding.Boss.Boss.Target, "No target during the introduction.");
            engagement.EndIntro();
            Assert.AreSame(player.transform, binding.Boss.Boss.Target, "The boss engages the entering player once the introduction ends.");
            yield return null;
            Assert.IsTrue(binding.Boss.IsStarted);
            Assert.IsFalse(room.NotifyPlayerEntered(player));
            Assert.AreEqual(1, spawner.Calls);

            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.IsTrue(binding.Boss.IsDefeated);
            Assert.AreEqual(RoomLifecycleState.Cleared, room.Lifecycle);
            Assert.IsFalse(room.DoorsLocked);
            Assert.AreEqual(1, cleared);
            Assert.IsTrue(binding.BossCache != null && !binding.BossCache.IsLocked, "The defeat spawns one openable Boss Cache.");
            Assert.AreEqual(1, room.GetComponentsInChildren<SupplyChest>().Length, "exactly one chest in the arena");
            Assert.IsTrue(binding.Transit.IsActivated, "Transit activates on defeat.");
            Assert.AreEqual(1, transitActivations);
            CollectionAssert.Contains(room.State.Resolved, "boss");
            Assert.IsTrue(binding.BossCache.TryOpen(out _));
            CollectionAssert.Contains(room.State.Resolved, "boss_cache");

            // Revisit: no second boss, cache stays open/opened, transit stays active.
            var revisit = CreateRoom(RoomType.Boss, 19, offset: new Vector2(60f, 0f), restore: room.State.Clone());
            var again = RoomCategoryComposer.Compose(revisit, _context, _services);
            Assert.AreEqual(1, spawner.Calls, "A beaten boss is never respawned.");
            Assert.IsNull(again.Boss);
            Assert.IsFalse(again.BossCache.IsLocked);
            Assert.IsTrue(again.BossCache.IsOpened);
            Assert.IsTrue(again.Transit.IsActivated);
            Assert.IsFalse(revisit.NotifyPlayerEntered(player));
            Assert.AreEqual(RoomLifecycleState.Cleared, revisit.Lifecycle);
        }

        // ---- Requirement 4: metadata composition, not names ----

        [Test]
        public void Composition_IsKeyedByRoomTypeMetadata_NotObjectNames()
        {
            var a = RoomCategoryComposer.Compose(CreateRoom(RoomType.Merchant, 20, objectName: "Room_Boss_Arena"), _context, _services);
            var b = RoomCategoryComposer.Compose(CreateRoom(RoomType.Merchant, 20, objectName: "totally_unrelated", offset: new Vector2(50f, 0f)), _context, _services);
            Assert.IsNotNull(a.Merchant);
            Assert.IsNotNull(b.Merchant);
            Assert.IsNull(a.Boss);
            Assert.AreEqual(a.Merchant.Merchant.StockSignature, b.Merchant.Merchant.StockSignature);

            var start = RoomCategoryComposer.Compose(CreateRoom(RoomType.Start, 21, objectName: "Merchant", offset: new Vector2(100f, 0f)), _context, _services);
            Assert.IsNull(start.Merchant);
            Assert.AreEqual(0, start.Chests.Count);
            Assert.IsNull(start.EventInstance);
        }
    }
}
