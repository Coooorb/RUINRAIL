using System;
using RuinRail.Gameplay.Player;

namespace RuinRail.Persistence
{
    /// <summary>
    /// First-launch display-name step and later renames (player/10, ui/95): validates through
    /// <see cref="DisplayNameValidator"/>, stores the normalised name in the profile and flips the first-launch flag.
    /// The caller autosaves (a profile mutation at base is a safe point).
    /// </summary>
    public sealed class DisplayNameService
    {
        private readonly SaveSlot _slot;
        private readonly DisplayNamePolicy _policy;

        public DisplayNameService(SaveSlot slot, DisplayNamePolicy policy)
        {
            _slot = slot ?? throw new ArgumentNullException(nameof(slot));
            _policy = policy != null ? policy : throw new ArgumentNullException(nameof(policy));
        }

        public string CurrentName => _slot.Profile.DisplayName;

        /// <summary>True until a name has been confirmed on this profile (first launch, or a migrated profile without one).</summary>
        public bool NeedsDisplayName => _slot.FirstLaunch == null || !_slot.FirstLaunch.DisplayNameConfirmed;

        public DisplayNameResult Validate(string raw) => DisplayNameValidator.Validate(raw, _policy);

        /// <summary>Confirms (first launch) or renames; nothing changes when the name is invalid.</summary>
        public DisplayNameResult TrySet(string raw)
        {
            var result = Validate(raw);
            if (!result.IsValid) return result;

            _slot.Profile.DisplayName = result.Normalized;
            _slot.FirstLaunch ??= new FirstLaunchFlags();
            _slot.FirstLaunch.DisplayNameConfirmed = true;
            return result;
        }
    }

    /// <summary>
    /// Explicit, independent resets (TASK 049 req. 5): the gameplay slot and the settings document each reset only
    /// themselves; wiping both requires asking for both. The previous gameplay document survives as the store backup.
    /// </summary>
    public static class PlayerDataReset
    {
        public static SaveError ResetGameplay(SaveSlotService saves, out SaveSlot fresh)
        {
            if (saves == null) throw new ArgumentNullException(nameof(saves));
            fresh = SaveSlotService.CreateNew();
            return saves.Save(fresh);
        }

        public static SaveError ResetSettings(UserSettingsService settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return settings.ResetToDefaults();
        }
    }
}
