using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using RuinRail.App;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Loot;
using RuinRail.Gameplay.Player;
using RuinRail.Gameplay.Stats;
using RuinRail.Networking;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>
    /// Ammo pickup into a nearly full matching stack with no free backpack slot: exactly the part that fits moves (up
    /// to the existing stack cap), the rest stays on the ground with the exact remaining quantity; a full stack with no
    /// room leaves the pickup untouched; enough room still takes it whole. Manual interaction, the auto-pickup pull and
    /// the co-op host authority share one transaction; nothing is created or lost across repeats, save/load or replays.
    /// </summary>
    public sealed class PartialAmmoPickupTests
    {
        private readonly List<Object> _created = new();
        private GameContentCatalog _content;
        private ItemDefinitionRegistry _registry;
        private GroundLootRegistry _ground;
        private LootSpawner _spawner;

        /// <summary>A collector bonus like Scavenger's Reserve: +25 % (rounded) on the stack being picked up.</summary>
        private sealed class BonusHook : MonoBehaviour, IPickupQuantityHook
        {
            public int Calls;
            public int PickupQuantityFor(ItemInstance item) { Calls++; return Mathf.RoundToInt(item.Quantity * 1.25f); }
        }

        [SetUp]
        public void SetUp()
        {
            _content = GameContentCatalog.Load();
            _registry = _content.BuildRegistry();
            _ground = new GroundLootRegistry();
            var spawnerObject = new GameObject("LootSpawner");
            _created.Add(spawnerObject);
            _spawner = spawnerObject.AddComponent<LootSpawner>();
            _spawner.SetRegistry(_ground);
            _spawner.SetDefinitionResolver(id => _registry.TryGet(id, out var d) ? d : null);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
            foreach (var go in _ground.Tracked.ToArray()) if (go != null) Object.DestroyImmediate(go);
        }

        private int Cap(AmmoType type) => _content.AmmoBalance.GetStackLimit(type);
        private AmmoItemDefinition Ammo(AmmoType type) => _content.Items.OfType<AmmoItemDefinition>().First(a => a.AmmoType == type);
        private IEnumerable<AmmoType> ShippedTypes() => _content.Items.OfType<AmmoItemDefinition>().Select(a => a.AmmoType).Distinct().OrderBy(t => t);

        /// <summary>A player whose backpack holds one stack of <paramref name="type"/> at <paramref name="stack"/> rounds and single items in every other slot.</summary>
        private (GameObject go, PlayerLootReceiver receiver, PlayerInventory inventory, PickupAttractor attractor) FullBackpack(Vector2 at, AmmoType type, int stack, int freeSlots = 0)
        {
            var go = new GameObject("Player");
            _created.Add(go);
            go.transform.position = at;
            var inventory = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            var receiver = go.AddComponent<PlayerLootReceiver>();
            receiver.SetInventory(inventory);
            var attractor = go.AddComponent<PickupAttractor>();
            attractor.SetStats(new PlayerStats(_content.StatCaps));
            Assert.AreEqual(stack, inventory.Add(type, stack));
            while (inventory.BackpackSlots.Count(s => s == null) > freeSlots) Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("weapon_field_knife")));
            return (go, receiver, inventory, attractor);
        }

        private WorldItemPickup Ground(AmmoType type, int quantity, Vector2 at)
        {
            var pickup = _spawner.CreateItemPickup(at);
            pickup.Hold(new ItemInstance(Ammo(type).Id, quantity), ItemCategory.Ammo);
            return pickup;
        }

        private static int Slots(PlayerInventory inventory) => inventory.BackpackSlots.Count(s => s != null);

        [Test]
        public void EveryShippedAmmoType_FullBackpack_FillsTheMatchingStackToItsCap_AndLeavesTheExactRestOnTheGround()
        {
            var types = ShippedTypes().ToList();
            Assert.AreEqual(4, types.Count);
            var x = 0f;
            foreach (var type in types)
            {
                var cap = Cap(type);
                var (go, _, inventory, _) = FullBackpack(new Vector2(x += 30f, 0f), type, cap - 7);
                Assert.AreEqual(0, inventory.BackpackSlots.Count(s => s == null), $"{type}: the backpack is completely full");
                var pickup = Ground(type, 20, go.transform.position);
                var slotsBefore = Slots(inventory);

                Assert.IsTrue(pickup.Interact(go), $"{type}: the part that fits is taken");
                Assert.AreEqual(cap, inventory.Get(type), $"{type}: the stack is filled exactly to its cap ({cap})");
                Assert.AreEqual(13, pickup.Item.Quantity, $"{type}: exactly 20 - 7 stays on the ground");
                Assert.IsFalse(pickup.IsConsumed, $"{type}: the ground pickup is not deleted");
                Assert.AreEqual(slotsBefore, Slots(inventory), $"{type}: no slot was needed");
                Assert.AreEqual(cap - 7 + 20, inventory.Get(type) + pickup.Item.Quantity, $"{type}: nothing created or lost");

                // The stack is full now and there is no slot: a second attempt changes nothing.
                Assert.IsFalse(pickup.CanBeCollectedBy(go));
                Assert.IsFalse(pickup.Interact(go), $"{type}: nothing more fits");
                Assert.AreEqual(13, pickup.Item.Quantity);
                Assert.AreEqual(cap, inventory.Get(type));
            }
        }

        [Test]
        public void FullStackWithNoRoom_LeavesThePickupUntouched_AndEnoughRoomStillTakesItWhole()
        {
            var cap = Cap(AmmoType.Medium);
            var (full, _, fullInventory, _) = FullBackpack(Vector2.zero, AmmoType.Medium, cap);
            var untouched = Ground(AmmoType.Medium, 15, new Vector2(0.3f, 0f));
            var id = untouched.Item.InstanceId;
            Assert.IsFalse(untouched.CanBeCollectedBy(full));
            Assert.IsFalse(untouched.Interact(full));
            Assert.AreEqual(15, untouched.Item.Quantity);
            Assert.AreEqual(id, untouched.Item.InstanceId, "the same instance, untouched");
            Assert.AreEqual(cap, fullInventory.Get(AmmoType.Medium));

            // Room across the partial stack and a free slot: the whole pickup is taken as before.
            var (roomy, _, roomyInventory, _) = FullBackpack(new Vector2(20f, 0f), AmmoType.Medium, cap - 5, freeSlots: 1);
            var whole = Ground(AmmoType.Medium, 30, new Vector2(20.3f, 0f));
            Assert.IsTrue(whole.FitsWhollyFor(roomy));
            Assert.IsTrue(whole.Interact(roomy));
            Assert.IsTrue(whole.IsConsumed, "the whole stack moved and the pickup despawns");
            Assert.AreEqual(cap - 5 + 30, roomyInventory.Get(AmmoType.Medium));
        }

        [UnityTest]
        public IEnumerator AutoPickup_TakesThePartThatFits_WhereThePickupLies_AndNeverDragsTheRestOntoThePlayer()
        {
            var cap = Cap(AmmoType.Light);
            var (go, _, inventory, attractor) = FullBackpack(Vector2.zero, AmmoType.Light, cap - 4);
            var pickup = Ground(AmmoType.Light, 10, new Vector2(0.9f, 0f)); // inside the base reach, not under the player
            var spot = (Vector2)pickup.transform.position;
            Physics2D.SyncTransforms();
            for (var i = 0; i < 60; i++) attractor.Step(0.02f);
            yield return null;
            Assert.AreEqual(cap, inventory.Get(AmmoType.Light), "auto-pickup filled the stack");
            Assert.AreEqual(6, pickup.Item.Quantity, "the rest stays on the ground");
            Assert.AreEqual(spot, (Vector2)pickup.transform.position, "taken where it lies: the remainder is not parked at the player's feet");
            Assert.AreEqual(0, attractor.PulledCount, "nothing is being pulled");

            // Natural ammo that fits whole still flies in and is collected as before.
            var (roomy, _, roomyInventory, roomyAttractor) = FullBackpack(new Vector2(30f, 0f), AmmoType.Light, 10, freeSlots: 2);
            var flyIn = Ground(AmmoType.Light, 12, new Vector2(30.9f, 0f));
            Physics2D.SyncTransforms();
            for (var i = 0; i < 60; i++) roomyAttractor.Step(0.02f);
            yield return null;
            Assert.AreEqual(22, roomyInventory.Get(AmmoType.Light));
            Assert.IsTrue(flyIn == null || flyIn.IsConsumed);
        }

        [Test]
        public void CollectorBonus_AppliesToWholePickupsClippedToTheRoom_AndThePartialRestKeepsItsBaseQuantity()
        {
            var cap = Cap(AmmoType.Heavy);
            // Whole fit, bonus clipped: base 8 fits (room 9), the +25 % would make 10 — the collector gets the 9 that fit.
            // (The player's loot receiver is the game's own hook and answers GetComponent first, so the test hands its
            // bonus straight to the one pickup transaction that Interact and the host arbiter both call.)
            var (clipped, clippedReceiver, clippedInventory, _) = FullBackpack(Vector2.zero, AmmoType.Heavy, cap - 9);
            var clipHook = clipped.AddComponent<BonusHook>();
            var eight = Ground(AmmoType.Heavy, 8, Vector2.zero);
            Assert.IsTrue(eight.Collect(clippedReceiver.Backpack, clippedReceiver.TransferService, clipHook).Success);
            Assert.AreEqual(1, clipHook.Calls);
            Assert.IsTrue(eight.IsConsumed);
            Assert.AreEqual(cap, clippedInventory.Get(AmmoType.Heavy), "the bonus fills what room there is, never beyond the cap");

            // Partial fit: exactly the room moves; the rest keeps its unsized quantity (the bonus is not double-counted).
            var (partial, partialReceiver, partialInventory, _) = FullBackpack(new Vector2(20f, 0f), AmmoType.Heavy, cap - 3);
            var hook = partial.AddComponent<BonusHook>();
            var twelve = Ground(AmmoType.Heavy, 12, new Vector2(20f, 0f));
            Assert.IsTrue(twelve.Collect(partialReceiver.Backpack, partialReceiver.TransferService, hook).Success);
            Assert.AreEqual(cap, partialInventory.Get(AmmoType.Heavy));
            Assert.AreEqual(9, twelve.Item.Quantity, "12 - 3 base rounds remain");
            Assert.AreEqual(0, hook.Calls, "a partial take does not apply the whole-pickup bonus");
        }

        [Test]
        public void RepeatedPartialPickups_AndSaveLoad_NeverCreateOrLoseAmmo()
        {
            var cap = Cap(AmmoType.Shells);
            var (go, _, inventory, _) = FullBackpack(Vector2.zero, AmmoType.Shells, cap - 2);
            var pickup = Ground(AmmoType.Shells, 9, Vector2.zero);
            var total = inventory.Get(AmmoType.Shells) + 9;
            for (var round = 0; round < 5; round++)
            {
                pickup.Interact(go);
                Assert.AreEqual(total, inventory.Get(AmmoType.Shells) + (pickup.IsConsumed ? 0 : pickup.Item.Quantity), $"round {round}");
                // Spend a little ammo, the way firing would, and try again.
                inventory.Consume(AmmoType.Shells, 3);
                total -= 3;
            }

            // Save/load of the carried state keeps the exact reserve.
            var snapshot = JsonUtility.ToJson(inventory.ToSnapshot());
            var restored = PlayerInventory.FromRegistry(_registry, _content.AmmoBalance);
            restored.RestoreFromSnapshot(JsonUtility.FromJson<InventorySnapshot>(snapshot));
            Assert.AreEqual(inventory.Get(AmmoType.Shells), restored.Get(AmmoType.Shells));
            Assert.AreEqual(Slots(inventory), Slots(restored));
        }

        [Test]
        public void CoopAuthority_GivesExactlyThePartThatFits_ReportsIt_AndAReplayedOrRepeatedRequestTakesNothingMore()
        {
            var cap = Cap(AmmoType.Light);
            var member = FullBackpack(new Vector2(100f, 0f), AmmoType.Light, cap - 6);
            var authority = new LootAuthorityService(LocalAuthorityContext.Instance);
            authority.RegisterParticipant(new LootParticipant(7, "member", member.receiver.Backpack, member.receiver.Wallet, member.go, member.receiver.CarriedContainers));
            var pickup = Ground(AmmoType.Light, 25, member.go.transform.position);

            var result = authority.RequestPickup("tx-1", 7, pickup);
            Assert.AreEqual(LootVerdict.Accepted, result.Verdict);
            Assert.AreEqual(6, result.Quantity, "the grant a client receives is exactly what the host moved");
            Assert.AreEqual(cap, member.inventory.Get(AmmoType.Light));
            Assert.AreEqual(19, pickup.Item.Quantity);

            var replay = authority.RequestPickup("tx-1", 7, pickup);
            Assert.AreEqual(6, replay.Quantity, "a re-delivered request returns the recorded result");
            Assert.AreEqual(19, pickup.Item.Quantity, "and moves nothing again");
            var again = authority.RequestPickup("tx-2", 7, pickup);
            Assert.AreEqual(LootVerdict.Rejected, again.Verdict, "no room left: refused");
            Assert.AreEqual(19, pickup.Item.Quantity);
            Assert.AreEqual(cap + 19, member.inventory.Get(AmmoType.Light) + pickup.Item.Quantity - 0, "conserved");
        }
    }
}
