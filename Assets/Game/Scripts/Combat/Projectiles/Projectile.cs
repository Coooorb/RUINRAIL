using RuinRail.Gameplay.Combat;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Projectiles
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField] private float _maxLifetimeSeconds = 5f;

        private const float DefaultSweepRadius = 0.1f;
        private static readonly RaycastHit2D[] SweepHits = new RaycastHit2D[16];
        private static readonly IDamageRoller ExactRoller = new UnityRandomDamageRoller();

        private Rigidbody2D _rigidbody2D;
        private CircleCollider2D _circle;
        private ProjectilePool _pool;

        private readonly System.Collections.Generic.HashSet<IDamageable> _pierced = new();
        private ProjectileSpawnData _data;
        private Vector2 _direction;
        private float _distanceTraveled;
        private float _elapsedLifetime;
        private bool _isResolved;

        public ProjectileSpawnData Data => _data;
        public bool IsResolved => _isResolved;

        /// <summary>Result of the last detonation (explosive projectiles only).</summary>
        public Area.AreaDamageResolver.Result LastExplosion { get; private set; }
        public event System.Action<Projectile, Vector2> Exploded;
        /// <summary>A non-explosive hit landed (target or wall) at a position; presentation only — damage was already applied by the hit path.</summary>
        public event System.Action<Projectile, Vector2, bool> Impacted;

        public void SetPool(ProjectilePool pool)
        {
            _pool = pool;
        }

        public void SetMaxLifetimeSeconds(float maxLifetimeSeconds)
        {
            _maxLifetimeSeconds = maxLifetimeSeconds;
        }

        private void Awake()
        {
            _rigidbody2D = GetComponent<Rigidbody2D>();
            _rigidbody2D.bodyType = RigidbodyType2D.Kinematic;
            _rigidbody2D.gravityScale = 0f;
            _rigidbody2D.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            // Kinematic projectiles must also register trigger contacts against kinematic targets (bosses, dummies).
            _rigidbody2D.useFullKinematicContacts = true;
            _circle = GetComponent<CircleCollider2D>();
        }

        /// <summary>Number of hits resolved by the per-step sweep rather than by a trigger overlap (diagnostics/tests).</summary>
        public int SweepResolvedHits { get; private set; }

        /// <summary>Targets a piercing shot has already passed through (each is damaged exactly once).</summary>
        public int PiercedTargets => _pierced.Count;

        public void Activate(ProjectileSpawnData data)
        {
            _data = data;
            _direction = data.Direction.sqrMagnitude > 0.0001f ? data.Direction.normalized : Vector2.right;
            _distanceTraveled = 0f;
            _elapsedLifetime = 0f;
            _isResolved = false;
            _pierced.Clear();
            _rigidbody2D.linearVelocity = Vector2.zero;
            // The pool placed the transform at the muzzle; the body only learns of that at the next transform sync,
            // which is *after* this projectile's first FixedUpdate. Teleport the body explicitly so the very first
            // sweep and MovePosition start from the muzzle, not from wherever the pooled projectile last ended.
            _rigidbody2D.position = transform.position;

            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(_direction.y, _direction.x) * Mathf.Rad2Deg);
        }

        private void FixedUpdate()
        {
            if (_isResolved)
            {
                return;
            }

            var deltaTime = Time.fixedDeltaTime;
            _elapsedLifetime += deltaTime;

            var stepLength = Mathf.Min(_data.Speed * deltaTime, Mathf.Max(0f, _data.MaxRange - _distanceTraveled));

            // Sweep the whole step first: a fast projectile (sniper 34 u/s = 0.68 u per physics step) must never skip a
            // thin wall or a small target that lies between two discrete positions. Trigger contacts are only the
            // fallback for things already overlapping the projectile.
            // A piercing shot works through every target inside the step (each once); anything else stops at the first hit.
            while (TrySweep(stepLength, out var hit))
            {
                TryResolveHit(hit.collider);
                if (_isResolved)
                {
                    var travelled = Mathf.Max(0f, hit.distance);
                    _rigidbody2D.MovePosition(_rigidbody2D.position + _direction * travelled);
                    _distanceTraveled += travelled;
                    return;
                }

                if (!_data.Piercing) break;
            }

            var step = _direction * stepLength;
            _rigidbody2D.MovePosition(_rigidbody2D.position + step);
            _distanceTraveled += stepLength;

            if (_distanceTraveled >= _data.MaxRange || _elapsedLifetime >= _maxLifetimeSeconds)
            {
                Expire();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryResolveHit(other);
        }

        /// <summary>Nearest damageable or obstacle along the next step, ignoring the shooter and unrelated triggers.</summary>
        private bool TrySweep(float stepLength, out RaycastHit2D nearest)
        {
            nearest = default;
            if (stepLength <= 0f) return false;
            var radius = _circle != null ? _circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y) : DefaultSweepRadius;
            var count = Physics2D.CircleCast(_rigidbody2D.position, radius, _direction, Physics2DQueries.LegacyQueryFilter(), SweepHits, stepLength);
            var found = false;
            for (var i = 0; i < count; i++)
            {
                var candidate = SweepHits[i];
                if (candidate.collider == null || !IsRelevant(candidate.collider)) continue;
                if (_data.Piercing && _pierced.Contains(candidate.collider.GetComponentInParent<IDamageable>())) continue;
                if (!found || candidate.distance < nearest.distance)
                {
                    nearest = candidate;
                    found = true;
                }
            }

            if (found) SweepResolvedHits++;
            return found;
        }

        private bool IsRelevant(Collider2D other)
        {
            if (other.transform == transform) return false;
            if (_data.Source != null && (other.transform == _data.Source.transform || other.transform.IsChildOf(_data.Source.transform))) return false;
            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null) return !IsSameTeam(other);
            return other.GetComponentInParent<EnvironmentObstacle>() != null;
        }

        /// <summary>
        /// Friendly fire is OFF on every team: a projectile passes through actors explicitly tagged with its own team
        /// (players through players, an enemy shot through the enemy in front of it). Untagged targets stay hittable.
        /// </summary>
        private bool IsSameTeam(Collider2D other)
        {
            return TeamMember.IsTagged(other, _data.SourceTeam);
        }

        private void TryResolveHit(Collider2D other)
        {
            if (_isResolved)
            {
                return;
            }

            // Never hit the shooter: the source object itself or anything under it (works for non-root sources too).
            if (_data.Source != null && (other.transform == _data.Source.transform || other.transform.IsChildOf(_data.Source.transform)))
            {
                return;
            }

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable != null && IsSameTeam(other))
            {
                return; // pass through teammates without resolving
            }

            if (damageable != null && _data.IsExplosive)
            {
                // A rocket never applies a separate direct hit: the detonation is the hit (one application per target).
                Detonate();
                return;
            }

            if (damageable != null && _data.Piercing)
            {
                if (_pierced.Add(damageable) && damageable.TryApplyDamage(new DamageRequest(_data.Damage, DamageKind.Normal, 0f, _direction)))
                {
                    Impact.ImpactDispatcher.Apply(other, new Impact.ImpactRequest(_data.Direction, _data.Knockback, _data.StaggerPower, DamageKind.Normal, _data.Source, _data.Feedback));
                    Impacted?.Invoke(this, _rigidbody2D.position, true);
                }

                return; // never resolved by a target: only walls and range end a piercing shot
            }

            if (damageable != null)
            {
                _isResolved = true;
                if (damageable.TryApplyDamage(new DamageRequest(_data.Damage, DamageKind.Normal, 0f, _direction)))
                {
                    Impact.ImpactDispatcher.Apply(other, new Impact.ImpactRequest(_data.Direction, _data.Knockback, _data.StaggerPower, DamageKind.Normal, _data.Source, _data.Feedback));
                }

                Impacted?.Invoke(this, _rigidbody2D.position, true);

                ReturnToPool();
                return;
            }

            var obstacle = other.GetComponentInParent<EnvironmentObstacle>();
            if (obstacle != null)
            {
                if (_data.IsExplosive)
                {
                    Detonate();
                    return;
                }

                _isResolved = true;
                Impacted?.Invoke(this, _rigidbody2D.position, false);
                ReturnToPool();
            }
        }

        /// <summary>
        /// Reusable explosion path (rockets, later Sunbreaker): every damageable in the radius takes the projectile's rolled
        /// damage exactly once with the Explosion tag (Blast Suit / Shock Absorber rules), the shooter's team is skipped,
        /// and knockback/stagger radiate from the blast centre through the impact receivers.
        /// </summary>
        private void Detonate()
        {
            if (_isResolved) return;
            _isResolved = true;
            var centre = _rigidbody2D.position;
            LastExplosion = Area.AreaDamageResolver.Apply(centre, _data.ExplosionRadius, _data.Damage, _data.Damage, DamageKind.Explosion, _data.StaggerPower,
                _data.SourceTeam, ExactRoller, _data.Knockback, _data.Source, _data.Feedback);
            Exploded?.Invoke(this, centre);
            ReturnToPool();
        }

        private void Expire()
        {
            if (_isResolved)
            {
                return;
            }

            if (_data.IsExplosive)
            {
                Detonate(); // a rocket that reaches its range or lifetime still goes off
                return;
            }

            _isResolved = true;
            ReturnToPool();
        }

        private void ReturnToPool()
        {
            if (_pool != null)
            {
                _pool.Return(this);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }
    }
}
