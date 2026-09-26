using System.Collections.Generic;
using RuinRail.Core.Input;
using RuinRail.Gameplay.Combat.Impact;
using RuinRail.Gameplay.Combat.Projectiles;
using RuinRail.Gameplay.Stats;
using RuinRail.Gameplay.Items;
using RuinRail.Gameplay.Player;
using UnityEngine;

namespace RuinRail.Gameplay.Combat.Weapons
{
    /// <summary>
    /// Hold-to-draw bow: Fire pressed starts charging, Fire released fires one pooled projectile whose damage/speed/range
    /// come from the clamped charge. Unequip cancels a pending draw without firing. No ammo, magazine or reload.
    /// A release short of full draw is followed by a recovery (<see cref="BowChargeResolver.RecoverySeconds"/>) before
    /// the next draw can begin, so click-spammed quick shots cannot exceed the full-draw damage rate. A press during
    /// recovery is kept: the draw starts the moment recovery ends, and a tap released meanwhile fires then.
    /// </summary>
    public sealed class BowWeapon : MonoBehaviour, IEquippableWeapon
    {
        [SerializeField] private BowWeaponDefinition _definition;

        public BowWeaponDefinition Definition => _definition;
        [SerializeField] private ProjectilePool _projectilePool;
        [SerializeField] private PlayerAiming _aiming;
        [SerializeField] private Transform _muzzle;

        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private IDamageRoller _damageRoller;
        private IPlayerStatsProvider _stats;
        private IImpactAttackerFeedback _impactFeedback;
        private RuinRail.Gameplay.Stats.PlayerCombatEvents _combatEvents;
        private AimAssistConfig _aimAssist;

        /// <summary>The soft aim-assist tuning; null = raw aim only (applied on release).</summary>
        public void SetAimAssist(AimAssistConfig config) => _aimAssist = config;
        public ShotSolution LastShot { get; private set; }

        /// <summary>Spawn point and direction of the released shot: pivot-anchored aim, close-range pull-back, soft assist.</summary>
        public ShotSolution ResolveShot(float range)
        {
            var rawDirection = _aiming != null ? _aiming.AimDirection : Vector2.right;
            var origin = _aiming != null ? _aiming.AimOrigin : (Vector2)transform.position;
            var muzzle = _muzzle != null ? (Vector2)_muzzle.position : origin;
            return ShotSolver.Solve(origin, muzzle, rawDirection, range, WeaponClass.Bow, gameObject, DamageTeam.Player, _aimAssist, _aiming);
        }
        private readonly ProjectileEmitter _emitter = new();
        private readonly List<Projectile> _lastSpawnedProjectiles = new();
        private bool _fireHeldLastFrame;
        private bool _drawQueued;
        private bool _releaseQueued;

        /// <summary>Seconds before the next draw may begin (0 after a full draw).</summary>
        public float RecoveryRemaining { get; private set; }

        /// <summary>Accepted releases (one projectile each) since this component was created.</summary>
        public int ShotsFired { get; private set; }
        public bool IsRecovering => RecoveryRemaining > 0f;

        public bool IsCharging { get; private set; }
        public float ChargeSeconds { get; private set; }

        /// <summary>Seconds to a full draw after the capped Bow Charge Speed bonus; the authored time is the base.</summary>
        public float CurrentFullChargeSeconds => _definition == null ? 0f : WeaponStatMath.BowFullChargeSeconds(_definition.FullChargeSeconds, _stats);

        public float ChargeFraction => _definition == null ? 0f : BowChargeResolver.ChargeFraction(ChargeSeconds, CurrentFullChargeSeconds);
        public bool IsFullyCharged => ChargeFraction >= 1f;
        public Projectile LastSpawnedProjectile { get; private set; }
        public IReadOnlyList<Projectile> LastSpawnedProjectiles => _lastSpawnedProjectiles;
        public bool IsEquipped { get; private set; } = true;

        public void OnEquipped()
        {
            IsEquipped = true;
            // A Fire button already held while switching in must not count as a new draw.
            _fireHeldLastFrame = _inputReader != null && _inputReader.FireHeld;
        }

        public void OnUnequipped()
        {
            IsEquipped = false;
            CancelCharge();
            // Recovery is kept (it runs on while holstered): a swap must not clear what the last release owes.
            _drawQueued = false;
            _releaseQueued = false;
        }

        public void SetDefinition(BowWeaponDefinition definition)
        {
            _definition = definition;
            CancelCharge();
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

        /// <summary>Attacker-side impact hooks carried by every projectile this weapon fires; null = none.</summary>
        public void SetImpactFeedback(IImpactAttackerFeedback feedback)
        {
            _impactFeedback = feedback;
        }

        public void SetProjectilePool(ProjectilePool projectilePool)
        {
            _projectilePool = projectilePool;
        }

        public void SetAiming(PlayerAiming aiming)
        {
            _aiming = aiming;
        }

        public void SetMuzzle(Transform muzzle)
        {
            _muzzle = muzzle;
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

            _inputReader ??= GetComponent<PlayerInput>()?.Reader;
        }

        private void Update()
        {
            if (IsCharging)
            {
                ChargeSeconds += Time.deltaTime;
            }

            TickRecovery(Time.deltaTime);

            if (_inputReader == null)
            {
                return;
            }

            var held = _inputReader.FireHeld;
            if (IsEquipped)
            {
                if (held && !_fireHeldLastFrame)
                {
                    // A new press during recovery is the player's latest intent: a draw, even if an earlier tap was queued.
                    if (!TryStartCharge() && IsRecovering) { _drawQueued = true; _releaseQueued = false; }
                }
                else if (!held && _fireHeldLastFrame)
                {
                    if (IsCharging) TryRelease();
                    else if (_drawQueued) _releaseQueued = true;
                }

                // Recovery over: the press made during it becomes a draw, and a tap already released fires now.
                if (_drawQueued && !IsRecovering && TryStartCharge())
                {
                    _drawQueued = false;
                    if (_releaseQueued)
                    {
                        _releaseQueued = false;
                        TryRelease();
                    }
                }
            }

            _fireHeldLastFrame = held;
        }

        private void TickRecovery(float deltaTime)
        {
            if (RecoveryRemaining > 0f) RecoveryRemaining = Mathf.Max(0f, RecoveryRemaining - deltaTime);
        }

        public bool TryStartCharge()
        {
            if (!IsEquipped || _definition == null || IsCharging || IsRecovering || !_actionGate.CanAct(this))
            {
                return false;
            }

            IsCharging = true;
            ChargeSeconds = 0f;
            return true;
        }

        /// <summary>Releases the draw: fires exactly one projectile scaled by the clamped charge and resets the charge.</summary>
        public bool TryRelease()
        {
            if (!IsEquipped || !IsCharging || _definition == null || _projectilePool == null)
            {
                CancelCharge();
                return false;
            }

            var shot = BowChargeResolver.Resolve(_definition, ChargeFraction);
            var fullDraw = ChargeFraction >= 1f;
            RecoveryRemaining = BowChargeResolver.RecoverySeconds(_definition, ChargeFraction, CurrentFullChargeSeconds);
            CancelCharge();
            // Archer's Ring (a full draw pierces its first target) and the per-attack damage hooks of the wearer.
            var pierce = _combatEvents != null ? _combatEvents.RaiseBowShotFired(fullDraw).Penetrations : 0;
            var damageMultiplier = (_stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f) * (_combatEvents == null ? 1f : (100 + _combatEvents.RaiseAttackDamageRolling(shot.DamageMax, true).BonusPercent) / 100f);

            var solved = ResolveShot(WeaponStatMath.ProjectileRange(shot.Range, _stats));
            LastShot = solved;

            _emitter.Emit(
                _projectilePool, SingleProjectilePattern.Instance, _damageRoller, solved.Direction, solved.SpawnPosition,
                shot.DamageMin, shot.DamageMax,
                WeaponStatMath.ProjectileSpeed(shot.ProjectileSpeed, _stats),
                WeaponStatMath.ProjectileRange(shot.Range, _stats),
                gameObject, _lastSpawnedProjectiles, damageMultiplier,
                WeaponStatMath.Knockback(_definition.Knockback, _stats),
                WeaponStatMath.StaggerPower(_definition.StaggerPower, _stats),
                _impactFeedback,
                0f,
                DamageTeam.Player,
                ProjectileVisualCatalog.ResolveWeaponVisualId(_definition),
                pierce);
            LastSpawnedProjectile = _lastSpawnedProjectiles[_lastSpawnedProjectiles.Count - 1];
            ShotsFired++;

            return true;
        }

        public void CancelCharge()
        {
            IsCharging = false;
            ChargeSeconds = 0f;
        }

        /// <summary>Test seam: lets a fixed amount of recovery time pass.</summary>
        public void AdvanceRecovery(float seconds) => TickRecovery(Mathf.Max(0f, seconds));

        /// <summary>Test/HUD seam: advances the draw by a fixed amount of time.</summary>
        public void AdvanceCharge(float seconds)
        {
            if (IsCharging)
            {
                ChargeSeconds += Mathf.Max(0f, seconds);
            }
        }
    }
}
