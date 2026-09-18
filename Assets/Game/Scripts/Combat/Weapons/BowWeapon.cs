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

        public bool IsCharging { get; private set; }
        public float ChargeSeconds { get; private set; }
        public float ChargeFraction => _definition == null ? 0f : BowChargeResolver.ChargeFraction(ChargeSeconds, _definition.FullChargeSeconds);
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

            if (_inputReader == null)
            {
                return;
            }

            var held = _inputReader.FireHeld;
            if (IsEquipped)
            {
                if (held && !_fireHeldLastFrame)
                {
                    TryStartCharge();
                }
                else if (!held && _fireHeldLastFrame)
                {
                    TryRelease();
                }
            }

            _fireHeldLastFrame = held;
        }

        public bool TryStartCharge()
        {
            if (!IsEquipped || _definition == null || IsCharging || !_actionGate.CanAct(this))
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
            CancelCharge();

            var solved = ResolveShot(shot.Range);
            LastShot = solved;

            _emitter.Emit(
                _projectilePool, SingleProjectilePattern.Instance, _damageRoller, solved.Direction, solved.SpawnPosition,
                shot.DamageMin, shot.DamageMax, shot.ProjectileSpeed, shot.Range,
                gameObject, _lastSpawnedProjectiles, _stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f,
                _definition.Knockback * (_stats?.GetMultiplier(StatId.Knockback) ?? 1f),
                _definition.StaggerPower * (_stats?.GetMultiplier(StatId.StaggerPower) ?? 1f),
                _impactFeedback);
            LastSpawnedProjectile = _lastSpawnedProjectiles[_lastSpawnedProjectiles.Count - 1];

            return true;
        }

        public void CancelCharge()
        {
            IsCharging = false;
            ChargeSeconds = 0f;
        }

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
