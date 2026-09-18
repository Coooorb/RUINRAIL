using System;
using System.Collections.Generic;
using System.Text;

namespace RuinRail.Gameplay.Player
{
    public enum DisplayNameError
    {
        None,
        Empty,
        TooShort,
        TooLong,
        ControlCharacters,
        Markup,
        InvalidCharacters,
        Blocked
    }

    public readonly struct DisplayNameResult
    {
        public DisplayNameResult(DisplayNameError error, string normalized)
        {
            Error = error;
            Normalized = normalized;
        }

        public DisplayNameError Error { get; }
        public bool IsValid => Error == DisplayNameError.None;

        /// <summary>The trimmed/collapsed name (also filled for length/blocklist failures so UI can show what was checked).</summary>
        public string Normalized { get; }
    }

    /// <summary>
    /// Normalises and validates a display name: leading/trailing spaces removed, runs of spaces collapsed, then length,
    /// character set (A-Z a-z 0-9 space _ -), control characters, markup-like tags and the local blocklist are checked.
    /// Pure and deterministic; the same rules apply on first launch and on later renames.
    /// </summary>
    public static class DisplayNameValidator
    {
        public static DisplayNameResult Validate(string raw, DisplayNamePolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (raw == null) return new DisplayNameResult(DisplayNameError.Empty, string.Empty);

            // Control characters (including newlines/tabs) are never sanitised into something else: reject outright.
            foreach (var c in raw)
            {
                if (char.IsControl(c)) return new DisplayNameResult(DisplayNameError.ControlCharacters, Normalize(raw));
            }

            var normalized = Normalize(raw);
            if (normalized.Length == 0) return new DisplayNameResult(DisplayNameError.Empty, normalized);
            if (normalized.IndexOf('<') >= 0 || normalized.IndexOf('>') >= 0) return new DisplayNameResult(DisplayNameError.Markup, normalized);

            foreach (var c in normalized)
            {
                if (!IsAllowed(c)) return new DisplayNameResult(DisplayNameError.InvalidCharacters, normalized);
            }

            if (normalized.Length < policy.MinLength) return new DisplayNameResult(DisplayNameError.TooShort, normalized);
            if (normalized.Length > policy.MaxLength) return new DisplayNameResult(DisplayNameError.TooLong, normalized);
            if (IsBlocked(normalized, policy.Blocklist)) return new DisplayNameResult(DisplayNameError.Blocked, normalized);

            return new DisplayNameResult(DisplayNameError.None, normalized);
        }

        /// <summary>Trim, then collapse consecutive spaces to one. Other characters are left as typed.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            var sb = new StringBuilder(raw.Length);
            var pendingSpace = false;
            foreach (var c in raw.Trim(' '))
            {
                if (c == ' ')
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
            }

            return sb.ToString();
        }

        private static bool IsAllowed(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == ' ' || c == '_' || c == '-';
        }

        private static bool IsBlocked(string normalized, IReadOnlyList<string> blocklist)
        {
            if (blocklist == null || blocklist.Count == 0) return false;
            var lower = normalized.ToLowerInvariant();
            var tokens = lower.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var collapsed = lower.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);

            foreach (var entryRaw in blocklist)
            {
                if (string.IsNullOrWhiteSpace(entryRaw)) continue;
                var entry = entryRaw.Trim().ToLowerInvariant();
                foreach (var token in tokens)
                {
                    if (token == entry) return true;
                }

                // Longer entries are unambiguous enough to catch "xxx123" / "123xxx" spellings; short ones stay token-only
                // so ordinary names containing them (e.g. "Cassandra") are not rejected.
                if (entry.Length >= 5 && collapsed.Contains(entry)) return true;
            }

            return false;
        }
    }
}
