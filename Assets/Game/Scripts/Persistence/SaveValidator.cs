using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;

namespace RuinRail.Persistence
{
    /// <summary>
    /// Validates a parsed slot before it is trusted: mandatory data must be sane, item instance ids must be unique across
    /// every safe container (duplication is a hard failure, never "fixed" by dropping one copy), and every item must
    /// resolve to a definition. Unresolved items are quarantined verbatim — never replaced with generated substitutes.
    /// </summary>
    public sealed class SaveValidator
    {
        private readonly Func<string, ItemDefinition> _resolveDefinition;

        public SaveValidator(Func<string, ItemDefinition> resolveDefinition)
        {
            _resolveDefinition = resolveDefinition ?? throw new ArgumentNullException(nameof(resolveDefinition));
        }

        public SaveError Validate(SaveSlot slot, SaveDiagnostics diagnostics)
        {
            if (slot == null)
            {
                diagnostics.Error("slot.null", "Save slot is missing.");
                return SaveError.InvalidMandatoryData;
            }

            if (slot.SaveVersion != SaveSlot.CurrentVersion)
            {
                diagnostics.Error("slot.version", $"Save version {slot.SaveVersion} is not the current schema ({SaveSlot.CurrentVersion}).");
                return SaveError.UnsupportedVersion;
            }

            var profile = slot.Profile;
            if (profile == null)
            {
                diagnostics.Error("profile.null", "Profile block is missing.");
                return SaveError.InvalidMandatoryData;
            }

            var error = SaveError.None;
            if (profile.TotalXp < 0) { diagnostics.Error("profile.xp", $"TotalXp {profile.TotalXp} is negative."); error = SaveError.InvalidMandatoryData; }
            if (profile.BankedCoins < 0) { diagnostics.Error("profile.coins", $"BankedCoins {profile.BankedCoins} is negative."); error = SaveError.InvalidMandatoryData; }
            if (profile.UnspentSkillPoints < 0) { diagnostics.Error("profile.skillPoints", $"UnspentSkillPoints {profile.UnspentSkillPoints} is negative."); error = SaveError.InvalidMandatoryData; }
            if (profile.Skills == null) { diagnostics.Error("profile.skills", "Skill allocation block is missing."); error = SaveError.InvalidMandatoryData; }
            if (profile.Trader == null) { diagnostics.Error("profile.trader", "Trader state block is missing."); error = SaveError.InvalidMandatoryData; }
            if (profile.Workshop == null) { diagnostics.Error("profile.workshop", "Workshop state block is missing."); error = SaveError.InvalidMandatoryData; }
            if (slot.Storage == null) { diagnostics.Error("storage.null", "Storage block is missing."); error = SaveError.InvalidMandatoryData; }
            else if (slot.Storage.Capacity <= 0) { diagnostics.Error("storage.capacity", $"Storage capacity {slot.Storage.Capacity} is invalid."); error = SaveError.InvalidMandatoryData; }
            if (error != SaveError.None) return error;

            slot.FirstLaunch ??= new FirstLaunchFlags();
            slot.Quarantine ??= new List<QuarantinedItem>();

            // Instance-id uniqueness across safe loadout + storage + quarantine: a duplicate means the save was tampered
            // with or a transaction bug leaked; refuse rather than guess which copy is real.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var duplicates = 0;
            foreach (var (source, entry) in AllEntries(slot))
            {
                var item = entry.Item;
                if (item == null || string.IsNullOrEmpty(item.InstanceId))
                {
                    diagnostics.Error("item.id", $"{source} slot {entry.Slot} has an item without an instance id.");
                    return SaveError.InvalidMandatoryData;
                }

                if (!seen.Add(item.InstanceId))
                {
                    diagnostics.Error("item.duplicate", $"Instance id {item.InstanceId} ({item.DefinitionId}) appears more than once ({source} slot {entry.Slot}).");
                    duplicates++;
                }
            }

            if (duplicates > 0) return SaveError.DuplicateInstanceIds;

            QuarantineUnresolved(slot, diagnostics);
            return SaveError.None;
        }

        private void QuarantineUnresolved(SaveSlot slot, SaveDiagnostics diagnostics)
        {
            if (slot.Profile.SafeLoadout != null)
            {
                slot.Profile.SafeLoadout.Equipped = Filter("SafeLoadout.Equipped", slot.Profile.SafeLoadout.Equipped, slot, diagnostics);
                slot.Profile.SafeLoadout.Backpack = Filter("SafeLoadout.Backpack", slot.Profile.SafeLoadout.Backpack, slot, diagnostics);
            }

            slot.Storage.Slots = Filter("Storage", slot.Storage.Slots, slot, diagnostics);
        }

        private InventorySnapshot.Entry[] Filter(string source, InventorySnapshot.Entry[] entries, SaveSlot slot, SaveDiagnostics diagnostics)
        {
            if (entries == null) return Array.Empty<InventorySnapshot.Entry>();
            var kept = new List<InventorySnapshot.Entry>(entries.Length);
            foreach (var entry in entries)
            {
                var item = entry?.Item;
                if (item == null) continue;
                if (string.IsNullOrEmpty(item.DefinitionId) || _resolveDefinition(item.DefinitionId) == null)
                {
                    var reason = $"Definition '{item.DefinitionId}' could not be resolved.";
                    diagnostics.Warning("item.unresolved", $"{source} slot {entry.Slot}: {reason} Item {item.InstanceId} quarantined (kept in save, not loaded).");
                    slot.Quarantine.Add(new QuarantinedItem { Source = source, Slot = entry.Slot, Reason = reason, Item = item });
                    continue;
                }

                kept.Add(entry);
            }

            return kept.ToArray();
        }

        private static IEnumerable<(string source, InventorySnapshot.Entry entry)> AllEntries(SaveSlot slot)
        {
            var loadout = slot.Profile.SafeLoadout;
            if (loadout != null)
            {
                foreach (var e in loadout.Equipped ?? Array.Empty<InventorySnapshot.Entry>()) if (e != null) yield return ("SafeLoadout.Equipped", e);
                foreach (var e in loadout.Backpack ?? Array.Empty<InventorySnapshot.Entry>()) if (e != null) yield return ("SafeLoadout.Backpack", e);
            }

            foreach (var e in slot.Storage.Slots ?? Array.Empty<InventorySnapshot.Entry>()) if (e != null) yield return ("Storage", e);
            foreach (var q in slot.Quarantine)
            {
                if (q?.Item != null) yield return ($"Quarantine({q.Source})", new InventorySnapshot.Entry { Slot = q.Slot, Item = q.Item });
            }
        }
    }
}
