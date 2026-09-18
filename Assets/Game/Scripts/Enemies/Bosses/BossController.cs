using System;
using System.Collections.Generic;
using System.Linq;
using RuinRail.Gameplay.Enemies.Attacks;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Bosses
{
    /// <summary>
    /// Boss actor: the shared moveset FSM plus exactly two phases. Phase 2 starts once when health drops to the
    /// configured fraction (50%), applies the timing multiplier and prepends the arena-hazard attacks to the
    /// rotation. There is no third phase and no hidden rule set.
    /// </summary>
    public sealed class BossController : MovesetActorController
    {
        [SerializeField] private BossDefinition _definition;

        private bool _phaseTwo;
        private IEnemySpawner _summonSpawner;
        private readonly List<EnemyController> _summons = new();
        private int _summonWaves;

        public BossDefinition Definition => _definition;
        public int Phase => _phaseTwo ? 2 : 1;
        public bool IsPhaseTwo => _phaseTwo;

        public event Action<BossController, int> PhaseChanged;
        public event Action<BossController, IReadOnlyList<EnemyController>> Summoned;

        /// <summary>Summons spawned by this boss that are still alive.</summary>
        public IReadOnlyList<EnemyController> LivingSummons { get { _summons.RemoveAll(s => s == null || !s.IsAlive); return _summons; } }
        public int SummonWaves => _summonWaves;

        /// <summary>The room's enemy spawner; without it a summoning attack simply summons nothing.</summary>
        public void SetSummonSpawner(IEnemySpawner spawner) => _summonSpawner = spawner;

        protected override IMovesetActorDefinition ActorDefinition => _definition;

        protected override IEnumerable<EnemyAttackDefinition> ActiveMoveset
        {
            get
            {
                if (_definition == null) return Array.Empty<EnemyAttackDefinition>();
                return _phaseTwo ? _definition.PhaseTwoArenaHazards.Concat(_definition.Moveset) : _definition.Moveset;
            }
        }

        public void SetDefinition(BossDefinition definition)
        {
            _definition = definition;
            _phaseTwo = false;
            TimingMultiplier = 1f;
            ApplyDefinition();
        }

        protected override void Awake()
        {
            base.Awake();
            AttackResolved += OnBossAttackResolved;
        }

        protected override void OnDestroy()
        {
            AttackResolved -= OnBossAttackResolved;
            base.OnDestroy();
        }

        private void OnBossAttackResolved(MovesetActorController actor, EnemyAttackDefinition attack)
        {
            if (_definition == null || !_definition.CanSummon || attack != _definition.SummonAttack || _summonSpawner == null || !IsAlive) return;
            var room = Mathf.Max(0, _definition.MaxLivingSummons - LivingSummons.Count);
            var count = Mathf.Min(_definition.SummonCount, room);
            if (count <= 0) return;
            var spawned = new List<EnemyController>();
            for (var i = 0; i < count; i++)
            {
                var angle = (i / (float)count) * Mathf.PI * 2f;
                var position = (Vector2)transform.position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * _definition.SummonRadiusTiles;
                var summon = _summonSpawner.Spawn(_definition.SummonDefinition, position, Target);
                if (summon == null) continue;
                _summons.Add(summon);
                spawned.Add(summon);
            }

            if (spawned.Count == 0) return;
            _summonWaves++;
            Summoned?.Invoke(this, spawned);
        }

        protected override void OnDamaged(int amount)
        {
            CheckPhaseTransition();
        }

        private void CheckPhaseTransition()
        {
            if (_phaseTwo || _definition == null || Health == null || !Health.IsAlive) return;
            var fraction = Health.CurrentHealth / (float)Health.MaxHealth;
            if (fraction <= _definition.PhaseTwoHealthFraction)
            {
                _phaseTwo = true;
                TimingMultiplier = _definition.PhaseTwoTimingMultiplier;
                PhaseChanged?.Invoke(this, 2);
            }
        }
    }
}
