using System;
using System.Collections.Generic;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using UnityEngine;

namespace RuinRail.Gameplay.Enemies.Attacks
{
    /// <summary>
    /// Tick-driven execution of one EnemyAttackDefinition after its telegraph: stationary hit sequences, a dash that
    /// damages what it touches, evenly fanned pooled projectile volleys, or a marked zone strike. Every direct hit goes
    /// through IDamageable and each target is hit at most once per hit window; projectiles carry their own once-only rule.
    /// Shared by Elites, Bosses and normal enemies so movesets differ only in data.
    /// </summary>
    public sealed class AttackResolver
    {
        private readonly Transform _self;
        private readonly Rigidbody2D _body;
        private readonly IDamageRoller _roller;
        private readonly ProjectilePool _projectilePool;
        private readonly HashSet<IDamageable> _hitThisWindow = new();
        // The queries are unfiltered (walls, props, pickups, projectiles, hazards and every actor count), so a long boss zone
        // across a busy arena can touch dozens of colliders: the buffer must hold them all or a player standing inside the
        // drawn footprint could be silently missed.
        private readonly Collider2D[] _overlaps = new Collider2D[128];
        private readonly List<Projectile> _spawnedProjectiles = new();

        private EnemyAttackDefinition _attack;
        private Vector2 _direction;
        private float _elapsed;
        private int _hitsDone;
        private float _dashTravelled;

        public AttackResolver(Transform self, Rigidbody2D body, IDamageRoller roller, ProjectilePool projectilePool = null)
        {
            _self = self;
            _body = body;
            _roller = roller ?? throw new ArgumentNullException(nameof(roller));
            _projectilePool = projectilePool;
        }

        public bool IsRunning => _attack != null;
        public EnemyAttackDefinition Current => _attack;

        /// <summary>Attacks begun on this resolver so far (presentation counts strikes with it).</summary>
        public int Begun { get; private set; }

        /// <summary>Hit windows (or the dash) that have struck for the current/last attack.</summary>
        public int WindowsFired { get; private set; }

        /// <summary>The direction the running attack was locked to.</summary>
        public Vector2 Direction => _direction;

        /// <summary>Hit windows of the running attack still to come (0 once the last has struck; a dash has none).</summary>
        public int WindowsRemaining => _attack == null || _attack.Motion == AttackMotion.Dash ? 0 : _attack.HitCount - _hitsDone;

        /// <summary>Seconds until the next hit window strikes (0 when none is pending).</summary>
        public float SecondsToNextWindow => WindowsRemaining <= 0 ? 0f : Mathf.Max(0f, _hitsDone * _attack.HitIntervalSeconds - _elapsed);
        public int HitsLanded { get; private set; }
        public int LastDamageDealt { get; private set; }

        /// <summary>True when the last dash ended early because a wall was in the way (clear recovery window, 44 Charger).</summary>
        public bool LastDashStoppedByWall { get; private set; }

        /// <summary>True when the last dash ended early at the encounter room's legal edge (a doorway is not a dash lane).</summary>
        public bool LastDashStoppedByBounds { get; private set; }
        private EncounterBounds _bounds;
        private EncounterBounds Bounds => _bounds != null ? _bounds : _bounds = _self != null ? _self.GetComponent<EncounterBounds>() : null;
        private static readonly RaycastHit2D[] WallHits = new RaycastHit2D[8];
        public IReadOnlyList<Projectile> SpawnedProjectiles => _spawnedProjectiles;

        /// <summary>Raised per landed direct hit with the damage applied (telemetry / audio / stagger hooks).</summary>
        public event Action<EnemyAttackDefinition, IDamageable, int> HitLanded;

        public void Begin(EnemyAttackDefinition attack, Vector2 lockedDirection)
        {
            _attack = attack;
            Begun++;
            // The lane the telegraph drew for this dash (same footprint, same wall clip): its hits stay inside it.
            if (attack.Motion == AttackMotion.Dash) _dashLane = AttackFootprint.Dash(attack, _self.position, lockedDirection, true, null, _self);
            WindowsFired = 0;
            _direction = lockedDirection.sqrMagnitude > 0.0001f ? lockedDirection.normalized : Vector2.right;
            _elapsed = 0f;
            _hitsDone = 0;
            _dashTravelled = 0f;
            LastDashStoppedByWall = false;
            LastDashStoppedByBounds = false;
            HitsLanded = 0;
            _hitThisWindow.Clear();
            _spawnedProjectiles.Clear();
        }

        public void Cancel()
        {
            _attack = null;
            if (_body != null) _body.linearVelocity = Vector2.zero;
        }

        /// <summary>Advances the active attack; returns true once it has fully resolved.</summary>
        public bool Tick(float deltaTime)
        {
            if (_attack == null)
            {
                return true;
            }

            _elapsed += deltaTime;
            switch (_attack.Motion)
            {
                case AttackMotion.Stationary:
                    return TickWindows(StrikeInFront);
                case AttackMotion.Dash:
                    return TickDash(deltaTime);
                case AttackMotion.Projectile:
                    return TickWindows(FireVolley);
                case AttackMotion.Zone:
                    return TickWindows(StrikeZone);
                case AttackMotion.Slam:
                    return TickWindows(StrikeAround);
                default:
                    Cancel();
                    return true;
            }
        }

        /// <summary>Runs HitCount windows spaced by HitIntervalSeconds; each window invokes the motion's action once.</summary>
        private bool TickWindows(Action window)
        {
            while (_hitsDone < _attack.HitCount && _elapsed >= _hitsDone * _attack.HitIntervalSeconds)
            {
                _hitThisWindow.Clear();
                window();
                _hitsDone++;
                WindowsFired = _hitsDone;
            }

            if (_hitsDone >= _attack.HitCount)
            {
                _attack = null;
                return true;
            }

            return false;
        }

        private void StrikeInFront()
        {
            var strike = AttackFootprint.Strike(_attack, _self.position, _direction);
            StrikeCircle(strike.Centre, strike.Radius);
        }

        private void StrikeAround()
        {
            var strike = AttackFootprint.Strike(_attack, _self.position, _direction);
            StrikeCircle(strike.Centre, strike.Radius);
        }

        private void StrikeZone()
        {
            var strike = AttackFootprint.Strike(_attack, _self.position, _direction);
            var count = Physics2D.OverlapBox(strike.Centre, strike.Size, strike.AngleDegrees, Physics2DQueries.LegacyQueryFilter(), _overlaps);
            ApplyHits(count);
        }

        private void FireVolley()
        {
            if (_projectilePool == null)
            {
                return;
            }

            var count = _attack.ProjectileCount;
            for (var i = 0; i < count; i++)
            {
                // Even fan: deterministic, readable, no RNG needed — the same lanes the telegraph draws.
                var direction = AttackFootprint.LaneDirection(_attack, _direction, i);
                var damage = _roller.Roll(_attack.DamageMin, _attack.DamageMax);
                var data = new ProjectileSpawnData(damage, _attack.ProjectileSpeed, _attack.ProjectileRange, _attack.Knockback, _attack.StaggerPower, direction, _self.gameObject, null, 0f, DamageTeam.Enemy, false, _attack.ProjectileVisualId);
                _spawnedProjectiles.Add(_projectilePool.Spawn((Vector2)_self.position + direction * AttackFootprint.VolleyMuzzleOffset, data));
            }
        }

        private bool TickDash(float deltaTime)
        {
            WindowsFired = 1;
            var step = Mathf.Min(_attack.DashSpeed * deltaTime, _attack.DashDistance - _dashTravelled);

            // A wall ahead ends the dash right there: no clipping and a readable recovery window for the player. The
            // probe reaches past the body's own radius, so a big body (Elite/Boss) sees the wall before it is pressed
            // against it rather than "dashing" in place until the distance runs out.
            if (WallAhead(step + Mathf.Max(_attack.HitRadius * 0.5f, BodyRadius())))
            {
                if (_body != null) _body.linearVelocity = Vector2.zero;
                StrikeDash();
                LastDashStoppedByWall = true;
                _attack = null;
                return true;
            }

            // The encounter room's legal edge ends a dash exactly like a wall: the endpoint is constrained to the room,
            // so a charge aimed through an open doorway stops at the threshold and leaves the same recovery window.
            var bounds = Bounds;
            if (bounds != null && bounds.IsBound)
            {
                // The tick may run at frame rate, but the velocity it commits is consumed by whole physics steps: the
                // endpoint is checked against the distance the body will actually travel in the coming step.
                var physicsStep = Mathf.Min(_attack.DashSpeed * Time.fixedDeltaTime, _attack.DashDistance - _dashTravelled);
                var position = _body != null ? _body.position : (Vector2)_self.position;
                var free = bounds.FreeDistance(position, _direction, physicsStep + 0.001f);
                if (free < physicsStep - 0.0001f)
                {
                    if (_body != null) _body.linearVelocity = free > 0.0001f ? _direction * (free / Time.fixedDeltaTime) : Vector2.zero;
                    StrikeDash();
                    LastDashStoppedByBounds = true;
                    _attack = null;
                    return true;
                }
            }

            if (_body != null)
            {
                _body.linearVelocity = _direction * _attack.DashSpeed;
            }

            _dashTravelled += step;
            StrikeDash();

            if (_dashTravelled >= _attack.DashDistance - 0.0001f)
            {
                if (_body != null) _body.linearVelocity = Vector2.zero;
                _attack = null;
                return true;
            }

            return false;
        }

        private float _bodyRadius = -1f;

        /// <summary>The solid body's radius (the circle collider on the actor itself), 0 when it has none.</summary>
        private float BodyRadius()
        {
            if (_bodyRadius >= 0f) return _bodyRadius;
            var circle = _self != null ? _self.GetComponent<CircleCollider2D>() : null;
            _bodyRadius = circle != null && !circle.isTrigger ? circle.radius : 0f;
            return _bodyRadius;
        }

        private bool WallAhead(float distance)
        {
            var count = Physics2D.Raycast(_self.position, _direction, Physics2DQueries.LegacyQueryFilter(), WallHits, distance);
            for (var i = 0; i < count; i++)
            {
                var hit = WallHits[i];
                if (hit.collider == null || hit.collider.transform == _self || hit.collider.transform.IsChildOf(_self)) continue;
                if (hit.collider.GetComponentInParent<EnvironmentObstacle>() != null) return true;
            }

            return false;
        }

        /// <summary>
        /// A dash strikes around the running body, but only inside the lane it was warned with: the body can be shoved off
        /// its line (crowding, a wall corner, the room edge), and its damage must never leave the red drawn for it.
        /// </summary>
        private void StrikeDash()
        {
            var count = Physics2D.OverlapCircle(_self.position, _attack.HitRadius, Physics2DQueries.LegacyQueryFilter(), _overlaps);
            var lane = _dashLane;
            var laneCount = Physics2D.OverlapCapsule(lane.Centre, lane.Size, CapsuleDirection2D.Horizontal, lane.AngleDegrees, Physics2DQueries.LegacyQueryFilter(), _laneOverlaps);
            var kept = 0;
            for (var i = 0; i < count; i++)
            {
                var inLane = false;
                for (var j = 0; j < laneCount && !inLane; j++) inLane = _laneOverlaps[j] == _overlaps[i];
                if (inLane) _overlaps[kept++] = _overlaps[i];
            }

            ApplyHits(kept);
        }

        private AttackFootprint.Shape _dashLane;
        private readonly Collider2D[] _laneOverlaps = new Collider2D[128];

        private void StrikeCircle(Vector2 center, float radius)
        {
            var count = Physics2D.OverlapCircle(center, radius, Physics2DQueries.LegacyQueryFilter(), _overlaps);
            ApplyHits(count);
        }

        private void ApplyHits(int count)
        {
            for (var i = 0; i < count; i++)
            {
                if (_overlaps[i].transform == _self || _overlaps[i].transform.IsChildOf(_self))
                {
                    continue;
                }

                // Now that enemies are solid, an Elite's slam must not injure the enemies beside it: explicit allies are skipped.
                if (TeamMember.AreAllies(_overlaps[i], _self)) continue;
                var damageable = DamageTargets.Resolve(_overlaps[i]);
                if (damageable == null || !_hitThisWindow.Add(damageable))
                {
                    continue;
                }

                var damage = _roller.Roll(_attack.DamageMin, _attack.DamageMax);
                if (damageable.TryApplyDamage(new DamageRequest(damage)))
                {
                    LastDamageDealt = damage;
                    HitsLanded++;
                    HitLanded?.Invoke(_attack, damageable, damage);

                    // Impact travels along the locked attack direction for dashes, away from the attacker otherwise.
                    var away = (Vector2)_overlaps[i].transform.position - (Vector2)_self.position;
                    var direction = _attack.Motion == AttackMotion.Dash || away.sqrMagnitude < 0.0001f ? _direction : away;
                    ImpactDispatcher.Apply(_overlaps[i], new ImpactRequest(direction, _attack.Knockback, _attack.StaggerPower, DamageKind.Normal, _self.gameObject));
                }
            }
        }
    }
}
