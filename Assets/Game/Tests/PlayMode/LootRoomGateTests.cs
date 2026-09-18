using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Enemies.Bosses;
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
    /// TASK 082 gate: one solo expedition through combat clear → loot pickup → merchant purchase → paid event → boss
    /// cache → transit (descend, then return), asserting item/coin conservation and one-time state at every step.
    /// </summary>
    public class LootRoomGateTests
    {
        private readonly List<Object> _created = new();
        private List<ItemDefinition> _catalog;
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private List<EnemyDefinition> _archetypes;
        private DungeonRuntimeServices _services;
        private ExpeditionService _expedition;
        private PlayerProfile _profile;
        private int _bossSpawns;

        private sealed class GateBossSpawner : IBossSpawner
        {
            private readonly LootRoomGateTests _owner;
            public GateBossSpawner(LootRoomGateTests owner) { _owner = owner; }
            public BossEncounter Spawn(in BossSpawnRequest request)
            {
                _owner._bossSpawns++;
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
                Set(definition, "_id", "boss_gate");
                Set(definition, "_baseHealth", 200);
                Set(definition, "_baseXp", 650);
                boss.SetDefinition(definition);
                encounter.Bind(boss);
                return encounter;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(_catalog);
            Assert.IsEmpty(_registry.Problems);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _archetypes = AssetDatabase.FindAssets("t:EnemyDefinition", new[] { "Assets/Game/ScriptableObjects/Enemies" })
                .Select(g => AssetDatabase.LoadAssetAtPath<EnemyDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            var ammoByType = _catalog.OfType<AmmoItemDefinition>().GroupBy(a => a.AmmoType).ToDictionary(g => g.Key, g => g.First());
            _expedition = new ExpeditionService(id => _registry.TryGet(id, out var d) ? d : null, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            _profile = new PlayerProfile
            {
                BankedCoins = 100,
                TotalXp = 0,
                SafeLoadout = new InventorySnapshot
                {
                    Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                    Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
                }
            };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None)) if (enemy != null) Object.DestroyImmediate(enemy.gameObject);
            foreach (var actor in Object.FindObjectsByType<BossController>(FindObjectsSortMode.None)) if (actor != null) Object.DestroyImmediate(actor.gameObject);
            if (_services?.GroundLoot != null) foreach (var go in _services.GroundLoot.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
            _created.Clear();
        }

        private static void Set(object target, string field, object value)
        {
            var type = target.GetType();
            FieldInfo info = null;
            while (type != null && info == null) { info = type.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance); type = type.BaseType; }
            info.SetValue(target, value);
        }

        private DungeonRuntimeServices BuildServices(ExpeditionState state)
        {
            return new DungeonRuntimeServices
            {
                LootCatalog = AssetDatabase.LoadAssetAtPath<LootSourceCatalog>("Assets/Game/ScriptableObjects/Loot/LootSourceCatalog.asset"),
                GroundLoot = new GroundLootRegistry(),
                ResolveDefinition = id => _registry.TryGet(id, out var d) ? d : null,
                ItemCatalog = _catalog,
                Prices = new PriceService(AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset")),
                MerchantConfig = AssetDatabase.LoadAssetAtPath<DungeonMerchantConfig>("Assets/Game/ScriptableObjects/Balance/DungeonMerchantConfig.asset"),
                CarriedWallet = state.CarriedWallet,
                EventConfig = AssetDatabase.LoadAssetAtPath<DungeonEventConfig>("Assets/Game/ScriptableObjects/Balance/DungeonEventConfig.asset"),
                BossSpawner = new GateBossSpawner(this),
                Expedition = _expedition
            };
        }

        private RoomRuntime CreateRoom(RoomType type, int nodeId, Vector2 offset, DungeonRuntimeContext context, string[] tags = null)
        {
            var definition = ScriptableObject.CreateInstance<RoomDefinition>();
            _created.Add(definition);
            Set(definition, "_id", $"gate_{type}".ToLowerInvariant());
            Set(definition, "_roomType", type);
            Set(definition, "_tags", tags ?? new string[0]);
            var go = new GameObject($"GateRoom_{nodeId}");
            _created.Add(go);
            go.transform.position = offset;
            go.AddComponent<UnityEngine.Grid>();
            var root = go.AddComponent<RoomRoot>();
            root.Configure(definition, new Vector2Int(16, 12));
            AddMarker(go, RoomMarkerRole.ChestSpawn, new Vector2Int(4, 6));
            AddMarker(go, RoomMarkerRole.MerchantAnchor, new Vector2Int(8, 8));
            AddMarker(go, RoomMarkerRole.EventAnchor, new Vector2Int(8, 4));
            AddMarker(go, RoomMarkerRole.BossAnchor, new Vector2Int(8, 9));
            AddMarker(go, RoomMarkerRole.EnemySpawn, new Vector2Int(13, 9));
            AddMarker(go, RoomMarkerRole.EnemySpawn, new Vector2Int(13, 2));
            var socket = new GameObject("Door_N").AddComponent<DoorSocket>();
            socket.transform.SetParent(go.transform, false);
            socket.Configure(DoorDirection.North, new Vector2Int(7, 11));
            socket.SnapToGrid();
            var runtime = go.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, context.Depth, context.PartySize);
            runtime.SetSpawner(context.Spawner);
            if (type == RoomType.Combat)
            {
                var plan = EncounterDirector.Compose(new EncounterContext(context.RunSeed, context.Depth, 1, Biome.RuinedMetro, nodeId), context.Archetypes);
                runtime.SetEncounter(plan, context.Spawner);
            }

            RoomCategoryComposer.Compose(runtime, context, _services);
            return runtime;
        }

        private static void AddMarker(GameObject room, RoomMarkerRole role, Vector2Int cell)
        {
            var marker = new GameObject(role.ToString()).AddComponent<RoomMarker>();
            marker.transform.SetParent(room.transform, false);
            marker.Configure(role, cell);
            marker.SnapToGrid();
        }

        private static int TotalUnits(PlayerInventory inventory)
        {
            var total = 0;
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) total += e.Quantity;
            }

            foreach (var s in inventory.BackpackSlots) if (s != null) total += s.Quantity;
            return total;
        }

        private static int GroundUnits(GroundLootRegistry ground) => ground.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null && p.Item != null).Sum(p => p.Item.Quantity);

        private static IEnumerable<string> AllInstanceIds(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) yield return e.InstanceId;
            }

            foreach (var s in inventory.BackpackSlots) if (s != null) yield return s.InstanceId;
        }

        private static void AssertNoDuplicateOwnership(PlayerInventory inventory, GroundLootRegistry ground, params IItemContainer[] extra)
        {
            var containers = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) containers.Add(new EquippedSlotContainer(inventory, slot));
            containers.AddRange(ground.Tracked.Select(g => g.GetComponent<WorldItemPickup>()).Where(p => p != null));
            containers.AddRange(extra.Where(c => c != null));
            CollectionAssert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(containers), "No instance may exist in two places.");
        }

        private static IEnumerator PickUpEverything(GroundLootRegistry ground, GameObject player, PlayerLootReceiver receiver)
        {
            foreach (var go in ground.Tracked.ToArray())
            {
                if (go == null) continue;
                var item = go.GetComponent<WorldItemPickup>();
                if (item != null)
                {
                    var result = item.TryPickUp(receiver.Backpack, receiver.TransferService);
                    Assert.IsTrue(result.Success || result.Error == TransferError.DestinationRejected, result.Error.ToString());
                    continue;
                }

                var coins = go.GetComponent<CoinPickup>();
                if (coins != null) Assert.IsTrue(coins.Interact(player));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SoloExpedition_CombatLoot_Merchant_PaidEvent_BossCache_Transit_ConserveEverything()
        {
            var state = _expedition.Start(_profile, runSeed: 2026, Biome.RuinedMetro);
            _services = BuildServices(state);
            using var groundLifetime = new GroundLootLifetime(_services.GroundLoot, _expedition);
            var context = new DungeonRuntimeContext(state.RunSeed, state.Depth, 1, _archetypes, new DefaultEnemySpawner());

            var player = new GameObject("Player");
            _created.Add(player);
            player.AddComponent<CircleCollider2D>().isTrigger = true;
            player.AddComponent<TeamMember>().SetTeam(DamageTeam.Player);
            player.AddComponent<TestDamageableTarget>();
            var receiver = player.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(state.Inventory);
            receiver.SetWallet(state.CarriedWallet);
            var startingIds = AllInstanceIds(state.Inventory).ToList();
            Assert.AreEqual(2, startingIds.Count, "Pistol + ammo stack carried in.");
            Assert.AreEqual(0, state.CarriedCoins);
            Assert.AreEqual(100, _profile.BankedCoins);

            // ---- 1. Combat room: enter, clear once, doors cycle ----
            var combat = CreateRoom(RoomType.Combat, 1, Vector2.zero, context);
            player.transform.position = new Vector2(1f, 1f);
            var clears = 0;
            combat.Cleared += (_, _) => clears++;
            Assert.IsTrue(combat.NotifyPlayerEntered(player));
            Assert.IsTrue(combat.DoorsLocked);
            yield return null;
            var guard = 0;
            while (combat.Lifecycle == RoomLifecycleState.Active && guard++ < 60)
            {
                foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsSortMode.None))
                {
                    if (enemy.IsAlive) enemy.GetComponent<HealthComponent>().TryApplyDamage(new DamageRequest(99999));
                }

                yield return null;
            }

            Assert.AreEqual(RoomLifecycleState.Cleared, combat.Lifecycle);
            Assert.AreEqual(1, clears);
            Assert.IsFalse(combat.DoorsLocked);
            Assert.IsFalse(combat.NotifyPlayerEntered(player));
            _expedition.RecordRoomCleared();

            // ---- 2. Loot room: chest opens once, pickups transfer exactly once ----
            var loot = CreateRoom(RoomType.Loot, 2, new Vector2(40f, 0f), context);
            var chest = loot.GetComponent<RoomContentBinding>().Chests.Single();
            player.transform.position = new Vector2(41f, 1f);
            loot.NotifyPlayerEntered(player);
            Assert.IsTrue(chest.TryOpen(out var chestLoot));
            Assert.IsFalse(chest.TryOpen(out _));
            var rolledIds = chestLoot.Items.Select(i => i.InstanceId).ToList();
            var rolledCoins = chestLoot.Coins;
            Assert.AreEqual(chestLoot.Items.Count + (rolledCoins > 0 ? 1 : 0), _services.GroundLoot.Count);
            AssertNoDuplicateOwnership(state.Inventory, _services.GroundLoot);
            var unitsBefore = TotalUnits(state.Inventory) + GroundUnits(_services.GroundLoot);
            yield return PickUpEverything(_services.GroundLoot, player, receiver);
            var carriedAfterChest = AllInstanceIds(state.Inventory).ToList();
            Assert.AreEqual(unitsBefore, TotalUnits(state.Inventory) + GroundUnits(_services.GroundLoot), "Item units are conserved across pickups (carried + ground).");
            Assert.IsTrue(rolledIds.All(id => carriedAfterChest.Contains(id)
                                              || _services.GroundLoot.Tracked.Any(g => g.GetComponent<WorldItemPickup>()?.Find(id) != null)
                                              || chestLoot.Items.First(i => i.InstanceId == id).Quantity == 0),
                "Non-merged rolled instances are carried or still on the ground.");

            Assert.AreEqual(rolledCoins, state.CarriedCoins, "Coins credited exactly once.");
            Assert.AreEqual(carriedAfterChest.Count, carriedAfterChest.Distinct().Count());
            AssertNoDuplicateOwnership(state.Inventory, _services.GroundLoot);

            // ---- 3. Merchant purchase: exact debit, item once, cannot buy twice ----
            var merchantRoom = CreateRoom(RoomType.Merchant, 3, new Vector2(80f, 0f), context);
            var merchant = merchantRoom.GetComponent<RoomContentBinding>().Merchant.Merchant;
            state.CarriedWallet.Credit(2000, "gate_funding");
            var coinsBefore = state.CarriedCoins;
            var offer = merchant.Offers.First(o => o.Definition is EquipmentItemDefinition);
            var offerItem = offer.Item;
            Assert.AreEqual(TradeError.None, merchant.Buy(offer.Index, receiver.Backpack));
            Assert.AreEqual(coinsBefore - offer.Price, state.CarriedCoins);
            Assert.AreEqual(TradeError.AlreadySold, merchant.Buy(offer.Index, receiver.Backpack));
            Assert.AreEqual(coinsBefore - offer.Price, state.CarriedCoins);
            Assert.AreEqual(1, AllInstanceIds(state.Inventory).Count(id => id == offerItem.InstanceId));
            Assert.AreEqual(100, _profile.BankedCoins, "Banked untouched inside the dungeon.");
            AssertNoDuplicateOwnership(state.Inventory, _services.GroundLoot, merchant.Sink);

            // ---- 4. Paid event: Locked Vault charges once, pays once ----
            var eventRoom = CreateRoom(RoomType.Event, 4, new Vector2(120f, 0f), context, new[] { "event:locked_vault" });
            var vault = eventRoom.GetComponent<RoomContentBinding>().Event;
            player.transform.position = new Vector2(121f, 1f);
            var groundBefore = _services.GroundLoot.Count;
            var beforeVault = state.CarriedCoins;
            Assert.IsTrue(vault.Interact(player));
            Assert.AreEqual(DungeonEventOutcome.Success, vault.LastResult.Outcome);
            Assert.AreEqual(beforeVault - 250, state.CarriedCoins, "Depth 1 vault costs 250.");
            var vaultItems = vault.LastResult.Loot.Items.Count + (vault.LastResult.Loot.Coins > 0 ? 1 : 0);
            Assert.AreEqual(groundBefore + vaultItems, _services.GroundLoot.Count);
            Assert.IsFalse(vault.Interact(player));
            Assert.AreEqual(beforeVault - 250, state.CarriedCoins);
            Assert.AreEqual(groundBefore + vaultItems, _services.GroundLoot.Count, "No second payout.");
            yield return PickUpEverything(_services.GroundLoot, player, receiver);
            AssertNoDuplicateOwnership(state.Inventory, _services.GroundLoot, merchant.Sink);

            // ---- 5. Boss room: defeat once → cache + transit, then descend clears the ground ----
            var bossRoom = CreateRoom(RoomType.Boss, 5, new Vector2(160f, 0f), context);
            var binding = bossRoom.GetComponent<RoomContentBinding>();
            player.transform.position = new Vector2(161f, 1f);
            Assert.IsTrue(bossRoom.NotifyPlayerEntered(player));
            Assert.IsTrue(bossRoom.DoorsLocked);
            Assert.IsTrue(binding.BossCache.IsLocked);
            yield return null;
            binding.Boss.Boss.Health.TryApplyDamage(new DamageRequest(99999));
            yield return null;
            Assert.AreEqual(RoomLifecycleState.Cleared, bossRoom.Lifecycle);
            Assert.IsFalse(binding.BossCache.IsLocked);
            Assert.IsTrue(binding.Transit.IsActivated);
            Assert.IsTrue(state.BossDefeatedThisDepth, "TransitCar recorded the defeat on the expedition.");
            Assert.AreEqual(1, state.Stats.BossesDefeated);
            Assert.AreEqual(1, _bossSpawns);
            Assert.IsTrue(binding.BossCache.TryOpen(out var cacheLoot));
            Assert.IsFalse(binding.BossCache.TryOpen(out _));
            Assert.IsTrue(cacheLoot.Items.Any(i => _registry.TryGet(i.DefinitionId, out var d) && d is EquipmentItemDefinition), "Boss Cache guarantees equipment.");
            var leftOnGround = _services.GroundLoot.Count;
            Assert.Greater(leftOnGround, 0);

            Assert.IsTrue(binding.Transit.Interact(player));
            Assert.IsFalse(binding.Transit.Interact(player), "Boarding is once.");
            var ownedBeforeDescend = AllInstanceIds(state.Inventory).ToList();
            var coinsBeforeDescend = state.CarriedCoins;
            Assert.IsTrue(binding.Transit.Choose(TransitChoice.DescendDeeper));
            Assert.IsFalse(binding.Transit.Choose(TransitChoice.ReturnToShelter), "A resolved decision cannot be changed.");
            Assert.AreEqual(2, state.Depth);
            Assert.AreEqual(0, _services.GroundLoot.Count, "Unclaimed ground loot is discarded when the party leaves the depth.");
            CollectionAssert.AreEquivalent(ownedBeforeDescend, AllInstanceIds(state.Inventory), "Descending keeps the carried inventory intact.");
            Assert.AreEqual(coinsBeforeDescend, state.CarriedCoins);
            Assert.AreEqual(100, _profile.BankedCoins);
            yield return null;

            // ---- 6. Return to Shelter on depth 2: everything carried becomes safe exactly once ----
            _expedition.RecordBossDefeated(650);
            Assert.IsTrue(_expedition.ChooseTransit(TransitChoice.ReturnToShelter));
            var summary = _expedition.LastSummary;
            Assert.IsNotNull(summary);
            Assert.IsTrue(summary.IsSuccess);
            CollectionAssert.AreEquivalent(ownedBeforeDescend, summary.ExtractedItemIds, "Every carried instance secured, none duplicated or lost.");
            Assert.AreEqual(coinsBeforeDescend, summary.CoinsExtracted);
            Assert.AreEqual(100 + coinsBeforeDescend, _profile.BankedCoins);
            Assert.AreEqual(0, state.CarriedCoins);
            var safeIds = _profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).Concat(_profile.SafeLoadout.Backpack.Select(b => b.Item.InstanceId)).ToList();
            CollectionAssert.AreEquivalent(ownedBeforeDescend, safeIds);
            Assert.AreEqual(safeIds.Count, safeIds.Distinct().Count());
            Assert.AreEqual(summary.TransactionId, _expedition.Return().TransactionId, "Replaying the return is idempotent.");
            Assert.AreEqual(100 + coinsBeforeDescend, _profile.BankedCoins, "No double banking.");
            Assert.IsFalse(_expedition.IsExpeditionActive);
        }
    }
}
