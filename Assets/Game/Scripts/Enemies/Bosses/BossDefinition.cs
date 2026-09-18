using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Bosses
{
    /// <summary>
    /// Hand-designed two-phase biome boss (combat/46_BOSSES.md): Solo pre-scaling baselines, ~4 core attacks, and a
    /// Phase 2 (at 50% HP) that speeds up / combines the same familiar mechanics and may add telegraphed arena hazards.
    /// Bosses are never displaced and have very high stagger resistance.
    /// </summary>
    [CreateAssetMenu(fileName = "Boss_", menuName = "RuinRail/Enemies/Boss Definition")]
    public sealed class BossDefinition : ScriptableObject, IMovesetActorDefinition
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private Biome _biome;
        [SerializeField, Min(1)] private int _baseHealth = 1050;
        [SerializeField, Min(0f)] private float _moveSpeed = 2f;
        [SerializeField, Min(0)] private int _baseXp = 650;
        [SerializeField, Min(0)] private int _strongestAttackDamageMin = 30;
        [SerializeField, Min(0)] private int _strongestAttackDamageMax = 36;
        [SerializeField, Range(0f, 1f)] private float _phaseTwoHealthFraction = 0.5f;
        [SerializeField, Range(0.25f, 1f)] private float _phaseTwoTimingMultiplier = 0.8f;
        [SerializeField, Range(0, 100)] private int _staggerResistancePercent = 95;
        [SerializeField] private EnemyAttackDefinition[] _moveset = System.Array.Empty<EnemyAttackDefinition>();
        [SerializeField] private EnemyAttackDefinition[] _phaseTwoArenaHazards = System.Array.Empty<EnemyAttackDefinition>();

        [Header("Summoning attack (46 Tunnel Maw: Roar with limited Swarm pressure)")]
        [Tooltip("Attack whose resolution summons; null = this boss never summons.")]
        [SerializeField] private EnemyAttackDefinition _summonAttack;
        [SerializeField] private EnemyDefinition _summonDefinition;
        [SerializeField, Min(0)] private int _summonCount;
        [SerializeField, Min(0)] private int _maxLivingSummons;
        [SerializeField, Min(0.5f)] private float _summonRadiusTiles = 2f;

        public string Id => _id;
        public string DisplayName => _displayName;
        public Biome Biome => _biome;
        public int BaseHealth => _baseHealth;
        public float MoveSpeed => _moveSpeed;
        public int BaseXp => _baseXp;
        public int StrongestAttackDamageMin => _strongestAttackDamageMin;
        public int StrongestAttackDamageMax => Mathf.Max(_strongestAttackDamageMin, _strongestAttackDamageMax);
        public float PhaseTwoHealthFraction => _phaseTwoHealthFraction;

        /// <summary>Phase 2 pressure: telegraph/recovery/cooldown durations are multiplied by this (0.8 = 25% faster).</summary>
        public float PhaseTwoTimingMultiplier => _phaseTwoTimingMultiplier;

        /// <summary>Very high by rule; bosses are never displaced by knockback (IsDisplaceable is always false).</summary>
        public int StaggerResistancePercent => _staggerResistancePercent;
        public int KnockbackResistancePercent => 100;
        public bool IsDisplaceable => false;

        public IReadOnlyList<EnemyAttackDefinition> Moveset => _moveset;

        /// <summary>Telegraphed arena hazards that join the rotation in Phase 2 (built from the same attack framework).</summary>
        public IReadOnlyList<EnemyAttackDefinition> PhaseTwoArenaHazards => _phaseTwoArenaHazards;

        public EnemyAttackDefinition SummonAttack => _summonAttack;
        public EnemyDefinition SummonDefinition => _summonDefinition;
        public int SummonCount => _summonCount;
        public int MaxLivingSummons => _maxLivingSummons;
        public float SummonRadiusTiles => _summonRadiusTiles;
        public bool CanSummon => _summonAttack != null && _summonDefinition != null && _summonCount > 0;
    }
}
