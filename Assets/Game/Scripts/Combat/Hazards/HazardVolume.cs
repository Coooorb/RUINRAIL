using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat.Impact;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Hazards
{
    /// <summary>
    /// Trigger volume that damages whoever stands in it (players and enemies alike — hazards are environmental and are
    /// never routed through the friendly-fire filter). Occupants are tracked per <see cref="IDamageable"/>, so a target
    /// with several colliders is one occupant with one timer; ticks are driven by the fixed step, so the interval is
    /// deterministic: first tick after InitialDelay, then every TickInterval while inside, timer reset on exit.
    /// Works on a room's Hazards Tilemap (trigger TilemapCollider2D) as well as on a marker-sized BoxCollider2D.
    /// </summary>
    public sealed class HazardVolume : MonoBehaviour
    {
        private sealed class Occupant
        {
            public IDamageable Damageable;
            public Component Anchor;
            public int ColliderCount;
            public float TimeUntilTick;
            public DamageTeam Team;
        }

        [SerializeField] private HazardDefinition _definition;

        private readonly Dictionary<IDamageable, Occupant> _occupants = new();
        private readonly List<Occupant> _tickBuffer = new();
        private IDamageRoller _roller;

        public HazardDefinition Definition => _definition;
        public int OccupantCount => _occupants.Count;
        public int TicksApplied { get; private set; }

        /// <summary>Raised per landed tick with the target and the damage the request carried.</summary>
        public event Action<IDamageable, int> Ticked;

        public void SetDefinition(HazardDefinition definition)
        {
            _definition = definition;
        }

        public void SetDamageRoller(IDamageRoller roller)
        {
            _roller = roller;
        }

        private void Awake()
        {
            _roller ??= new UnityRandomDamageRoller();
        }

        private void OnDisable()
        {
            _occupants.Clear();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) return;
            if (_occupants.TryGetValue(damageable, out var occupant))
            {
                occupant.ColliderCount++; // a second collider of the same target: same occupant, same timer
                return;
            }

            var team = TeamMember.TeamOf(other);
            if (!Affects(team)) return;
            _occupants[damageable] = new Occupant
            {
                Damageable = damageable,
                Anchor = damageable as Component ?? other,
                ColliderCount = 1,
                TimeUntilTick = _definition != null ? _definition.InitialDelaySeconds : 0f,
                Team = team
            };
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null || !_occupants.TryGetValue(damageable, out var occupant)) return;
            occupant.ColliderCount--;
            if (occupant.ColliderCount <= 0) _occupants.Remove(damageable);
        }

        private void FixedUpdate()
        {
            if (_definition == null || _occupants.Count == 0) return;
            var dt = Time.fixedDeltaTime;
            _tickBuffer.Clear();
            foreach (var occupant in _occupants.Values)
            {
                occupant.TimeUntilTick -= dt;
                if (occupant.TimeUntilTick <= 0.0001f) _tickBuffer.Add(occupant);
            }

            foreach (var occupant in _tickBuffer)
            {
                occupant.TimeUntilTick += _definition.TickIntervalSeconds;
                Tick(occupant);
            }
        }

        private bool Affects(DamageTeam team)
        {
            if (_definition == null) return false;
            return team switch
            {
                DamageTeam.Player => _definition.AffectsPlayers,
                DamageTeam.Enemy => _definition.AffectsEnemies,
                _ => false
            };
        }

        private void Tick(Occupant occupant)
        {
            if (occupant.Anchor == null)
            {
                _occupants.Remove(occupant.Damageable);
                return;
            }

            var damage = _roller.Roll(_definition.DamageMin, _definition.DamageMax);
            if (!occupant.Damageable.TryApplyDamage(new DamageRequest(damage, _definition.Kind, _definition.StaggerPower))) return;
            TicksApplied++;
            Ticked?.Invoke(occupant.Damageable, damage);

            if (_definition.Knockback > 0f || _definition.StaggerPower > 0f)
            {
                var direction = (Vector2)occupant.Anchor.transform.position - (Vector2)transform.position;
                ImpactDispatcher.Apply(occupant.Anchor, new ImpactRequest(direction, _definition.Knockback, _definition.StaggerPower, _definition.Kind, gameObject));
            }
        }
    }
}
