using System;

namespace RuinRail.Core.Rendering
{
    /// <summary>
    /// The player's gameplay-assist settings, published from the settings document to the runtime.
    ///
    /// It lives beside <see cref="FeedbackPreferences"/> and works the same way — a static the settings layer sets and
    /// gameplay reads — because the alternative, threading a setting down through every weapon composition, is what
    /// leaves a setting connected to a UI row and to nothing else.
    ///
    /// <see cref="AimAssist"/> defaults to ON, which is both the shipped behaviour and what an older settings document
    /// with no aim-assist key deserializes to, so upgrading a save never silently turns it off.
    /// </summary>
    public static class AssistPreferences
    {
        /// <summary>
        /// Soft aim assist. ON is the current, unchanged behaviour (cone widths and scoring are untouched); OFF makes
        /// every shot leave on the raw aim direction, with no target bend and no proximity assist.
        /// </summary>
        public static bool AimAssist { get; private set; } = true;

        public static event Action Changed;

        public static void Set(bool aimAssist)
        {
            if (AimAssist == aimAssist) return;
            AimAssist = aimAssist;
            Changed?.Invoke();
        }

        /// <summary>Restores the approved default (ON).</summary>
        public static void Reset() => Set(true);
    }
}
