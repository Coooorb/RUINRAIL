using System;
using UnityEngine;

namespace RuinRail.Core.Rendering
{
    /// <summary>
    /// The player's audio settings as the audio layer reads them (master/music/sfx 0..1 and mute). Settings persistence
    /// owns the values; this is the in-memory publication point (same pattern as <see cref="FeedbackPreferences"/>).
    /// </summary>
    public static class AudioLevels
    {
        public static float Master { get; private set; } = 1f;
        public static float Music { get; private set; } = 1f;
        public static float Sfx { get; private set; } = 1f;
        public static bool Muted { get; private set; }

        public static event Action Changed;

        public static void Set(float master, float music, float sfx, bool muted)
        {
            Master = Mathf.Clamp01(master);
            Music = Mathf.Clamp01(music);
            Sfx = Mathf.Clamp01(sfx);
            Muted = muted;
            Changed?.Invoke();
        }

        public static void Reset() => Set(1f, 1f, 1f, false);

        /// <summary>V1 FINAL: ambience never exceeds this share of the SFX gain (art/105: below combat readability).</summary>
        public const float AmbienceCeiling = 0.4f;

        /// <summary>Effective linear gain for a category: 0 when muted, otherwise master × category.</summary>
        public static float GainFor(bool isMusic) => Muted ? 0f : Master * (isMusic ? Music : Sfx);

        /// <summary>
        /// The ambience bed's effective gain. Ambience has no separate slider (art/105 keeps it a fixed share of the
        /// SFX bus), so this is the value a diagnostic must check rather than looking for a setting that never existed.
        /// </summary>
        public static float AmbienceGain => GainFor(false) * AmbienceCeiling;
    }

    /// <summary>UI sounds the menus raise (ui/90, art/105: subtle; confirm/cancel/failed-action clear but not noisy). The audio layer subscribes; UI never touches audio directly.</summary>
    public enum UiSound
    {
        Navigate,
        Confirm,
        Cancel,
        Failure,
        Purchase
    }

    public static class UiSoundBus
    {
        public static event Action<UiSound> Raised;
        public static int RaisedCount { get; private set; }

        public static void Raise(UiSound sound)
        {
            RaisedCount++;
            Raised?.Invoke(sound);
        }
    }
}
