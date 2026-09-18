using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests.EditMode
{
    /// <summary>
    /// TASK 144 — persistence boundaries under adversarial sequences: extraction/failure replays, quits before and after
    /// the commit, interrupted atomic writes, newer/stale documents, migration replays, and storage transfer replays.
    /// Outcomes are recorded for the hardening report.
    /// </summary>
    public sealed class PersistenceHardeningTests
    {
        public const string ReportPath = "TestResults/hardening_editor.md";
        private static readonly StringBuilder Report = new();

        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        [TearDown]
        public void TearDown()
        {
            Directory.CreateDirectory("TestResults");
            File.WriteAllText(ReportPath, "# Editor adversarial cases (TASK 144)\n\n" + Report);
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
            slot.Profile.TotalXp = 600;
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare).ToSnapshot() } },
                Backpack = new[] { new InventorySnapshot.Entry { Slot = 0, Item = new ItemInstance("ammo_light", 40).ToSnapshot() } }
            };
            return slot;
        }

        [Test]
        public void ExtractionReplays_ReturnFailReturn_CommitOnce_XpOnce_ItemsOnce()
        {
            var store = new MemorySaveStore();
            var slot = SlotWithLoadout();
            var saves = new SaveSlotService(store, Resolve);
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var pistolId = slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            var service = NewService();
            using var recorder = new ExpeditionTransactionRecorder(service, slot, saves);
            var state = service.Start(slot.Profile, 21, Biome.RuinedMetro);
            service.AddCarriedCoins(300);
            var found = new ItemInstance("armor_scrap_vest", 1, Rarity.Epic);
            Assert.IsTrue(state.Inventory.TryAddToBackpack(found));
            service.AddXp(250);
            var writesBefore = store.WriteCount;

            var summary = service.Return();
            Assert.IsTrue(summary.IsSuccess);
            Assert.AreSame(summary, service.Return(), "Second Return: the closed summary is replayed, nothing re-committed.");
            var failReplay = service.Fail();
            Assert.AreSame(summary, failReplay, "Fail after Return replays the same closed (successful) summary — extraction cannot be turned into a loss.");
            Assert.IsTrue(failReplay.IsSuccess);
            Assert.AreSame(summary, service.Return());

            var onDisk = saves.Load();
            Assert.IsTrue(onDisk.Success);
            Assert.AreEqual(400, onDisk.Slot.Profile.BankedCoins, "Coins banked once.");
            Assert.AreEqual(850, onDisk.Slot.Profile.TotalXp, "XP once.");
            var securedIds = onDisk.Slot.Profile.SafeLoadout.Equipped.Concat(onDisk.Slot.Profile.SafeLoadout.Backpack).Select(e => e.Item.InstanceId).ToList();
            Assert.AreEqual(1, securedIds.Count(id => id == pistolId));
            Assert.AreEqual(1, securedIds.Count(id => id == found.InstanceId));
            Assert.AreEqual(securedIds.Count, securedIds.Distinct().Count());
            Assert.IsFalse(onDisk.Slot.ActiveExpedition.IsOpen);
            Assert.AreEqual(writesBefore + 1, store.WriteCount, "Exactly one commit write for the whole replay sequence.");
            Report.AppendLine("- Return → Return → Fail → Return replays: one commit write, coins/XP banked once, every secured instance id exactly once, marker closed: PASS");
        }

        [Test]
        public void Quit_BeforeCommit_LosesAtRisk_QuitAfterCommit_KeepsIt_AndBootResolutionIsIdempotent()
        {
            // Before the commit: the marker is open on disk; the crash resolves as failure exactly once.
            var store = new MemorySaveStore();
            var slot = SlotWithLoadout();
            var saves = new SaveSlotService(store, Resolve);
            saves.Save(slot);
            var pistolId = slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            var service = NewService();
            var recorder = new ExpeditionTransactionRecorder(service, slot, saves);
            service.Start(slot.Profile, 5, Biome.Rustworks);
            service.AddXp(100);
            recorder.Dispose(); // process dies here: nothing after the start transaction is on disk

            var boot = saves.Load();
            var report = AbandonedExpeditionResolver.Resolve(boot.Slot, new SaveDiagnostics());
            Assert.IsNotNull(report);
            CollectionAssert.Contains(report.LostInstanceIds, pistolId);
            Assert.AreEqual(600, boot.Slot.Profile.TotalXp, "Unsaved in-run XP is not on disk; permanent XP as last saved.");
            Assert.AreEqual(100, boot.Slot.Profile.BankedCoins);
            Assert.IsTrue(boot.Slot.Profile.SafeLoadout == null || boot.Slot.Profile.SafeLoadout.Equipped.Length == 0, "At-risk gear cannot be recovered by quitting.");
            saves.Save(boot.Slot);
            var second = saves.Load();
            Assert.IsNull(AbandonedExpeditionResolver.Resolve(second.Slot, new SaveDiagnostics()), "Boot again: nothing to resolve.");
            Assert.AreEqual(1, second.Slot.Profile.ExpeditionsEnded);

            // After the commit: the Return write closed the marker in the same document; a crash right after loses nothing.
            var store2 = new MemorySaveStore();
            var slot2 = SlotWithLoadout();
            var saves2 = new SaveSlotService(store2, Resolve);
            saves2.Save(slot2);
            var pistol2 = slot2.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            var service2 = NewService();
            using (new ExpeditionTransactionRecorder(service2, slot2, saves2))
            {
                service2.Start(slot2.Profile, 6, Biome.OvergrownLabs);
                service2.AddCarriedCoins(50);
                service2.Return();
            }

            var boot2 = saves2.Load();
            Assert.IsNull(AbandonedExpeditionResolver.Resolve(boot2.Slot, new SaveDiagnostics()));
            Assert.AreEqual(150, boot2.Slot.Profile.BankedCoins);
            Assert.AreEqual(pistol2, boot2.Slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId, "Secured gear survives a crash after the commit.");

            // Stale-document replay: an old document with an open marker restored over the committed one resolves as a failure
            // of that old transaction — it cannot resurrect or duplicate anything the committed document secured.
            var stale = store.Backup; // the pre-resolution document with the open marker
            Assert.IsNotNull(stale);
            store.Document = stale;
            var replay = saves.Load();
            var replayReport = AbandonedExpeditionResolver.Resolve(replay.Slot, new SaveDiagnostics());
            Assert.IsNotNull(replayReport);
            Assert.IsTrue(replay.Slot.Profile.SafeLoadout == null || replay.Slot.Profile.SafeLoadout.Equipped.Length == 0);
            Report.AppendLine("- Quit before commit: at-risk gear lost once, permanent state intact, second boot no-op; quit after commit: everything secured; stale open-marker document restored: resolved as failure, nothing duplicated: PASS");
        }

        [Test]
        public void AtomicWrite_Interrupted_NewerDocument_Migration_AreSafe()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = SlotWithLoadout();
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var good = store.Document;

            slot.Profile.BankedCoins = 999;
            store.FailNextWriteBeforeCommit = true;
            Assert.AreEqual(SaveError.WriteFailed, saves.Save(slot));
            Assert.AreEqual(good, store.Document, "Interrupted write: the committed document is untouched.");
            var loaded = saves.Load();
            Assert.IsTrue(loaded.Success);
            Assert.IsTrue(loaded.WasRecovered, "The complete temp is recovered deterministically (never silently, always reported)…");
            Assert.AreEqual(999, loaded.Slot.Profile.BankedCoins);
            Assert.AreEqual(good, store.Document, "…while the committed document itself was never touched.");
            store.Temp = null;
            Assert.AreEqual(100, saves.Load().Slot.Profile.BankedCoins, "Without the temp, the committed document loads.");

            // Newer schema: refused, never downgraded, never overwritten.
            var newer = good.Replace("\"SaveVersion\":" + SaveSlot.CurrentVersion, "\"SaveVersion\":" + (SaveSlot.CurrentVersion + 5));
            Assert.AreNotEqual(good, newer);
            store.Document = newer;
            var future = saves.Load();
            Assert.IsFalse(future.Success);
            Assert.AreEqual(newer, store.Document, "A newer document is never rewritten.");

            // Migration replay: migrating a legacy document twice yields the same result as once.
            var legacy = JsonUtility.ToJson(new PlayerProfile { DisplayName = "Old Runner", BankedCoins = 42, TotalXp = 120 });
            var pipeline = SaveMigrationPipeline.Default;
            var once = pipeline.Migrate(legacy, 1, new SaveDiagnostics());
            var twice = pipeline.Migrate(once, SaveDocumentInspector.DetectVersion(once, null), new SaveDiagnostics());
            Assert.AreEqual(once, twice, "Idempotent migration.");
            var migrated = JsonUtility.FromJson<SaveSlot>(once);
            Assert.AreEqual(42, migrated.Profile.BankedCoins);
            Assert.AreEqual("Old Runner", migrated.Profile.DisplayName);
            Report.AppendLine("- Interrupted atomic write keeps the previous document loadable; newer schema refused without overwrite; legacy migration idempotent: PASS");
        }

        [Test]
        public void StorageTransfers_DoubleDeposit_DoubleWithdraw_CrossContainerCopies_AreRefused()
        {
            var inventory = PlayerInventory.FromRegistry(_registry, _ammoBalance);
            var storage = Storage.FromSnapshot(new StorageSnapshot { Capacity = Storage.BaseCapacity, Slots = Array.Empty<InventorySnapshot.Entry>() }, Resolve, _ammoBalance);
            var service = new StorageService(storage);
            var backpack = new BackpackContainer(inventory);
            var vest = new ItemInstance("armor_scrap_vest", 1, Rarity.Rare);
            Assert.IsTrue(inventory.TryAddToBackpack(vest));

            Assert.IsTrue(service.Deposit(backpack, vest.InstanceId).Success);
            var replay = service.Deposit(backpack, vest.InstanceId);
            Assert.IsFalse(replay.Success, "Replayed deposit: the source no longer holds it.");
            Assert.AreEqual(1, storage.Items.Count(i => i.InstanceId == vest.InstanceId));
            Assert.IsFalse(inventory.Contains(vest.InstanceId));

            // A forged copy with the same instance id can never enter a second container.
            var forged = ItemInstance.FromSnapshot(vest.ToSnapshot());
            Assert.AreEqual(vest.InstanceId, forged.InstanceId);
            Assert.IsFalse(storage.TryAdd(forged), "Duplicate instance id refused by storage.");
            Assert.IsTrue(inventory.TryAddToBackpack(forged), "A container that does not hold the id accepts it…");
            CollectionAssert.AreEquivalent(new[] { vest.InstanceId }, ItemTransferService.DetectDuplicateOwnership(new IItemContainer[] { backpack, storage }), "…and the ownership audit flags the duplicate immediately.");
            for (var i = 0; i < inventory.BackpackSlots.Count; i++) if (inventory.BackpackSlots[i] != null && inventory.BackpackSlots[i].InstanceId == forged.InstanceId) { inventory.RemoveFromBackpack(i); break; }

            Assert.IsTrue(service.Withdraw(vest.InstanceId, backpack).Success);
            Assert.IsFalse(service.Withdraw(vest.InstanceId, backpack).Success, "Replayed withdraw: gone from storage.");
            Assert.AreEqual(1, inventory.BackpackSlots.Count(i => i != null && i.InstanceId == vest.InstanceId));
            Assert.AreEqual(0, storage.Items.Count());
            Assert.IsEmpty(ItemTransferService.DetectDuplicateOwnership(new IItemContainer[] { backpack, storage }));
            Report.AppendLine("- Storage deposit/withdraw replays refused; forged duplicate instance id refused by storage and flagged by the ownership audit: PASS");
        }
    }
}
