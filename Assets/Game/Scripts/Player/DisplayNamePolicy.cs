using System;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// Display-name rules (player/10_PLAYER_PROFILE): 3–16 characters after trim/collapse, letters/digits/space/_/-
    /// only, and a small local case-insensitive profanity blocklist. The blocklist is content, kept in data so it can be
    /// curated without code changes; no network service is involved.
    /// </summary>
    [CreateAssetMenu(fileName = "DisplayNamePolicy", menuName = "RuinRail/Player/Display Name Policy")]
    public sealed class DisplayNamePolicy : ScriptableObject
    {
        [SerializeField] private int _minLength = 3;
        [SerializeField] private int _maxLength = 16;

        [Tooltip("Case-insensitive. Entries match whole tokens (split on space/_/-); entries of 5+ characters also match inside the collapsed name.")]
        [SerializeField] private string[] _blocklist = Array.Empty<string>();

        public int MinLength => _minLength;
        public int MaxLength => _maxLength;
        public string[] Blocklist => _blocklist;

        /// <summary>In-memory policy with the approved lengths and the given blocklist (tests, editor tools).</summary>
        public static DisplayNamePolicy Create(int minLength, int maxLength, params string[] blocklist)
        {
            var policy = CreateInstance<DisplayNamePolicy>();
            policy._minLength = minLength;
            policy._maxLength = maxLength;
            policy._blocklist = blocklist ?? Array.Empty<string>();
            return policy;
        }
    }
}
