using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RuinRail.Gameplay.Base;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using RuinRail.Persistence;
using UnityEditor;
using UnityEngine;

namespace RuinRail.Tests
{
    /// <summary>TASK 046 — one-slot save schema, SaveVersion dispatch and migrations.</summary>
    public class SaveSlotTests
    {
        private ItemDefinitionRegistry _registry;
        private AmmoBalanceConfig _ammoBalance;

        // A version-1 document exactly as the pre-envelope builds wrote it (bare PlayerProfile, no ExpeditionsEnded field).
        private const string LegacyV1Fixture =
            "{\"Version\":1,\"DisplayName\":\"Old Runner\",\"ProfileSeed\":4242,\"TotalXp\":1234,\"BankedCoins\":765,\"UnspentSkillPoints\":2," +
            "\"Skills\":{\"Vitality\":1,\"Power\":2,\"Mobility\":0,\"Recovery\":0,\"Handling\":0,\"Resilience\":0}," +
            "\"Trader\":{\"Level\":2,\"RefreshCount\":3,\"LastRefreshedExpedition\":2,\"SoldOfferIndices\":[1]}," +
            "\"Workshop\":{\"StorageTier\":1},\"StarterKitGranted\":true," +
            "\"SafeLoadout\":{\"Equipped\":[{\"Slot\":0,\"Item\":{\"InstanceId\":\"abc-1\",\"DefinitionId\":\"weapon_p9_ranger\",\"Rarity\":2,\"AffixRolls\":[{\"AffixId\":\"affix_damage\",\"Value\":8}],\"Quantity\":1,\"IsAtRisk\":false,\"IsUnsellable\":false}}]," +
            "\"Backpack\":[{\"Slot\":0,\"Item\":{\"InstanceId\":\"abc-2\",\"DefinitionId\":\"ammo_light\",\"Rarity\":0,\"AffixRolls\":[],\"Quantity\":40,\"IsAtRisk\":false,\"IsUnsellable\":false}}]}}";

        [SetUp]
        public void SetUp()
        {
            var catalog = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).Where(d => d != null).ToList();
            _registry = ItemDefinitionRegistry.Build(catalog);
            _ammoBalance = AssetDatabase.LoadAssetAtPath<AmmoBalanceConfig>("Assets/Game/ScriptableObjects/Items/AmmoBalanceConfig.asset");
        }

        private ItemDefinition Resolve(string id) => _registry.TryGet(id, out var d) ? d : null;

        private SaveSlotService Service(ISaveStore store) => new(store, Resolve);

        private SaveSlot BuildPopulatedSlot()
        {
            var slot = SaveSlotService.CreateNew();
            slot.Profile.DisplayName = "Rail Ghost";
            slot.Profile.ProfileSeed = 99;
            slot.Profile.TotalXp = 5000;
            slot.Profile.BankedCoins = 3210;
            slot.Profile.UnspentSkillPoints = 1;
            slot.Profile.Skills.Vitality = 3;
            slot.Profile.Skills.Handling = 2;
            slot.Profile.Trader.Level = 2;
            slot.Profile.Trader.RefreshCount = 7;
            slot.Profile.Trader.SoldOfferIndices.AddRange(new[] { 0, 3 });
            slot.Profile.Workshop.StorageTier = 2;
            slot.Profile.StarterKitGranted = true;
            slot.Profile.ExpeditionsEnded = 9;
            slot.FirstLaunch.DisplayNameConfirmed = true;

            var inventory = new PlayerInventory(Resolve, t => _registry.Definitions.OfType<AmmoItemDefinition>().FirstOrDefault(a => a.AmmoType == t), _ammoBalance);
            var rifle = new ItemInstance("weapon_ar_17", 1, Rarity.Epic);
            rifle.AddAffixRoll(new AffixRoll("affix_damage", 9));
            rifle.AddAffixRoll(new AffixRoll("affix_fire_rate", 7));
            rifle.AddAffixRoll(new AffixRoll("affix_range", 12));
            Assert.IsTrue(inventory.TryEquip(rifle, EquippedSlot.PrimaryWeapon));
            Assert.IsTrue(inventory.TryEquip(new ItemInstance("armor_scrap_vest", 1, Rarity.Common) { IsUnsellable = true }, EquippedSlot.Armor));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("ammo_light", 120)));
            Assert.IsTrue(inventory.TryAddToBackpack(new ItemInstance("consumable_bandage", 2)));
            slot.Profile.SafeLoadout = inventory.ToSnapshot();

            var storage = new Storage(Resolve, _ammoBalance);
            Assert.IsTrue(storage.ExpandTo(100));
            var stored = new ItemInstance("weapon_p9_ranger", 1, Rarity.Rare);
            stored.AddAffixRoll(new AffixRoll("affix_reload_speed", 11));
            Assert.IsTrue(storage.TryAdd(stored));
            Assert.IsTrue(storage.TryAdd(new ItemInstance("ammo_heavy", 30)));
            slot.Storage = storage.ToSnapshot();
            return slot;
        }

        // ---- Acceptance 1: exact round trip ----

        [Test]
        public void CurrentSlot_RoundTripsAllPermanentStateExactly()
        {
            var store = new MemorySaveStore();
            var service = Service(store);
            var slot = BuildPopulatedSlot();
            var expected = SaveSlotService.Serialize(slot);

            Assert.AreEqual(SaveError.None, service.Save(slot));
            Assert.AreEqual(1, store.WriteCount);
            Assert.AreEqual(SaveSlot.CurrentVersion, SaveDocumentInspector.DetectVersion(store.Document, null));

            var loaded = service.Load();
            Assert.IsTrue(loaded.Success, loaded.Diagnostics.ToString());
            Assert.IsFalse(loaded.WasMigrated);
            Assert.IsFalse(loaded.Diagnostics.HasWarnings, loaded.Diagnostics.ToString());
            Assert.AreEqual(expected, SaveSlotService.Serialize(loaded.Slot), "Byte-identical re-serialization.");

            var p = loaded.Slot.Profile;
            Assert.AreEqual("Rail Ghost", p.DisplayName);
            Assert.AreEqual(5000, p.TotalXp);
            Assert.AreEqual(3210, p.BankedCoins);
            Assert.AreEqual(1, p.UnspentSkillPoints);
            Assert.AreEqual(3, p.Skills.Vitality);
            Assert.AreEqual(2, p.Skills.Handling);
            Assert.AreEqual(2, p.Trader.Level);
            CollectionAssert.AreEqual(new[] { 0, 3 }, p.Trader.SoldOfferIndices);
            Assert.AreEqual(2, p.Workshop.StorageTier);
            Assert.IsTrue(p.StarterKitGranted);
            Assert.AreEqual(9, p.ExpeditionsEnded);
            Assert.IsTrue(loaded.Slot.FirstLaunch.DisplayNameConfirmed);
            Assert.AreEqual(100, loaded.Slot.Storage.Capacity);

            // Item instances: ids, definition ids, rarity and integer affix rolls survive; no Unity references anywhere.
            var rifle = p.SafeLoadout.Equipped.Single(e => e.Slot == (int)EquippedSlot.PrimaryWeapon).Item;
            Assert.AreEqual("weapon_ar_17", rifle.DefinitionId);
            Assert.AreEqual((int)Rarity.Epic, rifle.Rarity);
            CollectionAssert.AreEqual(new[] { ("affix_damage", 9), ("affix_fire_rate", 7), ("affix_range", 12) }, rifle.AffixRolls.Select(r => (r.AffixId, r.Value)));
            Assert.IsTrue(p.SafeLoadout.Equipped.Single(e => e.Slot == (int)EquippedSlot.Armor).Item.IsUnsellable);
            Assert.AreEqual(120, p.SafeLoadout.Backpack.Single(e => e.Item.DefinitionId == "ammo_light").Item.Quantity);
            var stored = loaded.Slot.Storage.Slots.Single(e => e.Item.DefinitionId == "weapon_p9_ranger").Item;
            Assert.AreEqual((int)Rarity.Rare, stored.Rarity);
            Assert.AreEqual(11, stored.AffixRolls[0].Value);

            var restoredStorage = Storage.FromSnapshot(loaded.Slot.Storage, Resolve, _ammoBalance);
            Assert.AreEqual(2, restoredStorage.OccupiedSlots);
            Assert.IsFalse(store.Document.Contains("m_FileID"), "No Unity object references are serialized.");
            Assert.IsFalse(store.Document.Contains("instanceID"));
        }

        // ---- Acceptance 2: legacy fixture migrates deterministically ----

        [Test]
        public void LegacyV1Fixture_MigratesStepwiseToCurrent_KeepingEveryValue()
        {
            var store = new MemorySaveStore { Document = LegacyV1Fixture };
            var service = Service(store);
            Assert.AreEqual(1, SaveDocumentInspector.DetectVersion(LegacyV1Fixture, null));

            var first = service.Load();
            Assert.IsTrue(first.Success, first.Diagnostics.ToString());
            Assert.IsTrue(first.WasMigrated);
            Assert.AreEqual(1, first.SourceVersion);
            Assert.AreEqual(SaveSlot.CurrentVersion, first.Slot.SaveVersion);
            Assert.AreEqual(LegacyV1Fixture, store.Document, "Loading never rewrites the source document.");
            Assert.AreEqual(0, store.WriteCount);

            var p = first.Slot.Profile;
            Assert.AreEqual("Old Runner", p.DisplayName);
            Assert.AreEqual(4242, p.ProfileSeed);
            Assert.AreEqual(1234, p.TotalXp);
            Assert.AreEqual(765, p.BankedCoins);
            Assert.AreEqual(2, p.UnspentSkillPoints);
            Assert.AreEqual(1, p.Skills.Vitality);
            Assert.AreEqual(2, p.Skills.Power);
            Assert.AreEqual(2, p.Trader.Level);
            Assert.AreEqual(3, p.Trader.RefreshCount);
            CollectionAssert.AreEqual(new[] { 1 }, p.Trader.SoldOfferIndices);
            Assert.AreEqual(1, p.Workshop.StorageTier);
            Assert.IsTrue(p.StarterKitGranted);
            Assert.AreEqual(0, p.ExpeditionsEnded, "New field defaults; nothing invented.");
            Assert.AreEqual("abc-1", p.SafeLoadout.Equipped[0].Item.InstanceId);
            Assert.AreEqual(8, p.SafeLoadout.Equipped[0].Item.AffixRolls[0].Value);
            Assert.AreEqual(40, p.SafeLoadout.Backpack[0].Item.Quantity);
            Assert.AreEqual(Storage.BaseCapacity, first.Slot.Storage.Capacity);
            Assert.IsEmpty(first.Slot.Storage.Slots);
            Assert.IsTrue(first.Slot.FirstLaunch.DisplayNameConfirmed, "A named legacy profile is not asked for a name again.");

            // Deterministic: a second migration of the same fixture yields the identical document.
            var second = Service(new MemorySaveStore { Document = LegacyV1Fixture }).Load();
            Assert.AreEqual(SaveSlotService.Serialize(first.Slot), SaveSlotService.Serialize(second.Slot));

            // Committing after a migrated load keeps the original as a versioned backup, then writes current schema.
            Assert.AreEqual(SaveError.None, service.Save(first.Slot));
            Assert.AreEqual(LegacyV1Fixture, store.LastBackup);
            Assert.AreEqual(SaveSlot.CurrentVersion, SaveDocumentInspector.DetectVersion(store.Document, null));
            var reloaded = service.Load();
            Assert.IsTrue(reloaded.Success);
            Assert.IsFalse(reloaded.WasMigrated);
            Assert.AreEqual(1234, reloaded.Slot.Profile.TotalXp);
        }

        // ---- Acceptance 3: unsupported / newer / corrupt fail safely without touching the source ----

        [Test]
        public void NewerCorruptAndInvalidDocuments_FailWithDiagnostics_AndNeverOverwriteTheSource()
        {
            var newer = "{\"SaveVersion\":99,\"Profile\":{\"Version\":1,\"TotalXp\":10}}";
            AssertFailsUntouched(newer, SaveError.UnsupportedVersion, "load.newer");

            AssertFailsUntouched("{ this is not json", SaveError.Corrupt, "load.corrupt");
            AssertFailsUntouched("{\"Hello\":1}", SaveError.Corrupt, "load.corrupt");
            AssertFailsUntouched("{\"SaveVersion\":0,\"Version\":1}", SaveError.Corrupt, "load.corrupt");

            var negativeCoins = SaveSlotService.Serialize(BuildPopulatedSlot()).Replace("\"BankedCoins\":3210", "\"BankedCoins\":-5");
            AssertFailsUntouched(negativeCoins, SaveError.InvalidMandatoryData, "profile.coins");

            var populated = BuildPopulatedSlot();
            var dupId = populated.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            populated.Storage.Slots[0].Item.InstanceId = dupId;
            AssertFailsUntouched(SaveSlotService.Serialize(populated), SaveError.DuplicateInstanceIds, "item.duplicate");

            // Saving an invalid slot is refused before any write.
            var store = new MemorySaveStore { Document = "original" };
            var diagnostics = new SaveDiagnostics();
            Assert.AreEqual(SaveError.DuplicateInstanceIds, Service(store).Save(populated, diagnostics));
            Assert.AreEqual("original", store.Document);
            Assert.AreEqual(0, store.WriteCount);
            Assert.IsTrue(diagnostics.HasErrors);

            // Missing file: not an error condition, but no slot either.
            var empty = Service(new MemorySaveStore()).Load();
            Assert.IsFalse(empty.Success);
            Assert.AreEqual(SaveError.NoSave, empty.Error);
        }

        private void AssertFailsUntouched(string document, SaveError expected, string code)
        {
            var store = new MemorySaveStore { Document = document };
            var result = Service(store).Load();
            Assert.IsFalse(result.Success, document);
            Assert.AreEqual(expected, result.Error, result.Diagnostics.ToString());
            Assert.IsNull(result.Slot);
            Assert.IsTrue(result.Diagnostics.Entries.Any(e => e.Code == code && e.Severity == SaveSeverity.Error), result.Diagnostics.ToString());
            Assert.AreEqual(document, store.Document, "Source untouched.");
            Assert.AreEqual(0, store.WriteCount);
        }

        [Test]
        public void UnresolvedDefinitions_AreQuarantined_NotReplaced_AndSurviveTheNextSave()
        {
            var slot = BuildPopulatedSlot();
            var ghostId = slot.Profile.SafeLoadout.Equipped[0].Item.InstanceId;
            slot.Profile.SafeLoadout.Equipped[0].Item.DefinitionId = "weapon_removed_from_build";
            var storedGhost = slot.Storage.Slots[1].Item;
            storedGhost.DefinitionId = "ammo_unknown";
            var store = new MemorySaveStore { Document = SaveSlotService.Serialize(slot) };
            var service = Service(store);

            var loaded = service.Load();
            Assert.IsTrue(loaded.Success, loaded.Diagnostics.ToString());
            Assert.IsTrue(loaded.Diagnostics.HasWarnings);
            Assert.AreEqual(2, loaded.Diagnostics.Entries.Count(e => e.Code == "item.unresolved"));
            Assert.AreEqual(2, loaded.Slot.Quarantine.Count);
            Assert.IsFalse(loaded.Slot.Profile.SafeLoadout.Equipped.Any(e => e.Item.InstanceId == ghostId), "Unresolved item not loaded into play.");
            Assert.IsFalse(loaded.Slot.Storage.Slots.Any(e => e.Item.InstanceId == storedGhost.InstanceId));
            var q = loaded.Slot.Quarantine.Single(x => x.Item.InstanceId == ghostId);
            Assert.AreEqual("SafeLoadout.Equipped", q.Source);
            Assert.AreEqual("weapon_removed_from_build", q.Item.DefinitionId);
            Assert.AreEqual(9, q.Item.AffixRolls[0].Value, "Roll state preserved verbatim.");
            StringAssert.Contains("weapon_removed_from_build", q.Reason);

            // Nothing was minted in place of the missing weapon.
            Assert.AreEqual(1, loaded.Slot.Profile.SafeLoadout.Equipped.Length);
            Assert.AreEqual((int)EquippedSlot.Armor, loaded.Slot.Profile.SafeLoadout.Equipped[0].Slot);

            // The quarantine is part of the slot and survives a save/load; ids stay unique with quarantined items included.
            Assert.AreEqual(SaveError.None, service.Save(loaded.Slot));
            var again = service.Load();
            Assert.IsTrue(again.Success, again.Diagnostics.ToString());
            Assert.AreEqual(2, again.Slot.Quarantine.Count);
            Assert.IsFalse(again.Diagnostics.HasWarnings, "Already quarantined: no repeated warnings.");
        }

        // ---- Acceptance 4 + requirement 5: settings and expedition state are not part of the slot ----

        [Test]
        public void Slot_ContainsNoSettingsAndNoExpeditionState()
        {
            var names = typeof(SaveSlot).GetFields(BindingFlags.Public | BindingFlags.Instance).Select(f => f.Name.ToLowerInvariant()).ToList();
            CollectionAssert.AreEquivalent(new[] { "saveversion", "profile", "storage", "firstlaunch", "activeexpedition", "quarantine" }, names);
            Assert.IsFalse(names.Any(n => n.Contains("setting") || n.Contains("volume") || n.Contains("binding") || n.Contains("resolution")));

            var allTypes = new[] { typeof(SaveSlot), typeof(PlayerProfile), typeof(FirstLaunchFlags) }
                .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Instance)).Select(f => f.FieldType).ToList();
            Assert.IsFalse(allTypes.Contains(typeof(ExpeditionState)), "No mid-expedition resume in V1.");
            Assert.IsFalse(allTypes.Contains(typeof(ExpeditionStats)));
            Assert.IsFalse(typeof(PlayerProfile).GetFields().Any(f => f.Name.ToLowerInvariant().Contains("carried")));
        }

        [Test]
        public void FileStore_WritesAtomically_AndKeepsPreviousDocumentAsBackup()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ruinrail_savetests_" + System.Guid.NewGuid().ToString("N"));
            var path = Path.Combine(dir, SaveSlotService.SlotFileName);
            try
            {
                var store = new FileSaveStore(path);
                Assert.IsFalse(store.Exists);
                var service = Service(store);
                Assert.AreEqual(SaveError.None, service.Save(BuildPopulatedSlot()));
                Assert.IsTrue(File.Exists(path));
                Assert.IsFalse(File.Exists(path + ".tmp"));

                var second = BuildPopulatedSlot();
                second.Profile.BankedCoins = 1;
                Assert.AreEqual(SaveError.None, service.Save(second));
                Assert.IsTrue(File.Exists(path + ".bak"));
                Assert.AreEqual(1, service.Load().Slot.Profile.BankedCoins);
                StringAssert.Contains("\"BankedCoins\":3210", File.ReadAllText(path + ".bak"));

                File.WriteAllText(path, "{ corrupt");
                var result = service.Load();
                // TASK 048: a corrupt current file is diagnosed and the last known good (.bak) is recovered instead.
                Assert.IsTrue(result.Diagnostics.Entries.Any(e => e.Code == "load.corrupt"));
                Assert.IsTrue(result.WasRecovered);
                Assert.AreEqual(SaveCandidateNames.Backup, result.Source);
                Assert.AreEqual(3210, result.Slot.Profile.BankedCoins);
                Assert.AreEqual("{ corrupt", File.ReadAllText(path), "Recovery never rewrites the file by itself.");

                File.WriteAllText(path + ".bak", "{ corrupt too");
                var none = service.Load();
                Assert.IsFalse(none.Success);
                Assert.AreEqual(SaveError.Corrupt, none.Error);
                Assert.AreEqual("{ corrupt", File.ReadAllText(path));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
