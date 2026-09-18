using System.Collections.Generic;
using System.Linq;

namespace RuinRail.Persistence
{
    public enum SaveSeverity
    {
        Info,
        Warning,
        Error
    }

    public enum SaveError
    {
        None,
        NoSave,
        Corrupt,
        UnsupportedVersion,
        MigrationFailed,
        InvalidMandatoryData,
        DuplicateInstanceIds,
        WriteFailed
    }

    public readonly struct SaveDiagnostic
    {
        public SaveDiagnostic(SaveSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public SaveSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        public override string ToString() => $"[{Severity}] {Code}: {Message}";
    }

    /// <summary>Actionable load/save diagnostics: what was wrong, where, and what was done about it.</summary>
    public sealed class SaveDiagnostics
    {
        private readonly List<SaveDiagnostic> _entries = new();

        public IReadOnlyList<SaveDiagnostic> Entries => _entries;
        public bool HasErrors => _entries.Any(e => e.Severity == SaveSeverity.Error);
        public bool HasWarnings => _entries.Any(e => e.Severity == SaveSeverity.Warning);

        public void Info(string code, string message) => _entries.Add(new SaveDiagnostic(SaveSeverity.Info, code, message));
        public void Warning(string code, string message) => _entries.Add(new SaveDiagnostic(SaveSeverity.Warning, code, message));
        public void Error(string code, string message) => _entries.Add(new SaveDiagnostic(SaveSeverity.Error, code, message));

        public override string ToString() => string.Join("\n", _entries);
    }

    /// <summary>Outcome of a load: either a validated slot or an error with diagnostics. A failed load never writes.</summary>
    public sealed class SaveLoadResult
    {
        private SaveLoadResult(bool success, SaveError error, SaveSlot slot, int sourceVersion, bool migrated, SaveDiagnostics diagnostics, string source)
        {
            Success = success;
            Error = error;
            Slot = slot;
            SourceVersion = sourceVersion;
            WasMigrated = migrated;
            Diagnostics = diagnostics;
            Source = source;
        }

        public bool Success { get; }
        public SaveError Error { get; }
        public SaveSlot Slot { get; }
        public int SourceVersion { get; }
        public bool WasMigrated { get; }
        public SaveDiagnostics Diagnostics { get; }

        /// <summary>Which store candidate was loaded ("current", "temp" or "backup"); null on failure.</summary>
        public string Source { get; }

        /// <summary>True when the committed document was unusable and a temp/backup candidate was loaded instead.</summary>
        public bool WasRecovered => Success && Source != SaveCandidateNames.Current;

        public static SaveLoadResult Ok(SaveSlot slot, int sourceVersion, bool migrated, SaveDiagnostics diagnostics, string source = SaveCandidateNames.Current) => new(true, SaveError.None, slot, sourceVersion, migrated, diagnostics, source);
        public static SaveLoadResult Fail(SaveError error, int sourceVersion, SaveDiagnostics diagnostics) => new(false, error, null, sourceVersion, false, diagnostics, null);
    }
}
