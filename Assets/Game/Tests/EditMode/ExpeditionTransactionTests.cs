using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Persistence;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 047 — at-risk transactions and quit/failure hardening.</summary>
    public class ExpeditionTransactionTests
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
                Def<TestItemDefinition>("armor_scrap_vest", ItemCategory.Armor),
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

        private ExpeditionService NewService()
        {
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            return new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
        }

        private static SaveSlot SlotWithLoadout()
        {
            var slot = SaveSlotService.CreateNew();
            slot.Profile.BankedCoins = 100;
            slot.Profile.TotalXp = 600; // level 3: exactly the 2 points spent below
            slot.Profile.Skills.Vitality = 2;
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare).ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
            };
            return slot;
        }

        private static IEnumerable<ItemInstance> Carried(PlayerInventory inventory)
        {
            foreach (EquippedSlot slot in System.Enum.GetValues(typeof(EquippedSlot)))
            {
                var e = inventory.GetEquipped(slot);
                if (e != null) yield return e;
            }

            foreach (var i in inventory.BackpackSlots) if (i != null) yield return i;
        }

        // ---- Requirement 1: everything carried, found or handed over is at-risk; identity unchanged ----

        [Test]
        public void Start_MarksCarriedAndAcquiredItemsAtRisk_WithoutChangingIdentity()
        {
            var service = NewService();
            var slot = SlotWithLoadout();
            var pistolId = slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            var state = service.Start(slot.Profile, 5, Biome.RuinedMetro);

            Assert.IsNotEmpty(state.TransactionId);
            Assert.AreEqual(pistolId, state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon).InstanceId, "Stable identity.");
            Assert.IsTrue(Carried(state.Inventory).All(i => i.IsAtRisk));
            Assert.IsNull(slot.Profile.SafeLoadout, "No safe copy remains.");

            // Found loot enters the carried inventory already at-risk, whichever path it takes.
            var found = new ItemInstance("armor_scrap_vest", 1, Rarity.Epic);
            Assert.IsFalse(found.IsAtRisk);
            Assert.IsTrue(state.Inventory.TryEquip(found, EquippedSlot.Armor));
            Assert.IsTrue(found.IsAtRisk);
            var bandages = new ItemInstance("consumable_bandage", 2);
            Assert.IsTrue(new BackpackContainer(state.Inventory).TryAdd(bandages));
            Assert.IsTrue(bandages.IsAtRisk);
            state.Inventory.Add(AmmoType.Light, 20);
            Assert.IsTrue(state.Inventory.BackpackSlots.Where(s => s != null).All(s => s.IsAtRisk));
            service.AddCarriedCoins(75);
            Assert.AreEqual(75, state.CarriedCoins);
            Assert.AreEqual(100, slot.Profile.BankedCoins, "Carried coins are their own at-risk wallet.");
        }

        // ---- Requirement 5: at-risk items can never reach Storage ----

        [Test]
        public void AtRiskItems_AreRefusedByStorage_ThroughEveryTransferPath()
        {
            var service = NewService();
            var slot = SlotWithLoadout();
            var state = service.Start(slot.Profile, 5, Biome.RuinedMetro);
            var storage = new Storage(Resolve, _ammoBalance);
            var transfers = new ItemTransferService();
            var pistol = state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon);
            var found = new ItemInstance("armor_scrap_vest");
            state.Inventory.TryAddToBackpack(found);

            var fromEquipped = transfers.Transfer(new EquippedSlotContainer(state.Inventory, EquippedSlot.PrimaryWeapon), pistol.InstanceId, storage);
            Assert.IsFalse(fromEquipped.Success);
            Assert.AreEqual(TransferError.DestinationRejected, fromEquipped.Error);
            var fromBackpack = transfers.Transfer(new BackpackContainer(state.Inventory), found.InstanceId, storage);
            Assert.IsFalse(fromBackpack.Success);
            Assert.IsFalse(storage.TryAdd(found), "Direct add is refused too.");
            Assert.AreEqual(0, storage.OccupiedSlots);
            Assert.AreSame(pistol, state.Inventory.GetEquipped(EquippedSlot.PrimaryWeapon), "Failed transfer leaves the source intact.");
            Assert.IsNotNull(state.Inventory.BackpackSlots.FirstOrDefault(s => s != null && s.InstanceId == found.InstanceId));

            // After extraction the very same instances are accepted.
            service.Return();
            var safe = new PlayerInventory(Resolve, t => null, _ammoBalance);
            safe.RestoreFromSnapshot(slot.Profile.SafeLoadout);
            var securedPistol = safe.GetEquipped(EquippedSlot.PrimaryWeapon);
            Assert.AreEqual(pistol.InstanceId, securedPistol.InstanceId);
            Assert.IsFalse(securedPistol.IsAtRisk);
            Assert.IsTrue(transfers.Transfer(new EquippedSlotContainer(safe, EquippedSlot.PrimaryWeapon), securedPistol.InstanceId, storage).Success);
        }

        // ---- Acceptance 1 + 4: duplicate callbacks commit/lose once; one domain owns an instance at a time ----

        [Test]
        public void DuplicateReturnCallbacks_CommitOnce_AndTheRiskDomainIsEmptiedOnCommit()
        {
            var service = NewService();
            var slot = SlotWithLoadout();
            var state = service.Start(slot.Profile, 5, Biome.RuinedMetro);
            service.AddCarriedCoins(200);
            var found = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            state.Inventory.TryAddToBackpack(found);
            var carriedIds = Carried(state.Inventory).Select(i => i.InstanceId).ToList();
            var ended = 0;
            service.ExpeditionEnded += _ => ended++;

            var first = service.Return();
            var second = service.Return();
            var third = service.Fail();

            Assert.AreSame(first, second);
            Assert.AreSame(first, third, "A late failure callback on a committed transaction cannot destroy anything.");
            Assert.AreEqual(1, ended);
            Assert.AreEqual(300, slot.Profile.BankedCoins, "Coins banked exactly once.");
            Assert.AreEqual(1, slot.Profile.ExpeditionsEnded);
            var safeIds = slot.Profile.SafeLoadout.Equipped.Select(e => e.Item.InstanceId).Concat(slot.Profile.SafeLoadout.Backpack.Select(e => e.Item.InstanceId)).ToList();
            CollectionAssert.AreEquivalent(carriedIds, safeIds);
            Assert.IsTrue(slot.Profile.SafeLoadout.Equipped.Concat(slot.Profile.SafeLoadout.Backpack).All(e => !e.Item.IsAtRisk));
            Assert.IsEmpty(Carried(state.Inventory), "The closed risk domain holds nothing: no instance lives in both domains.");
            Assert.IsFalse(state.Inventory.MarksIncomingAtRisk, "Closed inventory is no longer a risk domain.");
            Assert.IsFalse(service.IsExpeditionActive);
        }

        [Test]
        public void DuplicateFailCallbacks_LoseOnce_AndPermanentStateSurvives()
        {
            var service = NewService();
            var slot = SlotWithLoadout();
            var storage = new Storage(Resolve, _ammoBalance);
            var stored = new ItemInstance("weapon_p9_ranger", 1, Rarity.Epic);
            storage.TryAdd(stored);
            slot.Storage = storage.ToSnapshot();
            var state = service.Start(slot.Profile, 5, Biome.RuinedMetro);
            service.AddCarriedCoins(200);
            service.RecordEnemyDefeated(120);
            var ended = 0;
            service.ExpeditionEnded += _ => ended++;

            var first = service.Fail();
            var second = service.Fail();
            var third = service.Return();

            Assert.AreSame(first, second);
            Assert.AreSame(first, third, "A late success callback on a failed transaction cannot resurrect anything.");
            Assert.AreEqual(1, ended);
            Assert.AreEqual(ExpeditionOutcome.Failed, first.Outcome);
            Assert.AreEqual(2, first.LostItems.Count);
            Assert.AreEqual(200, first.CoinsLost);
            Assert.IsEmpty(Carried(state.Inventory));
            Assert.AreEqual(0, state.CarriedCoins);
            Assert.IsNull(slot.Profile.SafeLoadout);

            // Permanent domain untouched by the loss: XP (including this run's), skills, banked coins, storage.
            Assert.AreEqual(720, slot.Profile.TotalXp);
            Assert.AreEqual(2, slot.Profile.Skills.Vitality);
            Assert.AreEqual(100, slot.Profile.BankedCoins);
            Assert.AreEqual(stored.InstanceId, slot.Storage.Slots.Single().Item.InstanceId);
            Assert.AreEqual(1, slot.Profile.ExpeditionsEnded);
        }

        [Test]
        public void CallbacksWithNoExpeditionEverStarted_StillThrow()
        {
            var service = NewService();
            Assert.Throws<System.InvalidOperationException>(() => service.Return());
            Assert.Throws<System.InvalidOperationException>(() => service.Fail());
        }

        // ---- Acceptance 2 + 3: quit marker resolves as failure on next boot ----

        [Test]
        public void QuitDuringExpedition_ResolvesAsFailureOnNextBoot_ExactlyOnce()
        {
            var store = new MemorySaveStore();
            var slot = SlotWithLoadout();
            var saves = new SaveSlotService(store, Resolve);
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var pistolId = slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId;

            var service = NewService();
            using (new ExpeditionTransactionRecorder(service, slot, saves))
            {
                var state = service.Start(slot.Profile, 9, Biome.Rustworks);
                service.AddCarriedCoins(500);
                state.Inventory.TryAddToBackpack(new ItemInstance("armor_scrap_vest", 1, Rarity.Legendary));

                // The start transaction is on disk before anything else happens.
                Assert.AreEqual(2, store.WriteCount);
                var onDisk = saves.Load();
                Assert.IsTrue(onDisk.Success, onDisk.Diagnostics.ToString());
                Assert.IsTrue(onDisk.Slot.ActiveExpedition.IsOpen);
                Assert.AreEqual(state.TransactionId, onDisk.Slot.ActiveExpedition.TransactionId);
                Assert.AreEqual(9, onDisk.Slot.ActiveExpedition.RunSeed);
                CollectionAssert.Contains(onDisk.Slot.ActiveExpedition.AtRiskInstanceIds, pistolId);
                Assert.IsTrue(onDisk.Slot.Profile.SafeLoadout == null || onDisk.Slot.Profile.SafeLoadout.Equipped.Length == 0, "No at-risk gear is persisted anywhere.");
                Assert.IsFalse(onDisk.Slot.Storage.Slots.Any(s => s.Item.InstanceId == pistolId));
                Assert.AreEqual(100, onDisk.Slot.Profile.BankedCoins, "Carried coins are never persisted.");
            }
            // Alt+F4: the process ends here. Nothing else is written.

            // Next boot: load, resolve the open transaction as failure, save.
            var boot = saves.Load();
            Assert.IsTrue(boot.Success, boot.Diagnostics.ToString());
            var diagnostics = new SaveDiagnostics();
            var report = AbandonedExpeditionResolver.Resolve(boot.Slot, diagnostics);
            Assert.IsNotNull(report);
            Assert.AreEqual(9, report.RunSeed);
            Assert.AreEqual(1, report.ExpeditionIndex);
            CollectionAssert.Contains(report.LostInstanceIds, pistolId);
            Assert.IsFalse(boot.Slot.ActiveExpedition.IsOpen);
            Assert.AreEqual(1, boot.Slot.Profile.ExpeditionsEnded);
            Assert.AreEqual(100, boot.Slot.Profile.BankedCoins);
            Assert.AreEqual(600, boot.Slot.Profile.TotalXp, "Permanent XP as last saved survives.");
            Assert.AreEqual(2, boot.Slot.Profile.Skills.Vitality);
            Assert.IsTrue(boot.Slot.Profile.SafeLoadout == null || boot.Slot.Profile.SafeLoadout.Equipped.Length == 0);
            Assert.IsNull(AbandonedExpeditionResolver.Resolve(boot.Slot, diagnostics), "Second resolution is a no-op.");
            Assert.AreEqual(1, boot.Slot.Profile.ExpeditionsEnded);
            Assert.AreEqual(SaveError.None, saves.Save(boot.Slot));

            var afterBoot = saves.Load();
            Assert.IsFalse(afterBoot.Slot.ActiveExpedition.IsOpen);
            Assert.IsNull(AbandonedExpeditionResolver.Resolve(afterBoot.Slot));

            // A fresh expedition is startable again from a Starter Kit rescue, but not from the lost gear.
            var kit = new StarterKitService(Resolve, t => null, _ammoBalance);
            Assert.IsTrue(kit.NeedsRescueKit(afterBoot.Slot.Profile, Storage.FromSnapshot(afterBoot.Slot.Storage, Resolve, _ammoBalance)));
        }

        [Test]
        public void RecorderClearsTheMarkerInTheSameSaveAsTheCommit_AndIgnoresReplays()
        {
            var store = new MemorySaveStore();
            var slot = SlotWithLoadout();
            var saves = new SaveSlotService(store, Resolve);
            var service = NewService();
            using var recorder = new ExpeditionTransactionRecorder(service, slot, saves);

            var state = service.Start(slot.Profile, 3, Biome.OvergrownLabs);
            service.AddCarriedCoins(50);
            Assert.AreEqual(1, store.WriteCount);
            var duringRun = saves.Load().Slot;
            Assert.IsTrue(duringRun.ActiveExpedition.IsOpen);

            service.Return();
            Assert.AreEqual(2, store.WriteCount);
            var committed = saves.Load();
            Assert.IsTrue(committed.Success, committed.Diagnostics.ToString());
            Assert.IsFalse(committed.Slot.ActiveExpedition.IsOpen);
            Assert.AreEqual(150, committed.Slot.Profile.BankedCoins);
            Assert.AreEqual(state.Inventory.BackpackSlots.Count(s => s != null), 0);
            Assert.AreEqual(1, committed.Slot.Profile.SafeLoadout.Equipped.Length);
            Assert.IsNull(AbandonedExpeditionResolver.Resolve(committed.Slot), "Nothing to resolve after a clean commit.");

            service.Return();
            service.Fail();
            Assert.AreEqual(2, store.WriteCount, "Replayed callbacks write nothing.");
            Assert.AreEqual(SaveError.None, recorder.LastSaveError);
        }

        [Test]
        public void Recorder_RefusesAProfileThatIsNotTheSlotsProfile()
        {
            var slot = SlotWithLoadout();
            var saves = new SaveSlotService(new MemorySaveStore(), Resolve);
            var service = NewService();
            using var recorder = new ExpeditionTransactionRecorder(service, slot, saves);
            Assert.Throws<System.InvalidOperationException>(() => service.Start(new PlayerProfile(), 1, Biome.RuinedMetro));
        }
    }
}
