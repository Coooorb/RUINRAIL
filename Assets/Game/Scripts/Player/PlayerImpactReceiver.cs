using System;
using RuinRail.Gameplay.Combat;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Stats;
using UnityEngine;

namespace RuinRail.Gameplay.Player
{
    /// <summary>
    /// The player's stagger/knockback boundary. Incoming effects go through the player's stat pipeline (Stagger and
    /// Knockback Resistance, already capped at 50% by GlobalStatCapsConfig) and the reactive hooks on
    /// <see cref="PlayerCombatEvents"/> (Anchored negates one stagger, Shock Absorber ignores explosion knockback,
    /// Exo Lock raises resistances) without this class knowing any item. As the attacker-side feedback it forwards
    /// "enemy staggered" and "enemy knocked into wall" to the same hub for Shock Charm / Impact Module.
    /// A staggered player briefly loses movement (IMovementOverride); weapon input gating reads IsStaggered.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerImpactReceiver : MonoBehaviour, IStaggerReceiver, IKnockbackReceiver, IMovementOverride, IImpactAttackerFeedback
    {
        [SerializeField] private StaggerConfig _config;

        private Rigidbody2D _body;
        private StaggerMeter _meter;
        private IPlayerStatsProvider _stats;
        private PlayerCombatEvents _events;
        private Vector2 _knockbackDirection;
        private float _knockbackRemaining;
        private float _knockbackSpeed;
        private bool _knockbackStopPending;

        public StaggerMeter Meter => _meter ??= new StaggerMeter(_config);
        public bool IsStaggered => _meter != null && _meter.IsStaggered;
        public bool IsKnockbackActive => _knockbackRemaining > 0f || _knockbackStopPending;
        public bool IsActive => IsStaggered || IsKnockbackActive;
        public int StaggersNegated { get; private set; }
        public int KnockbacksNegated { get; private set; }

        public event Action Staggered;
        public event Action<float> KnockedBack;

        public void SetConfig(StaggerConfig config) { _config = config; _meter = null; }
        public void SetStats(IPlayerStatsProvider stats) => _stats = stats;
        public void SetEvents(PlayerCombatEvents events) => _events = events;

        /// <summary>The wearer's passive event hub (null until composed); the weapons' binder hands it on.</summary>
        public PlayerCombatEvents Events => _events;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
        }

        private void Update()
        {
            _meter?.Tick(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (!IsKnockbackActive) return;
            if (_knockbackStopPending)
            {
                _knockbackStopPending = false;
                _body.linearVelocity = Vector2.zero;
                return;
            }

            var dt = Time.fixedDeltaTime;
            var step = Mathf.Min(_knockbackSpeed * dt, _knockbackRemaining);
            _body.linearVelocity = _knockbackDirection * (step / dt);
            _knockbackRemaining -= step;
            if (_knockbackRemaining <= 0.0001f)
            {
                _knockbackRemaining = 0f;
                _knockbackStopPending = true;
            }
        }

        public StaggerResult ApplyStagger(ImpactRequest request)
        {
            if (_config == null || !request.HasStagger) return StaggerResult.None;
            var hook = _events?.RaiseStaggerIncoming();
            if (hook != null && hook.IsNegated)
            {
                StaggersNegated++;
                return new StaggerResult(0f, false, true);
            }

            var result = Meter.Apply(request.StaggerPower, _stats?.GetPercent(StatId.StaggerResistance) ?? 0);
            if (result.Triggered) Staggered?.Invoke();
            return result;
        }

        public KnockbackResult ApplyKnockback(ImpactRequest request)
        {
            if (_config == null || !request.HasKnockback) return KnockbackResult.None;
            if (request.IsExplosion)
            {
                var hook = _events?.RaiseExplosionKnockbackIncoming();
                if (hook != null && hook.IsNegated)
                {
                    KnockbacksNegated++;
                    return new KnockbackResult(0f, true);
                }
            }

            var distance = KnockbackMath.Distance(request.Knockback, _stats?.GetPercent(StatId.KnockbackResistance) ?? 0, _config);
            if (distance <= 0f) return KnockbackResult.None;
            if (IsKnockbackActive && distance <= _knockbackRemaining) return new KnockbackResult(0f, false);

            _knockbackStopPending = false;
            _knockbackDirection = request.Direction;
            _knockbackRemaining = distance;
            _knockbackSpeed = distance / _config.KnockbackDurationSeconds;
            KnockedBack?.Invoke(distance);
            return new KnockbackResult(distance, false);
        }

        // ---- attacker-side feedback (wearer effects) ----

        public void OnTargetStaggered(string targetId)
        {
            _events?.RaiseEnemyStaggeredByWearer(targetId);
        }

        public WallImpactOutcome OnTargetKnockedIntoWall(string targetId, bool isBoss)
        {
            if (_events == null) return default;
            var request = _events.RaiseEnemyKnockedIntoWall(targetId, isBoss);
            return new WallImpactOutcome(request.BonusDamageMin, request.BonusDamageMax, request.ApplyHighStagger);
        }

        /// <summary>Long Shot and any other projectile-hit hook of the wearer (items/34).</summary>
        public int OnProjectileHitRolling(int damage, float travelDistance) =>
            _events != null ? _events.RaiseProjectileHitRolling(damage, travelDistance).FinalAmount : damage;

        /// <summary>The wearer's kill (Adrenaline; Flow State on a melee kill).</summary>
        public void OnTargetKilled(bool melee)
        {
            if (_events == null) return;
            if (melee) _events.RaiseMeleeKill();
            else _events.RaiseEnemyKilled();
        }
    }
}
