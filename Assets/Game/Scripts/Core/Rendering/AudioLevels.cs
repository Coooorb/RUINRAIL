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
        /// <summary>The player's ambience level (AUDIO page); applied on top of the SFX bus and the fixed ceiling below.</summary>
        public static float Ambience { get; private set; } = 1f;
        public static bool Muted { get; private set; }

        public static event Action Changed;

        /// <summary>Three-bus form (ambience stays at its current level); kept for callers that predate the ambience setting.</summary>
        public static void Set(float master, float music, float sfx, bool muted) => Set(master, music, sfx, Ambience, muted);

        public static void Set(float master, float music, float sfx, float ambience, bool muted)
        {
            Master = Mathf.Clamp01(master);
            Music = Mathf.Clamp01(music);
            Sfx = Mathf.Clamp01(sfx);
            Ambience = Mathf.Clamp01(ambience);
            Muted = muted;
            Changed?.Invoke();
        }

        public static void Reset() => Set(1f, 1f, 1f, 1f, false);

        /// <summary>V1 FINAL: ambience never exceeds this share of the SFX gain (art/105: below combat readability).</summary>
        public const float AmbienceCeiling = 0.4f;

        /// <summary>Effective linear gain for a category: 0 when muted, otherwise master × category.</summary>
        public static float GainFor(bool isMusic) => Muted ? 0f : Master * (isMusic ? Music : Sfx);

        /// <summary>The ambience bus gain before the ceiling: 0 when muted, otherwise master × sfx × the player's ambience level.</summary>
        public static float AmbienceBusGain => GainFor(false) * Ambience;

        /// <summary>
        /// The ambience bed's effective gain: the ambience bus (master × sfx × the AUDIO page's ambience level) under
        /// the art/105 ceiling, so ambience can be turned down or off on its own but never rises above the fixed share
        /// of the SFX bus.
        /// </summary>
        public static float AmbienceGain => AmbienceBusGain * AmbienceCeiling;
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
