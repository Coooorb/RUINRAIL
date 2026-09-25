using System;
using System.Linq;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Enemies.Elites;
using RuinRail.Gameplay.Enemies.Encounters;
using UnityEngine;

namespace RuinRail.Dungeon.Runtime
{
    /// <summary>
    /// Elite Encounter (55/45): a compatible Combat room runs exactly one hand-designed Elite mini-boss instead of a
    /// director encounter. The Elite spawns at the marker farthest from the entering player when the room activates,
    /// is depth/party scaled like every enemy, and the room clears when it dies (once). XP is forwarded on defeat.
    /// </summary>
    public sealed class EliteEngagement : IRoomEngagement
    {
        private readonly EliteDefinition _definition;
        private readonly IEliteSpawner _spawner;
        private readonly int _depth;
        private readonly int _partySize;
        private readonly DepthScalingConfig _scaling;
        private bool _raised;

        public EliteEngagement(EliteDefinition definition, IEliteSpawner spawner, int depth, int partySize, DepthScalingConfig scaling = null,
            int runSeed = 0, int roomIndex = 0)
        {
            _definition = definition != null ? definition : throw new ArgumentNullException(nameof(definition));
            _spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            _depth = Math.Max(1, depth);
            _partySize = Math.Max(1, partySize);
            _scaling = scaling;
            _runSeed = runSeed;
            _roomIndex = roomIndex;
        }

        private readonly int _runSeed;
        private readonly int _roomIndex;

        public EliteDefinition Definition => _definition;
        public EliteEncounter Encounter { get; private set; }
        public bool IsComplete => Encounter != null && Encounter.IsCompleted;

        public event Action<IRoomEngagement> Completed;
        public event Action<EliteEngagement, int> EliteDefeated;
        /// <summary>The Elite actor exists (presentation binds its body here; gameplay never waits for it).</summary>
        public event Action<EliteEngagement, EliteEncounter> Spawned;

        public void Begin(RoomRuntime room, GameObject enteringPlayer)
        {
            if (Encounter != null) return;
            var points = room.SpawnPointsFor(enteringPlayer.transform.position);
            var position = points.Count > 0 ? points.OrderByDescending(p => Vector2.Distance(p, enteringPlayer.transform.position)).First() : (Vector2)room.transform.position;
            Encounter = _spawner.Spawn(_definition, position, room.transform, enteringPlayer.transform);
            if (Encounter == null || Encounter.Elite == null)
            {
                throw new InvalidOperationException($"Elite spawner produced no encounter for {_definition.Id}.");
            }

            // The Elite belongs to this room: its collider never leaves the room interior.
            room.BindEncounterBounds(Encounter.Elite.gameObject);
            // Depth + party scaling through the same seams as regular enemies (72/59/83).
            Encounter.Elite.SetDamageRoller(new DepthScaledDamageRoller(new UnityRandomDamageRoller(), _depth, _scaling));
            // Attack choice draws from RunSeed + Depth + this room, exactly like the boss, so the elite's sequence is
            // reproducible for the run rather than decided by moveset order.
            Encounter.Elite.SetSelectionSeed(_runSeed, _depth, _roomIndex);
            Encounter.Elite.Health.SetMaxHealth(DepthScaling.ScaledHealth(_definition.BaseHealth, _depth, _partySize, false, _scaling));
            Encounter.Completed += OnCompleted;
            Spawned?.Invoke(this, Encounter);
            if (Encounter.IsCompleted) OnCompleted(Encounter, Encounter.XpAwarded);
        }

        public void Tick(float deltaTime)
        {
        }

        private void OnCompleted(EliteEncounter encounter, int xp)
        {
            if (_raised) return;
            _raised = true;
            encounter.Completed -= OnCompleted;
            EliteDefeated?.Invoke(this, xp);
            Completed?.Invoke(this);
        }
    }
}
