using System;
using System.Collections.Generic;
using RuinRail.Core.Rng;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies
{
    /// <summary>Creates enemies for a summoner. Encounter code injects its own (pooling, depth scaling, room registration); the default builds a bare enemy.</summary>
    public interface IEnemySpawner
    {
        EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target);
    }

    /// <summary>Default spawner: a plain enemy object with the shared controller, the parent's stagger config and target.</summary>
    public sealed class DefaultEnemySpawner : IEnemySpawner
    {
        /// <summary>Feet-level body radius of a normal enemy (the player's is 0.4): fits every authored 2-tile doorway.</summary>
        public const float BodyRadius = 0.35f;

        private readonly StaggerConfig _staggerConfig;

        public DefaultEnemySpawner(StaggerConfig staggerConfig = null)
        {
            _staggerConfig = staggerConfig;
        }

        public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
        {
            var go = new GameObject(definition != null ? definition.DisplayName : "Enemy");
            go.transform.position = position;
            // The body: a feet-level circle the same size class as the player's, so an enemy is a solid actor that the
            // walls, the sealed sockets and the combat door blockers stop exactly as they stop the player. Without it
            // (the state before this fix) enemies had no collider at all: nothing could hit them and nothing could
            // block them. The hurtbox above it is what projectiles and blades resolve against.
            go.AddComponent<CircleCollider2D>().radius = BodyRadius;
            CombatLayers.TagEnemyBody(go);
            go.AddComponent<TeamMember>().SetTeam(DamageTeam.Enemy);
            if (definition != null && definition.AttackKind == EnemyAttackKind.Projectile) go.AddComponent<ProjectilePool>();
            var controller = go.AddComponent<EnemyController>();
            CombatHurtbox.Attach(go, CombatHurtbox.NormalSize, CombatHurtbox.NormalOffset);
            controller.SetTarget(target);
            controller.SetDefinition(definition);
            if (_staggerConfig != null) controller.SetStaggerConfig(_staggerConfig);
            return controller;
        }
    }

    /// <summary>
    /// Summoner behaviour (44): every SummonIntervalSeconds spawns SummonCountMin..Max copies of the authored summon
    /// (only Swarm in V1) around itself, never exceeding MaxLivingSummons of its own living summons. Runs while the
    /// summoner is alive and engaged; death stops it. Deterministic through an injected seeded random source.
    /// </summary>
    public sealed class EnemySummoner : MonoBehaviour
    {
        private EnemyDefinition _definition;
        private EnemyController _owner;
        private IEnemySpawner _spawner;
        private IRandomSource _random;
        private readonly List<EnemyController> _living = new();
        private float _untilNextWave;
        private bool _engaged;

        public int LivingSummons { get { Prune(); return _living.Count; } }
        public int TotalSummoned { get; private set; }
        public int Waves { get; private set; }
        public float UntilNextWave => _untilNextWave;
        public IReadOnlyList<EnemyController> Living => _living;

        public event Action<EnemySummoner, IReadOnlyList<EnemyController>> Summoned;

        public void Configure(EnemyDefinition definition, EnemyController owner, IEnemySpawner spawner = null, IRandomSource random = null)
        {
            _definition = definition;
            _owner = owner;
            _spawner = spawner ?? new DefaultEnemySpawner(owner != null && owner.Impact != null ? owner.Impact.Config : null);
            _random = random ?? new SeededRandom(SeededRandom.MixSeed(definition != null ? definition.Id.GetHashCode() : 0, GetInstanceID()));
            _untilNextWave = definition != null ? definition.SummonIntervalSeconds : 9f;
        }

        public void SetSpawner(IEnemySpawner spawner) => _spawner = spawner ?? _spawner;
        public void SetRandom(IRandomSource random) => _random = random ?? _random;

        private void Update()
        {
            if (_definition == null || _owner == null || !_owner.IsAlive || _owner.Target == null) return;
            if (!_engaged)
            {
                _engaged = true; // the clock starts when the summoner has something to fight
                _untilNextWave = _definition.SummonIntervalSeconds;
            }

            _untilNextWave -= Time.deltaTime;
            if (_untilNextWave > 0f) return;
            _untilNextWave += _definition.SummonIntervalSeconds;
            SummonWave();
        }

        /// <summary>One wave: 2-3 summons (data), capped by the living limit. Returns how many were created.</summary>
        public int SummonWave()
        {
            if (_definition == null || _definition.SummonDefinition == null || _spawner == null) return 0;
            Prune();
            var room = _definition.MaxLivingSummons - _living.Count;
            if (room <= 0) { Waves++; return 0; }

            var wanted = _random.NextInt(_definition.SummonCountMin, _definition.SummonCountMax);
            var count = Mathf.Min(wanted, room);
            var created = new List<EnemyController>(count);
            for (var i = 0; i < count; i++)
            {
                // Deterministic ring around the summoner: no random positions, no overlap between wave members.
                var angle = (360f / count) * i + Waves * 30f;
                var offset = (Vector2)(Quaternion.Euler(0f, 0f, angle) * Vector2.right) * _definition.SummonRadiusTiles;
                var summon = _spawner.Spawn(_definition.SummonDefinition, (Vector2)transform.position + offset, _owner.Target);
                if (summon == null) continue;
                _living.Add(summon);
                created.Add(summon);
                TotalSummoned++;
            }

            Waves++;
            if (created.Count > 0) Summoned?.Invoke(this, created);
            return created.Count;
        }

        private void Prune()
        {
            _living.RemoveAll(s => s == null || !s.IsAlive);
        }
    }
}
