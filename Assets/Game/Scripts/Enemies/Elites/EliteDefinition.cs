using System.Collections.Generic;
using RuinRail.Core;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Elites
{
    /// <summary>
    /// Hand-designed Elite mini-boss (combat/45_ELITES.md): pre-scaling baselines plus a fixed, ordered moveset.
    /// No random modifiers, no phases — the same attacks from start to finish.
    /// </summary>
    [CreateAssetMenu(fileName = "Elite_", menuName = "RuinRail/Enemies/Elite Definition")]
    public sealed class EliteDefinition : ScriptableObject, IMovesetActorDefinition
    {
        [SerializeField] private string _id;
        [SerializeField] private string _displayName;
        [SerializeField] private Biome _biome;
        [SerializeField, Min(1)] private int _baseHealth = 350;
        [SerializeField, Min(0f)] private float _moveSpeed = 3.6f;
        [SerializeField, Min(0)] private int _baseXp = 250;
        [SerializeField, Min(0)] private int _strongestAttackDamageMin = 24;
        [SerializeField, Min(0)] private int _strongestAttackDamageMax = 30;
        [SerializeField, Range(0, 100)] private int _staggerResistancePercent = 80;
        [SerializeField, Range(0, 100)] private int _knockbackResistancePercent = 80;
        [SerializeField] private EnemyAttackDefinition[] _moveset = System.Array.Empty<EnemyAttackDefinition>();

        public string Id => _id;
        public string DisplayName => _displayName;
        public Biome Biome => _biome;
        public int BaseHealth => _baseHealth;
        public float MoveSpeed => _moveSpeed;
        public int BaseXp => _baseXp;
        public int StrongestAttackDamageMin => _strongestAttackDamageMin;
        public int StrongestAttackDamageMax => Mathf.Max(_strongestAttackDamageMin, _strongestAttackDamageMax);

        /// <summary>Resistance hooks for the stagger/knockback foundation (TASK 050); data only until then.</summary>
        public int StaggerResistancePercent => _staggerResistancePercent;
        public int KnockbackResistancePercent => _knockbackResistancePercent;
        public bool IsDisplaceable => true;

        /// <summary>Fixed moveset in priority order; the first ready attack whose trigger range contains the target is used.</summary>
        public IReadOnlyList<EnemyAttackDefinition> Moveset => _moveset;
    }
}
