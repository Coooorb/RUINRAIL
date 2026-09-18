using UnityEngine;

namespace RuinRail.Gameplay.Combat.Impact
{
    /// <summary>
    /// Shared stagger/knockback tuning (42_STAGGER_KNOCKBACK: hidden pressure, threshold, short interruption, no
    /// permanent stunlock). One asset for every receiver; per-target differences come only from resistance percentages
    /// and displaceability in definitions, never from random per-enemy modifiers. All numbers here are V1 FINAL (TASK 179)
    /// tunables: the GDD gives the rules, not the units.
    /// </summary>
    [CreateAssetMenu(fileName = "StaggerConfig", menuName = "RuinRail/Balance/Stagger Config")]
    public sealed class StaggerConfig : ScriptableObject
    {
        [Header("Stagger (hidden pressure)")]
        [SerializeField, Min(0.01f)] private float _threshold = 10f;
        [SerializeField, Min(0f)] private float _recoveryPerSecond = 5f;
        [SerializeField, Min(0f)] private float _staggerDurationSeconds = 0.6f;
        [Tooltip("After a stagger ends the target ignores stagger pressure for this long (anti stun-lock).")]
        [SerializeField, Min(0f)] private float _postStaggerImmunitySeconds = 1.0f;
        [Tooltip("Pressure applied by 'high stagger' effects (wall impact, shockwaves) when they specify none.")]
        [SerializeField, Min(0f)] private float _highStaggerPower = 10f;

        [Header("Knockback (displacement)")]
        [Tooltip("World units travelled per knockback point before resistance.")]
        [SerializeField, Min(0f)] private float _unitsPerKnockbackPoint = 0.25f;
        [SerializeField, Min(0f)] private float _maxKnockbackDistance = 4f;
        [SerializeField, Min(0.01f)] private float _knockbackDurationSeconds = 0.15f;
        [Tooltip("Minimum displacement worth applying; smaller results are ignored (no jitter from tiny hits).")]
        [SerializeField, Min(0f)] private float _minKnockbackDistance = 0.05f;

        public float Threshold => _threshold;
        public float RecoveryPerSecond => _recoveryPerSecond;
        public float StaggerDurationSeconds => _staggerDurationSeconds;
        public float PostStaggerImmunitySeconds => _postStaggerImmunitySeconds;
        public float HighStaggerPower => _highStaggerPower;
        public float UnitsPerKnockbackPoint => _unitsPerKnockbackPoint;
        public float MaxKnockbackDistance => _maxKnockbackDistance;
        public float KnockbackDurationSeconds => _knockbackDurationSeconds;
        public float MinKnockbackDistance => _minKnockbackDistance;

        /// <summary>In-memory config for tests.</summary>
        public static StaggerConfig Create(float threshold = 10f, float recoveryPerSecond = 5f, float staggerDuration = 0.6f, float immunity = 1f,
            float unitsPerPoint = 0.25f, float maxDistance = 4f, float knockbackDuration = 0.15f, float highStagger = 10f)
        {
            var c = CreateInstance<StaggerConfig>();
            c._threshold = threshold;
            c._recoveryPerSecond = recoveryPerSecond;
            c._staggerDurationSeconds = staggerDuration;
            c._postStaggerImmunitySeconds = immunity;
            c._unitsPerKnockbackPoint = unitsPerPoint;
            c._maxKnockbackDistance = maxDistance;
            c._knockbackDurationSeconds = knockbackDuration;
            c._highStaggerPower = highStagger;
            return c;
        }
    }
}
