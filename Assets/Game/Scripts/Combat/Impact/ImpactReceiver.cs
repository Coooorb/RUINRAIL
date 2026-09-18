using System;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Impact
{
    /// <summary>Static impact properties a receiver takes from its owner's definition (enemy, elite, boss).</summary>
    public readonly struct ImpactProfile
    {
        public ImpactProfile(string targetId, int staggerResistancePercent, int knockbackResistancePercent, bool isDisplaceable, bool isBoss)
        {
            TargetId = targetId ?? "";
            StaggerResistancePercent = Mathf.Clamp(staggerResistancePercent, 0, 100);
            KnockbackResistancePercent = Mathf.Clamp(knockbackResistancePercent, 0, 100);
            IsDisplaceable = isDisplaceable;
            IsBoss = isBoss;
        }

        public string TargetId { get; }
        public int StaggerResistancePercent { get; }
        public int KnockbackResistancePercent { get; }
        public bool IsDisplaceable { get; }
        public bool IsBoss { get; }
    }

    /// <summary>
    /// Stagger and knockback receiver for non-player actors: a hidden <see cref="StaggerMeter"/> plus a short
    /// velocity-driven displacement that stops at walls (reporting the wall impact to the attacker's feedback hook).
    /// Owners (EnemyController, MovesetActorController) read <see cref="IsStaggered"/>/<see cref="IsKnockbackActive"/>
    /// to interrupt and yield movement; the receiver never touches health except for the wall-impact bonus the
    /// attacker's hook returns.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class ImpactReceiver : MonoBehaviour, IStaggerReceiver, IKnockbackReceiver
    {
        private const float WallProbeSkin = 0.1f;
        private static readonly RaycastHit2D[] Hits = new RaycastHit2D[8];

        [SerializeField] private StaggerConfig _config;

        private Rigidbody2D _body;
        private StaggerMeter _meter;
        private ImpactProfile _profile = new("", 0, 0, true, false);
        private IDamageRoller _roller;

        private Vector2 _knockbackDirection;
        private float _knockbackRemaining;
        private float _knockbackSpeed;
        private bool _knockbackStopPending;
        private IImpactAttackerFeedback _knockbackFeedback;

        public StaggerConfig Config => _config;
        public ImpactProfile Profile => _profile;
        public StaggerMeter Meter => _meter ??= new StaggerMeter(_config);
        public bool IsStaggered => _meter != null && _meter.IsStaggered;
        public bool IsKnockbackActive => _knockbackRemaining > 0f || _knockbackStopPending;
        public int WallImpacts { get; private set; }
        public int KnockbacksApplied { get; private set; }

        public event Action<ImpactReceiver> Staggered;
        public event Action<ImpactReceiver> StaggerEnded;
        public event Action<ImpactReceiver, float> KnockedBack;
        public event Action<ImpactReceiver> KnockedIntoWall;

        public void SetConfig(StaggerConfig config)
        {
            _config = config;
            _meter = null;
        }

        public void SetProfile(ImpactProfile profile)
        {
            _profile = profile;
        }

        public void SetDamageRoller(IDamageRoller roller)
        {
            _roller = roller;
        }

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            _roller ??= new UnityRandomDamageRoller();
        }

        private void Update()
        {
            if (_config == null || _meter == null) return;
            var wasStaggered = _meter.IsStaggered;
            _meter.Tick(Time.deltaTime);
            if (wasStaggered && !_meter.IsStaggered) StaggerEnded?.Invoke(this);
        }

        private void FixedUpdate()
        {
            if (!IsKnockbackActive) return;
            if (_knockbackStopPending)
            {
                // The last velocity step has been simulated: now the body stops.
                EndKnockback();
                return;
            }

            var dt = Time.fixedDeltaTime;
            var step = Mathf.Min(_knockbackSpeed * dt, _knockbackRemaining);

            if (ProbeWall(step))
            {
                ResolveWallImpact();
                return;
            }

            _body.linearVelocity = _knockbackDirection * (step / dt);
            _knockbackRemaining -= step;
            if (_knockbackRemaining <= 0.0001f)
            {
                _knockbackRemaining = 0f;
                _knockbackStopPending = true;
            }
        }

        // ---- IStaggerReceiver ----

        public StaggerResult ApplyStagger(ImpactRequest request)
        {
            if (_config == null || !request.HasStagger) return StaggerResult.None;
            var result = Meter.Apply(request.StaggerPower, _profile.StaggerResistancePercent);
            if (result.Triggered)
            {
                Staggered?.Invoke(this);
                request.Feedback?.OnTargetStaggered(_profile.TargetId);
            }

            return result;
        }

        // ---- IKnockbackReceiver ----

        public KnockbackResult ApplyKnockback(ImpactRequest request)
        {
            if (_config == null || !request.HasKnockback || !_profile.IsDisplaceable) return KnockbackResult.None;
            var distance = KnockbackMath.Distance(request.Knockback, _profile.KnockbackResistancePercent, _config);
            if (distance <= 0f) return KnockbackResult.None;

            // A stronger push replaces a weaker one in flight; the remaining travel is never summed (no launch stacking).
            if (IsKnockbackActive && distance <= _knockbackRemaining) return new KnockbackResult(0f, false);
            _knockbackStopPending = false;

            _knockbackDirection = request.Direction;
            _knockbackRemaining = distance;
            _knockbackSpeed = distance / _config.KnockbackDurationSeconds;
            _knockbackFeedback = request.Feedback;
            KnockbacksApplied++;
            KnockedBack?.Invoke(this, distance);
            return new KnockbackResult(distance, false);
        }

        private bool ProbeWall(float step)
        {
            var count = Physics2D.Raycast(_body.position, _knockbackDirection, Physics2DQueries.LegacyQueryFilter(), Hits, step + WallProbeSkin);
            for (var i = 0; i < count; i++)
            {
                var hit = Hits[i];
                if (hit.collider == null || hit.collider.transform == transform || hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.collider.GetComponentInParent<EnvironmentObstacle>() != null) return true;
            }

            return false;
        }

        private void ResolveWallImpact()
        {
            var feedback = _knockbackFeedback;
            EndKnockback();
            WallImpacts++;
            KnockedIntoWall?.Invoke(this);

            if (feedback == null) return;
            var outcome = feedback.OnTargetKnockedIntoWall(_profile.TargetId, _profile.IsBoss);
            if (!outcome.HasBonus) return;

            if (outcome.BonusDamageMax > 0)
            {
                var damageable = GetComponent<IDamageable>();
                if (damageable != null)
                {
                    var bonus = (_roller ?? new UnityRandomDamageRoller()).Roll(outcome.BonusDamageMin, outcome.BonusDamageMax);
                    damageable.TryApplyDamage(new DamageRequest(bonus));
                }
            }

            if (outcome.ApplyHighStagger)
            {
                ApplyStagger(new ImpactRequest(Vector2.zero, 0f, _config.HighStaggerPower, DamageKind.Normal, null, feedback));
            }
        }

        private void EndKnockback()
        {
            _knockbackRemaining = 0f;
            _knockbackSpeed = 0f;
            _knockbackStopPending = false;
            _knockbackFeedback = null;
            if (_body != null) _body.linearVelocity = Vector2.zero;
        }
    }

    /// <summary>Damage-free shockwave: knockback/stagger pressure to every receiver in a circle (Discharge, Arc Stagger).</summary>
    public static class ShockwaveResolver
    {
        private static readonly Collider2D[] Overlaps = new Collider2D[64];

        public static int Emit(Vector2 center, float radius, float knockback, float staggerPower, DamageTeam sourceTeam, GameObject source = null, IImpactAttackerFeedback feedback = null)
        {
            var count = Physics2D.OverlapCircle(center, radius, Physics2DQueries.LegacyQueryFilter(), Overlaps);
            var seen = new System.Collections.Generic.HashSet<Transform>();
            var affected = 0;
            for (var i = 0; i < count; i++)
            {
                var collider = Overlaps[i];
                if (source != null && (collider.transform == source.transform || collider.transform.IsChildOf(source.transform))) continue;
                if (TeamMember.TeamOf(collider) == sourceTeam) continue;
                var root = collider.attachedRigidbody != null ? collider.attachedRigidbody.transform : collider.transform;
                if (!seen.Add(root)) continue;

                var direction = (Vector2)root.position - center;
                ImpactDispatcher.Apply(collider, new ImpactRequest(direction, knockback, staggerPower, DamageKind.Normal, source, feedback));
                affected++;
            }

            return affected;
        }
    }
}
