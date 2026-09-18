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
    /// Heat-based projectile weapon. Shares aim, damage rolling and pooled projectile emission with RangedWeapon
    /// through ProjectileEmitter; its resource is BlasterHeatState instead of magazine + ammo reserve.
    /// Consumes no ammo type and has no reload action.
    /// </summary>
    public sealed class BlasterWeapon : MonoBehaviour, IEquippableWeapon
    {
        [SerializeField] private BlasterWeaponDefinition _definition;

        public BlasterWeaponDefinition Definition => _definition;
        [SerializeField] private ProjectilePool _projectilePool;
        [SerializeField] private PlayerAiming _aiming;
        [SerializeField] private Transform _muzzle;

        private IPlayerInputReader _inputReader;
        private readonly ActionGateLookup _actionGate = new();
        private IDamageRoller _damageRoller;
        private IPlayerStatsProvider _stats;
        private IImpactAttackerFeedback _impactFeedback;
        private AimAssistConfig _aimAssist;

        /// <summary>The soft aim-assist tuning; null = raw aim only.</summary>
        public void SetAimAssist(AimAssistConfig config) => _aimAssist = config;
        public ShotSolution LastShot { get; private set; }

        /// <summary>Spawn point and direction of the next shot: pivot-anchored aim, close-range pull-back, soft assist.</summary>
        public ShotSolution ResolveShot(float range)
        {
            var rawDirection = _aiming != null ? _aiming.AimDirection : Vector2.right;
            var origin = _aiming != null ? _aiming.AimOrigin : (Vector2)transform.position;
            var muzzle = _muzzle != null ? (Vector2)_muzzle.position : origin;
            var weaponClass = _definition != null ? _definition.WeaponClass : WeaponClass.Blaster;
            return ShotSolver.Solve(origin, muzzle, rawDirection, range, weaponClass, gameObject, DamageTeam.Player, _aimAssist, _aiming);
        }
        private BlasterHeatState _heat;
        private readonly ProjectileEmitter _emitter = new();
        private readonly List<Projectile> _lastSpawnedProjectiles = new();

        private float _fireCooldownRemaining;

        public BlasterHeatState Heat => _heat ??= BuildHeatState();
        public Projectile LastSpawnedProjectile { get; private set; }
        public IReadOnlyList<Projectile> LastSpawnedProjectiles => _lastSpawnedProjectiles;
        public bool IsEquipped { get; private set; } = true;

        public void OnEquipped()
        {
            IsEquipped = true;
        }

        public void OnUnequipped()
        {
            // Heat is deliberately untouched: a holstered blaster keeps its heat and keeps cooling.
            IsEquipped = false;
        }

        public void SetDefinition(BlasterWeaponDefinition definition)
        {
            _definition = definition;
            _heat = null;
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
            var deltaTime = Time.deltaTime;
            TickFireCooldown(deltaTime);
            Heat.Tick(deltaTime);

            if (IsEquipped && _inputReader != null && _inputReader.FireHeld)
            {
                TryFire();
            }
        }

        private void TickFireCooldown(float deltaTime)
        {
            if (_fireCooldownRemaining > 0f)
            {
                _fireCooldownRemaining = Mathf.Max(0f, _fireCooldownRemaining - deltaTime);
            }
        }

        public bool TryFire()
        {
            if (!IsEquipped || _definition == null || _projectilePool == null || !_actionGate.CanAct(this))
            {
                return false;
            }

            if (_fireCooldownRemaining > 0f || !Heat.CanFire)
            {
                return false;
            }

            _fireCooldownRemaining = 1f / _definition.FireRate;
            Heat.AddShotHeat(_definition.HeatPerShot);

            var solved = ResolveShot(_definition.Range);
            LastShot = solved;

            _emitter.Emit(
                _projectilePool, SingleProjectilePattern.Instance, _damageRoller, solved.Direction, solved.SpawnPosition,
                _definition.DamageMin, _definition.DamageMax, _definition.ProjectileSpeed, _definition.Range,
                gameObject, _lastSpawnedProjectiles, _stats?.GetMultiplier(StatId.WeaponDamage) ?? 1f,
                _definition.Knockback * (_stats?.GetMultiplier(StatId.Knockback) ?? 1f),
                _definition.StaggerPower * (_stats?.GetMultiplier(StatId.StaggerPower) ?? 1f),
                _impactFeedback);
            LastSpawnedProjectile = _lastSpawnedProjectiles[_lastSpawnedProjectiles.Count - 1];

            return true;
        }

        private BlasterHeatState BuildHeatState()
        {
            return _definition == null
                ? new BlasterHeatState(100f, 0f, 0f, 0f)
                : new BlasterHeatState(_definition.MaxHeat, _definition.CoolingRatePerSecond, _definition.CoolingDelaySeconds, _definition.OverheatLockoutSeconds);
        }
    }
}
