using System;

namespace RuinRail.Persistence
{
    [Serializable]
    public sealed class AudioPreferences
    {
        [UnityEngine.Range(0f, 1f)] public float MasterVolume = 1f;
        [UnityEngine.Range(0f, 1f)] public float MusicVolume = 1f;
        [UnityEngine.Range(0f, 1f)] public float SfxVolume = 1f;
        public bool Mute;
    }

    [Serializable]
    public sealed class VideoPreferences
    {
        public bool Fullscreen = true;
        public bool VSync = true;

        /// <summary>0 = use the display's current resolution.</summary>
        public int ResolutionWidth;
        public int ResolutionHeight;
    }

    [Serializable]
    public sealed class ControlPreferences
    {
        /// <summary>Input System binding overrides as produced by InputActionAsset.SaveBindingOverridesAsJson; empty = defaults.</summary>
        public string BindingOverridesJson = "";
    }

    [Serializable]
    public sealed class AccessibilityPreferences
    {
        public bool ScreenShake = true;
        /// <summary>art/104: shake is adjustable as well as off; 0..1 scale applied to every shake while ScreenShake is on.</summary>
        [UnityEngine.Range(0f, 1f)] public float ScreenShakeIntensity = 1f;
        /// <summary>ui/91: damage numbers ON/OFF.</summary>
        public bool DamageNumbers = true;
        /// <summary>Enemy/player hit flash (photosensitivity).</summary>
        public bool HitFlash = true;
    }

    [Serializable]
    public sealed class TutorialPreferences
    {
        /// <summary>ui/95: contextual prompts can be re-enabled from Settings; the seen list itself lives in the gameplay save.</summary>
        public bool ShowPrompts = true;
    }

    /// <summary>
    /// User settings document (player/10, technical/113: settings are separate from the gameplay save). Audio, video,
    /// control/rebinding and accessibility live here and nowhere in <see cref="SaveSlot"/>; resetting one never touches
    /// the other. Concrete option lists are exposed by the settings UI task; this is the persisted shape.
    /// </summary>
    [Serializable]
    public sealed class SettingsData
    {
        public const int CurrentVersion = 1;

        public int SettingsVersion = CurrentVersion;
        public AudioPreferences Audio = new();
        public VideoPreferences Video = new();
        public ControlPreferences Controls = new();
        public AccessibilityPreferences Accessibility = new();
        public TutorialPreferences Tutorial = new();

        public static SettingsData Defaults() => new();
    }
}
