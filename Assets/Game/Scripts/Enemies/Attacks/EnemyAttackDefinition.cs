using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Attacks
{
    /// <summary>How an attack moves the attacker while it resolves.</summary>
    public enum AttackMotion
    {
        /// <summary>Attacker stands still; hits land in a radius in front of it.</summary>
        Stationary,

        /// <summary>Attacker dashes along the direction locked at telegraph start and damages what it runs into.</summary>
        Dash,

        /// <summary>Attacker fires pooled projectiles: HitCount volleys, ProjectileCount each, fanned evenly over SpreadDegrees.</summary>
        Projectile,

        /// <summary>Marked area: after the telegraph, everything inside a ZoneLength x ZoneWidth box along the locked direction is hit once.</summary>
        Zone,

        /// <summary>Attacker stands still; hits land in a full circle of HitRadius around it (ground slams).</summary>
        Slam
    }

    /// <summary>
    /// One reusable telegraphed enemy attack (43_ENEMY_FRAMEWORK: modular attack behaviours). Damage is an integer range
    /// applied only through IDamageable. Knockback/StaggerPower are transported for the stagger foundation (TASK 050).
    /// </summary>
    [CreateAssetMenu(fileName = "Attack_", menuName = "RuinRail/Enemies/Attack Definition")]
    public sealed class EnemyAttackDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private AttackMotion _motion = AttackMotion.Stationary;
        [SerializeField, Min(0)] private int _damageMin;
        [SerializeField, Min(0)] private int _damageMax;
        [SerializeField, Min(0f)] private float _minTriggerRange;
        [SerializeField, Min(0f)] private float _maxTriggerRange = 1.5f;
        [SerializeField, Min(0f)] private float _telegraphSeconds = 0.4f;
        [SerializeField, Min(0f)] private float _recoverySeconds = 0.8f;
        [SerializeField, Min(1)] private int _hitCount = 1;
        [SerializeField, Min(0f)] private float _hitIntervalSeconds = 0.25f;
        [SerializeField, Min(0f)] private float _hitRadius = 1.2f;
        [SerializeField, Min(0f)] private float _dashDistance;
        [SerializeField, Min(0f)] private float _dashSpeed;
        [SerializeField, Min(0f)] private float _knockback;
        [SerializeField, Min(0f)] private float _staggerPower;
        [SerializeField, Min(0f)] private float _cooldownSeconds;
        [SerializeField, Min(1)] private int _projectileCount = 1;
        [SerializeField, Min(0f)] private float _spreadDegrees;
        [SerializeField, Min(0f)] private float _projectileSpeed = 10f;
        [SerializeField, Min(0f)] private float _projectileRange = 12f;
        [SerializeField, Min(0f)] private float _zoneLength = 6f;
        [SerializeField, Min(0f)] private float _zoneWidth = 1.5f;
        [Tooltip("Projectile motion: the in-flight visual profile id (ProjectileVisualCatalog); empty = the hostile default. Presentation only.")]
        [SerializeField] private string _projectileVisualId = string.Empty;

        public string Id => _id;
        public string DisplayName => _displayName;
        public AttackMotion Motion => _motion;
        public int DamageMin => _damageMin;
        public int DamageMax => Mathf.Max(_damageMin, _damageMax);
        public float MinTriggerRange => _minTriggerRange;
        public float MaxTriggerRange => Mathf.Max(_minTriggerRange, _maxTriggerRange);
        public float TelegraphSeconds => _telegraphSeconds;
        public float RecoverySeconds => _recoverySeconds;
        public int HitCount => Mathf.Max(1, _hitCount);
        public float HitIntervalSeconds => _hitIntervalSeconds;
        public float HitRadius => _hitRadius;
        public float DashDistance => _dashDistance;
        public float DashSpeed => _dashSpeed;
        public float Knockback => _knockback;
        public float StaggerPower => _staggerPower;
        public float CooldownSeconds => _cooldownSeconds;

        /// <summary>In-flight visual of this attack's projectiles (presentation only); empty = the hostile default profile.</summary>
        public string ProjectileVisualId => _projectileVisualId;

#if UNITY_EDITOR
        /// <summary>Editor-only binding used by the art pipeline. Never called at runtime.</summary>
        public void EditorSetProjectileVisualId(string id)
        {
            _projectileVisualId = id ?? string.Empty;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
        public int ProjectileCount => Mathf.Max(1, _projectileCount);
        public float SpreadDegrees => _spreadDegrees;
        public float ProjectileSpeed => _projectileSpeed;
        public float ProjectileRange => _projectileRange;
        public float ZoneLength => _zoneLength;
        public float ZoneWidth => _zoneWidth;

        public bool IsInTriggerRange(float distance) => distance >= MinTriggerRange && distance <= MaxTriggerRange;
    }
}
