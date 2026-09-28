using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Encounters
{
    /// <summary>
    /// Runs one composed encounter: spawns the plan through an <see cref="IEnemySpawner"/> at authored spawn points
    /// (cycled deterministically), never exceeding the party's active cap — the rest queue as reinforcements as actors
    /// die — and signals completion once every planned actor has spawned and died. Summoners spawned here get a
    /// cap-aware spawner so summoned units obey the same active cap (47).
    /// </summary>
    public sealed class EncounterRuntime
    {
        private readonly EncounterPlan _plan;
        private readonly IEnemySpawner _spawner;
        private readonly IReadOnlyList<Vector2> _spawnPoints;
        private readonly Transform _target;
        private readonly Queue<EnemyDefinition> _pending;
        private readonly List<EnemyController> _living = new();
        private readonly List<EnemyController> _all = new();
        private readonly Func<Vector2, bool> _isFreeSpot;
        private int _nextSpawnPoint;
        private bool _completedRaised;

        /// <summary>Centre-to-centre gap two spawning bodies keep: a little more than one body diameter (2 × 0.35).</summary>
        public const float SpawnSpacing = 0.8f;

        /// <param name="isFreeSpot">
        /// Whether a spot beside a marker may hold a body (clear of geometry and hazards, inside the room). Without it
        /// every actor spawns exactly on its marker.
        /// </param>
        public EncounterRuntime(EncounterPlan plan, IEnemySpawner spawner, IReadOnlyList<Vector2> spawnPoints, Transform target, Func<Vector2, bool> isFreeSpot = null)
        {
            _isFreeSpot = isFreeSpot;
            _plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            _spawnPoints = spawnPoints != null && spawnPoints.Count > 0 ? spawnPoints : new[] { Vector2.zero };
            _target = target;
            _pending = new Queue<EnemyDefinition>(plan.Expand());
        }

        public EncounterPlan Plan => _plan;
        public int ActiveCap => _plan.ActiveCap;
        public int PendingCount => _pending.Count;
        public int SpawnedCount => _all.Count;
        public int LivingCount { get { Prune(); return _living.Count; } }
        public IReadOnlyList<EnemyController> Living => _living;
        public bool IsStarted { get; private set; }
        public bool IsComplete => IsStarted && _pending.Count == 0 && LivingCount == 0;

        public event Action<EncounterRuntime, EnemyController> Spawned;
        public event Action<EncounterRuntime> Completed;

        /// <summary>Spawns the first wave up to the active cap.</summary>
        public void Start()
        {
            if (IsStarted) return;
            IsStarted = true;
            FillToCap();
            CheckCompletion();
        }

        /// <summary>Call when an actor dies or on a timer: refills freed capacity from the reinforcement queue.</summary>
        public void Tick()
        {
            if (!IsStarted) return;
            FillToCap();
            CheckCompletion();
        }

        private void FillToCap()
        {
            Prune();
            while (_pending.Count > 0 && _living.Count < ActiveCap)
            {
                var definition = _pending.Dequeue();
                var marker = _spawnPoints[_nextSpawnPoint % _spawnPoints.Count];
                _nextSpawnPoint++;
                var actor = _spawner.Spawn(definition, SpotBeside(marker), _target);
                if (actor == null) continue;
                Register(actor);
            }
        }

        /// <summary>
        /// A wave larger than the room's usable markers reuses them in the same frame. Two bodies created on one point
        /// have no direction to separate in, and when the solver finally pushes them apart it can throw one through the
        /// edge of a prop beside the marker, where the prop's merged outline holds it for good (an enemy buried in a
        /// locker, the room never clearing). So a marker another living actor still stands on hands its actor the
        /// nearest free spot on a small ring around it that a body can walk to from the marker; with no such spot (or no
        /// check available) it stays the marker.
        /// </summary>
        private Vector2 SpotBeside(Vector2 marker)
        {
            if (_isFreeSpot == null || !Occupied(marker)) return marker;
            foreach (var radius in new[] { SpawnSpacing, SpawnSpacing * 2f })
            {
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * Mathf.PI / 4f;
                    var spot = marker + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    // Free, and reached from the marker over open floor (never a pocket the props close off).
                    if (!Occupied(spot) && _isFreeSpot(spot) && SpawnClearance.HasClearPath(marker, spot)) return spot;
                }
            }

            return marker;
        }

        private bool Occupied(Vector2 spot)
        {
            foreach (var actor in _living)
                if (actor != null && actor.IsAlive && Vector2.Distance(actor.transform.position, spot) < SpawnSpacing) return true;
            return false;
        }

        private void Register(EnemyController actor)
        {
            _living.Add(actor);
            _all.Add(actor);
            actor.Died += OnActorDied;
            if (actor.Summoner != null) actor.Summoner.SetSpawner(new CappedSpawner(this, _spawner));
            Spawned?.Invoke(this, actor);
        }

        private void OnActorDied(EnemyController actor)
        {
            actor.Died -= OnActorDied;
            _living.Remove(actor);
            FillToCap();
            CheckCompletion();
        }

        private void Prune()
        {
            _living.RemoveAll(a => a == null || !a.IsAlive);
        }

        private void CheckCompletion()
        {
            if (_completedRaised || !IsComplete) return;
            _completedRaised = true;
            Completed?.Invoke(this);
        }

        /// <summary>Summoned units count toward the same active cap; a summon that would exceed it is refused (returns null).</summary>
        private sealed class CappedSpawner : IEnemySpawner
        {
            private readonly EncounterRuntime _runtime;
            private readonly IEnemySpawner _inner;

            public CappedSpawner(EncounterRuntime runtime, IEnemySpawner inner)
            {
                _runtime = runtime;
                _inner = inner;
            }

            public EnemyController Spawn(EnemyDefinition definition, Vector2 position, Transform target)
            {
                if (_runtime.LivingCount >= _runtime.ActiveCap) return null;
                var actor = _inner.Spawn(definition, position, target);
                if (actor != null) _runtime.Register(actor);
                return actor;
            }
        }
    }
}
