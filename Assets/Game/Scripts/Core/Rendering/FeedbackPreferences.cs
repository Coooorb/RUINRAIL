using System;
using UnityEngine;

namespace RuinRail.Core.Rendering
{
    /// <summary>
    /// The player's feedback/accessibility choices as the presentation layer reads them (art/104: screen shake
    /// adjustable/off; ui/91: damage numbers ON/OFF; hit flash for photosensitivity). Settings persistence owns the
    /// values; this is the in-memory publication point. Gameplay never reads these.
    /// </summary>
    public static class FeedbackPreferences
    {
        public static float ScreenShakeIntensity { get; private set; } = 1f;
        public static bool DamageNumbers { get; private set; } = true;
        public static bool HitFlash { get; private set; } = true;

        public static event Action Changed;

        public static void Set(float screenShakeIntensity, bool damageNumbers, bool hitFlash)
        {
            ScreenShakeIntensity = Mathf.Clamp01(screenShakeIntensity);
            DamageNumbers = damageNumbers;
            HitFlash = hitFlash;
            Changed?.Invoke();
        }

        public static void Reset() => Set(1f, true, true);
    }
}
