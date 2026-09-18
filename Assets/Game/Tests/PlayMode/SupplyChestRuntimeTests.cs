using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Core;
using RuinRail.Core.Rendering;
using RuinRail.Dungeon.Generation;
using RuinRail.Dungeon.Rooms;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Enemies;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// The ordinary-room Supply Chest through the real room composer on shipped prefabs: placed on a valid cell, drawn
    /// with the final art, one reward transaction, pickups tracked only once they are complete, opened state
    /// restored on a revisit without re-rolling, the Boss Cache gate → crate → opened crate, and — on the network
    /// harness — two clients racing for the chest's ammo stack with exactly one winner.
    /// </summary>
    public sealed class SupplyChestRuntimeTests
    {
        private sealed class Authority : IAuthorityContext
        {
            public Authority(NetworkRole role) { Role = role; }
            public NetworkRole Role { get; }
            public bool IsAuthority => Role != NetworkRole.Client;
        }

        private readonly List<Object> _created = new();
        private GameContentCatalog _content;

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            WorldObjectArt.Resolver = _content.WorldSpriteFor;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            WorldObjectArt.Resolver = null;
        }

        private (RoomRuntime runtime, DungeonRuntimeContext context, DungeonRuntimeServices services, GroundLootRegistry ground) Compose(RoomDefinition definition, int nodeId, bool withSupplyChest, int seed = 11)
        {
            var instance = Object.Instantiate(definition.Prefab);
            _created.Add(instance);
            var root = instance.GetComponent<RoomRoot>();
            var runtime = instance.AddComponent<RoomRuntime>();
            runtime.Configure(root, nodeId, 1, 1);
            var context = new DungeonRuntimeContext(seed, 1, 1, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling)
            {
                SupplyChestRooms = withSupplyChest ? new HashSet<int> { nodeId } : new HashSet<int>()
            };
            var ground = new GroundLootRegistry();
            var services = Services(ground);
            RoomCategoryComposer.Compose(runtime, context, services);
            return (runtime, context, services, ground);
        }

        private DungeonRuntimeServices Services(GroundLootRegistry ground) => new()
        {
            LootCatalog = _content.Loot,
            GroundLoot = ground,
            ResolveDefinition = id => _content.Items.FirstOrDefault(i => i != null && i.Id == id),
            ItemCatalog = _content.Items,
            Prices = new PriceService(_content.Economy),
            MerchantConfig = _content.Merchant,
            CarriedWallet = new CoinWallet(CoinDomain.Carried),
            EventConfig = _content.Events,
            UsefulAmmoTypes = new[] { AmmoType.Light }
        };

        [Test]
        public void OrdinaryCombatRoom_SelectedByThePlan_GetsOneVisibleSupplyChest_OnAValidCell_AndUnselectedRoomsGetNone()
        {
            foreach (var biome in new[] { Biome.RuinedMetro, Biome.Rustworks, Biome.OvergrownLabs })
            {
                var definition = _content.Rooms.First(r => r.Biome == biome && r.RoomType == RoomType.Combat);
                var (runtime, _, _, _) = Compose(definition, 5, withSupplyChest: true);
                var binding = runtime.GetComponent<RoomContentBinding>();
                Assert.IsNotNull(binding.SupplyChest, definition.Id);
                Assert.AreEqual(1, binding.Chests.Count, definition.Id + ": exactly one container");
                Assert.AreEqual(LootSourceKind.SupplyChest, binding.SupplyChest.Kind);
                Assert.IsTrue(binding.SupplyChestCell.HasValue);
                var grid = RoomLogicGrid.FromRoom(runtime.Root);
                Assert.IsTrue(grid.IsWalkable(binding.SupplyChestCell.Value), definition.Id);
                Assert.AreEqual((Vector3)SupplyChestPlacement.WorldCenter(runtime.Root, binding.SupplyChestCell.Value), binding.SupplyChest.transform.position);
                var visual = binding.SupplyChest.Visual;
                Assert.IsNotNull(visual, definition.Id + ": drawn");
                Assert.IsTrue(visual.IsVisible);
                Assert.AreEqual(_content.WorldSpriteFor(WorldObjectArt.SupplyChest), visual.Renderer.sprite, "the final supply chest art, not a placeholder");
                Assert.AreEqual(SortingLayers.Characters, visual.Renderer.sortingLayerName, "y-sorted with the characters: never under the floor or wall tilemaps");
                Assert.AreEqual(SortingConvention.OrderOf(SortingRole.Character, binding.SupplyChest.transform.position.y), visual.Renderer.sortingOrder);
                Assert.IsTrue(binding.SupplyChest.GetComponent<Collider2D>().isTrigger, "reachable: never a solid blocker");
                Assert.IsTrue(binding.SupplyChest.CanInteract(null));
                Assert.AreEqual("OPEN CHEST", binding.SupplyChest.PromptFor(null));

                var (other, _, _, _) = Compose(definition, 6, withSupplyChest: false);
                Assert.IsNull(other.GetComponent<RoomContentBinding>().SupplyChest, "an unselected ordinary room stays without a container");
                Assert.AreEqual(0, other.GetComponent<RoomContentBinding>().Chests.Count);
            }
        }

        [Test]
        public void SupplyChest_OpensOnce_SpawnsVisiblePickups_TracksThemComplete_AndRestoresOpenedWithoutReRolling()
        {
            var definition = _content.Rooms.First(r => r.Biome == Biome.RuinedMetro && r.RoomType == RoomType.Combat);
            var (runtime, context, services, ground) = Compose(definition, 3, withSupplyChest: true);
            var chest = runtime.GetComponent<RoomContentBinding>().SupplyChest;
            var tracked = new List<GameObject>();
            var completeWhenTracked = true;
            ground.PickupTracked += go =>
            {
                tracked.Add(go);
                var pickup = go.GetComponent<WorldItemPickup>();
                if (pickup != null && (pickup.Item == null || string.IsNullOrEmpty(pickup.DisplayName))) completeWhenTracked = false;
                var coins = go.GetComponent<CoinPickup>();
                if (coins != null && coins.Amount <= 0) completeWhenTracked = false;
            };
            var opened = 0;
            chest.Opened += (_, _) => opened++;

            Assert.IsTrue(chest.TryOpen(out var result));
            Assert.IsFalse(result.IsEmpty);
            Assert.AreEqual(1, opened);
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, chest.Visual.Key);
            Assert.AreEqual(_content.WorldSpriteFor(WorldObjectArt.SupplyChestOpen), chest.Visual.Renderer.sprite, "obvious opened state");
            Assert.AreEqual(string.Empty, chest.PromptFor(null), "no prompt on an opened chest");
            Assert.AreEqual(chest.SpawnedPickups.Count, tracked.Count, "every pickup is tracked exactly once");
            Assert.IsTrue(completeWhenTracked, "observers see item, name and amount when a pickup is tracked");
            Assert.IsTrue(chest.SpawnedPickups.All(p => p.GetComponent<WorldObjectVisual>() != null && p.GetComponent<WorldObjectVisual>().IsVisible && p.GetComponent<WorldObjectVisual>().Renderer.sortingLayerName == SortingLayers.Loot));
            var ammo = chest.SpawnedPickups.Select(p => p.GetComponent<WorldItemPickup>()).First(p => p != null && p.Category == ItemCategory.Ammo);
            Assert.Greater(ammo.Item.Quantity, 0);
            Assert.IsTrue(services.ResolveDefinition(ammo.Item.DefinitionId) is AmmoItemDefinition, "a valid ammo definition");

            Assert.IsFalse(chest.TryOpen(out var again), "the second open rolls nothing");
            Assert.IsNull(again);
            Assert.AreEqual(1, opened);
            Assert.IsFalse(chest.Interact(null));
            Assert.IsTrue(runtime.State.IsResolved(RoomCategoryComposer.SupplyChestResolvedId));

            // Revisit: the same room composed again from the persisted state comes back opened with nothing to roll.
            var revisit = Object.Instantiate(definition.Prefab);
            _created.Add(revisit);
            var runtime2 = revisit.AddComponent<RoomRuntime>();
            runtime2.Configure(revisit.GetComponent<RoomRoot>(), 3, 1, 1);
            runtime2.RestoreState(runtime.State);
            RoomCategoryComposer.Compose(runtime2, context, services);
            var restored = runtime2.GetComponent<RoomContentBinding>().SupplyChest;
            Assert.IsTrue(restored.IsOpened, "opened state does not reset during the same run");
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, restored.Visual.Key);
            Assert.IsFalse(restored.TryOpen(out _));
            Assert.AreEqual(0, restored.SpawnedPickups.Count, "no repeated loot after leaving and re-entering");
        }

        [Test]
        public void BossCache_DrawsTheGateWhileLocked_TheCrateWhenUnlocked_AndTheOpenedCrateAfterItsOneReward()
        {
            var go = new GameObject("Cache");
            _created.Add(go);
            var chest = go.AddComponent<SupplyChest>();
            _content.Loot.Configure(chest, LootSourceKind.BossCache, 42, 1, 15, 1, new[] { AmmoType.Light }, null);
            chest.AttachVisual();
            var gate = go.AddComponent<BossCacheGate>();
            Assert.IsTrue(chest.IsLocked);
            Assert.AreEqual(WorldObjectArt.BossCacheGate, chest.Visual.Key);
            Assert.AreEqual("LOCKED", chest.PromptFor(null));
            Assert.IsFalse(chest.TryOpen(out _), "locked: the roll is deferred");
            chest.SetLocked(false);
            Assert.AreEqual(WorldObjectArt.SupplyChest, chest.Visual.Key);
            Assert.AreEqual("OPEN BOSS CACHE", chest.PromptFor(null));
            Assert.IsTrue(chest.TryOpen(out var result) && !result.IsEmpty);
            Assert.AreEqual(WorldObjectArt.SupplyChestOpen, chest.Visual.Key);
            Assert.IsTrue(gate.IsUnlocked);
        }

        [Test]
        public void MerchantAndEventAnchors_DrawTheirFinalArt_AndAUsedEventReadsAsUsed()
        {
            var merchantRoom = _content.Rooms.First(r => r.Biome == Biome.Rustworks && r.RoomType == RoomType.Merchant);
            var (merchant, _, _, _) = Compose(merchantRoom, 4, withSupplyChest: false);
            var binding = merchant.GetComponent<RoomContentBinding>();
            Assert.IsNotNull(binding.Merchant, string.Join(",", binding.Skipped));
            var merchantVisual = binding.Merchant.GetComponent<WorldObjectVisual>();
            Assert.IsTrue(merchantVisual != null && merchantVisual.IsVisible && merchantVisual.Renderer.sprite == _content.WorldSpriteFor(WorldObjectArt.DungeonMerchant));
            var probe = new GameObject("p");
            _created.Add(probe);
            Assert.AreEqual("TRADE WITH MERCHANT", ((IInteractionPrompt)binding.Merchant).PromptFor(probe));

            var eventRoom = _content.Rooms.First(r => r.Biome == Biome.OvergrownLabs && r.RoomType == RoomType.Event);
            var (evt, _, _, _) = Compose(eventRoom, 8, withSupplyChest: false);
            var eventBinding = evt.GetComponent<RoomContentBinding>();
            Assert.IsNotNull(eventBinding.Event, string.Join(",", eventBinding.Skipped));
            var kind = eventBinding.EventInstance.Kind;
            var visual = eventBinding.Event.GetComponent<WorldObjectVisual>();
            Assert.IsTrue(visual != null && visual.IsVisible);
            Assert.AreEqual(_content.WorldSpriteFor(WorldObjectArt.EventKey(kind.ToString())), visual.Renderer.sprite, kind.ToString());
            Assert.AreEqual(Color.white, visual.Renderer.color);
            evt.State.MarkResolved($"event:{kind}");
            var revisit = Object.Instantiate(eventRoom.Prefab);
            _created.Add(revisit);
            var runtime2 = revisit.AddComponent<RoomRuntime>();
            runtime2.Configure(revisit.GetComponent<RoomRoot>(), 8, 1, 1);
            runtime2.RestoreState(evt.State);
            RoomCategoryComposer.Compose(runtime2, new DungeonRuntimeContext(11, 1, 1, _content.Enemies, new DefaultEnemySpawner(_content.Stagger), _content.DepthScaling), Services(new GroundLootRegistry()));
            var restoredVisual = runtime2.GetComponent<RoomContentBinding>().Event.GetComponent<WorldObjectVisual>();
            Assert.AreEqual(WorldObjectVisual.ResolvedTint, restoredVisual.Renderer.color, "a resolved event is drawn dimmed");
        }

        [UnityTest]
        public IEnumerator Network_TwoClientsRaceForTheSupplyChestAmmo_ExactlyOneReserveIncrements_AndTheChestOpensOnce()
        {
            var definition = _content.Rooms.First(r => r.Biome == Biome.RuinedMetro && r.RoomType == RoomType.Combat);
            var (runtime, _, services, ground) = Compose(definition, 2, withSupplyChest: true);
            var chest = runtime.GetComponent<RoomContentBinding>().SupplyChest;
            var registry = _content.BuildRegistry();
            var invA = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var invB = PlayerInventory.FromRegistry(registry, _content.AmmoBalance);
            var a = new LootParticipant(1, "p1", new BackpackContainer(invA), new CoinWallet(CoinDomain.Carried));
            var b = new LootParticipant(2, "p2", new BackpackContainer(invB), new CoinWallet(CoinDomain.Carried));
            var host = new LootAuthorityService(new Authority(NetworkRole.Host));
            host.RegisterParticipant(a);
            host.RegisterParticipant(b);
            host.SetDropService(new ItemDropService(services.CreateLootSpawner(runtime.gameObject)));

            var first = host.RequestChestOpen("tx-open-a", 1, chest);
            var second = host.RequestChestOpen("tx-open-b", 2, chest);
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreNotEqual(LootVerdict.Accepted, second.Verdict, "the chest opens once for the party");
            Assert.IsTrue(chest.IsOpened);
            var ammo = chest.SpawnedPickups.Select(p => p.GetComponent<WorldItemPickup>()).First(p => p != null && p.Category == ItemCategory.Ammo);
            var type = ((AmmoItemDefinition)services.ResolveDefinition(ammo.Item.DefinitionId)).AmmoType;
            var quantity = ammo.Item.Quantity;

            var takeA = host.RequestPickup("tx-take-a", 1, ammo);
            var takeB = host.RequestPickup("tx-take-b", 2, ammo);
            var retryA = host.RequestPickup("tx-take-a", 1, ammo);
            yield return null;
            Assert.AreEqual(LootVerdict.Accepted, takeA.Verdict);
            Assert.AreEqual(LootVerdict.AlreadyTaken, takeB.Verdict);
            Assert.AreSame(takeA, retryA, "a re-sent request never executes again");
            Assert.AreEqual(quantity, invA.Get(type), "the winner's reserve incremented by exactly the stack");
            Assert.AreEqual(0, invB.Get(type), "the loser got nothing");
            Assert.IsFalse(ground.Tracked.Any(g => g != null && g.GetComponent<WorldItemPickup>() is { } p && p.Item != null && p.Category == ItemCategory.Ammo && !p.IsConsumed), "the stack left the ground exactly once");
        }
    }
}
