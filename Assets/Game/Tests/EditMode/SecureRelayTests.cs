using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Dungeon.Runtime;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Events;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Networking;
using RuinRail.Persistence;
using RuinRail.UI.Base;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>57.7 Secure Relay: eligibility, the one-unit atomic move into Storage, per-member single use, host arbitration, generation and persistence.</summary>
    public class SecureRelayTests
    {
        private sealed class TestItemDefinition : ItemDefinition
        {
        }

        private readonly List<Object> _created = new();
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            _ammoBalance = ScriptableObject.CreateInstance<AmmoBalanceConfig>();
            _created.Add(_ammoBalance);
            _registry = ItemDefinitionRegistry.Build(new ItemDefinition[]
            {
                Def<TestItemDefinition>("weapon_p9_ranger", ItemCategory.Weapon),
                Def<TestItemDefinition>("weapon_vx_rifle", ItemCategory.Weapon),
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
                Def<TestItemDefinition>("accessory_ring", ItemCategory.Accessory),
                Def<TestItemDefinition>("consumable_bandage", ItemCategory.Consumable, true, 5),
                Ammo("ammo_light", AmmoType.Light)
            });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private T Def<T>(string id, ItemCategory category, bool stackable = false, int maxStack = 1) where T : ItemDefinition
        {
            var d = ScriptableObject.CreateInstance<T>();
            _created.Add(d);
            typeof(ItemDefinition).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, id);
            typeof(ItemDefinition).GetField("_category", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, category);
            typeof(ItemDefinition).GetField("_isStackable", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, stackable);
            typeof(ItemDefinition).GetField("_maxStack", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, maxStack);
            return d;
        }

        private AmmoItemDefinition Ammo(string id, AmmoType type)
        {
            var d = Def<AmmoItemDefinition>(id, ItemCategory.Ammo, true, 999);
            typeof(AmmoItemDefinition).GetField("_ammoType", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(d, type);
            return d;
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;
        private AmmoItemDefinition ResolveAmmo(AmmoType type) => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == type);

        private PlayerInventory NewRunInventory()
        {
            var inventory = new PlayerInventory(Resolve, ResolveAmmo, _ammoBalance) { MarksIncomingAtRisk = true };
            return inventory;
        }

        private static IReadOnlyList<IItemContainer> Carried(PlayerInventory inventory)
        {
            var list = new List<IItemContainer> { new BackpackContainer(inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) list.Add(new EquippedSlotContainer(inventory, slot));
            return list;
        }

        private SecureRelayEvent Relay(int index = 3) => new(new DungeonEventContext(11, 2, index), Resolve);
        private Storage NewStorage() => new(Resolve, _ammoBalance);

        private static IEnumerable<ItemInstance> AllCarried(PlayerInventory inventory) =>
            System.Enum.GetValues(typeof(EquippedSlot)).Cast<EquippedSlot>().Select(inventory.GetEquipped).Concat(inventory.BackpackSlots).Where(i => i != null);

        // ---- eligibility ----

        [Test]
        public void Eligibility_WeaponsArmorAccessoriesConsumables_ButNeverAmmoOrStarterGear()
        {
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEvent.Eligibility(new ItemInstance("weapon_vx_rifle"), Resolve("weapon_vx_rifle")));
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEvent.Eligibility(new ItemInstance("armor_scrap_vest", 1, Rarity.Rare), Resolve("armor_scrap_vest")));
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEvent.Eligibility(new ItemInstance("accessory_ring"), Resolve("accessory_ring")));
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEvent.Eligibility(new ItemInstance("consumable_bandage", 3), Resolve("consumable_bandage")));
            Assert.AreEqual(SecureRelayRefusal.Ammo, SecureRelayEvent.Eligibility(new ItemInstance("ammo_light", 60), Resolve("ammo_light")));
            // Starter Kit gear (75) carries IsUnsellable: protected, never secured.
            foreach (var (item, _) in StarterKitService.CreateKit().Where(k => k.item.IsUnsellable))
                Assert.AreEqual(SecureRelayRefusal.StarterItem, SecureRelayEvent.Eligibility(item, Resolve(item.DefinitionId) ?? Resolve("weapon_p9_ranger")), item.DefinitionId);
        }

        [Test]
        public void RefusedItems_LeaveRunAndStorageUntouched()
        {
            var inventory = NewRunInventory();
            var starter = new ItemInstance("weapon_p9_ranger") { IsUnsellable = true };
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("ammo_light", 60)));
            var ammo = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == "ammo_light"); // stacks are re-homed on add
            Assert.IsTrue(inventory.TryEquip(starter, EquippedSlot.PrimaryWeapon));
            var storage = NewStorage();
            var relay = Relay();

            Assert.AreEqual(SecureRelayRefusal.Ammo, relay.Secure("p1", Carried(inventory), ammo.InstanceId, new SecureRelayStorageTarget(storage)).Refusal);
            Assert.AreEqual(SecureRelayRefusal.StarterItem, relay.Secure("p1", Carried(inventory), starter.InstanceId, new SecureRelayStorageTarget(storage)).Refusal);
            Assert.AreEqual(SecureRelayRefusal.NoItem, relay.Secure("p1", Carried(inventory), "not-carried", new SecureRelayStorageTarget(storage)).Refusal);
            Assert.AreEqual(0, storage.OccupiedSlots);
            Assert.AreEqual(60, ammo.Quantity);
            Assert.AreSame(starter, inventory.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsFalse(relay.HasSecured("p1"), "A refused press spends nothing.");
        }

        // ---- the transfer ----

        [Test]
        public void Secure_MovesTheEquippedWeaponIntoStorage_SafeAndGoneFromTheRun()
        {
            var inventory = NewRunInventory();
            var rifle = new ItemInstance("weapon_vx_rifle", 1, Rarity.Epic);
            Assert.IsTrue(inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(rifle.IsAtRisk);
            var storage = NewStorage();
            var relay = Relay();
            string marked = null;
            relay.Secured += (_, participant, _) => marked = participant;

            var result = relay.Secure("p1", Carried(inventory), rifle.InstanceId, new SecureRelayStorageTarget(storage));

            Assert.IsTrue(result.Success, result.Refusal.ToString());
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "The weapon left the run.");
            Assert.AreSame(rifle, storage.Find(rifle.InstanceId), "The same instance (rarity, affixes) is now in Storage.");
            Assert.IsFalse(rifle.IsAtRisk, "Stored items are safe.");
            Assert.AreEqual(Rarity.Epic, storage.Find(rifle.InstanceId).Rarity);
            Assert.AreEqual("p1", marked);
            Assert.IsTrue(relay.HasSecured("p1"));
            Assert.AreEqual("weapon_vx_rifle", relay.SecuredDefinitionOf("p1"));
            Assert.AreEqual(0, ItemTransferService.DetectDuplicateOwnership(Carried(inventory).Append(storage)).Count);
        }

        [Test]
        public void Secure_TakesExactlyOneUnitOfAConsumableStack()
        {
            var inventory = NewRunInventory();
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 3)));
            var bandages = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == "consumable_bandage"); // stacks are re-homed on add
            var storage = NewStorage();

            var result = Relay().Secure("p1", Carried(inventory), bandages.InstanceId, new SecureRelayStorageTarget(storage));

            Assert.IsTrue(result.Success, result.Refusal.ToString());
            Assert.AreEqual(2, bandages.Quantity, "Two stay in the run.");
            Assert.AreEqual(1, storage.CountOf("consumable_bandage"));
            Assert.IsTrue(storage.Items.All(i => !i.IsAtRisk));
            Assert.IsTrue(bandages.IsAtRisk, "What stays carried stays at risk.");
        }

        [Test]
        public void Secure_StorageFull_RemovesNothingAndSpendsNoUse()
        {
            var inventory = NewRunInventory();
            var rifle = new ItemInstance("weapon_vx_rifle");
            Assert.IsTrue(inventory.TryAddToBackpack(rifle));
            var storage = NewStorage();
            while (storage.FreeSlots > 0) Assert.IsTrue(storage.TryAdd(new ItemInstance("armor_scrap_vest")));
            var relay = Relay();

            var result = relay.Secure("p1", Carried(inventory), rifle.InstanceId, new SecureRelayStorageTarget(storage));

            Assert.AreEqual(SecureRelayRefusal.StorageFull, result.Refusal);
            Assert.AreSame(rifle, inventory.BackpackSlots[0], "The item stays exactly where it was.");
            Assert.IsTrue(rifle.IsAtRisk);
            Assert.IsNull(storage.Find(rifle.InstanceId));
            Assert.IsFalse(relay.HasSecured("p1"), "The use is spent only by a completed transfer.");
        }

        [Test]
        public void EachMember_SecuresExactlyOnce_Independently_AndRepeatedPressesChangeNothing()
        {
            var relay = Relay();
            var a = NewRunInventory();
            var b = NewRunInventory();
            var a1 = new ItemInstance("weapon_vx_rifle");
            var a2 = new ItemInstance("armor_scrap_vest");
            var b1 = new ItemInstance("accessory_ring");
            a.TryAddToBackpack(a1);
            a.TryAddToBackpack(a2);
            b.TryAddToBackpack(b1);
            var storageA = NewStorage();
            var storageB = NewStorage();

            Assert.IsTrue(relay.Secure("pA", Carried(a), a1.InstanceId, new SecureRelayStorageTarget(storageA)).Success);
            var again = relay.Secure("pA", Carried(a), a2.InstanceId, new SecureRelayStorageTarget(storageA));
            Assert.AreEqual(SecureRelayRefusal.AlreadySecured, again.Refusal);
            Assert.IsTrue(a.BackpackSlots.Contains(a2), "A second item never leaves.");
            Assert.AreEqual(1, storageA.OccupiedSlots);

            Assert.IsTrue(relay.Secure("pB", Carried(b), b1.InstanceId, new SecureRelayStorageTarget(storageB)).Success, "Another member keeps its own use.");
            Assert.AreEqual(2, relay.SecuredCount);
            Assert.AreEqual(DungeonEventPhase.Available, relay.Phase, "The relay never resolves for the party.");
        }

        [Test]
        public void RoomStateIds_RestoreUses_AndNeverUseTheWholeEventPrefix()
        {
            Assert.IsFalse(SecureRelayEvent.ResolvedIdFor("p1").StartsWith(RoomCategoryComposer.EventTagPrefix), "An \"event:\" id would resolve the relay for everyone on a co-op resync.");
            var relay = Relay();
            relay.RestoreSecured("p1", "weapon_vx_rifle");
            Assert.IsTrue(relay.HasSecured("p1"));
            Assert.IsFalse(relay.HasSecured("p2"));
            var inventory = NewRunInventory();
            var ring = new ItemInstance("accessory_ring");
            inventory.TryAddToBackpack(ring);
            Assert.AreEqual(SecureRelayRefusal.AlreadySecured, relay.Secure("p1", Carried(inventory), ring.InstanceId, new SecureRelayStorageTarget(NewStorage())).Refusal);
        }

        // ---- generation ----

        [Test]
        public void EventKindRoll_RelayIsRare_Deterministic_AndLeavesEveryOtherRoomsEventUnchanged()
        {
            var relays = 0;
            var total = 0;
            for (var seed = 1; seed <= 400; seed++)
            for (var node = 0; node < 10; node++)
            {
                total++;
                var kind = RoomCategoryComposer.ResolveEventKind(null, seed, 2, node);
                Assert.AreEqual(kind, RoomCategoryComposer.ResolveEventKind(null, seed, 2, node), "Same run, same room, same event.");
                if (kind == DungeonEventKind.SecureRelay) { relays++; continue; }
                Assert.AreEqual(RoomCategoryComposer.ResolveEventKind(null, seed, 2, node, 0), kind, "A room the relay does not claim keeps its event.");
            }

            var rate = relays / (float)total;
            Assert.That(rate, Is.InRange(0.04f, 0.12f), $"relay rate {rate:P1} over {total} event rooms (config {DungeonEventConfig.DefaultSecureRelayChancePercent}%)");
            Assert.IsFalse(Enumerable.Range(0, 500).Any(n => RoomCategoryComposer.ResolveEventKind(null, n, 1, n % 7, 0) == DungeonEventKind.SecureRelay), "0% never spawns it.");
            Assert.AreEqual(DungeonEventKind.SecureRelay, RoomCategoryComposer.ResolveEventKind(new[] { "event:secure_relay" }, 5, 1, 0, 0), "An authored tag pins it.");
            Assert.IsFalse(RoomCategoryComposer.RandomEventKinds.Contains(DungeonEventKind.SecureRelay), "The six-way pick is unchanged.");
        }

        // ---- co-op authority ----

        [Test]
        public void Host_DecidesOncePerParticipant_ReplaysByTransaction_AndRejectsAnotherMembersItem()
        {
            var loot = new LootAuthorityService(LocalAuthorityContext.Instance);
            var a = NewRunInventory();
            var b = NewRunInventory();
            var aRifle = new ItemInstance("weapon_vx_rifle");
            var aVest = new ItemInstance("armor_scrap_vest");
            var bRing = new ItemInstance("accessory_ring");
            a.TryEquip(aRifle, EquippedSlot.PrimaryWeapon);
            a.TryAddToBackpack(aVest);
            b.TryAddToBackpack(bRing);
            var carriedA = Carried(a);
            var carriedB = Carried(b);
            loot.RegisterParticipant(new LootParticipant(1, "pA", carriedA[0], new CoinWallet(CoinDomain.Carried), null, carriedA));
            loot.RegisterParticipant(new LootParticipant(2, "pB", carriedB[0], new CoinWallet(CoinDomain.Carried), null, carriedB));
            var relay = Relay();

            // Member B names member A's rifle: the host looks only in B's own containers.
            var wrongOwner = loot.RequestRelaySecure("tx-b1", 2, relay, aRifle.InstanceId);
            Assert.AreEqual(LootVerdict.Rejected, wrongOwner.Verdict);
            Assert.AreSame(aRifle, a.GetEquipped(EquippedSlot.PrimaryWeapon));
            Assert.IsFalse(relay.HasSecured("pB"), "A rejected request spends no use.");

            var first = loot.RequestRelaySecure("tx-a1", 1, relay, aRifle.InstanceId);
            Assert.AreEqual(LootVerdict.Accepted, first.Verdict);
            Assert.AreEqual(1, first.Quantity);
            Assert.IsNull(a.GetEquipped(EquippedSlot.PrimaryWeapon), "The host's copy of the unit left the member's mirrored inventory.");

            var replay = loot.RequestRelaySecure("tx-a1", 1, relay, aRifle.InstanceId);
            Assert.AreSame(first, replay, "A re-sent request returns the stored verdict and executes nothing.");
            var second = loot.RequestRelaySecure("tx-a2", 1, relay, aVest.InstanceId);
            Assert.AreEqual(LootVerdict.AlreadyTaken, second.Verdict);
            Assert.AreSame(aVest, a.BackpackSlots.First(i => i != null));

            Assert.AreEqual(LootVerdict.Accepted, loot.RequestRelaySecure("tx-b2", 2, relay, bRing.InstanceId).Verdict, "Member B's own use is independent.");
            Assert.IsTrue(relay.HasSecured("pA") && relay.HasSecured("pB"));
        }

        [Test]
        public void MemberCommit_AfterHostGrant_WorksEvenWhenTheRoomStateArrivedFirst()
        {
            var inventory = NewRunInventory();
            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            inventory.TryEquip(vest, EquippedSlot.Armor);
            var storage = NewStorage();
            var replica = Relay();
            replica.RestoreSecured("pA"); // the host's room state ("relay:pA") landed before the verdict

            Assert.AreEqual(SecureRelayRefusal.AlreadySecured, replica.Secure("pA", Carried(inventory), vest.InstanceId, new SecureRelayStorageTarget(storage)).Refusal);
            var committed = replica.Secure("pA", Carried(inventory), vest.InstanceId, new SecureRelayStorageTarget(storage), authorized: true);
            Assert.IsTrue(committed.Success, committed.Refusal.ToString());
            Assert.IsNotNull(storage.Find(vest.InstanceId));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Armor));
        }

        // ---- persistence: the secured item survives save/reload, death and an abandoned run ----

        private SaveSlot SlotWithLoadout(out string rifleId)
        {
            var slot = SaveSlotService.CreateNew();
            slot.Profile.BankedCoins = 100;
            var rifle = new ItemInstance("weapon_vx_rifle", 1, Rarity.Epic);
            rifleId = rifle.InstanceId;
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = rifle.ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("armor_scrap_vest").ToSnapshot() } }
            };
            return slot;
        }

        private (ExpeditionService service, AutosaveService autosave, Storage storage, ExpeditionTransactionRecorder recorder) StartRun(SaveSlot slot, SaveSlotService saves)
        {
            var storage = Storage.FromSnapshot(slot.Storage, Resolve, _ammoBalance);
            var autosave = new AutosaveService(slot, saves);
            autosave.BeforeSave += s => s.Storage = storage.ToSnapshot(); // what BaseSession does
            var service = new ExpeditionService(Resolve, ResolveAmmo, _ammoBalance);
            var recorder = new ExpeditionTransactionRecorder(service, slot, autosave);
            service.Start(slot.Profile, 11, Biome.RuinedMetro);
            return (service, autosave, storage, recorder);
        }

        [Test]
        public void SecuredItem_IsOnDisk_AndSurvivesDeath()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = SlotWithLoadout(out var rifleId);
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var (service, autosave, storage, recorder) = StartRun(slot, saves);
            using (recorder)
            {
                var result = Relay().Secure("p1", Carried(service.State.Inventory), rifleId, new SecureRelayStorageTarget(storage));
                Assert.IsTrue(result.Success, result.Refusal.ToString());
                Assert.AreEqual(SaveError.None, autosave.SaveNow("secure_relay"));

                var midRun = saves.Load();
                Assert.IsTrue(midRun.Success, midRun.Diagnostics.ToString());
                Assert.IsTrue(midRun.Slot.ActiveExpedition.IsOpen, "The run is still open; only the secured item moved.");
                var stored = midRun.Slot.Storage.Slots.Single(e => e.Item != null && e.Item.InstanceId == rifleId).Item;
                Assert.IsFalse(stored.IsAtRisk);
                Assert.AreEqual((int)Rarity.Epic, stored.Rarity);

                var summary = service.Fail(); // death / wipe
                Assert.IsFalse(summary.LostItems.Any(l => l.InstanceId == rifleId), "The secured item is not part of the loss.");
                Assert.IsTrue(summary.LostItems.Any(l => l.DefinitionId == "armor_scrap_vest"), "Everything else carried is lost as before.");
            }

            var after = saves.Load();
            Assert.IsTrue(after.Success, after.Diagnostics.ToString());
            Assert.IsFalse(after.Slot.ActiveExpedition.IsOpen);
            var reloaded = Storage.FromSnapshot(after.Slot.Storage, Resolve, _ammoBalance);
            Assert.IsNotNull(reloaded.Find(rifleId));
            Assert.AreEqual(1, reloaded.OccupiedSlots, "Exactly one item was secured, once.");
        }

        [Test]
        public void SecuredItem_SurvivesAnAbandonedRun_AndIsNeverDuplicated()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = SlotWithLoadout(out var rifleId);
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var (service, autosave, storage, recorder) = StartRun(slot, saves);
            using (recorder)
            {
                Assert.IsTrue(Relay().Secure("p1", Carried(service.State.Inventory), rifleId, new SecureRelayStorageTarget(storage)).Success);
                Assert.AreEqual(SaveError.None, autosave.SaveNow("secure_relay"));
            }
            // Quit / crash here: the next boot resolves the open run as failure.

            var boot = saves.Load();
            Assert.IsTrue(boot.Success, boot.Diagnostics.ToString());
            Assert.IsNotNull(AbandonedExpeditionResolver.Resolve(boot.Slot));
            Assert.AreEqual(SaveError.None, saves.Save(boot.Slot));
            var final = saves.Load();
            var reloaded = Storage.FromSnapshot(final.Slot.Storage, Resolve, _ammoBalance);
            Assert.IsNotNull(reloaded.Find(rifleId), "Secured means safe even when the run is abandoned.");
            Assert.IsTrue(final.Slot.Profile.SafeLoadout == null || final.Slot.Profile.SafeLoadout.Equipped.All(e => e.Item == null || e.Item.InstanceId != rifleId), "It exists once: in Storage.");
        }

        // ---- co-op member escrow: the unit is out of the run and on disk before the request; it resolves exactly once ----

        private sealed class DownedGate : MonoBehaviour, RuinRail.Gameplay.Combat.IPlayerActionGate
        {
            public bool CanAct => false;
        }

        [Test]
        public void Escrow_TakesOneUnitOutOfTheRun_ReleaseStoresIt_ReturnPutsItBackAtRisk()
        {
            var inventory = NewRunInventory();
            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            Assert.IsTrue(inventory.TryEquip(vest, EquippedSlot.Armor));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 3)));
            var bandages = inventory.BackpackSlots.First(i => i != null && i.DefinitionId == "consumable_bandage");
            var storage = NewStorage();
            var relay = Relay();

            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(relay, "p1", Carried(inventory), bandages.InstanceId, storage, out var unitEscrow));
            Assert.AreEqual(2, bandages.Quantity, "exactly one unit left the run");
            Assert.AreEqual(1, unitEscrow.Unit.Quantity);
            Assert.AreEqual(0, storage.OccupiedSlots, "nothing is stored before the verdict");
            Assert.IsTrue(SecureRelayEscrows.Release(unitEscrow, storage));
            Assert.AreEqual(1, storage.CountOf("consumable_bandage"));
            Assert.IsTrue(storage.Items.All(i => !i.IsAtRisk));

            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(relay, "p2", Carried(inventory), vest.InstanceId, storage, out var vestEscrow));
            Assert.IsNull(inventory.GetEquipped(EquippedSlot.Armor));
            Assert.AreEqual("equipped:Armor", vestEscrow.SourceContainerId);
            Assert.IsTrue(SecureRelayEscrows.Return(vestEscrow, Carried(inventory)), "a refusal puts it back");
            var back = inventory.GetEquipped(EquippedSlot.Armor);
            Assert.AreEqual(vest.InstanceId, back?.InstanceId, "into the slot it came from, same instance");
            Assert.IsTrue(back.IsAtRisk);
            Assert.AreEqual(Rarity.Rare, back.Rarity);

            var full = NewStorage();
            while (full.FreeSlots > 0) full.TryAdd(new ItemInstance("armor_scrap_vest"));
            Assert.AreEqual(SecureRelayRefusal.StorageFull, SecureRelayEscrows.Open(relay, "p3", Carried(inventory), vest.InstanceId, full, out var none));
            Assert.IsNull(none);
            Assert.IsNotNull(inventory.GetEquipped(EquippedSlot.Armor), "a refused escrow moves nothing");
        }

        [Test]
        public void Host_ReplaysAnAcceptedGrant_AfterAReconnect_EvenDownedOrOnAnotherDepth_AndNeverDecidesItAgain()
        {
            var loot = new LootAuthorityService(LocalAuthorityContext.Instance);
            var mirror = NewRunInventory();
            var rifle = new ItemInstance("weapon_vx_rifle");
            var vest = new ItemInstance("armor_scrap_vest");
            mirror.TryEquip(rifle, EquippedSlot.PrimaryWeapon);
            mirror.TryAddToBackpack(vest);
            var carried = Carried(mirror);
            loot.RegisterParticipant(new LootParticipant(1, "pA", carried[0], new CoinWallet(CoinDomain.Carried), null, carried));
            var relay = Relay();

            var accepted = loot.RequestRelaySecure("tx-1", 1, relay, rifle.InstanceId);
            Assert.AreEqual(LootVerdict.Accepted, accepted.Verdict);
            Assert.IsTrue(LootAuthorityService.IsFinalRelayVerdict(accepted.Verdict));

            // The link drops before the verdict arrives; the member comes back under a new client id, Downed, and the host
            // has meanwhile moved to another depth (no relay object for the request any more).
            var body = new GameObject("DownedMember");
            body.AddComponent<DownedGate>();
            try
            {
                loot.UnregisterParticipant(1);
                loot.RegisterParticipant(new LootParticipant(5, "pA", carried[0], new CoinWallet(CoinDomain.Carried), body, carried));
                var replay = loot.RequestRelaySecure("tx-1", 5, null, rifle.InstanceId);
                Assert.AreEqual(LootVerdict.Accepted, replay.Verdict, "the recorded grant is replayed, whatever changed since");
                Assert.AreEqual(1, replay.Quantity);
                Assert.AreEqual(1, relay.SecuredCount, "and nothing is decided twice");
                Assert.IsNotNull(mirror.BackpackSlots.FirstOrDefault(i => i == vest), "no second unit leaves the member's host copy");
                var downedNew = loot.RequestRelaySecure("tx-2", 5, relay, vest.InstanceId);
                Assert.AreEqual(LootVerdict.Rejected, downedNew.Verdict, "a Downed member's new request is a final refusal (its unit goes back into its run)");
                Assert.IsTrue(LootAuthorityService.IsFinalRelayVerdict(downedNew.Verdict));
            }
            finally
            {
                Object.DestroyImmediate(body);
            }

            loot.UnregisterParticipant(5);
            loot.RegisterParticipant(new LootParticipant(6, "pA", carried[0], new CoinWallet(CoinDomain.Carried), null, carried));
            var undecidable = loot.RequestRelaySecure("tx-4", 6, null, vest.InstanceId);
            Assert.IsFalse(LootAuthorityService.IsFinalRelayVerdict(undecidable.Verdict), "a request the host cannot place (no relay on this depth) is 'not now', never a refusal the member would act on");
            var second = loot.RequestRelaySecure("tx-3", 6, relay, vest.InstanceId);
            Assert.AreEqual(LootVerdict.AlreadyTaken, second.Verdict, "the member's one use stays spent across reconnects");
            Assert.IsTrue(LootAuthorityService.IsFinalRelayVerdict(second.Verdict));
        }

        // ---- the member's Shelter resolves a left-over escrow: never lost, never twice ----

        private BaseConfigs RealConfigs()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            return new BaseConfigs
            {
                Registry = ItemDefinitionRegistry.Build(catalog),
                AmmoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset"),
                Economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset"),
                Trader = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset"),
                Workshop = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset")
            };
        }

        /// <summary>A member mid-run with one Epic weapon escrowed (out of the run, on disk) and no verdict yet.</summary>
        private static (BaseSession session, SecureRelayEscrow escrow, string weaponId) MemberWithEscrow(BaseConfigs configs, MainMenuViewModel menu)
        {
            menu.Play();
            var session = menu.Session;
            var state = session.Expedition.Start(session.Profile, 11, Biome.RuinedMetro, 2, "member-pA");
            var weaponDef = configs.Registry.Definitions.OfType<WeaponDefinition>().First(w => w.Id != StarterKitService.PistolId && w.Id != StarterKitService.KnifeId);
            var weapon = new ItemInstance(weaponDef.Id, 1, Rarity.Epic);
            Assert.IsTrue(state.Inventory.TryAddToBackpack(weapon));
            var carried = new List<IItemContainer> { new BackpackContainer(state.Inventory) };
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot))) carried.Add(new EquippedSlotContainer(state.Inventory, slot));
            var relay = new SecureRelayEvent(new DungeonEventContext(11, 1, 4), configs.Resolve);
            Assert.AreEqual(SecureRelayRefusal.None, SecureRelayEscrows.Open(relay, "pA", carried, weapon.InstanceId, session.Storage, out var escrow));
            escrow.TransactionId = "tx-escrow";
            escrow.RunTransactionId = state.TransactionId;
            session.Slot.RelayEscrow.Add(escrow);
            Assert.AreEqual(SaveError.None, session.SaveNow("secure_relay_escrow"));
            Assert.IsFalse(state.Inventory.BackpackSlots.Contains(weapon), "the unit is out of the run");
            return (session, escrow, weapon.InstanceId);
        }

        private static bool InLoadout(SaveSlot slot, string instanceId)
        {
            var loadout = slot.Profile.SafeLoadout;
            return loadout != null && ((loadout.Equipped ?? new InventorySnapshot.Entry[0]).Any(e => e?.Item?.InstanceId == instanceId)
                                       || (loadout.Backpack ?? new InventorySnapshot.Entry[0]).Any(e => e?.Item?.InstanceId == instanceId));
        }

        private static int InStorage(SaveSlot slot, string instanceId) => slot.Storage.Slots.Count(e => e.Item != null && e.Item.InstanceId == instanceId);

        [Test]
        public void NeverReceivedByTheHost_ThenTheProcessDies_IsNeverSecured_ItIsLostWithTheAbandonedRun()
        {
            var configs = RealConfigs();
            var store = new MemorySaveStore();
            var (_, _, weaponId) = MemberWithEscrow(configs, new MainMenuViewModel(new SaveSlotService(store, configs.Resolve), configs));
            var onDisk = new SaveSlotService(store, configs.Resolve).Load();
            Assert.IsTrue(onDisk.Success, onDisk.Diagnostics.ToString());
            Assert.AreEqual(SecureRelayEscrowState.Pending, onDisk.Slot.RelayEscrow.Single().State, "unresolved is recorded as such — never as accepted");

            var reboot = new MainMenuViewModel(new SaveSlotService(store, configs.Resolve), configs);
            reboot.Play();
            Assert.IsNotNull(reboot.AbandonedExpedition);
            Assert.IsNull(reboot.Session.Storage.Find(weaponId), "no host acceptance: never secured by a restart");
            Assert.AreEqual(1, reboot.Session.RelayEscrowsLostWithAbandonedRun, "it shares the abandoned run's fate");
            Assert.AreEqual(0, reboot.Session.Slot.RelayEscrow.Count);
            reboot.Session.Dispose();

            var third = new SaveSlotService(store, configs.Resolve).Load();
            Assert.AreEqual(0, InStorage(third.Slot, weaponId));
            Assert.IsFalse(InLoadout(third.Slot, weaponId));
            Assert.AreEqual(0, third.Slot.RelayEscrow.Count, "and it does not come back on a later boot");
        }

        [Test]
        public void AcceptedButNotYetStored_ThenTheProcessDies_IsDeliveredToStorageOnce()
        {
            var configs = RealConfigs();
            var store = new MemorySaveStore();
            var (session, escrow, weaponId) = MemberWithEscrow(configs, new MainMenuViewModel(new SaveSlotService(store, configs.Resolve), configs));
            escrow.State = SecureRelayEscrowState.Accepted; // the host's acceptance reached the member and was saved
            Assert.AreEqual(SaveError.None, session.SaveNow("secure_relay_verdict"));

            var reboot = new MainMenuViewModel(new SaveSlotService(store, configs.Resolve), configs);
            reboot.Play();
            Assert.IsNotNull(reboot.Session.Storage.Find(weaponId), "an accepted transfer is delivered");
            Assert.IsFalse(reboot.Session.Storage.Find(weaponId).IsAtRisk);
            Assert.AreEqual(0, reboot.Session.RelayEscrowsLostWithAbandonedRun);
            reboot.Session.Dispose();
            var third = new SaveSlotService(store, configs.Resolve).Load();
            Assert.AreEqual(1, InStorage(third.Slot, weaponId), "exactly once");
            Assert.AreEqual(0, third.Slot.RelayEscrow.Count);
        }

        [TestCase(true, false, TestName = "RunEnds_Extracted_Unresolved_ComesHomeWithTheExtraction_NotSecured")]
        [TestCase(false, false, TestName = "RunEnds_Failed_Unresolved_IsLostWithTheRun_NotSecured")]
        [TestCase(true, true, TestName = "RunEnds_Extracted_Accepted_IsInStorageOnce")]
        [TestCase(false, true, TestName = "RunEnds_Failed_Accepted_IsInStorageOnce")]
        public void Escrow_WhenTheRunEnds_OnlyAnAcceptanceSecures_EverythingElseSharesTheRunsFate(bool extracted, bool accepted)
        {
            var configs = RealConfigs();
            var store = new MemorySaveStore();
            var menu = new MainMenuViewModel(new SaveSlotService(store, configs.Resolve), configs);
            var (session, escrow, weaponId) = MemberWithEscrow(configs, menu);
            if (accepted) escrow.State = SecureRelayEscrowState.Accepted;

            var summary = extracted ? session.Expedition.Return() : session.Expedition.Fail();
            var disk = new SaveSlotService(store, configs.Resolve).Load();
            Assert.IsTrue(disk.Success, disk.Diagnostics.ToString());
            Assert.IsFalse(disk.Slot.ActiveExpedition.IsOpen);
            Assert.AreEqual(0, disk.Slot.RelayEscrow.Count, "settled in the same save that closes the run");
            if (accepted)
            {
                Assert.AreEqual(1, InStorage(disk.Slot, weaponId), "accepted → Storage, once");
                Assert.IsFalse(InLoadout(disk.Slot, weaponId));
                Assert.IsFalse(summary.LostItems.Any(l => l.InstanceId == weaponId) || summary.SecuredItems.Any(l => l.InstanceId == weaponId), "never part of the run's own transaction");
            }
            else if (extracted)
            {
                Assert.AreEqual(0, InStorage(disk.Slot, weaponId), "unresolved: not secured by the relay");
                Assert.IsTrue(summary.SecuredItems.Any(l => l.InstanceId == weaponId), "it rejoined the run and came home with the extraction");
                Assert.IsTrue(InLoadout(disk.Slot, weaponId));
            }
            else
            {
                Assert.AreEqual(0, InStorage(disk.Slot, weaponId), "unresolved: not secured by the relay");
                Assert.IsTrue(summary.LostItems.Any(l => l.InstanceId == weaponId), "it rejoined the run and is lost with it, like everything carried");
                Assert.IsFalse(InLoadout(disk.Slot, weaponId));
            }

            session.Dispose();
        }
    }
}
