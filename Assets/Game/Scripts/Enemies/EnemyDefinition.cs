using System;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>How a normal enemy delivers its core attack (43: modular attack behaviours, not nine AI codebases).</summary>
    public enum EnemyAttackKind
    {
        MeleeContact,
        Projectile,
        Charge,
        Moveset,
        Lob
    }

    /// <summary>
    /// One normal-enemy archetype (44_NORMAL_ENEMIES): pre-depth-scaling baseline stats, the attack behaviour it composes,
    /// its positioning preference, and the Encounter Director data (unlock depth, threat cost, spawn tags). Depth
    /// checks live in encounter data, never inside the enemy behaviour.
    /// </summary>
    [CreateAssetMenu(fileName = "EnemyDefinition", menuName = "RuinRail/Enemies/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private int _baseHealth;
        [SerializeField] private int _damageMin;
        [SerializeField] private int _damageMax;
        [SerializeField] private float _moveSpeed;
        [SerializeField] private int _baseXp;
        [SerializeField] private float _attackRange = 1f;
        [SerializeField] private float _attackTelegraphSeconds = 0.4f;
        [SerializeField] private float _attackCooldownSeconds = 1f;
        [SerializeField, Range(0, 100)] private int _staggerResistancePercent;
        [SerializeField, Range(0, 100)] private int _knockbackResistancePercent;

        [Header("Attack behaviour")]
        [SerializeField] private EnemyAttackKind _attackKind = EnemyAttackKind.MeleeContact;
        [SerializeField, Min(0f)] private float _projectileSpeed;
        [SerializeField, Min(0f)] private float _projectileRange;
        [SerializeField, Min(1)] private int _burstCount = 1;
        [SerializeField, Min(0f)] private float _burstIntervalSeconds;
        [Tooltip("Ranged: the aim tracks the target during the telegraph and locks this many seconds before the shot (0 = aim at fire time).")]
        [SerializeField, Min(0f)] private float _aimLockSeconds;
        [Tooltip("Distance the enemy tries to keep from its target while it can attack (0 = pure pursuer).")]
        [SerializeField, Min(0f)] private float _preferredDistance;
        [Tooltip("Charge archetypes: the authored Dash attack run by the shared AttackResolver.")]
        [SerializeField] private Attacks.EnemyAttackDefinition _chargeAttack;
        [Tooltip("Lob archetypes (Bomber): explosion radius and stagger of the thrown bomb; range = AttackRange, flight speed = ProjectileSpeed.")]
        [SerializeField, Min(0f)] private float _bombRadiusTiles;
        [SerializeField, Min(0f)] private float _bombStaggerPower;
        [Tooltip("Projectile archetypes: the in-flight visual profile id (ProjectileVisualCatalog); empty = the hostile default. Presentation only.")]
        [SerializeField] private string _projectileVisualId = string.Empty;
        [Tooltip("Moveset archetypes (Brute): fixed attacks in priority order, run by the shared AttackResolver.")]
        [SerializeField] private Attacks.EnemyAttackDefinition[] _moveset = Array.Empty<Attacks.EnemyAttackDefinition>();

        [Header("Summoning")]
        [Tooltip("Summoner archetypes: the enemy summoned (44: only Swarm), wave interval, wave size and the living cap per summoner.")]
        [SerializeField] private EnemyDefinition _summonDefinition;
        [SerializeField, Min(0.1f)] private float _summonIntervalSeconds = 9f;
        [SerializeField, Min(1)] private int _summonCountMin = 2;
        [SerializeField, Min(1)] private int _summonCountMax = 3;
        [SerializeField, Min(1)] private int _maxLivingSummons = 6;
        [SerializeField, Min(0.1f)] private float _summonRadiusTiles = 1.5f;

        [Header("Defence")]
        [Tooltip("Shield archetypes: frontal projectile damage reduction (44 Shield Enemy: 80%). 0 = no shield.")]
        [SerializeField, Range(0, 100)] private int _frontalShieldPercent;
        [SerializeField, Range(0f, 360f)] private float _frontalShieldArcDegrees = 120f;

        [Header("Encounter Director data")]
        [SerializeField, Min(1)] private int _unlockDepth = 1;
        [Tooltip("47_ENCOUNTER_BUDGETS threat weight (Swarm 0.5 ... Brute/Summoner 3).")]
        [SerializeField, Min(0f)] private float _threatCost = 1f;
        [SerializeField] private string[] _spawnTags = Array.Empty<string>();

        public string Id => _id;
        public string DisplayName => string.IsNullOrEmpty(_displayName) ? _id : _displayName;
        public int BaseHealth => _baseHealth;
        public int DamageMin => _damageMin;
        public int DamageMax => _damageMax;
        public float MoveSpeed => _moveSpeed;
        public int BaseXp => _baseXp;
        public float AttackRange => _attackRange;
        public float AttackTelegraphSeconds => _attackTelegraphSeconds;
        public float AttackCooldownSeconds => _attackCooldownSeconds;

        /// <summary>44_NORMAL_ENEMIES: low for Grunt/Swarm, high for Brute. 0 = fully susceptible.</summary>
        public int StaggerResistancePercent => _staggerResistancePercent;
        public int KnockbackResistancePercent => _knockbackResistancePercent;

        public EnemyAttackKind AttackKind => _attackKind;
        public float ProjectileSpeed => _projectileSpeed;
        public float ProjectileRange => _projectileRange > 0f ? _projectileRange : _attackRange;
        public int BurstCount => Mathf.Max(1, _burstCount);
        public float BurstIntervalSeconds => Mathf.Max(0f, _burstIntervalSeconds);
        public float AimLockSeconds => Mathf.Max(0f, _aimLockSeconds);
        public float PreferredDistance => _preferredDistance;
        public bool KeepsDistance => _preferredDistance > 0f;
        public Attacks.EnemyAttackDefinition ChargeAttack => _chargeAttack;
        public EnemyDefinition SummonDefinition => _summonDefinition;
        public bool IsSummoner => _summonDefinition != null;

        /// <summary>In-flight visual of this archetype's projectiles (presentation only); empty = the hostile default profile.</summary>
        public string ProjectileVisualId => _projectileVisualId;

#if UNITY_EDITOR
        /// <summary>Editor-only binding used by the art pipeline. Never called at runtime.</summary>
        public void EditorSetProjectileVisualId(string id)
        {
            _projectileVisualId = id ?? string.Empty;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
        public float SummonIntervalSeconds => Mathf.Max(0.1f, _summonIntervalSeconds);
        public int SummonCountMin => Mathf.Max(1, _summonCountMin);
        public int SummonCountMax => Mathf.Max(SummonCountMin, _summonCountMax);
        public int MaxLivingSummons => Mathf.Max(1, _maxLivingSummons);
        public float SummonRadiusTiles => Mathf.Max(0.1f, _summonRadiusTiles);
        public int FrontalShieldPercent => _frontalShieldPercent;
        public float FrontalShieldArcDegrees => _frontalShieldArcDegrees;
        public bool HasFrontalShield => _frontalShieldPercent > 0;
        public float BombRadiusTiles => _bombRadiusTiles;
        public float BombStaggerPower => _bombStaggerPower;
        public Attacks.EnemyAttackDefinition[] Moveset => _moveset ?? Array.Empty<Attacks.EnemyAttackDefinition>();

        public int UnlockDepth => Mathf.Max(1, _unlockDepth);
        public float ThreatCost => Mathf.Max(0f, _threatCost);
        public string[] SpawnTags => _spawnTags ?? Array.Empty<string>();
    }
}
