using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Expedition;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Persistence
{
    /// <summary>One schema step. Operates on the raw document so that old C# types never have to be kept alive.</summary>
    public interface ISaveMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }

        /// <summary>Returns the upgraded document, or null after recording an error (nothing is discarded silently).</summary>
        string Migrate(string document, SaveDiagnostics diagnostics);
    }

    /// <summary>Version 1 (legacy bare PlayerProfile document) → version 2 (SaveSlot envelope with Storage and first-launch flags).</summary>
    public sealed class MigrateV1ToV2 : ISaveMigration
    {
        public int FromVersion => 1;
        public int ToVersion => 2;

        public string Migrate(string document, SaveDiagnostics diagnostics)
        {
            PlayerProfile profile;
            try
            {
                profile = JsonUtility.FromJson<PlayerProfile>(document);
            }
            catch (Exception e)
            {
                diagnostics.Error("migration.v1.parse", $"Legacy profile document could not be parsed: {e.Message}");
                return null;
            }

            if (profile == null)
            {
                diagnostics.Error("migration.v1.parse", "Legacy profile document is empty.");
                return null;
            }

            var slot = new SaveSlot
            {
                SaveVersion = ToVersion,
                Profile = profile,
                FirstLaunch = new FirstLaunchFlags { DisplayNameConfirmed = !string.IsNullOrWhiteSpace(profile.DisplayName) }
            };
            diagnostics.Info("migration.v1", "Wrapped legacy profile into the save-slot envelope; storage initialised empty at base capacity.");
            return JsonUtility.ToJson(slot);
        }
    }

    /// <summary>Ordered chain of migrations; every supported version must reach <see cref="SaveSlot.CurrentVersion"/> one step at a time.</summary>
    public sealed class SaveMigrationPipeline
    {
        private readonly Dictionary<int, ISaveMigration> _byFromVersion;

        public SaveMigrationPipeline(IEnumerable<ISaveMigration> migrations)
        {
            _byFromVersion = new Dictionary<int, ISaveMigration>();
            foreach (var m in migrations ?? Array.Empty<ISaveMigration>())
            {
                if (m.ToVersion != m.FromVersion + 1) throw new ArgumentException($"Migration {m.GetType().Name} must advance exactly one version.");
                if (_byFromVersion.ContainsKey(m.FromVersion)) throw new ArgumentException($"Duplicate migration from version {m.FromVersion}.");
                _byFromVersion.Add(m.FromVersion, m);
            }
        }

        public static SaveMigrationPipeline Default => new(new ISaveMigration[] { new MigrateV1ToV2() });

        public int OldestSupportedVersion => _byFromVersion.Count == 0 ? SaveSlot.CurrentVersion : Math.Min(_byFromVersion.Keys.Min(), SaveSlot.CurrentVersion);

        public bool Supports(int version) => version == SaveSlot.CurrentVersion || (version >= OldestSupportedVersion && version < SaveSlot.CurrentVersion && HasUnbrokenChain(version));

        private bool HasUnbrokenChain(int from)
        {
            for (var v = from; v < SaveSlot.CurrentVersion; v++)
            {
                if (!_byFromVersion.ContainsKey(v)) return false;
            }

            return true;
        }

        /// <summary>Upgrades <paramref name="document"/> from <paramref name="version"/> to the current version; null on failure.</summary>
        public string Migrate(string document, int version, SaveDiagnostics diagnostics)
        {
            var current = document;
            for (var v = version; v < SaveSlot.CurrentVersion; v++)
            {
                if (!_byFromVersion.TryGetValue(v, out var step))
                {
                    diagnostics.Error("migration.missing", $"No migration registered from save version {v}.");
                    return null;
                }

                current = step.Migrate(current, diagnostics);
                if (current == null) return null;

                var reached = SaveDocumentInspector.DetectVersion(current, diagnostics);
                if (reached != step.ToVersion)
                {
                    diagnostics.Error("migration.version", $"Migration {step.GetType().Name} produced version {reached}, expected {step.ToVersion}.");
                    return null;
                }
            }

            return current;
        }
    }

    /// <summary>Reads only the version markers of a document without trusting the rest of it.</summary>
    public static class SaveDocumentInspector
    {
        [Serializable]
        private sealed class Header
        {
            public int SaveVersion;
            public int Version;
        }

        /// <summary>0 = unrecognised; 1 = legacy bare profile (PlayerProfile.Version == 1, no envelope); otherwise SaveVersion.</summary>
        public static int DetectVersion(string document, SaveDiagnostics diagnostics)
        {
            if (string.IsNullOrWhiteSpace(document)) return 0;
            Header header;
            try
            {
                header = JsonUtility.FromJson<Header>(document);
            }
            catch (Exception e)
            {
                diagnostics?.Error("document.parse", $"Save document is not valid JSON: {e.Message}");
                return 0;
            }

            if (header == null) return 0;
            if (header.SaveVersion > 0) return header.SaveVersion;
            return header.Version == 1 && !document.Contains("\"SaveVersion\"") ? 1 : 0;
        }
    }
}
