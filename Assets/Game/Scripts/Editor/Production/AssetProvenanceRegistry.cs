using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace RuinRail.EditorTools.Production
{
    /// <summary>
    /// TASK 151 requirement 5 — source, originality and licence metadata for every final asset that enters the
    /// release path, whether it was drawn, commissioned, generated or licensed.
    ///
    /// It is a checked-in JSON file rather than a ScriptableObject on purpose: it is production bookkeeping, it must
    /// diff readably in review, and it must never become a runtime dependency. It records what a human attested; it
    /// cannot verify the attestation (art/107 section 6).
    /// </summary>
    public static class AssetProvenanceRegistry
    {
        public const string RegistryPath = "production/asset_provenance.json";

        [Serializable]
        public sealed class Entry
        {
            public string roleId = string.Empty;
            public string sourcePath = string.Empty;
            /// <summary>Who or what produced it: a person, a studio, a tool, or a licensed pack.</summary>
            public string author = string.Empty;
            /// <summary>How it came to exist: original work, commissioned, generated from a described prompt/process, licensed.</summary>
            public string provenance = string.Empty;
            /// <summary>Licence or ownership statement. "project-owned" for original RUINRAIL work.</summary>
            public string license = string.Empty;
            /// <summary>True only if the asset derives from a reference game. art/107 section 6 forbids shipping it.</summary>
            public bool derivedFromReferenceGame;
            /// <summary>Set by the human reviewer at the TASK 152/165/176 gate. Never by a validator or a model.</summary>
            public string approvedBy = string.Empty;

            public ArtProductionContract.OriginalityAttestation ToAttestation() => new()
            {
                RoleId = roleId,
                Author = author,
                Provenance = provenance,
                License = license,
                DerivedFromReferenceGame = derivedFromReferenceGame
            };
        }

        [Serializable]
        private sealed class Document
        {
            public string note = string.Empty;
            public List<Entry> entries = new();
        }

        private const string DocumentNote =
            "TASK 151 asset provenance. One entry per final asset that enters the release path. " +
            "Required before a manifest role may claim APPROVED or INTEGRATED (art/107 section 6). " +
            "approvedBy is set by a human reviewer at the TASK 152/165/176 gate, never by automation.";

        public static IReadOnlyList<Entry> Load()
        {
            if (!File.Exists(RegistryPath)) return Array.Empty<Entry>();

            var json = File.ReadAllText(RegistryPath);
            if (json.Trim().Length == 0) return Array.Empty<Entry>();

            try
            {
                var document = JsonUtility.FromJson<Document>(json);
                return document?.entries?.Where(e => e != null).ToList() ?? (IReadOnlyList<Entry>)Array.Empty<Entry>();
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"{RegistryPath} is not valid JSON: {e.Message}");
                return Array.Empty<Entry>();
            }
        }

        public static void Save(IEnumerable<Entry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RegistryPath) ?? "production");
            var document = new Document
            {
                note = DocumentNote,
                entries = entries.Where(e => e != null).OrderBy(e => e.roleId, StringComparer.Ordinal).ToList()
            };
            File.WriteAllText(RegistryPath, JsonUtility.ToJson(document, true));
        }

        /// <summary>Creates the empty registry if it is absent, so the pipeline has a file to append to from TASK 153 on.</summary>
        [UnityEditor.MenuItem("RuinRail/Production/Create Asset Provenance Registry")]
        public static void EnsureExists()
        {
            if (File.Exists(RegistryPath)) return;
            Save(Array.Empty<Entry>());
        }

        public static bool TryGet(string roleId, out Entry entry)
        {
            entry = Load().FirstOrDefault(e => string.Equals(e.roleId, roleId, StringComparison.Ordinal));
            return entry != null;
        }
    }
}
