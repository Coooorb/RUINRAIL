using System.IO;
using System.Linq;
using NUnit.Framework;
using RuinRail.Core;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Economy;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Progression;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RuinRail.Tests
{
    /// <summary>TASK 048 — safe-point autosaves with atomic replacement and deterministic recovery.</summary>
    public class AutosaveTests
    {
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;
        private EconomyConfig _economy;
        private TraderConfig _traderConfig;
        private WorkshopConfig _workshopConfig;

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
            _economy = AssetDatabase.LoadAssetAtPath<EconomyConfig>("Assets/Game/ScriptableObjects/Balance/EconomyConfig.asset");
            _traderConfig = AssetDatabase.LoadAssetAtPath<TraderConfig>("Assets/Game/ScriptableObjects/Base/TraderConfig.asset");
            _workshopConfig = AssetDatabase.LoadAssetAtPath<WorkshopConfig>("Assets/Game/ScriptableObjects/Base/WorkshopConfig.asset");
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private static SaveSlot NewSlot(int coins, int xp)
        {
            var slot = SaveSlotService.CreateNew();
            slot.Profile.BankedCoins = coins;
            slot.Profile.TotalXp = xp;
            slot.Profile.ProfileSeed = 11;
            slot.Profile.SafeLoadout = new InventorySnapshot
            {
                Equipped = new[] { new InventorySnapshot.Entry { Slot = (int)EquippedSlot.PrimaryWeapon, Item = new ItemInstance("weapon_p9_ranger").ToSnapshot() } },
                Backpack = new InventorySnapshot.Entry[0]
            };
            return slot;
        }

        // ---- Acceptance 1: interrupted write leaves the previous valid save loadable ----

        [Test]
        public void InterruptedWrite_BeforeCommit_LeavesPreviousSaveLoadable_AndReportsFailure()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = NewSlot(100, 0);
            Assert.AreEqual(SaveError.None, saves.Save(slot));
            var committed = store.Document;

            slot.Profile.BankedCoins = 999;
            store.FailNextWriteBeforeCommit = true;
            var diagnostics = new SaveDiagnostics();
            Assert.AreEqual(SaveError.WriteFailed, saves.Save(slot, diagnostics));
            Assert.IsTrue(diagnostics.Entries.Any(e => e.Code == "save.write"));
            Assert.AreEqual(committed, store.Document, "Committed document untouched by the interrupted write.");
            Assert.IsNotNull(store.Temp, "The complete temp is left next to it.");

            // Memory-store semantics: a complete temp is the newest committed intention and is recovered deterministically.
            var loaded = saves.Load();
            Assert.IsTrue(loaded.Success, loaded.Diagnostics.ToString());
            Assert.AreEqual(SaveCandidateNames.Temp, loaded.Source);
            Assert.IsTrue(loaded.WasRecovered);
            Assert.AreEqual(999, loaded.Slot.Profile.BankedCoins);

            // With the temp truncated (a real partial write) the previous valid save is chosen, never the newer partial file.
            store.Temp = store.Temp.Substring(0, store.Temp.Length / 2);
            var recovered = saves.Load();
            Assert.IsTrue(recovered.Success, recovered.Diagnostics.ToString());
            Assert.AreEqual(SaveCandidateNames.Current, recovered.Source);
            Assert.IsFalse(recovered.WasRecovered);
            Assert.AreEqual(100, recovered.Slot.Profile.BankedCoins);
            Assert.IsTrue(recovered.Diagnostics.Entries.Any(e => e.Code == "load.parse" || e.Code == "load.corrupt"), "The rejected temp is diagnosed.");
        }

        // ---- Acceptance 2: corrupt temp/current fixtures pick the right last-known-good ----

        [Test]
        public void CorruptCurrent_FallsBackToBackup_AndCorruptEverything_FailsWithoutWriting()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            Assert.AreEqual(SaveError.None, saves.Save(NewSlot(1, 0)));
            Assert.AreEqual(SaveError.None, saves.Save(NewSlot(2, 0)));
            Assert.IsNotNull(store.Backup);

            store.Document = "{\"SaveVersion\":2,\"Profile\":{\"Version\":1,\"BankedCoins\":-1}}"; // parses, fails validation
            var fromBackup = saves.Load();
            Assert.IsTrue(fromBackup.Success, fromBackup.Diagnostics.ToString());
            Assert.AreEqual(SaveCandidateNames.Backup, fromBackup.Source);
            Assert.AreEqual(1, fromBackup.Slot.Profile.BankedCoins);
            Assert.IsTrue(fromBackup.Diagnostics.Entries.Any(e => e.Code == "load.recovered" && e.Severity == SaveSeverity.Warning));

            store.Temp = "{ partial";
            var stillBackup = saves.Load();
            Assert.AreEqual(SaveCandidateNames.Backup, stillBackup.Source, "A corrupt temp is skipped, not chosen for being newest.");

            store.Backup = "";
            var nothing = saves.Load();
            Assert.IsFalse(nothing.Success);
            Assert.AreEqual(0, store.WriteCount - 2, "A failed load never writes.");
            Assert.IsTrue(nothing.Diagnostics.Entries.Any(e => e.Code == "load.failed"));

            // Re-committing after a recovery normalises the store: the next load is the current document again.
            Assert.AreEqual(SaveError.None, saves.Save(fromBackup.Slot));
            Assert.AreEqual(SaveCandidateNames.Current, saves.Load().Source);
        }

        [Test]
        public void FileStore_RecoversFromCompleteTemp_AndFromCorruptCurrent_Deterministically()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ruinrail_autosave_" + System.Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, SaveSlotService.SlotFileName);
            try
            {
                var store = new FileSaveStore(path);
                var saves = new SaveSlotService(store, Resolve);
                Assert.AreEqual(SaveError.None, saves.Save(NewSlot(10, 0)));
                Assert.AreEqual(SaveError.None, saves.Save(NewSlot(20, 0)));
                Assert.IsTrue(File.Exists(store.BackupPath));
                Assert.IsFalse(File.Exists(store.TempPath));

                // Crash after the temp was fully written but before the swap: the temp is complete and wins.
                var complete = SaveSlotService.Serialize(NewSlot(30, 0));
                File.WriteAllText(store.TempPath, complete);
                File.SetLastWriteTimeUtc(store.TempPath, System.DateTime.UtcNow.AddDays(-2)); // timestamps are irrelevant
                var fromTemp = saves.Load();
                Assert.AreEqual(SaveCandidateNames.Temp, fromTemp.Source);
                Assert.AreEqual(30, fromTemp.Slot.Profile.BankedCoins);

                // A truncated temp is ignored no matter how new it is.
                File.WriteAllText(store.TempPath, complete.Substring(0, complete.Length - 40));
                File.SetLastWriteTimeUtc(store.TempPath, System.DateTime.UtcNow.AddDays(1));
                var fromCurrent = saves.Load();
                Assert.AreEqual(SaveCandidateNames.Current, fromCurrent.Source);
                Assert.AreEqual(20, fromCurrent.Slot.Profile.BankedCoins);

                // Current corrupted too: the backup is the last known good.
                File.WriteAllText(path, "garbage");
                var fromBackup = saves.Load();
                Assert.AreEqual(SaveCandidateNames.Backup, fromBackup.Source);
                Assert.AreEqual(10, fromBackup.Slot.Profile.BankedCoins);
                Assert.AreEqual("garbage", File.ReadAllText(path), "Recovery never rewrites files by itself.");

                // The next commit swaps in a good current and leaves no temp behind.
                Assert.AreEqual(SaveError.None, saves.Save(fromBackup.Slot));
                Assert.IsFalse(File.Exists(store.TempPath));
                Assert.AreEqual(SaveCandidateNames.Current, saves.Load().Source);
                Assert.AreEqual("garbage", File.ReadAllText(store.BackupPath), "Previous current becomes the backup, whatever it held.");
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        // ---- Acceptance 3: one write per approved mutation set, nothing while clean ----

        [Test]
        public void SafePoints_MarkDirty_AndFlushWritesOnce_NothingWhileClean()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = NewSlot(20000, 0);
            var autosave = new AutosaveService(slot, saves);
            var banked = new CoinWallet(CoinDomain.Banked, () => slot.Profile.BankedCoins, v => slot.Profile.BankedCoins = v);
            var storage = Storage.FromSnapshot(slot.Storage, Resolve, _ammoBalance);
            var trader = new TraderService(_traderConfig, new PriceService(_economy), banked, slot.Profile.Trader, slot.Profile.ProfileSeed, _registry.Definitions, Resolve);
            var workshop = new WorkshopService(_workshopConfig, banked, slot.Profile.Workshop, storage, trader) { IsAtBase = true };
            var progression = new ProgressionService(slot.Profile) { IsAtBase = true };
            autosave.BeforeSave += s => s.Storage = storage.ToSnapshot();
            using var binder = new AutosaveBinder(autosave, storage, trader, workshop, progression, banked);

            // Idle frames: nothing pending, nothing written.
            for (var frame = 0; frame < 100; frame++) Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual(0, store.WriteCount);
            Assert.IsFalse(autosave.IsDirty);

            // One trader purchase = coins + storage in the same frame = one write.
            trader.RefreshForEndedExpedition(1);
            var offer = trader.Offers.First(o => !o.IsSold);
            Assert.AreEqual(TradeError.None, trader.Buy(offer.Index, storage));
            Assert.IsTrue(autosave.IsDirty);
            CollectionAssert.Contains(autosave.PendingReasons, "banked_coins");
            CollectionAssert.Contains(autosave.PendingReasons, "storage");
            Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual(1, store.WriteCount);
            Assert.IsFalse(autosave.IsDirty);
            Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual(1, store.WriteCount, "Clean again: no second write.");
            var reloaded = saves.Load();
            Assert.AreEqual(slot.Profile.BankedCoins, reloaded.Slot.Profile.BankedCoins);
            Assert.AreEqual(1, reloaded.Slot.Storage.Slots.Length, "BeforeSave synced live storage into the slot.");

            // Workshop upgrade and skill spend are safe points too.
            Assert.AreEqual(UpgradeError.None, workshop.BuyStorageUpgrade());
            Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual(2, store.WriteCount);
            slot.Profile.TotalXp = 300;
            progression.ReconcilePoints();
            Assert.AreEqual(SkillSpendError.None, progression.TrySpend(SkillId.Vitality));
            Assert.AreEqual(SkillSpendError.NoUnspentPoints, progression.TrySpend(SkillId.Vitality));
            CollectionAssert.AreEqual(new[] { "skills" }, autosave.PendingReasons, "Only the successful spend marks the slot.");
            Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual(3, store.WriteCount);
            Assert.AreEqual(1, saves.Load().Slot.Profile.Skills.Vitality);
            Assert.IsTrue(autosave.LastStatus.Succeeded);
            Assert.AreEqual("skills", autosave.LastStatus.Reason);

            binder.Dispose();
            banked.Credit(5, "after_unbind");
            Assert.IsFalse(autosave.IsDirty);
        }

        [Test]
        public void ExpeditionCommits_ThroughAutosave_WriteImmediately_AndFoldPendingSafePoints()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = NewSlot(100, 0);
            var autosave = new AutosaveService(slot, saves);
            var ammoByType = _registry.Definitions.OfType<AmmoItemDefinition>().ToDictionary(a => a.AmmoType, a => a);
            var service = new ExpeditionService(Resolve, t => ammoByType.TryGetValue(t, out var a) ? a : null, _ammoBalance);
            using var recorder = new ExpeditionTransactionRecorder(service, slot, autosave);

            autosave.MarkDirty("storage");
            service.Start(slot.Profile, 4, Biome.RuinedMetro);
            Assert.AreEqual(1, store.WriteCount, "Start commits at once, folding the pending safe point.");
            Assert.IsFalse(autosave.IsDirty);
            Assert.AreEqual("storage+expedition_start", autosave.LastStatus.Reason);
            Assert.IsTrue(saves.Load().Slot.ActiveExpedition.IsOpen);

            service.AddCarriedCoins(40);
            service.RecordEnemyDefeated(10);
            Assert.AreEqual(1, store.WriteCount, "No mid-run autosave: nothing resumable is ever written.");

            service.Return();
            Assert.AreEqual(2, store.WriteCount);
            Assert.AreEqual("expedition_extracted", autosave.LastStatus.Reason);
            var committed = saves.Load().Slot;
            Assert.IsFalse(committed.ActiveExpedition.IsOpen);
            Assert.AreEqual(140, committed.Profile.BankedCoins);
            Assert.AreEqual(10, committed.Profile.TotalXp);
            service.Return();
            Assert.AreEqual(2, store.WriteCount, "Replayed commit: no write.");
        }

        // ---- Requirement 5: failures are surfaced, never swallowed ----

        [Test]
        public void SaveFailure_IsReported_KeepsTheSlotDirty_AndRetriesAtTheNextSafePoint()
        {
            var store = new MemorySaveStore();
            var saves = new SaveSlotService(store, Resolve);
            var slot = NewSlot(5, 0);
            var autosave = new AutosaveService(slot, saves);
            SaveStatus? failed = null;
            autosave.SaveFailed += s => failed = s;

            store.FailNextWriteBeforeCommit = true;
            autosave.MarkDirty("storage");
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Autosave failed"));
            Assert.AreEqual(SaveError.WriteFailed, autosave.Flush());
            Assert.IsTrue(failed.HasValue);
            Assert.AreEqual(SaveError.WriteFailed, failed.Value.Error);
            Assert.IsFalse(autosave.LastStatus.Succeeded);
            Assert.AreEqual(1, autosave.FailedSaveCount);
            Assert.IsTrue(autosave.IsDirty, "Still dirty: nothing claimed success.");
            Assert.AreEqual(0, store.WriteCount);

            autosave.MarkDirty("banked_coins");
            Assert.AreEqual(SaveError.None, autosave.Flush());
            Assert.AreEqual("storage+banked_coins", autosave.LastStatus.Reason);
            Assert.AreEqual(1, store.WriteCount);
            Assert.IsFalse(autosave.IsDirty);
        }

        // ---- Acceptance 4: nothing resumable ----

        [Test]
        public void NoMidExpeditionResumeData_IsIntroduced()
        {
            var fields = typeof(SaveSlot).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(f => f.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "SaveVersion", "Profile", "Storage", "FirstLaunch", "ActiveExpedition", "Quarantine" }, fields);
            var markerFields = typeof(ExpeditionMarker).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(f => f.Name).ToList();
            CollectionAssert.AreEquivalent(new[] { "TransactionId", "RunSeed", "StartedUtcTicks", "AtRiskInstanceIds" }, markerFields, "The marker records the open transaction only; no depth, inventory, coins or HP.");
        }
    }
}
