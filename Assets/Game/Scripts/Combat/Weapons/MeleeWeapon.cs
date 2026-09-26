using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Stats;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    public sealed class MeleeWeapon : MonoBehaviour, IEquippableWeapon
    {
        [SerializeField] private MeleeWeaponDefinition _definition;

        public MeleeWeaponDefinition Definition => _definition;
        [SerializeField] private PlayerAiming _aiming;

        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private IDamageRoller _damageRoller;
        private IPlayerStatsProvider _stats;
        private IImpactAttackerFeedback _impactFeedback;
        private RuinRail.Gameplay.Stats.PlayerCombatEvents _combatEvents;
        private readonly HashSet<IDamageable> _hitThisSwing = new();

        private float _cooldownRemaining;
        private float _phaseTimeRemaining;

        public MeleeAttackState State { get; private set; } = MeleeAttackState.Idle;

        /// <summary>Swings per second after the capped Melee Attack Speed bonus.</summary>
        public float CurrentAttackRate => _definition == null ? 0f : WeaponStatMath.MeleeAttackRate(_definition.AttackRate, _stats);

        /// <summary>Wind-up window after the capped Melee Attack Speed bonus; the whole cadence speeds up, not only the cooldown.</summary>
        public float CurrentWindUpSeconds => _definition == null ? 0f : WeaponStatMath.MeleePhaseSeconds(_definition.WindUpSeconds, _stats);

        /// <summary>Recovery window after the capped Melee Attack Speed bonus.</summary>
        public float CurrentRecoverySeconds => _definition == null ? 0f : WeaponStatMath.MeleePhaseSeconds(_definition.RecoverySeconds, _stats);
        public int HitCountThisSwing => _hitThisSwing.Count;
        public bool IsEquipped { get; private set; } = true;

        /// <summary>Accepted swings since this component was created.</summary>
        public int AttacksStarted { get; private set; }

        public void OnEquipped()
        {
            IsEquipped = true;
        }

        public void OnUnequipped()
        {
            IsEquipped = false;
            CancelAttack();
        }

        /// <summary>
        /// Drops a swing in progress. A swing cancelled while still winding up never hit, so its cooldown is refunded;
        /// once the hit has landed the cooldown is kept — clearing it let a swap out and straight back skip the swing's
        /// recovery and the attack interval.
        /// </summary>
        private void CancelAttack()
        {
            if (State == MeleeAttackState.WindUp) _cooldownRemaining = 0f;
            State = MeleeAttackState.Idle;
            _phaseTimeRemaining = 0f;
            _hitThisSwing.Clear();
        }

        public void SetDefinition(MeleeWeaponDefinition definition)
        {
            _definition = definition;
        }

        public void SetAiming(PlayerAiming aiming)
        {
            _aiming = aiming;
        }

        /// <summary>Player stat pipeline for Knockback / Stagger Power multipliers; null = authored values.</summary>
        public void SetStats(IPlayerStatsProvider stats)
        {
            _stats = stats;
        }

        /// <summary>The wearer's passive event hub (items/34 hooks); null = no passive hooks (enemies, tests).</summary>
        public void SetCombatEvents(RuinRail.Gameplay.Stats.PlayerCombatEvents events)
        {
            _combatEvents = events;
        }

        public void SetImpactFeedback(IImpactAttackerFeedback feedback)
        {
            _impactFeedback = feedback;
        }

        public void SetDamageRoller(IDamageRoller damageRoller)
        {
            _damageRoller = damageRoller;
        }

        public void SetInputReader(IPlayerInputReader inputReader)
        {
            _inputReader = inputReader;
        }

        private void Awake()
        {
            _damageRoller ??= new UnityRandomDamageRoller();

            if (_aiming == null)
            {
                _aiming = GetComponent<PlayerAiming>();
            }

            if (_inputReader == null)
            {
                _inputReader = GetComponent<PlayerInput>()?.Reader;
            }
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;

            // The attack interval runs on while holstered, exactly as a firearm's fire cooldown does.
            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= deltaTime;
                if (_cooldownRemaining < 0f)
                {
                    _cooldownRemaining = 0f;
                }
            }

            if (!IsEquipped)
            {
                return;
            }

            TickLifecycle(deltaTime);

            if (_inputReader != null && _inputReader.FireHeld)
            {
                TryAttack();
            }
        }

        private void TickLifecycle(float deltaTime)
        {
            switch (State)
            {
                case MeleeAttackState.WindUp:
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        ResolveActiveHit();
                        State = MeleeAttackState.Recovery;
                        _phaseTimeRemaining = CurrentRecoverySeconds;
                    }

                    break;

                case MeleeAttackState.Recovery:
                    _phaseTimeRemaining -= deltaTime;
                    if (_phaseTimeRemaining <= 0f)
                    {
                        State = MeleeAttackState.Idle;
                        _phaseTimeRemaining = 0f;
                    }

                    break;
            }
        }

        public bool TryAttack()
        {
            if (!IsEquipped || !_actionGate.CanAct(this))
            {
                return false;
            }

            if (_definition == null)
            {
                return false;
            }

            if (State != MeleeAttackState.Idle || _cooldownRemaining > 0f)
            {
                return false;
            }

            _hitThisSwing.Clear();
            State = MeleeAttackState.WindUp;
            _phaseTimeRemaining = CurrentWindUpSeconds;
            AttacksStarted++;
            _cooldownRemaining = Mathf.Max(
                CurrentWindUpSeconds + CurrentRecoverySeconds,
                1f / CurrentAttackRate);

            return true;
        }

        private void ResolveActiveHit()
        {
            var origin = (Vector2)transform.position;
            var aimDirection = _aiming != null ? _aiming.AimDirection : Vector2.right;
            if (aimDirection.sqrMagnitude < 0.0001f)
            {
                aimDirection = Vector2.right;
            }
            else
            {
                aimDirection = aimDirection.normalized;
            }

            var halfArc = _definition.AttackArcDegrees * 0.5f;
            var candidates = Physics2D.OverlapCircleAll(origin, _definition.AttackRange);
            // One attack per swing (not per target): the wearer's per-attack hooks (Fresh Mag) apply to every target it hits.
            var attackMultiplier = _combatEvents == null ? 1f : (100 + _combatEvents.RaiseAttackDamageRolling(_definition.DamageMax, false).BonusPercent) / 100f;

            foreach (var candidate in candidates)
            {
                var rootGameObject = candidate.transform.root.gameObject;
                if (rootGameObject == gameObject)
                {
                    continue;
                }

                var damageable = candidate.GetComponentInParent<IDamageable>();
                if (damageable == null || _hitThisSwing.Contains(damageable))
                {
                    continue;
                }

                // Friendly fire is OFF: player melee never lands on players.
                if (TeamMember.TeamOf(candidate) == DamageTeam.Player)
                {
                    continue;
                }

                var toTarget = (Vector2)candidate.transform.position - origin;
                var distance = toTarget.magnitude;
                if (distance > _definition.AttackRange || distance < 0.0001f)
                {
                    continue;
                }

                var angle = Vector2.Angle(aimDirection, toTarget);
                if (angle > halfArc)
                {
                    continue;
                }

                _hitThisSwing.Add(damageable);
                // Integer roll first, then the (already capped) weapon-damage multiplier, rounded back to an integer.
                var damage = Mathf.Max(0, Mathf.RoundToInt(_damageRoller.Roll(_definition.DamageMin, _definition.DamageMax) * (_stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f) * attackMultiplier));
                if (damageable.TryApplyDamage(new DamageRequest(damage)))
                {
                    // A melee kill (Combat Bracelet / Flow State, and any kill for Adrenaline); a co-op replica never dies locally.
                    if (candidate.GetComponentInParent<HealthComponent>() is { IsAlive: false }) _impactFeedback?.OnTargetKilled(true);
                    var knockback = WeaponStatMath.Knockback(_definition.Knockback, _stats);
                    var stagger = WeaponStatMath.StaggerPower(_definition.StaggerPower, _stats);
                    ImpactDispatcher.Apply(candidate, new ImpactRequest(toTarget, knockback, stagger, DamageKind.Normal, gameObject, _impactFeedback));
                }
            }
        }
    }
}
