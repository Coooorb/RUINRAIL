using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Items;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Tunable soft aim assist (spec: forgiving by design for a 640×360 top-down game). It bends the *direction* of a
    /// shot toward the best hostile hurtbox inside a cone in front of the raw aim; it never locks a target, never
    /// auto-fires, never moves the cursor or the camera, and never changes what a weapon does once the shot is out.
    ///
    /// The cone width per weapon class is a data table on this asset (class identity feeds data, never a behaviour
    /// switch): every projectile class gets the full cone unless the table says otherwise (sniper 0.75, rocket 0.65);
    /// melee classes are 0 — they have no projectile to assist.
    /// </summary>
    [CreateAssetMenu(menuName = "RuinRail/Combat/Aim Assist Config", fileName = "AimAssistConfig")]
    public sealed class AimAssistConfig : ScriptableObject
    {
        [Serializable]
        public sealed class ClassMultiplier
        {
            public WeaponClass WeaponClass;
            [Range(0f, 1.5f)] public float Multiplier = 1f;
        }

        [Header("Cone")]
        [SerializeField, Range(0f, 45f)] private float _mouseHalfAngleDegrees = 18f;
        [SerializeField, Range(0f, 45f)] private float _controllerHalfAngleDegrees = 24f;
        [Tooltip("Crosshair-to-hurtbox distance (reference pixels at 640x360) that counts as 'on it' for scoring.")]
        [SerializeField, Min(1f)] private float _crosshairProximityPixels = 28f;
        [SerializeField, Min(1)] private int _pixelsPerUnit = 32;

        [Header("Weapon class multipliers (cone width); classes not listed use 1, melee always 0")]
        [SerializeField] private List<ClassMultiplier> _classMultipliers = DefaultTable();

        public float MouseHalfAngleDegrees => _mouseHalfAngleDegrees;
        public float ControllerHalfAngleDegrees => _controllerHalfAngleDegrees;
        public float CrosshairProximityPixels => _crosshairProximityPixels;
        public int PixelsPerUnit => _pixelsPerUnit;
        public IReadOnlyList<ClassMultiplier> ClassMultipliers => _classMultipliers;

        /// <summary>Melee has no projectile aim assist (0); every other class scales the cone by its table entry (1 when unlisted).</summary>
        public float MultiplierFor(WeaponClass weaponClass)
        {
            if (IsMelee(weaponClass)) return 0f;
            if (_classMultipliers != null)
            {
                for (var i = 0; i < _classMultipliers.Count; i++)
                {
                    var entry = _classMultipliers[i];
                    if (entry != null && entry.WeaponClass == weaponClass) return Mathf.Max(0f, entry.Multiplier);
                }
            }

            return 1f;
        }

        public float HalfAngleFor(WeaponClass weaponClass, bool pointerAim) =>
            (pointerAim ? _mouseHalfAngleDegrees : _controllerHalfAngleDegrees) * MultiplierFor(weaponClass);

        /// <summary>The melee classes (no projectile): the assist never applies to them.</summary>
        public static bool IsMelee(WeaponClass weaponClass) => weaponClass == WeaponClass.Knife || weaponClass == WeaponClass.Spear;

        /// <summary>The approved table: sniper 0.75, rocket 0.65; everything else full width.</summary>
        public static List<ClassMultiplier> DefaultTable() => new()
        {
            new ClassMultiplier { WeaponClass = WeaponClass.Sniper, Multiplier = 0.75f },
            new ClassMultiplier { WeaponClass = WeaponClass.RocketLauncher, Multiplier = 0.65f }
        };

        /// <summary>The approved defaults, for a runtime without the asset (tests) — identical to the serialized asset's.</summary>
        public static AimAssistConfig Defaults()
        {
            var config = CreateInstance<AimAssistConfig>();
            config.name = "AimAssistConfig (defaults)";
            return config;
        }
    }
}
