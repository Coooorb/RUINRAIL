using System;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Persistence
{
    /// <summary>
    /// Load/save of the single V1 gameplay slot: detect version → migrate step by step → parse → validate. A load never
    /// writes; a failed load (corrupt, newer, unsupported, invalid mandatory data, duplicate ids) leaves the source
    /// document untouched and reports diagnostics. Recovery is deterministic: the store's candidates are tried in its
    /// fixed order (complete temp, current, backup) and the first one that fully loads wins — a partial or corrupt file
    /// is never chosen because it is newer. The first save after a migrated load keeps the original document as a
    /// versioned backup before overwriting it. Settings are not part of this slot.
    /// </summary>
    public sealed class SaveSlotService
    {
        public const string SlotFileName = "ruinrail_save.json";

        private readonly ISaveStore _store;
        private readonly SaveMigrationPipeline _migrations;
        private readonly SaveValidator _validator;
        private int _pendingBackupVersion;

        public SaveSlotService(ISaveStore store, Func<string, ItemDefinition> resolveDefinition, SaveMigrationPipeline migrations = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _validator = new SaveValidator(resolveDefinition);
            _migrations = migrations ?? SaveMigrationPipeline.Default;
        }

        /// <summary>Default on-disk location of the one slot.</summary>
        public static string DefaultPath => System.IO.Path.Combine(Application.persistentDataPath, SlotFileName);

        public bool HasSave => _store.Exists;

        /// <summary>Loads the first candidate (temp → current → backup) that parses, migrates and validates.</summary>
        public SaveLoadResult Load()
        {
            var diagnostics = new SaveDiagnostics();
            var candidates = _store.ReadCandidates();
            if (candidates.Count == 0)
            {
                diagnostics.Info("load.none", "No save document present.");
                return SaveLoadResult.Fail(SaveError.NoSave, 0, diagnostics);
            }

            SaveLoadResult firstFailure = null;
            foreach (var candidate in candidates)
            {
                var attempt = LoadDocument(candidate.Document, diagnostics, candidate.Name);
                if (attempt.Success)
                {
                    if (candidate.Name != SaveCandidateNames.Current)
                    {
                        diagnostics.Warning("load.recovered", $"Loaded the '{candidate.Name}' document because the committed save was missing or unusable; the next save re-commits it.");
                    }

                    _pendingBackupVersion = attempt.WasMigrated ? attempt.SourceVersion : 0;
                    return SaveLoadResult.Ok(attempt.Slot, attempt.SourceVersion, attempt.WasMigrated, diagnostics, candidate.Name);
                }

                firstFailure ??= attempt;
            }

            diagnostics.Error("load.failed", "No candidate document (temp, current, backup) could be loaded. Nothing was written.");
            return SaveLoadResult.Fail(firstFailure?.Error ?? SaveError.Corrupt, firstFailure?.SourceVersion ?? 0, diagnostics);
        }

        private SaveLoadResult LoadDocument(string document, SaveDiagnostics diagnostics, string name)
        {
            if (string.IsNullOrWhiteSpace(document))
            {
                diagnostics.Error("load.empty", $"[{name}] Document is empty.");
                return SaveLoadResult.Fail(SaveError.Corrupt, 0, diagnostics);
            }

            var version = SaveDocumentInspector.DetectVersion(document, diagnostics);
            if (version <= 0)
            {
                diagnostics.Error("load.corrupt", $"[{name}] Save document has no recognisable version marker. The file was left untouched.");
                return SaveLoadResult.Fail(SaveError.Corrupt, version, diagnostics);
            }

            if (version > SaveSlot.CurrentVersion)
            {
                diagnostics.Error("load.newer", $"[{name}] Save version {version} is newer than this build supports ({SaveSlot.CurrentVersion}). Update the game; the file was left untouched.");
                return SaveLoadResult.Fail(SaveError.UnsupportedVersion, version, diagnostics);
            }

            if (!_migrations.Supports(version))
            {
                diagnostics.Error("load.unsupported", $"[{name}] Save version {version} is older than the oldest supported version ({_migrations.OldestSupportedVersion}). The file was left untouched.");
                return SaveLoadResult.Fail(SaveError.UnsupportedVersion, version, diagnostics);
            }

            var migrated = false;
            if (version < SaveSlot.CurrentVersion)
            {
                document = _migrations.Migrate(document, version, diagnostics);
                if (document == null)
                {
                    return SaveLoadResult.Fail(SaveError.MigrationFailed, version, diagnostics);
                }

                migrated = true;
            }

            SaveSlot slot;
            try
            {
                slot = JsonUtility.FromJson<SaveSlot>(document);
            }
            catch (Exception e)
            {
                diagnostics.Error("load.parse", $"[{name}] Save document could not be parsed as a slot: {e.Message}");
                return SaveLoadResult.Fail(SaveError.Corrupt, version, diagnostics);
            }

            var error = _validator.Validate(slot, diagnostics);
            if (error != SaveError.None)
            {
                return SaveLoadResult.Fail(error, version, diagnostics);
            }

            // A blank name (never written by the game, but possible in an edited or damaged document) must not blank the
            // HUD and party lines: the profile falls back to the default and the name step asks for a real one again.
            if (string.IsNullOrWhiteSpace(slot.Profile.DisplayName))
            {
                slot.Profile.DisplayName = PlayerProfile.DefaultDisplayName;
                slot.FirstLaunch ??= new FirstLaunchFlags();
                slot.FirstLaunch.DisplayNameConfirmed = false;
                diagnostics.Info("load.display_name", $"[{name}] Blank display name replaced by the default.");
            }

            return SaveLoadResult.Ok(slot, version, migrated, diagnostics, name);
        }

        /// <summary>
        /// Validates the serialized slot and writes it atomically; an invalid slot is refused and nothing is written. A store
        /// failure (disk, interruption) is reported as <see cref="SaveError.WriteFailed"/> — the previous committed save is intact.
        /// </summary>
        public SaveError Save(SaveSlot slot, SaveDiagnostics diagnostics = null)
        {
            diagnostics ??= new SaveDiagnostics();
            if (slot == null) return SaveError.InvalidMandatoryData;
            slot.SaveVersion = SaveSlot.CurrentVersion;

            // Validate a serialized copy so what we check is exactly what will be read back.
            var document = Serialize(slot);
            var error = _validator.Validate(JsonUtility.FromJson<SaveSlot>(document), diagnostics);
            if (error != SaveError.None) return error;

            try
            {
                if (_pendingBackupVersion > 0)
                {
                    _store.Backup($".v{_pendingBackupVersion}.bak");
                }

                _store.Write(document);
            }
            catch (Exception e)
            {
                diagnostics.Error("save.write", $"Save could not be committed: {e.Message}. The previous save is intact.");
                return SaveError.WriteFailed;
            }

            _pendingBackupVersion = 0;
            return SaveError.None;
        }

        public static string Serialize(SaveSlot slot) => JsonUtility.ToJson(slot);

        public static SaveSlot CreateNew() => new();
    }
}
