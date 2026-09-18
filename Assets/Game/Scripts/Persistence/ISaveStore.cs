using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RuinRail.Persistence
{
    /// <summary>One readable document in the store, in recovery priority order.</summary>
    public readonly struct SaveCandidate
    {
        public SaveCandidate(string name, string document)
        {
            Name = name;
            Document = document;
        }

        /// <summary>"current", "temp" or "backup".</summary>
        public string Name { get; }
        public string Document { get; }
    }

    /// <summary>
    /// Raw document storage for the single save slot. Writes must be atomic: a failed or interrupted write leaves the
    /// previous committed document intact. Recovery never trusts timestamps: candidates come back in a fixed order
    /// (a completed-but-uncommitted temp first, then the committed current, then the previous backup) and the loader
    /// takes the first one that parses and validates.
    /// </summary>
    public interface ISaveStore
    {
        bool Exists { get; }
        bool TryRead(out string document);
        void Write(string document);

        /// <summary>Keeps a copy of the current document under a side name (used before an in-place migration is committed).</summary>
        void Backup(string suffix);

        /// <summary>Every readable document, in recovery priority order.</summary>
        IReadOnlyList<SaveCandidate> ReadCandidates();
    }

    public static class SaveCandidateNames
    {
        public const string Current = "current";
        public const string Temp = "temp";
        public const string Backup = "backup";
    }

    /// <summary>In-memory store for tests and editor dry runs. Temp/Backup can be set directly to model interrupted writes.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        public string Document { get; set; }
        public string Temp { get; set; }
        public string Backup { get; set; }
        public string LastBackup { get; private set; }
        public int WriteCount { get; private set; }

        /// <summary>When set, the next Write throws after the temp document is produced and before the commit (simulated crash).</summary>
        public bool FailNextWriteBeforeCommit { get; set; }

        public bool Exists => Document != null;

        public bool TryRead(out string document)
        {
            document = Document;
            return document != null;
        }

        public void Write(string document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            Temp = document;
            if (FailNextWriteBeforeCommit)
            {
                FailNextWriteBeforeCommit = false;
                throw new IOException("Simulated interruption before commit.");
            }

            if (Document != null) Backup = Document;
            Document = document;
            Temp = null;
            WriteCount++;
        }

        void ISaveStore.Backup(string suffix)
        {
            LastBackup = Document;
        }

        public IReadOnlyList<SaveCandidate> ReadCandidates()
        {
            var list = new List<SaveCandidate>(3);
            if (Temp != null) list.Add(new SaveCandidate(SaveCandidateNames.Temp, Temp));
            if (Document != null) list.Add(new SaveCandidate(SaveCandidateNames.Current, Document));
            if (Backup != null) list.Add(new SaveCandidate(SaveCandidateNames.Backup, Backup));
            return list;
        }
    }

    /// <summary>
    /// File-backed store: the document is written to "&lt;path&gt;.tmp", read back and compared byte-for-byte, and only
    /// then swapped in with the previous document kept as "&lt;path&gt;.bak". A crash at any point leaves either the old
    /// committed file, or a complete temp next to it, never a truncated current file.
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new(false);
        private readonly string _path;

        public FileSaveStore(string path)
        {
            _path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Path required.", nameof(path)) : path;
        }

        public string Path => _path;
        public string TempPath => _path + ".tmp";
        public string BackupPath => _path + ".bak";
        public bool Exists => File.Exists(_path);

        public bool TryRead(out string document)
        {
            document = null;
            if (!File.Exists(_path)) return false;
            document = File.ReadAllText(_path, Utf8NoBom);
            return true;
        }

        public void Write(string document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(TempPath, document, Utf8NoBom);
            var readBack = File.ReadAllText(TempPath, Utf8NoBom);
            if (!string.Equals(readBack, document, StringComparison.Ordinal))
            {
                throw new IOException("Temp save did not read back identically; the committed save was left untouched.");
            }

            if (File.Exists(_path))
            {
                File.Replace(TempPath, _path, BackupPath);
            }
            else
            {
                File.Move(TempPath, _path);
            }
        }

        public void Backup(string suffix)
        {
            if (File.Exists(_path)) File.Copy(_path, _path + suffix, true);
        }

        public IReadOnlyList<SaveCandidate> ReadCandidates()
        {
            var list = new List<SaveCandidate>(3);
            Add(list, SaveCandidateNames.Temp, TempPath);
            Add(list, SaveCandidateNames.Current, _path);
            Add(list, SaveCandidateNames.Backup, BackupPath);
            return list;
        }

        private static void Add(List<SaveCandidate> list, string name, string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                list.Add(new SaveCandidate(name, File.ReadAllText(path, Utf8NoBom)));
            }
            catch (IOException)
            {
                // unreadable candidate: skipped, the next one in order is tried
            }
        }
    }
}
